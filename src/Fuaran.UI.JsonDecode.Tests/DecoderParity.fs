module Fuaran.UI.JsonDecode.Tests.DecoderParity

// ============================================================================
//  Phase 2639 (t1) — the decoder parity harness.
//
//  `JsonDecode` is to become a policy record over the IDL-generated collecting
//  decoders, one family of positions at a time, each landing deleting the hand
//  structure it replaces (the operator's shape B). No landing may move a
//  verdict, so every landing is gated by THIS harness: the full verdict of the
//  node decoder over every node-decoder fixture of the corpus (the `nodes/`,
//  `lenient/` and `reject/` families) plus this test project's malformed
//  recovery sets, compared against a baseline recorded from the hand decoder
//  before the first family moved.
//
//  A verdict is everything a consumer can observe of one decode:
//
//   * the outcome, and on acceptance the canonical bytes the tree re-encodes to;
//   * on refusal the WHOLE §29 defect list — code, path, sentence and expected
//     shape of every entry, in order (its head is what `decodeNode` answers);
//   * the reliance events (`decodeNodeObjWithOutcome` under `Recovery.Lenient`):
//     the recoveries applied and the ones the gate declined;
//   * the work meter's reading of the strict decode (`DecodeWork.measure`);
//   * the admission verdict under a narrowing `DecodePolicy`, so the
//     `KIND_NOT_ADMITTED` path is held at every kind position the corpus has.
//
//  The baseline lives at `tests/decoder-parity/<family>.tsv`, one row per
//  fixture. A baseline is not a specification — the corpus manifest is — so it
//  holds MORE than the manifest says (every sentence, every later defect, the
//  meter) and moves only by a deliberate act:
//
//      dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --decoder-parity [<dir>]
//
//  rewrites it from the decoder as it stands. A landing that needs that command
//  to go green has moved a verdict, and says so in its own change.
// ============================================================================

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.KindPolicy
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.OpStream.Abstractions

/// The families the harness covers: the three node-decoder families of the
/// corpus, and the two malformed recovery sets this project carries.
type Family =
    | Nodes
    | Lenient
    | Reject
    | Recovery
    | OverClose
    /// Every op-decoder fixture (round-trips and rejects): an op carries nodes,
    /// and `decodeOp` answers ONE error, so the order a decoder visits its
    /// members in is held here where the node families' §29 list sorts it away.
    | Ops
    /// Every member of every kind object in every `nodes/` fixture, dropped, made
    /// `null`, and retyped as a boolean, a string and a fractional number — the
    /// corpus says little about most kinds' refusals, and a family that moves
    /// onto the generated structure must refuse exactly as it did. Its rows hold
    /// a digest of the verdict (the whole verdict, as the node families record
    /// it), since there are thousands; a moved row prints the verdict it has now.
    | Mutations

module Family =
    let all = [ Nodes; Lenient; Reject; Recovery; OverClose; Ops; Mutations ]

    let name (family: Family) : string =
        match family with
        | Nodes -> "nodes"
        | Lenient -> "lenient"
        | Reject -> "reject"
        | Recovery -> "recovery"
        | OverClose -> "overclose"
        | Ops -> "ops"
        | Mutations -> "mutations"

/// The policy every admission verdict is read under: the escape hatches and the
/// most common display kind excluded, so the refusal reaches deep positions on
/// most fixtures rather than only at the root.
let admissionPolicy: DecodePolicy =
    Policy.excluding "decoder-parity" [ "Text"; "Custom"; "Mount" ]

let private lenientPolicy =
    DecodePolicy.admitAll |> DecodePolicy.withRecovery Recovery.Lenient

