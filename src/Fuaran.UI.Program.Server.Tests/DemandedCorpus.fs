module Fuaran.UI.Program.Server.Tests.DemandedCorpus

// ─── The demanded document's conformance corpus, as one function ─────
//
// `Demanded.encode` writes the document and `Demanded.decode` is the pinned
// reader for it. A consumer in another repository — one that must not take a
// package dependency on this tier — cannot call either, and has historically
// written its own envelope reader instead. Such a reader cannot be wrong in a
// way anything downstream notices: it produces a projection that LOOKS like a
// projection, and whatever is computed from it is silently wrong.
//
// So the pin travels as DATA. Each vector below is a document paired with what
// THIS tier's own decoder read it as — the accepted ones with the server tier's
// contents, the refused ones with the defect class. A consumer's reader is
// conformant exactly when it produces the same answers.
//
// Phase 1978 moved this out of `tools/emit-demanded-conformance.fsx`, which had
// fallen two document versions and one clause behind the document it emits
// without any gate noticing: a script nobody runs is a specification of
// nothing. The script now wraps `emit`, and `DemandedCorpusTests` compares
// `emit` with the committed vectors at `conformance/demanded-effect-projection.json`,
// so a change to the document that does not regenerate the vectors is red here.
//
// Where the vectors live, stated: in the PROGRAM repository, beside the codec
// that is their authority, at `conformance/demanded-effect-projection.json`.
// Since fuaran#2012 moved the UI adapters here, the two vectors that are real
// UI harvests can only be produced here, so the emitter moved with them: it
// writes `conformance/demanded-effect-projection.json` beside this file, a
// byte COPY declared in `copies.json` with the program repository's file as
// canonical. The program repository certifies every vector's recorded read
// against its own decoder with no UI type in reach; this suite certifies that
// the documents are the ones this tier produces. They are not part of the program wire specification's
// corpus — that specification does not spell the demanded document (see
// `docs/generic-tier.md` §6), so the five-artefact forward coupling does not
// reach them.
//
// Nothing here names any consumer. The corpus describes this document and is
// useful to anyone reading one.

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.Program.Bounded
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

let private jstr (s: string) = Fuaran.Core.JStr s

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

/// A handler that reaches every member of the server tier a harvest fills: two
/// host functions, a notification channel, and an op sequence whose op names a
/// node — so the richest vector carries a non-empty `reach`, a replay posture
/// and an undo posture that this tier actually produced.
let private busy: Handler =
    { Name = "orders.settle"
      Stages =
        [ HandlerStage.Effect(ServerEffect.HostCall("sendMail", jstr "x", None))
          HandlerStage.Effect(ServerEffect.HostCall("charge", jstr "y", None))
          HandlerStage.Effect(ServerEffect.Notify("overdue", jstr "n"))
          HandlerStage.Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "spinner") ]) ] }

let private registration =
    Map.ofList [ "/handlers/settle", { busy with Name = "/handlers/settle" } ]

// ── the documents ────────────────────────────────────────────────────────────

/// The version every literal vector below is written at. Read from the codec,
/// so a version move re-renders the literals rather than leaving them behind —
/// which is exactly what made the stale vectors refusals before this phase.
let private v = string Demanded.Version

/// A real harvest: the whole pipeline, so the richest vector in the corpus is a
/// document this tier actually produces rather than one hand-written to look
/// like one.
let private harvested =
    (Harvest.ofProgram registration (treeCalling "/handlers/settle")).Document

let private tierLess =
    (Harvest.ofProgram registration (treeCalling "/handlers/nobody")).Document

let private emptyTier = (Harvest.ofRegistration []).Document

let private emptyServer =
    "{\"effects\":[],\"capabilities\":[],\"functions\":[],\"channels\":[],\"reach\":[],\"replay\":[],\"undo\":[],\"constraints\":[]}"

let private docWith (server: string) =
    "{\"kind\":\"demanded\",\"version\":"
    + v
    + ",\"effects\":[],\"hostCalls\":[],\"stateNamespaces\":[],\"opaqueHandlers\":[],\"server\":"
    + server
    + "}"

let private canonical = docWith emptyServer

/// A server tier with one member replaced, every other member at its empty value.
let private serverWith (key: string) (value: string) =
    emptyServer.Replace("\"" + key + "\":[]", "\"" + key + "\":" + value)

