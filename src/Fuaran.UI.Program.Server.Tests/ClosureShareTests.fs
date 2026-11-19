module Fuaran.UI.Program.Server.Tests.ClosureShareTests

// ─── The closure share, certified at the UI witness (Phase 2813) ─────
//
// Program 0.16.0 carries on the demanded document, per tier, how much of what a
// program demands stays inside the closed vocabularies against how much exits
// to a host (its D71): the client tier's effect kinds and state namespaces
// inside, its host calls, opaque handlers and opaque leaves out; the server
// tier's closed capabilities inside, its host functions out; distinct demands,
// the share in basis points rounded down, 10000 for a tier demanding nothing.
//
// The definition's family, `conformance/demanded-closure-share.json`, is
// emitted in the program repository from walks at its toy witness. The copy
// beside this file is that family at the release this repository pins,
// declared in `copies.json`. This suite certifies it at the UI host twice:
//
//  1. THE FAMILY, READ HERE. Every readable vector decodes through the reader
//     this tier pins and carries the shares its `read` records, client and
//     server; every refusal vector is refused for the defect and at the field
//     it names. Nothing is skipped: the suite counts what it ran.
//  2. THE SAME SHAPES, WALKED HERE. Each readable vector names a shape of
//     program (all inside, one host call, one call action, a server handler
//     mixing compute stages and performed effects, closure-held handlers, an
//     opaque leaf, a server walk that found nothing). The UI witness's own
//     walk over a UI program of that shape projects exactly the shares the
//     vector records, and the UI document it writes is one its reader admits.
//
// A shape the UI vocabulary could not express would be named here rather than
// faked; every readable vector has a UI analogue.

#nowarn "44" // Action.Dispatch is in-process only, which is what makes it an opaque leaf

open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.Program.Bounded
open Fuaran.Program.Server
open Fuaran.UI.Program
open Fuaran.UI.Program.Server

/// This repository's byte copy of the program repository's family.
let private familyPath: string =
    Path.Combine(__SOURCE_DIRECTORY__, "conformance", "demanded-closure-share.json")
    |> Path.GetFullPath

/// A vector's recorded reading.
type private Reading =
    | Readable of client: ClosureShare * server: ClosureShare option
    | Refused of defect: string * field: string

type private Vector =
    { Id: string
      Document: string
      Reading: Reading }

let private shareOf (element: JsonElement) : ClosureShare =
    { Inside = element.GetProperty("inside").GetInt32()
      Exits = element.GetProperty("exits").GetInt32() }

/// The recorded basis points, which the reading states beside the counts.
let private pointsOf (element: JsonElement) : int =
    element.GetProperty("shareBasisPoints").GetInt32()

let private family () : JsonDocument =
    JsonDocument.Parse(File.ReadAllText familyPath)

let private vectorsOf (doc: JsonDocument) : Vector list =
    [ for v in doc.RootElement.GetProperty("vectors").EnumerateArray() do
          let read = v.GetProperty "read"

          let reading =
              match read.GetProperty("verdict").GetString() with
              | "ok" ->
                  let client = shareOf (read.GetProperty "closure")

                  let server =
                      if read.GetProperty("serverWalked").GetBoolean() then
                          Some(shareOf (read.GetProperty "serverClosure"))
                      else
                          None

                  Readable(client, server)
              | _ -> Refused(read.GetProperty("defect").GetString(), read.GetProperty("field").GetString())

          { Id = v.GetProperty("id").GetString()
            Document = v.GetProperty("document").GetString()
            Reading = reading } ]

// ─── the UI programs, one per readable shape ────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private btn (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            OnClick = action }

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

let private select (id: string) : Node<obj> =
    Fuaran.select
        id
        { Defaults.select<obj> with
            Label = TextSource.Literal id }

let private settle = "/handlers/settle"

