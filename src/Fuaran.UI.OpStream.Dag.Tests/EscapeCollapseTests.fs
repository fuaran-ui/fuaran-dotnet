module Fuaran.UI.OpStream.Dag.Tests.EscapeCollapseTests

open System
open System.Reflection
open System.Text
open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Replay

// ============================================================================
//  Phase 2026: the op-stream tier's private string escapers collapsed onto
//  Core's `Json.escapeInto`. The claim is that NO byte moved: every site that
//  used to spell a string through its own copy of the canonical escape spells
//  it, now, through Core's, and produces what the copy produced.
//
//  So the pre-2026 body is frozen below as the ORACLE, and every collapsed
//  site is held to it over an edge table, Core's `StringEscapeVectors` and a
//  seeded fuzz. A site whose output is visible is checked by substitution: its
//  output for a clean marker is a template, and the marker's quoted spelling is
//  replaced by the oracle's spelling of the input. A site whose output is a
//  hash is held to a digest captured on the tree before the change. The two
//  SQLite sinks keep their envelope codecs private, so those are reached by
//  reflection (the Phase 2028 precedent): a rename throws, which reads red.
// ============================================================================

/// The pre-2026 body, frozen: one character at a time, control characters
/// through `sprintf`. Do not "fix" it: it is the oracle.
let private pre2026Quoted (s: string) : string =
    let sb = StringBuilder()
    sb.Append '"' |> ignore

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

/// A run-copying escaper with its run boundary deliberately broken: the clean
/// run before each escape loses its last character. It exists to show the
/// comparison below is not vacuous: held to the oracle, it must go red.
let private brokenRunBoundary (s: string) : string =
    let sb = StringBuilder()
    sb.Append '"' |> ignore
    let mutable start = 0

    for i in 0 .. s.Length - 1 do
        let c = s[i]

        if c = '"' || c = '\\' || c < ' ' then
            if i - start > 1 then
                sb.Append(s, start, i - start - 1) |> ignore

            sb.Append(pre2026Quoted (string c).[1 .. 2 + (if c < ' ' then 4 else 0)])
            |> ignore

            start <- i + 1

    if s.Length > start then
        sb.Append(s, start, s.Length - start) |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

let private ch (code: int) : string = string (char code)

/// The edge table: clean, escape-only, an escape at the first and at the last
/// position, runs between escapes, every control character alone and all of
/// them in one string, surrogate pairs and lone surrogates (built from char
/// values: a lone surrogate in an F# literal compiles as U+FFFD).
let private edgeTable: (string * string) list =
    [ "empty", ""
      "clean", "streamA-01HZX3K9P2Q7R8T5V6W4Y1Z0AB"
      "64-hex hash", "55ece62de2872db695191249edf1cc84aeab78ffb074542bcda17fcff7cf80ef"
      "escape only", "\"\\\n\u0001"
      "escape at the first position", "\"start"
      "escape at the last position", "end\\"
      "runs between escapes", "ab\ncd\"ef\\gh\u001fij"
      "adjacent escapes", "\"\"\\\\\n\n"
      "DEL and non-ASCII pass through", "\u007fnaïve 日本 é"
      "surrogate pair", "smile " + ch 0xD83D + ch 0xDE00 + " ok"
      "lone high surrogate", "a" + ch 0xD83D + "b"
      "lone low surrogate", "a" + ch 0xDE00 + "b"
      "lone surrogate at the end", "tail" + ch 0xD800
      "every control character", String.Join("", [ for c in 0..0x1F -> ch c ]) ]
    @ [ for c in 0..0x1F -> sprintf "U+%04X alone" c, ch c ]
    @ [ for c in 0..0x1F -> sprintf "U+%04X between runs" c, "run" + ch c + "run" ]

/// Core's own vector table: every input the spine's escapers are pinned over.
let private vectorTable: (string * string) list =
    [ for v in StringEscapeVectors.vectors -> "vector " + v.Name, v.Input ]

