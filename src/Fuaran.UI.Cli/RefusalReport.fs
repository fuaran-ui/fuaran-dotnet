// Fuaran.UI.Cli — the `refusal-report` verb.
//
// A generic refusal-report entry point for the reference host: given the shared
// conformance corpus, decode every reject-family fixture and emit ONE JSON
// document saying, per fixture, whether this host refused it and with which
// code and path.
//
// ── Why it exists ─────────────────────────────────────────────────────────
//
// Every host's own suite asserts its answers against the corpus DECLARATION, so
// cross-host agreement is true transitively — provided every host's leg actually
// ran, and provided every host is measured. The cross-host comparison had four
// hosts driven directly and this one participating only through the declaration,
// so any divergence with the reference host on one side was invisible to the one
// tool whose job is host agreement. That is the gap this verb closes, and it is
// not hypothetical: the divergences ratified in WIRE_FORMAT §20 were all found
// by reading source, precisely because no artefact compared this host to the
// others.
//
// ── What it does NOT do ───────────────────────────────────────────────────
//
// It answers per fixture; it does not compare, does not decide agreement, and
// does not know what the other hosts said. The comparison is the runner's, and
// keeping the two apart is what lets each host's answer be produced by that
// host's own toolchain rather than by a re-implementation living in the runner.
//
// A family this host has no reader for is reported as `skipped` WITH A REASON,
// per fixture, never omitted: an omitted fixture reads to the runner exactly
// like a fixture the host was never asked about, so a host that quietly dropped
// a family would be indistinguishable from one that had none.

module Fuaran.UI.Cli.RefusalReport

open System
open System.IO
open Fuaran.Core
open Fuaran.UI.Ops

/// One host answer. `Skipped` is `Some reason` when this host has no reader for
/// the fixture's declared decoder.
type private Answer =
    { Id: string
      Decoder: string
      Refused: bool
      Code: string
      Path: string
      Message: string
      Skipped: string option }

let private jstr (s: string) = JStr s

/// The reject families this verb reports on. Deliberately the manifest's own
/// `kind` values rather than a substring test: a family added to the corpus
/// should arrive here as a decision, not as a silent inclusion.
let private rejectKinds =
    set
        [ "reject"
          "elicitation-reject"
          "elicitation-answer-reject"
          "contract-card-reject"
          "envelope-reject" ]

let private field (key: string) (j: JVal) : JVal option =
    match j with
    | JObj members -> members |> List.tryPick (fun (k, v) -> if k = key then Some v else None)
    | _ -> None

let private str (key: string) (j: JVal) : string option =
    match field key j with
    | Some(JStr s) -> Some s
    | _ -> None

