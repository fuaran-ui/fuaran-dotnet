module Fuaran.UI.JsonDecode.Tests.DecoderFuzzTests

// ============================================================================
//  Phase 779 — the gate-resident half of the decoder robustness fuzz.
//
//  Four things live here, and the order they are written in is the order they
//  matter in:
//
//   1. The GO-RED self-test. Five deliberately-broken stand-in decoders, one
//      per invariant, driven through the identical machinery the real run uses.
//      A fuzz harness that has never been observed to fail is decoration, and
//      the way decoration survives review is by passing — so the proof that it
//      CAN fail is asserted here on every run rather than demonstrated once by
//      hand at authoring time and thereafter assumed.
//
//   2. The BOUNDED run against the real decoder — a fixed seed, a fixed
//      iteration count, sized to keep this suite a few seconds. This is the
//      regression gate: a decoder change that reintroduces an escape is caught
//      by the next `Test` run, not by the next long run somebody remembers to
//      launch.
//
//   3. The COVERAGE assertions. A harness generating nothing but unparseable
//      junk would pass invariant 1 trivially and prove nothing about the typed
//      decoders underneath. So the bounded run must ACCEPT some inputs, must
//      refuse others across several distinct error codes, and must reach the
//      over-closed recovery class; if it stops doing any of those, the
//      generators have drifted and this fails loudly rather than going quietly
//      green.
//
//   4. The OVER-CLOSE COST PIN — the one finding this phase surfaced and
//      deliberately did not fix, recorded as a ceiling so it cannot become
//      unrecorded again.
//
//  The long run (`--fuzz-long`) is the same machinery at a larger iteration
//  count and a larger payload cap; its published result is the evidence note
//  the threat-model documents cite.
// ============================================================================

open System
open System.IO
open Expecto

/// The gate seed is FIXED, not clock-derived. A gate whose input set changes
/// per run is a gate that fails on someone else's commit for reasons neither of
/// you can reproduce; exploration is the long run's job, and it takes its seed
/// on the command line.
let private gateSeed = 20260817UL

/// Sized against the measured throughput of this harness so the bounded run
/// stays a small fraction of the suite. Raising it is cheap and safe; the number
/// is a time budget, not a coverage claim.
let private gateIterations = 5000

// ─── Go-red self-test ───────────────────────────────────────────────────────

/// A stand-in decoder that misbehaves on the first `limit` inputs matching
/// `trigger`, and defers to the real one otherwise. Bounded deliberately: a
/// mutant that failed on EVERY input would prove the harness reports something,
/// not that it discriminates.
let private mutant
    (name: string)
    (limit: int)
    (trigger: string -> bool)
    (misbehave: string -> DecoderFuzz.SubjectResult)
    : DecoderFuzz.Subject =
    let fired = ref 0

    { Name = name
      Run =
        fun input ->
            if trigger input && fired.Value < limit then
                fired.Value <- fired.Value + 1
                misbehave input
            else
                DecoderFuzz.nodeSubject.Run input }

/// Budgets tuned for the self-test: a small soft time budget so the slow mutant
/// need only sleep briefly. The hard budget stays high, so the watchdog never
/// fires during a deliberate soft breach and kills the test host.
let private selfTestBudgets =
    { DecoderFuzz.defaultBudgets with
        SoftTimeMs = 100.0
        HardTimeMs = 60000.0 }

let private selfTestConfig =
    { DecoderFuzz.boundedConfig with
        MaxPayloadChars = 8 * 1024 }

/// Run a mutant subject and return the distinct verdict classes it produced,
/// with how many counterexamples there were in total.
let private classesFrom (subject: DecoderFuzz.Subject) (iterations: int) : string list * int =
    let stats =
        DecoderFuzz.run [ subject ] selfTestBudgets selfTestConfig gateSeed iterations false

    let classes =
        stats.Counterexamples
        |> List.map (fun c -> DecoderFuzz.verdictClass c.Verdict)
        |> List.distinct
        |> List.sort

    classes, List.length stats.Counterexamples

let private expectCaught (subject: DecoderFuzz.Subject) (iterations: int) (expectedClass: string) =
    let classes, count = classesFrom subject iterations

    if count = 0 then
        failtestf
            "GO-RED FAILURE: the harness ran %d iterations against the deliberately-broken '%s' and reported nothing. A fuzz harness that cannot fail is decoration."
            iterations
            subject.Name

    Expect.contains
        classes
        expectedClass
        (sprintf "'%s' should be caught as %s; saw %A" subject.Name expectedClass classes)

