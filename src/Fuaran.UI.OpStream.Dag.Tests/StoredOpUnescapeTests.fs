module Fuaran.UI.OpStream.Dag.Tests.StoredOpUnescapeTests

open System
open System.Reflection
open System.Text
open Expecto
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  Phase 2028: the stored-op string readers take a no-backslash fast path and
//  copy runs between escapes. The claim is that NOTHING observable moved: each
//  reader returns the same value, or fails with the same exception type and
//  message, for every input. So each live reader is compared against a frozen
//  copy of its pre-2028 body (`Pre2028` below, verbatim apart from layout), over
//  a hand-written edge table and a seeded fuzz over an escape-dense alphabet.
//
//  The readers are private (or internal to another assembly) by design, so they
//  are reached by reflection. A rename makes `reader` throw, which reads red:
//  the comparison must never pass by finding nothing to compare.
// ============================================================================

/// The pre-2028 bodies, frozen. Do not "fix" these: they are the oracle.
module private Pre2028 =

    /// `StreamEntry.unquote` before Phase 2028.
    let streamEntryUnquote (raw: string) : string =
        let inner =
            if raw.Length < 2 then
                raw
            else
                raw.Substring(1, raw.Length - 2)

        let sb = StringBuilder()
        let mutable i = 0

        let hexDigit (c: char) : int =
            if c >= '0' && c <= '9' then int c - int '0'
            elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
            elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
            else -1

        while i < inner.Length do
            if inner[i] = '\\' && i + 1 < inner.Length then
                match inner[i + 1] with
                | '"' ->
                    sb.Append '"' |> ignore
                    i <- i + 2
                | '\\' ->
                    sb.Append '\\' |> ignore
                    i <- i + 2
                | '/' ->
                    sb.Append '/' |> ignore
                    i <- i + 2
                | 'n' ->
                    sb.Append '\n' |> ignore
                    i <- i + 2
                | 'r' ->
                    sb.Append '\r' |> ignore
                    i <- i + 2
                | 't' ->
                    sb.Append '\t' |> ignore
                    i <- i + 2
                | 'b' ->
                    sb.Append '\b' |> ignore
                    i <- i + 2
                | 'f' ->
                    sb.Append '\f' |> ignore
                    i <- i + 2
                | 'u' when i + 5 < inner.Length ->
                    let d0 = hexDigit inner[i + 2]
                    let d1 = hexDigit inner[i + 3]
                    let d2 = hexDigit inner[i + 4]
                    let d3 = hexDigit inner[i + 5]

                    if d0 >= 0 && d1 >= 0 && d2 >= 0 && d3 >= 0 then
                        sb.Append(char ((d0 <<< 12) ||| (d1 <<< 8) ||| (d2 <<< 4) ||| d3)) |> ignore
                    else
                        sb.Append(inner.Substring(i, 6)) |> ignore

                    i <- i + 6
                | other ->
                    sb.Append other |> ignore
                    i <- i + 2
            else
                sb.Append inner[i] |> ignore
                i <- i + 1

        sb.ToString()

    /// `Actor.readString` before Phase 2028.
    let actorReadString (s: string) (i: int) : (string * int) option =
        if i >= s.Length || s[i] <> '"' then
            None
        else
            let sb = StringBuilder()
            let mutable j = i + 1
            let mutable finished = false
            let mutable ok = true

            while ok && not finished && j < s.Length do
                let c = s[j]

                if c = '"' then
                    finished <- true
                    j <- j + 1
                elif c = '\\' then
                    if j + 1 >= s.Length then
                        ok <- false
                    else
                        match s[j + 1] with
                        | '"' ->
                            sb.Append '"' |> ignore
                            j <- j + 2
                        | '\\' ->
                            sb.Append '\\' |> ignore
                            j <- j + 2
                        | 'u' when j + 5 < s.Length ->
                            let hexDigit (c: char) : int =
                                if c >= '0' && c <= '9' then int c - int '0'
                                elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
                                elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
                                else -1

                            let d0 = hexDigit s[j + 2]
                            let d1 = hexDigit s[j + 3]
                            let d2 = hexDigit s[j + 4]
                            let d3 = hexDigit s[j + 5]

                            if d0 >= 0 && d1 >= 0 && d2 >= 0 && d3 >= 0 then
                                let code = (d0 <<< 12) ||| (d1 <<< 8) ||| (d2 <<< 4) ||| d3
                                sb.Append(char code) |> ignore
                                j <- j + 6
                            else
                                ok <- false
                        | other ->
                            sb.Append other |> ignore
                            j <- j + 2
                else
                    sb.Append c |> ignore
                    j <- j + 1

            if ok && finished then Some(sb.ToString(), j) else None

    /// `DagWire.unquote` and `OpRecordWire.unquote` before Phase 2028 (one body).
    let wireUnquote (raw: string) : string =
        let inner = raw.Substring(1, raw.Length - 2)
        let sb = StringBuilder()
        let mutable i = 0

        while i < inner.Length do
            if inner[i] = '\\' && i + 1 < inner.Length then
                match inner[i + 1] with
                | '"' ->
                    sb.Append '"' |> ignore
                    i <- i + 2
                | '\\' ->
                    sb.Append '\\' |> ignore
                    i <- i + 2
                | 'n' ->
                    sb.Append '\n' |> ignore
                    i <- i + 2
                | 't' ->
                    sb.Append '\t' |> ignore
                    i <- i + 2
                | 'u' when i + 5 < inner.Length ->
                    let code = Convert.ToInt32(inner.Substring(i + 2, 4), 16)
                    sb.Append(char code) |> ignore
                    i <- i + 6
                | other ->
                    sb.Append other |> ignore
                    i <- i + 2
            else
                sb.Append inner[i] |> ignore
                i <- i + 1

        sb.ToString()

    /// `SqliteSink`'s `ResultEnvelopeJson.decode` Failure branch before Phase 2028
    /// (`extractString` and the code/message pairing around it).
    let failureEnvelope (json: string) : Result<OpResultEnvelope, string> =
        let extractString (key: string) : string option =
            let needle = sprintf "\"%s\":\"" key

            match json.IndexOf(needle, StringComparison.Ordinal) with
            | -1 -> None
            | start ->
                let valueStart = start + needle.Length
                let mutable i = valueStart
                let sb = StringBuilder()
                let mutable closed = false
                let mutable failed = false

                while not closed && not failed && i < json.Length do
                    let c = json[i]

                    if c = '\\' && i + 1 < json.Length then
                        match json[i + 1] with
                        | '"' ->
                            sb.Append '"' |> ignore
                            i <- i + 2
                        | '\\' ->
                            sb.Append '\\' |> ignore
                            i <- i + 2
                        | 'u' when i + 5 < json.Length ->
                            let hex = json.Substring(i + 2, 4)

                            match
                                UInt16.TryParse(
                                    hex,
                                    Globalization.NumberStyles.HexNumber,
                                    Globalization.CultureInfo.InvariantCulture
                                )
                            with
                            | true, cp ->
                                sb.Append(char cp) |> ignore
                                i <- i + 6
                            | false, _ -> failed <- true
                        | _ -> failed <- true
                    elif c = '"' then
                        closed <- true
                    else
                        sb.Append c |> ignore
                        i <- i + 1

                if closed && not failed then Some(sb.ToString()) else None

        match extractString "code", extractString "message" with
        | Some code, Some message -> Ok(OpResultEnvelope.Failure(code, message))
        | _ -> Error("OpResultEnvelope.Failure: missing code or message field")

    /// `SqliteDagSink`'s `DagJson.decodeParents` before Phase 2028.
    let decodeParents (json: string) : string list =
        let trimmed = json.Trim()

        if trimmed = "[]" || trimmed = "" then
            []
        else
            let inner = trimmed.Trim('[', ']')
            let result = ResizeArray<string>()
            let sb = StringBuilder()
            let mutable inStr = false
            let mutable escaped = false

            for ch in inner do
                if escaped then
                    sb.Append ch |> ignore
                    escaped <- false
                elif ch = '\\' then
                    escaped <- true
                elif ch = '"' then
                    if inStr then
                        result.Add(sb.ToString())
                        sb.Clear() |> ignore

                    inStr <- not inStr
                elif inStr then
                    sb.Append ch |> ignore

            List.ofSeq result