let private policy =
    "[{\"capability\":\"host:fetch\",\"clauses\":["
    + "{\"clause\":\"allowList\",\"argument\":\"url\",\"permitted\":[\"api.example.com\"]},"
    + "{\"clause\":\"ceiling\",\"bytes\":65536},"
    + "{\"clause\":\"label\",\"label\":\"pii\"},"
    + "{\"clause\":\"denyList\",\"argument\":\"path\",\"refused\":[\"/etc\"]},"
    + "{\"clause\":\"atMost\",\"argument\":\"count\",\"limit\":90}]}]"

/// Every vector: an id, the document, and what it is for.
let vectors: (string * string * string) list =
    [ ("harvest-full",
       harvested,
       "A real harvest: a program, the handler registration behind it, every reachable handler's replay and undo posture, and the nodes its ops reach.")
      ("tier-less",
       tierLess,
       "A program reaching no registered handler. The tier is present because a walk ran and demands nothing — a different fact from not having been asked.")
      ("empty-registration", emptyTier, "A registration with no handlers. A walk ran; there was nothing to find.")
      ("unknown-effect-arm",
       docWith (emptyServer.Replace("\"effects\":[]", "\"effects\":[\"RunQuery\",\"TeleportSomewhere\"]")),
       "A server tier naming an arm outside a consumer's vocabulary. Carried, never dropped: dropping it would make a newer program look safer than an older one.")
      ("declared-argument-policy",
       docWith (
           (serverWith "constraints" policy)
               .Replace("\"effects\":[]", "\"effects\":[\"HostCall\"]")
               .Replace("\"functions\":[]", "\"functions\":[{\"function\":\"fetch\",\"capability\":\"host:fetch\"}]")
       ),
       "A tier carrying the host's declared argument policy, every clause kind included. A reader that dropped a clause would report 'unconstrained' for a bounded capability, which is the one misreading in this document that is dangerous rather than merely lossy.")
      ("op-reach",
       docWith (
           (serverWith
               "reach"
               "[{\"capability\":\"ApplyOps\",\"argument\":\"target\",\"name\":\"spinner\"},{\"capability\":\"EmitPatch\",\"argument\":\"target\",\"name\":\"row\"}]")
               .Replace("\"effects\":[]", "\"effects\":[\"ApplyOps\",\"EmitPatch\"]")
               .Replace("\"capabilities\":[]", "\"capabilities\":[\"ApplyOps\",\"EmitPatch\"]")
       ),
       "A tier carrying what its ops reach: every named argument of every op, under the capability it rides. A reader that dropped it would report an op sequence that writes as one that names nothing.")
      ("unknown-policy-clause",
       docWith (
           serverWith
               "constraints"
               "[{\"capability\":\"host:fetch\",\"clauses\":[{\"clause\":\"teleport\",\"argument\":\"url\"}]}]"
       ),
       "A clause discriminator this version does not declare. REFUSED, unlike an unknown effect arm: the discriminator selects which members the object has, so a reader that carried it could not have read the object it introduces.")
      ("unknown-version",
       canonical.Replace("\"version\":" + v, "\"version\":2"),
       "A version this reader does not read. Refused rather than read through another version's lens.")
      ("previous-version",
       canonical.Replace("\"version\":" + v, "\"version\":" + string (Demanded.Version - 1)),
       "The version immediately before this one, in this version's shape. Refused: a document's version is a claim about its shape, and a reader that accepted the neighbouring number would read through the wrong lens the first time the shapes differ.")
      ("wrong-kind",
       canonical.Replace("\"demanded\"", "\"manifest\""),
       "A document of another kind. The kind is a claim about what the document IS.")
      ("undeclared-root-member",
       canonical.Replace("\"kind\":\"demanded\"", "\"kind\":\"demanded\",\"extra\":1"),
       "A root member this version does not declare. Refused rather than ignored: ignoring it is reading the document through the wrong lens with the version agreeing all the way.")
      ("undeclared-server-member",
       canonical.Replace("\"server\":{\"effects\"", "\"server\":{\"extra\":1,\"effects\""),
       "The same rule, one level down.")
      ("missing-server",
       canonical.Replace(",\"server\":" + emptyServer, ""),
       "The tier omitted entirely. This version carries it on every document, null where no walk ran — so its absence is a document this version does not describe.")
      ("null-server",
       canonical.Replace("\"server\":" + emptyServer, "\"server\":null"),
       "The tier spelled null: no walk was performed. A member spelled null IS an absent member.")
      ("missing-reach",
       docWith (emptyServer.Replace(",\"reach\":[]", "")),
       "A server tier without `reach`. Refused: an absent reach says the producer predates the member, an empty one that the ops name nothing, and a deployer reading a handler that writes must never take the first for the second.")
      ("missing-undo",
       docWith (emptyServer.Replace(",\"undo\":[]", "")),
       "A server tier without `undo`. Refused on the same argument: 'I could not see whether this can be undone' and 'nothing here needs undoing' must never share a spelling.")
      ("missing-root-member",
       canonical.Replace(",\"opaqueHandlers\":[]", ""),
       "A required root member absent. Nothing is defaulted — a reader that supplied an empty list would report a demand set the document does not state.")
      ("wrong-member-type",
       canonical.Replace("\"effects\":[],\"hostCalls\"", "\"effects\":\"none\",\"hostCalls\""),
       "A member carrying the wrong JSON type. Distinct from an absent one: only this means the producer and the reader disagree about what the member IS.")
      ("non-canonical-effects",
       canonical
           .Replace("\"effects\":[],\"hostCalls\"", "\"effects\":[\"b\",\"a\"],\"hostCalls\"")
           .Replace("\"server\":" + emptyServer, "\"server\":null"),
       "A client-tier list that is not distinct and sorted. Two such documents would not compare by value, which is the property the encoding exists to provide.")
      ("non-canonical-server-functions",
       docWith (
           serverWith
               "functions"
               "[{\"function\":\"b\",\"capability\":\"host:b\"},{\"function\":\"a\",\"capability\":\"host:a\"}]"
       ),
       "The same rule in the server tier.")
      ("not-an-object", "[]", "A document whose root is not an object.")
      ("not-json", "{", "Bytes that are not readable JSON.") ]