[<Tests>]
let goRedSelfTest =
    testList
        "Fuaran.UI.Ops.JsonDecode — fuzz harness go-red self-test (Phase 779)"
        [ test "invariant 1 (totality): an escaping exception is caught" {
              // The defect class the threat-model claim is actually about: a
              // decoder that throws instead of returning a typed error.
              let broken =
                  mutant "throwing-decoder" 4 (fun s -> s.Length > 2) (fun _ ->
                      raise (InvalidOperationException "deliberate injected decoder defect"))

              expectCaught broken 400 "escaped-InvalidOperationException"
          }

          test "invariant 2 (termination): a decode past the soft time budget is caught" {
              let broken =
                  mutant "slow-decoder" 3 (fun s -> s.Length > 2) (fun input ->
                      System.Threading.Thread.Sleep 250
                      DecoderFuzz.nodeSubject.Run input)

              expectCaught broken 400 "timeout"
          }

          test "invariant 3 (bounded work): an allocation blow-up is caught" {
              // The trigger excludes over-closed inputs on purpose: those are
              // routed to the documented higher ceiling, so a mutant firing on
              // one would prove nothing about the ORDINARY budget.
              let broken =
                  mutant
                      "allocating-decoder"
                      2
                      (fun s -> s.Length > 2 && not (DecoderFuzz.isOverClosed s))
                      (fun input ->
                          // Allocate well past the floor, in pieces the runtime
                          // cannot elide, touching each so it is real.
                          let mutable sink = 0

                          for _ in 1..24 do
                              let block = Array.zeroCreate<byte> (2 * 1024 * 1024)
                              block[0] <- 1uy
                              sink <- sink + int block[0]

                          if sink < 0 then
                              failwith "unreachable"

                          DecoderFuzz.nodeSubject.Run input)

              expectCaught broken 400 "overallocated"
          }

          test "invariant 4 (fixed point): a canonical form that is not a fixed point is caught" {
              let broken =
                  mutant "drifting-encoder" 4 (fun s -> s.Length > 2) (fun _ ->
                      DecoderFuzz.Accepted("{\"a\":1}", Ok "{\"a\":2}"))

              expectCaught broken 400 "fixed-point-broken"
          }

          test "invariant 4 (fixed point): a canonical form the decoder itself refuses is caught" {
              let broken =
                  mutant "unreadable-output-decoder" 4 (fun s -> s.Length > 2) (fun _ ->
                      DecoderFuzz.Accepted("{\"a\":1}", Error "INVALID_JSON"))

              expectCaught broken 400 "canonical-refused"
          }

          test "the mutants are PARTIAL — the real decoder over the same inputs is clean" {
              // The inverse pin. Every mutant above defers to the real decoder
              // once its firing budget is spent, so if this run ALSO reported
              // counterexamples the go-red proof would be vacuous: it would show
              // the harness reports everything, not that it discriminates.
              let stats =
                  DecoderFuzz.run [ DecoderFuzz.nodeSubject ] selfTestBudgets selfTestConfig gateSeed 400 false

              Expect.isEmpty
                  (stats.Counterexamples
                   |> List.map (fun c -> DecoderFuzz.describeVerdict c.Verdict))
                  "the unmutated decoder must be clean over the same inputs the mutants ran on"
          } ]

// ─── Minimiser + repro persistence ──────────────────────────────────────────

