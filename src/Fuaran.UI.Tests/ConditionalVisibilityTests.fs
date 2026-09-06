module Fuaran.UI.Tests.ConditionalVisibility

// ============================================================================
//  Conditional visibility and predicate branching (Fuaran-UI Phase 1535,
//  WIRE_FORMAT.md §3.1 "Conditional presence" + §3.6 "Selecting a case").
//
//  Four claims, kept apart because they fail for different reasons and a
//  reader who sees one go red should not have to work out which:
//
//  1. THE WIRE-NEUTRAL FIX. `Switch.on` and `Accessibility.hidden` resolve
//     through the SCALAR path, so a `Binding.Transform` yielding one cell —
//     the one wire spelling of "count > 3 ⇒ 'busy'" — reaches them. Each of
//     these tests carries its own go-red half: the same fixture through the
//     GENERIC `tryResolve` is asserted NOT to produce the value, which is what
//     makes the pair evidence rather than a green tick. Without that half a
//     test asserting only the new behaviour would pass identically if the
//     coercion silently stopped mattering.
//
//  2. THE VISIBILITY RULE. A resolved `false` removes; absent, `NotResolved`
//     and `Errored` all render. The asymmetry is the design, not a leniency,
//     so it is asserted per outcome rather than as "renders unless false".
//
//  3. CASE SELECTION. First-match-wins over an ORDER that mixes `match` and
//     `when`, with a predicate taken only on a resolved `true`. The mixed
//     ordering test is the one that catches a host evaluating all the
//     predicates before any of the matches.
//
//  4. THE VALIDATOR. FUARAN142's two shapes, and the two rules the phase
//     changed elsewhere: FUARAN103 stands down on a when-only switch (a
//     selector no case consults is not an unwritable selector), and FUARAN069
//     ignores `visible` (deciding whether a control appears is not an
//     affordance that makes it live).
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// `box` under F# 10's nullness rules — `BindingSources.State` is a
/// `Map<string, obj>` of non-nullable values.
let private nn (v: 'a) : obj = box v |> Unchecked.nonNull

let private sources (state: (string * obj) list) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList state }

/// The validator returns typed defects; this projects them to the FUARAN codes
/// the specification and the phase text talk in, through the library's own
/// `describe` — so a test naming a code cannot drift from what a reader sees.
let private codesOf (n: Node<obj>) : string list =
    match PreEmitValidate.validate n with
    | Ok() -> []
    | Error defects ->
        defects
        |> List.map (fun d -> let (code, _, _) = PreEmitValidate.describe d in code)

let private leaf (id: string) : Node<obj> =
    { Id = id
      Kind = NodeKind.Markdown({ Text = TextSource.Literal id })
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

/// A four-row embedded table, aggregated to ONE cell carrying a branch label.
/// The canonical scalar terminal of §3.6: `groupBy [] [count]`, a `derive`
/// folding the count into a label, a `project` to that column.
let private countLabelTransform (threshold: int) : Binding<'T> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "a"
                        Fuaran.Core.Str "b"
                        Fuaran.Core.Str "c"
                        Fuaran.Core.Str "d" ] ] }

    Binding.Transform(
        TransformSource.Data source,
        [ Fuaran.Core.GroupBy(
              [],
              [ { Name = "n"
                  Fn = Fuaran.Core.AggFn.Count
                  Of = "id" } ]
          )
          Fuaran.Core.Derive(
              "label",
              Fuaran.Core.Case(
                  [ Fuaran.Core.Binary(Fuaran.Core.Gt, Fuaran.Core.Col "n", Fuaran.Core.Lit(Fuaran.Core.Int threshold)),
                    Fuaran.Core.Lit(Fuaran.Core.Str "busy") ],
                  Fuaran.Core.Lit(Fuaran.Core.Str "quiet")
              )
          )
          Fuaran.Core.Project [ "label", "label" ] ],
        None
    )

/// The label pipeline TYPED at `Binding<bool>`: a string cell arriving at a
/// boolean slot. `cellToBool` is strict, so this is the ERRORED case — the
/// resolver's third outcome, kept distinct from `NotResolved` because the two
/// mean different things and a host may want to warn on one.
let private labelInBoolSlot: Binding<bool> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType ]
              Columns = [ Fuaran.Core.Column.create "id" Fuaran.Core.StringType [ Fuaran.Core.Str "a" ] ] }

    Binding.Transform(
        TransformSource.Data source,
        [ Fuaran.Core.Derive("label", Fuaran.Core.Lit(Fuaran.Core.Str "busy"))
          Fuaran.Core.Project [ "label", "label" ] ],
        None
    )

