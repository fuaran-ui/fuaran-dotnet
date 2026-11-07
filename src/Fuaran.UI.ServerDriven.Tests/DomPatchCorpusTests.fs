module Fuaran.UI.ServerDriven.Tests.DomPatchCorpusTests

// ─── The DomPatch frame contract: the `server-driven/` corpus family ─────────
//
// Phase 158 QW6 locked the TreeOp → DomPatch lowering with a golden corpus held
// in this file. Phase 2528 moved those cases, and the ones Phases 2526 and 2527
// added, into the specification repository as the `server-driven/` family
// (WIRE_FORMAT.md §33), so the frame is a contract a second host can certify
// against. This suite holds no golden of its own: it reads the family, and
// `FUARAN_REGEN=1` emits it.
//
//   dom-patch-vectors.json  lowering: `before` + `ops` → `patches`, the script
//                           lowering (`Lowering.lowerScript`) under the family's
//                           reference renderer; apply: authored patches. Every
//                           case also carries the DOM before and after, which the
//                           browser leg (tests/content-js/browser-leg.mjs) checks
//                           against the shim.
//   frame-vectors.json      frames: `FrameWire`'s JSON body and SSE form, byte for
//                           byte; inbound: the client event bodies `Inbound`
//                           parses, as the shim sends them.
//
// The inputs and the DOMs are authored in the family; every computed member is
// this host's. The `DomPatchCorpus` Build target still runs this project as a
// standalone CI gate. An absent corpus FAILS here rather than skipping: the
// goldens live nowhere else.

open System
open System.IO
open System.Text
open System.Text.Json
open Microsoft.FSharp.Reflection
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation

[<Literal>]
let private FamilyDir = "server-driven"

[<Literal>]
let private DomPatchFile = "dom-patch-vectors.json"

[<Literal>]
let private FrameFile = "frame-vectors.json"

/// The family's reference renderer (§33.2): where a fragment goes is the
/// contract, not what a renderer draws.
let private referenceRender (n: Node<obj>) : string =
    let escaped =
        n.Id.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

    "<div data-fuaran-node-id=\"" + escaped + "\"></div>"

// ─── reading ────────────────────────────────────────────────────────────────

let private parseJson (text: string) : JVal =
    match Json.parse text with
    | Ok v -> v
    | Error e -> failwithf "not JSON: %s" e

let private members (where: string) (v: JVal) : (string * JVal) list =
    match v with
    | JObj fields -> fields
    | _ -> failwithf "%s: not an object" where

/// The object's members, refusing any member the schema does not declare.
let private closed (where: string) (allowed: string list) (v: JVal) : Map<string, JVal> =
    let fields = members where v

    for name, _ in fields do
        if not (List.contains name allowed) then
            failwithf "%s: undeclared member '%s'" where name

    Map.ofList fields

let private req (where: string) (name: string) (m: Map<string, JVal>) : JVal =
    match m.TryFind name with
    | Some v -> v
    | None -> failwithf "%s: missing member '%s'" where name

let private str (where: string) (v: JVal) : string =
    match v with
    | JStr s -> s
    | _ -> failwithf "%s: not a string" where

let private int32 (where: string) (v: JVal) : int =
    match v with
    | JInt n -> n
    | _ -> failwithf "%s: not a 32-bit integer" where

let private items (where: string) (v: JVal) : JVal list =
    match v with
    | JArr xs -> xs
    | _ -> failwithf "%s: not an array" where

// ─── the patch, effect and frame shapes (§33.4, §33.5) ──────────────────────

let private patchJson (p: DomPatch) : JVal = parseJson (DomPatch.encode p)