[<Tests>]
let harnessMechanics =
    testList
        "Fuaran.UI.Ops.JsonDecode — fuzz harness mechanics (Phase 779)"
        [ test "the minimiser reduces an input to near the failing feature" {
              // A synthetic classifier, so the assertion is about the minimiser
              // and nothing else: the input "fails" iff it contains the marker.
              let marker = "@@BOOM@@"
              let noise = String.replicate 400 "0123456789"
              let input = noise + marker + noise

              let classify (s: string) =
                  if s.Contains marker then "boom" else "held"

              let reduced = DecoderFuzz.minimise classify "boom" input

              Expect.stringContains reduced marker "the minimiser must preserve the failing feature"

              Expect.isLessThan
                  reduced.Length
                  (input.Length / 4)
                  (sprintf
                      "the minimiser should have removed most of the noise; got %d of %d chars"
                      reduced.Length
                      input.Length)
          }

          test "a counterexample persists a replayable repro" {
              // Exercised here rather than only when a real find occurs: the
              // repro writer runs exactly when something has already gone wrong,
              // which is the worst possible moment to discover a defect in it.
              let dir =
                  Path.Combine(Path.GetTempPath(), "fuaran-fuzz-repro-" + Guid.NewGuid().ToString("N"))

              try
                  let c: DecoderFuzz.Counterexample =
                      { Subject = "decodeNodeObj"
                        Iteration = 42
                        Seed = 7UL
                        ConfigName = "long"
                        Origin = "mutation:truncate"
                        Verdict = DecoderFuzz.Escaped("InvalidOperationException", "boom")
                        Original = "{\"id\":\"a\"}"
                        Minimised = "{\"id\"" }

                  let payloadPath = DecoderFuzz.persistTo dir c

                  Expect.isTrue (File.Exists payloadPath) "the minimised payload is written"
                  Expect.equal (File.ReadAllText payloadPath) "{\"id\"" "the payload is the MINIMISED input, verbatim"

                  let notes = File.ReadAllText(payloadPath.Replace(".input.txt", ".md"))
                  Expect.stringContains notes "--fuzz-replay 7" "the notes carry a runnable replay command"
                  Expect.stringContains notes "reject fixture" "the notes carry the counterexample policy"
              finally
                  if Directory.Exists dir then
                      Directory.Delete(dir, true)
          }

          test "the PRNG is deterministic in its seed" {
              // The replay contract. If this ever fails, every persisted repro's
              // replay line is a lie.
              let draw (seed: uint64) =
                  let rng = DecoderFuzz.Rng(seed)
                  [ for _ in 1..64 -> rng.Next 1000 ]

              Expect.equal (draw 99UL) (draw 99UL) "same seed, same stream"
              Expect.notEqual (draw 99UL) (draw 100UL) "different seeds, different streams"
          } ]

// ─── The bounded gate run ───────────────────────────────────────────────────

/// ONE bounded run, shared by every assertion below. Three separate runs of the
/// same seed would triple this suite's cost to re-derive identical numbers.
let private boundedStats =
    lazy
        (DecoderFuzz.run
            DecoderFuzz.realSubjects
            DecoderFuzz.defaultBudgets
            DecoderFuzz.boundedConfig
            gateSeed
            gateIterations
            true)

[<Tests>]
let boundedRun =
    testList
        "Fuaran.UI.Ops.JsonDecode — decoder robustness fuzz, bounded run (Phase 779)"
        [ test "no hostile input escapes the refusal contract" {
              let stats = boundedStats.Value
              printfn "── decoder fuzz (seed %d): %s ──" gateSeed (DecoderFuzz.summarise stats)

              match stats.Counterexamples with
              | [] -> ()
              | finds ->
                  let report =
                      finds
                      |> List.map (fun c ->
                          let path = DecoderFuzz.persist c

                          sprintf
                              "  %s iteration %d (%s): %s\n    repro: %s"
                              c.Subject
                              c.Iteration
                              c.Origin
                              (DecoderFuzz.describeVerdict c.Verdict)
                              path)
                      |> String.concat "\n"

                  failtestf
                      "the decoder fuzz found %d counterexample(s) at seed %d:\n%s\n\nPolicy: fix the decoder, then land the minimised input as a permanent reject fixture in the shared corpus so every host inherits the case."
                      (List.length finds)
                      gateSeed
                      report
          }

          // ── Coverage: the harness must be reaching the real decoders ──────
          //
          // Without these, a generator regression that emitted nothing but
          // unparseable junk would keep the test above green forever while
          // testing none of the typed decode paths the claim is about.

          test "the bounded run accepts some inputs — it is not just generating junk" {
              Expect.isGreaterThan
                  boundedStats.Value.Accepted
                  0
                  "some generated input must survive to a valid tree, or the fixed-point invariant is never exercised at all"
          }

          test "the bounded run refuses across several distinct error codes" {
              let codes = boundedStats.Value.RejectCodes |> Map.toList |> List.map fst

              Expect.isGreaterThanOrEqual
                  (List.length codes)
                  4
                  (sprintf
                      "the generators should reach past syntax rejection into the typed decoders; saw only %A"
                      codes)

              Expect.contains codes "LIMIT_EXCEEDED" "the pathological family must be crossing the resource limits"
          }

          test "the bounded run reaches the over-closed recovery class" {
              // The class carrying the known super-linear cost. If the
              // generators stop producing it, the run's over-close figures
              // silently become vacuous zeros rather than a measurement.
              Expect.isGreaterThan
                  boundedStats.Value.OverClosedInputs
                  0
                  "no over-closed document was generated, so the quadratic recovery path went unmeasured"
          }

          test "the corpus is actually seeding the run" {
              // The seed pool is the difference between fuzzing the wire format
              // and fuzzing JSON. If the corpus stops being found, this fails
              // rather than the run silently narrowing to nine built-in seeds.
              let seeds = DecoderFuzz.loadSeeds ()

              Expect.isGreaterThan
                  seeds.Length
                  100
                  (sprintf "expected the shared corpus families as seeds; found %d" seeds.Length)
          } ]

