module Fuaran.UI.Telemetry.Tests.ValidateOutcomeTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

// ============================================================================
//  Phase 330 — the runtime validate-outcome leg of the interaction-correlation
//  spine, plus the OPEN-SURFACE INVARIANT that keeps this package's
//  correlation vocabulary generic.
//
//  The invariant test is the one that matters most here and is the least
//  obvious. `PromptId` on these records is an opaque token a host supplies;
//  this package must never name what a host's correlation id IS. A field, doc
//  comment, or error string that named a specific upstream layer would publish
//  that layer's existence on a searchable public surface — an abstraction leak,
//  not a cosmetic one. So the test greps the shipped source for the vocabulary
//  and fails on a hit, rather than trusting review to catch it.
// ============================================================================

let private record (outcome: ValidateOutcome) (codes: string list) : ValidateOutcomeTelemetry =
    { Outcome = outcome
      TopCodes = codes
      PromptId = Some "opaque-id-1"
      UserId = None
      Timestamp = DateTimeOffset.UnixEpoch }

[<Tests>]
let validateOutcomeTests =
    testList
        "Phase 330 — ValidateOutcome"
        [ test "the classification tokens are stable" {
              Expect.equal (ValidateOutcome.name ValidateOutcome.Clean) "clean" "clean"
              Expect.equal (ValidateOutcome.name (ValidateOutcome.Warnings 3)) "warnings" "warnings"
              Expect.equal (ValidateOutcome.name (ValidateOutcome.Errors 1)) "errors" "errors"
              Expect.equal (ValidateOutcome.name (ValidateOutcome.NotRun "no validator")) "not-run" "not-run"
          }

          test "a not-run outcome has NO finding count — unknown is not zero" {
              // The distinction is load-bearing: a host aggregate that read
              // NotRun as 0 findings would report a broken validator as a
              // perfectly clean session.
              Expect.equal (ValidateOutcome.findingCount ValidateOutcome.Clean) (Some 0) "clean means zero findings"

              Expect.equal
                  (ValidateOutcome.findingCount (ValidateOutcome.Warnings 4))
                  (Some 4)
                  "warnings carry their count"

              Expect.isNone
                  (ValidateOutcome.findingCount (ValidateOutcome.NotRun "validator threw"))
                  "not-run has no count at all"
          }

          test "only Clean is clean — a not-run outcome is unknown, not a pass" {
              Expect.isTrue (ValidateOutcome.isClean ValidateOutcome.Clean) "clean is clean"
              Expect.isFalse (ValidateOutcome.isClean (ValidateOutcome.Warnings 1)) "warnings are not clean"
              Expect.isFalse (ValidateOutcome.isClean (ValidateOutcome.Errors 1)) "errors are not clean"

              Expect.isFalse
                  (ValidateOutcome.isClean (ValidateOutcome.NotRun "none wired"))
                  "and neither is 'we did not check'"
          } ]

[<Tests>]
let validateSinkTests =
    testList
        "Phase 330 — the validate leg on the reference sinks"
        [ test "InMemorySink buffers validate records and exposes them" {
              let sink = InMemorySink()
              let asSink = sink :> IFuaranTelemetrySink

              asSink.RecordValidateOutcome(record ValidateOutcome.Clean [])
              asSink.RecordValidateOutcome(record (ValidateOutcome.Errors 2) [ "FUARAN040"; "FUARAN056" ])

              Expect.equal (List.length sink.ValidateOutcomeRecords) 2 "both records buffered, in order"

              Expect.equal
                  (sink.ValidateOutcomeRecords |> List.map _.Outcome)
                  [ ValidateOutcome.Clean; ValidateOutcome.Errors 2 ]
                  "insertion order preserved"

              Expect.equal
                  (sink.ValidateOutcomeRecords |> List.last).TopCodes
                  [ "FUARAN040"; "FUARAN056" ]
                  "the top codes survive"
          }

          test "Clear resets the validate buffer with the others" {
              let sink = InMemorySink()
              (sink :> IFuaranTelemetrySink).RecordValidateOutcome(record ValidateOutcome.Clean [])
              sink.Clear()
              Expect.isEmpty sink.ValidateOutcomeRecords "cleared"
          }

          test "the opaque PromptId round-trips through the sink untouched" {
              let sink = InMemorySink()

              (sink :> IFuaranTelemetrySink).RecordValidateOutcome
                  { record ValidateOutcome.Clean [] with
                      PromptId = Some "whatever-the-host-put-here" }

              Expect.equal
                  (sink.ValidateOutcomeRecords.Head.PromptId)
                  (Some "whatever-the-host-put-here")
                  "carried verbatim — the sink neither parses nor interprets it"
          }

          test "the no-op sink accepts a validate record and drops it" {
              // Structural: the point is that it compiles and does not throw.
              (NoOpSink.create ()).RecordValidateOutcome(record ValidateOutcome.Clean [])
          } ]