let private decodePatch (where: string) (v: JVal) : DomPatch =
    let kind = v |> members where |> List.tryFind (fst >> (=) "kind")

    let s name m =
        req where name m |> str $"{where}.{name}"

    let i name m =
        req where name m |> int32 $"{where}.{name}"

    match kind with
    | Some(_, JStr "SetAttr") ->
        let m = closed where [ "kind"; "nodeId"; "name"; "value" ] v
        DomPatch.SetAttr(s "nodeId" m, s "name" m, s "value" m)
    | Some(_, JStr "RemoveAttr") ->
        let m = closed where [ "kind"; "nodeId"; "name" ] v
        DomPatch.RemoveAttr(s "nodeId" m, s "name" m)
    | Some(_, JStr "SetText") ->
        let m = closed where [ "kind"; "nodeId"; "text" ] v
        DomPatch.SetText(s "nodeId" m, s "text" m)
    | Some(_, JStr "ReplaceFragment") ->
        let m = closed where [ "kind"; "nodeId"; "html" ] v
        DomPatch.ReplaceFragment(s "nodeId" m, s "html" m)
    | Some(_, JStr "InsertFragment") ->
        let m = closed where [ "kind"; "parentId"; "position"; "html" ] v
        DomPatch.InsertFragment(s "parentId" m, i "position" m, s "html" m)
    | Some(_, JStr "RemoveNode") ->
        let m = closed where [ "kind"; "nodeId" ] v
        DomPatch.RemoveNode(s "nodeId" m)
    | Some(_, JStr "ReorderChildren") ->
        let m = closed where [ "kind"; "parentId"; "orderedIds" ] v

        DomPatch.ReorderChildren(
            s "parentId" m,
            req where "orderedIds" m
            |> items $"{where}.orderedIds"
            |> List.map (str $"{where}.orderedIds")
        )
    | Some(_, JStr "MoveNode") ->
        let m = closed where [ "kind"; "nodeId"; "newParentId"; "position" ] v
        DomPatch.MoveNode(s "nodeId" m, s "newParentId" m, i "position" m)
    | _ -> failwithf "%s: no DomPatch kind" where

let private decodeEffect (where: string) (v: JVal) : ClientEffect =
    let kind = v |> members where |> List.tryFind (fst >> (=) "kind")

    let s name m =
        req where name m |> str $"{where}.{name}"

    match kind with
    | Some(_, JStr "WriteToClipboard") -> ClientEffect.WriteToClipboard(s "text" (closed where [ "kind"; "text" ] v))
    | Some(_, JStr "Navigate") ->
        let m = closed where [ "kind"; "route"; "target" ] v

        let target =
            match m.TryFind "target" with
            | None -> NavigateTarget.Self
            | Some(JStr "Blank") -> NavigateTarget.Blank
            | Some _ -> failwithf "%s.target: only \"Blank\" is written" where

        ClientEffect.Navigate(s "route" m, target)
    | Some(_, JStr "PushState") -> ClientEffect.PushState(s "route" (closed where [ "kind"; "route" ] v))
    | Some(_, JStr "Focus") -> ClientEffect.Focus(s "nodeId" (closed where [ "kind"; "nodeId" ] v))
    | Some(_, JStr "Download") ->
        let m = closed where [ "kind"; "url"; "name" ] v
        ClientEffect.Download(s "url" m, s "name" m)
    | Some(_, JStr "ReadFileBody") ->
        let m = closed where [ "kind"; "nodeId"; "encoding" ] v
        ClientEffect.ReadFileBody(s "nodeId" m, s "encoding" m)
    | Some(_, JStr "Print") ->
        closed where [ "kind" ] v |> ignore
        ClientEffect.Print
    | Some(_, JStr "Confirm") ->
        let m = closed where [ "kind"; "prompt"; "token" ] v
        ClientEffect.Confirm(s "prompt" m, s "token" m)
    | _ -> failwithf "%s: no ClientEffect kind" where

let private decodeFrame (where: string) (v: JVal) : Frame =
    let m = closed where [ "seq"; "patches"; "effects" ] v

    { Seq = req where "seq" m |> int32 $"{where}.seq"
      Patches =
        req where "patches" m
        |> items $"{where}.patches"
        |> List.mapi (fun i p -> decodePatch $"{where}.patches[{i}]" p)
      Effects =
        req where "effects" m
        |> items $"{where}.effects"
        |> List.mapi (fun i e -> decodeEffect $"{where}.effects[{i}]" e) }