/// A seeded fuzz over an alphabet dense in escapes, so runs and escapes meet in
/// every arrangement.
let private fuzzTable: (string * string) list =
    let alphabet =
        [| "a"
           "b"
           " "
           "\""
           "\\"
           "\n"
           "\t"
           "\u0000"
           "\u001f"
           "é"
           ch 0xD83D
           ch 0xDE00
           "/" |]

    let rng = Random 2026

    [ for n in 1..400 ->
          let len = rng.Next(0, 24)
          sprintf "fuzz %d" n, String.Join("", [ for _ in 1..len -> alphabet[rng.Next alphabet.Length] ]) ]

let private allInputs = edgeTable @ vectorTable @ fuzzTable

// --- the sites ---------------------------------------------------------------

let private marker = "MARKER2026"
let private ts = DateTimeOffset.FromUnixTimeSeconds 1_700_000_000L

let private updateWith (v: JVal) : TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "n", "p", PropValue.Wire v)

let private node: Node<obj> = Fuaran.UI.Fuaran.markdown "root" "hello"

/// The visible sites: each spells every string it is handed through the
/// collapsed escape, so its output for `s` is its output for the marker with
/// the marker's quoted spelling replaced by the oracle's spelling of `s`.
let private visibleSites: (string * (string -> string)) list =
    [ "CanonicalJson value", (fun s -> CanonicalJson.encodeOp (updateWith (JStr s)))
      "CanonicalJson key", (fun s -> CanonicalJson.encodeOp (updateWith (JObj [ s, JInt 1 ])))
      "Actor.encode", (fun s -> Actor.encode (Actor.Human s))
      "Actor.encode agent", (fun s -> Actor.encode (Actor.Agent(s, s, s)))
      "StreamEntry.encodeResult", (fun s -> StreamEntry.encodeResult (OpResultEnvelope.Failure(s, s)))
      "SegmentDescriptor.encode",
      (fun s ->
          SegmentDescriptor.encode
              { Algorithm = s
                ChainFormatVersion = 2
                StreamId = s
                FromSeq = 1
                ToSeq = 2
                PreviousHash = s
                Head = s })
      "DagWire.encodeRecord",
      (fun s ->
          DagWire.encodeRecord
              CanonicalJson.encodeOp
              { StreamId = s
                Hash = s
                Parents = [ s; s ]
                Op = updateWith (JStr "v")
                OutcomeHash = Some s
                PromptId = Some s
                Actor = Actor.Human s
                Timestamp = ts
                ResultEnvelope = OpResultEnvelope.Failure(s, s)
                Tombstoned = false })
      "MergeConflict.encodeEnvelope",
      (fun s ->
          MergeConflict.encodeEnvelope
              [ { NodeId = "n"
                  Facet = "f"
                  Class = MergeConflictClass.ConcurrentEdit
                  Base = "b"
                  A = Some { Value = s; Tag = Some s }
                  B = Some { Value = s; Tag = None }
                  Primary = None
                  Secondary = None
                  SecondaryTag = None
                  PrimacyHeld = false
                  Choices = []
                  Hint = ApplyHint.empty } ])
      "ValidatorGate.encodeVerdict",
      (fun s ->
          ValidatorGate.encodeVerdict
              [ { Code = "C"
                  NodeId = "n"
                  Facet = "f"
                  Message = s } ])
      "GuestExport.encode",
      (fun s ->
          GuestExport.encode
              { FormatVersion = 1
                ScopeId = s
                InitialTree = node
                Records = [] }) ]

/// The hash sites: the content addresses whose pre-images carry the strings.
let private hashSites: (string * (string -> string)) list =
    [ "HashChain.computeHash",
      (fun s ->
          HashChain.computeHash
              HashChain.genesisPreviousHash
              (updateWith (JStr s))
              1
              ts
              (Actor.Human s)
              (Some s)
              (OpResultEnvelope.Failure(s, s)))
      "DagOpRecord.computeHash",
      (fun s ->
          DagOpRecord.computeHash
              [ s ]
              (updateWith (JStr s))
              ts
              (Actor.Human s)
              (Some s)
              (OpResultEnvelope.Failure(s, s)))
      "DagOpRecord.computeMergeHash",
      (fun s -> DagOpRecord.computeMergeHash [ s; "p" ] s ts (Actor.Human s) (Some s) OpResultEnvelope.Success) ]

