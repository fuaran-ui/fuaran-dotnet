module Fuaran.UI.JsonDecode.Tests.SpecProjection

// ============================================================================
//  WIRE_FORMAT.md mechanical tables as MARKER-BLOCK PROJECTIONS.
//
//  The specification's prose — admission laws, rationale, decisions — is
//  authored. Its MECHANICAL surfaces are not: the kind→category table, the
//  bare-string enum vocabularies, the identity-default table, the wire-omitted
//  node fields, and every in-prose fixture count are all restatements of data
//  that already exists in a generated artefact, so a hand-maintained copy can
//  only ever be right by accident. Every one of them was measurably wrong when
//  this module was written: `Icon`/`Mount`/`Switch` were absent from a table
//  claiming to be exhaustive, six omit-at-default fields were missing from a
//  table claiming to enumerate them, twenty-one closed enums were unlisted, and
//  the fixture counts (70/21/30) were less than half the corpus.
//
//  Two sources, and only two:
//    * `idl.json`   — the canonical vocabulary artefact (Phase 696): kinds with
//                     their recovered category, closed enums, records, unions,
//                     the node envelope, and the optionality class of every
//                     field INCLUDING its omit-at-default value. This is the
//                     structural source; `schema.json` beside it is the
//                     validation surface and cannot express any of the above.
//    * `manifest.json` — the fixture enumeration. Counts drift; the manifest
//                     cannot.
//
//  GENERATION MAY NOT ADD INFORMATION THE SOURCES DO NOT HAVE. Where the spec
//  needs to say something the IDL cannot know — that `Image.src` is routed
//  through the §19 URL-scheme floor at render time, that `HashStrictness` lives
//  inside `Custom.contentHash` — that sentence is a HAND ANNOTATION, keyed by
//  the vocabulary name it describes, carried in `spec-annotations.json` beside
//  the spec. The generator emits it in a dedicated column and REFUSES to run
//  when a key no longer names anything: an annotation whose subject has been
//  renamed or retired is a stale claim in a normative document, and the whole
//  point of a projection is that such a thing cannot survive a regeneration.
//
//  Marker-block contract (the `authoring-pack.fsx` pattern, §3 of that script):
//
//      <!-- fuaran:spec-kinds -->
//      …generated table…
//      <!-- /fuaran:spec-kinds -->
//
//  and, for a count that sits mid-sentence, the inline form:
//
//      …the <!-- fuaran:count kind=reject -->60<!-- /fuaran:count --> reject
//      fixtures…
//
//  `write` rewrites every block from the sources; `check` reports drift and
//  exits non-zero. Both are reachable from the test CLI (`--project-spec` /
//  `--check-spec`) and `check` also runs as an ordinary test in this assembly,
//  so the repo gate and the cross-host conformance workflow both catch a hand
//  edit inside a managed block.
//
//  NOTE — this module reads and writes ONLY `WIRE_FORMAT.md`. It never touches
//  a fixture, `manifest.json`, or `idl.json`. That is deliberate: the corpus
//  emitter deletes and rewrites the payload directories, so a spec projection
//  that went through it would put fixture churn (and, today, fixture LOSS) into
//  a change-set that is about prose.
// ============================================================================

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

[<Literal>]
let specFileName = "WIRE_FORMAT.md"

[<Literal>]
let annotationsFileName = "spec-annotations.json"

// ─── Failure ────────────────────────────────────────────────────────────────

/// A structural defect in the projection inputs — an orphaned annotation key, a
/// missing marker block, an unknown count selector. Distinct from DRIFT: drift
/// is what `--write` fixes, a defect is what it refuses to paper over.
exception ProjectionDefect of messages: string list

let private defect (messages: string list) = raise (ProjectionDefect messages)

// ─── JSON helpers ───────────────────────────────────────────────────────────

let private prop (name: string) (el: JsonElement) : JsonElement option =
    match el.TryGetProperty name with
    | true, v -> Some v
    | _ -> None

let private str (name: string) (el: JsonElement) : string =
    match prop name el with
    | Some v ->
        match v.GetString() with
        | null -> failwithf "idl.json: property '%s' is null" name
        | s -> s
    | None -> failwithf "idl.json: missing property '%s'" name