// --- the live readers, by reflection ----------------------------------------

let private flags =
    BindingFlags.Static ||| BindingFlags.NonPublic ||| BindingFlags.Public

let private reader (asm: Assembly) (typeName: string) (name: string) : MethodInfo =
    match asm.GetType typeName with
    | null -> failwithf "Phase 2028 test: type %s not found in %s" typeName asm.FullName
    | t ->
        match t.GetMethods flags |> Array.tryFind (fun m -> m.Name = name) with
        | Some m -> m
        | None -> failwithf "Phase 2028 test: %s.%s not found" typeName name

/// Invoke and unbox, unwrapping the reflection layer so an exception compares
/// as the reader itself raised it. `None` and other null-represented values
/// come back as the type's default, which is what they are.
let private invoke<'T> (m: MethodInfo) (args: objnull array) : 'T =
    let result =
        try
            m.Invoke(null, args)
        with :? TargetInvocationException as tie ->
            match tie.InnerException with
            | null -> reraise ()
            | inner ->
                Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw()
                failwith "unreachable"

    match result with
    | null -> Unchecked.defaultof<'T>
    | value -> unbox<'T> value

let private abstractionsAsm = typeof<OpResultEnvelope>.Assembly
let private dagAsm = typeof<DagOpRecord<obj>>.Assembly
let private replayAsm = typeof<Fuaran.UI.OpStream.Replay.GuestExportBundle>.Assembly
let private sqliteAsm = Assembly.Load "Fuaran.UI.OpStream.Sqlite"
let private dagSqliteAsm = Assembly.Load "Fuaran.UI.OpStream.Dag.Sqlite"

