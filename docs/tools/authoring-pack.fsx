// authoring-pack.fsx — generate + drift-check the Fuaran AI-authoring pack.
//
// The artefact that makes a developer's own AI emit Fuaran is the system prompt +
// few-shot + JSON schema + tool definitions. Those must NOT drift from the canonical
// wire format the corpus + JsonDecode pin (flat `kind.$type`, spec fields hoisted, no
// `spec` wrapper). This script makes the corpus the single source of truth for every
// wire-shape example in the docs + prompt pack, so a hand edit cannot silently drift.
//
//   dotnet fsi authoring-pack.fsx --write    # regenerate every corpus-derived surface
//   dotnet fsi authoring-pack.fsx --check    # verify nothing drifted (exit 1 on drift)
//
// PROMPT-PACK example blocks are emitted as minified JSON BY DEFAULT (the adopted
// emission since the Phase 839 gate; the DEFAULT since Phase 838 — a bare `--write`
// used to emit the pretty form, which silently reverted the adopted minification
// whenever a session regenerated without the flag). `--pretty-examples` opts out for
// inspection; `--minify-examples` names the default explicitly and is accepted as a
// no-op. The decoder is whitespace-indifferent and the pack is a paid prefix, so the
// indentation buys a machine reader nothing and costs tokens on every request.
// Scope + guarantees:
//
//   * It applies to prompt-pack/system-prompt.md ONLY. AI_AUTHORING_GUIDE.md is a
//     human-facing document, is not part of the paid prefix, and stays pretty.
//   * It is a WHITESPACE-ONLY transform over the corpus bytes (see `minifyJson`), not
//     a re-serialisation. Key order, escaping and number formatting are the fixture's
//     own — round-tripping through JsonSerializer is NOT byte-safe (it re-escapes
//     embedded-JSON string payloads and re-formats some numbers), so a minified block
//     is exactly the corpus bytes with insignificant whitespace removed.
//   * few-shot.jsonl payloads are minified UNCONDITIONALLY, and always were: a JSONL
//     record cannot carry a raw newline, so the embedded tree has to be compact
//     whatever the flag says. The flag is a no-op there on a corpus that stores its
//     fixtures compact (which it does) — it only rescues a pretty-stored fixture.
//   * The drift check passes structurally in either emission (whitespace is
//     explicitly not drift — `canonicalize`), but under the minified default a
//     `--check` additionally asserts the pack IS in the minified form, so the build
//     gate pins the adopted emission; `--check --pretty-examples` waives that.
//
// Corpus-derived surfaces (cannot diverge from wire-format-fixtures/):
//   * Marker blocks  <!-- fuaran:example fixture=ID -->```json …```<!-- /fuaran:example -->
//     in the managed markdown files (the authoring guide + the pack system prompt).
//   * Marker block   <!-- fuaran:signature-catalogue --> … <!-- /fuaran:signature-catalogue -->
//     in the pack system prompt — the Phase 838 declaration-style signature catalogue
//     (every kind, field, payload DU and closed enum, `.d.ts`-flavoured), derived from
//     wire-format-fixtures/schema.json. Successor to the retired required-fields +
//     enum-vocab tables (Phase 422 / the 2026-07-15 smoke run created those; 838
//     re-encoded the same constraint set declaration-style at a fraction of the
//     token cost, spelling-complete on every closed vocabulary).
//   * prompt-pack/few-shot.jsonl   — one {prompt, decoder, fixture, tree} per curated id.
//   * prompt-pack/schema.json      — a byte copy of wire-format-fixtures/schema.json.
//
// The script reads only docs/ + wire-format-fixtures/ and writes only docs/. It is the
// generation source the Phase 110 drift-check Build target runs in --check mode.

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Text.Encodings.Web

// ── Paths ──────────────────────────────────────────────────────────────────────
let scriptDir = __SOURCE_DIRECTORY__ //                       fuaran-dotnet/docs/tools
let docsDir = Directory.GetParent(scriptDir).FullName //      fuaran-dotnet/docs
let fuaranDir = Directory.GetParent(docsDir).FullName //      fuaran
let workspaceRoot = Directory.GetParent(fuaranDir).FullName // <workspace root>
let fixturesDir = Path.Combine(workspaceRoot, "wire-format-fixtures")
let manifestPath = Path.Combine(fixturesDir, "manifest.json")
let schemaSrcPath = Path.Combine(fixturesDir, "schema.json")
let packDir = Path.Combine(docsDir, "prompt-pack")

// ── Mode ───────────────────────────────────────────────────────────────────────
let argv = fsi.CommandLineArgs |> Array.skip 1

let private usage () =
    eprintfn "usage: dotnet fsi authoring-pack.fsx (--write | --check | --mine) [--minify-examples | --pretty-examples]"

    exit 2

// `--mine` (Phase 841) is a READ-ONLY mode: it runs the set-cover exemplar miner over
// the generated rule→fixture coverage matrix and prints the report. It writes nothing,
// so it can be run against a dirty tree without touching the pack.
let mode =
    match argv |> Array.tryHead with
    | Some "--write" -> "write"
    | Some "--check" -> "check"
    | Some "--mine" -> "mine"
    | _ -> usage ()

let writeMode = mode = "write"
let mineMode = mode = "mine"

// An unrecognised trailing argument is a typo, not a no-op: silently ignoring
// `--minify-example` would emit an emission the caller did not choose.
match
    argv
    |> Array.skip 1
    |> Array.filter (fun a -> a <> "--minify-examples" && a <> "--pretty-examples")
with
| [||] -> ()
| unknown ->
    eprintfn "unknown argument(s): %s" (String.concat " " unknown)
    usage ()

// Minified is the DEFAULT (Phase 838): the pack's committed state is the adopted
// minified emission, and a bare `--write` regen must reproduce it rather than
// silently revert it. `--pretty-examples` opts out; `--minify-examples` names the
// default and remains accepted so older invocations keep working unchanged.
let minifyExamples = not (argv |> Array.contains "--pretty-examples")

// ── Corpus index ─────────────────────────────────────────────────────────────────
type FixtureMeta = { File: string; Decoder: string }

let fixtureById =
    use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.map (fun f ->
        f.GetProperty("id").GetString(),
        { File = f.GetProperty("inputFile").GetString()
          Decoder = f.GetProperty("decoder").GetString() })
    |> Map.ofSeq

let fixtureMeta (id: string) =
    match Map.tryFind id fixtureById with
    | Some m -> m
    | None -> failwithf "Unknown fixture id '%s' (not in wire-format-fixtures/manifest.json)" id

let fixtureRaw (id: string) =
    (File.ReadAllText(Path.Combine(fixturesDir, (fixtureMeta id).File))).Trim()

// ── JSON helpers ─────────────────────────────────────────────────────────────────
// Pretty form for human-facing docs: indented, with `<closure>` / `<opaque>` left
// literal (the relaxed encoder keeps `<` `>` unescaped, matching the canonical wire).
let private prettyOpts =
    JsonSerializerOptions(WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

let prettyJson (raw: string) =
    use doc = JsonDocument.Parse raw
    JsonSerializer.Serialize(doc.RootElement, prettyOpts)

// Minified form for the paid prompt prefix: the corpus bytes with every INSIGNIFICANT
// whitespace character removed and nothing else touched.
//
// Deliberately a scanner rather than `JsonSerializer.Serialize(…, WriteIndented=false)`.
// A re-serialisation would silently rewrite content the corpus pins: it re-escapes
// string payloads that themselves contain JSON (`nodes/btn-json-payloads.json` grows
// 757 → 787 chars through STJ) and re-formats some number literals
// (`nodes/code-1.json`, 156 → 152). Those are byte-parity changes against the
// conformance corpus dressed up as a whitespace cut. A scanner cannot do that: text
// inside string literals is copied verbatim (escapes included), so the output differs
// from the input in whitespace alone, which is the whole claim being made.
let minifyJson (raw: string) =
    use doc = JsonDocument.Parse raw // parse first: never emit bytes we could not read
    ignore doc

    let sb = StringBuilder(raw.Length)
    let mutable inString = false
    let mutable escaped = false

    for i in 0 .. raw.Length - 1 do
        let c = raw[i]

        if inString then
            sb.Append c |> ignore

            if escaped then
                escaped <- false
            elif c = '\\' then
                escaped <- true
            elif c = '"' then
                inString <- false
        else
            match c with
            | '"' ->
                inString <- true
                sb.Append c |> ignore
            | ' '
            | '\t'
            | '\n'
            | '\r' -> ()
            | _ -> sb.Append c |> ignore

    sb.ToString()

// Normalise line endings to LF. The repo pins `eol=lf` (.gitattributes), but the
// schema source lives in the workspace repo whose working tree may still carry CRLF;
// comparing + writing normalised content makes the drift check assert canonical
// *content*, immune to whatever line endings happen to be on disk.
let normalizeEol (s: string) =
    s.Replace("\r\n", "\n").Replace("\r", "\n")

// Structural canonical form for drift comparison: object keys Ordinal-sorted, arrays
// in source order, leaves verbatim. Whitespace / indentation differences do not count
// as drift — only a genuine structural divergence from the corpus does.
let canonicalize (raw: string) =
    use doc = JsonDocument.Parse raw
    let sb = StringBuilder()

    let rec go (el: JsonElement) =
        match el.ValueKind with
        | JsonValueKind.Object ->
            sb.Append '{' |> ignore

            el.EnumerateObject()
            |> Seq.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))
            |> Seq.iteri (fun i p ->
                if i > 0 then
                    sb.Append ',' |> ignore

                sb.Append(JsonSerializer.Serialize(p.Name)).Append ':' |> ignore
                go p.Value)

            sb.Append '}' |> ignore
        | JsonValueKind.Array ->
            sb.Append '[' |> ignore

            el.EnumerateArray()
            |> Seq.iteri (fun i v ->
                if i > 0 then
                    sb.Append ',' |> ignore

                go v)

            sb.Append ']' |> ignore
        | _ -> sb.Append(el.GetRawText()) |> ignore

    go doc.RootElement
    sb.ToString()