// ── what the pinned reader makes of each ─────────────────────────────────────

let private esc (s: string) =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")

let private q (s: string) = "\"" + esc s + "\""
let private arr (xs: string list) = "[" + String.concat "," xs + "]"

let private reasons (rs: (int * string) list) =
    rs
    |> List.map (fun (stage, defect) -> "{\"stage\":" + string stage + ",\"defect\":" + q defect + "}")
    |> arr

/// The tier as the corpus carries it — every member the reader reads, so a
/// consumer whose reader dropped one disagrees with this corpus.
///
/// A walk that did NOT run is `"serverWalked":false` with no `server` member, rather than
/// `"server":null`. The corpus is read by consumers whose JSON model has no null — the wire this
/// whole family lives on has none either — so emitting one would make the corpus unreadable to
/// exactly the readers it exists to certify. The two facts stay distinguishable because the boolean
/// is always present.
let private renderTier (tier: ServerDemand option) =
    match tier with
    | None -> "\"serverWalked\":false"
    | Some t ->
        let fns =
            t.Functions
            |> List.map (fun f -> "{\"function\":" + q f.Function + ",\"capability\":" + q f.Capability + "}")
            |> arr

        let chans =
            t.Channels
            |> List.map (fun c -> "{\"channel\":" + q c.Channel + ",\"name\":" + q c.Name + "}")
            |> arr

        let reach =
            t.Reach
            |> List.map (fun r ->
                "{\"capability\":"
                + q r.Capability
                + ",\"argument\":"
                + q r.Argument
                + ",\"name\":"
                + q r.Name
                + "}")
            |> arr

        let replay =
            t.Replay
            |> List.map (fun p ->
                "{\"handler\":"
                + q p.Handler
                + ",\"safety\":"
                + q p.Safety
                + ",\"reasons\":"
                + reasons (p.Reasons |> List.map (fun r -> r.Stage, r.Defect))
                + "}")
            |> arr

        let undo =
            t.Undo
            |> List.map (fun p ->
                "{\"handler\":"
                + q p.Handler
                + ",\"undo\":"
                + q p.Undo
                + ",\"reasons\":"
                + reasons (p.Reasons |> List.map (fun r -> r.Stage, r.Defect))
                + "}")
            |> arr

        // The declared argument policy, rendered clause by clause. A consumer
        // whose reader silently dropped a bound would otherwise agree with this
        // corpus while reading "unconstrained" off a constrained document — the
        // one misreading this member makes dangerous rather than merely lossy.
        let constraints =
            t.Constraints
            |> List.map (fun c ->
                let clauses =
                    c.Clauses
                    |> List.map (fun clause ->
                        match clause with
                        | ServerConstraintClause.AllowList(argument, permitted) ->
                            "{\"clause\":\"allowList\",\"argument\":"
                            + q argument
                            + ",\"permitted\":"
                            + arr (permitted |> List.map q)
                            + "}"
                        | ServerConstraintClause.DenyList(argument, refused) ->
                            "{\"clause\":\"denyList\",\"argument\":"
                            + q argument
                            + ",\"refused\":"
                            + arr (refused |> List.map q)
                            + "}"
                        | ServerConstraintClause.AtMost(argument, limit) ->
                            "{\"clause\":\"atMost\",\"argument\":"
                            + q argument
                            + ",\"limit\":"
                            + string limit
                            + "}"
                        | ServerConstraintClause.Ceiling bytes ->
                            "{\"clause\":\"ceiling\",\"bytes\":" + string bytes + "}"
                        | ServerConstraintClause.Label label -> "{\"clause\":\"label\",\"label\":" + q label + "}")
                    |> arr

                "{\"capability\":" + q c.Capability + ",\"clauses\":" + clauses + "}")
            |> arr

        "\"serverWalked\":true,\"server\":{\"effects\":"
        + arr (t.Effects |> List.map q)
        + ",\"capabilities\":"
        + arr (t.Capabilities |> List.map q)
        + ",\"functions\":"
        + fns
        + ",\"channels\":"
        + chans
        + ",\"reach\":"
        + reach
        + ",\"replay\":"
        + replay
        + ",\"undo\":"
        + undo
        + ",\"constraints\":"
        + constraints
        + "}"

