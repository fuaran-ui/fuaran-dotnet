module Fuaran.UI.Tests.NowGrain

// ============================================================================
//  The host instant: `Binding.Now`'s declared grain and `Format.Since`
//  (Phase 1533, WIRE_FORMAT.md §3.3.1).
//
//  Three things are pinned here, and they are three different claims:
//
//  1. TRUNCATION — the grain table is a prefix operation on the canonical
//     instant form and nothing else. Pinned exhaustively, because it is what
//     every host has to reproduce; a host that reached for its own date library
//     would pass the round-trip corpus and fail this.
//
//  2. THE `Since` REDUCTION — the `(unit, count)` pair the auto-selection ladder
//     yields, INCLUDING both sides of every boundary. The reduction is the
//     normative half of `Format.Since`; the phrasing is each host's own locale
//     tier (§13), which is why these assert the pair and the .NET fallback's
//     wording separately.
//
//  3. DETERMINISM — resolution is a pure function of `BindingSources`. Stated as
//     a test rather than as a comment, because "no host reads a clock" is
//     exactly the kind of claim that decays silently: the two tests at the end
//     resolve the SAME tree twice with the same furnished instant and once with
//     a different one, and assert the output tracks the SOURCES and not the
//     wall clock. A host that reached for `DateTime.UtcNow` inside the resolver
//     would pass every other test in this file.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

/// The instant every case below is measured against — a fixed, arbitrary,
/// second-precision UTC point. Fixed is the whole point: nothing here may vary
/// with when the suite runs.
let private pinned = "2026-08-02T06:59:24Z"

let private sourcesAt (instant: string) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        Now = instant }

/// The plainest "one labelled datum" node, carrying `b` in its value slot — the
/// shape both the byte test and the replay test need.
let private factWith (b: Binding<string>) : Node<obj> =
    let n: Node<obj> = Fuaran.fact "asof" "Today" ""

    { n with
        Kind =
            NodeKind.Fact(
                { Defaults.fact with
                    Label = TextSource.Literal "Today"
                    Value = TextSource.Bound b }
            ) }

let private resolveStr (sources: BindingResolver.BindingSources) (b: Binding<string>) : string =
    match BindingResolver.resolve sources b with
    | BindingResolver.Resolved s -> s
    | other -> failtestf "Expected Resolved, got %A" other

