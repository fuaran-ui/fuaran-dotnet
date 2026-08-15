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
// Either mode accepts `--minify-examples`, which emits the PROMPT-PACK example blocks
// as minified JSON instead of the 2-space pretty form. The decoder is
// whitespace-indifferent and the pack is a paid prefix, so the indentation buys a
// machine reader nothing and costs tokens on every request. Scope + guarantees:
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
//   * The drift check passes in either emission. Structural drift from the corpus is
//     what `--check` asserts, and whitespace is explicitly not drift (`canonicalize`).
//     `--check --minify-examples` additionally asserts the pack is IN the minified
//     form, so the adopted emission can be pinned by the build once it is gated.
//
// Corpus-derived surfaces (cannot diverge from wire-format-fixtures/):
//   * Marker blocks  <!-- fuaran:example fixture=ID -->```json …```<!-- /fuaran:example -->
//     in the managed markdown files (the authoring guide + the pack system prompt).
//   * Marker block   <!-- fuaran:required-fields --> … <!-- /fuaran:required-fields -->
//     in the pack system prompt — the per-kind required/optional field table, derived
//     from wire-format-fixtures/schema.json (the Phase 422 eval cohort showed models
//     omitting per-kind required fields when the pack didn't enumerate them).
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
    eprintfn "usage: dotnet fsi authoring-pack.fsx (--write | --check) [--minify-examples]"
    exit 2

let writeMode =
    match argv |> Array.tryHead with
    | Some "--write" -> true
    | Some "--check" -> false
    | _ -> usage ()

// An unrecognised trailing argument is a typo, not a no-op: silently ignoring
// `--minify-example` would emit the pretty form while the caller believed otherwise.
match argv |> Array.skip 1 |> Array.filter (fun a -> a <> "--minify-examples") with
| [||] -> ()
| unknown ->
    eprintfn "unknown argument(s): %s" (String.concat " " unknown)
    usage ()

let minifyExamples = argv |> Array.contains "--minify-examples"

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

