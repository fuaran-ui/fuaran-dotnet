module Fuaran.UI.JsonDecode.Tests.ElicitationFixtures

// ============================================================================
//  Elicitation envelope + answer-conformance fixture corpus (WIRE_FORMAT §18).
//
//  Four dispositions, mirroring the §15 envelope family's emit-time law
//  proofs:
//    - round-trip  : decode (envelope or outcome, per `Decoder`) → re-encode →
//                    byte-identical to the input.
//    - reject      : decode fails with exactly the expected code at a path
//                    starting with the expected prefix.
//    - answer-accept / answer-reject : a `{"answer": …, "contract": …}`
//                    conformance document drives `validateAnswerDocument` —
//                    the decode-side gate a resolution host runs before an
//                    `Answered` outcome reaches the asking agent.
//
//  Round-trip payloads are produced BY the F# codec (values → encode), so the
//  committed bytes are the canonical form every conformant host is held to.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.OpStream.Abstractions

// ─── Trees (canonicalised through the real node codec) ──────────────────────

let private decodeTree (label: string) (raw: string) : Node<obj> =
    match decodeNodeObj raw with
    | Ok n -> n
    | Error e -> failwithf "ElicitationFixtures: %s tree failed to decode (%s at %s)" label e.Code e.Path

/// A one-node question tree (the minimal ask shape).
let private treeNote =
    decodeTree
        "note"
        """{"id":"ask-note","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"Which environment should we deploy to?"}}}"""

let private treeNoteRaw = CanonicalJson.encodeNode treeNote

/// A form tree with a `Binding.Local` input — the Phase 62 form-state-capture
/// shape an answer contract addresses (`nodeId` + `stateKey`).
let private treeForm =
    decodeTree
        "form"
        """{"id":"ask-form","kind":{"$type":"Form","fields":[{"id":"salary-input","kind":{"$type":"Text","onChange":"<closure>","value":{"$type":"Local","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":"","key":"salary"},"onCommit":"<closure>","parse":"<closure>"}},"label":{"$type":"Literal","text":"Salary"},"required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":{"$type":"Literal","text":"Save"}}}"""

// ─── Envelope / outcome values ───────────────────────────────────────────────

let private encodeOrFail (label: string) (env: ElicitationEnvelope) : string =
    match Elicitation.encodeEnvelope env with
    | Ok s -> s
    | Error e -> failwithf "ElicitationFixtures: %s failed to encode (%s at %s)" label e.Code e.Path

let private choiceField: AnswerField =
    { Name = "choice"
      NodeId = NodeId "ask-note"
      StateKey = "choice"
      Space = Enum [ "staging"; "production" ]
      Required = true }

let private minimalEnvelope: ElicitationEnvelope =
    { ElicitationId = "elc-minimal"
      Tree = treeNote
      Contract = { Fields = [ choiceField ] }
      TimeoutMs = None
      Default = None }

let private fullEnvelope: ElicitationEnvelope =
    { ElicitationId = "elc-full"
      Tree = treeForm
      Contract =
        { Fields =
            // The Form node is the contract's address (a FormField is a spec
            // record, not a child Node); each answer field names the state
            // key the form's Binding.Local commits into.
            [ { Name = "salary"
                NodeId = NodeId "ask-form"
                StateKey = "salary"
                Space = IntRange(0, 1000000)
                Required = true }
              { Name = "bonusRatio"
                NodeId = NodeId "ask-form"
                StateKey = "bonus-ratio"
                Space = FloatRange(0.0, 1.5)
                Required = false }
              { Name = "note"
                NodeId = NodeId "ask-form"
                StateKey = "note"
                Space = StringLen(0, 280)
                Required = false }
              { Name = "grade"
                NodeId = NodeId "ask-form"
                StateKey = "grade"
                Space = Enum [ "a"; "b" ]
                Required = true }
              { Name = "label"
                NodeId = NodeId "ask-form"
                StateKey = "label"
                Space = AnyString
                Required = false } ] }
      TimeoutMs = Some 30000
      Default = Some(Map.ofList [ "grade", AnswerValue.Str "a"; "salary", AnswerValue.Int 45000 ]) }

let private outcome (id: string) (o: ElicitationOutcome) : string =
    Elicitation.encodeOutcome { ElicitationId = id; Outcome = o }

// ─── Fixture shapes ──────────────────────────────────────────────────────────

type ElicitationExpect =
    | RoundTrip
    | Refuse of code: string * path: string

