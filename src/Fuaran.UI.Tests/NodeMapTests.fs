module Fuaran.UI.Tests.NodeMap

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

#nowarn "3261" // Nullness — `box`-ing a test payload for the obj-typed Call /
// OnBubble seams is the same sanctioned type-erasure boundary the sibling
// scope + guest-boundary tests use.

// ============================================================================
//  Node.mapMsg — structural relabel of a tree's message type (Phase 268).
//
//  These tests pin the combinator STRUCTURALLY: build a `Node<int>`, map it to
//  `Node<string>`, and inspect the remapped `Action` slots. `Action<'Msg>`
//  carries function payloads (`Call`, `ReadFileBody`, `OnBubble`, every
//  `onChange`) so it is NOT an F# equality type — every assertion pattern-
//  matches to the leaf and compares that, never `=` on an Action.
//
//  No renderer is involved (a full .NET/Feliz render throws in
//  `renderNodeFallback`); mapMsg is a pure tree→tree function.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

/// A button whose `OnClick` is the supplied `Action<int>`.
let private buttonWith (id: string) (onClick: Action<int>) : Node<int> =
    Fuaran.button
        id
        { Defaults.button with
            OnClick = onClick }

[<Tests>]
let nodeMapTests =
    testList
        "Phase268.NodeMap"
        [ test "a leaf Dispatch payload is lifted through f" {
              let mapped = Node.mapMsg string (buttonWith "b" (Action.Dispatch 42))

              match mapped.Kind with
              | NodeKind.Button(spec) ->
                  match spec.OnClick with
                  | Action.Dispatch s -> Expect.equal s "42" "Dispatch payload is f-applied"
                  | other -> failtestf "expected Dispatch, got %A" other
              | other -> failtestf "expected a Button, got %A" other
          }

          test "a Chain rewrites every nested Dispatch" {
              let onClick = Action.Chain [ Action.Dispatch 1; Action.Dispatch 2 ]
              let mapped = Node.mapMsg string (buttonWith "b" onClick)

              match mapped.Kind with
              | NodeKind.Button(spec) ->
                  match spec.OnClick with
                  | Action.Chain [ Action.Dispatch a; Action.Dispatch b ] ->
                      Expect.equal (a, b) ("1", "2") "each Chain member is remapped"
                  | other -> failtestf "expected a 2-element Chain of Dispatch, got %A" other
              | other -> failtestf "expected a Button, got %A" other
          }

          test "Call.onResult is post-composed with f (obj -> 'a -> 'b)" {
              let onClick = Action.Call("ep", Some(fun (o: obj) -> unbox<int> o), None)

              let mapped = Node.mapMsg string (buttonWith "b" onClick)

              match mapped.Kind with
              | NodeKind.Button(spec) ->
                  match spec.OnClick with
                  | Action.Call(ep, Some onResult, None) ->
                      Expect.equal ep "ep" "endpoint preserved"
                      Expect.equal (onResult (box 7)) "7" "onResult result is f-applied"
                  | other -> failtestf "expected Call, got %A" other
              | other -> failtestf "expected a Button, got %A" other
          }

          test "ReadFileBody.onRead is post-composed with f (string -> 'a -> 'b)" {
              let onRead (s: string) = s.Length

              let onClick = Action.ReadFileBody("f1", None, FileReadEncoding.Text, Some onRead)

              let mapped = Node.mapMsg string (buttonWith "b" onClick)

              match mapped.Kind with
              | NodeKind.Button(spec) ->
                  match spec.OnClick with
                  | Action.ReadFileBody(fileRef, _, FileReadEncoding.Text, Some onRead') ->
                      Expect.equal fileRef "f1" "file ref preserved"
                      Expect.equal (onRead' "abcd") "4" "onRead result is f-applied"
                  | other -> failtestf "expected ReadFileBody, got %A" other
              | other -> failtestf "expected a Button, got %A" other
          }

          test "a non-Msg action case (Navigate) passes through unchanged" {
              let mapped = Node.mapMsg string (buttonWith "b" (Action.Navigate "/home"))

              match mapped.Kind with
              | NodeKind.Button(spec) ->
                  match spec.OnClick with
                  | Action.Navigate route -> Expect.equal route "/home" "Navigate route preserved"
                  | other -> failtestf "expected Navigate, got %A" other
              | other -> failtestf "expected a Button, got %A" other
          }

          test "recursion into layout children; Id / Style traits preserved" {
              let child = buttonWith "child" (Action.Dispatch 9)

              let root: Node<int> =
                  Fuaran.stack
                      "root"
                      { Orientation = Orientation.Vertical
                        Children = [ child ]
                        Wrap = false }
                  |> Node.withTone ToneVariant.Brand

              let mapped = Node.mapMsg string root

              Expect.equal mapped.Id "root" "root Id preserved"

              Expect.equal (mapped.Style |> Option.map _.Tone) (Some ToneVariant.Brand) "Style trait preserved"

              match mapped.Kind with
              | NodeKind.Box(spec) ->
                  match spec.Children with
                  | [ mappedChild ] ->
                      Expect.equal mappedChild.Id "child" "child Id preserved"

                      match mappedChild.Kind with
                      | NodeKind.Button(b) ->
                          match b.OnClick with
                          | Action.Dispatch s -> Expect.equal s "9" "child Dispatch remapped in place"
                          | other -> failtestf "expected child Dispatch, got %A" other
                      | other -> failtestf "expected a child Button, got %A" other
                  | other -> failtestf "expected exactly one child, got %A" other
              | other -> failtestf "expected a Stack, got %A" other
          }

          test "a form field's onChange closure is remapped (closure -> Action -> Action)" {
              let field: FormField<int> =
                  { Defaults.formField with
                      Id = "name"
                      Label = TextSource.Literal "Name"
                      Kind =
                          FormFieldKind.Text(
                              Some(Binding.Static(Some "")),
                              Some(fun (s: string) -> Action.Dispatch s.Length)
                          ) }

              let node: Node<int> =
                  Fuaran.form
                      "f"
                      { Defaults.form with
                          Fields = [ field ]
                          OnSubmit = Action.Dispatch 0
                          SubmitLabel = TextSource.Literal "Go" }

              let mapped = Node.mapMsg string node

              match mapped.Kind with
              | NodeKind.Form(spec) ->
                  match spec.Fields with
                  | [ f ] ->
                      match f.Kind with
                      | FormFieldKind.Text(_, Some onChange) ->
                          match onChange "abc" with
                          | Action.Dispatch s -> Expect.equal s "3" "onChange result is f-applied"
                          | other -> failtestf "expected Dispatch from onChange, got %A" other
                      | other -> failtestf "expected a Text field with a Some handler, got %A" other
                  | other -> failtestf "expected one field, got %A" other
              | other -> failtestf "expected a Form, got %A" other
          }

          test "a Mount's OnBubble is post-composed with f (obj -> Action -> Action)" {
              let node: Node<int> =
                  Fuaran.mount
                      "m"
                      { ScopeId = "guest-1"
                        Inputs = None
                        Channel = Fuaran.guestOutChannel
                        OnBubble = Some(fun (o: obj) -> Action.Dispatch(unbox<int> o))
                        Capabilities = [] }

              let mapped = Node.mapMsg string node

              match mapped.Kind with
              | NodeKind.Mount spec ->
                  Expect.equal spec.ScopeId "guest-1" "ScopeId preserved"

                  match spec.OnBubble |> Option.map (fun f -> f (box 5)) with
                  | Some(Action.Dispatch s) -> Expect.equal s "5" "OnBubble output is f-applied"
                  | other -> failtestf "expected Dispatch from OnBubble, got %A" other
              | other -> failtestf "expected a Mount, got %A" other
          }

          test "round-trips: int -> string -> int recovers the original payload" {
              let mapped =
                  buttonWith "b" (Action.Dispatch 41)
                  |> Node.mapMsg (fun (i: int) -> i + 1)
                  |> Node.mapMsg string
                  |> Node.mapMsg int

              match mapped.Kind with
              | NodeKind.Button(spec) ->
                  match spec.OnClick with
                  | Action.Dispatch n -> Expect.equal n 42 "chained maps compose (41 -> 42 -> \"42\" -> 42)"
                  | other -> failtestf "expected Dispatch, got %A" other
              | other -> failtestf "expected a Button, got %A" other
          } ]