let private arr (name: string) (el: JsonElement) : JsonElement list =
    match prop name el with
    | Some v when v.ValueKind = JsonValueKind.Array -> v.EnumerateArray() |> List.ofSeq
    | _ -> failwithf "idl.json: missing or non-array property '%s'" name

let private ordinal (a: string) (b: string) = String.CompareOrdinal(a, b)

/// Append a line terminated by a LITERAL LF. `StringBuilder.AppendLine` uses
/// `Environment.NewLine`, which is CRLF on Windows — the same defect the corpus
/// manifest writer carries a comment about. The corpus `.gitattributes` pins
/// `eol=lf` so git would normalise it on commit and `git status` would stay
/// clean, while every consumer that byte-compares the WORKING TREE saw drift it
/// could not clear.
let private line (sb: StringBuilder) (text: string) : unit = sb.Append(text).Append '\n' |> ignore

// ─── The IDL type + optionality renderers ───────────────────────────────────

/// Render an IDL type as the short spec-table form. The closed set of shapes is
/// `idl.json`'s own (`bool` / `enum` / `float` / `fn` / `hosted` / `int` /
/// `json` / `kind` / `list` / `map` / `node` / `op` / `record` / `str` /
/// `union` / `var`); an unrecognised one is a hard failure rather than a
/// fallback rendering, because a silently-degraded type in a normative table is
/// worse than a build break.
let rec renderType (t: JsonElement) : string =
    match str "$type" t with
    | "bool" -> "bool"
    | "int" -> "int"
    | "float" -> "float"
    | "str" -> "string"
    | "json" -> "json"
    | "node" -> "Node"
    | "op" -> "TreeOp"
    | "kind" -> "NodeKind"
    | "enum"
    | "record" -> str "name" t
    | "var" -> "'" + str "name" t
    | "list" ->
        match prop "of" t with
        | Some inner -> renderType inner + "[]"
        | None -> failwith "idl.json: list type with no 'of'"
    | "map" ->
        match prop "values" t with
        | Some inner -> "{ string: " + renderType inner + " }"
        | None -> failwith "idl.json: map type with no 'values'"
    | "union" ->
        let name = str "name" t

        match prop "args" t with
        | Some a when a.ValueKind = JsonValueKind.Array && a.GetArrayLength() > 0 ->
            let args = a.EnumerateArray() |> Seq.map renderType |> String.concat ", "
            name + "<" + args + ">"
        | _ -> name
    // A host-language slot. `wire` states the wire form the IDL fixes for it
    // (`"<closure>"`); the host-language spellings under `hostSurface` are
    // explicitly NOT wire spec (Phase 696) and never reach a wire table.
    | "fn"
    | "hosted" -> str "wire" t
    | other -> failwithf "idl.json: unrecognised type shape '%s'" other

/// Render an omit-at-default VALUE as it reads in the spec.
let renderDefault (d: JsonElement) : string =
    match str "$type" d with
    | "bool" ->
        match prop "value" d with
        | Some v when v.ValueKind = JsonValueKind.True -> "true"
        | Some v when v.ValueKind = JsonValueKind.False -> "false"
        | _ -> failwith "idl.json: bool default with no boolean 'value'"
    | "enum" -> str "case" d
    | "union" -> str "tag" d
    // Phase 1080 — the empty list. The only list default the IDL admits is the
    // EMPTY one (a non-empty list default would be content, not an identity), so
    // the spelling is `[]` and a populated `items` array is an IDL defect rather
    // than a value to render.
    | "list" ->
        match prop "items" d with
        | Some items when items.ValueKind = JsonValueKind.Array && items.GetArrayLength() = 0 -> "[]"
        | _ -> failwith "idl.json: list default with a non-empty 'items' — only the empty list is an identity default"
    | other -> failwithf "idl.json: unrecognised default shape '%s'" other

/// The compact field spelling used in the §3.2 table:
///   `name`      required
///   `name?`     optional
///   `name?=X`   omitted-when-default X (§3.6 carries the full table)
///   `name*`     host-only — see §4 / §9; carried as the wire form the IDL fixes
let renderFieldToken (f: JsonElement) : string =
    let name = str "name" f

    let optionality =
        match prop "optionality" f with
        | Some o -> o
        | None -> failwithf "idl.json: field '%s' has no optionality" name

    match str "$type" optionality with
    | "required" -> name
    | "optional" -> name + "?"
    | "omitDefault" ->
        match prop "default" optionality with
        | Some d -> name + "?=" + renderDefault d
        | None -> failwithf "idl.json: field '%s' is omitDefault with no default" name
    | "hostOnly" -> name + "*"
    | other -> failwithf "idl.json: field '%s' has unrecognised optionality '%s'" name other

