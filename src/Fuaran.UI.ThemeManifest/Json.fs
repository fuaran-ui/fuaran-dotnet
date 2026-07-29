namespace Fuaran.UI.ThemeManifest

// ─── Minimal portable JSON AST + parser + serializer ────────────
//
// A tiny, dependency-free JSON implementation so `Fuaran.UI.ThemeManifest`
// stays genuinely `FSharp.Core`-only (FGP 2) and runs byte-identically
// under both the Fable browser pipeline and the pure-.NET Expecto runner
// (FGP 4). `Fable.SimpleJson`'s .NET parser type-initializer is not
// reliable on the .NET 10 runner, and `System.Text.Json` is server-only
// (not Fable-portable) — so neither fits a portable language-tier package.
//
// `JObj` carries an **ordered** `(string * JsonValue) list` so encode is
// deterministic (stable key order → cache-friendly, byte-stable
// round-trips) and decode preserves document order.

type JsonValue =
    | JStr of string
    | JNum of float
    | JBool of bool
    | JNull
    | JArr of JsonValue list
    | JObj of (string * JsonValue) list

module Json =
    open System.Globalization
    open System.Text

    let private inv = CultureInfo.InvariantCulture

    // ─── Serialize ──────────────────────────────────────────────

    let private escape (s: string) : string =
        let sb = StringBuilder(s.Length + 2)

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | '\b' -> sb.Append "\\b" |> ignore
            | '\f' -> sb.Append "\\f" |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append c |> ignore

        sb.ToString()

    let private numStr (n: float) : string =
        // Integral floats serialise without a decimal point; the wire
        // value round-trips back to the same float either way.
        if System.Double.IsNaN n || System.Double.IsInfinity n then
            "0"
        elif n = floor n && abs n < 1e15 then
            (int64 n).ToString(inv)
        else
            // Phase 565 (fuaran#565): the `"R"` round-trip specifier is not
            // supported by Fable; plain invariant `ToString` is
            // shortest-round-trippable on .NET Core (same output for these
            // theme values) and Fable-safe — the `JsonEncode.float` convention.
            n.ToString(inv)

    let rec serialize (v: JsonValue) : string =
        match v with
        | JNull -> "null"
        | JBool b -> if b then "true" else "false"
        | JNum n -> numStr n
        | JStr s -> "\"" + escape s + "\""
        | JArr items -> "[" + (items |> List.map serialize |> String.concat ",") + "]"
        | JObj pairs ->
            "{"
            + (pairs
               |> List.map (fun (k, v) -> "\"" + escape k + "\":" + serialize v)
               |> String.concat ",")
            + "}"

    // ─── Parse (recursive descent) ──────────────────────────────

    let parse (input: string) : Result<JsonValue, string> =
        let n = input.Length
        let mutable i = 0

        let fail (msg: string) : 'a =
            raise (System.FormatException(sprintf "JSON parse error at %d: %s" i msg))

        let skipWs () =
            while i < n
                  && (input[i] = ' ' || input[i] = '\t' || input[i] = '\n' || input[i] = '\r') do
                i <- i + 1

        let expect (c: char) =
            if i < n && input[i] = c then
                i <- i + 1
            else
                fail (sprintf "expected '%c'" c)

        let parseHex4 () : int =
            if i + 4 > n then
                fail "truncated \\u escape"

            let h = (input.Substring(i, 4)).Trim()
            i <- i + 4

            // Hand-rolled `Int32.TryParse(h, NumberStyles.HexNumber, inv)`:
            // Fable cannot honour the `IFormatProvider` overload and says so,
            // which under this repo's `TreatWarningsAsErrors` stops the
            // transpile of every Fable consumer. The provider is semantically
            // inert for hex, and the BCL contract reproduced here is
            // `AllowLeadingWhite ||| AllowTrailingWhite` (the `Trim()` above)
            // then one or more case-insensitive hex digits — so this stays
            // byte-identical on both pipelines, which is the whole point of
            // this parser (see the module header).
            //
            // Differentially tested against the BCL overload over every 1- and
            // 2-char ASCII input and a 4-char hex/whitespace/sign alphabet. ONE
            // divergence class exists and is deliberate: the BCL silently
            // accepts a single trailing NUL (`"f\000"` -> 15, a legacy quirk of
            // the number parser); this rejects it. A NUL inside a `\uXXXX`
            // escape is malformed input either way.
            let mutable acc = 0
            let mutable ok = h.Length > 0

            for c in h do
                if c >= '0' && c <= '9' then
                    acc <- acc * 16 + (int c - int '0')
                elif c >= 'a' && c <= 'f' then
                    acc <- acc * 16 + (int c - int 'a' + 10)
                elif c >= 'A' && c <= 'F' then
                    acc <- acc * 16 + (int c - int 'A' + 10)
                else
                    ok <- false

            if ok then acc else fail "bad \\u escape"

        let parseString () : string =
            expect '"'
            let sb = StringBuilder()
            let mutable finished = false

            while not finished do
                if i >= n then
                    fail "unterminated string"

                let c = input[i]
                i <- i + 1

                match c with
                | '"' -> finished <- true
                | '\\' ->
                    if i >= n then
                        fail "unterminated escape"

                    let e = input[i]
                    i <- i + 1

                    match e with
                    | '"' -> sb.Append '"' |> ignore
                    | '\\' -> sb.Append '\\' |> ignore
                    | '/' -> sb.Append '/' |> ignore
                    | 'n' -> sb.Append '\n' |> ignore
                    | 'r' -> sb.Append '\r' |> ignore
                    | 't' -> sb.Append '\t' |> ignore
                    | 'b' -> sb.Append '\b' |> ignore
                    | 'f' -> sb.Append '\f' |> ignore
                    | 'u' -> sb.Append(char (parseHex4 ())) |> ignore
                    | _ -> fail "bad escape"
                | _ -> sb.Append c |> ignore

            sb.ToString()

        let parseLiteral (lit: string) (value: JsonValue) : JsonValue =
            if i + lit.Length <= n && input.Substring(i, lit.Length) = lit then
                i <- i + lit.Length
                value
            else
                fail (sprintf "expected '%s'" lit)

        let parseNumber () : JsonValue =
            let start = i

            if i < n && (input[i] = '-' || input[i] = '+') then
                i <- i + 1

            while i < n
                  && (let c = input[i] in (c >= '0' && c <= '9') || c = '.' || c = 'e' || c = 'E' || c = '+' || c = '-') do
                i <- i + 1

            let tok = input.Substring(start, i - start)

            // Fable's `Double.tryParse` is JS `+str` — culture-invariant by
            // construction — and it IGNORES both the `NumberStyles` and the
            // `IFormatProvider` argument, saying so loudly enough to fail the
            // transpile under this repo's `TreatWarningsAsErrors`. Over the
            // alphabet this tokeniser can emit (`[-+0-9.eE]`) the two forms agree
            // exactly: same sign / decimal-point / exponent grammar, same IEEE-754
            // rounding, same rejection of `--5` / `1e` / `1.2.3` / the empty token.
            // The .NET leg keeps the explicit invariant overload, because the
            // single-argument BCL overload reads CurrentCulture and would misread
            // `1.5` under a comma-decimal locale — which is precisely the
            // byte-identical-on-both-pipelines property this module exists for.
            let parsed =
