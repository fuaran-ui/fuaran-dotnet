module Fuaran.UI.Renderer.Server.Tests.SharedRendererModulesTests

// ============================================================================
//  Phase 2070 — the pure renderer modules, one place each.
//
//  `FieldRules`, `GridModel` and `TreeModel` in `Fuaran.UI.Renderer.Core` are
//  the single definitions both renderers (and, for the field rules, the
//  server-driven tier's re-check) call. These tests pin what each one decides,
//  over the shared module itself, so a renderer can no longer carry a private
//  copy that agrees today and drifts tomorrow. The headline is the row edits'
//  ONE out-of-range rule: a removal, an insertion and a move refuse an index
//  outside the range they address and return the SAME list instance.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg = | Noop

let private rule: FieldRule =
    { Compare = None
      Format = None
      MaxLength = None
      Message = None
      MinLength = None
      Pattern = None }

let private field (r: FieldRule) : FormField<Msg> =
    { Defaults.formField<Msg> with
        Id = "value"
        Label = TextSource.Literal "Value"
        Rule = Some r }

/// Evaluate `r` over a harvest, with no pattern predicate and no authored
/// message resolution, unless a test says otherwise.
let private unmetOver (values: (string * FieldRules.RuleValue) list) (r: FieldRule) : string option =
    let m = Map.ofList values
    FieldRules.unmet None (fun _ -> None) (fun id -> Map.tryFind id m) (field r)

let private text s = FieldRules.RuleValue.Text s
let private num n = FieldRules.RuleValue.Number n