// ─── Annotations ────────────────────────────────────────────────────────────

/// The hand-authored notes the IDL cannot know, keyed by vocabulary name.
/// One map per managed block; a key that names nothing in its block's generated
/// subject set is a defect (see the module doc).
type Annotations =
    { Kinds: Map<string, string>
      Enums: Map<string, string>
      OmitDefaults: Map<string, string>
      NodeFields: Map<string, string> }

let private emptyAnnotations =
    { Kinds = Map.empty
      Enums = Map.empty
      OmitDefaults = Map.empty
      NodeFields = Map.empty }

let private readMap (root: JsonElement) (name: string) : Map<string, string> =
    match prop name root with
    | Some v when v.ValueKind = JsonValueKind.Object ->
        v.EnumerateObject()
        |> Seq.map (fun p ->
            match p.Value.GetString() with
            | null -> failwithf "%s: annotation '%s' is null" annotationsFileName p.Name
            | s -> p.Name, s)
        |> Map.ofSeq
    | Some _ -> failwithf "%s: '%s' is not an object" annotationsFileName name
    | None -> Map.empty

let loadAnnotations (corpusDir: string) : Annotations =
    let path = Path.Combine(corpusDir, annotationsFileName)

    if not (File.Exists path) then
        emptyAnnotations
    else
        use doc = JsonDocument.Parse(File.ReadAllText path)

        let ann =
            match prop "annotations" doc.RootElement with
            | Some a -> a
            | None -> failwithf "%s: no 'annotations' object" annotationsFileName

        { Kinds = readMap ann "kinds"
          Enums = readMap ann "enums"
          OmitDefaults = readMap ann "omitDefaults"
          NodeFields = readMap ann "nodeFields" }

/// Every annotation key must name a subject the generated block actually
/// carries. Collected rather than thrown one at a time, so a rename sweep sees
/// its whole blast radius in one run.
let private orphanKeys (block: string) (known: string Set) (notes: Map<string, string>) : string list =
    notes
    |> Map.toList
    |> List.map fst
    |> List.filter (fun k -> not (Set.contains k known))
    |> List.sortWith ordinal
    |> List.map (fun k ->
        sprintf
            "%s: annotation '%s.%s' names nothing in the generated block — the subject was renamed or retired. Update or remove the annotation in %s."
            annotationsFileName
            block
            k
            annotationsFileName)

let private note (notes: Map<string, string>) (key: string) =
    match Map.tryFind key notes with
    | Some n -> n
    | None -> ""

// ─── Block bodies ───────────────────────────────────────────────────────────

/// Markdown table-cell escaping: a literal `|` would end the cell.
let private cell (s: string) = s.Replace("|", "\\|")

/// The order the four behavioural categories are introduced in §3.2's prose,
/// with `Meta` (the structural cases) last. A category the IDL grows that this
/// list does not name is appended in ordinal order rather than dropped — a
/// vocabulary addition must never be able to vanish from an exhaustive table.
let private categoryOrder =
    [ "Layout"; "Display"; "Input"; "Visualisation"; "Meta" ]

let private kindsBlock (idl: JsonElement) (ann: Annotations) : string * string Set =
    let kinds = arr "kinds" idl

    let byCategory =
        kinds
        |> List.groupBy (str "category")
        |> List.map (fun (c, ks) -> c, ks |> List.sortWith (fun a b -> ordinal (str "tag" a) (str "tag" b)))

    let known = kinds |> List.map (str "tag") |> Set.ofList

    let ordered =
        let named =
            categoryOrder
            |> List.choose (fun c -> byCategory |> List.tryFind (fst >> (=) c))

        let extra =
            byCategory
            |> List.filter (fun (c, _) -> not (List.contains c categoryOrder))
            |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)

        named @ extra

    let sb = StringBuilder()

    line sb "| `kind.$type` | Recovered category | Fields (hoisted under `$type`) | Notes |"
    line sb "|---|---|---|---|"

    for (category, ks) in ordered do
        for k in ks do
            let tag = str "tag" k

            let fields =
                arr "fields" k
                |> List.map (fun f -> str "name" f, renderFieldToken f)
                |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                |> List.map (fun (_, token) -> "`" + token + "`")
                |> String.concat ", "

            let fields = if fields = "" then "_(none)_" else fields

            line sb (sprintf "| `%s` | _%s_ | %s | %s |" tag category (cell fields) (cell (note ann.Kinds tag)))

    sb.ToString().TrimEnd('\n'), known

