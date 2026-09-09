module Fuaran.UI.OpStream.Tests.TreeOpMapLaws

// ============================================================================
//  Phase 1587 — the laws `TreeOp.mapMsg` and the reference `IOpJsonCodec`
//  are asserted to satisfy.
//
//  Three claims, and they are deliberately different in kind:
//
//   1. **Identity preservation, over the corpus's registered op set.** For
//      every committed `ops/` fixture, decode → map → re-encode reproduces the
//      bytes of decode → re-encode. Stated over the CORPUS rather than over
//      generated ops because the corpus is the wire's own account of which op
//      shapes exist, and the manifest's `ops` family is checked to be COVERED
//      by the fixtures the law ran on — otherwise "over the registered op set"
//      is an unfalsifiable claim about whatever files happened to be there.
//
//   2. **The map is wire-invisible.** Encoding is `'Msg`-invariant
//      (`Action.Dispatch` emits `{"$type":"Dispatch"}` and nothing else), so
//      mapping onto a DIFFERENT host message type must not move a byte. This is
//      the claim a host depends on when it persists what it decoded.
//
//   3. **A refusal names the op and the slot**, and constructs nothing. Run
//      against a hand-built op rather than a corpus fixture, because no corpus
//      op carries an eagerly-reachable message: the wire has no message type, so
//      only a `Dispatch` the decoder rebuilt has a payload at all. The op is
//      built here so the refusal has something to refuse.
//
//  `Action.Dispatch` is marked in-process-only by the IDL annotation, which
//  renders as `[<Obsolete(…, false)>]` — FS0044, an error under this repo's
//  warnings-as-errors. Scoped off for this file: the whole point of the
//  refusal law is an op carrying exactly that case, and the marking addresses
//  code that puts one on a tree it means to SERIALISE, which this does not.
#nowarn "44"

// The corpus walk uses `DirectoryInfo.Parent` and `JsonElement.GetString()`,
// both nullable, against a committed fixture tree where the null cases are
// dead (the walk terminates at the filesystem root; every manifest entry is a
// string). `ChainCorpusTests` scopes the same warning off for the same reason.
// The `box`ed payloads below are `objnull` for the same F# 10 reason the
// decoder's own sentinel is.
#nowarn "3261"

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.TreeOpMap
open Fuaran.UI.OpStream.Abstractions

// ---------------------------------------------------------------------------
//  The corpus
// ---------------------------------------------------------------------------

/// Walk up from the test assembly to the workspace `wire-format-fixtures/`
/// corpus. `None` in a bare single-repo clone — the same posture (and the same
/// reason) as `ChainCorpusTests`: a missing input degrades to a skip and never
/// takes the assembly's type initializer down with it.
let private tryCorpusRoot () : string option = Fuaran.Tests.CorpusRoot.tryFind () // Phase 1647 — the ONE resolver

let private corpusRoot = tryCorpusRoot ()

let private requireCorpus () : string =
    match corpusRoot with
    | Some root -> root
    | None ->
        skiptest
            "wire-format-fixtures/ not found walking up from the test assembly — these TreeOp.mapMsg laws need the workspace checkout (skipped in a bare single-repo clone)"

