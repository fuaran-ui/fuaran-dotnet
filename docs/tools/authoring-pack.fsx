// authoring-pack.fsx — generate + drift-check the Fuaran AI-authoring pack.
//
// The artefact that makes a developer's own AI emit Fuaran is the system prompt +
// few-shot + JSON schema + tool definitions. Those must NOT drift from the canonical
// wire format the corpus + JsonDecode pin (flat `kind.$type`, spec fields hoisted, no
// `spec` wrapper). This script makes the corpus the single source of truth for every
// wire-shape example in the docs + prompt pack, so a hand edit cannot silently drift.
//
//   dotnet fsi authoring-pack.fsx --write                     # regenerate every corpus-derived surface
//   dotnet fsi authoring-pack.fsx --check                     # verify nothing drifted (exit 1 on drift)
//   dotnet fsi authoring-pack.fsx --write --dialect lenient   # regenerate the Phase 840 lenient-dialect
//                                                             # pack variant (docs/prompt-pack-lenient/;
//                                                             # decoder-proved — build JsonDecode.Tests first)
//   dotnet fsi authoring-pack.fsx --write --family all        # compile the Phase 843 per-family pack
//                                                             # variants (docs/prompt-pack-variants/<id>/)
//   dotnet fsi authoring-pack.fsx --write --family <id> \     # …with the OPTIONAL per-host registered-
//       --host-manifest <path> --out <dir>                    # components section (default OFF; never
//                                                             # written into the committed variant set)
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
// Record-derived surfaces (cannot diverge from the recorded flips):
//   * tools/section-demand-index.json — the Phase 843 per-family (section → needed |
//     never-needed | unknown) map, distilled from prompt-pack/SLIMMING-CENSUS.md and
//     prompt-pack/demand-attribution.jsonl. Every verdict carries the record line
//     that decided it, and `unknown` defaults to INCLUDED.
//   * prompt-pack-variants/<family>/ — the compiled per-family packs, each a
//     versioned artefact with an inclusion manifest under the Phase 383 hash
//     discipline. Emitted only under `--family`, so a bare `--write` can never
//     revert them and they can never revert the canonical pack.
//
// The script reads only docs/ + wire-format-fixtures/ and writes only docs/. It is the
// generation source the Phase 110 drift-check Build target runs in --check mode.

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Text.Encodings.Web
open System.Xml.Linq

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
    eprintfn
        "usage: dotnet fsi authoring-pack.fsx (--write | --check | --mine) [--minify-examples | --pretty-examples]\n\
         \                                    [--dialect lenient]\n\
         \                                    [--family <id|all> [--host-manifest <path> --out <dir>]]"

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

// `--dialect lenient` (Phase 840) selects the DIALECT surfaces: the sibling
// `docs/prompt-pack-lenient/` pack variant, whose example blocks re-emit in the
// taught §16 shorthand and are decoder-verified loss-free (dialect-verify.fsx).
// The canonical pack is untouched by a dialect run, and a bare `--write` never
// touches the dialect variant — the two emissions are separate artefact sets,
// each reproducible only by its own invocation, so neither can silently revert
// the other.
let dialectMode, argvSansDialect =
    match argv |> Array.tryFindIndex ((=) "--dialect") with
    | None -> false, argv
    | Some i when i + 1 < argv.Length && argv[i + 1] = "lenient" -> true, Array.append argv[.. i - 1] argv[i + 2 ..]
    | Some _ ->
        eprintfn "--dialect takes exactly one value: lenient"
        usage ()

if dialectMode && mineMode then
    eprintfn "--dialect does not combine with --mine (the miner runs on the canonical pack only)"
    usage ()

/// Pull a `--name <value>` pair out of an argument array, returning the value (when
/// present) and the array with BOTH tokens removed.
///
/// A flag supplied without a value is a typo, never a default. Every option this
/// takes selects a compilation dimension, and a dimension chosen by omission is a
/// variant nobody asked for — the same argument the unknown-argument guard below
/// makes about `--minify-example`.
let private takeOption (name: string) (args: string array) : string option * string array =
    match args |> Array.tryFindIndex ((=) name) with
    | None -> None, args
    | Some i when i + 1 < args.Length && not (args[i + 1].StartsWith "--") ->
        Some args[i + 1], Array.append args[.. i - 1] args[i + 2 ..]
    | Some _ ->
        eprintfn "%s takes exactly one value" name
        usage ()

// `--family <id|all>` (Phase 843) selects the PER-FAMILY COMPILED PACK surfaces —
// the `docs/prompt-pack-variants/<family>/` artefact set. Like `--dialect`, it is its
// own invocation: a bare `--write` never touches a variant and a `--family` run never
// touches the canonical pack, so neither emission can silently revert the other.
let familyArg, argvSansFamily = takeOption "--family" argvSansDialect
let hostManifestArg, argvSansHost = takeOption "--host-manifest" argvSansFamily
let outArg, argvRest = takeOption "--out" argvSansHost

let familyMode = familyArg.IsSome

if familyMode && mineMode then
    eprintfn "--family does not combine with --mine (the miner runs on the canonical pack only)"
    usage ()

if familyMode && dialectMode then
    eprintfn
        "--family does not combine with --dialect: the dialect is a COMPILED dimension, chosen per \
         family from the flip record's own per-family verdicts, not set by hand"

    usage ()

if hostManifestArg.IsSome && not familyMode then
    eprintfn "--host-manifest is a --family dimension; supply --family <id|all> as well"
    usage ()

// The per-host section is default-OFF and its output is default-NOWHERE. A host
// registry is host-specific by definition, so a run carrying one may not write into
// the committed, host-free variant set — `--out` is mandatory and is the only place
// such a run writes.
if hostManifestArg.IsSome && outArg.IsNone then
    eprintfn
        "--host-manifest requires --out <dir>: a host-specific variant is not the committed artefact \
         and must never overwrite it"

    usage ()

if outArg.IsSome && hostManifestArg.IsNone then
    eprintfn "--out is only meaningful with --host-manifest (the committed variants have a fixed home)"
    usage ()

// An unrecognised trailing argument is a typo, not a no-op: silently ignoring
// `--minify-example` would emit an emission the caller did not choose.
match
    argvRest
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
// §21's shape-limit fixtures nest past System.Text.Json's default 64-level reader
// ceiling (`nodes/limit-node-depth-at-max.json` exists precisely to sit AT the
// node-depth maximum, corpus bc5fcc0), so every parse that can receive fixture
// bytes takes these options. 512 clears the §21 ceiling with headroom while still
// bounding a hostile input; without this, the whole `--check` gate throws on the
// first deep fixture it reads.
let private deepJson = JsonDocumentOptions(MaxDepth = 512)

// Pretty form for human-facing docs: indented, with `<closure>` / `<opaque>` left
// literal (the relaxed encoder keeps `<` `>` unescaped, matching the canonical wire).
let private prettyOpts =
    JsonSerializerOptions(WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 512)

let prettyJson (raw: string) =
    use doc = JsonDocument.Parse(raw, deepJson)
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
    use doc = JsonDocument.Parse(raw, deepJson) // parse first: never emit bytes we could not read
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
    use doc = JsonDocument.Parse(raw, deepJson)
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
            isHoistedAllOf el
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
    //
    // `allOf` is NOT on its own the hoist marker, and reading it as one is a crash
    // rather than a wrong rendering. A case may carry its own `properties`/`$type`
    // and use `allOf` purely to REFUSE a legacy field (`allOf: [{ not: { required:
    // ["position"] } }]` on InsertChild / MoveNode). The hoist is recognised by its
    // shape instead: two-or-more members whose FIRST carries the `$type` const.
    // A refusal guard renders as nothing — the catalogue lists the vocabulary you may
    // emit, and a field the schema forbids has no spelling to teach.
    and isHoistedAllOf (case: JsonElement) =
        hasProp "allOf" case
        && (let parts = case.GetProperty("allOf").EnumerateArray() |> Seq.toArray

            parts.Length >= 2
            && hasProp "properties" parts.[0]
            && hasProp "$type" (parts.[0].GetProperty "properties"))

    and renderAlternative (case: JsonElement) : string =
        if isHoistedAllOf case then
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
//
// THE PROSE-FIRST SPLIT, AND WHY IT IS A STANDING RULE RATHER THAN ONE PHASE'S NOTE.
// A marker block reconciles against the CORPUS, which is at head — so a block using
// vocabulary the head corpus carries passes this check even when no PUBLISHED package
// can decode it. The pack's consumers restore released packages, so an exemplar whose
// spelling only exists in an unreleased version is an example that fails in the reader's
// own hands. Teaching for new vocabulary therefore lands as PROSE first (a prose line has
// no decode gate) and gains its fenced exemplar at the publish that carries the fields.
//
// CLOSED 2026-08-27, and left here as the worked instance. Five exemplars were owed
// against the 0.36.0–0.39.0 grid / form / metric vocabulary; `v0.39.0` was then tagged and
// verified served by the registry, so all five landed in the same shape they were deferred
// in — `grid-bound-sort` (sortStateKey + columns[].sortable + defaultSort), `grid-paged`
// (pageSize + pageStateKey), `grid-declared-edit` (editStateKey + per-column editable),
// `form-field-rules` (FormField.rule) and `metric-inverted-polarity` (trendPolarity).
//
// The check to run before adding a block for NEW vocabulary: compare the pack manifest's
// `languageVersion` stamp against the newest version the registry actually serves. A tag
// is evidence the release gesture was made, never that the index serves it — and note the
// npm tier releases on its own train, so a `.NET` publication does not answer for it.
let private markerRegex =
    Regex(
        @"<!-- fuaran:example fixture=(?<id>[A-Za-z0-9._-]+) -->\r?\n```json\r?\n(?<body>.*?)\r?\n```\r?\n<!-- /fuaran:example -->",
        RegexOptions.Singleline
    )

// The managed-block regex above is the ONLY thing that sees an example, and it
// requires the fence: a marker PAIR enclosing no ```json block matches nothing,
// so `--write` emits no content for it and `--check` passes over it in silence.
// That is a gate that cannot fail on the commonest authoring mistake — adding
// the markers first and expecting the writer to fill the body — and it was hit
// live (Phase 750, 2026-07-30), caught only by someone reading the diff.
//
// So malformed pairs are found structurally rather than by regex-that-must-match:
// walk the OPEN markers in order and decide, for each, whether the managed
// rewriter could possibly have seen it.
let private openMarkerRegex =
    Regex(@"<!-- fuaran:example fixture=(?<id>[A-Za-z0-9._-]+) -->")

[<Literal>]
let private closeMarker = "<!-- /fuaran:example -->"

/// A well-formed body: the fenced ```json block the managed regex requires, and
/// nothing else of substance between the markers.
let private fencedJsonBodyRegex =
    Regex(@"^\r?\n```json\r?\n.*\r?\n```\r?\n$", RegexOptions.Singleline)

/// Every `fuaran:example` marker the managed-block rewriter cannot see, with the
/// reason. An empty list means every opening marker is part of a pair the
/// rewriter will process.
let private malformedExampleMarkers (text: string) : (string * string) list =
    [ for m in openMarkerRegex.Matches text do
          let id = m.Groups.["id"].Value
          let rest = text.Substring(m.Index + m.Length)
          let closeIdx = rest.IndexOf(closeMarker, StringComparison.Ordinal)
          let nextOpen = openMarkerRegex.Match rest

          if closeIdx < 0 then
              yield id, $"the opening marker has no matching `{closeMarker}`"
          elif nextOpen.Success && nextOpen.Index < closeIdx then
              yield id, "a second opening marker appears before this one closes (markers do not nest)"
          else
              let between = rest.Substring(0, closeIdx)

              if not (fencedJsonBodyRegex.IsMatch between) then
                  yield
                      id,
                      (if between.Trim() = "" then
                           "the marker pair encloses no fenced ```json block"
                       else
                           "the marker pair encloses no well-formed fenced ```json block") ]

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

    // Reported in BOTH modes, unlike the drift compares above: a malformed pair is
    // not something `--write` can fix — the writer has no body to re-emit — so a
    // silent `--write` would leave the author believing the example landed. In
    // write mode the verdict block ignores accumulated drift, hence the direct
    // stderr line here as well.
    for id, reason in malformedExampleMarkers original do
        let msg =
            $"{relPath}: example '{id}' — {reason}. The managed-block rewriter cannot see this pair, so --write emits nothing for it and --check would otherwise pass over it in silence. Add the fenced ```json body (any content — --write replaces it with the canonical fixture)."

        if writeMode then
            eprintfn "WARNING — %s" msg
        else
            reportDrift msg

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