type WireFixture =
    {
        /// Corpus filename stem (`elicitation/<id>.json`) + manifest id.
        Id: string
        Wire: string
        /// Which decoder entry point a conformant host invokes:
        /// `"elicitation"` ⇒ the envelope codec, `"elicitation-outcome"` ⇒
        /// the outcome codec.
        Decoder: string
        Expect: ElicitationExpect
        Description: string
    }

type AnswerDocExpect =
    | Accept
    | RefuseAnswer of code: string * path: string

type AnswerDocFixture =
    { Id: string
      Wire: string
      Expect: AnswerDocExpect
      Description: string }

/// The host operation the round-trip/reject families drive: decode with the
/// `Decoder`-named entry point, re-encode canonically. Shared by the emitter
/// (law proof) and the corpus suites.
let decodeReencode (decoder: string) (wire: string) : Result<string, DecodeError> =
    match decoder with
    | "elicitation" -> Elicitation.decodeEnvelope wire |> Result.bind Elicitation.encodeEnvelope
    | "elicitation-outcome" -> Elicitation.decodeOutcome wire |> Result.map Elicitation.encodeOutcome
    | other -> failwithf "ElicitationFixtures.decodeReencode: unknown decoder '%s'" other

// ─── Round-trips ─────────────────────────────────────────────────────────────

let roundTrips: WireFixture list =
    [ { Id = "elc-minimal"
        Wire = encodeOrFail "elc-minimal" minimalEnvelope
        Decoder = "elicitation"
        Expect = RoundTrip
        Description = "minimal envelope — one required enum field, no timeout, no default (WIRE_FORMAT 18.2)" }
      { Id = "elc-full"
        Wire = encodeOrFail "elc-full" fullEnvelope
        Decoder = "elicitation"
        Expect = RoundTrip
        Description =
          "full envelope — every value-space kind, optional fields, timeoutMs, conforming default (WIRE_FORMAT 18.2)" }
      { Id = "elc-out-answered"
        Wire =
          outcome
              "elc-full"
              (ElicitationOutcome.Answered(
                  Map.ofList
                      [ "bonusRatio", AnswerValue.Float 0.25
                        "grade", AnswerValue.Str "a"
                        "salary", AnswerValue.Int 52000 ]
              ))
        Decoder = "elicitation-outcome"
        Expect = RoundTrip
        Description = "Answered outcome — canonical typed answer object (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-declined"
        Wire = outcome "elc-minimal" ElicitationOutcome.Declined
        Decoder = "elicitation-outcome"
        Expect = RoundTrip
        Description = "Declined outcome (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-timedout"
        Wire = outcome "elc-minimal" ElicitationOutcome.TimedOut
        Decoder = "elicitation-outcome"
        Expect = RoundTrip
        Description = "TimedOut outcome — dispatched by the HOST's clock; timeoutMs is data (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-superseded-by"
        Wire = outcome "elc-minimal" (ElicitationOutcome.Superseded(Some "elc-full"))
        Decoder = "elicitation-outcome"
        Expect = RoundTrip
        Description = "Superseded outcome naming its successor (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-superseded-anon"
        Wire = outcome "elc-minimal" (ElicitationOutcome.Superseded None)
        Decoder = "elicitation-outcome"
        Expect = RoundTrip
        Description = "Superseded outcome without a successor id — 'by' is optional (WIRE_FORMAT 18.3)" } ]

// ─── Rejects ─────────────────────────────────────────────────────────────────

let private contractChoiceRaw =
    """{"fields":[{"name":"choice","nodeId":"ask-note","required":true,"space":{"$type":"anyString"},"stateKey":"choice"}]}"""

let private envelopeRaw (extra: string) (contract: string) (tree: string) : string =
    "{\"$elicitation\":\"1\","
    + extra
    + "\"contract\":"
    + contract
    + ",\"id\":\"elc-r\",\"tree\":"
    + tree
    + "}"