// ── Per-kind required-fields table (derived from the canonical schema) ───────────
// Walks $defs.NodeKind in wire-format-fixtures/schema.json: the four category defs
// (LayoutKind / DisplayKind / InputKind / VisKind) hold oneOf cases that are either
// `allOf [ { $type const }; { $ref <Kind>Spec } ]` or an inline object case (e.g.
// Filters); the remaining non-$ref NodeKind cases (Custom, ErrorBoundary, …) are
// inline. For each kind: required = the spec's `required` minus `$type`; optional =
// its `properties` minus required minus `$type`. Because the schema itself is
// generated from the decoder, this table cannot drift from what the decoder rejects
// with MISSING_FIELD.
let buildRequiredFieldsTable () =
    use doc = JsonDocument.Parse(File.ReadAllText schemaSrcPath)
    let defs = doc.RootElement.GetProperty "$defs"

    let resolveRef (refValue: string) =
        defs.GetProperty(refValue.Substring "#/$defs/".Length)

    let fieldsOf (spec: JsonElement) =
        // 2026-07-17 — mark Binding-typed fields inline. The launch eval's biggest
        // failure family was ENVELOPE CONFUSION: models could not predict which
        // fields take a Binding envelope vs a plain value (fraction: 0.9 bare;
        // indeterminate wrapped in Static). The table now says so per field.
        let isBinding (name: string) =
            match spec.TryGetProperty "properties" with
            | true, props ->
                match props.TryGetProperty name with
                | true, p ->
                    match p.TryGetProperty "$ref" with
                    | true, r -> r.GetString() = "#/$defs/Binding"
                    | _ -> false
                | _ -> false
            | _ -> false

        let tag name =
            if isBinding name then name + "†" else name

        let required =
            match spec.TryGetProperty "required" with
            | true, r ->
                [ for e in r.EnumerateArray() do
                      let name = e.GetString()

                      if name <> "$type" then
                          tag name ]
            | _ -> []

        let optional =
            match spec.TryGetProperty "properties" with
            | true, props ->
                [ for p in props.EnumerateObject() do
                      if p.Name <> "$type" && not (List.contains p.Name required) then
                          p.Name ]
                |> List.filter (fun n -> not (List.contains (tag n) required))
                |> List.map tag
                |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
            | _ -> []

        required, optional

    // A oneOf case → (kind name, (required, optional)); None for a pure $ref case.
    let caseEntry (case: JsonElement) =
        match case.TryGetProperty "allOf" with
        | true, allOf ->
            let parts = allOf.EnumerateArray() |> Seq.toArray

            let kind =
                parts.[0].GetProperty("properties").GetProperty("$type").GetProperty("const").GetString()

            // Two allOf shapes exist: the hoisted-spec form (`[$type-const; $ref]`,
            // resolve the ref) and the inline-record-plus-constraint form
            // (Phase 768's Switch: `[record; anyOf]` — the record IS parts[0],
            // and the trailing constraint carries no field definitions).
            match parts.[1].TryGetProperty "$ref" with
            | true, r -> Some(kind, fieldsOf (resolveRef (r.GetString())))
            | _ -> Some(kind, fieldsOf parts.[0])
        | _ ->
            match case.TryGetProperty "properties" with
            | true, props ->
                match props.TryGetProperty "$type" with
                | true, t ->
                    match t.TryGetProperty "const" with
                    | true, c -> Some(c.GetString(), fieldsOf case)
                    | _ -> None
                | _ -> None
            | _ -> None

    let categoryRows =
        [ "Layout", "LayoutKind"
          "Display", "DisplayKind"
          "Input", "InputKind"
          "Visualisation", "VisKind" ]
        |> List.collect (fun (category, defName) ->
            defs.GetProperty(defName).GetProperty("oneOf").EnumerateArray()
            |> Seq.choose caseEntry
            |> Seq.map (fun (kind, fields) -> category, kind, fields)
            |> List.ofSeq)

    let structuralRows =
        defs.GetProperty("NodeKind").GetProperty("oneOf").EnumerateArray()
        |> Seq.choose caseEntry
        |> Seq.map (fun (kind, fields) -> "Structural", kind, fields)
        |> List.ofSeq

    let fieldList =
        function
        | [] -> "—"
        | fields -> fields |> List.map (sprintf "`%s`") |> String.concat ", "

    [ yield "| Kind | Required (MISSING_FIELD if absent) | Optional (omittable) |"
      yield "|---|---|---|"
      for category, kind, (required, optional) in categoryRows @ structuralRows do
          yield $"| {category} `{kind}` | {fieldList required} | {fieldList optional} |" ]
    |> String.concat "\n"