/// The digest of every hash site over every input, captured on the tree BEFORE
/// the copies were deleted. A moved pre-image byte moves it.
[<Literal>]
let private pinnedHashDigest =
    "2ee99609ad42600e89b6efb033343e620890ddfb04663951a6d88473eca8537a"

// --- the private SQLite envelope codecs, by reflection --------------------------

let private flags =
    BindingFlags.Static ||| BindingFlags.NonPublic ||| BindingFlags.Public

let private method (asmName: string) (typeName: string) (name: string) : MethodInfo =
    let asm = Assembly.Load asmName

    match asm.GetType typeName with
    | null -> failwithf "Phase 2026 test: type %s not found in %s" typeName asmName
    | t ->
        match t.GetMethods flags |> Array.tryFind (fun m -> m.Name = name) with
        | Some m -> m
        | None -> failwithf "Phase 2026 test: %s.%s not found" typeName name

let private call<'T> (m: MethodInfo) (args: objnull array) : 'T =
    match m.Invoke(null, args) with
    | null -> Unchecked.defaultof<'T>
    | v -> unbox<'T> v

let private sqliteEncode =
    lazy (method "Fuaran.UI.OpStream.Sqlite" "Fuaran.UI.OpStream.SqliteStore.ResultEnvelopeJson" "encode")

let private sqliteDecode =
    lazy (method "Fuaran.UI.OpStream.Sqlite" "Fuaran.UI.OpStream.SqliteStore.ResultEnvelopeJson" "decode")

let private dagEncodeEnvelope =
    lazy (method "Fuaran.UI.OpStream.Dag.Sqlite" "Fuaran.UI.OpStream.SqliteStore.ResultEnvelopeJson" "encode")

let private dagDecodeEnvelope =
    lazy (method "Fuaran.UI.OpStream.Dag.Sqlite" "Fuaran.UI.OpStream.SqliteStore.ResultEnvelopeJson" "decode")

let private dagEncodeParents =
    lazy (method "Fuaran.UI.OpStream.Dag.Sqlite" "Fuaran.UI.OpStream.Dag.Sqlite.DagJson" "encodeParents")

let private failureJson (code: string) (message: string) =
    "{\"$type\":\"Failure\",\"code\":"
    + pre2026Quoted code
    + ",\"message\":"
    + pre2026Quoted message
    + "}"

/// The mismatches of `quote` against the oracle over `inputs`.
let private mismatches (quote: string -> string) (inputs: (string * string) list) : string list =
    [ for name, s in inputs do
          if quote s <> pre2026Quoted s then
              yield name ]

let private sha256Hex (s: string) : string =
    Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes s)
    |> Convert.ToHexString
    |> fun h -> h.ToLowerInvariant()

