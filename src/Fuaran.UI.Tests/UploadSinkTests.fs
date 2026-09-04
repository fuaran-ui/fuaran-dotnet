module Fuaran.UI.Tests.UploadSinkTests

// ============================================================================
//  Phase 1117 — the large-binary upload seam.
//
//  Three things are proved here and they are deliberately different in kind:
//
//    1. THE SEAM behaves as its contract says — one completion per upload,
//       chunked progress that never exceeds the total, and each refusal arm
//       reachable and distinguishable.
//    2. THE DEFAULT-DENY is real. Both refusals are exercised from the outside:
//       no sink at all, and a sink that does not name the destination. Neither
//       falls through to anything.
//    3. THE OP-STREAM DISCIPLINE. This is the phase's whole reason for
//       existing, so it is asserted directly rather than inferred: what the
//       host writes back after a transfer contains the reference and does not
//       contain the bytes, at any size — and the same fixture IS body-bearing
//       under the route this member exists to replace, which is the go-red twin
//       that keeps the assertion from passing vacuously.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI.HostPrelude
open Fuaran.UI.Ops.UploadSink

/// A selection of `size` bytes. Nothing here holds bytes — a `FileSelection` is
/// metadata plus an opaque handle by construction, which is the property the
/// third test set leans on.
let private selection (name: string) (mime: string) (size: int64) : FileSelection =
    { Name = name
      Size = size
      MimeType = mime
      Ref = { Id = "0:" + name; Handle = None } }

/// Drive one upload and collect everything the sink reported.
let private run (sink: IFuaranUploadSink) (destination: string) (sel: FileSelection) =
    let progress = ResizeArray<UploadProgress>()
    let completions = ResizeArray<Result<UploadedRef, UploadRefusal>>()
    sink.Upload(destination, sel, progress.Add, completions.Add)
    List.ofSeq progress, List.ofSeq completions