// ── Closed enum vocabulary table (derived from the canonical schema) ─────────────
// The 2026-07-15 smoke run found the sequel to the Phase 422 lesson that created the
// required-fields table: the pack REQUIRED `variant` on Heading but never enumerated
// its legal values, so every provider guessed a plausible synonym ('Title', 'Page')
// and was rejected with UNKNOWN_DU_CASE. This table walks every kind's spec
// properties to their $ref'd string-enum defs and pins kind.field → legal values —
// usage-anchored (not enum-name-only) because `variant` names a DIFFERENT enum on
// Heading vs Button vs Badge vs Image. Enums only reachable inside nested payloads
// (Binding / CellFormat / Action cases) are appended without a usage anchor so the
// vocabulary is complete. Schema-derived ⇒ cannot drift from the decoder.
let buildEnumVocabTable () =
    use doc = JsonDocument.Parse(File.ReadAllText schemaSrcPath)
    let defs = doc.RootElement.GetProperty "$defs"

    let resolveRef (refValue: string) =
        defs.GetProperty(refValue.Substring "#/$defs/".Length)

    let enumDefName (prop: JsonElement) =
        // direct { "$ref": "#/$defs/X" } or array { "items": { "$ref": … } }
        let refOf (e: JsonElement) =
            match e.TryGetProperty "$ref" with
            | true, r -> Some(r.GetString())
            | _ -> None

        let candidate =
            match refOf prop with
            | Some r -> Some r
            | None ->
                match prop.TryGetProperty "items" with
                | true, items -> refOf items
                | _ -> None

        candidate
        |> Option.map (fun r -> r.Substring "#/$defs/".Length)
        |> Option.filter (fun name ->
            let target = defs.GetProperty name

            match target.TryGetProperty "enum" with
            | true, _ ->
                match target.TryGetProperty "type" with
                | true, t -> t.GetString() = "string"
                | _ -> false
            | _ -> false)

    let enumValues (name: string) =
        defs.GetProperty(name).GetProperty("enum").EnumerateArray()
        |> Seq.map (fun v -> v.GetString())
        |> List.ofSeq

    // Same kind walk as buildRequiredFieldsTable, but keeping the spec element.
    let kindSpecs =
        let caseSpec (case: JsonElement) =
            match case.TryGetProperty "allOf" with
            | true, allOf ->
                let parts = allOf.EnumerateArray() |> Seq.toArray

                let kind =
                    parts.[0].GetProperty("properties").GetProperty("$type").GetProperty("const").GetString()

                // Same two allOf shapes as `caseEntry` above: hoisted-spec
                // (resolve the ref) vs inline-record-plus-constraint (Phase 768's
                // Switch — the record IS parts[0]).
                match parts.[1].TryGetProperty "$ref" with
                | true, r -> Some(kind, resolveRef (r.GetString()))
                | _ -> Some(kind, parts.[0])
            | _ ->
                match case.TryGetProperty "properties" with
                | true, props ->
                    match props.TryGetProperty "$type" with
                    | true, t ->
                        match t.TryGetProperty "const" with
                        | true, c -> Some(c.GetString(), case)
                        | _ -> None
                    | _ -> None
                | _ -> None

        [ "LayoutKind"; "DisplayKind"; "InputKind"; "VisKind"; "NodeKind" ]
        |> List.collect (fun defName ->
            defs.GetProperty(defName).GetProperty("oneOf").EnumerateArray()
            |> Seq.choose caseSpec
            |> List.ofSeq)

    // (enum name → the kind.field sites that use it)
    let usages =
        [ for kind, spec in kindSpecs do
              match spec.TryGetProperty "properties" with
              | true, props ->
                  for p in props.EnumerateObject() do
                      if p.Name <> "$type" then
                          match enumDefName p.Value with
                          | Some enumName -> yield enumName, $"`{kind}.{p.Name}`"
                          | None -> ()
              | _ -> () ]
        |> List.groupBy fst
        |> List.map (fun (enumName, sites) -> enumName, sites |> List.map snd |> List.distinct |> List.sort)
        |> List.sortBy fst

    let allEnumNames =
        [ for d in defs.EnumerateObject() do
              match d.Value.TryGetProperty "enum" with
              | true, _ ->
                  match d.Value.TryGetProperty "type" with
                  | true, t when t.GetString() = "string" -> d.Name
                  | _ -> ()
              | _ -> () ]
        |> List.sort

    let anchored = usages |> List.map fst |> Set.ofList
    let nested = allEnumNames |> List.filter (fun n -> not (anchored.Contains n))

    let fmtValues name =
        enumValues name |> List.map (sprintf "`%s`") |> String.concat " · "

    // Payload-DU discriminator vocabularies — oneOf defs whose cases carry a
    // `$type` const (Binding, TextSource, CellFormat, TreeOp, …). The kind
    // category defs are excluded: the kinds table above already owns them.
    // Historical failure class: `body.$type: 'Query'` (a Binding case guessed
    // into a TextSource slot), `weight.$type: 'Bold'`-style case guessing.
    // Each case carries its REQUIRED payload fields (minus $type) — the Kimi
    // smokes showed models learning the case names from the list but then
    // guessing the payload keys (`Navigate` emitted with `href` instead of the
    // required `route`, twice). Case name alone teaches half the contract.
    let discriminatorDefs =
        let excluded =
            set [ "LayoutKind"; "DisplayKind"; "InputKind"; "VisKind"; "NodeKind" ]

        let caseRequired (case: JsonElement) =
            match case.TryGetProperty "required" with
            | true, req ->
                [ for e in req.EnumerateArray() do
                      let name = e.GetString()

                      if name <> "$type" then
                          name ]
            | _ -> []

        // A payload-DU case can be the inline-record-plus-constraint allOf shape
        // (Phase 818's SetState, like Phase 768's Switch: `[record; oneOf]` — the
        // record IS parts[0] and the trailing constraint carries no fields). Unwrap
        // it so the case keeps its `$type` const and its required list; a flat case
        // passes through unchanged.
        let caseRecord (case: JsonElement) =
            match case.TryGetProperty "allOf" with
            | true, allOf -> allOf.EnumerateArray() |> Seq.head
            | _ -> case

        [ for d in defs.EnumerateObject() do
              if not (excluded.Contains d.Name) then
                  match d.Value.TryGetProperty "oneOf" with
                  | true, oneOf ->
                      let consts =
                          [ for case in oneOf.EnumerateArray() |> Seq.map caseRecord do
                                match case.TryGetProperty "properties" with
                                | true, props ->
                                    match props.TryGetProperty "$type" with
                                    | true, t ->
                                        match t.TryGetProperty "const" with
                                        | true, c -> c.GetString(), caseRequired case
                                        | _ -> ()
                                    | _ -> ()
                                | _ -> () ]

                      if not (List.isEmpty consts) then
                          d.Name, consts
                  | _ -> () ]
        |> List.sortBy fst

    // Nested collection item shapes — kind-spec properties that are arrays of
    // objects with their own `required` list (inline, or via items.$ref). The
    // single largest historical MISSING_FIELD class lived here (columns[*].kind,
    // items[*].kind, tabHeaders[*].label) — required fields the per-kind table
    // cannot see because they sit one level down.
    let nestedCollections =
        let itemObj (prop: JsonElement) =
            match prop.TryGetProperty "items" with
            | true, items ->
                let resolved =
                    match items.TryGetProperty "$ref" with
                    | true, r -> resolveRef (r.GetString())
                    | _ -> items

                match resolved.TryGetProperty "required" with
                | true, req ->
                    let required =
                        [ for e in req.EnumerateArray() do
                              e.GetString() ]

                    let optional =
                        match resolved.TryGetProperty "properties" with
                        | true, props ->
                            [ for p in props.EnumerateObject() do
                                  if not (List.contains p.Name required) then
                                      p.Name ]
                            |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
                        | _ -> []

                    if List.isEmpty required then
                        None
                    else
                        Some(required, optional)
                | _ -> None
            | _ -> None

        [ for kind, spec in kindSpecs do
              match spec.TryGetProperty "properties" with
              | true, props ->
                  for p in props.EnumerateObject() do
                      if p.Name <> "$type" then
                          match itemObj p.Value with
                          | Some(required, optional) -> yield kind, p.Name, required, optional
                          | None -> ()
              | _ -> () ]
        |> List.sortBy (fun (k, f, _, _) -> k, f)

    let fieldList =
        function
        | [] -> "—"
        | fields -> fields |> List.map (sprintf "`%s`") |> String.concat ", "

    [ yield "| Field(s) | Enum | Legal values — anything else is an `UNKNOWN_DU_CASE` reject |"
      yield "|---|---|---|"
      for enumName, sites in usages do
          let siteList = String.concat ", " sites
          yield $"| {siteList} | `{enumName}` | {fmtValues enumName} |"
      if not (List.isEmpty nested) then
          yield ""
          yield "Closed vocabularies inside nested payloads (`Binding` / `CellFormat` / `Action` cases):"
          yield ""

          for n in nested do
              yield $"- `{n}`: {fmtValues n}"
      if not (List.isEmpty discriminatorDefs) then
          yield ""

          yield
              "**`$type` discriminators are closed vocabularies too** — each of these takes exactly one of its listed cases (a `Binding` case in a `TextSource` slot, or an invented case name, is an `UNKNOWN_DU_CASE` reject). A case's REQUIRED payload fields ride in parentheses — use those exact key names (`Navigate(route)` means the key is `route`, not `href`/`url`):"

          yield ""

          for name, consts in discriminatorDefs do
              let cases =
                  consts
                  |> List.map (fun (n, required) ->
                      match required with
                      | [] -> $"`{n}`"
                      | fields -> "`" + n + "(" + String.concat ", " fields + ")`")
                  |> String.concat " · "

              yield $"- `{name}.$type`: {cases}"
      if not (List.isEmpty nestedCollections) then
          yield ""

          yield
              "**Nested collection items carry required fields of their own** — the per-kind table above stops at the kind's top level; each item in these arrays must ALSO carry its required fields (`MISSING_FIELD` on absence):"

          yield ""
          yield "| Collection | Each item requires | Optional per item |"
          yield "|---|---|---|"

          for kind, field, required, optional in nestedCollections do
              yield $"| `{kind}.{field}[]` | {fieldList required} | {fieldList optional} |" ]
    |> String.concat "\n"

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