// ── The lenient emission dialect (Phase 840) ─────────────────────────────────────
// §16's lenient-accept profile exists decoder-side as a silent safety net; Phase 840
// inverts the posture for ONE pack variant: teach the tersest decodable form as the
// primary emission dialect and let the decoder normalise to canonical. Three parts:
//
//   * A CLASSIFICATION of the whole leniency surface (the corpus's lenient-accept
//     family is the executable enumeration): which shorthands are total AND loss-free
//     AND token-positive (taught as primary), which are safe but buy nothing (never
//     taught), which are the canonical form already, and which are partial/heuristic/
//     contextual (never taught — they stay a safety net). Emitted as a generated
//     appendix (DIALECT-APPENDIX.md); every lenient fixture id must be claimed by
//     exactly one family, so a NEW leniency landing in the corpus fails this script
//     until it is classified — the appendix tracks decoder changes by construction.
//   * A MECHANICAL canonical→dialect transform (`toDialect`) applying only the
//     taught families, each locally guarded to its total, loss-free domain. Run to a
//     fixpoint, so an emitted dialect block is idempotent under the transform —
//     which is the one-dialect-per-variant purity property, enforced structurally.
//   * A DECODER PROOF per emitted block (dialect-verify.fsx, spawned): the real
//     decoder decodes both forms and the canonical re-encodes must be byte-equal.
//     "Loss-free" is proved per artefact, never assumed from the classification.
//
// The dialect pack variant lives at `docs/prompt-pack-lenient/` — a sibling artefact
// set (system-prompt.md + few-shot.jsonl + schema.json), fully generated from the
// canonical pack + the corpus; nothing in it is hand-authored. The signature
// catalogue and all prose teaching the WIRE stay canonical in both variants — the
// wire contract does not change; only the example encodings and the added dialect
// passage differ. TreeOp example blocks stay canonical in both variants: all 60
// lenient-accept fixtures pin NODE decode, so no op-position shorthand is
// corpus-pinned cross-host, and an op emission teaching an unpinned leniency would
// be a private-dialect defect (§16: a conformant decoder must not extend the
// profile).

let dialectPackDir = Path.Combine(docsDir, "prompt-pack-lenient")

// ── A tiny raw-preserving JSON AST ──
// Leaves keep their exact source text (number formatting, string escapes), so the
// transform can only ever change STRUCTURE it explicitly rewrites — the same
// argument the minifier's scanner makes, in AST form.
type private JNode =
    | JLeaf of string
    | JObj of (string * JNode) list
    | JArr of JNode list

let rec private ofElement (el: JsonElement) : JNode =
    match el.ValueKind with
    | JsonValueKind.Object -> JObj [ for p in el.EnumerateObject() -> p.Name, ofElement p.Value ]
    | JsonValueKind.Array -> JArr [ for v in el.EnumerateArray() -> ofElement v ]
    | _ -> JLeaf(el.GetRawText())

let rec private writeJ (sb: StringBuilder) (n: JNode) =
    match n with
    | JLeaf raw -> sb.Append raw |> ignore
    | JObj fields ->
        sb.Append '{' |> ignore

        fields
        |> List.iteri (fun i (k, v) ->
            if i > 0 then
                sb.Append ',' |> ignore

            sb.Append(JsonSerializer.Serialize k).Append ':' |> ignore
            writeJ sb v)

        sb.Append '}' |> ignore
    | JArr items ->
        sb.Append '[' |> ignore

        items
        |> List.iteri (fun i v ->
            if i > 0 then
                sb.Append ',' |> ignore

            writeJ sb v)

        sb.Append ']' |> ignore

let private jString =
    function
    | JLeaf r when r.StartsWith "\"" -> Some(JsonSerializer.Deserialize<string> r)
    | _ -> None

let private jField (name: string) =
    function
    | JObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
    | _ -> None

let private jTypeTag (n: JNode) = jField "$type" n |> Option.bind jString

// ── The taught transform families, each guarded to its total loss-free domain ──

/// T1 — Static-envelope elision: `{"$type":"Static","value":V}` → `V` for a scalar
/// or array V (§3.6: a bare scalar/array in a Binding slot can only mean Static).
/// Never for an object V (a bare object without `$type` is refused as more plausibly
/// a mistyped binding) and never for null/absent (ambiguous with absence).
let private staticEnvelopeValue (n: JNode) : JNode option =
    match n with
    | JObj fields when fields.Length = 2 ->
        match jTypeTag n, jField "value" n with
        | Some "Static", Some(JLeaf raw) when raw <> "null" -> Some(JLeaf raw)
        | Some "Static", Some(JArr items) -> Some(JArr items)
        | _ -> None
    | _ -> None

/// T2 — option compaction: `{"label":S,"value":S}` → `S` (the HTML `<select>`
/// prior, §3.6 SelectOption rule), only when label and value are the SAME string.
let private isCollapsibleOption (n: JNode) =
    match n with
    | JObj [ (ka, va); (kb, vb) ] when (ka = "label" && kb = "value") || (ka = "value" && kb = "label") ->
        match jString va, jString vb with
        | Some a, Some b -> a = b
        | _ -> false
    | _ -> false

let private optionLabel (n: JNode) =
    match jField "label" n with
    | Some l -> l
    | None -> n

/// T3 guard — a column envelope `{"values":[…],"validity":[true,…]}` whose mask is
/// all-true (the only mask a bare array can denote — the wire has no null).
let private collapsibleColumn (n: JNode) : JNode option =
    match n with
    | JObj fields when fields.Length = 2 ->
        match jField "values" n, jField "validity" n with
        | Some(JArr vs), Some(JArr mask) when mask.Length = vs.Length && mask |> List.forall (fun m -> m = JLeaf "true") ->
            Some(JArr vs)
        | _ -> None
    | _ -> None

/// T4 guard — deterministic column-type inference per the §3.6 rule (all-int→int,
/// any-fractional→float, all-bool→bool, all-string→string; NEVER date/timestamp;
/// empty or mixed → no inference). The schema is dropped only when inference
/// reproduces every declared type exactly, so the drop is loss-free by check, not
/// by hope.
let private inferColumnType (cells: JNode list) : string option =
    let raws =
        cells
        |> List.map (function
            | JLeaf r -> Some r
            | _ -> None)

    if cells.IsEmpty || raws |> List.exists Option.isNone then
        None
    else
        let rs = raws |> List.map Option.get

        let isNum (r: string) =
            r.Length > 0
            && r <> "true"
            && r <> "false"
            && not (r.StartsWith "\"")
            && r <> "null"

        if rs |> List.forall (fun r -> r.StartsWith "\"") then
            Some "string"
        elif rs |> List.forall (fun r -> r = "true" || r = "false") then
            Some "bool"
        elif rs |> List.forall isNum then
            if rs |> List.exists (fun r -> r.Contains "." || r.Contains "e" || r.Contains "E") then
                Some "float"
            else
                Some "int"
        else
            None

/// T6 — flat filter step: a `filter` whose pred is exactly ONE binary comparison of
/// a column against a param, with an op the corpus pins flat (`eq` — lenient-
/// transform-flat-filter; `contains` — lenient-transform-flat-contains). Any other
/// predicate shape keeps the full `pred` form — the flat spelling for literals or
/// composed predicates is NOT corpus-pinned, so teaching it would be a private
/// dialect.
let private flattenFilterStep (n: JNode) : JNode option =
    match n with
    | JObj [ _; _ ] when jTypeTag n = Some "filter" ->
        match jField "pred" n with
        | Some pred when jTypeTag pred = Some "binary" ->
            match jField "left" pred, jField "op" pred, jField "right" pred with
            | Some left, Some(JLeaf opRaw), Some right when
                jTypeTag left = Some "col"
                && jTypeTag right = Some "param"
                && (opRaw = "\"eq\"" || opRaw = "\"contains\"")
                ->
                match jField "name" left, jField "name" right with
                | Some(JLeaf col), Some(JLeaf param) ->
                    Some(
                        JObj
                            [ "$type", JLeaf "\"filter\""
                              "column", JLeaf col
                              "op", JLeaf opRaw
                              "param", JLeaf param ]
                    )
                | _ -> None
            | _ -> None
        | _ -> None
    | _ -> None

let rec private rw (key: string) (node: JNode) : JNode =
    match node with
    | JLeaf _ -> node
    | JArr items ->
        let items = items |> List.map (rw key)

        // T2 — gated to option positions (`options`, a Select's `source`) so a
        // data array whose rows happen to be {label,value} pairs is never touched.
        if
            (key = "options" || key = "source")
            && not items.IsEmpty
            && items |> List.forall isCollapsibleOption
        then
            JArr(items |> List.map optionLabel)
        else
            JArr items
    | JObj _ ->
        let fields =
            match node with
            | JObj fs -> fs |> List.map (fun (k, v) -> k, rw k v)
            | _ -> []

        // T7 — DateRange value pair: `{"from":A,"to":B}` → `[A,B]` (pinned by
        // lenient-daterange-bare-array; a two-element array at that position can
        // only mean from/to). Gated on the sibling `$type` being DateRange.
        let fields =
            if fields |> List.exists (fun (k, v) -> k = "$type" && v = JLeaf "\"DateRange\"") then
                fields
                |> List.map (fun (k, v) ->
                    match k, v with
                    | "value", JObj [ ("from", f); ("to", t) ] when (jString f).IsSome && (jString t).IsSome ->
                        "value", JArr [ f; t ]
                    | _ -> k, v)
            else
                fields

        // T5 — params map (pinned by lenient-shape-params-map): a Transform's
        // params array whose every element is exactly {name, from} becomes the
        // name→binding map (params are a name-keyed SET — key order carries no
        // meaning, so the map is loss-free).
        let fields =
            if fields |> List.exists (fun (k, v) -> k = "$type" && v = JLeaf "\"Transform\"") then
                fields
                |> List.map (fun (k, v) ->
                    match k, v with
                    | "params", JArr elems when
                        not elems.IsEmpty
                        && elems
                           |> List.forall (fun e ->
                               match e with
                               | JObj efs when efs.Length = 2 ->
                                   (jField "name" e |> Option.bind jString).IsSome && (jField "from" e).IsSome
                               | _ -> false)
                        ->
                        let pairs =
                            elems
                            |> List.map (fun e ->
                                (jField "name" e |> Option.bind jString).Value, (jField "from" e).Value)

                        let names = pairs |> List.map fst

                        // Order guard (decoder-proof finding): the map coercion
                        // re-encodes the params array in NAME-SORTED order, so the
                        // byte-exact transform applies only when the canonical
                        // array is already name-sorted. (Emission-side the map is
                        // order-free — params are a name-keyed set — but a
                        // generated artefact holds itself to byte-parity.)
                        if
                            names |> List.distinct |> List.length = names.Length
                            && names = List.sortWith (fun a b -> String.CompareOrdinal(a, b)) names
                        then
                            "params", JObj pairs
                        else
                            k, v
                    | _ -> k, v)
            else
                fields

        // T3 + T4 — embedded columnar source: collapse all-valid column envelopes
        // to bare arrays, then drop a schema that inference provably reproduces.
        let fields =
            match fields |> List.tryFind (fun (k, _) -> k = "columns") with
            | Some("columns", JObj cols) ->
                let cols =
                    cols
                    |> List.map (fun (name, col) ->
                        match collapsibleColumn col with
                        | Some bare -> name, bare
                        | None -> name, col)

                let fields =
                    fields |> List.map (fun (k, v) -> if k = "columns" then k, JObj cols else k, v)

                let schemaDroppable =
                    match fields |> List.tryFind (fun (k, _) -> k = "schema") with
                    | Some("schema", JArr entries) when not entries.IsEmpty ->
                        let declared =
                            entries
                            |> List.map (fun e ->
                                match e with
                                | JObj efs when efs.Length = 2 ->
                                    match
                                        jField "name" e |> Option.bind jString, jField "type" e |> Option.bind jString
                                    with
                                    | Some n, Some t -> Some(n, t)
                                    | _ -> None
                                | _ -> None)

                        if declared |> List.exists Option.isNone then
                            false
                        else
                            let declared = declared |> List.map Option.get

                            // Order guard (decoder-proof finding): schema entry
                            // order is SEMANTIC on the canonical wire (re-encode
                            // preserves it), and inference derives its order from
                            // the columns object's key order — so the drop is
                            // loss-free only when the declared order matches the
                            // column order exactly, not merely as a set.
                            declared |> List.map fst = (cols |> List.map fst)
                            && declared
                               |> List.forall (fun (n, t) ->
                                   match cols |> List.tryFind (fun (cn, _) -> cn = n) with
                                   | Some(_, JArr cells) -> inferColumnType cells = Some t
                                   | _ -> false)
                    | _ -> false

                if schemaDroppable then
                    fields |> List.filter (fun (k, _) -> k <> "schema")
                else
                    fields
            | _ -> fields

        let node = JObj fields

        // T1 — Static-envelope elision, anywhere in a Node document (any Binding
        // slot; the decoder proof backstops the position argument per block).
        match staticEnvelopeValue node with
        | Some v -> v
        | None ->
            // T6 — flat filter step.
            match flattenFilterStep node with
            | Some flat -> flat
            | None -> node