/// A payload value as the server types it (§33.5): the wire model has no
/// `null`, so each value names its `LiveValue` case.
let private liveValueJson (v: LiveValue) : JVal =
    match v with
    | LiveValue.Str s -> JObj [ "$type", JStr "Str"; "value", JStr s ]
    | LiveValue.Num n when Math.Floor n = n && n >= float Int32.MinValue && n <= float Int32.MaxValue ->
        JObj [ "$type", JStr "Num"; "value", JInt(int n) ]
    | LiveValue.Num n -> JObj [ "$type", JStr "Num"; "value", JFloat n ]
    | LiveValue.Bool b -> JObj [ "$type", JStr "Bool"; "value", JBool b ]
    | LiveValue.Null -> JObj [ "$type", JStr "Null" ]

/// What a server reads from an inbound body; `"refused"` for a body that is not JSON.
let private eventJson (body: string) : JVal =
    match Inbound.tryParseLiveEvent "conn" body with
    | None -> JStr "refused"
    | Some e ->
        JObj
            [ "nodeId", JStr e.NodeId
              "event", JStr e.Event
              "payload", JObj [ for KeyValue(k, v) in e.Payload -> k, liveValueJson v ]
              "lastSeq", JInt e.LastSeq ]

// ─── emitting ───────────────────────────────────────────────────────────────

let private nodeOf (where: string) (v: JVal) : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObjJVal v with
    | Ok n -> n
    | Error e -> failwithf "%s: the node does not decode (%s at %s)" where e.Code e.Path

let private opOf (where: string) (v: JVal) : TreeOp<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeOpJVal v with
    | Ok op -> op
    | Error e -> failwithf "%s: the op does not decode (%s at %s)" where e.Code e.Path

let private domOf (where: string) (v: JVal) : JVal =
    let m = closed where [ "before"; "after" ] v
    req where "before" m |> str $"{where}.before" |> ignore
    req where "after" m |> str $"{where}.after" |> ignore
    v

let private nameOf (where: string) (m: Map<string, JVal>) =
    req where "name" m |> str $"{where}.name"

/// The patches the reference lowers a case to.
let private lowered (before: Node<obj>) (ops: TreeOp<obj> list) : DomPatch list =
    Lowering.lowerScript referenceRender before ops

let private emitLowering (i: int) (v: JVal) : JVal =
    let where = $"lowering[{i}]"
    let m = closed where [ "name"; "before"; "ops"; "patches"; "dom" ] v
    let before = req where "before" m |> nodeOf $"{where}.before"

    let ops =
        req where "ops" m
        |> items $"{where}.ops"
        |> List.mapi (fun j op -> opOf $"{where}.ops[{j}]" op)

    JObj
        [ "name", JStr(nameOf where m)
          "before", Generated.encodeNodeJson before
          "ops",
          JArr(
              ops
              |> List.map (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeOp >> parseJson)
          )
          "patches", JArr(lowered before ops |> List.map patchJson)
          "dom", req where "dom" m |> domOf $"{where}.dom" ]

/// `{ "<selector>": <scrollTop> }` or the focus record; carried as authored,
/// checked for shape.
let private stateOf (where: string) (allowed: string list) (v: JVal) : JVal =
    let m = closed where [ "before"; "after" ] v

    for side in [ "before"; "after" ] do
        let s = req where side m

        if List.isEmpty allowed then
            for _, top in members $"{where}.{side}" s do
                int32 $"{where}.{side}" top |> ignore
        else
            closed $"{where}.{side}" allowed s |> ignore

    v