let private enumsBlock (idl: JsonElement) (ann: Annotations) : string * string Set =
    let enums =
        arr "enums" idl
        |> List.sortWith (fun a b -> ordinal (str "name" a) (str "name" b))

    let known = enums |> List.map (str "name") |> Set.ofList
    let sb = StringBuilder()

    for e in enums do
        let name = str "name" e

        let cases =
            arr "cases" e
            |> List.map (fun c ->
                match c.GetString() with
                | null -> failwithf "idl.json: enum '%s' has a null case" name
                | s -> "`\"" + s + "\"`")
            |> String.concat " / "

        match note ann.Enums name with
        | "" -> line sb (sprintf "- `%s`: %s" name cases)
        | n -> line sb (sprintf "- `%s` (%s): %s" name n cases)

    sb.ToString().TrimEnd('\n'), known

/// One row per distinct (field, identity default) pair, with every site that
/// carries it. The KEY an annotation uses is `field=default` — unique by
/// construction over the current vocabulary, and checked to be so, because two
/// rows sharing a key would silently give one row the other's note.
let private omitDefaultsBlock (idl: JsonElement) (ann: Annotations) : string * string Set =
    let rows = ResizeArray<string * string * string * string>() // key, type, default, site

    let scan (site: string) (fields: JsonElement list) =
        for f in fields do
            match prop "optionality" f with
            | Some o when str "$type" o = "omitDefault" ->
                let name = str "name" f

                let dflt =
                    match prop "default" o with
                    | Some d -> renderDefault d
                    | None -> failwithf "idl.json: field '%s' is omitDefault with no default" name

                let ty =
                    match prop "type" f with
                    | Some t -> renderType t
                    | None -> failwithf "idl.json: field '%s' has no type" name

                rows.Add(name + "=" + dflt, ty, dflt, site)
            | _ -> ()

    for k in arr "kinds" idl do
        scan (str "tag" k + "Spec") (arr "fields" k)

    for r in arr "records" idl do
        scan (str "name" r) (arr "fields" r)

    for u in arr "unions" idl do
        let uname = str "name" u

        for c in arr "cases" u do
            scan (uname + "." + str "tag" c) (arr "fields" c)

    for o in arr "ops" idl do
        scan (str "tag" o) (arr "fields" o)

    let grouped =
        rows
        |> List.ofSeq
        |> List.groupBy (fun (key, ty, dflt, _) -> key, ty, dflt)
        |> List.map (fun ((key, ty, dflt), rs) ->
            key, ty, dflt, (rs |> List.map (fun (_, _, _, s) -> s) |> List.distinct |> List.sortWith ordinal))
        |> List.sortWith (fun (a, _, _, _) (b, _, _, _) -> ordinal a b)

    // A key collision would hand one row another's annotation. It cannot happen
    // over today's vocabulary; if a future field/default pair repeats at two
    // DIFFERENT types, say so rather than picking one.
    let dupes =
        grouped
        |> List.countBy (fun (key, _, _, _) -> key)
        |> List.filter (fun (_, n) -> n > 1)
        |> List.map (fun (key, n) ->
            sprintf
                "idl.json: the omit-at-default key '%s' resolves to %d distinct rows, so an annotation on it would be ambiguous. Widen the key (module SpecProjection.omitDefaultsBlock)."
                key
                n)

    if not dupes.IsEmpty then
        defect dupes

    let known = grouped |> List.map (fun (key, _, _, _) -> key) |> Set.ofList
    let sb = StringBuilder()

    line sb "| Field | Type | Identity default | Sites | Notes |"
    line sb "|---|---|---|---|---|"

    for (key, ty, dflt, sites) in grouped do
        let field = key.Substring(0, key.IndexOf '=')

        let siteList = sites |> List.map (fun s -> "`" + s + "`") |> String.concat ", "

        line
            sb
            (sprintf "| `%s` | `%s` | `%s` | %s | %s |" field ty dflt (cell siteList) (cell (note ann.OmitDefaults key)))

    sb.ToString().TrimEnd('\n'), known