// ─── The open-surface invariant ───────────────────────────────────

[<Tests>]
let openSurfaceInvariantTests =
    testList
        "Phase 330 — the correlation vocabulary stays generic"
        [ test "no shipped source in this package names a specific upstream correlation vocabulary" {
              // `PromptId` is an OPAQUE token. This package must describe it in
              // generic terms only — a field, comment, or message naming a
              // particular upstream layer's id, package, or API would publish
              // that layer on a searchable public surface. Grepping is the
              // enforcement, because review is exactly what has let this class
              // of leak through before.
              //
              // SCOPE, stated because the narrower list is a deliberate choice
              // and not an oversight. The banned tokens are the ones that name a
              // SPECIFIC upstream thing: the id's private name, the private
              // package prefixes, and a private API by name. They do NOT include
              // the bare words "orchestration" / "orchestrator", which appear in
              // several pre-existing comments in this package as generic prose
              // about a downstream tier ("emitted from the orchestration engine's
              // provider call site"). Genericising that prose is real work with a
              // real owner — the moat-telegraphing sweep — and folding it into
              // this invariant would have meant either rewriting six unrelated
              // files under a correlation-spine phase or leaving the guard red.
              // A guard that is red for reasons outside its phase gets disabled,
              // which is worse than a guard with a stated boundary.
              let banned =
                  [ "turnid"
                    "fuaran.ui.orchestration"
                    "toolup.fuaran.adapter"
                    "orchestration.install" ]

              let root = IO.Path.GetFullPath(IO.Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

              let shippedDirs =
                  [ IO.Path.Combine(root, "src", "Fuaran.UI.Telemetry.Abstractions")
                    IO.Path.Combine(root, "src", "Fuaran.UI.Telemetry.Default") ]

              let offences =
                  shippedDirs
                  |> List.filter IO.Directory.Exists
                  |> List.collect (fun dir ->
                      IO.Directory.GetFiles(dir, "*.fs", IO.SearchOption.AllDirectories)
                      |> Array.filter (fun f ->
                          let n = f.Replace('\\', '/')
                          not (n.Contains "/obj/" || n.Contains "/bin/"))
                      |> Array.toList)
                  |> List.collect (fun file ->
                      let text = IO.File.ReadAllText(file).ToLowerInvariant()

                      banned
                      |> List.filter text.Contains
                      |> List.map (fun token -> sprintf "%s names '%s'" (IO.Path.GetFileName file) token))

              Expect.isEmpty
                  offences
                  (sprintf
                      "the telemetry surface must describe correlation ids generically; offences: %s"
                      (String.concat "; " offences))
          }

          test "the grep would actually catch a leak (the guard tests itself)" {
              // A guard that silently matches nothing is worse than no guard —
              // it passes forever and proves nothing. This exercises the same
              // scan against a token that IS present, so a future refactor that
              // breaks the file discovery fails HERE rather than going quiet.
              let root = IO.Path.GetFullPath(IO.Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

              let files =
                  IO.Directory.GetFiles(
                      IO.Path.Combine(root, "src", "Fuaran.UI.Telemetry.Abstractions"),
                      "*.fs",
                      IO.SearchOption.AllDirectories
                  )
                  |> Array.filter (fun f ->
                      let n = f.Replace('\\', '/')
                      not (n.Contains "/obj/" || n.Contains "/bin/"))

              Expect.isNonEmpty files "the invariant's file discovery finds shipped sources"

              let hits =
                  files
                  |> Array.filter (fun f -> IO.File.ReadAllText(f).ToLowerInvariant().Contains "opaque")

              Expect.isNonEmpty hits "and the same scan finds a token that is genuinely present"
          } ]