let private emitApply (i: int) (v: JVal) : JVal =
    let where = $"apply[{i}]"
    let m = closed where [ "name"; "patches"; "dom"; "focus"; "scroll" ] v

    let patches =
        req where "patches" m
        |> items $"{where}.patches"
        |> List.mapi (fun j p -> decodePatch $"{where}.patches[{j}]" p)

    JObj
        [ yield "name", JStr(nameOf where m)
          yield "patches", JArr(patches |> List.map patchJson)
          yield "dom", req where "dom" m |> domOf $"{where}.dom"
          match m.TryFind "focus" with
          | Some f -> yield "focus", stateOf $"{where}.focus" [ "element"; "selection"; "scrollTop"; "same" ] f
          | None -> ()
          match m.TryFind "scroll" with
          | Some s -> yield "scroll", stateOf $"{where}.scroll" [] s
          | None -> () ]

let private emitFrame (i: int) (v: JVal) : JVal =
    let where = $"frames[{i}]"
    let m = closed where [ "name"; "frame"; "json"; "sse" ] v
    let frame = req where "frame" m |> decodeFrame $"{where}.frame"

    JObj
        [ "name", JStr(nameOf where m)
          "frame", parseJson (FrameWire.encodeJson frame)
          "json", JStr(FrameWire.encodeJson frame)
          "sse", JStr(FrameWire.encodeSse frame) ]

let private emitInbound (i: int) (v: JVal) : JVal =
    let where = $"inbound[{i}]"
    let m = closed where [ "name"; "dom"; "seq"; "gesture"; "body"; "event" ] v
    let body = req where "body" m |> str $"{where}.body"
    let produced = [ "dom"; "seq"; "gesture" ] |> List.filter m.ContainsKey

    if not (produced.IsEmpty || produced.Length = 3) then
        failwithf "%s: dom, seq and gesture go together (a body the shim produces) or not at all" where

    JObj
        [ yield "name", JStr(nameOf where m)
          for key in produced -> key, m.[key]
          yield "body", JStr body
          yield "event", eventJson body ]

let private emitArray (where: string) (emit: int -> JVal -> JVal) (file: Map<string, JVal>) : JVal =
    JArr(req "file" where file |> items where |> List.mapi emit)

let private domPatchDescription =
    "The DomPatch frame contract (WIRE_FORMAT.md 33, Phase 2528). lowering: decode `before` (a node) and `ops` (TreeOps) and lower `ops` as a script applied to `before`, each op read against the tree its predecessors built (33.3), every fragment drawn by `renderer`; the patches must equal `patches` in order. Then, in a DOM holding `dom.before`, apply `patches` in order; the DOM must equal `dom.after`. apply: patch lists the lowering does not produce under `renderer` (field addresses, positions inside a child list, absent addresses, and the focus, caret and scroll survival cases); apply `patches` to `dom.before` and compare with `dom.after`. Where `focus` is given, focus `focus.before.element` with its `selection` and `scrollTop` first; afterwards `focus.after.element` holds focus with those values, and is the same element object when `same` is true. Where `scroll` is given, each selector's scrollTop is set first and holds the `after` value afterwards. Emitted by the reference host (fuaran-dotnet, FUARAN_REGEN=1 over Fuaran.UI.ServerDriven.Tests): the inputs and both DOMs are authored, every lowering case's `patches` is the reference's."

let private rendererDescription =
    "A node renders as <div data-fuaran-node-id=\"ID\"></div>, ID its id with & < > \" written as character references. The family certifies where a fragment goes, not what a renderer draws."

let private frameDescription =
    "The frame envelope and the client event (WIRE_FORMAT.md 33.5, Phase 2528). frames: `frame` is a frame; `json` is its JSON body and `sse` its Server-Sent-Events form, byte for byte; a client reading `sse` receives one `patch` event whose id is the frame's seq and whose data parses to `frame`. inbound: `body` is a client event; a server reads it as `event`, each payload value naming the case the server types it as (Str, Num, Bool, Null), or refuses it as not JSON (`event` is \"refused\"). Where `dom`, `seq` and `gesture` are given the shim produces `body` itself: in a DOM holding `dom`, after a frame with that seq, the gesture on `gesture.target` sends exactly `body`. Emitted by the reference host (fuaran-dotnet, FUARAN_REGEN=1 over Fuaran.UI.ServerDriven.Tests): the inputs are authored, `frame`, `json`, `sse` and `event` are the reference's."