/// §9 — the node-envelope fields the IDL classes `hostOnly`: they carry a
/// host-language surface and NOTHING on the wire, so a conformant host emits no
/// key for them at all. Deliberately NOT the optional-when-absent fields
/// (`accessibility` / `state` / `style`): those ARE emitted when authored, which
/// is a different claim and is made in the prose beneath.
let private wireOmittedBlock (idl: JsonElement) (ann: Annotations) : string * string Set =
    let fields =
        arr "nodeFields" idl
        |> List.filter (fun f ->
            match prop "optionality" f with
            | Some o -> str "$type" o = "hostOnly"
            | None -> false)
        |> List.sortWith (fun a b -> ordinal (str "name" a) (str "name" b))

    let known = fields |> List.map (str "name") |> Set.ofList
    let sb = StringBuilder()

    line sb "| Field | Host surface (F#) | Default on decode | Why omitted |"
    line sb "|---|---|---|---|"

    for f in fields do
        let name = str "name" f

        let host, placeholder =
            match prop "type" f |> Option.bind (prop "hostSurface") with
            | Some hs -> str "fsharp" hs, str "placeholder" hs
            | None -> failwithf "idl.json: node field '%s' is hostOnly with no hostSurface" name

        line
            sb
            (sprintf "| `%s` | `%s` | `%s` | %s |" name (cell host) (cell placeholder) (cell (note ann.NodeFields name)))

    sb.ToString().TrimEnd('\n'), known

// ─── Marker rewriting ───────────────────────────────────────────────────────

let private blockRegex (id: string) =
    Regex(
        sprintf @"<!-- fuaran:%s -->\r?\n(?<body>.*?)\r?\n<!-- /fuaran:%s -->" (Regex.Escape id) (Regex.Escape id),
        RegexOptions.Singleline
    )

let private countRegex =
    Regex(@"<!-- fuaran:count kind=(?<kind>[A-Za-z0-9._-]+) -->(?<body>.*?)<!-- /fuaran:count -->")

let private normalizeEol (s: string) =
    s.Replace("\r\n", "\n").Replace("\r", "\n")

/// The corpus's own fixture tally, by manifest `kind`, plus the pseudo-kind
/// `total`. The manifest is the authoritative enumeration — a count restated by
/// hand in prose is exactly the thing that was 70/21/30 against a corpus of
/// 134/22/60.
let private fixtureCounts (corpusDir: string) : Map<string, int> =
    let text = File.ReadAllText(Path.Combine(corpusDir, "manifest.json"))
    use doc = JsonDocument.Parse text

    let fixtures =
        doc.RootElement.GetProperty("fixtures").EnumerateArray() |> List.ofSeq

    let byKind =
        fixtures
        |> List.countBy (fun f ->
            match f.GetProperty("kind").GetString() with
            | null -> failwith "manifest.json: a fixture has a null kind"
            | s -> s)
        |> Map.ofList

    Map.add "total" fixtures.Length byKind