/// A handler mixing compute stages with performed effects: the compute stage's
/// actions join the client tier, the closed arms are the server tier's inside
/// and the host function its exit.
let private mixed: Handler =
    { Name = settle
      Stages =
        [ HandlerStage.Compute(
              Action.Chain
                  [ Action.Navigate(TextSource.Literal "/receipt", NavigateTarget.Self)
                    Action.SetState("order.status", Some(jstr "settled"), None) ]
          )
          HandlerStage.Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "spinner") ])
          HandlerStage.Effect(ServerEffect.EmitPatch [ TreeOp.RemoveNode(NodeId "spinner") ])
          HandlerStage.Effect(ServerEffect.Notify("outbox", jstr "settled"))
          HandlerStage.Effect(ServerEffect.HostCall("charge", jstr "card", None)) ] }

let private registration = Map.ofList [ settle, mixed ]

/// The UI program of each readable vector's shape, walked by the UI witness.
let private walks: Map<string, DemandedProjection> =
    Map.ofList
        [ "no-exits",
          Demanded.ofTree (
              dash
                  [ btn
                        "save"
                        (Action.Chain
                            [ Action.SetState("form.name", Some(jstr "x"), None)
                              Action.SetState("view.mode", Some(jstr "read"), None)
                              Action.Navigate(TextSource.Literal "/saved", NavigateTarget.Self) ]) ]
          )
          "host-call-only", Demanded.ofTree (dash [ btn "quiet" (Action.Invoke("cap.quiet", [])) ])
          "call-only", Demanded.ofTree (dash [ btn "settle" (Action.Call(settle, None, None)) ])
          "server-mixed",
          (Harvest.ofProgram
              QueryPosture.PureRead
              registration
              (dash [ btn "settle" (Action.Call(settle, None, None)) ]))
              .Projection
          "undecoded-opaque-handlers",
          Demanded.ofTree (
              dash
                  [ select "region"
                    select "size"
                    btn "pick" (Action.SetState("form.region", Some(jstr "x"), None)) ]
          )
          "opaque-leaf",
          Demanded.ofTree (
              dash
                  [ btn "relay" (Action.Dispatch(box "a host message"))
                    btn "keep" (Action.SetState("form.kept", Some(jstr "x"), None)) ]
          )
          "tier-walked-empty",
          (Harvest.ofProgram
              QueryPosture.PureRead
              registration
              (dash [ btn "nobody" (Action.Call("/handlers/nobody", None, None)) ]))
              .Projection ]

let private render (share: ClosureShare) : string =
    $"{share.Inside} inside, {share.Exits} out, {share.BasisPoints} bp"