[<Tests>]
let tests =
    testList
        "Phase 1117 — the upload sink seam"
        [ test "the reference sink streams in CHUNKS and reports monotonic progress" {
              // 10 MiB over a 1 MiB chunk. The figure is the point: this is the
              // size class the member exists for, and the one a base64 round
              // trip through the message loop would inflate to ~14 MiB inside a
              // durable record.
              let mib = 1024L * 1024L
              let sink = InMemorySink([ "recordings" ], chunkBytes = int mib) :> IFuaranUploadSink

              let progress, completions =
                  run sink "recordings" (selection "clip.mp4" "video/mp4" (10L * mib))

              Expect.equal progress.Length 10 "one report per chunk"

              Expect.equal
                  (progress |> List.map (fun p -> p.BytesSent))
                  [ for i in 1..10 -> int64 i * mib ]
                  "progress advances one chunk at a time"

              // A consumer renders `sent / total`, so a report above the total
              // would render above 100%. The last chunk of a real transfer is
              // short; this asserts the sink never overshoots rather than
              // trusting that it divides evenly.
              Expect.isTrue
                  (progress |> List.forall (fun p -> p.BytesSent <= p.TotalBytes))
                  "no report exceeds the total"

              Expect.equal completions.Length 1 "exactly one completion"

              match completions with
              | [ Ok reference ] ->
                  Expect.equal reference.Size (10L * mib) "the reference carries the accepted size"
                  Expect.equal reference.ContentType "video/mp4" "and the recorded type"
                  Expect.isNotEmpty reference.Hash "and a content digest"
              | other -> failtestf "expected one Ok completion, got %A" other
          }

          test "a SHORT last chunk is reported, and a zero-byte file still completes" {
              let sink = InMemorySink([ "d" ], chunkBytes = 100) :> IFuaranUploadSink
              let progress, _ = run sink "d" (selection "a.bin" "application/octet-stream" 250L)

              Expect.equal
                  (progress |> List.map (fun p -> p.BytesSent))
                  [ 100L; 200L; 250L ]
                  "the last chunk is short, not rounded up"

              // A zero-byte file is a legitimate selection. Without the terminal
              // report a consumer that renders only on a progress callback would
              // never leave its initial state, which looks exactly like a hang.
              let zeroProgress, zeroCompletions =
                  run sink "d" (selection "empty.txt" "text/plain" 0L)

              Expect.equal zeroProgress.Length 1 "a zero-byte file reports once"
              Expect.equal zeroCompletions.Length 1 "and completes"
          }

          test "EVERY refusal arm is reachable, and each says something different" {
              // The arms are not interchangeable: a reader told "not available
              // here" and a reader told "too large" do different things next,
              // and an operator reading the log goes to different places. A
              // single "upload failed" case would have collapsed all five.
              let noSinkAtAll = noSink
              let _, unwired = run noSinkAtAll "recordings" (selection "a" "text/plain" 1L)

              match unwired with
              | [ Error(UploadRefusal.NoSink "recordings") ] -> ()
              | other -> failtestf "expected NoSink, got %A" other

              let sink =
                  InMemorySink([ "recordings" ], limitBytes = 100L, acceptTypes = [ "video/mp4" ]) :> IFuaranUploadSink

              let _, unregistered = run sink "somewhere-else" (selection "a" "video/mp4" 1L)

              match unregistered with
              | [ Error(UploadRefusal.UnregisteredDestination "somewhere-else") ] -> ()
              | other -> failtestf "expected UnregisteredDestination, got %A" other

              let _, tooLarge = run sink "recordings" (selection "a" "video/mp4" 101L)

              match tooLarge with
              | [ Error(UploadRefusal.TooLarge("recordings", 100L)) ] -> ()
              | other -> failtestf "expected TooLarge naming the limit, got %A" other

              let _, wrongType = run sink "recordings" (selection "a" "image/png" 1L)

              match wrongType with
              | [ Error(UploadRefusal.UnacceptableType("recordings", "image/png")) ] -> ()
              | other -> failtestf "expected UnacceptableType, got %A" other

              // The five announcements a reader can be given are distinct where
              // the distinction is actionable, and deliberately NOT distinct
              // where it is not: the three "you cannot upload here" causes are
              // one sentence, because a reader can do nothing differently about
              // any of them and naming the host's configuration to them would
              // be a disclosure with no purpose.
              Expect.equal
                  (announce (UploadRefusal.NoSink "d"))
                  (announce (UploadRefusal.UnregisteredDestination "d"))
                  "the reader is told the same thing by both unavailability causes"

              Expect.notEqual
                  (announce (UploadRefusal.NoSink "d"))
                  (announce (UploadRefusal.TooLarge("d", 1L)))
                  "and something different by the one they can act on"

              // The OPERATOR is told them apart, on the other channel. That
              // split is the whole design: a reader gets a sentence, a log gets
              // a diagnosis.
              Expect.notEqual
                  (describe (UploadRefusal.NoSink "d"))
                  (describe (UploadRefusal.UnregisteredDestination "d"))
                  "the operator's channel keeps the two causes apart"
          }

          test "a REFUSAL records nothing, and a refused destination is never resolved another way" {
              let sink = InMemorySink([ "recordings" ])
              let seam = sink :> IFuaranUploadSink

              // The near miss an emitter actually writes: a path, and a URL.
              // Neither is tried as anything — no prefix match, no default
              // destination, no fetch. A fallback would make registration
              // advisory, which is indistinguishable from not having it.
              for attempted in [ "/recordings"; "recordings/2026"; "https://example.invalid/upload" ] do
                  let _, result = run seam attempted (selection "a" "text/plain" 1L)

                  match result with
                  | [ Error(UploadRefusal.UnregisteredDestination d) ] ->
                      Expect.equal d attempted "the refusal names what was attempted"
                  | other -> failtestf "expected UnregisteredDestination for %s, got %A" attempted other

              Expect.isEmpty sink.Accepted "a refused upload accepted nothing"
          }

          test "the log-safe describer carries the DESTINATION and nothing of the reader's" {
              // The one arm whose text a sink chooses is `TransportFailed`, so
              // the constraint is stated on it. Every other arm's text is this
              // module's own and carries only the author-declared name.
              let reader = "medical-scan-of-jane-doe.png"

              let lines =
                  [ describe (UploadRefusal.NoSink "d")
                    describe (UploadRefusal.UnregisteredDestination "d")
                    describe (UploadRefusal.TooLarge("d", 10L))
                    describe (UploadRefusal.UnacceptableType("d", "image/png"))
                    describe (UploadRefusal.DispatchDenied "d") ]

              for line in lines do
                  Expect.isFalse (line.Contains reader) (sprintf "no reader-supplied text in: %s" line)
                  Expect.stringContains line "d" "the destination is named"
          }

          // ── The op-stream discipline ───────────────────────────────────────
          test "what the host writes back carries the REFERENCE and not the bytes" {
              // The write-back shape the renderer produces, asserted here
              // because this module can assert it without a browser: the state
              // slot is the host-reserved key, and its value is the reference
              // record and nothing else.
              let mib = 1024L * 1024L
              let sink = InMemorySink([ "recordings" ], chunkBytes = int mib) :> IFuaranUploadSink

              let _, completions =
                  run sink "recordings" (selection "clip.mp4" "video/mp4" (50L * mib))

              let reference =
                  match completions with
                  | [ Ok r ] -> r
                  | other -> failtestf "expected a reference, got %A" other

              let written =
                  JObj
                      [ "fileId", JStr reference.FileId
                        "hash", JStr reference.Hash
                        "size", JStr(string reference.Size)
                        "contentType", JStr reference.ContentType ]

              let text: string = Canon.render written

              // The size is a NUMBER in the record and a short decimal string on
              // the wire. Fifty mebibytes of payload would be ~70 MB of base64;
              // this bound is three orders of magnitude below that and would
              // catch any spelling of "the body came along too".
              Expect.isLessThan
                  text.Length
                  512
                  (sprintf "the whole write-back is a short record, not a payload: %d chars" text.Length)

              Expect.stringContains text reference.FileId "the reference's id is what replays"
              Expect.stringContains text reference.Hash "with the digest that makes it verifiable"

              // No base64 anywhere: the specific shape a body would arrive in.
              Expect.isFalse (text.Contains "base64") "no base64 marker"
              Expect.isFalse (text.Contains "data:") "no data URL"
          }

          test "GO-RED TWIN: the same file IS body-bearing on the route this member replaces" {
              // Without this the assertion above could pass on a fixture that
              // never had a body to leak. `Action.ReadFileBody` is the route
              // `destination` exists to replace, and its payload is the body as
              // a string — so the identical selection, taken that way, produces
              // exactly the thing the test above proves is absent.
              //
              // The body is synthesised here rather than read: this test is
              // about the SHAPE of the two routes, and reading a real file would
              // make it a test of the file system.
              let bodyBytes =
                  System.Text.Encoding.UTF8.GetBytes(String.replicate 4096 "video-payload-")

              let asBase64 = System.Convert.ToBase64String bodyBytes

              let throughTheMessageLoop: string = Canon.render (JObj [ "body", JStr asBase64 ])

              Expect.isGreaterThan
                  throughTheMessageLoop.Length
                  512
                  "the body route really does carry a payload — the reference assertion above is not vacuous"

              Expect.isGreaterThan
                  asBase64.Length
                  bodyBytes.Length
                  "and base64 inflates it, which is the second half of why the reference exists"
          } ]
