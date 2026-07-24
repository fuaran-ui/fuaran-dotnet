module Fuaran.UI.ServerDriven.Tests.FormBufferTests

// ─── Phase 152 (Wave 18): form / input / local-state buffer flush ─────────────
//
// Policy (b): a `Binding.Local` form field is client-buffered (its live DOM value
// is the buffer; the shim does not round-trip keystrokes). The server sees the
// values only on a flush — `onSubmit` (harvest all fields) or
// `Action.CommitLocal` (an explicit "Apply", one field). The flush turns each
// buffered value into the field's `OnCommit` action, folds it (with the form's
// `OnSubmit` on a submit) in one diff, and patches only the changed nodes.
//
// Non-`Local` fields are NOT part of the protocol — a form of non-`Local` fields
// behaves exactly as the pre-policy 152 path (only `OnSubmit` fires).

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.FormBuffer

type Msg =
    | SetName of string
    | SetAge of float
    | Greet

type Model =
    { Name: string
      Age: float
      Greeted: bool }

let private initialModel =
    { Name = ""
      Age = 0.0
      Greeted = false }

let private update (msg: Msg) (m: Model) : Model =
    match msg with
    | SetName n -> { m with Name = n }
    | SetAge a -> { m with Age = a }
    | Greet -> { m with Greeted = true }

// A name field whose value is a client-buffered `Binding.Local` (flush via
// OnCommit → SetName); a 0..120 ranged age (flush via SetAge).
let private nameField: FormField<Msg> =
    { Defaults.formField<Msg> with
        Id = "name"
        Required = true
        Kind =
            FormFieldKind.Text(
                binding.local
                    (Binding.Static "")
                    LocalFlushTrigger.OnCommitAction
                    (fun n -> Action.Dispatch(SetName n))
                    None
                    (fun s -> Ok s),
                Some(fun _ -> Action.Chain [])
            ) }

let private ageField: FormField<Msg> =
    { Defaults.formField<Msg> with
        Id = "age"
        Kind =
            FormFieldKind.RangedNumber(
                binding.local
                    (Binding.Static 0.0)
                    LocalFlushTrigger.OnSubmit
                    (fun a -> Action.Dispatch(SetAge a))
                    None
                    (fun s ->
                        match System.Double.TryParse s with
                        | true, v -> Ok v
                        | _ -> Error "nan"),
                Some(fun _ -> Action.Chain []),
                { Defaults.numberFieldConstraints with
                    Min = Some 0.0
                    Max = Some 120.0 }
            ) }

let private formSpec: FormSpec<Msg> =
    { Defaults.form<Msg> with
        Fields = [ nameField; ageField ]
        OnSubmit = Action.Dispatch Greet }

let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.form "f" formSpec
                  Fuaran.button
                      "apply"
                      { Defaults.button<Msg> with
                          OnClick = Action.CommitLocal "name" }
                  Fuaran.markdown "status" (sprintf "%s/%g/%b" m.Name m.Age m.Greeted) ] }

let private stubRender (n: Node<Msg>) : string =
    let (NodeId s) = n.Id
    $"<n id='{s}'/>"

let private session () =
    init (DriverServices.create stubRender) update view initialModel

let private submitEv (values: (string * LiveValue) list) : LiveEvent =
    { ConnId = "c"
      NodeId = "f"
      Event = "submit"
      Payload = Map.ofList values
      LastSeq = 0 }

let private replacesOnly (nodeId: string) (patches: DomPatch list) : bool =
    // No patch re-renders / removes / moves any node other than `nodeId` (the
    // proof of "only the changed nodes patch" — the form keeps its DOM identity).
    patches
    |> List.forall (fun p ->
        match p with
        | DomPatch.ReplaceFragment(id, _)
        | DomPatch.SetText(id, _)
        | DomPatch.RemoveNode id -> id = nodeId
        | DomPatch.SetAttr(id, _, _)
        | DomPatch.RemoveAttr(id, _) -> id = nodeId
        | _ -> false)