/// The canonical→dialect transform, run to a FIXPOINT: rewrites cascade (an
/// envelope elision exposes an option array to compaction) and the fixpoint is the
/// one-dialect purity property — an emitted block is invariant under its own
/// transform, so no canonical spelling a taught family covers can survive in it.
let toDialect (raw: string) : string =
    use doc = JsonDocument.Parse(raw, deepJson)
    let mutable ast = ofElement doc.RootElement
    let mutable go = true
    let mutable iterations = 0

    while go do
        let next = rw "$" ast
        iterations <- iterations + 1

        if next = ast || iterations > 10 then
            go <- false

        ast <- next

    let sb = StringBuilder()
    writeJ sb ast
    sb.ToString()

// ── The leniency-surface classification (the generated appendix's data) ──────────

type private DialectClass =
    /// Total, loss-free, token-positive — taught as the primary emission dialect.
    | TaughtPrimary
    /// Total and loss-free, but buys no tokens (or contradicts the taught
    /// catalogue spelling) — accepted, never taught.
    | SafeNotTaught
    /// The terse side of the pair IS the canonical form — the leniency accepts the
    /// VERBOSE spelling, so there is nothing to teach beyond the canonical rule.
    | AlreadyCanonical
    /// Partial, heuristic, or contextual — never taught; stays a decode-side
    /// safety net. A leniency whose normalisation cannot be proved loss-free for
    /// every legal input is in this class by default.
    | NeverTaught
    /// A composite fixture exercising several taught families at once (the pack's
    /// own compact exemplars).
    | Composite

    member this.Label =
        match this with
        | TaughtPrimary -> "taught-primary"
        | SafeNotTaught -> "safe-not-taught"
        | AlreadyCanonical -> "already-canonical"
        | NeverTaught -> "never-taught"
        | Composite -> "composite"

/// One leniency family: its corpus pins + the classification judgement + the
/// evidence the judgement rests on. The FixtureIds partition is asserted total
/// over the manifest's lenient-accept family, so a new leniency cannot land
/// unclassified.
type private LeniencyFamily =
    { Name: string
      Class: DialectClass
      FixtureIds: string list
      Evidence: string }

let private leniencyFamilies: LeniencyFamily list =
    [ { Name = "Static-envelope elision (bare scalar / array in a Binding slot)"
        Class = TaughtPrimary
        FixtureIds =
          [ "lenient-shape-a11y-label-bare-scalar"
            "lenient-shape-binding-scalar-fraction" ]
        Evidence =
          "JUDGEMENT: total + loss-free — §3.6: every Binding case is $type-discriminated, so a bare "
          + "array/scalar can only mean Static; bare objects and null stay refused (ambiguity preserved). "
          + "Token-positive: ~24 chars per slot. Decoder-proof per emitted block. The Accessibility "
          + "trait's label/hidden fixture is the SAME rule at a second position, not a second rule — "
          + "its own manifest entry says so ('the general §3.6 scalar rule'), so it joins this family "
          + "rather than minting one that would restate the identical judgement." }
      { Name = "Option as bare string (label = value)"
        Class = TaughtPrimary
        FixtureIds =
          [ "lenient-shape-options-bare-strings"
            "lenient-shape-segmented-orientation-omitted" ]
        Evidence =
          "JUDGEMENT: total + loss-free on its domain — a bare string option denotes exactly "
          + "{label:s, value:s} (§3.6 SelectOption rule, the HTML <select> prior). Applied only where "
          + "label equals value; distinct labels keep the object form. (The segmented fixture also pins "
          + "orientation-omitted-⇒-Horizontal — the omitted-default family below.)" }
      { Name = "Embedded source column as bare array (validity elided)"
        Class = TaughtPrimary
        FixtureIds = [ "lenient-transform-bare-columns" ]
        Evidence =
          "JUDGEMENT: total + loss-free when the mask is all-true — the wire has no JSON null, so a bare "
          + "array can only denote all-present (§3.6, §16.1 explicitly PREFERS this form). Guarded: a "
          + "column with any false validity keeps its envelope." }
      { Name = "Schema omission (inferable column types)"
        Class = TaughtPrimary
        FixtureIds = [ "lenient-transform-schemaless" ]
        Evidence =
          "JUDGEMENT: loss-free ONLY on the guarded domain — types infer deterministically for "
          + "string/int/float/bool (fuaran-core columnar codec authority); date/timestamp NEVER infer and "
          + "empty/mixed refuse. The transform drops a schema only when inference reproduces every "
          + "declared type exactly (checked per column), so e.g. a float column of integral literals "
          + "keeps its schema. §16.1 PREFERS the omitted form on this domain." }
      { Name = "Transform.params as a name→binding map"
        Class = TaughtPrimary
        FixtureIds = [ "lenient-shape-params-map" ]
        Evidence =
          "JUDGEMENT: total + loss-free — params are a name-keyed SET (ColExpr.Param lookup), so object "
          + "key order carries no meaning (§3.6, contrast the refused options-map form where order IS "
          + "meaningful). Applied only when every element is exactly {name, from} with distinct names." }
      { Name = "Flat filter step (column/op/param)"
        Class = TaughtPrimary
        FixtureIds = [ "lenient-transform-flat-filter"; "lenient-transform-flat-contains" ]
        Evidence =
          "JUDGEMENT: total + loss-free on its pinned domain — one binary comparison of a column against "
          + "a param with op eq (flat-filter) or contains (flat-contains). The flat spelling for literal "
          + "right-hands or composed predicates is NOT corpus-pinned, so those keep the full pred form "
          + "(teaching an unpinned shorthand would be a private dialect, §16). Largest per-occurrence "
          + "saving (~90 chars per wired filter step)." }
      { Name = "DateRange value as the bare [from,to] pair"
        Class = TaughtPrimary
        FixtureIds = [ "lenient-daterange-bare-array" ]
        Evidence =
          "JUDGEMENT: total + loss-free — a two-element array at a DateRange value position maps uniquely "
          + "onto {from, to} (pinned cross-host by the fixture)." }
      { Name = "Compact composites (multi-family exemplars)"
        Class = Composite
        FixtureIds =
          [ "lenient-filterable-static-dashboard-compact"
            "lenient-grid-field-named-compact"
            "lenient-grid-transform-param-compact"
            "lenient-master-detail-preselected-compact"
            "lenient-scalar-transform-composition-compact" ]
        Evidence =
          "Whole-tree fixtures composing several taught families (bare columns, schema omission, wired "
          + "filters). Several are pack exemplars already — the shipped dialect the 841 miner was "
          + "constrained to preserve." }
      { Name = "values-only column envelope"
        Class = SafeNotTaught
        FixtureIds = [ "lenient-transform-values-only-columns" ]
        Evidence =
          "Safe (validity restores all-true) but strictly dominated by the bare-array form — an "
          + "intermediate spelling with no reason to teach it." }
      { Name = "Literal-envelope acceptance (bare string IS canonical)"
        Class = AlreadyCanonical
        FixtureIds =
          [ "lenient-bare-text-button-label"
            "lenient-bare-text-callout"
            "lenient-bare-text-heading"
            "lenient-bare-text-markdown" ]
        Evidence =
          "0.2.0 direction-flip (§16 rule 1): the bare string is the canonical TextSource form; the "
          + "leniency accepts the VERBOSE {\"$type\":\"Literal\"} envelope. The terse side is already "
          + "taught as canonical." }
      { Name = "Bound-wrapper unwrap in Binding value positions"
        Class = AlreadyCanonical
        FixtureIds = [ "lenient-binding-bound-wrapper" ]
        Evidence =
          "fuaran#633: {\"$type\":\"Bound\",\"binding\":B} in a Binding slot unwraps to B. The wrapper "
          + "costs MORE tokens than canonical — a safety net for a TextSource-convention carry-over, "
          + "nothing to teach." }
      { Name = "Explicit defaults accepted (canonical omits them)"
        Class = AlreadyCanonical
        FixtureIds =
          [ "lenient-460-explicit-default-column"
            "lenient-460-explicit-default-metric"
            "lenient-460-explicit-default-style"
            "lenient-596-form-explicit-auto-state"
            "lenient-fact-explicit-defaults" ]
        Evidence =
          "Omitted-when-default is the canonical posture on both boundaries (§3.6): the leniency accepts "
          + "the VERBOSE explicit spelling and re-encode drops it. The terse side (omission) is already "
          + "the taught rule — restated in the dialect passage, no transform needed (canonical corpus "
          + "bytes already omit)." }
      { Name = "Static-envelope unwrap at plain-value fields"
        Class = AlreadyCanonical
        FixtureIds = [ "lenient-shape-static-envelope-plain-scalars" ]
        Evidence =
          "The INVERSE confusion (§3.6): models wrap plain fields in Static envelopes; the decoder "
          + "unwraps. Canonical at a plain field is the bare value — already the terse form." }
      { Name = "DateRange value in a Static envelope"
        Class = AlreadyCanonical
        FixtureIds = [ "lenient-daterange-static-envelope" ]
        Evidence = "Same inverse-wrap acceptance at the DateRange position; canonical is the bare {from,to}." }
      { Name = "Enum-value aliases"
        Class = SafeNotTaught
        FixtureIds =
          [ "lenient-460-alias-emphasis-muted"
            "lenient-460-alias-emphasis-strong"
            "lenient-460-alias-tone-danger"
            "lenient-460-alias-tone-positive"
            "lenient-tonedpill-tone-aliases" ]
        Evidence =
          "Total (each alias maps to exactly one canonical case, §3.6 tables) but token-NEUTRAL — "
          + "synonym acceptance for model priors, not compression. Teaching them would displace the "
          + "catalogue's canonical spellings for zero gain." }
      { Name = "Enum-spelling coercion at bool emphasis slots"
        Class = SafeNotTaught
        FixtureIds =
          [ "lenient-022-lvr-emphasis-loud"
            "lenient-022-lvr-emphasis-normal"
            "lenient-emphasis-cross-vocab" ]
        Evidence =
          "Cross-vocabulary re-typing (\"Loud\"→true at a bool slot; true→\"Loud\" at the enum slot): "
          + "total for the accepted spellings but the canonical bool is already the terse form." }
      { Name = "Field-name aliases"
        Class = SafeNotTaught
        FixtureIds =
          [ "lenient-alias-call-url"
            "lenient-alias-card-title-metric-value"
            "lenient-alias-datagrid-data-column-type"
            "lenient-alias-form-field-name"
            "lenient-alias-grid-columns-row"
            "lenient-alias-navigate-href"
            "lenient-alias-select-options-query-deps"
            "lenient-tonedpill-tonemap-alias" ]
        Evidence =
          "Total (same concept, same semantics, §3.6 table; canonical wins when both present) but "
          + "token-neutral or negative — the canonical names are as short or shorter (route vs href, "
          + "cols vs columns, map vs toneMap). Synonym safety net, not compression." }
      { Name = "Pill tag carrying a tone map (→ TonedPill)"
        Class = SafeNotTaught
        FixtureIds = [ "lenient-tonedpill-pill-tag" ]
        Evidence =
          "Total + unambiguous (a closure Pill can never carry a map — Phase 750, prevents silent data "
          + "loss) but saves ~1 token and contradicts the catalogue's case name. Not taught." }
      { Name = "Pipeline step / aggregation aliases"
        Class = SafeNotTaught
        FixtureIds = [ "lenient-transform-step-aliases" ]
        Evidence =
          "Alias spellings (by→keys, aggregations→aggs, avg→mean, descending→dir, count→n): the "
          + "canonical names are mostly SHORTER. Synonym acceptance, not compression." }
      { Name = "Alternate predicate/expression spellings"
        Class = SafeNotTaught
        FixtureIds =
          [ "lenient-transform-expr-spellings"
            "lenient-transform-flat-or"
            "lenient-transform-flat-scalar-fn" ]
        Evidence =
          "Alternate spellings of the expression algebra ($type-as-op eq, or/exprs n-ary form, "
          + "call/fn/predicate spellings). Marginally terser in places, but the flat step above covers "
          + "the dominant case and teaching a second predicate dialect would split model attention for "
          + "single-digit tokens. Accepted, not taught." }
      { Name = "Row-major source transposition"
        Class = NeverTaught
        FixtureIds = [ "lenient-transform-source-rowmajor" ]
        Evidence =
          "JUDGEMENT: heuristic + not order-preserving — transposed with the FIRST row's key set "
          + "(sorted), absent cells null, ragged rows refuse downstream (fuaran#815). Column order is "
          + "not preserved, so normalisation is not loss-free in general; also token-NEGATIVE beyond "
          + "~2 rows (keys repeat per row). Safety net only." }
      { Name = "State/Static/Bound envelope at a Transform source"
        Class = NeverTaught
        FixtureIds = [ "lenient-transform-source-state-rows" ]
        Evidence =
          "Semantics-bearing (live/snapshot distinction, fuaran#815/#818) — the fixture is now an "
          + "identity (the State envelope round-trips as a live source), so this is not an emission "
          + "shorthand at all; teaching it would teach a different meaning." }
      { Name = "Epoch-integer timestamps"
        Class = NeverTaught
        FixtureIds = [ "lenient-transform-epoch-timestamps" ]
        Evidence =
          "JUDGEMENT: heuristic — seconds-vs-milliseconds is resolved by magnitude (the fixture's own "
          + "1752000000 and 1752000000000 normalise to the SAME instant), and the coercion is contextual "
          + "on a schema declaring timestamp. Not provably loss-free; safety net only." }
      { Name = "Legacy window-function spelling"
        Class = NeverTaught
        FixtureIds = [ "lenient-window-cumsum-legacy" ]
        Evidence =
          "cumSum→cumulSum is a superseded-spelling seam — §16's own admission law says backward "
          + "compatibility is NOT an admission ground; teaching it would resurrect a retired spelling." }
      { Name = "Opaque-sentinel recovery"
        Class = NeverTaught
        FixtureIds =
          [ "lenient-665-rows-opaque-sentinel"
            "lenient-opaque-static-markers"
            "lenient-opaque-static-options"
            "lenient-opaque-static-series"
            "lenient-opaque-static-values" ]
        Evidence =
          "\"<opaque>\" is the §5.1 erasure residue of a survivability boundary, not an authoring form — "
          + "an author emitting it would be emitting data loss on purpose." }
      { Name = "null accepted for absence (two positions)"
        Class = NeverTaught
        FixtureIds = [ "lenient-null-static-options" ]
        Evidence =
          "null in a Binding slot is refused in general (ambiguous with absence, §3.6); the two accepted "
          + "positions normalise to empty. Omission is the taught form; emitting null teaches the "
          + "refused shape everywhere else." }
      { Name = "Bare Grid (no cols) → Auto"
        Class = SafeNotTaught
        FixtureIds = [ "lenient-shape-grid-no-cols" ]
        Evidence =
          "Total (accept-and-canonicalise across kinds, the CSS auto-grid prior) but token-neutral: "
          + "emitting {\"$type\":\"Auto\"} costs the same and matches the catalogue." }
      { Name = "Grid templateColumns without cols (cols synthesised)"
        Class = NeverTaught
        FixtureIds = [ "lenient-shape-grid-template-no-cols" ]
        Evidence =
          "Contextual synthesis — the decoder inserts cols:1 beside a templateColumns; loss-free only "
          + "when the intended cols was 1, which the input cannot state. Safety net only." } ]

