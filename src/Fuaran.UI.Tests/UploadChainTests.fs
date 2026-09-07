module Fuaran.UI.Tests.UploadChainTests

// ============================================================================
//  `UploadStream.streamSelections` — the ORDERING contract of a multi-file
//  transfer, driven against a sink that completes LATER.
//
//  Why a second upload module beside `UploadSinkTests`: that one proves the
//  SEAM keeps its contract (one completion, monotonic progress, every refusal
//  reachable), and every one of its assertions holds against a sink that
//  completes inside `Upload`. This one proves the CALLER treats that completion
//  as the asynchronous event the seam declares it to be — a distinction a
//  synchronous sink cannot express at all, because under it "started" and
//  "completed" are the same instant and an eager caller is indistinguishable
//  from a chained one.
//
//  So the sink here does the one thing the reference sink does not: it parks
//  each `Upload` and hands the test the completion callback. Everything the
//  suite asserts is then a statement about WHEN, and every one of them fails on
//  the loop-and-report-after form:
//
//    * only one transfer is ever in flight, so the second file is not started
//      until the first has completed;
//    * the completed set is reported exactly once, from the LAST completion,
//      carrying every reference in selection order;
//    * a refusal mid-chain stops it where it stands — the files after it are
//      never started and the completed set is never reported at all, which is
//      what keeps the caller from writing a slot that says files exist at a
//      destination that does not hold them.
//
//  `streamSelections` is deliberately extracted OUT of the function component
//  so it can be driven here: a React hook cannot be exercised off a browser,
//  and the three callbacks are the whole of the component's involvement.
// ============================================================================

open Expecto
open Fuaran.UI.HostPrelude
open Fuaran.UI.Ops.UploadSink
open Fuaran.UI.Renderer

/// One parked transfer: what was asked for, and the two callbacks that answer
/// it. Holding the callbacks is what makes the completion an event the TEST
/// schedules rather than one the sink chooses.
type private Parked =
    { Destination: string
      Selection: FileSelection
      Progress: UploadProgress -> unit
      Complete: Result<UploadedRef, UploadRefusal> -> unit }

/// A sink that starts nothing and completes nothing until told. This is the
/// honest shape of a sink that touches a network: `Upload` returns before a
/// byte has moved.
type private DeferredSink(destinations: string list) =
    let pending = ResizeArray<Parked>()

    /// Every transfer the caller has started, in the order it started them.
    /// Its LENGTH is the load-bearing observation: an eager caller starts all
    /// of them before any completes.
    member _.Pending = pending

    /// Complete the transfer at `index` with the sink's answer. The chained
    /// caller starts the next one from inside this call, so `Pending` grows
    /// while this returns.
    member _.Settle (index: int) (result: Result<UploadedRef, UploadRefusal>) = pending[index].Complete result

    interface IFuaranUploadSink with
        member _.Destinations = Set.ofList destinations

        member _.Upload(destination, selection, onProgress, onComplete) =
            pending.Add
                { Destination = destination
                  Selection = selection
                  Progress = onProgress
                  Complete = onComplete }

let private selection (name: string) (size: int64) : FileSelection =
    { Name = name
      Size = size
      MimeType = "application/octet-stream"
      Ref = { Id = "0:" + name; Handle = None } }

let private reference (name: string) (size: int64) : UploadedRef =
    { FileId = "id-" + name
      Hash = "hash-" + name
      Size = size
      ContentType = "application/octet-stream" }

/// Drive `streamSelections` and collect everything it reported, in order.
let private drive (sink: DeferredSink) (selections: FileSelection array) =
    let refused = ResizeArray<UploadRefusal>()
    let completed = ResizeArray<UploadedRef list>()
    let progress = ResizeArray<int64 * int64>()

    UploadStream.streamSelections
        (sink :> IFuaranUploadSink)
        "recordings"
        selections
        (fun sent total -> progress.Add(sent, total))
        refused.Add
        completed.Add

    progress, refused, completed