// ── Drift accumulation ───────────────────────────────────────────────────────────
let mutable drift: string list = []
let mutable wrote = 0
let reportDrift msg = drift <- msg :: drift

// Write a file only when the content actually changed (keeps --write idempotent and
// avoids touching mtimes needlessly); report drift in --check.
let reconcileFile (label: string) (path: string) (expected: string) =
    let expected = normalizeEol expected

    let current =
        if File.Exists path then
            normalizeEol (File.ReadAllText path)
        else
            null

    if current = expected then
        ()
    elif writeMode then
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllText(path, expected)
        wrote <- wrote + 1
        printfn "  wrote %s" label
    else
        reportDrift $"{label}: out of sync with the corpus (run authoring-pack.fsx --write)"

// ── Signature catalogue (Phase 838) ──────────────────────────────────────────────
// The whole type surface as a `.d.ts`-flavoured declaration block, derived from
// wire-format-fixtures/schema.json — the same derivation source as the retired
// required-fields + enum-vocab tables, re-encoded declaration-style. Design rules:
//
//   * `Name { field: type; opt?: type }` — required fields first (schema `required`
//     order), optional fields `?`-marked and Ordinal-sorted, `$type` implied by the
//     case / kind name.
//   * `A = B | C` — a closed `$type`-discriminated union, one case per `| ` line.
//   * SPELLING-COMPLETE on every closed vocabulary: enums referenced from exactly one
//     site inline their quoted values AT the use site (`"Group"|"Card"|…`); enums
//     used from several sites are declared once by name (`ToneVariant = …`) — the
//     measured catalogue-stub arm's one failure class was an invented enum spelling,
//     so every legal value appears verbatim somewhere in the block.
//   * `closure` renders a host-code slot (`const "<closure>"`). REQUIRED closure
//     fields are kept (the author must emit the sentinel — Mount.onBubble, the
//     CellKindErased closure cells, Local's format/parse/onCommit); OPTIONAL closure
//     fields are suppressed outright — they are host-side handler slots the pack's
//     self-wiring rules forbid authoring (rules 6–9: omit onChange/onSelect/
//     onRowClick/onResult), so listing them would spend tokens teaching fields whose
//     only correct emission is absence.
//   * `any` renders a schema-unconstrained value; `str`/`num`/`int`/`bool` the JSON
//     primitives.
//   * Emission order is deterministic: Node, the kind unions, TreeOp, then payload
//     unions / records / enums alphabetically — only defs actually referenced by
//     name are emitted, so the block cannot carry dead vocabulary.
let buildSignatureCatalogue () =
    let schemaText = File.ReadAllText schemaSrcPath
    use doc = JsonDocument.Parse schemaText
    let defs = doc.RootElement.GetProperty "$defs"
    let getDef (name: string) = defs.GetProperty name

    let refCount (name: string) =
        Regex.Matches(schemaText, Regex.Escape($"\"#/$defs/{name}\"")).Count

    let hasProp (name: string) (el: JsonElement) =
        el.ValueKind = JsonValueKind.Object
        && (match el.TryGetProperty name with
            | true, _ -> true
            | _ -> false)

    let isStringEnum (el: JsonElement) =
        hasProp "enum" el
        && (match el.TryGetProperty "type" with
            | true, t -> t.GetString() = "string"
            | _ -> false)

    // A named alias for a bare string with no closed vocabulary (AriaRole) — rendered
    // as `str` at the use site, never declared.
    let isPlainStringAlias (el: JsonElement) =
        not (hasProp "enum" el)
        && not (hasProp "oneOf" el)
        && not (hasProp "properties" el)
        && (match el.TryGetProperty "type" with
            | true, t -> t.ValueKind = JsonValueKind.String && t.GetString() = "string"
            | _ -> false)

    let enumInline (el: JsonElement) =
        el.GetProperty("enum").EnumerateArray()
        |> Seq.map (fun v -> "\"" + v.GetString() + "\"")
        |> String.concat "|"

    // Defs that must be declared by name because some rendered type referenced them.
    let namedRefs = System.Collections.Generic.HashSet<string>()

    let rec renderType (el: JsonElement) : string =
        if el.ValueKind = JsonValueKind.True then
            "any"
        elif hasProp "$ref" el then
            let name = el.GetProperty("$ref").GetString().Substring "#/$defs/".Length
            let target = getDef name

            if isStringEnum target then
                if refCount name <= 1 then
                    enumInline target
                else
                    namedRefs.Add name |> ignore
                    name
            elif isPlainStringAlias target then
                "str"
            else
                namedRefs.Add name |> ignore
                name
        elif hasProp "const" el then
            let c = el.GetProperty "const"

            if c.ValueKind = JsonValueKind.String && c.GetString() = "<closure>" then
                "closure"
            else
                c.GetRawText()
        elif hasProp "not" el then
            "any"
        elif
            hasProp "allOf" el
            || (hasProp "properties" el && hasProp "$type" (el.GetProperty "properties"))
        then
            renderAlternative el
        elif hasProp "oneOf" el then
            el.GetProperty("oneOf").EnumerateArray()
            |> Seq.map renderAlternative
            |> String.concat " | "
        elif hasProp "enum" el then
            enumInline el
        else
            match el.TryGetProperty "type" with
            | true, t ->
                match t.GetString() with
                | "string" -> "str"
                | "number" -> "num"
                | "integer" -> "int"
                | "boolean" -> "bool"
                | "array" ->
                    let itemTy =
                        match el.TryGetProperty "items" with
                        | true, items when items.ValueKind <> JsonValueKind.True -> renderType items
                        | _ -> "any"

                    if itemTy.Contains "|" then
                        $"({itemTy})[]"
                    else
                        itemTy + "[]"
                | "object" ->
                    if hasProp "properties" el then
                        "{ " + renderFields el + " }"
                    else
                        match el.TryGetProperty "additionalProperties" with
                        | true, ap when ap.ValueKind <> JsonValueKind.True -> "{ [key]:" + renderType ap + " }"
                        | true, _ -> "{ [key]:any }"
                        | _ -> "object"
                | other -> other
            | _ -> "any"

    // A union alternative: an allOf hoist (`[$type-const; $ref Spec]` or the
    // inline-record-plus-constraint shape — Switch/SetState), an inline object case
    // with a `$type` const, or (TextSource's bare-string leg) any other type.
    and renderAlternative (case: JsonElement) : string =
        if hasProp "allOf" case then
            let parts = case.GetProperty("allOf").EnumerateArray() |> Seq.toArray

            let kind =
                parts.[0].GetProperty("properties").GetProperty("$type").GetProperty("const").GetString()

            let record =
                if hasProp "$ref" parts.[1] then
                    getDef (parts.[1].GetProperty("$ref").GetString().Substring "#/$defs/".Length)
                else
                    parts.[0]

            renderCase kind record
        elif hasProp "properties" case && hasProp "$type" (case.GetProperty "properties") then
            let kind =
                case.GetProperty("properties").GetProperty("$type").GetProperty("const").GetString()

            renderCase kind case
        else
            renderType case

    and renderCase (name: string) (record: JsonElement) : string =
        match renderFields record with
        | "" -> name
        | fields -> name + " { " + fields + " }"

    and renderFields (record: JsonElement) : string =
        let required =
            match record.TryGetProperty "required" with
            | true, r ->
                [ for e in r.EnumerateArray() do
                      let n = e.GetString()

                      if n <> "$type" then
                          n ]
            | _ -> []

        let props =
            match record.TryGetProperty "properties" with
            | true, props ->
                [ for p in props.EnumerateObject() do
                      if p.Name <> "$type" then
                          p.Name, p.Value ]
            | _ -> []

        let byName = dict props

        let optional =
            props
            |> List.map fst
            |> List.filter (fun n -> not (List.contains n required))
            |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

        // No space after the colon: measured in the o200k reference tokenizer, the
        // colon-dense form saves ~135 tokens over the whole catalogue while the
        // `; ` field separator saves almost nothing — so density is spent exactly
        // where the tokenizer pays for it (the Phase 839 minification argument).
        [ for n in required do
              if byName.ContainsKey n then
                  $"{n}:{renderType byName.[n]}"
              else
                  $"{n}:any"
          for n in optional do
              // Optional closure fields are suppressed: host-side handler slots the
              // self-wiring rules forbid authoring (see the module doc). Required
              // closure fields stay — the author must emit the sentinel there.
              match renderType byName.[n] with
              | "closure" -> ()
              | ty -> $"{n}?:{ty}" ]
        |> String.concat "; "

    // Nested `oneOf` wrappers flatten into the parent union (TextSource's inner DU).
    let rec altsOf (el: JsonElement) : JsonElement seq =
        if hasProp "oneOf" el && not (hasProp "properties" el) && not (hasProp "allOf" el) then
            el.GetProperty("oneOf").EnumerateArray() |> Seq.collect altsOf
        else
            Seq.singleton el

    let renderDecl (name: string) =
        let d = getDef name

        if isStringEnum d then
            name + " = " + enumInline d
        elif hasProp "oneOf" d then
            let cases = altsOf d |> Seq.map renderAlternative |> List.ofSeq
            name + " =\n" + (cases |> List.map (fun c -> "| " + c) |> String.concat "\n")
        else
            name + " { " + renderFields d + " }"

    // Roots first (fixed order), then everything the rendering named, to a fixpoint.
    let roots =
        [ "Node"
          "NodeKind"
          "LayoutKind"
          "DisplayKind"
          "InputKind"
          "VisKind"
          "TreeOp" ]

    let decls = System.Collections.Generic.Dictionary<string, string>()

    for r in roots do
        decls.[r] <- renderDecl r

    let mutable added = true

    while added do
        added <- false

        for n in namedRefs |> Seq.toArray do
            if not (decls.ContainsKey n) then
                decls.[n] <- renderDecl n
                added <- true

    let rest =
        decls.Keys
        |> Seq.filter (fun n -> not (List.contains n roots))
        |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> List.ofSeq

    let classOf (name: string) =
        let d = getDef name

        if isStringEnum d then 2
        elif hasProp "oneOf" d then 0
        else 1

    let ordered = roots @ (rest |> List.sortBy (fun n -> classOf n)) // stable: keeps alpha within class

    ordered |> List.map (fun n -> decls.[n]) |> String.concat "\n"