// ── The taught dialect passage (generated into the dialect variant's prompt) ─────

let private dialectPassage =
    "## The emission dialect — emit the shorthand; the decoder canonicalises\n\
     \n\
     Every conformant decoder accepts a small FIXED set of shorthands and normalises\n\
     each to exactly its canonical form at the decode boundary — hosts and wire\n\
     consumers only ever see canonical bytes, so a shorthand costs nothing downstream.\n\
     In this prompt, EMIT THE SHORTHAND wherever a row below applies; every JSON\n\
     example here is already written in this dialect. The signature catalogue below\n\
     remains the wire contract — every type, field name and enum spelling is\n\
     unchanged; only these encodings shorten:\n\
     \n\
     | Emit | Instead of | Where |\n\
     |---|---|---|\n\
     | `\"value\":1234.5`, `\"options\":[\"A\",\"B\"]` | `{\"$type\":\"Static\",\"value\":…}` | any `Binding` slot holding literal data — a bare scalar or array means `Static`. A bare OBJECT is never a shorthand: objects keep their `$type` envelope |\n\
     | `\"options\":[\"EMEA\",\"APAC\"]` | `[{\"label\":\"EMEA\",\"value\":\"EMEA\"},…]` | an option whose label equals its value is the bare string |\n\
     | `\"amount\":[100,200]` | `{\"values\":[100,200],\"validity\":[true,true]}` | an embedded Transform source column — cells are all-present by construction (the wire has no null) |\n\
     | omit `schema` | `\"schema\":[{\"name\":…,\"type\":…},…]` | an embedded Transform source whose column types are plain string / int / float / bool (they infer). KEEP `schema` for `date` / `timestamp` or an empty column — those never infer |\n\
     | `\"params\":{\"region\":{\"$type\":\"Filter\",\"name\":\"region\"}}` | `\"params\":[{\"name\":\"region\",\"from\":…}]` | `Transform.params` — a name→binding map |\n\
     | `{\"$type\":\"filter\",\"column\":\"region\",\"op\":\"eq\",\"param\":\"region\"}` | `{\"$type\":\"filter\",\"pred\":{\"$type\":\"binary\",\"left\":{\"$type\":\"col\",\"name\":\"region\"},\"op\":\"eq\",\"right\":{\"$type\":\"param\",\"name\":\"region\"}}}` | a pipeline `filter` step comparing ONE column against ONE param with `eq` or `contains`. Any other predicate (a literal comparison, `and`/`or`, a function) keeps the full `pred` form |\n\
     | `\"value\":[\"2026-03-01\",\"2026-03-08\"]` | `\"value\":{\"from\":\"2026-03-01\",\"to\":\"2026-03-08\"}` | a `DateRange` value — the bare `[from,to]` pair |\n\
     \n\
     And, as everywhere: omit every optional field whose value is its default — the\n\
     omitted form is the canonical one. Nothing else has a shorthand. Do not invent\n\
     an abbreviation, an alias, or a shape beyond this table — anything not listed\n\
     here is emitted exactly as the catalogue and the rules state.\n\
     \n"

// ── The generated appendix ───────────────────────────────────────────────────────

let private lenientFixtureFiles: Map<string, string * string> =
    use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter (fun f -> f.GetProperty("kind").GetString() = "lenient-accept")
    |> Seq.map (fun f ->
        f.GetProperty("id").GetString(),
        (f.GetProperty("inputFile").GetString(), f.GetProperty("expectedFile").GetString()))
    |> Map.ofSeq

/// Every manifest lenient-accept id must be claimed by exactly one family — the
/// mechanism that makes the appendix track decoder/corpus changes by construction:
/// a new leniency fixture fails BOTH --write and --check until it is classified.
let private assertLenientPartition () =
    let claimed = leniencyFamilies |> List.collect _.FixtureIds

    match claimed |> List.countBy id |> List.filter (fun (_, n) -> n > 1) with
    | [] -> ()
    | dups -> failwithf "DIALECT-APPENDIX: fixture id(s) claimed twice: %A" (dups |> List.map fst)

    let claimedSet = Set.ofList claimed
    let manifestSet = lenientFixtureFiles |> Map.toSeq |> Seq.map fst |> Set.ofSeq

    let unclaimed = Set.difference manifestSet claimedSet
    let phantom = Set.difference claimedSet manifestSet

    if not (Set.isEmpty unclaimed) then
        failwithf
            "DIALECT-APPENDIX: new lenient-accept fixture(s) with no classification — judge and classify them \
             in leniencyFamilies (a leniency you cannot prove loss-free is never-taught by default): %A"
            (Set.toList unclaimed)

    if not (Set.isEmpty phantom) then
        failwithf "DIALECT-APPENDIX: classified fixture id(s) not in the manifest: %A" (Set.toList phantom)

let private buildDialectAppendix () =
    assertLenientPartition ()

    let sb = StringBuilder()

    let line (s: string) = sb.Append(s).Append '\n' |> ignore

    line "<!--"
    line "  GENERATED FILE (Phase 840) — the leniency-surface classification behind the"
    line "  lenient emission dialect. Produced by docs/tools/authoring-pack.fsx from the"
    line "  wire-format-fixtures manifest's lenient-accept family + the classification"
    line "  table in the generator; drift-checked in the build. Do not hand-edit — edit"
    line "  the generator's `leniencyFamilies` table and rerun `authoring-pack.fsx --write`."
    line "-->"
    line ""
    line "# The lenient-dialect appendix — Phase 840"
    line ""
    line "Classification of the ENTIRE decoder-leniency surface, as pinned by the corpus's"

    line
        $"`lenient-accept` fixture family ({Map.count lenientFixtureFiles} fixtures — `manifest.json` authoritative). Every"

    line "fixture id is claimed by exactly one family below (asserted at generation, so a"
    line "new leniency cannot land unclassified). Classes:"
    line ""
    line "- **taught-primary** — TOTAL and LOSS-FREE and token-positive: the decoder"
    line "  provably normalises the shorthand to exactly the canonical semantics for every"
    line "  legal input, and the shorthand is cheaper. Taught as the primary emission"
    line "  dialect in `prompt-pack-lenient/`; the pack transform applies exactly these,"
    line "  each block decoder-proved (`dialect-verify.fsx`)."
    line "- **safe-not-taught** — total and loss-free, but token-neutral/negative (synonym"
    line "  acceptance for model priors) or catalogue-contradicting. Stays a safety net."
    line "- **already-canonical** — the TERSE side of the pair is the canonical form; the"
    line "  leniency accepts the verbose spelling. Nothing to teach beyond the canonical"
    line "  rule the pack already teaches."
    line "- **never-taught** — partial, heuristic, or contextual: normalisation cannot be"
    line "  proved loss-free for every legal input (unproved ⇒ unsafe by default)."
    line ""
    line "The Δ column is mechanical: minified EXPECTED (canonical) bytes − minified"
    line "INPUT (shorthand) bytes, summed per family (positive = the shorthand side is"
    line "cheaper; 0 = identity or equal size; negative = the fixture's input is the"
    line "VERBOSE side — the accepted-not-preferred direction). It measures the corpus"
    line "pins, not the pack — the pack-level ledger lives in the census."
    line ""
    line "| Family | Class | Δ bytes | Fixtures | Evidence / judgement |"
    line "|---|---|---:|---|---|"

    for fam in leniencyFamilies do
        let delta =
            fam.FixtureIds
            |> List.sumBy (fun id ->
                let input, expected = Map.find id lenientFixtureFiles
                let i = (minifyJson (File.ReadAllText(Path.Combine(fixturesDir, input)))).Length
                let e = (minifyJson (File.ReadAllText(Path.Combine(fixturesDir, expected)))).Length
                e - i)

        let fixtures = fam.FixtureIds |> List.map (sprintf "`%s`") |> String.concat "<br>"
        let deltaText = delta.ToString("+#;-#;0")

        line $"| {fam.Name} | {fam.Class.Label} | {deltaText} | {fixtures} | {fam.Evidence} |"

    line ""
    line "## What the dialect variant does with this"
    line ""
    line "`docs/prompt-pack-lenient/` re-emits every example block and few-shot tree in"
    line "the taught-primary shorthand via a mechanical transform run to a fixpoint (so"
    line "the variant is ONE dialect — no canonical spelling a taught family covers can"
    line "survive in an emitted block), and every transformed block is proved loss-free"
    line "through the real decoder: `encode(decode(dialect)) == encode(decode(canonical))`,"
    line "byte-equal. TreeOp examples stay canonical in both variants: the lenient-accept"
    line "family pins NODE decode only, so no op-position shorthand is corpus-pinned"
    line "cross-host, and §16 forbids teaching an unpinned one. Regenerate with"
    line "`dotnet fsi docs/tools/authoring-pack.fsx --write --dialect lenient` (requires"
    line "the Release build of src/Fuaran.UI.JsonDecode.Tests for the decoder proof)."
    line ""

    sb.ToString()

// ── The dialect pack variant emission ────────────────────────────────────────────

/// Collected (label, canonical, dialect) pairs for the decoder proof.
let private dialectProofPairs = ResizeArray<string * string * string>()

let private genericJsonBlockRegex =
    Regex(@"```json\r?\n(?<body>.*?)\r?\n```", RegexOptions.Singleline)