[<Tests>]
let fieldRulesTests =
    testList
        "Phase 2070 - FieldRules: the one definition of a declared field rule"
        [ test "format: email is the structural shape <input type=email> demands" {
              Expect.isTrue (FieldRules.matchesFormat TextFormat.Email "a@b") "one @, both sides non-empty"
              Expect.isFalse (FieldRules.matchesFormat TextFormat.Email "a@b@c") "two @"
              Expect.isFalse (FieldRules.matchesFormat TextFormat.Email "@b") "empty local part"
              Expect.isFalse (FieldRules.matchesFormat TextFormat.Email "a b@c") "whitespace"
          }

          test "format: url needs a scheme and a body; tel claims nothing" {
              Expect.isTrue (FieldRules.matchesFormat TextFormat.Url "https://x") "absolute"
              Expect.isFalse (FieldRules.matchesFormat TextFormat.Url "://x") "no scheme"
              Expect.isFalse (FieldRules.matchesFormat TextFormat.Url "x") "no scheme separator"
              Expect.isTrue (FieldRules.matchesFormat TextFormat.Tel "anything at all") "tel is never refused"
          }

          test "compare: same-shape values order; different shapes, booleans under ordering and NaN are unmet" {
              Expect.isTrue (FieldRules.meets CompareOp.Lt (text "2026-01-01") (text "2026-02-01")) "ISO dates, ordinal"
              Expect.isTrue (FieldRules.meets CompareOp.Gte (num 3.0) (num 3.0)) "numbers"
              Expect.isFalse (FieldRules.meets CompareOp.Eq (text "3") (num 3.0)) "a string is not a number"

              Expect.isTrue
                  (FieldRules.meets CompareOp.Eq (FieldRules.RuleValue.Flag true) (FieldRules.RuleValue.Flag true))
                  "bool equality"

              Expect.isFalse
                  (FieldRules.meets CompareOp.Lt (FieldRules.RuleValue.Flag false) (FieldRules.RuleValue.Flag true))
                  "no bool order"

              Expect.isFalse (FieldRules.meets CompareOp.Eq (num nan) (num nan)) "NaN is not ordered"

              Expect.isFalse
                  (FieldRules.meets CompareOp.Neq FieldRules.RuleValue.Other (text "x"))
                  "other is never comparable"
          }

          test "the first unmet slot wins, in the order minLength, maxLength, format, pattern, compare" {
              let r =
                  { rule with
                      MinLength = Some 5
                      Format = Some TextFormat.Email }

              Expect.equal (unmetOver [ "value", text "a@b" ] r) (Some "Must be at least 5 characters.") "length first"
              Expect.equal (unmetOver [ "value", text "abcdef" ] r) (Some "Enter an email address.") "then format"
              Expect.equal (unmetOver [ "value", text "abc@def" ] r) None "met"
          }

          test "an empty or non-string value is not length-, format- or pattern-checked" {
              let r =
                  { rule with
                      MinLength = Some 3
                      Format = Some TextFormat.Url
                      Pattern = Some "x" }

              let refuseAll = Some(fun (_: string) (_: string) -> false)

              Expect.isNone (unmetOver [ "value", text "" ] r) "empty"
              Expect.isNone (unmetOver [ "value", num 1.0 ] r) "number"

              Expect.isNone
                  (FieldRules.unmet refuseAll (fun _ -> None) (fun _ -> Some(text "")) (field r))
                  "the pattern hook is not consulted for an empty value"
          }

          test "pattern is checked only where the tier passes a predicate" {
              let r = { rule with Pattern = Some "[0-9]+" }

              let digitsOnly =
                  Some(fun (_: string) (s: string) -> s |> Seq.forall System.Char.IsDigit)

              let harvest = fun _ -> Some(text "12a")

              Expect.isNone (FieldRules.unmet None (fun _ -> None) harvest (field r)) "client: left to the browser"

              Expect.equal
                  (FieldRules.unmet digitsOnly (fun _ -> None) harvest (field r))
                  (Some "This value is not in the required format.")
                  "server: the regex floor"
          }

          test "compare reads its sibling by State key; a non-State operand is not evaluated" {
              let confirm =
                  { rule with
                      Compare =
                          Some
                              { Against = Binding.State("other", None)
                                Op = CompareOp.Eq } }

              Expect.isNone (unmetOver [ "value", text "pw"; "other", text "pw" ] confirm) "equal"

              Expect.equal
                  (unmetOver [ "value", text "pw"; "other", text "px" ] confirm)
                  (Some "This value does not match the field it is compared against.")
                  "unequal"

              Expect.isSome (unmetOver [ "value", text "pw" ] confirm) "a missing sibling is unmet"

              let staticOperand =
                  { rule with
                      Compare =
                          Some
                              { Against = Binding.Static(Some(JStr "x"))
                                Op = CompareOp.Eq } }

              Expect.isNone (unmetOver [ "value", text "pw" ] staticOperand) "reported, not refused"
          }

          test "the message policy replaces the generated sentence only where it resolves" {
              let r =
                  { rule with
                      MinLength = Some 9
                      Message = Some(TextSource.Literal "Too short") }

              let harvest = fun _ -> Some(text "abc")

              let literalOnly =
                  function
                  | TextSource.Literal t -> Some t
                  | _ -> None

              Expect.equal (FieldRules.unmet None literalOnly harvest (field r)) (Some "Too short") "resolved"

              Expect.equal
                  (FieldRules.unmet None (fun _ -> None) harvest (field r))
                  (Some "Must be at least 9 characters.")
                  "unresolved: the generated sentence stands"
          }

          test "the projections both renderers emit: control type and compare declaration" {
              Expect.equal (FieldRules.textInputType None) "text" "no rule"

              Expect.equal
                  (FieldRules.textInputType (
                      Some
                          { rule with
                              Format = Some TextFormat.Tel }
                  ))
                  "tel"
                  "tel"

              Expect.equal
                  (FieldRules.compareDeclaration
                      { Against = Binding.State("start", None)
                        Op = CompareOp.Gte })
                  "gte:start"
                  "op and key"

              Expect.equal
                  (FieldRules.compareDeclaration
                      { Against = Binding.Static None
                        Op = CompareOp.Lt })
                  "lt:"
                  "a non-State operand writes no key"
          } ]