let private familyDescription =
    "The DomPatch frame contract (WIRE_FORMAT.md 33, Phase 2528). SELF-ENUMERATED, so the directory is one corpus family (27.7): `server-driven`. dom-patch-vectors.json carries the TreeOp to DomPatch lowering (a tree, an op script, the patches) and the patch application (the DOM before and after, with focus, caret and scroll survival); frame-vectors.json carries the frame envelope (JSON body and SSE form) and the client event bodies. One producer (fuaran-dotnet's lowering) and one applier (fuaran-dotnet's fuaran-live-patch.js) certify on it today (33.6). Emitted by the reference host (fuaran-dotnet, FUARAN_REGEN=1 over Fuaran.UI.ServerDriven.Tests)."

/// A family file as committed: canonical members, indented two spaces, LF,
/// one trailing newline - the layout the self-enumerated families share.
let private committedText (value: JVal) : string =
    use doc = JsonDocument.Parse(Canonical.write Rendering.CrossHost value)
    use buffer = new MemoryStream()

    let options =
        JsonWriterOptions(Indented = true, Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    do
        use writer = new Utf8JsonWriter(buffer, options)
        doc.WriteTo writer

    Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n") + "\n"

let private familyPath (root: string) (file: string) = Path.Combine(root, FamilyDir, file)

let private committed (root: string) (file: string) : Map<string, JVal> =
    let path = familyPath root file

    if not (File.Exists path) then
        failwithf "%s is absent - the family's inputs are authored there" path

    File.ReadAllText path |> parseJson |> members file |> Map.ofList

/// Every family file this host emits, from the committed inputs.
let private emitted (root: string) : (string * string) list =
    let domPatch = committed root DomPatchFile
    let frames = committed root FrameFile

    [ DomPatchFile,
      committedText (
          JObj
              [ "version", JInt 1
                "description", JStr domPatchDescription
                "renderer", JStr rendererDescription
                "lowering", emitArray "lowering" emitLowering domPatch
                "apply", emitArray "apply" emitApply domPatch ]
      )
      FrameFile,
      committedText (
          JObj
              [ "version", JInt 1
                "description", JStr frameDescription
                "frames", emitArray "frames" emitFrame frames
                "inbound", emitArray "inbound" emitInbound frames ]
      )
      "manifest.json",
      committedText (
          JObj
              [ "version", JInt 1
                "description", JStr familyDescription
                "files", JArr [ JStr DomPatchFile; JStr FrameFile ] ]
      ) ]

// ─── the suite ──────────────────────────────────────────────────────────────

let private regenerating = Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1"

let private opKinds (ops: JVal list) : string list =
    let rec walk (op: JVal) =
        [ match op with
          | JObj fields ->
              match fields |> List.tryFind (fst >> (=) "$type") with
              | Some(_, JStr t) -> yield t
              | _ -> ()

              match fields |> List.tryFind (fst >> (=) "ops") with
              | Some(_, JArr inner) -> yield! inner |> List.collect walk
              | _ -> ()
          | _ -> () ]

    ops |> List.collect walk

let private patchKinds (cases: JVal list) : string list =
    [ for case in cases do
          match case |> members "case" |> List.tryFind (fst >> (=) "patches") with
          | Some(_, JArr ps) ->
              for p in ps do
                  match p |> members "patch" |> List.tryFind (fst >> (=) "kind") with
                  | Some(_, JStr k) -> yield k
                  | _ -> ()
          | _ -> () ]

let private unionCases (t: Type) : string list =
    FSharpType.GetUnionCases t |> Array.map (fun c -> c.Name) |> List.ofArray

let private memberOf (name: string) (case: JVal) : JVal =
    case |> members "case" |> List.find (fst >> (=) name) |> snd

let private caseName (case: JVal) : string = memberOf "name" case |> str "name"

let private familyTests (root: string) : Test list =
    let domPatch = committed root DomPatchFile
    let frames = committed root FrameFile
    let lowering = req "file" "lowering" domPatch |> items "lowering"
    let apply = req "file" "apply" domPatch |> items "apply"
    let frameCases = req "file" "frames" frames |> items "frames"
    let inbound = req "file" "inbound" frames |> items "inbound"

    let allOps =
        lowering
        |> List.collect (fun c -> memberOf "ops" c |> items "ops")
        |> opKinds
        |> Set.ofList

    let framePatches =
        frameCases |> List.map (memberOf "frame") |> patchKinds |> Set.ofList

    let frameEffects =
        [ for c in frameCases do
              for e in
                  memberOf "frame" c
                  |> members "frame"
                  |> List.find (fst >> (=) "effects")
                  |> snd
                  |> items "effects" do
                  match e |> members "effect" |> List.tryFind (fst >> (=) "kind") with
                  | Some(_, JStr k) -> yield k
                  | _ -> () ]
        |> Set.ofList

    [ test "the committed family is what this host emits (FUARAN_REGEN=1 rewrites it)" {
          for file, text in emitted root do
              let path = familyPath root file

              if regenerating then
                  Directory.CreateDirectory(Path.GetDirectoryName path |> nonNull) |> ignore
                  File.WriteAllText(path, text, UTF8Encoding(false))

              Expect.equal (File.ReadAllText path) text $"{path} is current"
      }

      test "every TreeOp case has a lowering case" {
          for kind in unionCases typeof<TreeOp<obj>> do
              Expect.isTrue (allOps.Contains kind) $"no lowering case carries a {kind}"
      }

      test "every DomPatch kind reaches the applier, and every DomPatch and ClientEffect kind has a frame vector" {
          let applied = patchKinds (lowering @ apply) |> Set.ofList

          for kind in unionCases typeof<DomPatch> do
              Expect.isTrue (applied.Contains kind) $"no DOM case applies a {kind}"
              Expect.isTrue (framePatches.Contains kind) $"no frame vector carries a {kind}"

          for kind in unionCases typeof<ClientEffect> do
              Expect.isTrue (frameEffects.Contains kind) $"no frame vector carries a {kind}"
      }

      testList
          "lowering"
          [ for i, case in List.indexed lowering ->
                test (caseName case) {
                    let where = $"lowering[{i}]"
                    let before = memberOf "before" case |> nodeOf where

                    let ops = memberOf "ops" case |> items where |> List.map (opOf where)

                    let actual =
                        lowered before ops
                        |> List.map patchJson
                        |> JArr
                        |> Canonical.write Rendering.CrossHost

                    let expected = memberOf "patches" case |> Canonical.write Rendering.CrossHost
                    Expect.equal actual expected (caseName case)
                } ]

      testList
          "frames"
          [ for i, case in List.indexed frameCases ->
                test (caseName case) {
                    let frame = memberOf "frame" case |> decodeFrame $"frames[{i}]"
                    Expect.equal (FrameWire.encodeJson frame) (memberOf "json" case |> str "json") "the JSON body"
                    Expect.equal (FrameWire.encodeSse frame) (memberOf "sse" case |> str "sse") "the SSE form"
                } ]

      testList
          "inbound"
          [ for case in inbound ->
                test (caseName case) {
                    let body = memberOf "body" case |> str "body"

                    Expect.equal
                        (eventJson body |> Canonical.write Rendering.CrossHost)
                        (memberOf "event" case |> Canonical.write Rendering.CrossHost)
                        "the event a server reads"
                } ] ]

/// An absent or unreadable family is ONE failing test, not a suite that cannot
/// be discovered: the rest of this project still runs and reports.
[<Tests>]
let tests =
    let cases =
        try
            familyTests (Fuaran.Tests.CorpusRoot.find ())
        with e ->
            [ test "the server-driven corpus family reads" { failtest e.Message } ]

    testList "DomPatch frame contract (the server-driven corpus family, Phase 158 QW6 / Phase 2528)" cases