let private buildDialectSystemPrompt (handVerdicts: Map<string, string> option) =
    let original =
        normalizeEol (File.ReadAllText(Path.Combine(packDir, "system-prompt.md")))

    // 1. Swap the banner: this file is FULLY generated, not generated-adjacent.
    let banner =
        String.concat
            "\n"
            [ "<!--"
              "  FULLY GENERATED FILE (Phase 840) — the LENIENT-DIALECT variant of"
              "  ../prompt-pack/system-prompt.md. Every byte here derives from the canonical"
              "  pack + the wire-format-fixtures corpus: example blocks re-emit in the taught"
              "  emission shorthand (decoder-proved loss-free), and the emission-dialect"
              "  passage is generated from the classification in authoring-pack.fsx. Do not"
              "  hand-edit ANYTHING here — edit the canonical pack and rerun"
              "  `authoring-pack.fsx --write --dialect lenient`."
              "-->" ]

    let body =
        let t = original.TrimStart()

        if t.StartsWith "<!--" then
            // The canonical banner's prose itself names `<!-- fuaran:example -->`
            // markers, so the first inline `-->` is NOT the banner close — the
            // close is the delimiter on its own line.
            match t.IndexOf "\n-->" with
            | i when i >= 0 -> banner + t.Substring(i + 4)
            | _ -> banner + "\n" + t
        else
            banner + "\n" + t

    // 2. Marker blocks: node-decoder fixtures re-emit through the transform; op
    //    fixtures stay canonical (no op-position leniency is corpus-pinned).
    let body =
        markerRegex.Replace(
            body,
            fun (m: Match) ->
                let id = m.Groups.["id"].Value
                let canonical = fixtureRaw id

                let newBody =
                    if (fixtureMeta id).Decoder = "op" then
                        minifyJson canonical
                    else
                        let d = toDialect canonical
                        dialectProofPairs.Add($"example:{id}", minifyJson canonical, d)
                        d

                $"<!-- fuaran:example fixture={id} -->\n```json\n{newBody}\n```\n<!-- /fuaran:example -->"
        )

    // 3. Hand-authored ```json blocks that are complete Node documents re-emit too
    //    (one dialect per variant); fragments and TreeOp documents stay as they are.
    //    Marker bodies are already at the transform's fixpoint, so this pass leaves
    //    them byte-identical and adds no duplicate proof pair. Hand blocks are the
    //    ADVISORY tier of the proof: a block the decoder cannot verify — notably the
    //    deliberately-WRONG teaching examples, which do not decode by design — falls
    //    back to its canonical text instead of failing the emission (collect mode
    //    proposes every candidate; apply mode keeps only the proved ones).
    let mutable handIndex = -1

    let body =
        genericJsonBlockRegex.Replace(
            body,
            fun (m: Match) ->
                let blockBody = m.Groups.["body"].Value

                let isNodeDoc =
                    try
                        use doc = JsonDocument.Parse blockBody
                        let root = doc.RootElement

                        root.ValueKind = JsonValueKind.Object
                        && (match root.TryGetProperty "id" with
                            | true, _ -> true
                            | _ -> false)
                        && (match root.TryGetProperty "kind" with
                            | true, _ -> true
                            | _ -> false)
                    with _ ->
                        false

                if isNodeDoc then
                    let d = toDialect blockBody

                    if d = normalizeEol blockBody then
                        m.Value
                    else
                        handIndex <- handIndex + 1
                        let label = $"hand:{handIndex}"

                        match handVerdicts with
                        | None ->
                            dialectProofPairs.Add(label, minifyJson blockBody, d)
                            $"```json\n{d}\n```"
                        | Some verdicts ->
                            if verdicts.TryFind label = Some "ok" then
                                $"```json\n{d}\n```"
                            else
                                m.Value
                else
                    m.Value
        )

    // 4. Insert the dialect passage after the wire-shape section (shape first, then
    //    the economy). Anchored on the next section's heading prefix; a heading
    //    rename fails loudly rather than silently omitting the passage.
    let anchor = "\n## Containers nest"

    match body.IndexOf anchor with
    | -1 ->
        failwith
            "dialect emission: anchor heading '## Containers nest' not found in the canonical \
             system prompt — update the insertion anchor in authoring-pack.fsx"
    | i -> body.Substring(0, i) + "\n" + dialectPassage + body.Substring(i + 1)

let private buildDialectFewShotJsonl () =
    fewShot
    |> List.map (fun (id, prompt) ->
        let meta = fixtureMeta id
        let promptJson = JsonSerializer.Serialize(prompt, prettyOpts)
        let idJson = JsonSerializer.Serialize(id)

        let tree =
            if meta.Decoder = "op" then
                minifyJson (fixtureRaw id)
            else
                let canonical = fixtureRaw id
                let d = toDialect canonical
                dialectProofPairs.Add($"few-shot:{id}", minifyJson canonical, d)
                d

        $"{{\"prompt\":{promptJson},\"decoder\":\"{meta.Decoder}\",\"fixture\":{idJson},\"tree\":{tree}}}")
    |> String.concat "\n"
    |> fun body -> body + "\n"

/// The decoder proof: spawn dialect-verify.fsx over the collected pairs and
/// return the per-pair verdicts (label → ok | lossy | canonical-fail |
/// dialect-fail). Refuses to run (with the remedy) when the decoder build
/// outputs are absent — an unproved dialect emission must not be writable.
let private runDialectProof () : Map<string, string> =
    let binDir =
        Path.Combine(fuaranDir, "src", "Fuaran.UI.JsonDecode.Tests", "bin", "Release", "net10.0")

    if not (File.Exists(Path.Combine(binDir, "Fuaran.UI.Ops.dll"))) then
        failwith
            "dialect emission requires the decoder proof, and the decoder build outputs are missing — \
             run `dotnet build src/Fuaran.UI.JsonDecode.Tests -c Release` first"

    let tmp = Path.GetTempFileName()

    File.WriteAllLines(
        tmp,
        dialectProofPairs
        |> Seq.map (fun (label, canonical, dialect) ->
            JsonSerializer.Serialize(
                {| label = label
                   canonical = canonical
                   dialect = dialect |}
            ))
    )

    let verifyScript = Path.Combine(scriptDir, "dialect-verify.fsx")

    let psi =
        Diagnostics.ProcessStartInfo(
            "dotnet",
            $"fsi \"{verifyScript}\" \"{tmp}\"",
            WorkingDirectory = scriptDir,
            UseShellExecute = false
        )

    use p = Diagnostics.Process.Start psi
    p.WaitForExit()

    let verdictsPath = tmp + ".verdicts"

    // Exit 0 = all pairs proved; exit 3 = some pair failed, verdicts written (the
    // caller applies the policy: corpus pairs are REQUIRED, hand pairs fall back).
    // Anything else is the verify script itself failing (missing dlls, bad file).
    if (p.ExitCode <> 0 && p.ExitCode <> 3) || not (File.Exists verdictsPath) then
        failwith $"dialect emission REFUSED: dialect-verify did not complete (exit {p.ExitCode}; pairs kept at {tmp})"

    let verdicts =
        File.ReadAllLines verdictsPath
        |> Array.choose (fun line ->
            match line.Split '\t' with
            | [| label; status |] -> Some(label, status)
            | _ -> None)
        |> Map.ofArray

    // Corpus-derived pairs (example blocks + few-shot trees) are the REQUIRED
    // tier: a failure there is a transform defect, never a fallback.
    let requiredFailures =
        verdicts
        |> Map.toList
        |> List.filter (fun (label, status) -> not (label.StartsWith "hand:") && status <> "ok")

    if not requiredFailures.IsEmpty then
        failwith
            $"dialect emission REFUSED: corpus-derived pair(s) failed the loss-free proof \
              (%A{requiredFailures}; pairs kept at {tmp})"

    File.Delete tmp
    File.Delete verdictsPath
    verdicts

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
    use doc = JsonDocument.Parse(fixtureRaw id, deepJson)
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

// ── Per-family compiled packs (Phase 843) ───────────────────────────────────────
//
// The pack as a COMPILATION TARGET over the flip history. One pack serves every
// family, so it carries the union of every family's needs — yet the flip record
// (prompt-pack/SLIMMING-CENSUS.md) records WHICH family each teaching flipped for,
// and the dialect verdicts are already per-family. `--family <id>` emits a variant
// carrying the always-core plus every section that family's flip record does not
// show to be unnecessary, in the dialect that family's own verdict adopted.
//
// THREE DIMENSIONS, each DERIVED and none hand-set:
//   * sections — the generated section-demand index below, which distils the flip
//     record into a per-family (section → needed | never-needed | unknown) map.
//     `unknown` defaults to INCLUDED, the conservative side: a family whose flip
//     record says nothing about a section gets the full pack rather than a guess.
//   * dialect  — the per-family verdict table the flip record already carries
//     (Phase 840, "Per-family verdicts"). ADOPT compiles from the lenient sibling
//     pack; anything else, and any family with no verdict at all, stays canonical.
//   * per-host — an OPTIONAL registered-components section, emitted only when a host
//     registry manifest is supplied. Default OFF, and its output cannot land in the
//     committed variant set (`--out` is mandatory beside it).
//
// SIZING IS NOT A DIMENSION, and that is the amended framing, not an omission. Cache
// posture is a MEASURED input per deployment — the Phase 842 shakedown found a family
// taking deep prefix-cache hits against its own reputation, and a deep-cache family
// whose token fade saved nothing — so a warm posture can afford the RICHER pack while
// a genuinely cold one cannot, and the SAME family can be either. The compiler
// therefore selects TEACHING CONTENT per family and says nothing about which posture
// should run which variant. Which variant a posture is served is the measurement's
// job.

/// The families the flip record and the evaluation harness both name, in the
/// provider-generic spelling shared with the harness's own family ids.
let private packFamilies =
    [ "claude-opus"; "claude-sonnet"; "gemini"; "gpt"; "grok"; "kimi" ]

/// Every spelling the flip record uses for a family, mapped to its family id.
///
/// Matched on WORD BOUNDARIES, so a snapshot or effort suffix (`…-4-8@low`,
/// `…@low`) resolves and an incidental substring does not. Two conventions are
/// worth stating because they are inferences rather than readings:
///
///   * a bare model-line name (a snapshot codename) resolves to its family, because
///     the record names arms by snapshot and sections by family;
///   * a bare vendor name resolves to the arm the record actually ran — the recorded
///     arms are the larger model of that vendor throughout — and the alias that
///     matched is carried into every provenance row, so the inference is visible at
///     the point of use rather than buried here.
let private familyAliases: (string * string) list =
    [ "claude-opus-4-8", "claude-opus"
      "claude-opus", "claude-opus"
      "opus", "claude-opus"
      "claude", "claude-opus"
      "claude-sonnet", "claude-sonnet"
      "sonnet", "claude-sonnet"
      "gpt-5.6-terra", "gpt"
      "gpt-5.6-sol", "gpt"
      "gpt-5.6", "gpt"
      "gpt-4o", "gpt"
      "gpt", "gpt"
      "terra", "gpt"
      "sol", "gpt"
      "gemini", "gemini"
      "grok", "grok"
      "kimi", "kimi" ]

/// Lower-cased, punctuation-flattened text for phrase matching. Backticks, `§`
/// sigils, em-dashes, slashes and colons all become spaces, so the record's several
/// spellings of one heading (`§Metric-vs-Fact`, "Numbers vs text: `Metric` vs
/// `Fact`") normalise to the same words without a hand-written alias table.
let private normaliseForMatch (s: string) =
    let sb = StringBuilder(s.Length)

    for c in s do
        if Char.IsLetterOrDigit c then
            sb.Append(Char.ToLowerInvariant c) |> ignore
        else
            sb.Append ' ' |> ignore

    Regex.Replace(sb.ToString(), @"\s+", " ").Trim()

/// Split pack text into the SAME units a mechanical slice cuts on: the preamble
/// under key `""`, one unit per `## ` section, one per `### ` subsection keyed
/// `<parent>/<child>`.
///
/// Deliberately identical to the harness's own splitter, line for line. The
/// compiler's output is loaded by that splitter downstream, so a unit boundary that
/// disagreed would make an included section and a priced section different bytes.
let private packUnits (text: string) : (string * string) list =
    let lines = text.Replace("\r\n", "\n").Split '\n'
    let acc = ResizeArray<string * StringBuilder>()
    acc.Add("", StringBuilder())
    let mutable parent = ""

    for line in lines do
        if line.StartsWith "## " then
            parent <- line.Substring(3).Trim()
            acc.Add(parent, StringBuilder())
        elif line.StartsWith "### " then
            let child = line.Substring(4).Trim()
            acc.Add((if parent = "" then child else parent + "/" + child), StringBuilder())

        let _, sb = acc[acc.Count - 1]
        sb.Append(line).Append '\n' |> ignore

    acc |> Seq.map (fun (t, sb) -> t, sb.ToString()) |> List.ofSeq

/// Reassemble units into pack text. Every unit is LF-terminated and the split's final
/// element is the empty tail, so dropping ONE trailing LF makes an empty drop set
/// reproduce the source byte for byte — the property the whole variant discipline
/// rests on, since a variant that drops nothing must BE its source.
let private reassembleUnits (units: (string * string) list) =
    let joined = units |> List.map snd |> String.concat ""

    if joined.EndsWith "\n" then
        joined.Substring(0, joined.Length - 1)
    else
        joined