/// One field of a row, made single-line and tab-free as a JSON string literal
/// (quotes included), so a sentence carrying any character survives the TSV.
let private cell (s: string) : string =
    let b = StringBuilder("\"")

    for c in s do
        match c with
        | '"' -> b.Append("\\\"") |> ignore
        | '\\' -> b.Append("\\\\") |> ignore
        | '\n' -> b.Append("\\n") |> ignore
        | '\r' -> b.Append("\\r") |> ignore
        | '\t' -> b.Append("\\t") |> ignore
        | c when c < ' ' -> b.AppendFormat("\\u{0:x4}", int c) |> ignore
        | c -> b.Append(c) |> ignore

    b.Append('"').ToString()

let private defect (e: DecodeError) : string =
    sprintf
        "%s@%s:%s%s"
        e.Code
        e.Path
        e.Message
        (match e.ExpectedShape with
         | Some s -> " | expected " + s
         | None -> "")

let private defects (es: DecodeError list) : string =
    es |> List.map (defect >> cell) |> String.concat " "

let private digest (s: string) : string =
    SHA256.HashData(Encoding.UTF8.GetBytes s)
    |> Convert.ToHexString
    |> fun h -> h.Substring(0, 16).ToLowerInvariant()

/// The verdict of a decode result: `ok <digest> <length>` of the canonical
/// re-encoding, or `refused` and the defect list.
let private outcome (r: Result<Node<obj>, DecodeError list>) : string =
    match r with
    | Ok node ->
        let bytes = CanonicalJson.encodeNode node
        sprintf "ok %s %d" (digest bytes) bytes.Length
    | Error es -> "refused " + defects es

/// One fixture's full verdict, as the columns of its TSV row (after the id).
let verdict (text: string) : string list =
    let strict, work =
        DecodeWork.measure (fun () -> decodeNodeObjWithDefects DecodePolicy.admitAll text)

    let lenient = decodeNodeObjWithOutcome lenientPolicy text

    let lenientResult =
        match lenient.Result with
        | Ok node -> outcome (Ok node)
        | Error e -> "refused " + cell (defect e)

    let admitted = decodeNodeObjWithDefects admissionPolicy text

    [ outcome strict
      sprintf "steps=%d units=%d nesting=%d parses=%d" work.Steps work.Units work.MaxNesting work.Parses
      lenientResult
      "recovered=" + String.concat "," lenient.Recovered
      "refused=" + String.concat "," lenient.Refused
      outcome admitted ]

/// An op fixture's verdict: the op decoder's answer, its work, and its answer
/// under the admission policy (the reliance columns do not apply to an op).
let opVerdict (text: string) : string list =
    let opOutcome (r: Result<Fuaran.UI.Ops.Types.TreeOp<obj>, DecodeError>) =
        match r with
        | Ok op ->
            let bytes = CanonicalJson.encodeOp op
            sprintf "ok %s %d" (digest bytes) bytes.Length
        | Error e -> "refused " + cell (defect e)

    let strict, work = DecodeWork.measure (fun () -> decodeOp text)

    [ opOutcome strict
      sprintf "steps=%d units=%d nesting=%d parses=%d" work.Steps work.Units work.MaxNesting work.Parses
      "-"
      "-"
      "-"
      opOutcome (decodeOpWithPolicy admissionPolicy text) ]

let columns =
    [ "fixture"
      "strict"
      "work"
      "lenient"
      "recovered"
      "refusedRecoveries"
      "admission" ]