// ── Managed marker blocks ────────────────────────────────────────────────────────
// <!-- fuaran:example fixture=ID -->
// ```json
// …pretty canonical JSON…
// ```
// <!-- /fuaran:example -->
let private markerRegex =
    Regex(
        @"<!-- fuaran:example fixture=(?<id>[A-Za-z0-9._-]+) -->\r?\n```json\r?\n(?<body>.*?)\r?\n```\r?\n<!-- /fuaran:example -->",
        RegexOptions.Singleline
    )

// <!-- fuaran:signature-catalogue -->
// ```ts
// …declaration-style type surface…   (schema-derived; see buildSignatureCatalogue)
// ```
// <!-- /fuaran:signature-catalogue -->
let private catalogueRegex =
    Regex(
        @"<!-- fuaran:signature-catalogue -->\r?\n```ts\r?\n(?<body>.*?)\r?\n```\r?\n<!-- /fuaran:signature-catalogue -->",
        RegexOptions.Singleline
    )

// `packSurface` marks a file that ships as part of the paid prompt prefix, and so is
// subject to the minified-example emission. The human-facing authoring guide is not one.
let reconcileMarkdown (relPath: string) (expectCatalogue: bool) (packSurface: bool) =
    let minifyHere = packSurface && minifyExamples
    let path = Path.Combine(docsDir, relPath)
    let original = File.ReadAllText path
    let mutable matched = 0

    let rebuilt =
        markerRegex.Replace(
            original,
            fun (m: Match) ->
                matched <- matched + 1
                let id = m.Groups.["id"].Value
                let body = m.Groups.["body"].Value
                let canonical = fixtureRaw id

                let expectedBody =
                    if minifyHere then
                        minifyJson canonical
                    else
                        prettyJson canonical

                if canonicalize body <> canonicalize canonical then
                    if not writeMode then
                        reportDrift
                            $"{relPath}: example '{id}' diverges from wire-format-fixtures/{(fixtureMeta id).File}"
                elif minifyHere && not writeMode && normalizeEol body <> expectedBody then
                    // Structurally identical but not in the adopted emission. Only
                    // asserted when the caller asked for minified — a bare --check
                    // stays whitespace-indifferent, so it passes against either form.
                    reportDrift
                        $"{relPath}: example '{id}' is structurally correct but not minified (run authoring-pack.fsx --write)"

                // In --write we always re-emit the canonical form for the requested
                // emission (which normalises whitespace too); in --check we leave the
                // text as-is — the compares above already decided pass/fail.
                let newBody = if writeMode then expectedBody else body
                $"<!-- fuaran:example fixture={id} -->\n```json\n{newBody}\n```\n<!-- /fuaran:example -->"
        )

    let mutable cataloguesMatched = 0

    let rebuilt =
        catalogueRegex.Replace(
            rebuilt,
            fun (m: Match) ->
                cataloguesMatched <- cataloguesMatched + 1
                let body = m.Groups.["body"].Value
                let expected = buildSignatureCatalogue ()

                if normalizeEol body <> expected then
                    if not writeMode then
                        reportDrift $"{relPath}: signature catalogue diverges from wire-format-fixtures/schema.json"

                let newBody = if writeMode then expected else body
                $"<!-- fuaran:signature-catalogue -->\n```ts\n{newBody}\n```\n<!-- /fuaran:signature-catalogue -->"
        )

    if matched = 0 then
        reportDrift $"{relPath}: no <!-- fuaran:example --> blocks found (marker contract broken?)"

    if expectCatalogue && cataloguesMatched = 0 then
        reportDrift $"{relPath}: no <!-- fuaran:signature-catalogue --> block found (marker contract broken?)"

    reconcileFile relPath path rebuilt