let rejects: WireFixture list =
    [ { Id = "elc-reject-missing-version"
        Wire =
          "{\"contract\":"
          + contractChoiceRaw
          + ",\"id\":\"elc-r\",\"tree\":"
          + treeNoteRaw
          + "}"
        Decoder = "elicitation"
        Expect = Refuse("MISSING_FIELD", "$.$elicitation")
        Description = "envelope without the $elicitation format tag (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-bad-version"
        Wire =
          "{\"$elicitation\":\"2\",\"contract\":"
          + contractChoiceRaw
          + ",\"id\":\"elc-r\",\"tree\":"
          + treeNoteRaw
          + "}"
        Decoder = "elicitation"
        Expect = Refuse("UNSUPPORTED_VERSION", "$.$elicitation")
        Description = "unknown elicitation format version — hard-refuse, never mis-decode (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-stray-key"
        Wire =
          "{\"$elicitation\":\"1\",\"contract\":"
          + contractChoiceRaw
          + ",\"id\":\"elc-r\",\"note\":\"x\",\"tree\":"
          + treeNoteRaw
          + "}"
        Decoder = "elicitation"
        Expect = Refuse("UNDECLARED_FIELD", "$.note")
        Description = "undeclared envelope key — default-deny by shape (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-empty-id"
        Wire =
          "{\"$elicitation\":\"1\",\"contract\":"
          + contractChoiceRaw
          + ",\"id\":\"\",\"tree\":"
          + treeNoteRaw
          + "}"
        Decoder = "elicitation"
        Expect = Refuse("WRONG_TYPE", "$.id")
        Description = "empty elicitation id (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-empty-contract"
        Wire = envelopeRaw "" "{\"fields\":[]}" treeNoteRaw
        Decoder = "elicitation"
        Expect = Refuse("CONTRACT_EMPTY", "$.contract.fields")
        Description = "contract with no fields — an elicitation must declare its answer (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-duplicate-field"
        Wire =
          envelopeRaw
              ""
              ("{\"fields\":["
               + "{\"name\":\"choice\",\"nodeId\":\"ask-note\",\"required\":true,\"space\":{\"$type\":\"anyString\"},\"stateKey\":\"a\"},"
               + "{\"name\":\"choice\",\"nodeId\":\"ask-note\",\"required\":false,\"space\":{\"$type\":\"anyString\"},\"stateKey\":\"b\"}]}")
              treeNoteRaw
        Decoder = "elicitation"
        Expect = Refuse("CONTRACT_DUPLICATE_FIELD", "$.contract.fields[1].name")
        Description = "two answer fields sharing a name (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-unknown-node"
        Wire =
          envelopeRaw
              ""
              """{"fields":[{"name":"choice","nodeId":"ghost","required":true,"space":{"$type":"anyString"},"stateKey":"choice"}]}"""
              treeNoteRaw
        Decoder = "elicitation"
        Expect = Refuse("CONTRACT_UNKNOWN_NODE", "$.contract.fields[0].nodeId")
        Description = "contract field addressing a node absent from the tree (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-bad-space"
        Wire =
          envelopeRaw
              ""
              """{"fields":[{"name":"choice","nodeId":"ask-note","required":true,"space":{"$type":"regex","pattern":".*"},"stateKey":"choice"}]}"""
              treeNoteRaw
        Decoder = "elicitation"
        Expect = Refuse("UNKNOWN_DU_CASE", "$.contract.fields[0].space.$type")
        Description = "unknown value-space discriminator (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-tree-decode"
        Wire = envelopeRaw "" contractChoiceRaw "{\"id\":\"ask-note\"}"
        Decoder = "elicitation"
        Expect = Refuse("MISSING_FIELD", "$.tree")
        Description =
          "embedded tree fails the standard 3.1 node decode — error re-rooted under $.tree (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-bad-timeout"
        Wire =
          "{\"$elicitation\":\"1\",\"contract\":"
          + contractChoiceRaw
          + ",\"id\":\"elc-r\",\"timeoutMs\":0,\"tree\":"
          + treeNoteRaw
          + "}"
        Decoder = "elicitation"
        Expect = Refuse("WRONG_TYPE", "$.timeoutMs")
        Description = "timeoutMs below 1 (WIRE_FORMAT 18.3)" }
      { Id = "elc-reject-default-nonconformant"
        Wire =
          "{\"$elicitation\":\"1\",\"contract\":"
          + contractChoiceRaw
          + ",\"default\":{\"choice\":5},\"id\":\"elc-r\",\"tree\":"
          + treeNoteRaw
          + "}"
        Decoder = "elicitation"
        Expect = Refuse("DEFAULT_NONCONFORMANT", "$.default.choice")
        Description = "default answer violating the contract (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-reject-unknown-type"
        Wire = """{"$type":"Escalated","elicitationId":"elc-r"}"""
        Decoder = "elicitation-outcome"
        Expect = Refuse("UNKNOWN_DU_CASE", "$.$type")
        Description = "outcome outside the closed set — the outcome DU is total (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-reject-missing-answer"
        Wire = """{"$type":"Answered","elicitationId":"elc-r"}"""
        Decoder = "elicitation-outcome"
        Expect = Refuse("MISSING_FIELD", "$.answer")
        Description = "Answered outcome without an answer object (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-reject-stray-key"
        Wire = """{"$type":"Declined","answer":{},"elicitationId":"elc-r"}"""
        Decoder = "elicitation-outcome"
        Expect = Refuse("UNDECLARED_FIELD", "$.answer")
        Description = "Declined outcome carrying an answer — undeclared key, default-deny (WIRE_FORMAT 18.3)" }
      { Id = "elc-out-reject-empty-id"
        Wire = """{"$type":"Declined","elicitationId":""}"""
        Decoder = "elicitation-outcome"
        Expect = Refuse("WRONG_TYPE", "$.elicitationId")
        Description = "outcome with an empty elicitationId (WIRE_FORMAT 18.3)" } ]