[<Tests>]
let tests =
    testList
        "Form buffer flush (Phase 152 form policy)"
        [ test "fieldFlushAction recovers a Local Text field's OnCommit" {
              match fieldFlushAction nameField (LiveValue.Str "Ada") with
              | Some(Action.Dispatch(SetName "Ada")) -> ()
              | other -> failtestf "expected Dispatch(SetName Ada), got %A" other
          }

          test "fieldFlushAction parses a Local Number buffer through the binding's Parse" {
              match fieldFlushAction ageField (LiveValue.Num 42.0) with
              | Some(Action.Dispatch(SetAge 42.0)) -> ()
              | other -> failtestf "expected Dispatch(SetAge 42), got %A" other
          }

          test "fieldFlushAction returns None for a non-Local field (not in the protocol)" {
              let plain =
                  { Defaults.formField<Msg> with
                      Id = "p"
                      Kind = FormFieldKind.Text(Binding.Static "", Some(fun _ -> Action.Chain [])) }

              Expect.isNone (fieldFlushAction plain (LiveValue.Str "z")) "a non-Local field is not buffered here"
          }

          test "fieldFlushAction returns None for an unparseable buffer (flush to nothing, no mutation)" {
              Expect.isNone
                  (fieldFlushAction ageField (LiveValue.Str "not-a-number"))
                  "a malformed buffer flushes to nothing"
          }

          test "commitActions yields the Local fields' commits in declaration order" {
              let acts =
                  commitActions formSpec (Map.ofList [ "age", LiveValue.Num 30.0; "name", LiveValue.Str "Ada" ])

              match acts with
              | [ Action.Dispatch(SetName "Ada"); Action.Dispatch(SetAge 30.0) ] -> ()
              | other -> failtestf "expected [SetName Ada; SetAge 30] in field order, got %A" other
          }

          test "submit flushes buffered fields + OnSubmit in one step, patching only the changed node" {
              let s2, out =
                  step None (session ()) (submitEv [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 30.0 ])

              Expect.equal s2.Model.Name "Ada" "name buffer committed on submit"
              Expect.equal s2.Model.Age 30.0 "age buffer committed on submit"
              Expect.isTrue s2.Model.Greeted "OnSubmit (Greet) folded after the field commits"
              Expect.isNone out.Rejected "a clean submit is not rejected"
              Expect.isTrue (replacesOnly "status" out.Patches) "only the status node patched (form keeps DOM identity)"
          }

          test "CommitLocal flushes only the named field's buffer (the explicit Apply)" {
              let ev =
                  { ConnId = "c"
                    NodeId = "apply"
                    Event = "click"
                    Payload = Map.ofList [ "name", LiveValue.Str "Bob" ]
                    LastSeq = 0 }

              let s2, out = step None (session ()) ev

              Expect.equal s2.Model.Name "Bob" "Apply committed the name buffer"
              Expect.isFalse s2.Model.Greeted "Apply did not run OnSubmit"
              Expect.equal s2.Model.Age 0.0 "Apply did not touch the age buffer"
              Expect.isNone out.Rejected "a gated CommitLocal is not rejected"
          }

          test "with a validator, an invalid submit suppresses the mutation + surfaces field errors" {
              let s2, out =
                  step (Some FormValidation.declaredOnly) (session ()) (submitEv [ "age", LiveValue.Num 30.0 ])

              Expect.equal s2.Model initialModel "missing required 'name' → no mutation"
              Expect.isNone out.Rejected "a business-validation failure is not a G1 breach"

              Expect.contains
                  out.Patches
                  (DomPatch.SetAttr("name", "data-fuaran-field-error", "This field is required."))
                  "the missing-required field error addresses the field marker"
          }

          test "with a validator, a valid submit commits the buffers + clears field errors" {
              let s2, out =
                  step
                      (Some FormValidation.declaredOnly)
                      (session ())
                      (submitEv [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 30.0 ])

              Expect.equal s2.Model.Name "Ada" "valid submit committed the name buffer"
              Expect.isTrue s2.Model.Greeted "valid submit ran OnSubmit"

              Expect.contains
                  out.Patches
                  (DomPatch.RemoveAttr("name", "data-fuaran-field-error"))
                  "stale field-error attributes cleared on a valid submit"
          }

          test "declared bounds are enforced server-side on the buffered value (HTML5 bypass closed)" {
              // The age buffer arrives out of range; declared enforcement rejects
              // it before any commit, despite the field being client-buffered.
              let s2, out =
                  step
                      (Some FormValidation.declaredOnly)
                      (session ())
                      (submitEv [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 999.0 ])

              Expect.equal s2.Model initialModel "out-of-range age suppressed the mutation"

              Expect.contains
                  out.Patches
                  (DomPatch.SetAttr("age", "data-fuaran-field-error", "Must be at most 120."))
                  "the declared-bound error is surfaced on the buffered field"
          }

          test "a G1-rejected submit (stale node id) mutates nothing" {
              let ev =
                  { ConnId = "c"
                    NodeId = "ghost"
                    Event = "submit"
                    Payload = Map.empty
                    LastSeq = 0 }

              let s2, out = step None (session ()) ev
              Expect.equal s2.Model initialModel "an unknown node id is rejected, no mutation"
              Expect.isSome out.Rejected "G1 rejected the forged node id"
          }

          test "end-to-end through a LiveConnection: buffered submit commits + pushes one targeted frame" {
              let channel = InMemoryChannel()
              let conn = LiveConnection("c1", session (), channel)
              conn.EnableFormValidation FormValidation.declaredOnly

              // The channel routes by ConnId — must match the connection's id.
              channel.Send
                  { ConnId = "c1"
                    NodeId = "f"
                    Event = "submit"
                    Payload = Map.ofList [ "name", LiveValue.Str "Ada"; "age", LiveValue.Num 30.0 ]
                    LastSeq = 0 }

              Expect.equal conn.Session.Model.Name "Ada" "name committed through the channel"
              Expect.isTrue conn.Session.Model.Greeted "the form greeted on a valid submit"

              // One frame. The "status" node re-renders; the validated path also
              // clears the field-error attrs on name/age. The proof of "targeted"
              // is that neither the form ("f") nor the document root ("root") is
              // re-rendered — they keep DOM identity / focus / scroll.
              let reRenders id =
                  List.exists (function
                      | DomPatch.ReplaceFragment(n, _) -> n = id
                      | _ -> false)

              match channel.Pushed with
              | [ frame ] ->
                  Expect.isTrue (reRenders "status" frame.Patches) "the status node re-rendered"
                  Expect.isFalse (reRenders "f" frame.Patches) "the form kept its DOM identity (not re-rendered)"
                  Expect.isFalse (reRenders "root" frame.Patches) "no full-document re-render"
              | other -> failtestf "expected exactly one targeted frame, got %A" other
          }

          test "tryFindFormField resolves a field nested under a form" {
              match tryFindFormField "age" (view initialModel) with
              | Some f -> Expect.equal f.Id "age" "found the nested age field"
              | None -> failtest "expected to find the age field under the form"
          } ]
