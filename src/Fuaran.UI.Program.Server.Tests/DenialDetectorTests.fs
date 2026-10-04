module Fuaran.UI.Program.Server.Tests.DenialDetectorTests

// ─── Denial-pattern detection over the recorded denial sink ──────────────────
//
// Four claims, each stated in the form that can go red rather than the form that
// reads well:
//
//  1. A BURST OF ENVELOPE-OUTSIDE DENIALS TRIPS IT, AND THE RECORDED REASON
//     NAMES THE PATTERN. Not "a suspend entry exists" — the entry's own reason
//     is asserted to carry the capability reached for and the count, because the
//     entry is what an auditor reads months later and a detector that suspended
//     with a generic reason would have recorded that something happened and not
//     what.
//
//  2. ORDINARY DENIALS NEVER TRIP IT, AT ANY DENSITY. Asserted with MORE
//     ordinary denials than the threshold, not with a sparse handful: "sparse
//     denials do not trip it" would also be satisfied by a detector that counted
//     both classes together and simply had not been pushed hard enough.
//
//  3. REPORT-ONLY REPORTS AND DOES NOT SUSPEND. Both halves, from the same
//     burst that suspends in claim 1 — so the two modes are shown to differ in
//     what they DO and to agree about what they SAW.
//
//  4. A SESSION WITH NO ENVELOPE CLASSIFIES EVERY DENIAL AS ORDINARY — and so
//     does one whose document carries no server tier. The second is the half a
//     future edit is most likely to get wrong, because "demands nothing" and "no
//     walk was performed" look alike from inside a match and mean opposite
//     things here: collapsing them would make every denial on a client-tier
//     document an intrusion signal.
//
// ── What claim 1 is run through, and why ─────────────────────────────────────
// The burst arrives through `Handler.run` against a real registry rather than by
// poking the sink, because a denial HALTS a handler — so the probing shape is
// one refused effect per dispatch, repeated, and a suite that drove the sink
// directly would never have noticed that. The finer-grained claims below do
// drive the sink, where the arithmetic is the subject.

open Expecto
open Fuaran.UI
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

// ─── fixtures ────────────────────────────────────────────────────────────────

let private scope = "session-under-watch"

/// What the program DECLARED it would ask for: one notification channel.
let private declared: Handler =
    { Name = "declared"
      Stages = [ Effect(ServerEffect.Notify("ops", Fuaran.Core.JObj [])) ] }

/// What it actually reaches for: a host function its envelope never named.
let private probing: Handler =
    { Name = "probe"
      Stages = [ Effect(ServerEffect.HostCall("exfiltrate", Fuaran.Core.JObj [], None)) ] }

let private envelope: DemandedProjection = ServerDemanded.ofHandler declared

let private store: ServerStore =
    { Tree = Fuaran.markdown "root" "watched"
      Bindings = empty }

let private sources: string -> Result<Fuaran.Core.Table, Fuaran.Compute.EvalError> =
    Fuaran.Compute.DataFrame.noResolve

/// A session's controls over a fresh in-memory stream, returned with the stream
/// so a test can read back what was recorded.
let private controlled () =
    let journal = Controls.inMemory ()
    journal, ControlServices.create scope |> ControlServices.withJournal journal

/// The detector's report sink, as a list a test can read.
let private recording () =
    let seen = ResizeArray<DenialPattern>()
    seen, (fun pattern -> seen.Add pattern)

/// Denials of a capability the envelope never named.
let private outside = ServerEffectDenial.GateRefused "host:exfiltrate"

/// A denial of a capability it DID name.
let private inside = ServerEffectDenial.GateRefused "Notify"