[<Tests>]
let rowEditTests =
    testList
        "Phase 2070 - GridModel row edits: one out-of-range rule"
        [ test "a removal addresses an item; outside 0..length-1 it returns the SAME instance" {
              let xs = [ "a"; "b"; "c" ]
              Expect.equal (GridModel.removeAt 1 xs) [ "a"; "c" ] "in range"

              for i in [ -1; 3; 99 ] do
                  Expect.isTrue (obj.ReferenceEquals(GridModel.removeAt i xs, xs)) (sprintf "removeAt %d refused" i)
          }

          test "an insertion addresses a gap; the length appends; outside 0..length it returns the SAME instance" {
              let xs = [ "a"; "b" ]
              Expect.equal (GridModel.insertAt 0 "x" xs) [ "x"; "a"; "b" ] "head"
              Expect.equal (GridModel.insertAt 2 "x" xs) [ "a"; "b"; "x" ] "append"

              for i in [ -1; 3 ] do
                  Expect.isTrue (obj.ReferenceEquals(GridModel.insertAt i "x" xs, xs)) (sprintf "insertAt %d refused" i)
          }

          test "a move IS a removal then an insertion into what remains, for every in-range pair" {
              let xs = [ "a"; "b"; "c"; "d" ]

              for f in 0..3 do
                  for t in 0..3 do
                      let expected =
                          if f = t then
                              xs
                          else
                              GridModel.insertAt t (List.item f xs) (GridModel.removeAt f xs)

                      Expect.equal (GridModel.moveRow f t xs) expected (sprintf "move %d->%d" f t)
          }

          test "a move with either index out of range, or no move, returns the SAME instance" {
              let xs = [ "a"; "b"; "c" ]

              for f, t in [ -1, 1; 3, 1; 1, -1; 1, 3; 1, 1 ] do
                  Expect.isTrue (obj.ReferenceEquals(GridModel.moveRow f t xs, xs)) (sprintf "move %d->%d refused" f t)
          }

          test "the BindingResolver re-export is the same function" {
              let xs = [ 1; 2; 3; 4 ]

              for f in -1 .. 4 do
                  for t in -1 .. 4 do
                      Expect.equal (BindingResolver.moveRow f t xs) (GridModel.moveRow f t xs) (sprintf "%d->%d" f t)
          } ]

[<Tests>]
let modelDecoderTests =
    testList
        "Phase 2070 - GridModel / TreeModel decoders and the State reads over them"
        [ test "sort, page and window descriptors validate rather than trust" {
              Expect.equal
                  (GridModel.sortDescriptorOfJVal (JObj [ "column", JInt 2; "direction", JStr "desc" ]))
                  (Some(2, Fuaran.UI.Types.SortDirection.Desc))
                  "usable"

              Expect.isNone
                  (GridModel.sortDescriptorOfJVal (JObj [ "column", JInt -1; "direction", JStr "asc" ]))
                  "negative column"

              Expect.equal (GridModel.pageOfJVal (JObj [ "page", JFloat 3.0 ])) (Some 3) "integral float"
              Expect.isNone (GridModel.pageOfJVal (JObj [ "page", JInt 0 ])) "page is 1-based"

              Expect.equal
                  (GridModel.windowOfJVal (JObj [ "offset", JInt 0; "count", JInt 5 ]))
                  (Some { Offset = 0; Count = 5 })
                  "usable window"
          }

          test "tree expansion and selection decode their fixed shapes" {
              Expect.equal
                  (TreeModel.expandedOfJVal (JArr [ JStr "a"; JInt 3; JStr "b" ]))
                  (Set.ofList [ "a"; "b" ])
                  "non-string members dropped individually"

              Expect.equal (TreeModel.expandedOfJVal (JObj [])) Set.empty "not an array"
              Expect.equal (TreeModel.selectedOfJVal (JStr "r1")) (Some "r1") "a row id"
              Expect.isNone (TreeModel.selectedOfJVal (JStr "")) "empty is no selection"
          }

          test "the State reads go through the decoders" {
              let sources =
                  { BindingResolver.empty with
                      State =
                          Map.ofList
                              [ "sort", box (JObj [ "column", JInt 1; "direction", JStr "asc" ])
                                "page", box (JObj [ "page", JInt 4 ])
                                "open", box (JArr [ JStr "x" ])
                                "sel", box (JStr "x") ] }

              Expect.equal
                  (BindingResolver.readSortDescriptor sources "sort")
                  (Some(1, Fuaran.UI.Types.SortDirection.Asc))
                  "sort"

              Expect.equal (BindingResolver.readPageDescriptor sources "page") 4 "page"
              Expect.equal (BindingResolver.readPageDescriptor sources "absent") 1 "absent page is 1"
              Expect.equal (BindingResolver.readExpandedItems sources (Some "open")) (Set.ofList [ "x" ]) "expanded"
              Expect.equal (BindingResolver.readExpandedItems sources None) Set.empty "no key named"
              Expect.equal (BindingResolver.readSelectedItem sources (Some "sel")) (Some "x") "selected"
          } ]
