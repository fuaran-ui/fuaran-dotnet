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
    eprintfn "usage: dotnet fsi authoring-pack.fsx (--write | --check) [--minify-examples | --pretty-examples]"
    exit 2

let writeMode =
    match argv |> Array.tryHead with
    | Some "--write" -> true
    | Some "--check" -> false
    | _ -> usage ()

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
      "markdown-1", "Render a short markdown note that says 'Updated hourly.'."
      "callout-1", "Warn the user with a dismissable callout that live data is delayed."
      "btn-1", "A primary 'Refresh' button with a refresh icon, disabled while the 'loading' state flag is true."
      "form-1",
      "A save form: a required name field, an age number, a required 'I agree' checkbox, a tier dropdown, and a notes text area."
      "filters-declarative",
      "A filter strip that scopes a dataset: a search box, a tier dropdown, and an age range — self-wiring, no host code (each chip omits onChange and its value reads its own filter)."
      "filters-date-range",
      "A filter strip with a single date-range chip: pick a start and end date in one control, scoping everything downstream through one filter param."
      "lenient-grid-transform-param-compact",
      "A grid of embedded department data scoped by a filter: the transform's filter step compares the dept column to a param sourced from the 'dept' filter chip, so the grid re-filters as the chip changes."
      // grid-toned-pill: cut 2026-08-15 (Phase 834 dedup — system-prompt block).
      "query-dependson",
      "A revenue metric fed by a host 'orders' query that declares it depends on the status and region filters — the host re-runs the query when either filter changes."
      "discl-1", "A collapsible 'Additional entitlements' section, open by default, containing a short note."
      "tabs-explicit-1", "A horizontal tab panel with explicit 'Overview' and 'Detail' headers, second tab active."
      "custom-1", "Emit the host-registered custom 'trend-card' component from the 'analytics' module."
      "op-replacebinding",
      "Edit the existing tree: pin node 'metric-1' to a static figure of 99.5 by replacing its Source binding."
      "op-insertchild", "Edit the existing tree: add the revenue metric to the empty dashboard."
      "op-reorderchildren",
      "Edit the existing tree: put the markdown note above the metric in 'stack-1' by stating the order."
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

// ── Run ──────────────────────────────────────────────────────────────────────────
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