/// The mutations of one document: for every kind object in it (an object
/// member `kind` carrying `$type`), each other member dropped, made `null`,
/// and replaced by `true`, by `"x"` and by `1.5`, as (id suffix, text).
let mutations (text: string) : (string * string) list =
    let parse () =
        // The corpus's deep fixtures nest past the reader's default 64.
        match Nodes.JsonNode.Parse(text, Nullable(), JsonDocumentOptions(MaxDepth = 4096)) with
        | null -> failwith "decoder parity: a fixture parsed to null"
        | n -> n

    let rec kindObjects (steps: string list) (n: Nodes.JsonNode) : string list list =
        match n with
        | :? Nodes.JsonObject as o ->
            let own =
                match o.["kind"] with
                | :? Nodes.JsonObject as k when k.ContainsKey "$type" -> [ steps @ [ "kind" ] ]
                | _ -> []

            own
            @ [ for kv in o do
                    match kv.Value with
                    | Null -> ()
                    | NonNull v -> yield! kindObjects (steps @ [ kv.Key ]) v ]
        | :? Nodes.JsonArray as a ->
            [ for i in 0 .. a.Count - 1 do
                  match a.[i] with
                  | Null -> ()
                  | NonNull v -> yield! kindObjects (steps @ [ string i ]) v ]
        | _ -> []

    let rec at (n: Nodes.JsonNode) (steps: string list) : Nodes.JsonObject =
        match steps, n with
        | [], (:? Nodes.JsonObject as o) -> o
        | step :: rest, (:? Nodes.JsonObject as o) ->
            match o.[step] with
            | null -> failwith "decoder parity: a mutation path left the document"
            | c -> at c rest
        | step :: rest, (:? Nodes.JsonArray as a) ->
            match a.[int step] with
            | null -> failwith "decoder parity: a mutation path left the document"
            | c -> at c rest
        | _ -> failwith "decoder parity: a mutation path left the document"

    let deep = JsonSerializerOptions(MaxDepth = 4096)

    let edits: (string * (Nodes.JsonObject -> string -> unit)) list =
        [ "drop", (fun o k -> o.Remove k |> ignore)
          "null", (fun o k -> o.[k] <- null)
          "bool", (fun o k -> o.[k] <- Nodes.JsonValue.Create true)
          "string", (fun o k -> o.[k] <- Nodes.JsonValue.Create "x")
          "float", (fun o k -> o.[k] <- Nodes.JsonValue.Create 1.5) ]

    [ for steps in kindObjects [] (parse ()) do
          let keys =
              [ for kv in at (parse ()) steps do
                    if kv.Key <> "$type" then
                        yield kv.Key ]

          for key in keys do
              for name, edit in edits do
                  let doc = parse ()
                  edit (at doc steps) key
                  yield sprintf "%s/%s:%s" (String.concat "/" steps) key name, doc.ToJsonString(deep) ]

/// Every fixture of one family, as (id, document text), in a stable order.
let fixtures (family: Family) : (string * string) list =
    let fromCorpusWith (decoder: string) (kinds: string list) =
        let root, entries = Corpus.load ()

        entries
        |> List.filter (fun e -> List.contains e.Kind kinds && e.Decoder = decoder)
        |> List.sortWith (fun a b -> String.CompareOrdinal(a.Id, b.Id))
        |> List.map (fun e -> e.Id, Corpus.readPayload root e.InputFile)

    let fromCorpus (kind: string) = fromCorpusWith "node" [ kind ]

    let fromProject (dir: string) =
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, dir), "*.txt")
        |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> Array.map (fun (p: string) -> string (Path.GetFileNameWithoutExtension p), File.ReadAllText p)
        |> List.ofArray

    match family with
    | Nodes -> fromCorpus "node-round-trip"
    | Lenient -> fromCorpus "lenient-accept"
    | Reject -> fromCorpus "reject"
    | Recovery -> fromProject "recovery-fixtures"
    | OverClose -> fromProject "overclose-fixtures"
    | Ops -> fromCorpusWith "op" [ "op-round-trip"; "reject" ]
    | Mutations ->
        [ for id, text in fromCorpus "node-round-trip" do
              for suffix, mutated in mutations text do
                  yield id + "#" + suffix, mutated ]

/// One family's rows: id, then the verdict columns, tab-separated.
let rows (family: Family) : (string * string) list =
    fixtures family
    |> List.map (fun (id, text) ->
        let columns =
            match family with
            | Ops -> opVerdict text
            | Mutations -> [ digest (String.concat "\t" (verdict text)) ]
            | _ -> verdict text

        id, String.concat "\t" (id :: columns))