// ── Few-shot corpus (curated id → natural-language prompt) ───────────────────────
// Each pairs a canonical fixture tree with the kind of request that should produce it.
// The tree is corpus-sourced; only the prompt is authored. Drives the pack's few-shot
// and (downstream) the evaluation seed corpus.
//
// Phase 834 dedup rule: a fixture that already renders as a SYSTEM-PROMPT example
// block is not repeated here. The flip record shows the system prompt is the
// operative teaching surface (flip-4 2026-08-02: a few-shot entry "the default
// posture never reads"; the Badge flip's mechanism was promotion INTO the system
// prompt) — a duplicate entry adds no example any posture would otherwise lack and
// spends the prefix budget twice. New teaching examples go into the system prompt;
// few-shot carries fixtures the system prompt does not (plus the caution-listed
// metric-1 / badge-1 / btn-1 / composite-root / op-replacebinding pairings).
let fewShot =
    [ "composite-root",
      "Show a revenue dashboard: a card holding the revenue metric and a labelled total row, then a stack with the same metric and a short 'updated hourly' note."
      "heading-1", "Add a level-2 section heading reading 'Channel performance'."
      "metric-1",
      "Show revenue as a headline metric, formatted as GBP currency, with a +7% upward trend versus last month."
      // markdown-1: cut 2026-08-15 (Phase 841 minimisation). The 834 census read it as
      // "unique", meaning unique among few-shot FIXTURES; the coverage matrix asks the
      // stronger question and finds every rule it carries already carried elsewhere —
      // `switch-on-selection`'s default branch is a Markdown node, in the system prompt,
      // where every posture reads it. Both the redundancy prune and greedy set-cover
      // drop it independently.
      "callout-1", "Warn the user with a dismissable callout that live data is delayed."
      "btn-1", "A primary 'Refresh' button with a refresh icon, disabled while the 'loading' state flag is true."
      "form-1",
      "A save form: a required name field, an age number, a required 'I agree' checkbox, a tier dropdown, and a notes text area."
      "filters-declarative",
      "A filter strip that scopes a dataset: a search box, a tier dropdown, and an age range — self-wiring, no host code (each chip omits onChange and its value reads its own filter)."
      "filters-date-range",
      "A filter strip with a single date-range chip: pick a start and end date in one control, scoping everything downstream through one filter param."
      // lenient-grid-transform-param-compact: cut 2026-08-15 (Phase 841 minimisation).
      // Same correction as markdown-1 — "unique" in the 834 census meant not-a-
      // system-prompt-block, not carrying a rule nothing else carries. The wired-filter
      // composition it demonstrates (Filter param → pipeline `param` reference → grid)
      // is exactly what `lenient-filterable-static-dashboard-compact` demonstrates, and
      // that one IS a system-prompt block.
      // grid-toned-pill: cut 2026-08-15 (Phase 834 dedup — system-prompt block).
      "query-dependson",
      "A revenue metric fed by a host 'orders' query that declares it depends on the status and region filters — the host re-runs the query when either filter changes."
      "discl-1", "A collapsible 'Additional entitlements' section, open by default, containing a short note."
      // tabs-explicit-1: cut 2026-08-15 (Phase 841 reinvestment). Superseded by the
      // `composite-tabs-panels` system-prompt block, which is a strict SUPERSET of its
      // rules (same headers/tags/activeTag surface, same Sparkline leaf) and additionally
      // shows the containers-inside-a-wrapper composition this fixture's bare leaves
      // could not. The teaching moves from few-shot onto the surface every posture reads.
      "custom-1", "Emit the host-registered custom 'trend-card' component from the 'analytics' module."
      "op-replacebinding",
      "Edit the existing tree: pin node 'metric-1' to a static figure of 99.5 by replacing its Source binding."
      // op-insertchild + op-reorderchildren: cut 2026-08-15 (Phase 841 minimisation,
      // second pass). §Editing an existing tree carries a hand-authored `Batch` block
      // that inserts a child AND states the resulting order, on the surface every
      // posture reads — so both entries were teaching a second time what the system
      // prompt already teaches once. The first pass missed this because the coverage
      // model only read corpus-derived marker blocks; teaching it to read the pack's
      // hand-authored JSON too is what made the redundancy visible.
      // `op-replacebinding` stays: it is the section's marker example and caution-listed.
      // Cut 2026-08-15 (Phase 834 dedup — each renders as a system-prompt example
      // block, which the flip record shows is the operative surface):
      //   lenient-filterable-static-dashboard-compact, master-detail-multi-field
      //   (flip-4 named this exact entry as never read by the default posture),
      //   empty-state-card, form-toggle, now-environment-binding.
      // RESTORED 2026-08-15 (the charter's regression rule): the post-slim
      // sweep regressed stress-001/003 on claude (judge c2 PARTIAL where the
      // pre-slim sweep passed) — the three entries teaching exactly those
      // intents come back:
      "lenient-master-detail-preselected-compact",
      "A support-ticket triage screen: a ticket grid with TCK-2041 selected by default, and a detail card showing the selected ticket."
      "switch-on-selection",
      "A ward dashboard: a grid of wards, and a status panel that changes with the selected ward — a critical ward shows an escalation callout, otherwise a normal-range note."
      "lenient-scalar-transform-composition-compact",
      "A triage dashboard over embedded ticket data: a badge counting the critical tickets, and a warning callout whose body is the selected ticket's alert text (TCK-2041 selected by default)."
      // progress-1: cut on the flip-3 verdict — "the few-shot addition was
      // redundant; no change at n=6" (Progress stays taught in prose).
      // 2026-08-01 n=3 review — 042/c3 (×3): every emission reached for a Fact
      // label-value tile in the STATUS-CHIP role. `Badge` existed and was taught;
      // it was never a few-shot exemplar either. (Kept through the 834 dedup —
      // caution-listed: the Badge example.)
      "badge-1",
      "Mark the record's state with a small inline status chip reading 'Active' — a compact badge, not a labelled stat tile." ]

let buildFewShotJsonl () =
    fewShot
    |> List.map (fun (id, prompt) ->
        let meta = fixtureMeta id
        let promptJson = JsonSerializer.Serialize(prompt, prettyOpts)
        let idJson = JsonSerializer.Serialize(id)
        // The tree is the canonical fixture text, embedded verbatim as JSON — minified
        // unconditionally, not under --minify-examples. A JSONL record is one line by
        // definition, so a pretty-stored fixture would corrupt the file rather than
        // merely cost tokens; the emission has no pretty variant to choose between.
        // On a corpus that stores its fixtures compact this is byte-for-byte a no-op.
        let tree = minifyJson (fixtureRaw id)
        $"{{\"prompt\":{promptJson},\"decoder\":\"{meta.Decoder}\",\"fixture\":{idJson},\"tree\":{tree}}}")
    |> String.concat "\n"
    |> fun body -> body + "\n"

// ── Rule → fixture coverage matrix (Phase 841) ───────────────────────────────────
// Exemplar selection used to be accretion: one example per lesson, added when a
// lesson was learned and never re-examined against the rest. This section makes it an
// OPTIMISATION — score every corpus fixture by which taught rules it exercises, then
// choose the smallest set whose joint coverage matches what the pack already teaches.
//
// The taught-rule inventory has three families, and only the third is authored:
//
//   * `case:<Union>.<Case>`  — every $type-discriminated alternative of every schema
//     union (NodeKind and its four sub-unions, Binding, Action, TreeOp, CellKindErased,
//     Format, …). Enumerable from `schema.json`, so the inventory cannot go stale.
//   * `field:<Scope>.<name>` — every OPTIONAL property of every record/case. Required
//     properties are implied by the case rule (a fixture cannot carry the case without
//     them), so listing them would inflate coverage with rules nothing can miss.
//   * `enum:<Enum>=<value>`  — every value of every closed string vocabulary, keyed by
//     the enum's def name (matching the signature catalogue's "declared once by name").
//
//   * `idiom:<name>` — the semantic residue: teaching that is a COMPOSITION rather than
//     a symbol, so no schema walk can see it (a filter chip wired through a Transform
//     param; a Transform in a scalar slot; a container nested inside a wrapper). Each is
//     a structural predicate below, not a hand-maintained fixture list — a fixture that
//     stops exercising an idiom loses the rule at the next regen rather than rotting.
//   * `pipestep:` / `pipefn:` / `pipeop:` / `pipeagg:` — the Transform pipeline algebra.
//     `schema.json` models `pipeline` as `any[]` (which is why the 838 catalogue could
//     not carry it either), so this vocabulary is enumerated from the corpus itself —
//     the corpus is the oracle for the one surface the schema does not constrain.
//
// The matrix is GENERATED into `docs/tools/coverage-matrix.json` and reconciled by
// `--check` like every other derived surface, so a fixture added to the corpus without
// a regen fails the build rather than silently ageing the mining evidence.

let private schemaDoc = JsonDocument.Parse(File.ReadAllText schemaSrcPath)
let private schemaDefs = schemaDoc.RootElement.GetProperty "$defs"

let private sHas (n: string) (el: JsonElement) =
    el.ValueKind = JsonValueKind.Object
    && (match el.TryGetProperty n with
        | true, _ -> true
        | _ -> false)

let private sProp (n: string) (el: JsonElement) = el.GetProperty n

let private sRefName (el: JsonElement) =
    (sProp "$ref" el).GetString().Substring "#/$defs/".Length

let private sIsStringEnum (el: JsonElement) =
    sHas "enum" el
    && (match el.TryGetProperty "type" with
        | true, t -> t.ValueKind = JsonValueKind.String && t.GetString() = "string"
        | _ -> false)

/// Flatten nested `oneOf` wrappers (TextSource's inner DU) into one alternative list.
let rec private sAlts (el: JsonElement) : JsonElement seq =
    if sHas "oneOf" el && not (sHas "properties" el) && not (sHas "allOf" el) then
        (sProp "oneOf" el).EnumerateArray() |> Seq.collect sAlts
    else
        Seq.singleton el

/// The `$type` const an alternative discriminates on, if it is a tagged case at all.
let private sCaseTag (alt: JsonElement) : string option =
    let carrier =
        if sHas "allOf" alt then
            (sProp "allOf" alt).EnumerateArray() |> Seq.tryHead
        elif sHas "properties" alt then
            Some alt
        else
            None

    match carrier with
    | Some c when sHas "properties" c ->
        match (sProp "properties" c).TryGetProperty "$type" with
        | true, t when sHas "const" t -> Some((sProp "const" t).GetString())
        | _ -> None
    | _ -> None