/// Every `ops/` fixture, as (file name, decoded op). A fixture that does not
/// decode is a defect in the corpus or the decoder, not something to skip past
/// — the whole family is asserted decodable.
let private decodedOps () : (string * TreeOp<obj>) list =
    let root = requireCorpus ()

    Directory.GetFiles(Path.Combine(root, "ops"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.map (fun path ->
        let name = Path.GetFileName path

        match JsonDecode.decodeOp (File.ReadAllText path) with
        | Ok op -> name, op
        | Error e -> failtestf "corpus fixture ops/%s did not decode: %s at '%s' — %s" name e.Code e.Path e.Message)

/// The op kinds `manifest.json` declares registered.
let private registeredOpKinds () : string list =
    let root = requireCorpus ()
    use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

    doc.RootElement.GetProperty("ops").EnumerateArray()
    |> Seq.map (fun e -> e.GetString())
    |> List.ofSeq

/// The op case's own name — the discriminator the manifest's `ops` family
/// lists, read off the decoded value rather than off the file name.
let private opKindName (op: TreeOp<obj>) : string =
    match op with
    | TreeOp.EditNode _ -> "EditNode"
    | TreeOp.UpdateProp _ -> "UpdateProp"
    | TreeOp.ReplaceBinding _ -> "ReplaceBinding"
    | TreeOp.UpdateStyle _ -> "UpdateStyle"
    | TreeOp.UpdateState _ -> "UpdateState"
    | TreeOp.InsertChild _ -> "InsertChild"
    | TreeOp.RemoveNode _ -> "RemoveNode"
    | TreeOp.MoveNode _ -> "MoveNode"
    | TreeOp.ReorderChildren _ -> "ReorderChildren"
    | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
    | TreeOp.Batch _ -> "Batch"

// ---------------------------------------------------------------------------
//  A host message type that is NOT `obj`
// ---------------------------------------------------------------------------

/// Stands in for a host's own message DU. Two cases so the mapper has a real
/// choice to make and `Relabelled` is not the only inhabitant a wrong
/// implementation could stumble into.
type private LawMsg =
    | Relabelled of string
    | Never

/// The shape a real host's mapper takes: it recognises the decoder's erased
/// payload and answers with one of its own messages.
let private totalMapper (payload: obj) : LawMsg option = Some(Relabelled(string payload))

/// The refusing mapper — nothing is typeable.
let private refusingMapper (_: obj) : LawMsg option = None

// ---------------------------------------------------------------------------
//  An op that carries an EAGERLY-reachable message
// ---------------------------------------------------------------------------

/// A button whose `OnClick` is a `Dispatch` — the one place a `'Msg` sits in a
/// node as a field rather than behind a closure, and therefore the only shape
/// `mapMsg` can refuse before building anything.
let private dispatchingButton: Node<obj> =
    { Id = "law-button"
      Kind =
        NodeKind.Button
            { Defaults.button with
                Label = TextSource.Literal "Go"
                OnClick = Action.Dispatch(box "law-payload") }
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

let private insertingOp: TreeOp<obj> =
    TreeOp.InsertChild(NodeId "law-parent", dispatchingButton)

[<Tests>]
let tests =
    testList
        "Phase 1587 — TreeOp.mapMsg laws"
        [ test "identity: mapping with a total mapper moves no byte, over every corpus op fixture" {
              let fixtures = decodedOps ()

              Expect.isNonEmpty fixtures "the ops/ fixture family is not empty"

              for name, op in fixtures do
                  match TreeOp.mapMsg totalMapper op with
                  | Error refusal -> failtestf "ops/%s: a total mapper refused — %s" name (MapRefusal.render refusal)
                  | Ok mapped ->
                      Expect.equal
                          (CanonicalJson.encodeOp mapped)
                          (CanonicalJson.encodeOp op)
                          (sprintf "ops/%s re-encodes identically after the map" name)
          }

          test "the law ran over the corpus's REGISTERED op set, not merely over some files" {
              let seen = decodedOps () |> List.map (snd >> opKindName) |> Set.ofList

              let missing =
                  registeredOpKinds () |> List.filter (fun kind -> not (Set.contains kind seen))

              Expect.isEmpty
                  missing
                  (sprintf
                      "every op kind manifest.json registers is exercised by an ops/ fixture the identity law ran on; missing: %s"
                      (String.concat ", " missing))
          }

          test "decode-then-map equals decode, as bytes — through the shipped reference codec" {
              let codec = OpJsonCodec.canonical totalMapper
              let erased = OpJsonCodec.canonicalObj ()

              for name, _ in decodedOps () do
                  let json = File.ReadAllText(Path.Combine(requireCorpus (), "ops", name))

                  match codec.DecodeOp json, erased.DecodeOp json with
                  | Ok typed, Ok untyped ->
                      Expect.equal
                          (codec.EncodeOp typed)
                          (erased.EncodeOp untyped)
                          (sprintf "ops/%s: the typed and erased codecs agree on the bytes" name)
                  | Error e, _
                  | _, Error e -> failtestf "ops/%s: the reference codec refused a corpus fixture — %s" name e
          }

          test "refusal names the op and the slot, and constructs nothing" {
              match TreeOp.mapMsg refusingMapper insertingOp with
              | Ok _ -> failtest "a mapper that types nothing must not produce a mapped op"
              | Error refusal ->
                  Expect.equal refusal.Op "InsertChild" "the refusal names the op case"
                  Expect.equal refusal.Slot "child" "the refusal names the op's slot"
                  Expect.equal refusal.Node (Some "law-parent") "the refusal names the addressed node"

                  Expect.isNonEmpty refusal.Payloads "the refusal names the payload it declined"

                  let rendered = MapRefusal.render refusal

                  Expect.stringContains rendered "InsertChild" "the rendering names the op"
                  Expect.stringContains rendered "child" "the rendering names the slot"
                  Expect.stringContains rendered "law-payload" "the rendering names the refused payload"
          }

          test "a refusal inside a Batch keeps the inner op's name and carries its index" {
              let batch = TreeOp.Batch [ TreeOp.RemoveNode(NodeId "x"); insertingOp ]

              match TreeOp.mapMsg refusingMapper batch with
              | Ok _ -> failtest "a Batch containing an unmappable op must refuse"
              | Error refusal ->
                  Expect.equal refusal.Op "InsertChild" "the inner op's name survives — 'Batch' answers no question"
                  Expect.equal refusal.Slot "ops[1].child" "the slot carries the index within the batch"
          }

          test "the eagerly-reachable payload IS reached — a total mapper maps the same op" {
              // The go-red half of the refusal law: without this, a `mapMsg`
              // that refused unconditionally would pass the two tests above.
              match TreeOp.mapMsg totalMapper insertingOp with
              | Error refusal -> failtestf "a total mapper must map this op — %s" (MapRefusal.render refusal)
              | Ok(TreeOp.InsertChild(_, child)) ->
                  match child.Kind with
                  | NodeKind.Button spec ->
                      match spec.OnClick with
                      | Action.Dispatch msg ->
                          Expect.equal msg (Relabelled "law-payload") "the host's own message reached the tree"
                      | other -> failtestf "expected the Dispatch to survive the map, got %A" other
                  | other -> failtestf "expected a Button, got %A" other
              | Ok other -> failtestf "expected an InsertChild, got %A" other
          }

          test "the 'Msg-free ops re-tag and can never refuse" {
              // `UpdateProp` / `ReplaceBinding` / `UpdateStyle` / `RemoveNode` /
              // `MoveNode` / `ReorderChildren` carry no message, so the refusing
              // mapper must still map them: a refusal here would mean the walk
              // was inventing payloads to ask about.
              let msgFree =
                  [ TreeOp.UpdateProp(NodeId "a", "Label", PropValue.Native(box "x"))
                    TreeOp.ReplaceBinding(NodeId "a", "Source", Binding.Static(Some(box 1)))
                    TreeOp.UpdateStyle(NodeId "a", Defaults.style)
                    TreeOp.RemoveNode(NodeId "a")
                    TreeOp.MoveNode(NodeId "a", NodeId "b")
                    TreeOp.ReorderChildren(NodeId "a", [ NodeId "b"; NodeId "c" ]) ]

              for op in msgFree do
                  match TreeOp.mapMsg refusingMapper op with
                  | Ok _ -> ()
                  | Error refusal ->
                      failtestf
                          "%s carries no message and must not refuse — %s"
                          (opKindName op)
                          (MapRefusal.render refusal)
          } ]
