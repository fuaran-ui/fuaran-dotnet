module Fuaran.Program.Server.Tests.ReplayTests

// ─── Replay reasons, mode enforcement, and the projection join ───────────────
//
// The classification itself is certified against the conformance corpus, which
// pins one verdict per handler vector. This suite pins the three things the
// corpus does not, and one that keeps it honest:
//
//  1. WHY, NOT JUST WHETHER — a non-`safe` handler names the STAGE and the
//     defect that forced it, from a closed vocabulary, with no string the
//     handler document supplied.
//  2. THE TWO MODES ARE NOT INTERCHANGEABLE — audit admits recorded ops and
//     nothing else, unconditionally and without consulting a handler at all;
//     resume refuses an `unsafe` handler with a typed code, admits an `unknown`
//     one carrying its reasons, and records an override when one is used.
//  3. THE POSTURE REACHES THE DOCUMENT — the demanded projection carries one
//     posture per reachable handler, and the version moved with the shape.
//  4. THAT THE WALK CAN GO RED — the classifier DISCRIMINATES: the same handler
//     with an absolutely-addressed op and with a relatively-addressed one
//     classifies differently and names the difference. A walk that had been
//     flattened to "safe" would pass every test that only ever looked at one of
//     the two, and this is the one that would not.
//
// ── The one rule this suite must never quietly relax ─────────────────────────
// `unknown` RESUMES. The specification refuses `unsafe` and only `unsafe`, and
// rounding an honest "no proof available" up to a proof of harm is exactly what
// the three-valued classification exists to prevent. A future edit that refuses
// `unknown` fails here, on purpose.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.Program.Bounded
open Fuaran.Program.UI
open Fuaran.Program.Server
open Fuaran.Program.Server.UI

// ─── fixtures ────────────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private endpoint = "/handlers/work"

let private handlerOf (stages: HandlerStage list) : Handler = { Name = "work"; Stages = stages }

/// A tree whose one button calls `target`.
let private treeCalling (target: string) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "call"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "call"
                          OnClick = Action.Call(target, None, None) } ] }

/// An op that names the node it addresses.
let private absolute = TreeOp.RemoveNode(NodeId "orders-empty")

/// An op that addresses by POSITION within a parent rather than by naming a
/// node — the case the classification cannot decide, and the one the probe at
/// the foot of this file turns on.
let private relative =
    TreeOp.ReorderChildren(NodeId "stack-1", [ NodeId "a"; NodeId "b" ])

let private defectsOf (handler: Handler) =
    HandlerWire.replayReasons handler
    |> List.map (fun r -> r.Stage, ProgramWire.replayDefectTag r.Defect)

let private tagOf (handler: Handler) =
    ProgramWire.replaySafetyTag (HandlerWire.replaySafety handler)

/// The three postures, each from a handler that is minimally that thing.
let private safeHandler =
    handlerOf
        [ Effect(
              ServerEffect.RunQuery("slot", Fuaran.Core.Embedded({ Schema = []; Columns = [] }: Fuaran.Core.Table), [])
          )
          Compute(Action.SetState("status", Some(jstr "loaded"), None))
          Effect(ServerEffect.ApplyOps [ absolute ]) ]

let private unknownHandler =
    handlerOf [ Effect(ServerEffect.ApplyOps [ relative ]) ]

let private unsafeHandler =
    handlerOf
        [ Effect(ServerEffect.ApplyOps [ absolute ])
          Effect(ServerEffect.HostCall("sendMail", jstr "x", None))
          Effect(ServerEffect.Notify("ops", jstr "n")) ]

