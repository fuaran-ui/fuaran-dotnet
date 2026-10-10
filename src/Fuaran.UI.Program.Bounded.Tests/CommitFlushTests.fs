module Fuaran.UI.Program.Bounded.Tests.CommitFlushTests

// ─── Phase 2198: the bounded flush — a commit names the key it writes ───────
//
// `Action.CommitLocal` names a form field; the key it writes is that field's
// `Local` binding's `commitTo`, found in the TREE, and the value is the event's
// flush payload member named by the field's id. Every bounded placement
// resolves both after the trust boundary and folds the commit as the core's
// `Assign`; each flushed write meets the dispatch gate on its own; and the
// demanded projection of a tree names the namespace a commit writes, where a
// tree-blind view reads it as a leaf that demands nothing.
//
// The trees are DECODED from the wire, so the `Local` bindings are the ones a
// decoding host holds: no closure, a declared `commitTo`, and the codec.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime
open Fuaran.UI.Program
open Fuaran.UI.Program.BoundedDriver
open Fuaran.UI.Renderer.BindingResolver

let private local (commitTo: string option) (codec: string option) (key: string) (dflt: string) : string =
    let commit =
        match commitTo with
        | Some k -> sprintf ",\"commitTo\":\"%s\"" k
        | None -> ""

    let codec =
        match codec with
        | Some c -> sprintf ",\"codec\":%s" c
        | None -> ""

    sprintf
        "{\"$type\":\"Local\"%s%s,\"flushOn\":{\"$type\":\"OnCommitAction\"},\"format\":\"<closure>\",\"initialFrom\":{\"$type\":\"State\",\"defaultValue\":%s,\"key\":\"%s\"},\"parse\":\"<closure>\"}"
        codec
        commit
        dflt
        key

let private field (id: string) (kind: string) (value: string) : string =
    sprintf
        "{\"id\":\"%s\",\"kind\":{\"$type\":\"%s\",\"value\":%s},\"label\":\"%s\",\"required\":false}"
        id
        kind
        value
        id

/// One form, four fields: a numeric buffer with the `Number` codec, a text
/// buffer, a buffer that declares no destination, and one whose destination is
/// under the host-reserved namespace.
let private profileForm: Node<obj> =
    let fields =
        [ field
              "salary-input"
              "Number"
              (local (Some "profile.salary") (Some "{\"$type\":\"Number\",\"decimals\":2}") "profile.salary" "0")
          field "name-input" "Text" (local (Some "profile.name") None "profile.name" "\"\"")
          field "note-input" "Text" (local None None "profile.note" "\"\"")
          field "secret-input" "Text" (local (Some "host.secret") None "profile.secret" "\"\"") ]

    let json =
        sprintf
            "{\"id\":\"profile\",\"kind\":{\"$type\":\"Form\",\"fields\":[%s],\"onSubmit\":{\"$type\":\"Chain\",\"ops\":[]},\"submitLabel\":\"Save\"}}"
            (String.concat "," fields)

    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error err -> failwithf "the profile form does not decode: %A" err

let private button (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            Label = TextSource.Literal id
            OnClick = action }

let private root (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = profileForm :: children }

let private wire (children: Node<obj> list) : WireTree = WireTree.ofDecoded (root children)

let private session (services: BoundedServices) (children: Node<obj> list) : BoundedSession =
    BoundedDriver.init services BindingSources.empty (wire children)

let private permissive (children: Node<obj> list) : BoundedSession =
    session (BoundedServices.createPermissive (fun n -> n.Id)) children

let private click (nodeId: string) (payload: (string * LiveValue) list) : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = "click"
      Payload = Map.ofList payload
      LastSeq = 0 }

let private stateOf (s: BoundedSession) (key: string) : obj option = Map.tryFind key s.Store.State

let private commit (fieldId: string) : Action<obj> = Action.CommitLocal fieldId