[<Tests>]
let tests =
    testList
        "Phase 2813 — the closure share, certified at the UI witness"
        [ test "the family is the copy at the version this tier reads" {
              Expect.isTrue (File.Exists familyPath) $"the family is committed at {familyPath}"
              use doc = family ()
              let root = doc.RootElement

              Expect.equal (root.GetProperty("corpus").GetString()) "demanded-closure-share" "the family's name"
              Expect.equal (root.GetProperty("documentKind").GetString()) Demanded.Kind "a demanded-document family"

              Expect.equal
                  [ for v in root.GetProperty("decodableVersions").EnumerateArray() -> v.GetInt32() ]
                  Demanded.decodableVersions
                  "written at the version the pinned reader reads"
          }

          test "every readable vector carries the shares its reading records, at the reader this tier pins" {
              use doc = family ()
              let readable = vectorsOf doc |> List.filter (fun v -> v.Reading.IsReadable)

              Expect.isNonEmpty readable "the family has readable vectors"

              for v in readable do
                  match v.Reading, Demanded.decode v.Document with
                  | Readable(client, server), Ok projection ->
                      Expect.equal projection.Closure client $"{v.Id}: the client tier's share"

                      Expect.equal
                          (projection.Server |> Option.map _.Closure)
                          server
                          $"{v.Id}: the server tier's share, present exactly when a server walk ran"

                      Expect.equal
                          (Demanded.encode projection)
                          v.Document
                          $"{v.Id}: and this tier writes it back byte for byte"
                  | _, Error failure -> failtestf "%s: refused %A at '%s'" v.Id failure.Defect failure.Field
                  | Refused _, _ -> ()
          }

          test "every recorded basis-point figure is the one the counts define" {
              use doc = family ()

              let figures =
                  [ for v in doc.RootElement.GetProperty("vectors").EnumerateArray() do
                        let read = v.GetProperty "read"

                        for key in [ "closure"; "serverClosure" ] do
                            match read.TryGetProperty key with
                            | true, share ->
                                let id = v.GetProperty("id").GetString()
                                yield $"{id}.{key}", share
                            | _ -> () ]

              Expect.isNonEmpty figures "the family records figures to check"

              for name, share in figures do
                  Expect.equal (pointsOf share) (shareOf share).BasisPoints name
          }

          test "every refusal vector is refused for its defect, at its field" {
              use doc = family ()
              let refused = vectorsOf doc |> List.filter (fun v -> v.Reading.IsRefused)

              Expect.isNonEmpty refused "the family has refusal vectors"

              for v in refused do
                  match v.Reading, Demanded.decode v.Document with
                  | Refused(defect, field), Error failure ->
                      Expect.equal (string failure.Defect) defect $"{v.Id}: the defect"
                      Expect.equal failure.Field field $"{v.Id}: the field"
                  | _, Ok _ -> failtestf "%s: a refusal vector was admitted" v.Id
                  | Readable _, _ -> ()
          }

          test "every readable shape has a UI program, and only those" {
              use doc = family ()

              let readable =
                  vectorsOf doc
                  |> List.filter (fun v -> v.Reading.IsReadable)
                  |> List.map _.Id
                  |> List.sort

              Expect.equal (walks.Keys |> List.ofSeq |> List.sort) readable "one UI program per readable vector"
          }

          test "the UI witness's own walk projects each readable vector's shares" {
              use doc = family ()

              for v in vectorsOf doc do
                  match v.Reading with
                  | Refused _ -> ()
                  | Readable(client, server) ->
                      let walked = walks[v.Id]

                      Expect.equal
                          (render walked.Closure)
                          (render client)
                          $"{v.Id}: the UI program's client share is the vector's"

                      Expect.equal
                          (walked.Server |> Option.map (fun t -> render t.Closure))
                          (server |> Option.map render)
                          $"{v.Id}: and its server share, present exactly when the vector's is"
          }

          test "the document each UI walk writes carries its shares, and its reader admits it" {
              for KeyValue(id, walked) in walks do
                  let document = Demanded.encode walked

                  Expect.stringContains
                      document
                      $"\"closure\":{{\"inside\":{walked.Closure.Inside},\"exits\":{walked.Closure.Exits},\"shareBasisPoints\":{walked.Closure.BasisPoints}}}"
                      $"{id}: the client share is on the document"

                  match Demanded.decode document with
                  | Ok back -> Expect.equal back walked $"{id}: read back unchanged"
                  | Error failure ->
                      failtestf "%s: the UI document is refused %A at '%s'" id failure.Defect failure.Field
          }

          test "PROBE — a UI document whose carried share its lists do not project is refused" {
              // The share is derived: a reader trusts the carried figure without
              // recounting only because it refuses one the lists contradict. Here
              // the UI tier's own document is edited by one count.
              let walked = walks["no-exits"]
              let document = Demanded.encode walked
              let carried = "\"closure\":{\"inside\":3,\"exits\":0,\"shareBasisPoints\":10000}"

              Expect.stringContains document carried "the walk carried three demands inside"

              match
                  Demanded.decode (
                      document.Replace(carried, "\"closure\":{\"inside\":2,\"exits\":0,\"shareBasisPoints\":10000}")
                  )
              with
              | Error failure ->
                  Expect.equal (string failure.Defect) "NotCanonical" "the edited share is refused as not canonical"
                  Expect.equal failure.Field "closure" "at the client share"
              | Ok _ -> failtest "a share its lists contradict was admitted"
          } ]