/// The reconciled text of `WIRE_FORMAT.md`, plus the drift findings comparing
/// it against what is on disk. Structural defects raise `ProjectionDefect`.
let reconcile (corpusDir: string) : string * string * string list =
    let specPath = Path.Combine(corpusDir, specFileName)

    if not (File.Exists specPath) then
        defect [ sprintf "%s not found under %s" specFileName corpusDir ]

    let idlPath = Path.Combine(corpusDir, "idl.json")

    if not (File.Exists idlPath) then
        defect
            [ sprintf
                  "idl.json not found under %s — it is the projection's structural source (Phase 696); regenerate it from Fuaran.Core's test CLI (--emit-idl)."
                  corpusDir ]

    use idlDoc = JsonDocument.Parse(File.ReadAllText idlPath)
    let idl = idlDoc.RootElement
    let ann = loadAnnotations corpusDir

    let kindsBody, knownKinds = kindsBlock idl ann
    let enumsBody, knownEnums = enumsBlock idl ann
    let defaultsBody, knownDefaults = omitDefaultsBlock idl ann
    let omittedBody, knownNodeFields = wireOmittedBlock idl ann

    let orphans =
        orphanKeys "kinds" knownKinds ann.Kinds
        @ orphanKeys "enums" knownEnums ann.Enums
        @ orphanKeys "omitDefaults" knownDefaults ann.OmitDefaults
        @ orphanKeys "nodeFields" knownNodeFields ann.NodeFields

    if not orphans.IsEmpty then
        defect orphans

    let blocks =
        [ "spec-kinds", kindsBody
          "spec-enums", enumsBody
          "spec-omit-defaults", defaultsBody
          "spec-wire-omitted", omittedBody ]

    let original = normalizeEol (File.ReadAllText specPath)
    let mutable rebuilt = original
    let drift = ResizeArray<string>()
    let missing = ResizeArray<string>()

    for (id, body) in blocks do
        let rx = blockRegex id
        let mutable seen = 0

        rebuilt <-
            rx.Replace(
                rebuilt,
                fun (m: Match) ->
                    seen <- seen + 1

                    if m.Groups.["body"].Value <> body then
                        drift.Add(
                            sprintf
                                "%s: block `fuaran:%s` diverges from idl.json (run --project-spec to regenerate)"
                                specFileName
                                id
                        )

                    sprintf "<!-- fuaran:%s -->\n%s\n<!-- /fuaran:%s -->" id body id
            )

        if seen = 0 then
            missing.Add(
                sprintf
                    "%s: no `<!-- fuaran:%s -->` … `<!-- /fuaran:%s -->` block found — the marker contract is broken, so this table is unmanaged and can drift silently."
                    specFileName
                    id
                    id
            )
        elif seen > 1 then
            missing.Add(
                sprintf "%s: `fuaran:%s` appears %d times; each managed block must be unique." specFileName id seen
            )

    let counts = fixtureCounts corpusDir
    let unknownCounts = ResizeArray<string>()
    let mutable countBlocks = 0

    rebuilt <-
        countRegex.Replace(
            rebuilt,
            fun (m: Match) ->
                countBlocks <- countBlocks + 1
                let kind = m.Groups.["kind"].Value

                match Map.tryFind kind counts with
                | None ->
                    unknownCounts.Add(
                        sprintf
                            "%s: `fuaran:count kind=%s` names no manifest fixture kind. Known kinds: %s."
                            specFileName
                            kind
                            (counts |> Map.toList |> List.map fst |> String.concat ", ")
                    )

                    m.Value
                | Some n ->
                    let expected = string n

                    if m.Groups.["body"].Value <> expected then
                        drift.Add(
                            sprintf
                                "%s: count `%s` reads %s, manifest.json says %s"
                                specFileName
                                kind
                                m.Groups.["body"].Value
                                expected
                        )

                    sprintf "<!-- fuaran:count kind=%s -->%s<!-- /fuaran:count -->" kind expected
        )

    if countBlocks = 0 then
        missing.Add(
            sprintf
                "%s: no `<!-- fuaran:count … -->` blocks found — every in-prose fixture count must be managed (the counts drift; the manifest cannot)."
                specFileName
        )

    let defects = List.ofSeq missing @ List.ofSeq unknownCounts

    if not defects.IsEmpty then
        defect defects

    // Deduplicated: one manifest kind can back several count markers (`reject`
    // is cited in §6 and §12), and repeating an identical finding once per
    // citation says nothing the first line did not.
    specPath, rebuilt, drift |> List.ofSeq |> List.distinct

/// Regenerate the managed blocks in place. Returns true when the file changed.
let write (corpusDir: string) : bool =
    let path, rebuilt, _ = reconcile corpusDir
    // RAW, not normalised. `rebuilt` is LF-only by construction, so comparing
    // raw means a CRLF working copy is rewritten to LF rather than reported as
    // "already matches" — `check` stays normalised because a line ending is not
    // a CONTENT divergence and should not read as spec drift.
    let current = File.ReadAllText path

    if current = rebuilt then
        false
    else
        // LF only — the corpus `.gitattributes` pins `eol=lf` and consumers
        // byte-compare the WORKING TREE, not the normalised blob.
        File.WriteAllText(path, rebuilt, UTF8Encoding false)
        true

/// Drift findings against the committed spec. Empty means the projection and
/// the document agree.
let check (corpusDir: string) : string list =
    let _, _, drift = reconcile corpusDir
    drift