[<Tests>]
let tests =
    testList
        "bounded flush (Phase 2198)"
        [ test "a commit writes the buffered value into its field's commitTo key" {
              let s, out =
                  BoundedDriver.step
                      (permissive [ button "apply" (commit "name-input") ])
                      (click "apply" [ "name-input", LiveValue.Str "Ada" ])

              Expect.isNone out.Rejected "an admitted commit"
              Expect.isEmpty out.Diagnostics "nothing refused, nothing inert"
              Expect.equal (stateOf s "profile.name") (Some(box "Ada")) "the flushed value, under commitTo"
          }

          test "a numeric codec writes a number: as sent, or read from its text, and refuses anything else" {
              let start = permissive [ button "apply" (commit "salary-input") ]

              let asNumber, _ =
                  BoundedDriver.step start (click "apply" [ "salary-input", LiveValue.Num 52000.0 ])

              let asText, _ =
                  BoundedDriver.step start (click "apply" [ "salary-input", LiveValue.Str " 52000.5 " ])

              let refused, out =
                  BoundedDriver.step start (click "apply" [ "salary-input", LiveValue.Str "52k" ])

              Expect.equal (stateOf asNumber "profile.salary") (Some(box 52000.0)) "a number, written as one"
              Expect.equal (stateOf asText "profile.salary") (Some(box 52000.5)) "numeric text, read as a number"
              Expect.isNone (stateOf refused "profile.salary") "text that is not a number writes nothing"
              Expect.isNone out.Rejected "a refused write is not a refused event"
              Expect.isNonEmpty out.Diagnostics "and it says so"
          }

          test "an event carrying no value for the field writes nothing, and says so" {
              let s, out =
                  BoundedDriver.step (permissive [ button "apply" (commit "name-input") ]) (click "apply" [])

              Expect.isNone (stateOf s "profile.name") "nothing written"
              Expect.isNone out.Rejected "not an event-level refusal"
              Expect.isNonEmpty out.Diagnostics "a diagnostic names the refused write"
          }

          test "a commit in a chain folds in order with the writes around it" {
              let action =
                  Action.Chain
                      [ Action.SetState("profile.name", Some(Fuaran.Core.JStr "before"), None)
                        commit "name-input"
                        Action.SetState("msg", Some(Fuaran.Core.JStr "after"), None) ]

              let s, _ =
                  BoundedDriver.step
                      (permissive [ button "apply" action ])
                      (click "apply" [ "name-input", LiveValue.Str "Ada" ])

              Expect.equal (stateOf s "profile.name") (Some(box "Ada")) "the commit overwrote the write before it"
              Expect.equal (stateOf s "msg") (Some(box "after")) "and the write after it ran"
          }

          test "the flushed write meets the dispatch gate on its own, as the SetState it is" {
              let gated =
                  { BoundedServices.createPermissive (fun n -> n.Id) with
                      CanDispatch =
                          fun a ->
                              match a with
                              | Action.SetState _ -> false
                              | _ -> true }

              let s, out =
                  BoundedDriver.step
                      (session gated [ button "apply" (commit "name-input") ])
                      (click "apply" [ "name-input", LiveValue.Str "Ada" ])

              Expect.isSome out.Rejected "a denied write refuses the event"
              Expect.isNone (stateOf s "profile.name") "and nothing is written"
          }

          test "a commit whose key is under the host-reserved namespace is refused by the core" {
              let s, out =
                  BoundedDriver.step
                      (permissive [ button "apply" (commit "secret-input") ])
                      (click "apply" [ "secret-input", LiveValue.Str "forged" ])

              Expect.isNone (stateOf s "host.secret") "no write under host."
              Expect.isNonEmpty out.Diagnostics "the refusal is diagnosed"
          }

          test "a commit whose field declares no destination writes nothing" {
              let s, out =
                  BoundedDriver.step
                      (permissive [ button "apply" (commit "note-input") ])
                      (click "apply" [ "note-input", LiveValue.Str "kept local" ])

              Expect.isNone (stateOf s "profile.note") "nothing written"
              Expect.isNone out.Rejected "an inert commit is not a refused event"
          }

          test "a commit in a confirm's continuation is flushed by the answer, not the ask" {
              let action =
                  Action.Confirm(TextSource.Literal "Save the name?", commit "name-input", None)

              let start = permissive [ button "apply" action ]
              let payload = [ "name-input", LiveValue.Str "Ada" ]
              let asked, _ = BoundedDriver.step start (click "apply" payload)
              Expect.isNone (stateOf asked "profile.name") "the ask writes nothing"

              let answered, out =
                  BoundedDriver.step
                      asked
                      (click
                          "apply"
                          (payload
                           @ [ ConfirmAnswer.TokenKey, LiveValue.Str "apply#"
                               ConfirmAnswer.AcceptedKey, LiveValue.Bool true ]))

              Expect.isNone out.Rejected "the answer is admitted"
              Expect.equal (stateOf answered "profile.name") (Some(box "Ada")) "the yes flushed the commit"
          }

          test "the client placement flushes exactly as the server placement does" {
              let program =
                  Program.mkBounded
                      (ProgramServices.createPermissive ignore)
                      BindingSources.empty
                      (wire [ button "apply" (commit "name-input") ])

              let after, out =
                  Program.handleEvent program (click "apply" [ "name-input", LiveValue.Str "Ada" ])

              Expect.isNone out.Rejected "admitted"
              Expect.equal (Map.tryFind "profile.name" after.Store.State) (Some(box "Ada")) "written"
          }

          test "a tree's demanded projection names the namespace a commit writes" {
              let tree = root [ button "apply" (commit "name-input") ]
              let projection = Demanded.ofTree tree

              Expect.contains
                  projection.StateNamespaces
                  { Namespace = "profile"
                    Written = true
                    Read = false }
                  "the commit writes the profile namespace"
          }

          // The go-red half: the same tree read through the tree-blind witness —
          // the view before this phase — demands nothing for the commit, so the
          // test above cannot be met by a witness that leaves the commit a leaf.
          test "the same tree read without resolving its commits names no write (the silent leaf)" {
              let tree = root [ button "apply" (commit "name-input") ]

              let blind = Fuaran.Program.Bounded.Demanded.ofTree UiWitness.demandWitness tree

              Expect.isEmpty blind.StateNamespaces "a tree-blind commit reads as a leaf that demands nothing"
              Expect.isNonEmpty (Demanded.ofTree tree).StateNamespaces "and the resolved one does not"
          }

          test "the lowering names the key: a resolved commit views as Assign, an unresolvable one as a leaf" {
              let tree = root []

              match UiWitness.view (UiWitness.lowerCommits tree (commit "name-input")) with
              | ActionView.Assign(key, None, None) -> Expect.equal key "profile.name" "the commitTo key"
              | other -> failtestf "a resolved commit should view as Assign, viewed as %A" other

              match UiWitness.view (UiWitness.lowerCommits tree (commit "note-input")) with
              | ActionView.Leaf declaration ->
                  Expect.equal declaration LeafDeclaration.none "no destination: a leaf that demands nothing"
              | other -> failtestf "an unresolvable commit should view as a leaf, viewed as %A" other

              match UiWitness.view (UiWitness.lowerCommits tree (commit "absent")) with
              | ActionView.Leaf _ -> ()
              | other -> failtestf "a commit naming no field should view as a leaf, viewed as %A" other
          } ]