#if FABLE_COMPILER
                System.Double.TryParse tok
#else
                System.Double.TryParse(tok, NumberStyles.Float, inv)
#endif

            match parsed with
            | true, v -> JNum v
            | _ -> fail (sprintf "bad number '%s'" tok)

        let rec parseValue () : JsonValue =
            skipWs ()

            if i >= n then
                fail "unexpected end of input"

            match input[i] with
            | '{' -> parseObject ()
            | '[' -> parseArray ()
            | '"' -> JStr(parseString ())
            | 't' -> parseLiteral "true" (JBool true)
            | 'f' -> parseLiteral "false" (JBool false)
            | 'n' -> parseLiteral "null" JNull
            | _ -> parseNumber ()

        and parseObject () : JsonValue =
            expect '{'
            skipWs ()
            let pairs = ResizeArray<string * JsonValue>()

            if i < n && input[i] = '}' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    skipWs ()
                    let key = parseString ()
                    skipWs ()
                    expect ':'
                    let value = parseValue ()
                    pairs.Add(key, value)
                    skipWs ()

                    if i < n && input[i] = ',' then
                        i <- i + 1
                    else
                        expect '}'
                        go <- false

            JObj(List.ofSeq pairs)

        and parseArray () : JsonValue =
            expect '['
            skipWs ()
            let items = ResizeArray<JsonValue>()

            if i < n && input[i] = ']' then
                i <- i + 1
            else
                let mutable go = true

                while go do
                    let value = parseValue ()
                    items.Add value
                    skipWs ()

                    if i < n && input[i] = ',' then
                        i <- i + 1
                    else
                        expect ']'
                        go <- false

            JArr(List.ofSeq items)

        try
            let v = parseValue ()
            skipWs ()

            if i <> n then
                Error(sprintf "trailing content at %d" i)
            else
                Ok v
        with ex ->
            Error ex.Message

    // ─── Accessors ──────────────────────────────────────────────

    let asString =
        function
        | JStr s -> Some s
        | _ -> None

    let asNumber =
        function
        | JNum n -> Some n
        | _ -> None

    let asArray =
        function
        | JArr a -> Some a
        | _ -> None

    let asObject =
        function
        | JObj o -> Some o
        | _ -> None

    /// Look up a key in an object's ordered pair list (first match).
    let field (name: string) (pairs: (string * JsonValue) list) : JsonValue option =
        pairs |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