let private entry (id: string, document: string, description: string) =
    let verdict =
        match Demanded.decode document with
        | Ok projection -> "{\"verdict\":\"ok\"," + renderTier projection.Server + "}"
        | Error failure ->
            "{\"verdict\":\"refused\",\"defect\":"
            + q (string failure.Defect)
            + ",\"field\":"
            + q failure.Field
            + "}"

    "{\"id\":"
    + q id
    + ",\"description\":"
    + q description
    + ",\"document\":"
    + q document
    + ",\"read\":"
    + verdict
    + "}"

/// The corpus file's path: this repository's byte COPY of the program repository's
/// `conformance/demanded-effect-projection.json`, declared in `copies.json` (fuaran#2012).
let committedPath: string =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "conformance", "demanded-effect-projection.json")
    |> System.IO.Path.GetFullPath

/// The corpus, as bytes: one vector per line, LF line endings, a trailing newline.
let emit () : string =
    "{\n  \"corpus\": \"demanded-effect-projection\",\n  \"documentKind\": \""
    + Demanded.Kind
    + "\",\n  \"decodableVersions\": "
    + arr (Demanded.decodableVersions |> List.map string)
    + ",\n  \"generator\": \"fuaran-dotnet: src/Fuaran.UI.Program.Server.Tests/emit-demanded-conformance.fsx\",\n  \"vectors\": [\n    "
    + String.concat ",\n    " (vectors |> List.map entry)
    + "\n  ]\n}\n"

/// The first line at which two renderings of the corpus disagree, as
/// `(line number, expected line, actual line)` — `None` when they agree. Each
/// vector is one line, so a stale vector is named by its own line.
let firstDifference (expected: string) (actual: string) : (int * string * string) option =
    let split (s: string) = s.Replace("\r\n", "\n").Split('\n')
    let e = split expected
    let a = split actual

    seq { 0 .. (max e.Length a.Length) - 1 }
    |> Seq.tryPick (fun i ->
        let el = if i < e.Length then e[i] else "<absent>"
        let al = if i < a.Length then a[i] else "<absent>"
        if el = al then None else Some(i + 1, el, al))