[<Tests>]
let tests =
    testList
        "Binding.Now grain + Format.Since (Phase 1533)"
        [ // ── 1. Truncation ────────────────────────────────────────────────
          test "the grain table truncates by prefix, and Second is the identity" {
              Expect.equal
                  (Formatting.truncateToGrain TimeGrain.Second pinned)
                  "2026-08-02T06:59:24Z"
                  "Second is the identity — it is the DEFAULT grain, so a document declaring none must resolve through exactly the bytes Phase 765 shipped"

              Expect.equal
                  (Formatting.truncateToGrain TimeGrain.Minute pinned)
                  "2026-08-02T06:59:00Z"
                  "Minute zero-fills the seconds and stays a well-formed instant"

              Expect.equal
                  (Formatting.truncateToGrain TimeGrain.Hour pinned)
                  "2026-08-02T06:00:00Z"
                  "Hour zero-fills minutes and seconds"

              Expect.equal
                  (Formatting.truncateToGrain TimeGrain.Day pinned)
                  "2026-08-02"
                  "Day drops the time entirely — this is the YYYY-MM-DD that Core's DateDiffDays reads, which is the whole reason the field exists"
          }

          test "a sub-second instant truncates identically, and Second keeps its precision" {
              let fractional = "2026-08-02T06:59:24.512Z"

              Expect.equal
                  (Formatting.truncateToGrain TimeGrain.Second fractional)
                  fractional
                  "Second passes the host's instant through verbatim, sub-second precision included"

              Expect.equal (Formatting.truncateToGrain TimeGrain.Minute fractional) "2026-08-02T06:59:00Z" "Minute"
              Expect.equal (Formatting.truncateToGrain TimeGrain.Day fractional) "2026-08-02" "Day"
          }

          test "an instant too short to slice is passed through verbatim, never padded" {
              // A host-furnished value is not wire data, and a renderer is the
              // wrong place to adjudicate a host's clock format: the result is a
              // visibly odd date rather than a silently plausible wrong one.
              Expect.equal
                  (Formatting.truncateToGrain TimeGrain.Hour "2026-08-02")
                  "2026-08-02"
                  "shorter than the Hour slice"

              Expect.equal (Formatting.truncateToGrain TimeGrain.Day "") "" "empty"
          }

          test "resolution applies the grain BEFORE the accessor, not after" {
              // Before, not after: the accessor is the document's projection onto
              // its slot type, so a Transform param projecting to a string would
              // otherwise carry a full datetime into DateDiffDays, which reads
              // only the leading YYYY-MM-DD.
              let seen = ResizeArray<string>()

              let probe: Binding<string> =
                  Binding.Now(
                      (fun (o: obj) ->
                          let s = unbox<string> o
                          seen.Add s
                          s),
                      Some TimeGrain.Day
                  )

              Expect.equal (resolveStr (sourcesAt pinned) probe) "2026-08-02" "the resolved value"
              Expect.sequenceEqual seen [ "2026-08-02" ] "the ACCESSOR saw the truncated instant, not the full one"
          }

          test "an absent grain resolves through the untouched instant" {
              Expect.equal (resolveStr (sourcesAt pinned) binding.now) pinned "no grain declared ⇒ Second ⇒ identity"
          }

          test "nowAt Second and the bare now are the same bytes on the wire" {
              // The smart constructor collapses the default to `None`, so
              // `nowAt Second` cannot mint a document that says `"grain":"Second"`
              // — which would round-trip differently from every pre-1533 tree.
              let encoded (b: Binding<string>) = CanonicalJson.encodeNode (factWith b)

              Expect.equal
                  (encoded (binding.nowAt TimeGrain.Second))
                  (encoded binding.now)
                  "the default grain is omitted, so the bytes are Phase 765's"

              Expect.stringContains
                  (encoded (binding.nowAt TimeGrain.Day))
                  "\"grain\":\"Day\""
                  "a non-default grain DOES ride the wire"
          }

          test "an unfurnished instant is NotResolved at every grain — never a substituted one" {
              for g in [ TimeGrain.Second; TimeGrain.Minute; TimeGrain.Hour; TimeGrain.Day ] do
                  let b: Binding<string> = Binding.Now((fun (o: obj) -> unbox<string> o), Some g)

                  match BindingResolver.resolve BindingResolver.empty b with
                  | BindingResolver.NotResolved -> ()
                  | other -> failtestf "grain %A: expected NotResolved, got %A" g other
          }

          // ── 2. The `Since` reduction ─────────────────────────────────────
          test "the auto-selection ladder picks the largest unit that fits, on BOTH sides of every boundary" {
              let cases =
                  [ -1.0, (RelativeTimeUnit.Second, -1.0)
                    -59.0, (RelativeTimeUnit.Second, -59.0)
                    -60.0, (RelativeTimeUnit.Minute, -1.0)
                    -3599.0, (RelativeTimeUnit.Minute, -59.0)
                    -3600.0, (RelativeTimeUnit.Hour, -1.0)
                    -86399.0, (RelativeTimeUnit.Hour, -23.0)
                    -86400.0, (RelativeTimeUnit.Day, -1.0)
                    -604799.0, (RelativeTimeUnit.Day, -6.0)
                    -604800.0, (RelativeTimeUnit.Week, -1.0)
                    -2629745.0, (RelativeTimeUnit.Week, -4.0)
                    -2629746.0, (RelativeTimeUnit.Month, -1.0)
                    -31556951.0, (RelativeTimeUnit.Month, -11.0)
                    -31556952.0, (RelativeTimeUnit.Year, -1.0)
                    // The future direction is the same ladder with the sign kept.
                    7200.0, (RelativeTimeUnit.Hour, 2.0)
                    0.0, (RelativeTimeUnit.Second, 0.0) ]

              for delta, expected in cases do
                  Expect.equal (Formatting.sinceUnitAndCount None delta) expected (sprintf "delta %f" delta)
          }

          test "the count truncates toward zero — 3599 seconds is 59 minutes, never 1 hour" {
              // Truncation rather than rounding, so the ladder and the count
              // agree at every boundary. Rounding would break them exactly where
              // a reader is most likely to check.
              Expect.equal
                  (Formatting.sinceUnitAndCount None -3599.0)
                  (RelativeTimeUnit.Minute, -59.0)
                  "just under the hour"

              Expect.equal
                  (Formatting.sinceUnitAndCount (Some RelativeTimeUnit.Hour) -3599.0)
                  (RelativeTimeUnit.Hour, 0.0)
                  "a DECLARED unit skips the ladder and truncates in that unit — 'this hour', not 'an hour ago'"
          }

          test "a declared unit is used verbatim, however large the delta" {
              Expect.equal
                  (Formatting.sinceUnitAndCount (Some RelativeTimeUnit.Day) -31556952.0)
                  (RelativeTimeUnit.Day, -365.0)
                  "declaring Day over a year's delta says 365 days, not 1 year"
          }

          test "the instant parser reads the canonical form, and refuses what is not one" {
              // The epoch of the Unix epoch, and one exactly a day later: a
              // hand-checkable pair, so an arithmetic slip in days-from-civil
              // cannot hide behind a plausible-looking large number.
              Expect.equal (Formatting.epochSecondsOfInstant "1970-01-01T00:00:00Z") (Some 0.0) "the epoch"
              Expect.equal (Formatting.epochSecondsOfInstant "1970-01-02") (Some 86400.0) "date-only form, one day on"
              Expect.equal (Formatting.epochSecondsOfInstant "1970-01-01T00:01:05Z") (Some 65.0) "time components"

              // A leap day the proleptic-Gregorian rules must get right.
              Expect.equal (Formatting.epochSecondsOfInstant "2000-02-29") (Some 951782400.0) "a century leap day"

              for bad in [ ""; "not-a-date"; "20260802"; "0000-01-01"; "2026-13-01"; "2026-08-32" ] do
                  Expect.equal (Formatting.epochSecondsOfInstant bad) None ("refused: '" + bad + "'")
          }

          test "Format.Since resolves against the HOST instant, and reads its source as an instant" {
              // Source is three hours BEFORE the pinned instant, in whole
              // Unix-epoch seconds (Format.Date's convention).
              let threeHoursAgo =
                  match Formatting.epochSecondsOfInstant pinned with
                  | Some e -> e - 10800.0
                  | None -> failtest "the pinned instant must parse"

              let b: Binding<string> =
                  Binding.Format(Binding.Static(Some threeHoursAgo), Format.Since None, LocaleSource.Ambient)

              Expect.equal
                  (resolveStr (sourcesAt pinned) b)
                  "3 hours ago"
                  "the .NET fallback's wording over the normative (Hour, -3) reduction"
          }

          test "Format.Since with no host instant is NotResolved — never a delta against an invented now" {
              let b: Binding<string> =
                  Binding.Format(Binding.Static(Some 1700000000.0), Format.Since None, LocaleSource.Ambient)

              match BindingResolver.resolve BindingResolver.empty b with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "Expected NotResolved, got %A" other
          }

          test "Format.RelativeTime's count semantics are NOT widened by Since" {
              // The two cases read their source differently on purpose: a
              // RelativeTime source is already a signed COUNT, and conflating
              // them would silently re-interpret every shipped document.
              let asCount: Binding<string> =
                  Binding.Format(
                      Binding.Static(Some -3.0),
                      Format.RelativeTime RelativeTimeUnit.Hour,
                      LocaleSource.Ambient
                  )

              Expect.equal
                  (resolveStr (sourcesAt pinned) asCount)
                  "3 hours ago"
                  "-3 is THREE HOURS, not three seconds before the epoch — the host instant is not consulted at all here"
          }

          // ── 3. Determinism ───────────────────────────────────────────────
          test "resolution is a pure function of the sources — the same instant renders the same text" {
              let subject =
                  match Formatting.epochSecondsOfInstant pinned with
                  | Some e -> e - 7200.0
                  | None -> failtest "the pinned instant must parse"

              let tree: Binding<string> list =
                  [ binding.nowAt TimeGrain.Day
                    binding.nowAt TimeGrain.Minute
                    Binding.Format(Binding.Static(Some subject), Format.Since None, LocaleSource.Ambient) ]

              let renderPass () =
                  tree |> List.map (resolveStr (sourcesAt pinned))

              // Two passes separated by real elapsed time. If ANY host arm read
              // a clock rather than `sources.Now`, these two lists could differ.
              let first = renderPass ()
              System.Threading.Thread.Sleep 5
              let second = renderPass ()

              Expect.sequenceEqual second first "two render passes over one furnished instant agree"

              Expect.sequenceEqual
                  first
                  [ "2026-08-02"; "2026-08-02T06:59:00Z"; "2 hours ago" ]
                  "and they agree with the values the pinned instant determines"
          }

          test "a re-render under a DIFFERENT furnished instant moves — which is what makes the test above meaningful" {
              // The go-red twin of the determinism test: if resolution ignored
              // `sources.Now` entirely, the pair above would pass vacuously.
              let later = "2026-08-03T09:30:00Z"

              Expect.equal (resolveStr (sourcesAt pinned) (binding.nowAt TimeGrain.Day)) "2026-08-02" "the pinned pass"

              Expect.equal
                  (resolveStr (sourcesAt later) (binding.nowAt TimeGrain.Day))
                  "2026-08-03"
                  "a later furnished instant"
          }

          test "a replayed op-stream reproduces its ORIGINAL render, whatever the wall clock says at replay" {
              // The op-stream carries no instant, so replay means re-rendering
              // the recorded tree against the recorded instant. Nothing about
              // the tree, its encoding or the apply engine can reintroduce one —
              // which is exactly why the tree round-trips through canonical JSON
              // here rather than being re-used as a value.
              let recordedInstant = pinned

              let tree = factWith (binding.nowAt TimeGrain.Day)
              let json = CanonicalJson.encodeNode tree

              let decoded =
                  match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
                  | Ok n -> n
                  | Error e -> failtestf "the recorded tree must decode: %A" e

              let valueOf (n: Node<obj>) =
                  match n.Kind with
                  | NodeKind.Fact spec ->
                      match spec.Value with
                      | TextSource.Bound b -> resolveStr (sourcesAt recordedInstant) b
                      | other -> failtestf "expected a Bound value, got %A" other
                  | other -> failtestf "expected a Fact, got %A" other

              Expect.equal (valueOf decoded) "2026-08-02" "the replayed render is the original render"
              Expect.equal (valueOf decoded) (valueOf tree) "and it agrees with the tree as authored"
          } ]