[<Tests>]
let tests =
    testList
        "the multi-file upload chain"
        [ test "only ONE transfer is in flight — the next starts from the previous completion" {
              let sink = DeferredSink [ "recordings" ]

              let selections =
                  [| selection "a.mp4" 100L; selection "b.mp4" 200L; selection "c.mp4" 300L |]

              let _, refused, completed = drive sink selections

              // The whole finding, in one assertion: the eager form starts all
              // three here, because `Upload` returns immediately and the loop
              // does not wait for anything.
              Expect.equal sink.Pending.Count 1 "exactly one transfer started before any completion"
              Expect.equal sink.Pending[0].Selection.Name "a.mp4" "and it is the first selection"
              Expect.isEmpty completed "nothing is reported before the first file lands"

              sink.Settle 0 (Ok(reference "a.mp4" 100L))
              Expect.equal sink.Pending.Count 2 "the second starts from the first's completion"
              Expect.equal sink.Pending[1].Selection.Name "b.mp4" "in selection order"
              Expect.isEmpty completed "and still nothing is reported"

              sink.Settle 1 (Ok(reference "b.mp4" 200L))
              Expect.equal sink.Pending.Count 3 "and the third from the second's"
              Expect.isEmpty completed "and still nothing is reported"

              sink.Settle 2 (Ok(reference "c.mp4" 300L))

              // Reported exactly once, from the last completion, whole.
              Expect.equal completed.Count 1 "the completed set is reported exactly once"

              Expect.equal
                  (completed[0] |> List.map _.FileId)
                  [ "id-a.mp4"; "id-b.mp4"; "id-c.mp4" ]
                  "carrying every reference, in selection order"

              Expect.isEmpty refused "and nothing refused"
          }

          test "progress is CUMULATIVE across the chain and never exceeds the total" {
              let sink = DeferredSink [ "recordings" ]
              let selections = [| selection "a.mp4" 100L; selection "b.mp4" 200L |]

              let progress, _, _ = drive sink selections

              // The opening report is the honest zero: the control renders
              // 0 / 300 rather than an empty bar of unknown length.
              Expect.equal (List.ofSeq progress) [ (0L, 300L) ] "the total is known before the first byte"

              sink.Pending[0].Progress { BytesSent = 40L; TotalBytes = 100L }
              sink.Settle 0 (Ok(reference "a.mp4" 100L))
              sink.Pending[1].Progress { BytesSent = 50L; TotalBytes = 200L }

              // The second file's 50 bytes are reported as 150 of 300, not as
              // 50 of 200: the reader is watching one transfer of a set, and a
              // figure that restarted per file would go backwards.
              Expect.equal
                  (List.ofSeq progress)
                  [ (0L, 300L); (40L, 300L); (150L, 300L) ]
                  "each report carries the bytes already banked by the files before it"

              Expect.isTrue (progress |> Seq.forall (fun (sent, total) -> sent <= total)) "no report exceeds the total"
          }

          test "a REFUSAL mid-chain stops it, and the completed set is never reported" {
              let sink = DeferredSink [ "recordings" ]

              let selections =
                  [| selection "a.mp4" 100L; selection "b.mp4" 200L; selection "c.mp4" 300L |]

              let _, refused, completed = drive sink selections

              sink.Settle 0 (Ok(reference "a.mp4" 100L))
              sink.Settle 1 (Error(UploadRefusal.TooLarge("recordings", 150L)))

              // The files after the refusal are never started: reporting one
              // refusal rather than three for the same cause tells the reader
              // everything the three would.
              Expect.equal sink.Pending.Count 2 "the third file is never started"

              Expect.equal
                  (List.ofSeq refused)
                  [ UploadRefusal.TooLarge("recordings", 150L) ]
                  "the refusal is reported once, as the sink stated it"

              // The half that matters most: the caller writes its state slot
              // from `onCompleted`, so a set that is never reported is a slot
              // that is never written. A partial set reported here would tell
              // the reader that files exist at a destination that does not hold
              // them, and a later refusal cannot retract that.
              Expect.isEmpty completed "no completed set is reported after a refusal"
          }

          test "an EMPTY selection completes immediately with an empty set" {
              // The cleared-picker case. It must still report, or the control
              // says 'uploading' forever; and it must report EMPTY, not omit
              // the report, or the slot keeps a previous transfer's references.
              let sink = DeferredSink [ "recordings" ]
              let _, refused, completed = drive sink [||]

              Expect.equal sink.Pending.Count 0 "nothing was uploaded"
              Expect.equal completed.Count 1 "and the empty set is reported exactly once"
              Expect.isEmpty completed[0] "as empty"
              Expect.isEmpty refused "with nothing refused"
          }

          test "a sink that completes TWICE cannot fork the chain or report the set twice" {
              // The seam contracts exactly one completion per upload and this
              // caller does not trust it: a second completion for the same file
              // would otherwise restart the tail of the chain and report the
              // whole set again, which is a worse defect than the one this
              // function exists to remove.
              let sink = DeferredSink [ "recordings" ]
              let selections = [| selection "a.mp4" 100L; selection "b.mp4" 200L |]

              let _, refused, completed = drive sink selections

              sink.Settle 0 (Ok(reference "a.mp4" 100L))
              sink.Settle 1 (Ok(reference "b.mp4" 200L))
              Expect.equal completed.Count 1 "reported once"

              // Every one of these is a contract violation by the sink, and
              // none of them may reach the caller's reader.
              sink.Settle 1 (Ok(reference "b.mp4" 200L))
              sink.Settle 0 (Ok(reference "a.mp4" 100L))
              sink.Settle 1 (Error(UploadRefusal.TransportFailed("recordings", "late")))

              Expect.equal completed.Count 1 "and still exactly once after three late completions"
              Expect.isEmpty refused "a refusal arriving after the set was reported does not retract it"
              Expect.equal sink.Pending.Count 2 "and no transfer was restarted"
          } ]