/// Does this union (transitively through `$ref` alternatives) declare `tg`?
///
/// `NodeKind`'s alternatives are `$ref`s to the four sub-unions, so a `Box` node's tag
/// matches NONE of them directly — the observe walk has to know to descend. Getting
/// this wrong is silent: the walk records the `Node` fields, finds no case, and returns
/// a plausible-looking rule set an order of magnitude too small.
let rec private unionHasTag (el: JsonElement) (tg: string) : bool =
    sAlts el
    |> Seq.exists (fun a ->
        match sCaseTag a with
        | Some t -> t = tg
        | None -> sHas "$ref" a && unionHasTag (schemaDefs.GetProperty(sRefName a)) tg)

/// Schema-directed walk. With `json = None` it ENUMERATES every rule the schema can
/// express from `rootDef` down (the taught inventory); with `json = Some tree` it
/// records only the rules that tree actually exercises. One traversal, two modes, so
/// an inventory rule and an observed rule are spelled by the same code — a coverage
/// figure cannot drift from its denominator.
let private schemaRules (rootDef: string) (json: JsonElement option) : Set<string> =
    let acc = System.Collections.Generic.HashSet<string>()
    let emit (r: string) = acc.Add r |> ignore
    // Enumeration mode only: `Node` is recursive, so a def is expanded once. Rules are
    // keyed by def name rather than by path, so one expansion is complete.
    let visited = System.Collections.Generic.HashSet<string>()

    // A string an enum slot accepts but does not DECLARE is a §16 lenient alias
    // (`"Danger"` for `Critical`, `"row"` for `Horizontal`). It is a distinct rule, not
    // the canonical one: the exemplar's bytes are what the model reads, so a fixture
    // spelling the alias teaches the alias. Recording it as the canonical value would
    // credit teaching that is not on the page.
    let emitEnum (name: string) (enumSch: JsonElement) (v: string) =
        let declared =
            (sProp "enum" enumSch).EnumerateArray()
            |> Seq.map (fun e -> e.GetString())
            |> Set.ofSeq

        if declared.Contains v then
            emit $"enum:{name}={v}"
        else
            emit $"alias:{name}={v}"

    let rec go (scope: string) (sch: JsonElement) (j: JsonElement option) =
        if sch.ValueKind = JsonValueKind.True then
            ()
        elif sHas "$ref" sch then
            let n = sRefName sch
            let target = schemaDefs.GetProperty n

            if sIsStringEnum target then
                match j with
                | Some v when v.ValueKind = JsonValueKind.String -> emitEnum n target (v.GetString())
                | Some _ -> ()
                | None ->
                    for e in (sProp "enum" target).EnumerateArray() do
                        emit $"enum:{n}={e.GetString()}"
            elif j.IsSome || visited.Add n then
                go n target j
        elif sHas "allOf" sch then
            for p in (sProp "allOf" sch).EnumerateArray() do
                go scope p j
        elif sHas "oneOf" sch then
            let alts = sAlts sch |> Seq.toArray

            match j with
            | Some je when je.ValueKind = JsonValueKind.Object ->
                let tag =
                    match je.TryGetProperty "$type" with
                    | true, t when t.ValueKind = JsonValueKind.String -> Some(t.GetString())
                    | _ -> None

                match tag with
                | Some tg ->
                    match alts |> Array.tryFind (fun a -> sCaseTag a = Some tg) with
                    | Some a ->
                        emit $"case:{scope}.{tg}"
                        goCase scope tg a j
                    | None ->
                        // No direct case — descend into the `$ref` alternative that owns
                        // the tag (NodeKind → LayoutKind / DisplayKind / InputKind /
                        // VisKind). `go`'s `$ref` branch resets the scope to the
                        // declaring union, so the rule reads `case:LayoutKind.Box`.
                        alts
                        |> Array.tryFind (fun a -> sHas "$ref" a && unionHasTag (schemaDefs.GetProperty(sRefName a)) tg)
                        |> Option.iter (fun a -> go scope a j)
                | None ->
                    alts
                    |> Array.tryFind (fun a -> sCaseTag a = None && (sHas "properties" a || sHas "$ref" a))
                    |> Option.iter (fun a -> go scope a j)
            | Some _ -> () // a primitive leg (TextSource's bare string) carries no rule
            | None ->
                for a in alts do
                    match sCaseTag a with
                    | Some tg ->
                        emit $"case:{scope}.{tg}"
                        goCase scope tg a None
                    | None -> go scope a None
        elif sHas "properties" sch then
            goObject scope sch j
        elif sHas "enum" sch then
            match j with
            | Some v when v.ValueKind = JsonValueKind.String -> emitEnum scope sch (v.GetString())
            | Some _ -> ()
            | None ->
                for e in (sProp "enum" sch).EnumerateArray() do
                    if e.ValueKind = JsonValueKind.String then
                        emit $"enum:{scope}={e.GetString()}"
        else
            match sch.TryGetProperty "type" with
            | true, t when t.ValueKind = JsonValueKind.String ->
                match t.GetString() with
                | "array" ->
                    match sch.TryGetProperty "items" with
                    | true, items when items.ValueKind <> JsonValueKind.True ->
                        match j with
                        | Some je when je.ValueKind = JsonValueKind.Array ->
                            for v in je.EnumerateArray() do
                                go scope items (Some v)
                        | Some _ -> ()
                        | None -> go scope items None
                    | _ -> ()
                | "object" ->
                    match sch.TryGetProperty "additionalProperties" with
                    | true, ap when ap.ValueKind <> JsonValueKind.True ->
                        match j with
                        | Some je when je.ValueKind = JsonValueKind.Object ->
                            for p in je.EnumerateObject() do
                                go scope ap (Some p.Value)
                        | Some _ -> ()
                        | None -> go scope ap None
                    | _ -> ()
                | _ -> ()
            | _ -> ()

    // A tagged case either hoists a named Spec record (`allOf [$type-const; $ref Spec]`
    // — the scope becomes the Spec, matching the signature catalogue) or declares its
    // fields inline (`Binding.Query`, `NodeKind.Custom` — scope `Union.Case`).
    and goCase (union: string) (tag: string) (alt: JsonElement) (j: JsonElement option) =
        if sHas "allOf" alt then
            for p in (sProp "allOf" alt).EnumerateArray() do
                if sHas "$ref" p then
                    let n = sRefName p

                    if j.IsSome || visited.Add n then
                        go n (schemaDefs.GetProperty n) j
                else
                    goObject $"{union}.{tag}" p j
        else
            goObject $"{union}.{tag}" alt j

    and goObject (scope: string) (sch: JsonElement) (j: JsonElement option) =
        let required =
            match sch.TryGetProperty "required" with
            | true, r -> r.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> Set.ofSeq
            | _ -> Set.empty

        // An OPTIONAL closure slot is not a taught rule. The 838 signature catalogue
        // suppresses exactly these ("host-side handler slots the self-wiring rules
        // forbid authoring — listing them would spend tokens teaching fields whose only
        // correct emission is absence"), and the coverage model has to agree with the
        // catalogue or it scores exemplars on teaching the pack does not do. Required
        // closure slots are unaffected: they carry no field rule either way, and the
        // sentinel itself is covered by `idiom:closure-sentinel`.
        let isOptionalClosure (name: string) (p: JsonElement) =
            not (required.Contains name)
            && sHas "const" p
            && (let c = sProp "const" p in c.ValueKind = JsonValueKind.String && c.GetString() = "<closure>")

        match sch.TryGetProperty "properties" with
        | true, props ->
            for p in props.EnumerateObject() do
                if p.Name <> "$type" && not (isOptionalClosure p.Name p.Value) then
                    match j with
                    | Some je ->
                        match je.TryGetProperty p.Name with
                        | true, v when v.ValueKind <> JsonValueKind.Null ->
                            if not (required.Contains p.Name) then
                                emit $"field:{scope}.{p.Name}"

                            go $"{scope}.{p.Name}" p.Value (Some v)
                        | _ -> ()
                    | None ->
                        if not (required.Contains p.Name) then
                            emit $"field:{scope}.{p.Name}"

                        go $"{scope}.{p.Name}" p.Value None
        | _ -> ()

    go rootDef (schemaDefs.GetProperty rootDef) json
    Set.ofSeq acc

// ── Idioms + the Transform algebra (the non-schema-derivable residue) ────────────
let private layoutCases =
    sAlts (schemaDefs.GetProperty "LayoutKind") |> Seq.choose sCaseTag |> Set.ofSeq