/// A query result the host never furnishes — the resolver's `NotResolved`.
/// `Binding.State` cannot express this: its own rule resolves a default-less
/// unwritten key to the slot default, which at `bool` is `false`. That is
/// FUARAN143's subject, asserted at the end of this file.
let private unfurnishedQuery: Binding<bool> =
    Binding.Query("flags.betaEnabled", (fun (o: obj) -> unbox<bool> o), None)

/// The same aggregate reduced to a BOOL cell rather than a label — what a
/// `hidden` or a `visible` predicate actually carries.
let private countExceedsTransform (threshold: int) : Binding<bool> =
    let source =
        Fuaran.Core.Embedded
            { Schema = [ "id", Fuaran.Core.StringType ]
              Columns =
                [ Fuaran.Core.Column.create
                      "id"
                      Fuaran.Core.StringType
                      [ Fuaran.Core.Str "a"
                        Fuaran.Core.Str "b"
                        Fuaran.Core.Str "c"
                        Fuaran.Core.Str "d" ] ] }

    Binding.Transform(
        TransformSource.Data source,
        [ Fuaran.Core.GroupBy(
              [],
              [ { Name = "n"
                  Fn = Fuaran.Core.AggFn.Count
                  Of = "id" } ]
          )
          Fuaran.Core.Derive(
              "over",
              Fuaran.Core.Binary(Fuaran.Core.Gt, Fuaran.Core.Col "n", Fuaran.Core.Lit(Fuaran.Core.Int threshold))
          )
          Fuaran.Core.Project [ "over", "over" ] ],
        None
    )