let private headerOf (family: Family) : string list =
    match family with
    | Mutations -> [ "fixture"; "verdictDigest" ]
    | _ -> columns

let render (family: Family) : string =
    let header = String.concat "\t" (headerOf family)

    (header :: (rows family |> List.map snd) |> String.concat "\n") + "\n"

/// The baseline directory: `tests/decoder-parity/` at the repository root.
let baselineDir () : string =
    match Fuaran.Tests.CorpusRoot.tryRepoRoot () with
    | Some root -> Path.Combine(root, "tests", "decoder-parity")
    | None -> failwith "decoder parity: could not locate the repository root (tests/decoder-parity/)"

let baselinePath (dir: string) (family: Family) : string =
    Path.Combine(dir, Family.name family + ".tsv")

/// Rewrite every family's baseline from the decoder as it stands.
let write (dir: string) : string list =
    Directory.CreateDirectory dir |> ignore

    [ for family in Family.all do
          let path = baselinePath dir family
          File.WriteAllText(path, render family)
          yield path ]

/// The rows that differ from the baseline, as human-readable lines; empty when
/// the decoder reproduces it exactly.
let divergences (dir: string) (family: Family) : string list =
    let baseline =
        File.ReadAllLines(baselinePath dir family)
        |> Array.skip 1
        |> Array.filter (fun l -> l <> "")
        |> Array.map (fun l -> l.Split('\t').[0], l)
        |> Map.ofArray

    let actual = rows family |> Map.ofList

    let missing =
        baseline
        |> Map.toList
        |> List.filter (fun (id, _) -> not (actual.ContainsKey id))
        |> List.map (fun (id, _) -> sprintf "%s: in the baseline, no longer a fixture" id)

    let moved =
        actual
        |> Map.toList
        |> List.choose (fun (id, row) ->
            match baseline.TryFind id with
            | None -> Some(sprintf "%s: a fixture with no baseline row" id)
            | Some expected when expected = row -> None
            | Some expected ->
                let e = expected.Split('\t')
                let a = row.Split('\t')

                let fields =
                    List.zip3 (headerOf family) (List.ofArray e) (List.ofArray a)
                    |> List.filter (fun (_, x, y) -> x <> y)
                    |> List.map (fun (c, x, y) -> sprintf "    %s\n      was %s\n      now %s" c x y)

                let now =
                    match family with
                    | Mutations ->
                        let text = fixtures family |> List.find (fun (i, _) -> i = id) |> snd
                        sprintf "\n    document %s\n    verdict now %s" text (String.concat " | " (verdict text))
                    | _ -> ""

                Some(sprintf "%s:\n%s%s" id (String.concat "\n" fields) now))

    missing @ moved

[<Tests>]
let tests =
    testList
        "Phase 2639 — decoder parity: every verdict of the node decoder matches its baseline"
        [ for family in Family.all do
              test (sprintf "the %s family" (Family.name family)) {
                  let dir = baselineDir ()

                  match divergences dir family with
                  | [] -> ()
                  | ds ->
                      failtestf
                          "%d verdict(s) moved against %s:\n%s\n\nA landing that moves a verdict on purpose re-records the baseline with `--decoder-parity` and says why in its own change."
                          ds.Length
                          (baselinePath dir family)
                          (ds |> List.truncate 20 |> String.concat "\n")
              }

          test "the harness covers every node-decoder fixture the corpus holds" {
              let _, entries = Corpus.load ()

              let nodeDecoder =
                  entries
                  |> List.filter (fun e ->
                      e.Decoder = "node"
                      && List.contains e.Kind [ "node-round-trip"; "lenient-accept"; "reject" ])
                  |> List.length

              let covered =
                  [ Nodes; Lenient; Reject ] |> List.sumBy (fun f -> (fixtures f).Length)

              Expect.equal covered nodeDecoder "every node-decoder fixture has a parity row"
          } ]