/// Every idiom rule, with the pack section that teaches it. Present as a fixed list so
/// the inventory enumerates even when no fixture exercises the idiom — an idiom nothing
/// covers is exactly what the reinvestment queue is looking for.
let private idiomCatalogue =
    [ "idiom:multi-section-composition", "Containers nest under `children`"
      "idiom:nested-container", "Containers nest under `children`"
      "idiom:container-in-wrapper", "Containers nest under `children`"
      "idiom:actionable-message", "Empty states — a Card `Box`, not a `Callout`"
      "idiom:filter-param-wiring", "Filters must be WIRED"
      "idiom:transform-embedded-source", "Transform — the embedded-data canonical shape"
      "idiom:transform-scalar-slot", "Deriving ONE value — Transform in a scalar slot"
      "idiom:selection-multi-field", "Selected, pre-selected, and derived state"
      "idiom:preselected-default", "Selected, pre-selected, and derived state"
      "idiom:prefilled-default", "The node kinds — control `value` slots"
      "idiom:closure-sentinel", "Closure cells cannot be authored from the wire" ]

let private isNodeObj (el: JsonElement) =
    el.ValueKind = JsonValueKind.Object && sHas "id" el && sHas "kind" el

/// The shallowest Node-shaped descendants of a node — its direct children, whatever
/// slot they ride in (`children`, `child`, `fallback`, a Switch case).
let private directChildren (el: JsonElement) : JsonElement list =
    let acc = ResizeArray<JsonElement>()

    let rec search (x: JsonElement) =
        match x.ValueKind with
        | JsonValueKind.Object ->
            if isNodeObj x then
                acc.Add x
            else
                for p in x.EnumerateObject() do
                    search p.Value
        | JsonValueKind.Array ->
            for v in x.EnumerateArray() do
                search v
        | _ -> ()

    for p in el.EnumerateObject() do
        search p.Value

    List.ofSeq acc

let private kindTag (nodeEl: JsonElement) =
    match (sProp "kind" nodeEl).TryGetProperty "$type" with
    | true, t when t.ValueKind = JsonValueKind.String -> t.GetString()
    | _ -> ""

let private idiomRules (root: JsonElement) : Set<string> =
    let acc = System.Collections.Generic.HashSet<string>()
    let emit (r: string) = acc.Add r |> ignore

    // Keyed walk — the carrier property name is what separates a Transform feeding a
    // grid (`source`) from one feeding a scalar slot (`binding` / `value`), which is a
    // distinction the schema itself cannot make (both are `Binding`).
    let rec keyed (key: string) (el: JsonElement) (f: string -> JsonElement -> unit) =
        f key el

        match el.ValueKind with
        | JsonValueKind.Object ->
            for p in el.EnumerateObject() do
                keyed p.Name p.Value f
        | JsonValueKind.Array ->
            for v in el.EnumerateArray() do
                keyed key v f
        | _ -> ()

    let tagOf (el: JsonElement) =
        if el.ValueKind <> JsonValueKind.Object then
            ""
        else
            match el.TryGetProperty "$type" with
            | true, t when t.ValueKind = JsonValueKind.String -> t.GetString()
            | _ -> ""

    // ── Node-structure idioms ──
    let rec nodes (el: JsonElement) =
        seq {
            if isNodeObj el then
                yield el

            match el.ValueKind with
            | JsonValueKind.Object ->
                for p in el.EnumerateObject() do
                    yield! nodes p.Value
            | JsonValueKind.Array ->
                for v in el.EnumerateArray() do
                    yield! nodes v
            | _ -> ()
        }

    let allNodes = nodes root |> Seq.toList

    if isNodeObj root && List.length (directChildren root) >= 3 then
        emit "idiom:multi-section-composition"

    for n in allNodes do
        let k = kindTag n

        if layoutCases.Contains k then
            let childKinds = directChildren n |> List.map kindTag

            if k = "Box" && childKinds |> List.exists (fun c -> c = "Box") then
                emit "idiom:nested-container"

            if k <> "Box" && childKinds |> List.exists layoutCases.Contains then
                emit "idiom:container-in-wrapper"

            let hasAction = childKinds |> List.exists (fun c -> c = "Button" || c = "Form")

            let hasMessage =
                childKinds
                |> List.exists (fun c -> c = "Callout" || c = "Markdown" || c = "Heading" || c = "Icon")

            if hasAction && hasMessage then
                emit "idiom:actionable-message"

    // ── Binding / Transform idioms + the pipeline algebra ──
    let selections = ResizeArray<string * string>()

    keyed "$" root (fun key el ->
        match tagOf el with
        | "Selection" ->
            let nodeId =
                match el.TryGetProperty "nodeId" with
                | true, v -> v.GetString()
                | _ -> ""

            let field =
                match el.TryGetProperty "field" with
                | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                | _ -> ""

            selections.Add(nodeId, field)

            if sHas "defaultValue" el then
                emit "idiom:preselected-default"
        | "Transform" ->
            if key = "binding" || key = "value" then
                emit "idiom:transform-scalar-slot"

            match el.TryGetProperty "source" with
            | true, s when sHas "columns" s -> emit "idiom:transform-embedded-source"
            | _ -> ()

            let filterParams =
                match el.TryGetProperty "params" with
                | true, ps when ps.ValueKind = JsonValueKind.Array ->
                    ps.EnumerateArray()
                    |> Seq.exists (fun p ->
                        match p.TryGetProperty "from" with
                        | true, f -> tagOf f = "Filter"
                        | _ -> false)
                | _ -> false

            let mutable usesParam = false

            match el.TryGetProperty "pipeline" with
            | true, steps when steps.ValueKind = JsonValueKind.Array ->
                for step in steps.EnumerateArray() do
                    let stepTag = tagOf step

                    if stepTag <> "" then
                        emit $"pipestep:{stepTag}"

                    keyed "$" step (fun k2 inner ->
                        let t = tagOf inner

                        if t <> "" then
                            if t = "param" then
                                usesParam <- true

                            if t <> stepTag then
                                emit $"pipefn:{t}"

                        if inner.ValueKind = JsonValueKind.String then
                            if k2 = "op" then
                                emit $"pipeop:{inner.GetString()}"
                            elif k2 = "fn" then
                                emit $"pipeagg:{inner.GetString()}")
            | _ -> ()

            if filterParams && usesParam then
                emit "idiom:filter-param-wiring"
        | _ ->
            ignore key

            if el.ValueKind = JsonValueKind.String && el.GetString() = "<closure>" then
                emit "idiom:closure-sentinel")

    // A control slot arriving PRE-FILLED: the `value` of a form field, filter chip or
    // select carrying the prompt-named default rather than an empty slot. Matched at
    // the control carrier rather than by property name — `value` is also MetricSpec's
    // data slot, and a name-only sweep would attest the wrong family on every metric.
    let controlSpecs (n: JsonElement) =
        let k = sProp "kind" n

        let sub (arrName: string) =
            match k.TryGetProperty arrName with
            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                arr.EnumerateArray()
                |> Seq.choose (fun item ->
                    match item.TryGetProperty "kind" with
                    | true, ck -> Some ck
                    | _ -> None)
                |> List.ofSeq
            | _ -> []

        match kindTag n with
        | "Form" -> sub "fields"
        | "Filters" -> sub "items"
        | "Select" -> [ k ]
        | _ -> []

    for n in allNodes do
        for spec in controlSpecs n do
            match spec.TryGetProperty "value" with
            | true, v when v.ValueKind <> JsonValueKind.Null ->
                if v.ValueKind <> JsonValueKind.Object || tagOf v = "Static" then
                    emit "idiom:prefilled-default"
            | _ -> ()

    if
        selections
        |> Seq.filter (fun (n, f) -> n <> "" && f <> "")
        |> Seq.groupBy fst
        |> Seq.exists (fun (_, g) -> g |> Seq.map snd |> Seq.distinct |> Seq.length >= 2)
    then
        emit "idiom:selection-multi-field"

    Set.ofSeq acc

// ── The exemplar sets ───────────────────────────────────────────────────────────
let private systemPromptExemplars =
    let text = File.ReadAllText(Path.Combine(packDir, "system-prompt.md"))

    markerRegex.Matches text
    |> Seq.map (fun m -> m.Groups.["id"].Value)
    |> Seq.distinct
    |> List.ofSeq

let private fewShotExemplars = fewShot |> List.map fst