[<Tests>]
let tests =
    testList
        "Phase 1535 — conditional visibility, predicate cases, the scalar selector"
        [
          // ── 1. The wire-neutral fix ────────────────────────────────────────

          test "a computed Switch selector resolves through the SCALAR path" {
              let selector: Binding<string> = countLabelTransform 3

              Expect.equal
                  (BindingResolver.tryResolveScalarText BindingResolver.empty selector)
                  (Some "busy")
                  "a Transform yielding one cell is the selector's value"
          }

          test "…and the GENERIC resolver does not produce it — the go-red half" {
              // This is the defect Phase 1535 fixed, asserted as the thing that
              // is still true of the resolver the renderers no longer call. The
              // generic arm evaluates the pipeline to a `Row seq` and `unbox`es
              // it at `string`: on .NET that throws and is caught as `Errored`,
              // under Fable it "succeeds" and yields the rows array. Neither is
              // the string "busy", and neither ever could be — which is why the
              // switch fell through to `Default` with nothing saying why.
              let selector: Binding<string> = countLabelTransform 3

              Expect.notEqual
                  (BindingResolver.tryResolve BindingResolver.empty selector)
                  (Some "busy")
                  "the row-shaped arm cannot yield a scalar; if this ever passes, the two paths have merged and this test's premise is gone"
          }

          test "Accessibility.hidden takes a Transform yielding a 1x1 bool" {
              let a11y: Accessibility option =
                  Some
                      { Defaults.Accessibility.empty with
                          Hidden = Some(countExceedsTransform 3) }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.equal attrs [ "aria-hidden", "true" ] "the computed predicate hides"
          }

          test "…and a computed FALSE emits no aria-hidden at all" {
              // Not "aria-hidden=false" — absence. An `aria-hidden="false"` is a
              // different statement from no attribute, and the projection has
              // always emitted the attribute only for a true.
              let a11y: Accessibility option =
                  Some
                      { Defaults.Accessibility.empty with
                          Hidden = Some(countExceedsTransform 10) }

              Expect.equal (Render.accessibilityAttributes BindingResolver.empty a11y) [] "a false hides nothing"
          }

          test "a NON-BOOL cell in hidden is refused, not read through a truthiness rule" {
              // `cellToBool` is strict by design (Phase 1534): a text cell in a
              // boolean slot errors rather than reading as `true`. The
              // projection drops an unresolved predicate, so the observable
              // consequence is no attribute — which is the safe direction, and
              // the reason the strictness is safe to have.
              let a11y: Accessibility option =
                  Some
                      { Defaults.Accessibility.empty with
                          Hidden = Some(labelInBoolSlot) }

              Expect.equal
                  (Render.accessibilityAttributes BindingResolver.empty a11y)
                  []
                  "a text cell is not a boolean; nothing is emitted"
          }

          // ── 2. The visibility rule ─────────────────────────────────────────

          test "a node with no `visible` renders" {
              Expect.isTrue (BindingResolver.isNodeVisible BindingResolver.empty (leaf "plain")) "absence is presence"
          }

          test "a resolved FALSE removes the node" {
              let n =
                  { leaf "banner" with
                      Visible = Some(Binding.State("shown", None)) }

              Expect.isFalse (BindingResolver.isNodeVisible (sources [ "shown", nn false ]) n) "false removes"
          }

          test "a resolved TRUE renders the node" {
              let n =
                  { leaf "banner" with
                      Visible = Some(Binding.State("shown", None)) }

              Expect.isTrue (BindingResolver.isNodeVisible (sources [ "shown", nn true ]) n) "true renders"
          }

          test "an UNRESOLVED predicate renders the node — a missing source must not hide content" {
              // A QUERY the host has not furnished. `Binding.State` cannot
              // express this case at all — see the last test in this file.
              let n =
                  { leaf "banner" with
                      Visible = Some unfurnishedQuery }

              Expect.equal
                  (BindingResolver.nodeVisibility BindingResolver.empty n)
                  (Some BindingResolver.NotResolved)
                  "the predicate genuinely does not resolve — the premise of the next assertion"

              Expect.isTrue (BindingResolver.isNodeVisible BindingResolver.empty n) "and the node renders anyway"
          }

          test "an ERRORED predicate renders the node, and the error is still reportable" {
              // A text cell in a boolean slot: `cellToBool` refuses it, so the
              // resolution is `Errored` rather than `NotResolved`. Both render —
              // and the two are kept DISTINGUISHABLE through `nodeVisibility`
              // precisely so a host can warn on one and not the other.
              let n =
                  { leaf "banner" with
                      Visible = Some(labelInBoolSlot) }

              match BindingResolver.nodeVisibility BindingResolver.empty n with
              | Some(BindingResolver.Errored _) -> ()
              | other -> failtestf "expected Errored, got %A" other

              Expect.isTrue (BindingResolver.isNodeVisible BindingResolver.empty n) "and the node renders anyway"
          }

          test "a computed `visible` predicate resolves through the scalar path" {
              let n =
                  { leaf "badge" with
                      Visible = Some(countExceedsTransform 3) }

              Expect.isTrue (BindingResolver.isNodeVisible BindingResolver.empty n) "count 4 > 3"

              let hidden =
                  { leaf "badge" with
                      Visible = Some(countExceedsTransform 10) }

              Expect.isFalse (BindingResolver.isNodeVisible BindingResolver.empty hidden) "count 4 is not > 10"
          }

          // ── 3. Case selection ──────────────────────────────────────────────

          test "a `match` case is selected by the resolved selector" {
              let cases: SwitchCase<obj> list =
                  [ { Match = Some "a"
                      When = None
                      Child = leaf "child-a" }
                    { Match = Some "b"
                      When = None
                      Child = leaf "child-b" } ]

              Expect.equal
                  (BindingResolver.selectSwitchCase BindingResolver.empty (Some "b") cases
                   |> Option.map _.Id)
                  (Some "child-b")
                  "the second case"
          }

          test "a `when` case is taken on a resolved TRUE and consults no selector" {
              let cases: SwitchCase<obj> list =
                  [ { Match = None
                      When = Some(Binding.State("flag", None))
                      Child = leaf "predicate-child" } ]

              Expect.equal
                  (BindingResolver.selectSwitchCase (sources [ "flag", nn true ]) None cases
                   |> Option.map _.Id)
                  (Some "predicate-child")
                  "no selector was resolved, and the case is still selected"
          }

          test "a `when` case falls through on false, unresolved and errored alike" {
              let cases (b: Binding<bool>) : SwitchCase<obj> list =
                  [ { Match = None
                      When = Some b
                      Child = leaf "predicate-child" } ]

              Expect.isNone
                  (BindingResolver.selectSwitchCase
                      (sources [ "flag", nn false ])
                      None
                      (cases (Binding.State("flag", None))))
                  "false falls through"

              Expect.isNone
                  (BindingResolver.selectSwitchCase BindingResolver.empty None (cases (Binding.State("never", None))))
                  "unresolved falls through"

              Expect.isNone
                  (BindingResolver.selectSwitchCase BindingResolver.empty None (cases (labelInBoolSlot)))
                  "errored falls through"
          }

          test "first-match-wins runs over the AUTHORED order, mixing match and when" {
              // The one test a host that batches all its matches ahead of all
              // its predicates (or the reverse) fails. The `when` sits FIRST and
              // is true, and the `match` that would also have selected sits
              // second — so an implementation that checked matches first would
              // return `by-match` here and be wrong in a way no pure-predicate
              // fixture could reveal.
              let cases: SwitchCase<obj> list =
                  [ { Match = None
                      When = Some(Binding.State("flag", None))
                      Child = leaf "by-predicate" }
                    { Match = Some "a"
                      When = None
                      Child = leaf "by-match" } ]

              Expect.equal
                  (BindingResolver.selectSwitchCase (sources [ "flag", nn true ]) (Some "a") cases
                   |> Option.map _.Id)
                  (Some "by-predicate")
                  "authored order decides"

              Expect.equal
                  (BindingResolver.selectSwitchCase (sources [ "flag", nn false ]) (Some "a") cases
                   |> Option.map _.Id)
                  (Some "by-match")
                  "…and the match still wins once the predicate declines"
          }

          test "a case carrying NEITHER is never selected" {
              // Unreachable from the wire (the decoder refuses it) and reported
              // pre-emit as FUARAN142, but a tree built in-process can hold one
              // and the renderer is total by signature.
              let cases: SwitchCase<obj> list =
                  [ { Match = None
                      When = None
                      Child = leaf "unreachable" } ]

              Expect.isNone
                  (BindingResolver.selectSwitchCase BindingResolver.empty (Some "anything") cases)
                  "no condition, no selection"
          }

          // ── 4. The validator ───────────────────────────────────────────────

          test "FUARAN142 reports a case carrying both match and when" {
              let n =
                  { leaf "sw" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("view", None)
                                  Cases =
                                      [ { Match = Some "a"
                                          When = Some(Binding.State("flag", None))
                                          Child = leaf "c" } ]
                                  Default = leaf "d" } }

              let codes = codesOf n

              Expect.contains codes "FUARAN142" "both is a shape defect"
          }

          test "FUARAN142 reports a case carrying neither" {
              let n =
                  { leaf "sw" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("view", None)
                                  Cases =
                                      [ { Match = None
                                          When = None
                                          Child = leaf "c" } ]
                                  Default = leaf "d" } }

              let codes = codesOf n

              Expect.contains codes "FUARAN142" "neither is a shape defect"
          }

          test "FUARAN142 is silent on a well-formed mixed switch" {
              let n =
                  { leaf "sw" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("view", None)
                                  Cases =
                                      [ { Match = Some "a"
                                          When = None
                                          Child = leaf "c1" }
                                        { Match = None
                                          When = Some(Binding.State("flag", None))
                                          Child = leaf "c2" } ]
                                  Default = leaf "d" } }

              let codes = codesOf n

              Expect.isFalse (List.contains "FUARAN142" codes) "one of the two per case is the whole rule"
          }

          test "FUARAN082 does not fold two PREDICATE cases into a duplicate" {
              // Two `when` cases carry no match string, so there is nothing to
              // duplicate. Folding them under a synthetic key would report a
              // dead case that is not dead.
              let n =
                  { leaf "sw" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("view", None)
                                  Cases =
                                      [ { Match = None
                                          When = Some(Binding.State("a", None))
                                          Child = leaf "c1" }
                                        { Match = None
                                          When = Some(Binding.State("b", None))
                                          Child = leaf "c2" } ]
                                  Default = leaf "d" } }

              let codes = codesOf n

              Expect.isFalse (List.contains "FUARAN082" codes) "two predicates are not duplicates"
          }

          test "FUARAN103 stands down on a switch whose cases are ALL predicates" {
              // `on` names a key nothing writes — which is FUARAN103's exact
              // shape — but no case consults it, so the rule's conclusion ("one
              // branch renders forever") is false here. The guard is what makes
              // a when-only switch authorable without a spurious warning.
              let n =
                  { leaf "sw" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("nobody.writes.this", None)
                                  Cases =
                                      [ { Match = None
                                          When = Some(Binding.State("flag", None))
                                          Child = leaf "c" } ]
                                  Default = leaf "d" } }

              let codes = codesOf n

              Expect.isFalse
                  (List.contains "FUARAN103" codes)
                  "a selector no case consults is not an unwritable selector"
          }

          test "…and FUARAN103 still fires on a MIXED switch, which does consult it" {
              let n =
                  { leaf "sw" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("nobody.writes.this", None)
                                  Cases =
                                      [ { Match = None
                                          When = Some(Binding.State("flag", None))
                                          Child = leaf "c1" }
                                        { Match = Some "a"
                                          When = None
                                          Child = leaf "c2" } ]
                                  Default = leaf "d" } }

              let codes = codesOf n

              Expect.contains codes "FUARAN103" "the match case reads the selector, so the rule applies"
          }

          test "FUARAN069 ignores `visible` — a hidden-when-false control is still inert" {
              // The inert-control rule asks whether a control can carry a
              // gesture. `visible` decides whether the control APPEARS, which is
              // a different question, so attaching one must neither create nor
              // silence the finding. Asserted both ways round on one node.
              let inertDisclosure (visible: Binding<bool> option) =
                  { leaf "disc" with
                      Kind =
                          NodeKind.Disclosure
                              { Defaults.disclosure with
                                  OnToggle = None
                                  Open = Binding.Static(Some false) }
                      Visible = visible }

              let without = codesOf (inertDisclosure None)
              let withVisible = codesOf (inertDisclosure (Some(Binding.State("shown", None))))

              Expect.contains without "FUARAN069" "the control is inert to begin with — the premise"
              Expect.contains withVisible "FUARAN069" "and `visible` neither rescues it nor is mistaken for a writer"
          }

          test "FUARAN143 reports a visibility predicate nothing in the tree can make true" {
              // The SILENT HIDE, and the reason this code exists at all.
              // `visible` is an ordinary `Binding<bool>`, so it follows the
              // shared `Binding.State` rule: a default-less key nothing has
              // written resolves to the slot default, which at `bool` is FALSE.
              // The node is therefore removed — NOT unresolved-and-rendered — and
              // the slot's own "an unresolved predicate renders" rule cannot see
              // it, because nothing failed to resolve. Asserted as the resolver
              // outcome first, so the test states the hazard rather than only the
              // report.
              let n =
                  { leaf "banner" with
                      Visible = Some(Binding.State("nobody.writes.this", None)) }

              Expect.equal
                  (BindingResolver.nodeVisibility BindingResolver.empty n)
                  (Some(BindingResolver.Resolved false))
                  "the hazard: it resolves FALSE rather than failing to resolve"

              Expect.contains (codesOf n) "FUARAN143" "and the author is told"
          }

          test "…and FUARAN143 is silent once the default is declared" {
              // One character of authoring is the whole remedy, in both
              // directions: `Some true` is "visible unless something says
              // otherwise", `Some false` a deliberate start-hidden. Neither is a
              // defect, so neither is reported.
              for declared in [ true; false ] do
                  let n =
                      { leaf "banner" with
                          Visible = Some(Binding.State("nobody.writes.this", Some declared)) }

                  Expect.isFalse
                      (List.contains "FUARAN143" (codesOf n))
                      (sprintf "a declared default of %b is an authored decision, not a defect" declared)
          }

          test "…and FUARAN143 is silent when something DOES write the key" {
              let banner =
                  { leaf "banner" with
                      Visible = Some(Binding.State("banner.shown", None)) }

              let writer: Node<obj> =
                  { leaf "toggle" with
                      Kind =
                          NodeKind.Button
                              { Defaults.button with
                                  Label = TextSource.Literal "Show"
                                  OnClick = Action.SetState("banner.shown", Some(Fuaran.Core.JBool true), None) } }

              let tree =
                  { leaf "root" with
                      Kind =
                          NodeKind.Box
                              { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                                Role = BoxRole.Group
                                Heading = None
                                Children = [ banner; writer ]
                                KeepTogether = false
                                BreakBefore = false } }

              Expect.isFalse (List.contains "FUARAN143" (codesOf tree)) "the key has a writer"
          } ]