let private streamEntryUnquote =
    lazy (reader abstractionsAsm "Fuaran.UI.OpStream.Abstractions.StreamEntry" "unquote")

let private actorReadString =
    lazy (reader abstractionsAsm "Fuaran.UI.OpStream.Abstractions.ActorModule" "readString")

let private dagUnquote =
    lazy (reader dagAsm "Fuaran.UI.OpStream.Dag.Abstractions.DagWire" "unquote")

let private guestUnquote =
    lazy (reader replayAsm "Fuaran.UI.OpStream.Replay.OpRecordWire" "unquote")

let private sqliteEnvelope =
    lazy (reader sqliteAsm "Fuaran.UI.OpStream.SqliteStore.ResultEnvelopeJson" "decode")

let private dagParents =
    lazy (reader dagSqliteAsm "Fuaran.UI.OpStream.Dag.Sqlite.DagJson" "decodeParents")

/// A reader's observable outcome: its value, or the exception type and message.
let private outcome (f: unit -> 'a) : Result<'a, string * string> =
    try
        Ok(f ())
    with ex ->
        Error(ex.GetType().ToString(), ex.Message)

/// The six readers, each paired live-vs-frozen over one raw string content
/// (the characters between the quotes, as stored).
let private compareAll (content: string) : unit =
    let span = "\"" + content + "\""

    let same (name: string) (live: Result<'a, string * string>) (frozen: Result<'a, string * string>) =
        Expect.equal live frozen (sprintf "%s diverged from its pre-2028 body on %A" name content)

    same
        "StreamEntry.unquote"
        (outcome (fun () -> invoke<string> streamEntryUnquote.Value [| box span |]))
        (outcome (fun () -> Pre2028.streamEntryUnquote span))

    // Actor.readString reads from an offset inside a larger document, so read
    // the value in place, followed by more JSON, and also as a truncated tail.
    for doc, at in
        [ "{\"id\":" + span + ",\"kind\":\"human\"}", 6
          span, 0
          "x" + "\"" + content, 1 ] do
        same
            "Actor.readString"
            (outcome (fun () -> invoke<(string * int) option> actorReadString.Value [| box doc; box at |]))
            (outcome (fun () -> Pre2028.actorReadString doc at))

    same
        "DagWire.unquote"
        (outcome (fun () -> invoke<string> dagUnquote.Value [| box span |]))
        (outcome (fun () -> Pre2028.wireUnquote span))

    same
        "OpRecordWire.unquote"
        (outcome (fun () -> invoke<string> guestUnquote.Value [| box span |]))
        (outcome (fun () -> Pre2028.wireUnquote span))

    let envelope =
        "{\"$type\":\"Failure\",\"code\":" + span + ",\"message\":\"m" + content + "\"}"

    same
        "SqliteSink extractString"
        (outcome (fun () -> invoke<Result<OpResultEnvelope, string>> sqliteEnvelope.Value [| box envelope |]))
        (outcome (fun () -> Pre2028.failureEnvelope envelope))

    for parents in [ "[" + span + "]"; "[" + span + "," + span + "]"; "[" + span; span + "\"" ] do
        same
            "SqliteDagSink.decodeParents"
            (outcome (fun () -> invoke<string list> dagParents.Value [| box parents |]))
            (outcome (fun () -> Pre2028.decodeParents parents))

/// Raw string contents as stored, one per edge the fast path and the run copy
/// could get wrong.
let private edgeTable: (string * string) list =
    [ "empty", ""
      "no backslash", "s-01HZX3K9P2Q7R8T5V6W4Y1Z0AB"
      "64-hex hash", "55ece62de2872db695191249edf1cc84aeab78ffb074542bcda17fcff7cf80ef"
      "non-ASCII, no backslash", "naïve 日本"
      "escape at the first position", "\\\"start"
      "escape at the last position", "end\\\""
      "lone backslash at the end (truncated escape)", "end\\"
      "lone backslash only", "\\"
      "adjacent escapes", "\\n\\t\\\\\\\"\\/"
      "run between escapes", "a\\nbc\\tdef\\\\g"
      "every shorthand", "\\b\\f\\r\\n\\t\\/\\\\\\\""
      "unicode escape", "caf\\u00e9"
      "unicode escape, upper hex", "\\u00C9x"
      "unicode escape at the end", "x\\u00e9"
      "bad hex in \\u", "\\u00g9 tail"
      "bad hex in \\u at the end", "tail \\uZZZZ"
      "truncated \\u at end of input", "abc\\u12"
      "truncated \\u, three digits", "\\u123"
      "unknown escape", "\\q\\z"
      "escaped backslash then u", "\\\\u0041"
      "surrogate pair by escape", "\\ud83d\\ude00" ]

/// A seeded fuzz over an alphabet dense in escape-relevant characters, so the
/// boundaries between runs and escapes are crossed in every arrangement.
let private fuzzContents (count: int) : string list =
    let alphabet =
        [| 'a'; 'Z'; '\\'; '"'; 'u'; '0'; '9'; 'f'; 'F'; 'g'; 'n'; 't'; '/'; 'é'; ' ' |]

    let rng = Random 2028

    [ for _ in 1..count ->
          let len = rng.Next(0, 14)
          String(Array.init len (fun _ -> alphabet[rng.Next alphabet.Length])) ]

let private decodeOp (s: string) : Result<TreeOp<obj>, string> =
    JsonDecode.decodeOp s |> Result.mapError (sprintf "%A")

[<Tests>]
let tests =
    testList
        "Phase 2028 - stored-op unescapers: fast path and run copy change nothing observable"
        [ testList
              "edge table: every reader equals its pre-2028 body"
              [ for name, content in edgeTable -> testCase name (fun () -> compareAll content) ]

          testCase "seeded fuzz: every reader equals its pre-2028 body on 20000 escape-dense strings"
          <| fun () ->
              for content in fuzzContents 20000 do
                  compareAll content

          testCase "a string with no backslash comes back as the inner substring itself"
          <| fun () ->
              let content = "plain-run-0123456789"

              Expect.equal
                  (invoke<string> streamEntryUnquote.Value [| box ("\"" + content + "\"") |])
                  content
                  "StreamEntry.unquote"

              Expect.equal (invoke<string> dagUnquote.Value [| box ("\"" + content + "\"") |]) content "DagWire.unquote"

              Expect.equal
                  (invoke<(string * int) option> actorReadString.Value [| box ("\"" + content + "\"!"); box 0 |])
                  (Some(content, content.Length + 2))
                  "Actor.readString returns the value and the index past the closing quote"

          testCase "bad \\u hex: DagWire.decodeRecord's caught error text is unchanged"
          <| fun () ->
              // The failure path is untouched on purpose: Convert.ToInt32 still throws,
              // decodeRecord still catches it, and the message it carries is the same.
              let good =
                  """{"actor":{"kind":"human","id":"u"},"hash":"h","op":{"$type":"Batch","ops":[]},"parents":[],"streamId":"s1","timestamp":1700000000}"""

              let bad = good.Replace("\"streamId\":\"s1\"", "\"streamId\":\"s\\u00g1\"")

              let expected =
                  match outcome (fun () -> Pre2028.wireUnquote "\"s\\u00g1\"") with
                  | Error(_, message) -> Error(sprintf "DagWire.decodeRecord: %s" message)
                  | Ok v -> failwithf "the frozen body accepted bad hex: %s" v

              Expect.isOk (DagWire.decodeRecord decodeOp good |> Result.map ignore) "the control record decodes"

              Expect.equal (DagWire.decodeRecord decodeOp bad |> Result.map ignore) expected "same error text as before"

          testCase "bad \\u hex: StreamEntry keeps the six literal characters, Actor and the sqlite envelope refuse"
          <| fun () ->
              Expect.equal
                  (invoke<string> streamEntryUnquote.Value [| box "\"a\\u00g9b\"" |])
                  "a\\u00g9b"
                  "StreamEntry.unquote reads a bad \\u as the literal six characters"

              Expect.isNone (Actor.tryDecode """{"kind":"human","id":"a\u00g9"}""") "Actor refuses a bad \\u"

              Expect.equal
                  (invoke<Result<OpResultEnvelope, string>>
                      sqliteEnvelope.Value
                      [| box """{"$type":"Failure","code":"E","message":"a\u00g9"}""" |])
                  (Error "OpResultEnvelope.Failure: missing code or message field")
                  "the sqlite envelope refuses a bad \\u with the same message" ]