/// PINNED — the miner may never displace these, only make them work harder.
///
/// Every system-prompt example block is pinned, and that is not a shortcut: the Phase
/// 834 per-section census returned KEEP for every section carrying one, each citing its
/// own flip record (Badge 0/6 → 6/6; the toned pill 35/35; filters-wired against the
/// ×34+×12+×9+×7 cluster; the multi-field projection 1 → 5; Now/Icon/Duration verified
/// in the 817 sweep). The flip record also shows WHY the pin sits on this surface and
/// not the other: the system prompt is what the default posture reads, so displacing a
/// block would remove teaching from every request, while a few-shot entry is optional
/// context. The miner's freedom is therefore over few-shot, which is where accretion
/// actually happened.
let private pinnedExemplars = systemPromptExemplars

let private currentExemplars =
    (systemPromptExemplars @ fewShotExemplars)
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

// ── The matrix ──────────────────────────────────────────────────────────────────
/// Every corpus fixture a pack exemplar could be drawn from.
///
/// Two exclusions, both by construction rather than by taste. REJECT fixtures: a wire
/// form the decoder refuses cannot exemplify anything, and several are not parseable
/// JSON at all. LENIENT-ACCEPT fixtures the pack does not already carry: admitting one
/// would swap a canonical example for a §16 shorthand, which changes the taught DIALECT
/// — a separate decision with its own evidence, not a side-effect of minimising a
/// coverage set. The lenient exemplars already in the pack stay eligible because their
/// dialect is the shipped one; the miner may keep them, it may not introduce more.
let private candidateFixtures =
    use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter (fun f ->
        let id = f.GetProperty("id").GetString()

        match f.GetProperty("kind").GetString() with
        | "node-round-trip"
        | "op-round-trip"
        | "envelope-round-trip" -> true
        | "lenient-accept" -> List.contains id currentExemplars
        | _ -> false)
    |> Seq.map (fun f -> f.GetProperty("id").GetString())
    |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b))
    |> List.ofSeq

let private rulesFor (id: string) : Set<string> =
    let meta = fixtureMeta id
    use doc = JsonDocument.Parse(fixtureRaw id)
    let root = doc.RootElement
    let rootDef = if meta.Decoder = "op" then "TreeOp" else "Node"
    Set.union (schemaRules rootDef (Some root)) (idiomRules root)

let private fixtureRules =
    candidateFixtures |> List.map (fun id -> id, rulesFor id) |> Map.ofList

let private schemaInventory =
    Set.union (schemaRules "Node" None) (schemaRules "TreeOp" None)

/// The taught inventory. The schema half enumerates itself; the idiom half is the fixed
/// catalogue; the Transform-algebra half is the corpus's own vocabulary (the schema
/// models `pipeline` as `any[]`, so there is nothing else to enumerate it from).
let private taughtRules =
    let observed = fixtureRules |> Map.toSeq |> Seq.collect snd |> Set.ofSeq

    let corpusDerived =
        observed
        |> Set.filter (fun r ->
            r.StartsWith "pipestep:"
            || r.StartsWith "pipefn:"
            || r.StartsWith "pipeop:"
            || r.StartsWith "pipeagg:"
            || r.StartsWith "alias:")

    schemaInventory
    |> Set.union (idiomCatalogue |> List.map fst |> Set.ofList)
    |> Set.union corpusDerived

/// A rule a fixture exercises that the inventory does not know about means the walker
/// and the enumerator disagree — a probe failure, not a coverage finding. Fail loudly
/// rather than quietly reporting coverage against the wrong denominator.
let private inventoryEscapes =
    fixtureRules
    |> Map.toSeq
    |> Seq.collect snd
    |> Set.ofSeq
    |> fun observed -> Set.difference observed taughtRules

/// The pack's HAND-AUTHORED JSON blocks — the fenced examples that are not corpus
/// marker blocks. There is at least one and it is load-bearing: §Editing an existing
/// tree illustrates `Batch` + `InsertChild` + `ReorderChildren` against ids from the
/// `composite-root` tree, which is precisely why it cannot be a corpus fixture (no
/// fixture knows another fixture's ids). It is teaching on the surface every posture
/// reads, so a coverage model blind to it reports gaps the pack does not have — and
/// would send a reinvestment pass to fill one that is already filled.
let private inlineRules =
    let text =
        normalizeEol (File.ReadAllText(Path.Combine(packDir, "system-prompt.md")))

    let stripped = markerRegex.Replace(text, "")

    Regex.Matches(stripped, @"```json\n(?<body>.*?)\n```", RegexOptions.Singleline)
    |> Seq.map (fun m -> m.Groups.["body"].Value)
    |> Seq.fold
        (fun acc body ->
            try
                use doc = JsonDocument.Parse body
                let root = doc.RootElement

                let rootDef =
                    if sHas "id" root && sHas "kind" root then
                        "Node"
                    else
                        "TreeOp"

                Set.union acc (Set.union (schemaRules rootDef (Some root)) (idiomRules root))
            with _ ->
                // An illustrative fragment that is not a whole document (a spec excerpt)
                // contributes nothing rather than failing the run.
                acc)
        Set.empty

/// Coverage always starts from the hand-authored blocks: they are on the page whatever
/// the exemplar set does, so they are the floor every candidate is scored against.
let private coverageOf (ids: string list) =
    ids
    |> List.fold (fun acc id -> Set.union acc (Map.find id fixtureRules)) inlineRules

/// Minified bytes — the currency an exemplar actually spends in the paid prefix.
let private costOf (id: string) = (minifyJson (fixtureRaw id)).Length

/// Greedy set-cover with the pinned set forced into the solution, weighted by COST:
/// each round takes the candidate with the best new-rules-per-byte ratio, not simply
/// the most new rules. Unweighted greedy answers "how few exemplars", which is not the
/// question — one maximally dense tree demonstrating forty rules beats ten demonstrating
/// four each only if it is also cheaper than the ten, and the ratio form is what tests
/// that. The target universe is what the CURRENT exemplar set covers: the miner may not
/// declare success by lowering the bar, and any rule it cannot reach is named.
let private mine () =
    let universe = coverageOf currentExemplars
    let mutable covered = coverageOf pinnedExemplars
    let mutable chosen: (string * int) list = []

    let pool =
        candidateFixtures
        |> List.filter (fun id -> not (List.contains id pinnedExemplars))

    let mutable go = true

    while go do
        let remaining = Set.difference universe covered

        if Set.isEmpty remaining then
            go <- false
        else
            let scored =
                pool
                |> List.filter (fun id -> not (chosen |> List.exists (fun (c, _) -> c = id)))
                |> List.map (fun id ->
                    let gain = Set.intersect (Map.find id fixtureRules) remaining
                    id, Set.count gain, costOf id)
                |> List.filter (fun (_, g, _) -> g > 0)

            match scored with
            | [] -> go <- false
            | _ ->
                // Best rules-per-byte wins; ties go to the larger absolute gain, then
                // the cheaper fixture, then the Ordinal-first id — so the selection is
                // reproducible byte-for-byte on any machine.
                let best =
                    scored
                    |> List.sortWith (fun (idA, gA, cA) (idB, gB, cB) ->
                        let rA = float gA / float (max cA 1)
                        let rB = float gB / float (max cB 1)

                        if rA <> rB then compare rB rA
                        elif gA <> gB then compare gB gA
                        elif cA <> cB then compare cA cB
                        else String.CompareOrdinal(idA, idB))
                    |> List.head

                let id, gain, _ = best
                chosen <- chosen @ [ (id, gain) ]
                covered <- Set.union covered (Map.find id fixtureRules)

    universe, covered, chosen

/// The other half of the minimisation, and on an already-deduped set the half that
/// pays: walk the CURRENT exemplars most-expensive-first and drop each one whose
/// removal leaves the covered rule set unchanged. Greedy-from-scratch answers "what
/// would we choose knowing nothing"; the prune answers "what is provably carried
/// twice", and only the second is a cut with no re-authoring attached — a pruned entry
/// needs no new natural-language prompt and no new fixture.
let private prune () =
    let universe = coverageOf currentExemplars

    let ordered =
        currentExemplars
        |> List.filter (fun id -> not (List.contains id pinnedExemplars))
        |> List.sortWith (fun a b ->
            let ca, cb = costOf a, costOf b

            if ca <> cb then
                compare cb ca
            else
                String.CompareOrdinal(a, b))

    let mutable kept = currentExemplars
    let mutable dropped = []

    for id in ordered do
        let candidate = kept |> List.filter (fun x -> x <> id)

        if coverageOf candidate = universe then
            kept <- candidate
            dropped <- dropped @ [ id ]

    kept, dropped