[<Tests>]
let tests =
    testList
        "Phase 2026 — the tier's escapers are Core's, byte for byte"
        [ testCase "Core's escape is the oracle over every input (parity before the copies were deleted)"
          <| fun () ->
              let viaCore (s: string) =
                  let sb = StringBuilder()
                  sb.Append '"' |> ignore
                  Json.escapeInto sb s
                  sb.Append '"' |> ignore
                  sb.ToString()

              Expect.isEmpty (mismatches viaCore allInputs) "Json.escapeInto differs from the pre-2026 body"
              Expect.isEmpty (mismatches JsonString.quote allInputs) "JsonString.quote differs from the pre-2026 body"

          testCase "a deliberately broken run boundary is red against the oracle"
          <| fun () ->
              let broken = mismatches brokenRunBoundary allInputs
              Expect.isNonEmpty broken "the comparison must catch a run boundary that drops a character"
              Expect.contains broken "runs between escapes" "the run-between-escapes row must expose it"

          testCase "every Core string-escape vector is spelled as the table pins it"
          <| fun () ->
              for v in StringEscapeVectors.vectors do
                  Expect.equal (pre2026Quoted v.Input) ("\"" + v.Escaped + "\"") $"oracle vs vector {v.Name}"

                  Expect.equal
                      (Actor.encode (Actor.Human v.Input))
                      ("{\"kind\":\"human\",\"id\":\"" + v.Escaped + "\"}")
                      $"Actor.encode vs vector {v.Name}"

          testCase "every visible site spells every input as the pre-2026 body did"
          <| fun () ->
              let quotedMarker = "\"" + marker + "\""

              for site, encode in visibleSites do
                  let template = encode marker

                  Expect.stringContains template quotedMarker $"{site}: the marker must reach the output"

                  for name, s in allInputs do
                      let expected = template.Replace(quotedMarker, pre2026Quoted s)
                      Expect.equal (encode s) expected $"{site} moved a byte on '{name}'"

          testCase "the hash sites' pre-images are unchanged (digest pinned before the change)"
          <| fun () ->
              let lines =
                  [ for site, hash in hashSites do
                        for name, s in allInputs -> site + "\t" + name + "\t" + hash s ]

              Expect.equal (sha256Hex (String.Join("\n", lines))) pinnedHashDigest "a hash pre-image moved"

          testCase "SqliteSink's envelope codec: the pre-2026 bytes, and every input round-trips"
          <| fun () ->
              for name, s in allInputs do
                  let envelope = OpResultEnvelope.Failure(s, "m" + s)
                  let json = call<string> sqliteEncode.Value [| box envelope |]
                  Expect.equal json (failureJson s ("m" + s)) $"SqliteSink envelope bytes on '{name}'"

                  Expect.equal
                      (call<Result<OpResultEnvelope, string>> sqliteDecode.Value [| box json |])
                      (Ok envelope)
                      $"SqliteSink envelope round trip on '{name}'"

          testCase "SqliteDagSink's envelope codec escapes control characters, round-trips, and reads old rows"
          <| fun () ->
              for name, s in allInputs do
                  let envelope = OpResultEnvelope.Failure(s, "m" + s)
                  let json = call<string> dagEncodeEnvelope.Value [| box envelope |]
                  Expect.equal json (failureJson s ("m" + s)) $"SqliteDagSink envelope bytes on '{name}'"

                  // Since Phase 1937 the DAG sink's column codec is the linear one,
                  // linked into its assembly (`SqliteStore.ResultEnvelopeJson`).
                  Expect.equal
                      (call<Result<OpResultEnvelope, string>> dagDecodeEnvelope.Value [| box json |])
                      (Ok envelope)
                      $"SqliteDagSink envelope round trip on '{name}'"

                  // A row the pre-2026 writer stored: only `\` and `"` escaped,
                  // a control character raw. It still reads.
                  let legacy (v: string) =
                      v.Replace("\\", "\\\\").Replace("\"", "\\\"")

                  let oldRow =
                      sprintf "{\"$type\":\"Failure\",\"code\":\"%s\",\"message\":\"%s\"}" (legacy s) (legacy ("m" + s))

                  Expect.equal
                      (call<Result<OpResultEnvelope, string>> dagDecodeEnvelope.Value [| box oldRow |])
                      (Ok envelope)
                      $"SqliteDagSink reads a pre-2026 row on '{name}'"

          testCase "SqliteDagSink's parents column is unchanged for every hash-shaped parent"
          <| fun () ->
              let parents =
                  [ "55ece62de2872db695191249edf1cc84aeab78ffb074542bcda17fcff7cf80ef"
                    "0000000000000000000000000000000000000000000000000000000000000000" ]

              Expect.equal
                  (call<string> dagEncodeParents.Value [| box parents |])
                  ("[" + String.Join(",", parents |> List.map pre2026Quoted) + "]")
                  "parents_json bytes" ]