/// The `## ` title of a unit — its own for a `## ` unit, the parent's for a `### `
/// child, `""` for the preamble.
let private unitTitle (key: string) =
    match key.IndexOf '/' with
    | -1 -> key
    | i -> key.Substring(i + 1)

// ── The section-demand index ────────────────────────────────────────────────────
//
// The flip record distilled to a per-family map, GENERATED rather than
// hand-classified. Three mechanical steps, and every classification carries the
// line that justified it:
//
//   1. RESOLVE a record line to a pack section, by longest matching phrase over
//      phrases derived from the live section titles (full title, the head before an
//      em-dash, the tail after one, the text after a colon, and the first two to
//      four words). One line resolves to at most ONE section — the longest match —
//      so a passing mention cannot borrow another section's evidence.
//   2. ATTRIBUTE the line to families, by word-boundary alias match.
//   3. CLASSIFY by marker: a positive marker makes the section `needed` for that
//      family, an extinction marker makes it `never-needed`, and a line with
//      neither says nothing.
//
// NEEDED WINS over never-needed wherever both fire on one (section, family). The
// asymmetry is the conservative direction: a section with any live flip evidence for
// a family is teaching that family has been measured to want, and the cost of
// keeping it is tokens where the cost of cutting it is a regression.

/// Phrases that identify a live pack section in record prose, longest first.
let private sectionPhrases (key: string) : string list =
    let title = unitTitle key

    if title = "" then
        []
    else
        let norm = normaliseForMatch title

        let splitOn (seps: string list) (s: string) =
            seps
            |> List.tryPick (fun sep ->
                match s.IndexOf(sep, StringComparison.Ordinal) with
                | -1 -> None
                | i -> Some(s.Substring(0, i).Trim(), s.Substring(i + sep.Length).Trim()))

        let head, tail =
            match splitOn [ " — "; " – "; " (" ] title with
            | Some(h, t) -> normaliseForMatch h, normaliseForMatch t
            | None -> norm, ""

        let afterColon =
            match splitOn [ ": " ] title with
            | Some(_, t) -> normaliseForMatch t
            | None -> ""

        let stripArticle (s: string) =
            if s.StartsWith "the " then s.Substring 4 else s

        let words = norm.Split ' '

        // A phrase that OPENS on a function word is not distinctive — "the canonical"
        // and "on inputs" occur throughout the record's prose about other sections,
        // and one of them did attribute an unrelated row to the wire-shape section
        // before this guard existed. Distinctiveness is what a resolver needs; length
        // alone is not it.
        let functionWords =
            set
                [ "the"
                  "a"
                  "an"
                  "and"
                  "or"
                  "of"
                  "on"
                  "in"
                  "to"
                  "for"
                  "from"
                  "with"
                  "no"
                  "not"
                  "is"
                  "its"
                  "by"
                  "this"
                  "that"
                  "per"
                  "as"
                  "at" ]

        let distinctive (p: string) =
            match p.Split ' ' with
            | [||] -> false
            | first -> not (functionWords.Contains first[0])

        let lastTwo =
            if words.Length >= 2 then
                [ String.Join(" ", words |> Array.skip (words.Length - 2)) ]
            else
                []

        let openingFour =
            if words.Length >= 4 then
                [ String.Join(" ", words |> Array.take 4) ]
            else
                []

        [ norm; head; tail; stripArticle tail; afterColon ]
        @ (lastTwo @ openingFour |> List.filter distinctive)
        |> List.filter (fun p -> p.Length >= 10)
        |> List.distinct
        |> List.sortByDescending _.Length

/// Word separators removed. The record spells several headings closed-up
/// (`§TonedPill` for "the toned pill", `§Metric-vs-Fact`), and a matcher that only
/// saw word-separated text would read those as no reference at all — which is worse
/// than a false negative, because the section would then look UNATTESTED rather than
/// unmatched.
let private despace (s: string) = s.Replace(" ", "")

/// A record line that fired: which section, which family, which way, and why.
type private DemandEvidence =
    { Section: string
      Family: string
      Needed: bool
      Alias: string
      Marker: string
      Line: int
      Excerpt: string }

/// Markers that read as LIVE teaching for the family named on the same line. Every
/// one is a verdict word the record uses about a section that earned its place.
let private positiveMarkers =
    [ "keep"
      "flip"
      "verified"
      "extinguish"
      "adopt"
      "mechanism"
      "cluster"
      "demand"
      "pinned" ]

/// Markers that read as the teaching's target behaviour being ABSENT on the family
/// named on the same line — the only evidence that licenses cutting a section for
/// one family and not another. Deliberately narrow: "the teaching failed to move
/// this family" is NOT one of them, because a teaching that fails to flip a family
/// is not thereby teaching that family does not need.
let private extinctionMarkers =
    [ "generation-scoped"; "extinct"; "no current family emits"; "unaffected" ]

let private censusPath = Path.Combine(packDir, "SLIMMING-CENSUS.md")

/// The second recorded-flip source: the evaluation harness's capability-demand log,
/// distilled to one record per row that names BOTH a family and a teaching lever.
///
/// It is a GENERATED, committed input rather than a file this script reads across a
/// repo boundary — the harness emits it, this repo carries it, and the division of
/// labour is deliberate: the emitter EXTRACTS (date, cluster, families, text) and
/// nothing else, while every classification decision — which section a record names,
/// and which way it points — is made here, by the same resolver and the same marker
/// lists the flip record goes through. Two evidence sources, one classifier.
let private demandAttributionPath =
    Path.Combine(packDir, "demand-attribution.jsonl")

let private aliasRegexes =
    familyAliases
    |> List.map (fun (alias, family) ->
        alias, family, Regex(@"(?<![A-Za-z0-9])" + Regex.Escape alias + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase))

let private liveSectionKeys =
    packUnits (File.ReadAllText(Path.Combine(packDir, "system-prompt.md")))
    |> List.map fst
    |> List.filter (fun k -> k <> "")

let private phraseIndex = liveSectionKeys |> List.map (fun k -> k, sectionPhrases k)

/// The one section a line of record prose names, or `None`. Longest matching phrase
/// wins, so a passing mention can never borrow a neighbouring section's evidence,
/// and one line contributes to at most one section.
let private resolveSection (text: string) : string option =
    let norm = normaliseForMatch text
    let flat = despace norm

    phraseIndex
    |> List.choose (fun (key, phrases) ->
        phrases
        |> List.tryFind (fun p -> norm.Contains p || (let d = despace p in d.Length >= 9 && flat.Contains d))
        |> Option.map (fun p -> key, p.Length))
    |> function
        | [] -> None
        | hits -> hits |> List.maxBy snd |> fst |> Some

/// Resolve a MARKDOWN TABLE ROW, whose first section-naming cell outranks the rest of
/// the row.
///
/// The flip record's per-section census is a table whose Section column names the row's
/// subject, while its Verdict column routinely cites OTHER sections ("the exact shape
/// §Conditional-rendering below now warns against"). Under a whole-line longest match a
/// citation can outrank the subject, and the row's evidence then lands on the section it
/// merely mentioned. Reading the first cell that resolves fixes that mechanically, and
/// falls through to the whole line for every table whose cells name no section at all.
let private resolveRow (line: string) : string option =
    if not (line.StartsWith "|") then
        resolveSection line
    else
        let cells = line.Split '|'

        match cells |> Array.tryPick resolveSection with
        | Some s -> Some s
        | None -> resolveSection line

let private excerptOf (text: string) =
    let t = text.Trim().Replace("\n", " ")
    if t.Length > 200 then t.Substring(0, 197) + "…" else t

/// Classify one piece of record prose against a family set: which section it names,
/// which way its markers point, and the row that says so.
let private classifyRecordText (source: string) (lineNo: int) (text: string) (families: (string * string) list) =
    match resolveRow text with
    | None -> []
    | Some section ->
        let lower = text.ToLowerInvariant()
        let positive = positiveMarkers |> List.tryFind lower.Contains
        let extinction = extinctionMarkers |> List.tryFind lower.Contains
        let excerpt = $"{source}:{lineNo} {excerptOf text}"

        // One record line, one row per (family, polarity). Several aliases of the same
        // family routinely match one line ("gpt-5.6-sol@low", "terra@low", "gpt-4o"),
        // and six identical rows are not six pieces of evidence. The LONGEST alias is
        // kept, because it is the most specific spelling the record actually used.
        families
        |> List.groupBy snd
        |> List.map (fun (family, aliases) -> aliases |> List.maxBy (fst >> String.length) |> fst, family)
        |> List.collect (fun (alias, family) ->
            [ match positive with
              | Some m ->
                  { Section = section
                    Family = family
                    Needed = true
                    Alias = alias
                    Marker = m
                    Line = lineNo
                    Excerpt = excerpt }
              | None -> ()
              match extinction with
              | Some m ->
                  { Section = section
                    Family = family
                    Needed = false
                    Alias = alias
                    Marker = m
                    Line = lineNo
                    Excerpt = excerpt }
              | None -> () ])

/// Every (section, family, polarity) the two recorded-flip sources assert.
let private demandEvidence: DemandEvidence list =
    let censusRows =
        normalizeEol (File.ReadAllText censusPath)
        |> fun t -> t.Split '\n'
        |> Array.toList
        |> List.mapi (fun i line -> i + 1, line)
        |> List.collect (fun (lineNo, line) ->
            let families =
                aliasRegexes
                |> List.filter (fun (_, _, rx) -> rx.IsMatch line)
                |> List.map (fun (alias, family, _) -> alias, family)

            classifyRecordText "SLIMMING-CENSUS.md" lineNo line families)

    let demandRows =
        if not (File.Exists demandAttributionPath) then
            []
        else
            File.ReadAllLines demandAttributionPath
            |> Array.toList
            |> List.mapi (fun i line -> i + 1, line)
            |> List.collect (fun (lineNo, line) ->
                if String.IsNullOrWhiteSpace line then
                    []
                else
                    use doc = JsonDocument.Parse line
                    let root = doc.RootElement

                    let str name =
                        match root.TryGetProperty(name: string) with
                        | true, v when v.ValueKind = JsonValueKind.String ->
                            defaultArg (Option.ofObj (v.GetString())) ""
                        | _ -> ""

                    let families =
                        match root.TryGetProperty "families" with
                        | true, v when v.ValueKind = JsonValueKind.Array ->
                            v.EnumerateArray()
                            |> Seq.map _.GetString()
                            |> Seq.filter (fun f -> List.contains f packFamilies)
                            |> Seq.map (fun f -> f, f)
                            |> List.ofSeq
                        | _ -> []

                    let text = str "intent" + " " + str "disposition"
                    let date = str "date"
                    let cluster = str "cluster"
                    classifyRecordText $"demand-attribution.jsonl[{date} {cluster}]" lineNo text families)

    censusRows @ demandRows

/// `needed` | `never-needed` | `unknown` for one (section, family), plus the
/// evidence rows that decided it. `needed` wins a conflict.
let private demandClass (section: string) (family: string) : string * DemandEvidence list =
    let rows =
        demandEvidence
        |> List.filter (fun e -> e.Section = section && e.Family = family)

    match rows |> List.filter _.Needed, rows |> List.filter (fun e -> not e.Needed) with
    | [], [] -> "unknown", []
    | [], neg -> "never-needed", neg
    | pos, [] -> "needed", pos
    | pos, neg -> "needed", pos @ neg

// ── Always-core ─────────────────────────────────────────────────────────────────
//
// The sections no variant may drop, whatever the index says. Enumerated in every
// variant manifest rather than left implicit, because a reader of a compiled pack
// must be able to see what was structurally out of the compiler's reach.

/// Sections that are always-core by their ROLE in the contract. A key matched by
/// prefix, so a `### ` child of one rides with its parent.
let private alwaysCoreByRole =
    [ "The canonical wire shape", "wire-shape — the emission contract itself"
      "The node kinds you can emit", "type-surface — the spelling-complete signature catalogue"
      "Rules", "contract — the self-wiring / editable / Call-into rules" ]

/// Why a section is always-core, or `None` if it is not.
let private alwaysCoreReason (section: string) : string option =
    if section = "" then
        Some "preamble — the framing every posture reads"
    else
        match alwaysCoreByRole |> List.tryFind (fun (prefix, _) -> section.StartsWith prefix) with
        | Some(_, why) -> Some why
        | None ->
            // Cross-family flip: teaching two or more families' flip records both
            // name. A per-family cut of one of these would remove teaching another
            // family is measured to need from a pack the two might share.
            let neededBy =
                packFamilies |> List.filter (fun f -> fst (demandClass section f) = "needed")

            if neededBy.Length >= 2 then
                Some($"cross-family-flip — needed by " + String.concat ", " neededBy)
            else
                None