let private buildCoverageMatrix () =
    let ruleIndex =
        taughtRules
        |> Set.toList
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    let idxOf = ruleIndex |> List.mapi (fun i r -> r, i) |> Map.ofList

    let jsonArr (xs: string seq) =
        xs |> Seq.map JsonSerializer.Serialize |> String.concat ","

    let sb = StringBuilder()

    sb
        .AppendLine("{")
        .AppendLine(
            "  \"_\": \"GENERATED by docs/tools/authoring-pack.fsx (Phase 841). Do not hand-edit — run --write.\","
        )
        .AppendLine("  \"source\": \"wire-format-fixtures/\",")
        .AppendLine($"  \"rules\": [{jsonArr ruleIndex}],")
        .AppendLine($"  \"pinned\": [{jsonArr pinnedExemplars}],")
        .AppendLine($"  \"systemPrompt\": [{jsonArr systemPromptExemplars}],")
        .AppendLine($"  \"fewShot\": [{jsonArr fewShotExemplars}],")
        .AppendLine("  \"coverage\": {")
    |> ignore

    let rows =
        candidateFixtures
        |> List.map (fun id ->
            let idxs =
                Map.find id fixtureRules
                |> Set.toList
                |> List.choose (fun r -> Map.tryFind r idxOf)
                |> List.sort
                |> List.map string
                |> String.concat ","

            $"    {JsonSerializer.Serialize id}: [{idxs}]")

    sb.AppendLine(String.concat ",\n" rows).AppendLine("  }").AppendLine("}")
    |> ignore

    sb.ToString()

// ── Run ──────────────────────────────────────────────────────────────────────────
if mineMode then
    let universe, covered, chosen = mine ()
    let unexemplified = Set.difference taughtRules universe
    let unreached = Set.difference universe covered

    printfn "Fuaran exemplar miner (Phase 841) — greedy set-cover over the coverage matrix"
    printfn ""
    printfn "  taught rules (inventory)   %d" (Set.count taughtRules)
    printfn "  corpus candidates          %d" (List.length candidateFixtures)
    printfn ""

    printfn
        "  current exemplars          %d (%d system-prompt + %d few-shot, deduped)"
        currentExemplars.Length
        systemPromptExemplars.Length
        fewShotExemplars.Length

    printfn
        "  current coverage           %d rules (%.1f%% of inventory)"
        (Set.count universe)
        (100.0 * float (Set.count universe) / float (Set.count taughtRules))

    printfn "  pinned coverage            %d rules" (Set.count (coverageOf pinnedExemplars))
    printfn ""
    printfn "  mined additions (greedy, pinned forced):"

    if List.isEmpty chosen then
        printfn "    (none — the pinned set already covers the current universe)"

    for id, gain in chosen do
        printfn "    +%-3d %s" gain id

    let minedSet = pinnedExemplars @ (chosen |> List.map fst)
    let bytesOf ids = ids |> List.sumBy costOf

    let displaced =
        fewShotExemplars
        |> List.filter (fun id ->
            not (List.contains id pinnedExemplars)
            && not (chosen |> List.exists (fun (c, _) -> c = id)))

    let admitted =
        chosen
        |> List.map fst
        |> List.filter (fun id -> not (List.contains id currentExemplars))

    printfn ""

    printfn
        "  mined set size             %d (%d pinned + %d mined)"
        minedSet.Length
        pinnedExemplars.Length
        chosen.Length

    printfn
        "  exemplar tree bytes        %d current → %d mined (%+d)"
        (bytesOf currentExemplars)
        (bytesOf minedSet)
        (bytesOf minedSet - bytesOf currentExemplars)

    printfn "  displaced few-shot entries %d" displaced.Length

    for id in displaced do
        printfn "    - %-46s (%d B)" id (costOf id)

    if not admitted.IsEmpty then
        printfn "  newly-admitted fixtures    %d" admitted.Length

        for id in admitted do
            printfn "    + %-46s (%d B)" id (costOf id)

    let keptSet, droppedSet = prune ()

    printfn ""
    printfn "  redundancy prune (coverage-preserving removals from the CURRENT set):"

    if List.isEmpty droppedSet then
        printfn "    (none — every current exemplar carries at least one rule uniquely)"

    for id in droppedSet do
        printfn "    - %-46s (%d B)" id (costOf id)

    printfn
        "  pruned set                 %d exemplars, %d B (%+d B)"
        keptSet.Length
        (bytesOf keptSet)
        (bytesOf keptSet - bytesOf currentExemplars)

    printfn ""

    printfn
        "  ADOPT: %s"
        (if bytesOf keptSet <= bytesOf minedSet then
             $"the pruned current set ({bytesOf keptSet} B) — greedy-from-scratch is no cheaper ({bytesOf minedSet} B)"
         else
             $"the greedy set ({bytesOf minedSet} B) — cheaper than the pruned current set ({bytesOf keptSet} B)")

    if not (Set.isEmpty unreached) then
        printfn ""
        printfn "  UNREACHED (in the current universe, no candidate covers): %d" (Set.count unreached)

        for r in unreached do
            printfn "    ! %s" r

    // The operative-surface gap. flip-4 (2026-08-02) recorded that the default posture
    // never reads few-shot, so a rule exemplified ONLY there is exemplified only for
    // the postures that opt in — a softer form of uncovered, and the one the Badge flip
    // (0/6 → 6/6 on promotion INTO the system prompt) is the worked example of.
    let fewShotOnly = Set.difference universe (coverageOf pinnedExemplars)

    printfn ""
    printfn "  exemplified ONLY in few-shot: %d rules (the default posture does not read these)" (Set.count fewShotOnly)

    for r in fewShotOnly do
        printfn "    ~ %s" r

    printfn ""
    printfn "  taught but UNEXEMPLIFIED   %d rules (the reinvestment queue)" (Set.count unexemplified)

    for fam in
        [ "idiom:"
          "pipestep:"
          "pipefn:"
          "pipeop:"
          "pipeagg:"
          "case:"
          "field:"
          "enum:" ] do
        let hits = unexemplified |> Set.filter (fun r -> r.StartsWith fam) |> Set.toList

        if not hits.IsEmpty then
            printfn "    %s %d" (fam.TrimEnd ':') hits.Length

            if fam <> "field:" && fam <> "enum:" then
                for h in hits do
                    printfn "        %s" h

    if not (Set.isEmpty inventoryEscapes) then
        printfn ""
        printfn "  PROBE FAILURE — observed rules outside the inventory: %d" (Set.count inventoryEscapes)

        for r in inventoryEscapes do
            printfn "    ? %s" r

        exit 1

    exit 0

printfn
    "Fuaran authoring pack — %s mode%s"
    (if writeMode then "write" else "check")
    (if minifyExamples then " (minified pack examples)" else "")

// 1. Corpus-derived marker examples (+ the schema-derived required-fields table) in
//    the managed markdown. Only the pack system prompt carries the catalogue.
reconcileMarkdown "AI_AUTHORING_GUIDE.md" false false
reconcileMarkdown (Path.Combine("prompt-pack", "system-prompt.md")) true true

// 2. Few-shot corpus.
reconcileFile
    (Path.Combine("prompt-pack", "few-shot.jsonl"))
    (Path.Combine(packDir, "few-shot.jsonl"))
    (buildFewShotJsonl ())

// 3. Schema copy (byte-equal to the canonical artefact).
reconcileFile
    (Path.Combine("prompt-pack", "schema.json"))
    (Path.Combine(packDir, "schema.json"))
    (File.ReadAllText schemaSrcPath)

// 4. Rule→fixture coverage matrix (Phase 841). Tooling output, not pack content — it
//    is the mining evidence, so it lives beside the generator rather than in the paid
//    prefix, and is reconciled here so a corpus change cannot age it silently.
if not (Set.isEmpty inventoryEscapes) then
    eprintfn "PROBE FAILURE — the coverage walker observed rules the enumerator cannot express:"

    for r in inventoryEscapes do
        eprintfn "  ? %s" r

    eprintfn "This is a generator defect, not a coverage finding — fix schemaRules before trusting any figure."
    exit 1

reconcileFile
    (Path.Combine("tools", "coverage-matrix.json"))
    (Path.Combine(docsDir, "tools", "coverage-matrix.json"))
    (buildCoverageMatrix ())

// ── Verdict ────────────────────────────────────────────────────────────────────
match writeMode, drift with
| true, _ ->
    printfn "Done. %d file(s) updated." wrote
    exit 0
| false, [] ->
    printfn "OK — every corpus-derived example matches the wire-format-fixtures corpus."
    exit 0
| false, ds ->
    eprintfn "DRIFT — the authoring pack diverged from the canonical wire format:"

    for d in List.rev ds do
        eprintfn "  - %s" d

    eprintfn "Regenerate with: dotnet fsi docs/tools/authoring-pack.fsx --write"
    exit 1