// <!-- fuaran:required-fields -->
// | Kind | Required … | Optional … |   (schema-derived; see buildRequiredFieldsTable)
// <!-- /fuaran:required-fields -->
let private tableRegex =
    Regex(
        @"<!-- fuaran:required-fields -->\r?\n(?<body>.*?)\r?\n<!-- /fuaran:required-fields -->",
        RegexOptions.Singleline
    )

// <!-- fuaran:enum-vocab -->
// | Field(s) | Enum | Legal values … |   (schema-derived; see buildEnumVocabTable)
// <!-- /fuaran:enum-vocab -->
let private vocabRegex =
    Regex(@"<!-- fuaran:enum-vocab -->\r?\n(?<body>.*?)\r?\n<!-- /fuaran:enum-vocab -->", RegexOptions.Singleline)

// `packSurface` marks a file that ships as part of the paid prompt prefix, and so is
// subject to `--minify-examples`. The human-facing authoring guide is not one.
let reconcileMarkdown (relPath: string) (expectTable: bool) (packSurface: bool) =
    let minifyHere = packSurface && minifyExamples
    let path = Path.Combine(docsDir, relPath)
    let original = File.ReadAllText path
    let mutable matched = 0
    let mutable tablesMatched = 0

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
                        $"{relPath}: example '{id}' is structurally correct but not minified (run authoring-pack.fsx --write --minify-examples)"

                // In --write we always re-emit the canonical form for the requested
                // emission (which normalises whitespace too); in --check we leave the
                // text as-is — the compares above already decided pass/fail.
                let newBody = if writeMode then expectedBody else body
                $"<!-- fuaran:example fixture={id} -->\n```json\n{newBody}\n```\n<!-- /fuaran:example -->"
        )

    let rebuilt =
        tableRegex.Replace(
            rebuilt,
            fun (m: Match) ->
                tablesMatched <- tablesMatched + 1
                let body = m.Groups.["body"].Value
                let expected = buildRequiredFieldsTable ()

                if normalizeEol body <> expected then
                    if not writeMode then
                        reportDrift $"{relPath}: required-fields table diverges from wire-format-fixtures/schema.json"

                let newBody = if writeMode then expected else body
                $"<!-- fuaran:required-fields -->\n{newBody}\n<!-- /fuaran:required-fields -->"
        )

    let mutable vocabMatched = 0

    let rebuilt =
        vocabRegex.Replace(
            rebuilt,
            fun (m: Match) ->
                vocabMatched <- vocabMatched + 1
                let body = m.Groups.["body"].Value
                let expected = buildEnumVocabTable ()

                if normalizeEol body <> expected then
                    if not writeMode then
                        reportDrift $"{relPath}: enum-vocab table diverges from wire-format-fixtures/schema.json"

                let newBody = if writeMode then expected else body
                $"<!-- fuaran:enum-vocab -->\n{newBody}\n<!-- /fuaran:enum-vocab -->"
        )

    if matched = 0 then
        reportDrift $"{relPath}: no <!-- fuaran:example --> blocks found (marker contract broken?)"

    if expectTable && tablesMatched = 0 then
        reportDrift $"{relPath}: no <!-- fuaran:required-fields --> block found (marker contract broken?)"

    // The pack system prompt carries the vocab block alongside the required-fields
    // table (same expectTable file set) — a missing block is a broken contract.
    if expectTable && vocabMatched = 0 then
        reportDrift $"{relPath}: no <!-- fuaran:enum-vocab --> block found (marker contract broken?)"

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
//    the managed markdown. Only the pack system prompt carries the table.
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