// ─── tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "denial-pattern detection"
        [ test "a burst of envelope-outside denials suspends the session, and the reason names the pattern" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope envelope
                  |> DenialDetector.withThreshold 3
                  |> DenialDetector.onPattern sink
                  |> DenialDetector.suspending

              // The registry refuses everything, so each dispatch of the probing
              // handler produces exactly one denial — a denial halts a handler,
              // which is why a probe is repeated dispatches and not one big one.
              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              for _ in 1..2 do
                  Handler.run watched sources "call" probing store |> ignore

              Expect.isFalse
                  (Controls.stateOf journal scope |> Controls.isSuspended)
                  "two denials are a coincidence: below the threshold nothing is suspended"

              Expect.isEmpty patterns "and nothing is reported either"

              Handler.run watched sources "call" probing store |> ignore

              Expect.isTrue
                  (Controls.stateOf journal scope |> Controls.isSuspended)
                  "the third crosses the threshold and the session is suspended"

              let entries = journal.Read scope
              Expect.hasLength entries 1 "one act, recorded once"

              let entry = List.head entries
              Expect.equal entry.Op ControlOp.Suspend "and it is a suspend, through the existing vocabulary"

              Expect.equal
                  entry.Actor
                  DenialDetector.defaultActor
                  "raised by a MACHINE actor — the distinction D16's actor exists to carry"

              Expect.stringContains
                  entry.Reason
                  "host:exfiltrate"
                  "the recorded reason names the capability that was reached for"

              Expect.stringContains entry.Reason "envelope-outside" "and the class that makes it a pattern"

              Expect.stringContains entry.Reason scope "and the session it was counted for"

              Expect.hasLength patterns 1 "the pattern was reported exactly once"

              Expect.equal patterns[0].Capabilities [ "host:exfiltrate" ] "naming the capabilities, distinct and sorted"

              Expect.equal patterns[0].EnvelopeOutside 3 "with the count that breached"
          }

          test "the report precedes the act — a report-only detector sees the same pattern and suspends nothing" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope envelope
                  |> DenialDetector.withThreshold 3
                  |> DenialDetector.onPattern sink

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              for _ in 1..3 do
                  Handler.run watched sources "call" probing store |> ignore

              Expect.hasLength patterns 1 "the same burst reports the same pattern"

              Expect.equal
                  patterns[0].Capabilities
                  [ "host:exfiltrate" ]
                  "describing exactly what a suspending detector would have acted on"

              Expect.isEmpty (journal.Read scope) "and nothing at all is recorded on the control stream"

              Expect.isFalse
                  (Controls.stateOf journal scope |> Controls.isSuspended)
                  "so the session runs on — report-only never suspends"
          }

          test "report-only is the DEFAULT; suspending is the deliberate act" {
              let _, controls = controlled ()

              Expect.equal
                  (DenialDetector.create controls).Response
                  DenialResponse.ReportOnly
                  "a detector wired without asking for a hand does not get one"

              Expect.equal
                  (DenialDetector.create controls |> DenialDetector.suspending).Response
                  DenialResponse.Suspend
                  "and the consequential behaviour is named rather than inherited"
          }

          test "ordinary denials never trip it, however many arrive" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope envelope
                  |> DenialDetector.withThreshold 3
                  |> DenialDetector.onPattern sink
                  |> DenialDetector.suspending

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              // Twice the threshold, deliberately: a detector that counted both
              // classes into one number would fail here and pass a sparse test.
              for _ in 1..6 do
                  watched.OnDenied inside

              Expect.isEmpty patterns "a program being refused what it DID claim is a policy working, not a probe"

              Expect.isEmpty (journal.Read scope) "so nothing is recorded"

              Expect.equal (DenialDetector.classify detector inside) DenialClass.Ordinary "and the class itself says so"

              Expect.equal
                  (DenialDetector.classify detector outside)
                  DenialClass.EnvelopeOutside
                  "while a capability outside the envelope is the other class"
          }

          test "a session with no envelope classifies every denial as ordinary" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withThreshold 2
                  |> DenialDetector.onPattern sink
                  |> DenialDetector.suspending

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              for _ in 1..5 do
                  watched.OnDenied outside

              Expect.equal
                  (DenialDetector.classify detector outside)
                  DenialClass.Ordinary
                  "with nothing to be outside OF, no denial can be outside it"

              Expect.isEmpty patterns "so nothing is reported"
              Expect.isEmpty (journal.Read scope) "and nothing is suspended"
          }

          test "a document with no SERVER TIER is 'not asked', not 'asked and demands nothing'" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              // `Demanded.empty` carries `Server = None` — no server walk was
              // performed. Reading that as an empty demand would make every
              // denial envelope-outside, which is the reading the projection's
              // own None-versus-empty note forbids.
              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope Demanded.empty
                  |> DenialDetector.withThreshold 2
                  |> DenialDetector.onPattern sink
                  |> DenialDetector.suspending

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              for _ in 1..5 do
                  watched.OnDenied outside

              Expect.isEmpty patterns "a document that never described this host's server tier accuses nobody"
              Expect.isEmpty (journal.Read scope) "and suspends nobody"
          }

          test "the window is consumed by a breach, so a second burst is a second breach" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope envelope
                  |> DenialDetector.withThreshold 3
                  |> DenialDetector.onPattern sink
                  |> DenialDetector.suspending

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              for _ in 1..6 do
                  watched.OnDenied outside

              // A detector that LATCHED would report once and be deaf
              // afterwards — so an operator's resume would hand a probing
              // session a permanently silent watcher.
              Expect.hasLength patterns 2 "six denials at a threshold of three are two patterns, not one"
              Expect.hasLength (journal.Read scope) 2 "each recorded as its own act"

              Expect.equal patterns[1].EnvelopeOutside 3 "the second window counts from zero, not from four"
          }

          test "the host's own denial sink is composed, never replaced" {
              let _, controls = controlled ()
              let heard = ResizeArray<ServerEffectDenial>()

              let watched =
                  ServerEffectRegistry.denyAll
                  |> ServerEffectRegistry.onDenied heard.Add
                  |> DenialDetector.watching (
                      DenialDetector.create controls
                      |> DenialDetector.withEnvelope envelope
                      |> DenialDetector.withThreshold 2
                      |> DenialDetector.suspending
                  )

              watched.OnDenied outside
              watched.OnDenied inside

              Expect.sequenceEqual
                  (List.ofSeq heard)
                  [ outside; inside ]
                  "wiring a detector must never cost a host the logging it already had"
          }

          test "an indeterminate window open at the breach is recorded WITH the suspend" {
              let journal, controls = controlled ()

              let window: MidStageWindow =
                  { Invocation = "inv-7"
                    Step = 2
                    Capability = "host:exfiltrate" }

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope envelope
                  |> DenialDetector.withThreshold 1
                  |> DenialDetector.withMidStage (fun () -> [ window ])
                  |> DenialDetector.suspending

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector
              watched.OnDenied outside

              let entry = journal.Read scope |> List.head

              // The detector fires from INSIDE an invocation, so a window can be
              // open — and D12's whole claim is that a resume must know it is
              // crossing one rather than discover it afterwards.
              Expect.equal
                  entry.MidStage
                  [ window ]
                  "the window the host supplied travels on the act that closed over it"
          }

          test "a threshold below one is read as one, not as 'breach before any denial'" {
              let journal, controls = controlled ()
              let patterns, sink = recording ()

              let detector =
                  DenialDetector.create controls
                  |> DenialDetector.withEnvelope envelope
                  |> DenialDetector.withThreshold 0
                  |> DenialDetector.onPattern sink
                  |> DenialDetector.suspending

              let watched = ServerEffectRegistry.denyAll |> DenialDetector.watching detector

              Expect.isEmpty patterns "nothing is reported before a denial arrives"

              watched.OnDenied outside

              Expect.hasLength patterns 1 "and the first one is the breach"
              Expect.equal patterns[0].Threshold 1 "reported as the threshold that was actually applied"
              Expect.isTrue (Controls.stateOf journal scope |> Controls.isSuspended) "which suspends"
          } ]