/// The fixture ids the pack's own system-prompt example blocks pin, per section —
/// the load-bearing exemplars, listed in the manifest so "always-core" names the
/// examples it protects and not only the headings.
let private sectionExemplars: Map<string, string list> =
    packUnits (File.ReadAllText(Path.Combine(packDir, "system-prompt.md")))
    |> List.map (fun (key, body) ->
        key,
        markerRegex.Matches body
        |> Seq.map (fun m -> m.Groups.["id"].Value)
        |> Seq.distinct
        |> List.ofSeq)
    |> Map.ofList

// ── The dialect dimension ───────────────────────────────────────────────────────

/// Per-family dialect, read from the flip record's own verdict table (Phase 840,
/// "Per-family verdicts"): ADOPT ⇒ the lenient sibling pack is this family's
/// compilation source. A family the table does not name keeps canonical — the
/// default artifact — because no verdict is not a verdict.
let private dialectVerdicts: Map<string, string * int * string> =
    let lines = normalizeEol (File.ReadAllText censusPath) |> fun t -> t.Split '\n'

    let mutable inTable = false
    let acc = ResizeArray<string * (string * int * string)>()

    for i in 0 .. lines.Length - 1 do
        let line = lines[i]

        if line.StartsWith "## " then
            inTable <- normaliseForMatch line |> fun n -> n.Contains "per family verdicts"

        if inTable && line.StartsWith "|" && line.ToUpperInvariant().Contains "ADOPT" then
            let cell = line.Split '|' |> Array.tryItem 1 |> Option.defaultValue ""

            for _, family, rx in aliasRegexes do
                if rx.IsMatch cell && not (acc |> Seq.exists (fun (f, _) -> f = family)) then
                    let excerpt = line.Trim()

                    acc.Add(
                        family,
                        ("lenient",
                         i + 1,
                         (if excerpt.Length > 200 then
                              excerpt.Substring(0, 197) + "…"
                          else
                              excerpt))
                    )

    Map.ofSeq acc

// ── The optional per-host registered-components dimension ───────────────────────
//
// `Custom` is emittable only for a `(moduleId, componentId)` pair the host has
// registered, and the pack registers none — so the escape hatch is unreachable in
// the shipped artefact BY DESIGN. A probe of the taught form (recorded in the
// evaluation harness's own probe record, 2026-08-15) read the registry as a CLOSED
// ENUMERATION in both families tried: zero foreign pairs, zero diversion under gap
// pressure, zero erosion where a typed kind sufficed, and exact contract fidelity
// from ONE exemplar. That is what licenses this dimension; it does not license a
// many-component registry, a component overlapping a typed kind, or the weaker
// families the probe did not reach — so the section is OFF unless a host asks for
// it, and a host that asks supplies the content.
//
// NOTHING HERE IS HAND-AUTHORED CONTENT. The section is generated from the supplied
// registry manifest: the closed-enumeration framing (the wording the probe measured),
// then one block per component with its props contract and its own exemplar tree,
// minified through the same scanner the pack's example blocks use.

type private HostComponent =
    { ModuleId: string
      ComponentId: string
      Summary: string
      Props: (string * string * string) list
      Notes: string list
      Exemplar: string }

type private HostRegistry =
    { HostId: string
      Components: HostComponent list
      Sha256: string
      Path: string }

let private sha256Of (text: string) =
    use sha = SHA256.Create()

    "sha256:"
    + (sha.ComputeHash(Encoding.UTF8.GetBytes(normalizeEol text))
       |> Array.map (fun b -> b.ToString "x2")
       |> String.concat "")

let private readHostRegistry (path: string) : HostRegistry =
    let raw = File.ReadAllText path
    use doc = JsonDocument.Parse raw
    let root = doc.RootElement

    let str (el: JsonElement) name =
        match el.TryGetProperty(name: string) with
        | true, v when v.ValueKind = JsonValueKind.String -> defaultArg (Option.ofObj (v.GetString())) ""
        | _ -> ""

    match str root "kind" with
    | "fuaran.pack.host-registry/v1" -> ()
    | other -> failwithf "%s: expected kind 'fuaran.pack.host-registry/v1', found '%s'" path other

    let manifestDir = Path.GetDirectoryName(Path.GetFullPath path)

    let components =
        root.GetProperty("components").EnumerateArray()
        |> Seq.map (fun c ->
            let exemplarRel = str c "exemplar"
            let exemplarPath = Path.Combine(manifestDir, exemplarRel)

            if not (File.Exists exemplarPath) then
                failwithf "%s: component exemplar '%s' does not exist" path exemplarRel

            { ModuleId = str c "moduleId"
              ComponentId = str c "componentId"
              Summary = str c "summary"
              Props =
                match c.TryGetProperty "props" with
                | true, ps ->
                    ps.EnumerateArray()
                    |> Seq.map (fun p -> str p "name", str p "type", str p "doc")
                    |> List.ofSeq
                | _ -> []
              Notes =
                match c.TryGetProperty "notes" with
                | true, ns -> ns.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                | _ -> []
              // Parsed here, not merely read: an exemplar that is not JSON would ride
              // into a paid prefix as teaching, which is the one thing a generated
              // surface must make impossible.
              Exemplar = minifyJson ((File.ReadAllText exemplarPath).Trim()) })
        |> List.ofSeq

    if components.IsEmpty then
        failwithf "%s: a host registry with no components teaches nothing — omit --host-manifest instead" path

    { HostId = str root "host"
      Components = components
      Sha256 = sha256Of raw
      Path = path }

let private buildHostSection (registry: HostRegistry) =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append '\n' |> ignore
    let n = registry.Components.Length

    line ""
    line "## Registered components (this host)"
    line ""

    line (
        "`Custom` is emittable only for a `(moduleId, componentId)` pair the host has registered. "
        + "This section is that registry in full: this host registers exactly "
        + (if n = 1 then "ONE pair" else $"{n} pairs")
        + ". Any other pair does not exist here."
    )

    for c in registry.Components do
        line ""
        line $"### `{c.ModuleId}` / `{c.ComponentId}`"
        line ""
        line c.Summary
        line ""

        if c.Props.IsEmpty then
            line "Props — this component accepts none."
        else
            line "Props — all are required, and no other prop is accepted:"
            line ""

            for name, kind, doc in c.Props do
                line $"- `{name}` ({kind}) — {doc}"

        for note in c.Notes do
            line ""
            line note

        line ""
        line "Exemplar emission:"
        line ""
        line "```json"
        line c.Exemplar
        line "```"

    sb.ToString()

// ── The index artefact ──────────────────────────────────────────────────────────

let private jstr (s: string) = JsonSerializer.Serialize s

let private polarityWord (needed: bool) =
    if needed then "needed" else "never-needed"

let private decisionWord (keep: bool) = if keep then "included" else "excluded"

let private jbool (b: bool) = if b then "true" else "false"

let private jstrOrNull (s: string option) =
    match s with
    | Some v -> jstr v
    | None -> "null"

let private buildSectionDemandIndex () =
    let systemPromptText = File.ReadAllText(Path.Combine(packDir, "system-prompt.md"))
    let keys = packUnits systemPromptText |> List.map fst
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append '\n' |> ignore

    line "{"
    line "  \"kind\": \"fuaran.pack.section-demand-index/v1\","
    line "  \"generatedBy\": \"docs/tools/authoring-pack.fsx --write\","

    line
        "  \"what\": \"Per-family (section -> needed | never-needed | unknown) map, distilled from the flip record. `unknown` defaults to INCLUDED; `needed` wins a conflict with `never-needed`. Every verdict carries the record line that decided it.\","

    line
        $"  \"flipRecord\": {{ \"file\": \"SLIMMING-CENSUS.md\", \"sha256\": {jstr (sha256Of (File.ReadAllText censusPath))} }},"

    line (
        if File.Exists demandAttributionPath then
            let records =
                File.ReadAllLines demandAttributionPath
                |> Array.filter (String.IsNullOrWhiteSpace >> not)
                |> Array.length

            $"  \"demandAttribution\": {{ \"file\": \"demand-attribution.jsonl\", \"sha256\": {jstr (sha256Of (File.ReadAllText demandAttributionPath))}, \"records\": {records} }},"
        else
            "  \"demandAttribution\": null,"
    )

    line
        $"  \"pack\": {{ \"file\": \"system-prompt.md\", \"sha256\": {jstr (sha256Of systemPromptText)}, \"sections\": {keys.Length} }},"

    line $"""  "families": [{packFamilies |> List.map jstr |> String.concat ", "}],"""
    line "  \"dialect\": {"

    packFamilies
    |> List.map (fun f ->
        match Map.tryFind f dialectVerdicts with
        | Some(d, ln, ex) ->
            $"    {jstr f}: {{ \"dialect\": {jstr d}, \"evidence\": {{ \"line\": {ln}, \"excerpt\": {jstr ex} }} }}"
        | None -> $"    {jstr f}: {{ \"dialect\": \"canonical\", \"evidence\": null }}")
    |> String.concat ",\n"
    |> line

    line "  },"
    line "  \"sections\": ["

    keys
    |> List.map (fun key ->
        let core = alwaysCoreReason key

        let verdicts =
            packFamilies
            |> List.map (fun f ->
                let cls, ev = demandClass key f

                let evJson =
                    ev
                    |> List.map (fun e ->
                        $"{{ \"line\": {e.Line}, \"alias\": {jstr e.Alias}, \"marker\": {jstr e.Marker}, \"polarity\": {jstr (polarityWord e.Needed)}, \"excerpt\": {jstr e.Excerpt} }}")
                    |> String.concat ", "

                $"        {jstr f}: {{ \"class\": {jstr cls}, \"evidence\": [{evJson}] }}")
            |> String.concat ",\n"

        let exemplars =
            Map.tryFind key sectionExemplars
            |> Option.defaultValue []
            |> List.map jstr
            |> String.concat ", "

        String.concat
            "\n"
            [ "    {"
              $"      \"key\": {jstr key},"
              $"      \"alwaysCore\": {jbool core.IsSome},"
              $"      \"alwaysCoreReason\": {jstrOrNull core},"
              $"      \"pinnedExemplars\": [{exemplars}],"
              "      \"families\": {"
              verdicts
              "      }"
              "    }" ])
    |> String.concat ",\n"
    |> line

    line "  ],"
    line "  \"summary\": {"

    packFamilies
    |> List.map (fun f ->
        let classOf k = fst (demandClass k f)
        let needed = keys |> List.filter (fun k -> classOf k = "needed") |> List.length
        let never = keys |> List.filter (fun k -> classOf k = "never-needed") |> List.length

        let cuttable =
            keys
            |> List.filter (fun k -> classOf k = "never-needed" && (alwaysCoreReason k).IsNone)
            |> List.length

        $"    {jstr f}: {{ \"needed\": {needed}, \"neverNeeded\": {never}, \"unknownDefaultedIn\": {keys.Length - needed - never}, \"cuttable\": {cuttable} }}")
    |> String.concat ",\n"
    |> line

    line "  }"
    line "}"
    sb.ToString()

// ── The compiler ────────────────────────────────────────────────────────────────

let private variantsRoot = Path.Combine(docsDir, "prompt-pack-variants")