// ─── Answer-conformance documents ───────────────────────────────────────────

let private contractC =
    """{"fields":[{"name":"rating","nodeId":"n1","required":true,"space":{"$type":"intRange","max":5,"min":1},"stateKey":"rating"},{"name":"email","nodeId":"n1","required":true,"space":{"$type":"anyString"},"stateKey":"email"},{"name":"score","nodeId":"n1","required":false,"space":{"$type":"floatRange","max":1,"min":0},"stateKey":"score"},{"name":"size","nodeId":"n1","required":false,"space":{"$type":"enum","values":["s","m","l"]},"stateKey":"size"},{"name":"bio","nodeId":"n1","required":false,"space":{"$type":"stringLen","max":10,"min":0},"stateKey":"bio"}]}"""

let private answerDoc (answer: string) : string =
    "{\"answer\":" + answer + ",\"contract\":" + contractC + "}"

let answerDocs: AnswerDocFixture list =
    [ { Id = "elc-ans-accept-minimal"
        Wire = answerDoc """{"email":"a@b.c","rating":4}"""
        Expect = Accept
        Description = "required fields only — optional fields may be omitted (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-accept-full"
        Wire = answerDoc """{"bio":"hi","email":"a@b.c","rating":4,"score":0.5,"size":"m"}"""
        Expect = Accept
        Description = "every declared field, all in-space (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-accept-int-for-float"
        Wire = answerDoc """{"email":"a@b.c","rating":4,"score":1}"""
        Expect = Accept
        Description = "a whole-valued number satisfies a floatRange — JSON has one number type (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-missing-required"
        Wire = answerDoc """{"email":"a@b.c"}"""
        Expect = RefuseAnswer("ANSWER_MISSING_FIELD", "$.answer.rating")
        Description = "required answer field absent (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-undeclared"
        Wire = answerDoc """{"color":"red","email":"a@b.c","rating":4}"""
        Expect = RefuseAnswer("ANSWER_UNDECLARED_FIELD", "$.answer.color")
        Description = "undeclared answer key — default-deny by shape (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-type-mismatch"
        Wire = answerDoc """{"email":"a@b.c","rating":"4"}"""
        Expect = RefuseAnswer("ANSWER_TYPE_MISMATCH", "$.answer.rating")
        Description = "string where the space demands an integer (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-int-range"
        Wire = answerDoc """{"email":"a@b.c","rating":9}"""
        Expect = RefuseAnswer("ANSWER_OUT_OF_SPACE", "$.answer.rating")
        Description = "integer outside its intRange (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-float-range"
        Wire = answerDoc """{"email":"a@b.c","rating":4,"score":1.5}"""
        Expect = RefuseAnswer("ANSWER_OUT_OF_SPACE", "$.answer.score")
        Description = "number outside its floatRange (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-enum"
        Wire = answerDoc """{"email":"a@b.c","rating":4,"size":"xl"}"""
        Expect = RefuseAnswer("ANSWER_OUT_OF_SPACE", "$.answer.size")
        Description = "string outside its enum (WIRE_FORMAT 18.4)" }
      { Id = "elc-ans-reject-string-len"
        Wire = answerDoc """{"bio":"this is far too long","email":"a@b.c","rating":4}"""
        Expect = RefuseAnswer("ANSWER_OUT_OF_SPACE", "$.answer.bio")
        Description = "string longer than its stringLen bound (WIRE_FORMAT 18.4)" } ]