/// Decode one fixture through the entry point its declared decoder names.
///
/// `envelope` is the one family with no shipped reader, and the fall-through arm
/// is where it lands. Profile negotiation (WIRE_FORMAT §15) lives in this
/// repository's conformance harness, as a bridge over `Fuaran.Core.Versioning`,
/// and on no surface this package exposes — so a `$profile`-carrying document
/// handed to `decodeNode` is refused for its SHAPE (`MISSING_FIELD` at `$.id`)
/// rather than for its profile. That is a plausible answer to a different
/// question, and reporting it would manufacture a disagreement out of a gap. It
/// is a named skip.
let private answer (corpusRoot: string) (id: string) (decoder: string) (inputFile: string) : Answer =
    let empty =
        { Id = id
          Decoder = decoder
          Refused = false
          Code = ""
          Path = ""
          Message = ""
          Skipped = None }

    let refused (e: JsonDecode.DecodeError) =
        { empty with
            Refused = true
            Code = e.Code
            Path = e.Path
            Message = e.Message }

    let skip reason = { empty with Skipped = Some reason }

    let text =
        File.ReadAllText(Path.Combine(corpusRoot, inputFile.Replace('/', Path.DirectorySeparatorChar)))

    match decoder with
    | "node" ->
        match JsonDecode.decodeNode text with
        | Ok _ -> empty
        | Error e -> refused e
    | "op" ->
        match JsonDecode.decodeOp text with
        | Ok _ -> empty
        | Error e -> refused e
    | "contract-card" ->
        match CustomCardJson.decodeCardJson text with
        | Ok _ -> empty
        | Error e -> refused e
    | "contract-card-bundle" ->
        match CustomCardJson.decodeBundleJson text with
        | Ok _ -> empty
        | Error e -> refused e
    | "elicitation" ->
        match Fuaran.UI.OpStream.Abstractions.Elicitation.decodeEnvelope text with
        | Ok _ -> empty
        | Error e -> refused e
    | "elicitation-outcome" ->
        match Fuaran.UI.OpStream.Abstractions.Elicitation.decodeOutcome text with
        | Ok _ -> empty
        | Error e -> refused e
    | "elicitation-answer" ->
        // `validateAnswerDocument`, not `decodeAnswerJson`. The corpus's
        // `elicitation-answer-*` family is an answer measured AGAINST ITS
        // CONTRACT — its declared codes are `ANSWER_OUT_OF_SPACE` and friends,
        // which no purely structural reader can produce. Reporting the
        // structural decoder's answer here looked plausible and was a different
        // question: it answered `WRONG_TYPE` at the answer, where the corpus
        // declares the space the answer left.
        match Fuaran.UI.OpStream.Abstractions.Elicitation.validateAnswerDocument text with
        | Ok() -> empty
        | Error e -> refused e
    | other -> skip (sprintf "this host has no '%s' reader on a shipped surface" other)

let private renderAnswer (a: Answer) : JVal =
    match a.Skipped with
    | Some reason -> JObj [ "decoder", jstr a.Decoder; "id", jstr a.Id; "skipped", jstr reason ]
    | None ->
        JObj
            [ "code", jstr a.Code
              "decoder", jstr a.Decoder
              "id", jstr a.Id
              "message", jstr a.Message
              "path", jstr a.Path
              "refused", JBool a.Refused ]

/// `fuaran refusal-report --corpus <dir>` → the JSON report on stdout.
///
/// Exit 2 (not 1) for a usage or corpus problem, matching the other verbs: a
/// missing corpus is the caller's error, and the runner must be able to tell it
/// apart from a host that answered.
let run (corpusRoot: string) : int * string =
    let manifestPath = Path.Combine(corpusRoot, "manifest.json")

    if not (File.Exists manifestPath) then
        2, sprintf "refusal-report: no manifest.json at %s\n" manifestPath
    else
        match Json.parse (File.ReadAllText manifestPath) with
        | Error e -> 2, sprintf "refusal-report: manifest.json does not parse: %s\n" e
        | Ok manifest ->
            match field "fixtures" manifest with
            | Some(JArr fixtures) ->
                let answers =
                    fixtures
                    |> List.choose (fun fx ->
                        match str "kind" fx, str "id" fx, str "inputFile" fx with
                        | Some kind, Some id, Some inputFile when rejectKinds.Contains kind ->
                            // The reader a fixture needs is not always the
                            // `decoder` it declares: an `envelope-reject` says
                            // `node`, and taking that literally hands a §15
                            // profile envelope to the plain node decoder, which
                            // answers a different question plausibly. See the
                            // `envelope` arm of `answer`.
                            let decoder =
                                if kind = "envelope-reject" then
                                    "envelope"
                                else
                                    str "decoder" fx |> Option.defaultValue "node"

                            Some(answer corpusRoot id decoder inputFile)
                        | _ -> None)

                let doc =
                    JObj
                        [ "cases", JArr(answers |> List.map renderAnswer)
                          "host", jstr "fuaran-dotnet" ]

                0, Canon.render doc + "\n"
            | _ -> 2, "refusal-report: manifest.json has no 'fixtures' array\n"