// ─── the suite ───────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 988 — replay reasons, mode enforcement, and the projection join"
        [
          // ── why, not just whether ──────────────────────────────────

          test "a safe handler carries NO reasons, and that is what makes it safe" {
              Expect.equal (tagOf safeHandler) "safe" "the verdict"
              Expect.isEmpty (HandlerWire.replayReasons safeHandler) "nothing to explain"
          }

          test "an unsafe handler names every stage that forced it, and what each lacks" {
              Expect.equal (tagOf unsafeHandler) "unsafe" "the verdict"

              Expect.equal
                  (defectsOf unsafeHandler)
                  [ 1, "opaque-host-call"; 2, "outbound-notification" ]
                  "the two outward-reaching stages, by position, in stage order"
          }

          test "an undecidable handler is named too — reasons are not only for a refusal" {
              Expect.equal (tagOf unknownHandler) "unknown" "the verdict"

              Expect.equal (defectsOf unknownHandler) [ 0, "relative-addressing" ] "the undecidable stage, named"
          }

          test "a non-literal write names the stage it sits in" {
              let handler =
                  handlerOf
                      [ Compute(Action.SetState("a", Some(jstr "x"), None))
                        Compute(
                            Action.SetState(
                                "chosen",
                                None,
                                Some(
                                    Binding.Selection(
                                        "orders-grid",
                                        Binding.projectSelectionField<Fuaran.Core.JVal> "id",
                                        None,
                                        Some "id"
                                    )
                                )
                            )
                        ) ]

              Expect.equal (tagOf handler) "unknown" "the verdict"
              Expect.equal (defectsOf handler) [ 1, "non-literal-write" ] "the second stage, not the first"
          }

          test "reasons are DISTINCT within a stage and repeated ACROSS stages" {
              // Nine undecidable ops in one effect is one fact about that
              // stage; the same defect in two stages is two places to look.
              let oneStage =
                  handlerOf [ Effect(ServerEffect.ApplyOps [ relative; relative; relative ]) ]

              let twoStages =
                  handlerOf
                      [ Effect(ServerEffect.ApplyOps [ relative ])
                        Effect(ServerEffect.EmitPatch [ relative ]) ]

              Expect.equal (defectsOf oneStage) [ 0, "relative-addressing" ] "one stage, one reason"

              Expect.equal
                  (defectsOf twoStages)
                  [ 0, "relative-addressing"; 1, "relative-addressing" ]
                  "two stages, two reasons"
          }

          test "the classification IS the verdict of its reasons" {
              // Not a tautology to assert: the two were separate walks before,
              // and a reasons list that disagreed with the verdict beside it
              // would be a set of explanations for a conclusion nobody reached.
              for handler in [ safeHandler; unknownHandler; unsafeHandler; handlerOf [] ] do
                  Expect.equal
                      (HandlerWire.replaySafety handler)
                      (ProgramWire.verdictOfReasons (HandlerWire.replayReasons handler))
                      "the verdict is derived from the reasons, not computed beside them"
          }

          // ── the two modes ─────────────────────────────────────────

          test "AUDIT admits recorded ops only — unconditionally, whatever the handler declared" {
              for policy in [ Replay.strict; Replay.acceptingUnsafeResume ] do
                  for handler in [ safeHandler; unknownHandler; unsafeHandler ] do
                      let decision = Replay.admit ReplayMode.Audit policy handler

                      Expect.equal
                          decision.Admission
                          ReplayAdmission.OpsOnly
                          "an audit replay is effect-free whatever the handler is, and whatever the policy says"

                      Expect.isNone decision.Record "and there is nothing to record, because nothing was overridden"
          }

          test "RESUME of an unsafe handler is refused with the typed code and the reasons" {
              let decision = Replay.admit ReplayMode.Resume Replay.strict unsafeHandler

              match decision.Admission with
              | ReplayAdmission.Refused refusal ->
                  Expect.equal refusal.Code ReplayCode.ResumeUnsafeHandler "the typed code"
                  Expect.equal refusal.Safety ReplaySafety.Unsafe "and the verdict that forced it"

                  Expect.equal
                      (refusal.Reasons
                       |> List.map (fun r -> r.Stage, ProgramWire.replayDefectTag r.Defect))
                      [ 1, "opaque-host-call"; 2, "outbound-notification" ]
                      "carrying what to go and look at"
              | other -> failtestf "expected a refusal, got %A" other

              Expect.isNone decision.Record "a refusal is not an override"
          }

          test "RESUME of an UNDECIDABLE handler proceeds, carrying its reasons" {
              // The rule this suite exists to protect: only a proof is a
              // finding, so `unknown` is never rounded up to `unsafe`.
              let decision = Replay.admit ReplayMode.Resume Replay.strict unknownHandler

              match decision.Admission with
              | ReplayAdmission.ReEvaluateReads reasons ->
                  Expect.equal
                      (reasons |> List.map (fun r -> ProgramWire.replayDefectTag r.Defect))
                      [ "relative-addressing" ]
                      "admitted, and the host can still see why it was undecided"
              | other -> failtestf "an undecidable handler must not be refused, got %A" other

              Expect.isNone decision.Record "nothing was overridden — it was never refused"
          }

          test "RESUME of a safe handler proceeds with nothing to explain" {
              let decision = Replay.admit ReplayMode.Resume Replay.strict safeHandler

              Expect.equal decision.Admission (ReplayAdmission.ReEvaluateReads []) "reads re-evaluated, no reasons"
              Expect.isNone decision.Record "and nothing to record"
          }

          test "an explicitly-configured host may resume an unsafe handler, and MUST record that it did" {
              let decision =
                  Replay.admit ReplayMode.Resume Replay.acceptingUnsafeResume unsafeHandler

              match decision.Admission with
              | ReplayAdmission.ReEvaluateReads reasons ->
                  Expect.isNonEmpty reasons "the reasons travel with the override"
              | other -> failtestf "an explicitly-configured host is admitted, got %A" other

              match decision.Record with
              | Some record ->
                  Expect.equal record.Handler "work" "the record names the handler it was made for"
                  Expect.equal record.Safety ReplaySafety.Unsafe "and what was overridden"
                  Expect.isNonEmpty record.Reasons "and why it was refusable"
              | None ->
                  failtest
                      "an override that produced no record is indistinguishable afterwards from a handler that was safe"
          }

          test "the permissive policy is never what a caller gets by saying nothing" {
              Expect.isFalse Replay.strict.AcceptUnsafeResume "the default refuses"
              Expect.isTrue Replay.acceptingUnsafeResume.AcceptUnsafeResume "and the other one is named, deliberately"
          }

          test "every decision renders log-safely — no payload, no document-supplied name" {
              // The names are deliberately unmistakable. An earlier draft of this
              // test looked for "ops" — which is a substring of the renderer's
              // OWN prose ("recorded ops only"), so it failed on a collision with
              // the thing it was checking rather than on the property. A probe
              // that can match its own harness measures nothing.
              let payload = jstr "NEEDLE-PAYLOAD"

              let hostile =
                  { Name = "work"
                    Stages =
                      [ Effect(ServerEffect.HostCall("NEEDLE-FUNCTION", payload, Some "NEEDLE-SLOT"))
                        Effect(ServerEffect.Notify("NEEDLE-CHANNEL", payload)) ] }

              let rendered =
                  [ Replay.admit ReplayMode.Audit Replay.strict hostile
                    Replay.admit ReplayMode.Resume Replay.strict hostile
                    Replay.admit ReplayMode.Resume Replay.acceptingUnsafeResume hostile
                    Replay.admit ReplayMode.Resume Replay.strict unknownHandler
                    Replay.admit ReplayMode.Resume Replay.strict safeHandler ]
                  |> List.map Replay.describe

              for text in rendered do
                  Expect.isFalse (text.Contains "NEEDLE") "not one string the declared form supplied"
                  Expect.isNotEmpty text "and it still says something"

              // And the probe can go red: the same needles ARE reachable, so the
              // check above is a property of the renderer rather than of a
              // handler that happened to carry nothing.
              Expect.stringContains
                  (ServerEffect.capability (ServerEffect.HostCall("NEEDLE-FUNCTION", payload, None)))
                  "NEEDLE-FUNCTION"
                  "the name is present in the declared form, so its absence above was earned"
          }

          // ── the projection join ───────────────────────────────────

          test "the demanded projection carries one posture per REACHABLE handler" {
              let handlers =
                  Map.ofList
                      [ endpoint, unsafeHandler
                        "/handlers/unreached", { safeHandler with Name = "unreached" } ]

              let projection = Replay.ofTreeAndHandlers handlers (treeCalling endpoint)

              match projection.Server with
              | Some tier ->
                  Expect.equal (tier.Replay |> List.map _.Handler) [ "work" ] "the reached handler, and only it"

                  match tier.Replay with
                  | [ posture ] ->
                      Expect.equal posture.Safety "unsafe" "its derived posture"

                      Expect.equal
                          (posture.Reasons |> List.map (fun r -> r.Stage, r.Defect))
                          [ 1, "opaque-host-call"; 2, "outbound-notification" ]
                          "with the reasons, as tokens"
                  | other -> failtestf "expected exactly one posture, got %A" other
              | None -> failtest "the join must not silence the server tier"
          }

          test "the joined document is deterministic and the version moved with the shape" {
              let handlers = Map.ofList [ endpoint, unsafeHandler ]
              let tree = treeCalling endpoint
              let a = Replay.ofTreeAndHandlers handlers tree
              let b = Replay.ofTreeAndHandlers handlers tree

              Expect.equal a b "the same tree and registration project identically"
              Expect.equal (Demanded.encode a) (Demanded.encode b) "and encode to the same bytes"

              let json = Demanded.encode a
              Expect.stringContains json (sprintf "\"version\":%d" Demanded.Version) "the version the encoder emits"

              Expect.stringContains
                  json
                  "\"constraints\":[]"
                  "this walk declares no argument policy — the key is present and EMPTY rather than absent, so 'predates the member' and 'read and empty' stay distinguishable"

              Expect.stringContains
                  json
                  "\"replay\":[{\"handler\":\"work\",\"safety\":\"unsafe\",\"reasons\":[{\"stage\":1,\"defect\":\"opaque-host-call\"},{\"stage\":2,\"defect\":\"outbound-notification\"}]}]"
                  "the posture, verbatim"
          }

          test "the joined document round-trips through the reader, both directions" {
              // The round-trip suite beside the encoder proves this over documents
              // it builds; this proves it over a document the WALKS actually
              // produce — the two-tier shape with a derived posture, which is the
              // one no hand-built fixture can vouch for. A reader that agreed with
              // a fixture and disagreed with the emitter would be worse than none.
              let handlers = Map.ofList [ endpoint, unsafeHandler ]
              let projection = Replay.ofTreeAndHandlers handlers (treeCalling endpoint)
              let json = Demanded.encode projection

              match Demanded.decode json with
              | Ok read ->
                  Expect.equal read projection "the projection survives its own document"
                  Expect.equal (Demanded.encode read) json "and the document survives being read"
              | Error failure ->
                  failtestf "the emitter's own document was refused: %A at '%s'" failure.Defect failure.Field
          }

          test "a safe handler's posture carries an EMPTY reason list, not an absent one" {
              let handlers = Map.ofList [ endpoint, safeHandler ]

              let json =
                  Demanded.encode (Replay.ofTreeAndHandlers handlers (treeCalling endpoint))

              Expect.stringContains
                  json
                  "\"replay\":[{\"handler\":\"work\",\"safety\":\"safe\",\"reasons\":[]}]"
                  "present and empty — a reader can tell it from a producer that carries no posture at all"
          }

          test "a projection with NO server tier is not given one by the join" {
              // `None` means no server walk was performed. Attaching a posture
              // would turn "not asked" into "asked, and here is the answer".
              let clientOnly = Demanded.ofTree (treeCalling endpoint)
              Expect.isNone clientOnly.Server "the precondition"

              let joined = Replay.withPostures [ unsafeHandler ] clientOnly
              Expect.isNone joined.Server "and the join left it alone"
              Expect.equal joined clientOnly "the document is untouched"
          }

          // ── that the walk can go red ──────────────────────────────

          test "PROBE: the walk DISCRIMINATES on addressing, and names the difference" {
              // The same handler, the same effect arm, one op each — differing
              // only in whether the op names the node it addresses. A walk
              // flattened to "safe" passes every test above that looks at one
              // of these in isolation, and fails here.
              let addressed = handlerOf [ Effect(ServerEffect.ApplyOps [ absolute ]) ]
              let positional = handlerOf [ Effect(ServerEffect.ApplyOps [ relative ]) ]

              Expect.equal (tagOf addressed) "safe" "an op naming its target is provably re-runnable"
              Expect.notEqual (tagOf positional) "safe" "and one addressing by position is NOT classified safe"
              Expect.equal (tagOf positional) "unknown" "it is undecided — never rounded to unsafe either"

              Expect.equal
                  (defectsOf positional)
                  [ 0, "relative-addressing" ]
                  "and the difference is NAMED, so the finding is actionable rather than merely present"

              // And the consequence at the decision, which is what the
              // difference is for.
              Expect.equal
                  (Replay.admit ReplayMode.Resume Replay.strict addressed).Admission
                  (ReplayAdmission.ReEvaluateReads [])
                  "the addressed one resumes with nothing to explain"
          } ]