// ─── The recorded cost of the over-closed recovery class ────────────────────

[<Tests>]
let overCloseCostProfile =
    // Phase 779 found this and deliberately did not fix it: the remedy is a
    // decision about how far the over-close recovery should reach, not a
    // side-effect a fuzz harness gets to take while looking for something else.
    // What the harness CAN do is stop the property being UNRECORDED — so the
    // measured cost is pinned here, and a regression past the pin fails loudly.
    //
    // Measured (Release), a node list with one surplus closer mid-document.
    // Every row DECODES SUCCESSFULLY: this is the cost of the success path.
    //
    // |  input | allocated |   ms |
    // |--------|-----------|------|
    // |    899 |   0.5 MiB |  0.5 |
    // |  2 129 |   2.3 MiB |  2.8 |
    // |  4 179 |   8.1 MiB |  8.6 |
    // |  8 280 |  30.1 MiB | 22.3 |
    // | 16 580 | 115.9 MiB | 81.9 |
    //
    // The pin is a CEILING only. A future fix makes the numbers fall and this
    // keeps passing — a test that failed on the defect being fixed would be
    // worse than no test at all.
    let overClosedList (n: int) =
        let child i =
            sprintf """{"id":"c%d","kind":{"$type":"Heading","level":1,"text":"x","variant":"Standard"}}""" i

        let children =
            [ for i in 1..n -> if i = n / 2 then child i + "}" else child i ]
            |> String.concat ","

        sprintf
            """{"id":"r","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Group"}}"""
            children

    let measureAlloc (json: string) : int64 * Result<unit, string> =
        Fuaran.UI.Ops.JsonDecode.decodeNodeObj json |> ignore // warm, so JIT is not measured
        let before = GC.GetAllocatedBytesForCurrentThread()

        let outcome =
            match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
            | Ok _ -> Ok()
            | Error e -> Error e.Code

        GC.GetAllocatedBytesForCurrentThread() - before, outcome

    testList
        "Fuaran.UI.Ops.JsonDecode — over-closed recovery cost, pinned (Phase 779)"
        [ test "the harness classifies an over-closed document as over-closed" {
              // The routing predicate the two-tier budget rests on. If this
              // stopped working, the quadratic class would be judged against the
              // ordinary ceiling and every run would fail for the wrong reason —
              // or, worse, the reverse.
              Expect.isTrue (DecoderFuzz.isOverClosed (overClosedList 4)) "a surplus closer is detected"

              Expect.isFalse
                  (DecoderFuzz.isOverClosed "{\"a\":[1,2],\"b\":\"}]\"}")
                  "closers inside a string are not structural"

              Expect.isFalse (DecoderFuzz.isOverClosed "{\"a\":1}") "a balanced document is not over-closed"
          }

          test "an 8 KB over-closed document still recovers, and costs no more than the pin" {
              let json = overClosedList 100
              let allocated, outcome = measureAlloc json

              Expect.equal outcome (Ok()) "the over-close recovery still accepts this document"

              // 64 MiB against 30.1 MiB measured: enough headroom that a busy
              // machine cannot trip it, tight enough that another doubling of
              // the cost is caught.
              Expect.isLessThan
                  allocated
                  (64L * 1024L * 1024L)
                  (sprintf
                      "recovering a %d-char over-closed document allocated %d bytes (%.0f x input); the recorded figure is 30.1 MiB"
                      json.Length
                      allocated
                      (float allocated / float json.Length))
          }

          test "the cost of the class is reported, not merely bounded" {
              // Phrased as an inequality that stays true under ANY fix — the
              // smaller document must not cost more than the larger — so this
              // records the shape without demanding the defect persist. The
              // printed ratio is the finding: a 3.9x input for a ~13x cost.
              let small, _ = measureAlloc (overClosedList 25)
              let large, _ = measureAlloc (overClosedList 100)

              Expect.isLessThan small large "the smaller document must not cost more than the larger"

              printfn
                  "── over-close recovery cost: 2 KB -> %d bytes, 8 KB -> %d bytes (%.1f x cost for a 3.9 x input) ──"
                  small
                  large
                  (float large / float small)
          } ]