/// Compile one family's variant into `outDir`, reconciling each artefact through the
/// shared drift/write path so a `--check` reports exactly what a `--write` would fix.
let private compileFamily (family: string) (outDir: string) (host: HostRegistry option) =
    let dialect, dialectWhy =
        match Map.tryFind family dialectVerdicts with
        | Some(d, ln, ex) -> d, $"flip record line {ln}: {ex}"
        | None -> "canonical", "no per-family dialect verdict on record — canonical is the default artifact"

    let sourceDir, sourceRel =
        if dialect = "lenient" then
            dialectPackDir, "docs/prompt-pack-lenient"
        else
            packDir, "docs/prompt-pack"

    let sourcePromptPath = Path.Combine(sourceDir, "system-prompt.md")
    let sourceFewShotPath = Path.Combine(sourceDir, "few-shot.jsonl")

    if not (File.Exists sourcePromptPath) then
        failwithf
            "family '%s' compiles from %s, which has no system-prompt.md — regenerate it first (authoring-pack.fsx --write --dialect lenient)"
            family
            sourceRel

    let sourcePrompt = normalizeEol (File.ReadAllText sourcePromptPath)
    let sourceFewShot = normalizeEol (File.ReadAllText sourceFewShotPath)
    let units = packUnits sourcePrompt

    // A unit the index does not know (the dialect pack's own generated passage) is
    // `unknown`, and `unknown` is included — so a new section can never be dropped
    // by an index that has not seen it.
    let decisions =
        units
        |> List.map (fun (key, body) ->
            let cls, evidence = demandClass key family
            let core = alwaysCoreReason key

            let keep, why =
                match cls, core with
                | "never-needed", Some r -> true, $"always-core ({r}) — outranks the family's never-needed verdict"
                | "never-needed", None -> false, "never-needed on this family's flip record"
                | "needed", _ -> true, "needed on this family's flip record"
                | _, Some r -> true, $"always-core ({r})"
                | _, None -> true, "unknown on this family's flip record — included, the conservative default"

            key, body, cls, core, keep, why, evidence)

    let kept = decisions |> List.filter (fun (_, _, _, _, k, _, _) -> k)

    let hostSection =
        match host with
        | Some r -> buildHostSection r
        | None -> ""

    let systemPrompt =
        reassembleUnits (kept |> List.map (fun (k, b, _, _, _, _, _) -> k, b))
        + hostSection

    let variantSha = sha256Of (systemPrompt + "\n" + sourceFewShot)

    let manifest =
        let sb = StringBuilder()
        let line (s: string) = sb.Append(s).Append '\n' |> ignore

        let coreSections =
            decisions
            |> List.choose (fun (k, _, _, core, _, _, _) ->
                core
                |> Option.map (fun r ->
                    let ex =
                        Map.tryFind k sectionExemplars
                        |> Option.defaultValue []
                        |> List.map jstr
                        |> String.concat ", "

                    $"      {{ \"key\": {jstr k}, \"because\": {jstr r}, \"pinnedExemplars\": [{ex}] }}"))

        let included = kept.Length
        let excluded = decisions.Length - included

        let unknownDefaulted =
            decisions
            |> List.filter (fun (_, _, c, _, k, _, _) -> k && c = "unknown")
            |> List.length

        line "{"
        line "  \"kind\": \"fuaran.pack.variant/v1\","
        line $"  \"family\": {jstr family},"
        line "  \"wireFormatVersion\": \"v1\","

        let hostArgs =
            if host.IsSome then
                " --host-manifest <path> --out <dir>"
            else
                ""

        line $"  \"generatedBy\": \"docs/tools/authoring-pack.fsx --write --family {family}{hostArgs}\","

        line "  \"artifacts\": { \"systemPrompt\": \"system-prompt.md\", \"fewShot\": \"few-shot.jsonl\" },"

        line "  \"schema\": \"../../prompt-pack/schema.json\","

        line
            "  \"schemaNote\": \"The wire schema is the CONTRACT, not a compiled dimension — every variant shares the canonical artefact rather than carrying a copy.\","

        line $"  \"variantSha256\": {jstr variantSha},"

        line
            $"  \"bytes\": {{ \"systemPrompt\": {Encoding.UTF8.GetByteCount systemPrompt}, \"fewShot\": {Encoding.UTF8.GetByteCount sourceFewShot}, \"total\": {Encoding.UTF8.GetByteCount systemPrompt
                                                                                                                                                                + Encoding.UTF8.GetByteCount sourceFewShot} }},"

        line "  \"source\": {"
        line $"    \"pack\": {jstr sourceRel},"
        line $"    \"systemPromptSha256\": {jstr (sha256Of sourcePrompt)},"
        line $"    \"fewShotSha256\": {jstr (sha256Of sourceFewShot)}"
        line "  },"
        line "  \"index\": {"
        line "    \"file\": \"../../tools/section-demand-index.json\","
        line $"    \"sha256\": {jstr (sha256Of (buildSectionDemandIndex ()))}"
        line "  },"
        line "  \"dimensions\": {"
        line $"    \"dialect\": {{ \"value\": {jstr dialect}, \"why\": {jstr dialectWhy} }},"

        line
            $"    \"sections\": {{ \"included\": {included}, \"excluded\": {excluded}, \"unknownDefaultedIn\": {unknownDefaulted} }},"

        line (
            match host with
            | None -> "    \"perHost\": { \"enabled\": false, \"why\": \"default OFF — no --host-manifest supplied\" }"
            | Some r ->
                $"    \"perHost\": {{ \"enabled\": true, \"host\": {jstr r.HostId}, \"components\": {r.Components.Length}, \"registrySha256\": {jstr r.Sha256} }}"
        )

        line "  },"
        line "  \"alwaysCore\": {"

        line
            "    \"rule\": \"The wire shape, the signature catalogue, the Rules, the preamble, and every section two or more families' flip records both name. Enumerated, never implicit — a compiled pack must show what was out of the compiler's reach.\","

        line "    \"sections\": ["
        line (String.concat ",\n" coreSections)
        line "    ]"
        line "  },"
        line "  \"sections\": ["

        decisions
        |> List.map (fun (key, _, cls, core, keep, why, evidence) ->
            let evJson =
                evidence
                |> List.map (fun e ->
                    $"{{ \"line\": {e.Line}, \"alias\": {jstr e.Alias}, \"marker\": {jstr e.Marker}, \"polarity\": {jstr (polarityWord e.Needed)} }}")
                |> String.concat ", "

            String.concat
                "\n"
                [ "    {"
                  $"      \"key\": {jstr key},"
                  $"      \"decision\": {jstr (decisionWord keep)},"
                  $"      \"class\": {jstr cls},"
                  $"      \"alwaysCore\": {jbool core.IsSome},"
                  $"      \"why\": {jstr why},"
                  $"      \"evidence\": [{evJson}]"
                  "    }" ])
        |> String.concat ",\n"
        |> line

        line "  ]"
        line "}"
        sb.ToString()

    // The reported label is the artefact's own directory, so a `--out` run reports the
    // path it actually wrote rather than the committed one it deliberately did not.
    let label (name: string) =
        Path.Combine(Path.GetFileName outDir, name)

    reconcileFile (label "system-prompt.md") (Path.Combine(outDir, "system-prompt.md")) systemPrompt
    reconcileFile (label "few-shot.jsonl") (Path.Combine(outDir, "few-shot.jsonl")) sourceFewShot
    reconcileFile (label "manifest.json") (Path.Combine(outDir, "manifest.json")) manifest

    printfn
        "  %-14s dialect=%-9s sections %d/%d kept  %d B  %s"
        family
        dialect
        kept.Length
        decisions.Length
        (Encoding.UTF8.GetByteCount systemPrompt
         + Encoding.UTF8.GetByteCount sourceFewShot)
        variantSha

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

// ── Pack-manifest language stamp ─────────────────────────────────────────────────
// The committed pack records which language version it was regenerated against, as a
// `languageVersion` field in prompt-pack/manifest.json — the one honest evidence the
// downstream version-freshness pre-flight can read (a stamp written at regeneration
// time, never inferred from mtimes or taught vocabulary). Read the way that checker
// reads the producer side: the FIRST <Version> element in Directory.Build.props wins
// (XML-parsed, so version strings inside historical bump commentary cannot match).
let languageVersion =
    let propsPath = Path.Combine(fuaranDir, "Directory.Build.props")
    let doc = XDocument.Load propsPath

    match doc.Descendants(XName.Get "Version") |> Seq.tryHead with
    | Some v -> v.Value.Trim()
    | None -> failwithf "%s declares no <Version> — cannot stamp the pack manifest" propsPath

/// The manifest with its `languageVersion` stamped to the repo's current <Version>.
/// A targeted text rewrite, not a re-serialisation: every other byte of the manifest
/// stays exactly as authored (the minifier's scanner argument, applied to one field).
let buildStampedPackManifest () =
    let raw = normalizeEol (File.ReadAllText(Path.Combine(packDir, "manifest.json")))
    use doc = JsonDocument.Parse raw // parse first: never rewrite bytes we could not read
    ignore doc
    let field = $"\"languageVersion\": \"{languageVersion}\""

    if Regex.IsMatch(raw, "\"languageVersion\"\\s*:") then
        Regex.Replace(raw, "\"languageVersion\"\\s*:\\s*\"[^\"]*\"", field)
    else
        // First stamp: anchor directly under the wireFormatVersion line, so the two
        // version facts the manifest carries sit together.
        let anchor = Regex.Match(raw, "\"wireFormatVersion\"\\s*:\\s*\"[^\"]*\",\\n")

        if not anchor.Success then
            failwith "prompt-pack/manifest.json: no wireFormatVersion line to anchor the languageVersion stamp"

        raw.Insert(anchor.Index + anchor.Length, $"  {field},\n")

printfn
    "Fuaran authoring pack — %s mode%s%s%s"
    (if writeMode then "write" else "check")
    (if minifyExamples then " (minified pack examples)" else "")
    (if dialectMode then " (lenient dialect variant)" else "")
    (match familyArg with
     | Some f -> $" (per-family compiled pack: {f})"
     | None -> "")

if familyMode then
    // ── Per-family compiled packs ONLY (Phase 843). Like the dialect run, this is
    // its own artefact set: a family run never touches the canonical pack, and a
    // bare --write never touches a variant.
    let families =
        match familyArg with
        | Some "all" -> packFamilies
        | Some f when List.contains f packFamilies -> [ f ]
        | Some f ->
            eprintfn "unknown family '%s' — known: %s" f (String.concat ", " packFamilies)
            usage ()
        | None -> []

    let host = hostManifestArg |> Option.map readHostRegistry

    match host with
    | Some r -> printfn "  host registry %s — %d component(s), %s" r.HostId r.Components.Length r.Sha256
    | None -> ()

    for family in families do
        let outDir =
            match outArg with
            | Some root -> Path.Combine(root, family)
            | None -> Path.Combine(variantsRoot, family)

        compileFamily family outDir host
elif dialectMode then
    // ── Dialect surfaces ONLY (Phase 840). The canonical pack is a different
    // invocation's artefact set; neither run can touch the other's files.
    //
    // Two-phase: COLLECT proposes every candidate transform and gathers the proof
    // pairs; the decoder proof then gates them (corpus pairs required, hand-block
    // pairs advisory); APPLY rebuilds keeping only what was proved. The proof
    // gates BOTH --write and --check: an unproved emission is neither written nor
    // passed. (Purity is by construction — the transform runs to a fixpoint, so
    // every emitted block is invariant under its own transform.)
    buildDialectSystemPrompt None |> ignore
    let dialectFewShot = buildDialectFewShotJsonl ()
    let verdicts = runDialectProof ()
    let dialectSystemPrompt = buildDialectSystemPrompt (Some verdicts)

    reconcileFile
        (Path.Combine("prompt-pack-lenient", "system-prompt.md"))
        (Path.Combine(dialectPackDir, "system-prompt.md"))
        dialectSystemPrompt

    reconcileFile
        (Path.Combine("prompt-pack-lenient", "few-shot.jsonl"))
        (Path.Combine(dialectPackDir, "few-shot.jsonl"))
        dialectFewShot

    // The wire schema is the CONTRACT, not the dialect — a byte copy of the same
    // canonical artefact the default pack ships (the harness's constrained-decode
    // arm binds the canonical schema whichever pack variant is pinned).
    reconcileFile
        (Path.Combine("prompt-pack-lenient", "schema.json"))
        (Path.Combine(dialectPackDir, "schema.json"))
        (File.ReadAllText schemaSrcPath)
else
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

    // 3b. Pack manifest — the languageVersion stamp. Reconciled by the DEFAULT run so
    //     the every-commit drift gate pins it: a <Version> bump without a pack regen
    //     fails --check rather than leaving the committed pack claiming a language it
    //     no longer derives from.
    reconcileFile
        (Path.Combine("prompt-pack", "manifest.json"))
        (Path.Combine(packDir, "manifest.json"))
        (buildStampedPackManifest ())

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

    // 5. The leniency-surface classification appendix (Phase 840). Deliberately
    //    reconciled by the DEFAULT (build-free) run: its derivation is pure —
    //    manifest + fixture bytes + the classification table — so the every-commit
    //    drift gate pins it, and a new lenient-accept fixture fails the gate until
    //    classified (see assertLenientPartition).
    reconcileFile
        (Path.Combine("prompt-pack", "DIALECT-APPENDIX.md"))
        (Path.Combine(packDir, "DIALECT-APPENDIX.md"))
        (buildDialectAppendix ())

    // 6. The section-demand index (Phase 843). Tooling output on the coverage-matrix
    //    precedent — it is the compiler's INPUT evidence, not pack content, so it
    //    lives beside the generator. Reconciled by the DEFAULT run because its two
    //    inputs (the flip record and the live section list) both live in this repo:
    //    editing the census without regenerating the index would leave the compiler
    //    reading a map of a pack that no longer exists, silently.
    reconcileFile
        (Path.Combine("tools", "section-demand-index.json"))
        (Path.Combine(docsDir, "tools", "section-demand-index.json"))
        (buildSectionDemandIndex ())

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

    eprintfn
        "Regenerate with: dotnet fsi docs/tools/authoring-pack.fsx --write%s%s"
        (if dialectMode then " --dialect lenient" else "")
        (match familyArg with
         | Some f -> $" --family {f}"
         | None -> "")

    exit 1
