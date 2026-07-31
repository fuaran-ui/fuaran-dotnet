module Fuaran.UI.JsonDecode.Tests.LenientFixtures

// ============================================================================
//  Lenient-ingest fixture corpus (data form) — WIRE_FORMAT §16.
//
//  Each entry pairs a §16 SHORTHAND input with its VERBOSE canonical twin.
//  `Corpus.emit` decodes BOTH, asserts they re-encode byte-identically (the
//  §16 normalization law: `encode(decode(shorthand)) == encode(decode(verbose))`),
//  and writes `lenient/<id>.json` (the shorthand input) + the canonical bytes
//  as `lenient/<id>.expected.json`, with manifest kind `lenient-accept`.
//
//  This is what makes §16 a HOST-ENFORCEABLE contract rather than spec prose:
//  a conformant host MUST decode the shorthand input and re-encode to exactly
//  the expected bytes — a host that rejects the shorthand fails loudly, and a
//  host that decodes it to something else fails the byte comparison. (Before
//  this family existed, §16 lived only in per-host unit tests, so a host could
//  silently diverge while passing full corpus certification.)
//
//  §16 shorthand 1 (bare-string TextSource) needs fixtures; the node-level
//  shorthand 2 (omitted `state`/`style`/`accessibility`) is exercised by every
//  canonical round-trip fixture. Phase 460 extends shorthand 2 to the per-field
//  STYLISTIC slots (`format`/`tone`/`weight`/`emphasis`/`width`): the encoder now
//  omits each at its identity default, and explicit-default / lenient-alias inputs
//  decode-and-canonicalise to the minimal form — pinned by the `lenient-460-*`
//  fixtures below (so a second host must match, not just the F# reference).
//
//  Phase 429 adds a second family — LEGACY-FORM read-compat: the pre-429
//  encoder collapsed slot-typed `Binding.Static` payloads to `"<opaque>"`
//  (and empty lists / `None` to `null`, the F# boxes-to-null asymmetry).
//  Those wire forms stay decode-accepted indefinitely; these fixtures pin
//  that a conformant host decodes them and normalises to the typed forms
//  (the placeholder array / typed empty) — the same accept-and-canonicalise
//  law as §16, applied to superseded wire dialects.
// ============================================================================

type LenientFixture =
    {
        /// Corpus filename stem (`lenient/<id>.json`) + manifest id.
        Id: string
        /// The §16 shorthand form — what a token-frugal AI author emits.
        LenientJson: string
        /// The verbose canonical twin — what the strict encoder would emit for
        /// the same value. The emitter derives the expected bytes from this and
        /// asserts the two decode identically.
        VerboseJson: string
        /// Human-readable corpus/manifest description.
        Description: string
    }

let all: LenientFixture list =
    [
      // ─── fuaran#665 — legacy rows sentinel read-compat ───────────────────
      // Every pre-665 emission carried grid/chart rows as the residual
      // `"<opaque>"` sentinel. The sentinel stays decode-accepted INDEFINITELY
      // and normalises to the typed empty feed (`[]`) — the same
      // accept-and-canonicalise law as the Phase 429 legacy forms.
      { Id = "lenient-665-rows-opaque-sentinel"
        LenientJson =
          """{"id":"len-rows-opq","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"State","defaultValue":"<opaque>","key":"rows"}}}"""
        VerboseJson =
          """{"id":"len-rows-opq","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"State","defaultValue":[],"key":"rows"}}}"""
        Description =
          "fuaran#665 read-compat: the legacy \"<opaque>\" rows sentinel decodes to the empty typed feed and re-encodes as []" }

      // ─── 0.2.3 / fuaran-core#88 — Transform embedded-source shorthands ──
      // Core's lenient columnar ingest surfaces at the Transform slot: an
      // embedded source may omit `schema` (inferred from the cells) and a
      // column may ride as a bare array (all-present validity).
      { Id = "lenient-transform-schemaless"
        LenientJson =
          """{"id":"len-tf-s","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,200]},"dept":{"validity":[true,true],"values":["ops","eng"]}}}}}}"""
        VerboseJson =
          """{"id":"len-tf-s","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,200]},"dept":{"validity":[true,true],"values":["ops","eng"]}},"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}]}}}}"""
        Description =
          "fuaran-core#88 — an embedded Transform source may omit schema; column types infer from the cells (int/float/bool/string only)" }

      { Id = "lenient-transform-bare-columns"
        LenientJson =
          """{"id":"len-tf-b","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":[100,200],"dept":["ops","eng"]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-b","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,200]},"dept":{"validity":[true,true],"values":["ops","eng"]}},"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}]}}}}"""
        Description =
          "fuaran-core#88 — bare-array columns are the just-the-data shorthand (all-present validity); the wrapped form stays canonical" }

      // ─── 0.2.4 / fuaran-core#89 — flat filter-step coercion ─────────────
      { Id = "lenient-transform-flat-filter"
        LenientJson =
          """{"id":"len-tf-f","kind":{"$type":"DataGrid","columns":[{"field":"variety","kind":{"$type":"Text"},"label":"Variety"}],"rowKeyField":"variety","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"variety"},"name":"variety"}],"pipeline":[{"$type":"filter","column":"variety","op":"eq","param":"variety"}],"source":{"columns":{"variety":["Pinot","Chardonnay"]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-f","kind":{"$type":"DataGrid","columns":[{"field":"variety","kind":{"$type":"Text"},"label":"Variety"}],"rowKeyField":"variety","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"variety"},"name":"variety"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"variety"},"op":"eq","right":{"$type":"param","name":"variety"}}}],"source":{"columns":{"variety":{"validity":[true,true],"values":["Pinot","Chardonnay"]}},"schema":[{"name":"variety","type":"string"}]}}}}"""
        Description =
          "fuaran-core#89 — the flat filter step {column, op, param} coerces to the canonical nested predicate" }

      // ─── 0.2.5 / fuaran-core#90 — the search-chip prior (flat `contains`) ─
      { Id = "lenient-transform-flat-contains"
        LenientJson =
          """{"id":"len-tf-c","kind":{"$type":"DataGrid","columns":[{"field":"desk","kind":{"$type":"Text"},"label":"Desk"}],"rowKeyField":"desk","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"q"},"name":"q"}],"pipeline":[{"$type":"filter","column":"desk","op":"contains","param":"q"}],"source":{"columns":{"desk":["A1","B2"]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-c","kind":{"$type":"DataGrid","columns":[{"field":"desk","kind":{"$type":"Text"},"label":"Desk"}],"rowKeyField":"desk","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"q"},"name":"q"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"desk"},"op":"contains","right":{"$type":"param","name":"q"}}}],"source":{"columns":{"desk":{"validity":[true,true],"values":["A1","B2"]}},"schema":[{"name":"desk","type":"string"}]}}}}"""
        Description =
          "fuaran-core#90 — the text-search prior: a flat contains step coerces to the canonical nested predicate" }

      // ─── 0.2.6 / fuaran-core#92 — pipeline-step field aliases (pilot-4 census) ─
      { Id = "lenient-transform-step-aliases"
        LenientJson =
          """{"id":"len-tf-a","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[{"$type":"groupBy","by":["dept"],"aggregations":[{"column":"salary","op":"avg","as":"avgPay"}]},{"$type":"sort","keys":[{"column":"avgPay","descending":true}]},{"$type":"limit","count":3}],"source":{"columns":{"dept":["ops","eng"],"salary":[100,200]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-a","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"mean","name":"avgPay","of":"salary"}],"keys":["dept"]},{"$type":"sort","by":[{"col":"avgPay","dir":"desc"}]},{"$type":"limit","n":3,"offset":0}],"source":{"columns":{"dept":{"validity":[true,true],"values":["ops","eng"]},"salary":{"validity":[true,true],"values":[100,200]}},"schema":[{"name":"dept","type":"string"},{"name":"salary","type":"int"}]}}}}"""
        Description =
          "fuaran-core#92 — the SQL/pandas step-field spellings (by/aggregations/op/as/avg, keys/column/descending, count) coerce to the canonical fields" }

      // ─── 0.2.6 — CumulSum rename (legacy cumSum admitted) ────────────────
      { Id = "lenient-window-cumsum-legacy"
        LenientJson =
          """{"id":"len-w-cs","kind":{"$type":"DataGrid","columns":[{"field":"running","kind":{"$type":"Text"},"label":"Running"}],"rowKeyField":"running","source":{"$type":"Transform","pipeline":[{"$type":"window","as":"running","fn":"cumSum","of":"salary","orderBy":[{"col":"salary","dir":"asc"}],"partitionBy":["dept"]}],"source":{"columns":{"dept":["ops","eng"],"salary":[100,200]}}}}}"""
        VerboseJson =
          """{"id":"len-w-cs","kind":{"$type":"DataGrid","columns":[{"field":"running","kind":{"$type":"Text"},"label":"Running"}],"rowKeyField":"running","source":{"$type":"Transform","pipeline":[{"$type":"window","as":"running","fn":"cumulSum","of":"salary","orderBy":[{"col":"salary","dir":"asc"}],"partitionBy":["dept"]}],"source":{"columns":{"dept":{"validity":[true,true],"values":["ops","eng"]},"salary":{"validity":[true,true],"values":[100,200]}},"schema":[{"name":"dept","type":"string"},{"name":"salary","type":"int"}]}}}}"""
        Description = "2026-07-19 rename — the legacy cumSum window-fn tag coerces to the canonical cumulSum" }

      // ─── 0.2.7 / fuaran-core#93 — expression-spelling aliases ────────────
      // The verbatim tier-a-055 shakedown filter: predicate + expr-level
      // contains over call/lower both sides -> canonical Binary(Contains, ...).
      { Id = "lenient-transform-expr-spellings"
        LenientJson =
          """{"id":"len-tf-sp","kind":{"$type":"DataGrid","columns":[{"field":"name","kind":{"$type":"Text"},"label":"Name"}],"rowKeyField":"name","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"search"},"name":"search"}],"pipeline":[{"$type":"filter","predicate":{"$type":"contains","expr":{"$type":"call","fn":"lower","args":[{"$type":"col","name":"name"}]},"other":{"$type":"call","fn":"lower","args":[{"$type":"param","name":"search"}]}}}],"source":{"columns":{"name":["Mara","Kit"]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-sp","kind":{"$type":"DataGrid","columns":[{"field":"name","kind":{"$type":"Text"},"label":"Name"}],"rowKeyField":"name","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"search"},"name":"search"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"apply","args":[{"$type":"col","name":"name"}],"fn":"lower"},"op":"contains","right":{"$type":"apply","args":[{"$type":"param","name":"search"}],"fn":"lower"}}}],"source":{"columns":{"name":{"validity":[true,true],"values":["Mara","Kit"]}},"schema":[{"name":"name","type":"string"}]}}}}"""
        Description =
          "fuaran-core#93 — predicate/contains-as-$type/call spellings (the tier-a-055 shape) coerce to the canonical nested binary" }

      // ─── 0.2.2 — LVR emphasis style-enum coercion ──────────────────────
      // Pilot-3 trap: the Emphasis style enum written into the LVR BOOL.
      // "Loud" unambiguously means an emphasised row; "Normal"/"Quiet" not.
      { Id = "lenient-022-lvr-emphasis-loud"
        LenientJson =
          """{"id":"len-lvr-e","kind":{"$type":"LabelValueRow","emphasis":"Loud","label":"Total","value":{"$type":"Static","value":412}}}"""
        VerboseJson =
          """{"id":"len-lvr-e","kind":{"$type":"LabelValueRow","emphasis":true,"label":"Total","value":{"$type":"Static","value":412}}}"""
        Description =
          "0.2.2 — the Emphasis style-enum string in LabelValueRow's bool slot coerces (Loud→true; Normal/Quiet→false)" }

      { Id = "lenient-022-lvr-emphasis-normal"
        LenientJson =
          """{"id":"len-lvr-n","kind":{"$type":"LabelValueRow","emphasis":"Normal","label":"Adult fiction","value":{"$type":"Static","value":187}}}"""
        VerboseJson =
          """{"id":"len-lvr-n","kind":{"$type":"LabelValueRow","label":"Adult fiction","value":{"$type":"Static","value":187}}}"""
        Description = "0.2.2 — 'Normal' coerces to false, which is the omitted-when-false canonical form" }

      // ─── Phase 596 — symmetric form-field auto-bind ────────────────────
      // The OMITTED-value field is the canonical form; input carrying the
      // explicit auto-shape (`State(field id, typed placeholder)`) decodes to
      // the same value and re-encodes to the omitted bytes.
      { Id = "lenient-596-form-explicit-auto-state"
        LenientJson =
          """{"id":"len-fda","kind":{"$type":"Form","fields":[{"id":"guest-name","kind":{"$type":"Text","value":{"$type":"State","defaultValue":"","key":"guest-name"}},"label":"Name","required":true}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Book"}}"""
        VerboseJson =
          """{"id":"len-fda","kind":{"$type":"Form","fields":[{"id":"guest-name","kind":{"$type":"Text"},"label":"Name","required":true}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Book"}}"""
        Description =
          "Phase 596 — a form field's explicit auto-shape value (State(field id, placeholder)) normalises to the omitted-value canonical form" }

      // ─── §16.1 bare-string TextSource ───────────────────────────────────
      { Id = "lenient-bare-text-markdown"
        LenientJson = """{"id":"len-md","kind":{"$type":"Markdown","text":"hello *world*"}}"""
        VerboseJson =
          """{"id":"len-md","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"hello *world*"}}}"""
        Description = "Markdown.text as a bare JSON string (§16.1) — the highest-frequency saving" }

      { Id = "lenient-bare-text-heading"
        LenientJson =
          """{"id":"len-h","kind":{"$type":"Heading","level":2,"text":"Quarterly revenue","variant":"Standard"}}"""
        VerboseJson =
          """{"id":"len-h","kind":{"$type":"Heading","level":2,"text":{"$type":"Literal","text":"Quarterly revenue"},"variant":"Standard"}}"""
        Description = "Heading.text as a bare JSON string (§16.1)" }

      { Id = "lenient-bare-text-button-label"
        LenientJson =
          """{"id":"len-btn","kind":{"$type":"Button","label":"Refresh","onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}"""
        VerboseJson =
          """{"id":"len-btn","kind":{"$type":"Button","label":{"$type":"Literal","text":"Refresh"},"onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}"""
        Description = "Button.label as a bare JSON string (§16.1)" }

      { Id = "lenient-bare-text-callout"
        LenientJson =
          """{"id":"len-call","kind":{"$type":"Callout","body":"Check the numbers","dismissable":false,"heading":"Heads up","tone":"Warning"}}"""
        VerboseJson =
          """{"id":"len-call","kind":{"$type":"Callout","body":{"$type":"Literal","text":"Check the numbers"},"dismissable":false,"heading":{"$type":"Literal","text":"Heads up"},"tone":"Warning"}}"""
        Description = "Callout.body (required) + Callout.heading (optional position) as bare strings (§16.1)" }

      // ─── Phase 429 legacy-form read-compat (pre-429 opaque / null Statics) ──
      { Id = "lenient-opaque-static-options"
        LenientJson =
          """{"id":"len-opt","kind":{"$type":"Select","label":{"$type":"Literal","text":"Region"},"source":{"$type":"Static","value":"<opaque>"},"value":{"$type":"Static","value":"<opaque>"}}}"""
        VerboseJson =
          """{"id":"len-opt","kind":{"$type":"Select","label":{"$type":"Literal","text":"Region"},"source":{"$type":"Static","value":[{"label":{"$type":"Literal","text":"<opaque>"},"value":"<opaque>"}]},"value":{"$type":"Static","value":"<opaque>"}}}"""
        Description = "Pre-429 opaque Static options + value decode to the tagged placeholder forms and re-encode typed" }

      { Id = "lenient-null-static-options"
        LenientJson =
          """{"id":"len-nul","kind":{"$type":"Select","label":{"$type":"Literal","text":"Region"},"source":{"$type":"Static","value":null},"value":{"$type":"Static","value":null}}}"""
        VerboseJson =
          """{"id":"len-nul","kind":{"$type":"Select","label":{"$type":"Literal","text":"Region"},"source":{"$type":"Static","value":[]},"value":{"$type":"Static","value":null}}}"""
        Description =
          "Pre-429 boxes-to-null empty options list decodes to the typed empty array (value None stays null)" }

      { Id = "lenient-opaque-static-values"
        LenientJson =
          """{"id":"len-val","kind":{"$type":"Select","label":{"$type":"Literal","text":"Regions"},"multiple":true,"source":{"$type":"Static","value":[{"label":{"$type":"Literal","text":"UK"},"value":"uk"}]},"value":{"$type":"Static","value":null},"values":{"$type":"Static","value":"<opaque>"}}}"""
        VerboseJson =
          """{"id":"len-val","kind":{"$type":"Select","label":{"$type":"Literal","text":"Regions"},"multiple":true,"source":{"$type":"Static","value":[{"label":{"$type":"Literal","text":"UK"},"value":"uk"}]},"value":{"$type":"Static","value":null},"values":{"$type":"Static","value":["<opaque>"]}}}"""
        Description = "Pre-429 opaque multi-select values decode to the tagged placeholder list and re-encode typed" }

      { Id = "lenient-opaque-static-series"
        LenientJson = """{"id":"len-ser","kind":{"$type":"Sparkline","source":{"$type":"Static","value":"<opaque>"}}}"""
        VerboseJson = """{"id":"len-ser","kind":{"$type":"Sparkline","source":{"$type":"Static","value":[]}}}"""
        Description = "Pre-429 opaque Sparkline series decodes to the typed empty array" }

      { Id = "lenient-opaque-static-markers"
        LenientJson =
          """{"id":"len-mrk","kind":{"$type":"Map","centreLatitude":51.5,"centreLongitude":-0.1,"source":{"$type":"Static","value":"<opaque>"},"zoom":6}}"""
        VerboseJson =
          """{"id":"len-mrk","kind":{"$type":"Map","centreLatitude":51.5,"centreLongitude":-0.1,"source":{"$type":"Static","value":[]},"zoom":6}}"""
        Description = "Pre-429 opaque Map markers decode to the typed empty array" }


      // ─── Phase 460 stylistic omit-when-default read-compat + lenient aliases ──
      //  (a) Explicit-default reads: a pre-460 emission that wrote the stylistic
      //  fields at their identity default decodes and re-encodes to the minimal
      //  form (the encoder now omits them). (b) Lenient aliases: curated decode-only
      //  synonyms canonicalise to the DU case names. Both are the accept-and-
      //  canonicalise law (§16), applied to the stylistic slots.
      { Id = "lenient-460-explicit-default-metric"
        LenientJson =
          """{"id":"len-460-m","kind":{"$type":"Metric","emphasis":"Normal","format":{"$type":"None"},"label":{"$type":"Literal","text":"Revenue"},"tone":"Default","weight":"Standard","value":{"$type":"Static","value":1.0}}}"""
        VerboseJson =
          """{"id":"len-460-m","kind":{"$type":"Metric","label":{"$type":"Literal","text":"Revenue"},"value":{"$type":"Static","value":1.0}}}"""
        Description = "Phase 460 — explicit-default Metric style fields decode and re-encode minimal (read-compat)" }

      { Id = "lenient-460-explicit-default-style"
        LenientJson =
          """{"id":"len-460-s","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        VerboseJson = """{"id":"len-460-s","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}}}"""
        Description = "Phase 460 — explicit all-default SemanticStyle re-encodes as an omitted style (read-compat)" }

      { Id = "lenient-460-explicit-default-column"
        LenientJson =
          """{"id":"len-460-g","kind":{"$type":"DataGrid","columns":[{"format":{"$type":"None"},"kind":{"$type":"Text"},"label":"Channel","width":{"$type":"Auto"}}],"editable":false,"source":{"$type":"Static","value":"<opaque>"}}}"""
        VerboseJson =
          """{"id":"len-460-g","kind":{"$type":"DataGrid","columns":[{"kind":{"$type":"Text"},"label":"Channel"}],"editable":false,"source":{"$type":"Static","value":"<opaque>"}}}"""
        Description = "Phase 460 — explicit-default column format/width decode and re-encode minimal (read-compat)" }

      { Id = "lenient-460-alias-tone-positive"
        LenientJson =
          """{"id":"len-460-tp","kind":{"$type":"Callout","body":{"$type":"Literal","text":"x"},"dismissable":false,"tone":"Positive"}}"""
        VerboseJson =
          """{"id":"len-460-tp","kind":{"$type":"Callout","body":{"$type":"Literal","text":"x"},"dismissable":false,"tone":"Success"}}"""
        Description = "Phase 460 — lenient tone alias `Positive` canonicalises to `Success`" }

      { Id = "lenient-460-alias-tone-danger"
        LenientJson =
          """{"id":"len-460-td","kind":{"$type":"Callout","body":{"$type":"Literal","text":"x"},"dismissable":false,"tone":"Danger"}}"""
        VerboseJson =
          """{"id":"len-460-td","kind":{"$type":"Callout","body":{"$type":"Literal","text":"x"},"dismissable":false,"tone":"Critical"}}"""
        Description = "Phase 460 — lenient tone alias `Danger` canonicalises to `Critical`" }

      { Id = "lenient-460-alias-emphasis-strong"
        LenientJson =
          """{"id":"len-460-es","kind":{"$type":"Metric","emphasis":"Strong","label":{"$type":"Literal","text":"R"},"value":{"$type":"Static","value":1.0}}}"""
        VerboseJson =
          """{"id":"len-460-es","kind":{"$type":"Metric","emphasis":"Loud","label":{"$type":"Literal","text":"R"},"value":{"$type":"Static","value":1.0}}}"""
        Description = "Phase 460 — lenient emphasis alias `Strong` canonicalises to `Loud`" }

      { Id = "lenient-460-alias-emphasis-muted"
        LenientJson =
          """{"id":"len-460-em","kind":{"$type":"Metric","emphasis":"Muted","label":{"$type":"Literal","text":"R"},"value":{"$type":"Static","value":1.0}}}"""
        VerboseJson =
          """{"id":"len-460-em","kind":{"$type":"Metric","emphasis":"Quiet","label":{"$type":"Literal","text":"R"},"value":{"$type":"Static","value":1.0}}}"""
        Description = "Phase 460 — lenient emphasis alias `Muted` canonicalises to `Quiet`" }

      // ─── 2026-07-17 web-prior aliases (field names + remaining enum values) ──
      //  The 2026-07-16 Kimi smokes showed models emitting the dominant web-
      //  ecosystem NAME for a Fuaran concept (`href` for Navigate's `route`,
      //  2/2 identical). Field-name aliases join the 460 value aliases under
      //  the same law: decode-only, canonical name wins when both present,
      //  faithful same-concept mappings only. One fixture per alias family.
      { Id = "lenient-alias-navigate-href"
        LenientJson =
          """{"id":"len-nav","kind":{"$type":"Button","label":{"$type":"Literal","text":"Browse jobs"},"onClick":{"$type":"Navigate","href":"/jobs"},"variant":"Danger"}}"""
        VerboseJson =
          """{"id":"len-nav","kind":{"$type":"Button","label":{"$type":"Literal","text":"Browse jobs"},"onClick":{"$type":"Navigate","route":"/jobs"},"variant":"Destructive"}}"""
        Description =
          "Web-prior aliases — Navigate `href`→`route` (the 2/2 observed Kimi guess) + ButtonVariant `Danger`→`Destructive`" }

      { Id = "lenient-alias-call-url"
        LenientJson =
          """{"id":"len-url","kind":{"$type":"Button","label":{"$type":"Literal","text":"Refresh"},"onClick":{"$type":"Call","url":"/api/refresh"},"variant":"Primary"}}"""
        VerboseJson =
          """{"id":"len-url","kind":{"$type":"Button","label":{"$type":"Literal","text":"Refresh"},"onClick":{"$type":"Call","endpoint":"/api/refresh"},"variant":"Primary"}}"""
        Description = "Web-prior alias — Call `url`→`endpoint` (the fetch prior)" }

      { Id = "lenient-alias-grid-columns-row"
        LenientJson =
          """{"id":"len-grid","kind":{"$type":"Box","children":[{"id":"len-flexrow","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"row","wrap":false},"role":"Group"}}],"layout":{"$type":"Grid","columns":2},"role":"Group"}}"""
        VerboseJson =
          """{"id":"len-grid","kind":{"$type":"Box","children":[{"id":"len-flexrow","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Horizontal","wrap":false},"role":"Group"}}],"layout":{"$type":"Grid","cols":2},"role":"Group"}}"""
        Description =
          "Web-prior aliases — Grid `columns`→`cols` (CSS/Tailwind prior) + Flex direction `row`→`Horizontal` (CSS flex-direction prior)" }

      { Id = "lenient-alias-card-title-metric-value"
        LenientJson =
          """{"id":"len-card","kind":{"$type":"Box","children":[{"id":"len-kpi","kind":{"$type":"Metric","label":{"$type":"Literal","text":"Revenue"},"value":{"$type":"Static","value":1234.5}}},{"id":"len-hd","kind":{"$type":"Heading","level":3,"text":{"$type":"Literal","text":"Detail"},"variant":"Default"}},{"id":"len-bg","kind":{"$type":"Badge","label":{"$type":"Literal","text":"OK"},"variant":"Default"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card","title":{"$type":"Literal","text":"KPIs"}}}"""
        VerboseJson =
          """{"id":"len-card","kind":{"$type":"Box","children":[{"id":"len-kpi","kind":{"$type":"Metric","label":{"$type":"Literal","text":"Revenue"},"value":{"$type":"Static","value":1234.5}}},{"id":"len-hd","kind":{"$type":"Heading","level":3,"text":{"$type":"Literal","text":"Detail"},"variant":"Standard"}},{"id":"len-bg","kind":{"$type":"Badge","label":{"$type":"Literal","text":"OK"},"variant":"Neutral"}}],"heading":{"$type":"Literal","text":"KPIs"},"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}}"""
        Description =
          "Web-prior aliases — Box `title`→`heading` (the universal card/modal prior), Metric scalar `value` (0.2.0 canonical; the KPI-card prior IS canon now), HeadingVariant/BadgeVariant `Default`→identity case" }

      { Id = "lenient-alias-select-options-query-deps"
        LenientJson =
          """{"id":"len-sel","kind":{"$type":"Select","label":{"$type":"Literal","text":"Region"},"options":{"$type":"Query","accessor":"<closure>","deps":["region"],"name":"regions"},"value":{"$type":"State","initialValue":"uk","key":"sel"}}}"""
        VerboseJson =
          """{"id":"len-sel","kind":{"$type":"Select","label":{"$type":"Literal","text":"Region"},"source":{"$type":"Query","accessor":"<closure>","dependsOn":["region"],"name":"regions"},"value":{"$type":"State","defaultValue":"uk","key":"sel"}}}"""
        Description =
          "Web-prior aliases — Select `options`→`source` (the HTML select prior), Query `deps`→`dependsOn` (React hooks prior), State `initialValue`→`defaultValue` (useState prior)" }

      { Id = "lenient-alias-datagrid-data-column-type"
        LenientJson =
          """{"id":"len-dg","kind":{"$type":"DataGrid","columns":[{"field":"name","header":"Name","type":{"$type":"Text"}}],"data":{"$type":"Static","value":[{"name":"A"}]},"editable":false}}"""
        VerboseJson =
          """{"id":"len-dg","kind":{"$type":"DataGrid","columns":[{"field":"name","kind":{"$type":"Text"},"label":"Name"}],"editable":false,"source":{"$type":"Static","value":[{"name":"A"}]}}}"""
        Description =
          "Web-prior aliases — DataGrid `data`→`source` (Chart.js/react-table prior), column `type`→`kind` + `header`→`label` (react-table prior)" }

      { Id = "lenient-alias-form-field-name"
        LenientJson =
          """{"id":"len-form","kind":{"$type":"Form","fields":[{"kind":{"$type":"Text","value":{"$type":"State","defaultValue":"","key":"email"}},"label":{"$type":"Literal","text":"Email"},"name":"email","required":true}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":{"$type":"Literal","text":"Save"}}}"""
        VerboseJson =
          """{"id":"len-form","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","defaultValue":"","key":"email"}},"label":{"$type":"Literal","text":"Email"},"required":true}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":{"$type":"Literal","text":"Save"}}}"""
        Description = "Web-prior alias — form field `name`→`id` (the HTML forms prior)" }

      { Id = "lenient-shape-options-bare-strings"
        LenientJson =
          """{"id":"len-optshape","kind":{"$type":"Filters","items":[{"kind":{"$type":"Choice","options":["Pending","In Review","Approved"]},"label":"Status","name":"status"}]}}"""
        VerboseJson =
          """{"id":"len-optshape","kind":{"$type":"Filters","items":[{"kind":{"$type":"Choice","options":{"$type":"Static","value":[{"label":{"$type":"Literal","text":"Pending"},"value":"Pending"},{"label":{"$type":"Literal","text":"In Review"},"value":"In Review"},{"label":{"$type":"Literal","text":"Approved"},"value":"Approved"}]},"value":{"$type":"Filter","name":"status"}},"label":{"$type":"Literal","text":"Status"},"name":"status"}]}}"""
        Description =
          "Web-prior SHAPE coercions — a bare array where a Binding is expected coerces to `Static` (the omitted-envelope prior, 2/2 observed eval failures), and a bare string option element coerces to `{value: s, label: Literal s}` (the HTML `<select>` prior). The value→label map form (`{\"A\":\"A\"}`) is deliberately NOT coerced: JSON key order is not contractual, so it could silently reorder visible options." }

      { Id = "lenient-shape-segmented-orientation-omitted"
        LenientJson =
          """{"id":"len-seg","kind":{"$type":"Filters","items":[{"kind":{"$type":"SegmentedChoice","options":["Last 7 days","Last 30 days"]},"label":"Range","name":"range"}]}}"""
        VerboseJson =
          """{"id":"len-seg","kind":{"$type":"Filters","items":[{"kind":{"$type":"SegmentedChoice","options":{"$type":"Static","value":[{"label":{"$type":"Literal","text":"Last 7 days"},"value":"Last 7 days"},{"label":{"$type":"Literal","text":"Last 30 days"},"value":"Last 30 days"}]},"orientation":"Horizontal","value":{"$type":"Filter","name":"range"}},"label":{"$type":"Literal","text":"Range"},"name":"range"}]}}"""
        Description =
          "Omitted-when-default (the Phase 460 posture applied to `orientation`) — an absent segmented `orientation` restores the language default `Horizontal` (observed omitted in eval emission data); decode-only, the encoder still always emits it. Combined here with the bare-options shape coercion." }

      { Id = "lenient-shape-binding-scalar-fraction"
        LenientJson =
          """{"id":"len-prog","kind":{"$type":"Progress","fraction":0.65,"indeterminate":{"$type":"Static","value":false},"label":{"$type":"Literal","text":"Overall completion"}}}"""
        VerboseJson =
          """{"id":"len-prog","kind":{"$type":"Progress","fraction":{"$type":"Static","value":0.65},"indeterminate":false,"label":{"$type":"Literal","text":"Overall completion"}}}"""
        Description =
          "Envelope-confusion coercions, BOTH directions (2026-07-17 launch-eval evidence) — a bare scalar where a Binding is expected coerces to `Static` (`fraction: 0.65`), and a Static envelope where a PLAIN value is expected unwraps (`indeterminate: {\"$type\":\"Static\",\"value\":false}`). Unambiguous both ways; `null` and untyped objects stay strict." }

      { Id = "lenient-shape-params-map"
        LenientJson =
          """{"id":"len-params","kind":{"$type":"DataGrid","columns":[],"editable":false,"rowKey":"<closure>","source":{"$type":"Transform","params":{"stockLevel":{"$type":"Filter","name":"stockLevel"},"warehouse":{"$type":"Filter","name":"warehouse"}},"pipeline":[],"source":{"columns":{"sku":{"validity":[true],"values":["A-1"]}},"schema":[{"name":"sku","type":"string"}]}}}}"""
        VerboseJson =
          """{"id":"len-params","kind":{"$type":"DataGrid","columns":[],"editable":false,"rowKey":"<closure>","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"stockLevel"},"name":"stockLevel"},{"from":{"$type":"Filter","name":"warehouse"},"name":"warehouse"}],"pipeline":[],"source":{"columns":{"sku":{"validity":[true],"values":["A-1"]}},"schema":[{"name":"sku","type":"string"}]}}}}"""
        Description =
          "Query-params shape coercion — the name→binding MAP form coerces to the canonical `[{name, from}]` array (params are a name-keyed set, so key order carries no meaning — unlike the options map, which is refused), and `value` aliases `from` at the element. Every provider's first guess in the launch eval; 21/31 failures were repair-proof." }

      { Id = "lenient-shape-grid-no-cols"
        LenientJson =
          """{"id":"len-autogrid","kind":{"$type":"Box","children":[],"layout":{"$type":"Grid"},"role":"Group"}}"""
        VerboseJson =
          """{"id":"len-autogrid","kind":{"$type":"Box","children":[],"layout":{"$type":"Auto"},"role":"Group"}}"""
        Description =
          "Grid with NO column spec (no cols/columns/templateColumns) is the CSS auto-grid prior — accept-and-canonicalise to the language's existing `Auto` responsive auto-tile layout. 35 launch-eval cells across 8 tasks, every provider." }

      { Id = "lenient-fact-explicit-defaults"
        LenientJson =
          """{"id":"len-fact","kind":{"$type":"Fact","emphasis":false,"label":{"$type":"Literal","text":"Patient"},"tone":"Default","value":{"$type":"Literal","text":"Alice Smith"}}}"""
        VerboseJson = """{"id":"len-fact","kind":{"$type":"Fact","label":"Patient","value":"Alice Smith"}}"""
        Description =
          "Fact (the new labeled text-fact kind) — explicit-default `tone`/`emphasis` decode and canonicalise away (omitted-when-default on both boundaries from day one), and the bare-string TextSource shorthand applies to `label`/`value`. The canonical minimal Fact is exactly the two-field form models want to write." }

      { Id = "lenient-shape-static-envelope-plain-scalars"
        LenientJson =
          """{"id":"len-env","kind":{"$type":"LabelValueRow","emphasis":{"$type":"Static","value":true},"label":"Total","value":{"$type":"Static","value":42.0}}}"""
        VerboseJson =
          """{"id":"len-env","kind":{"$type":"LabelValueRow","emphasis":true,"label":{"$type":"Literal","text":"Total"},"value":{"$type":"Static","value":42.0}}}"""
        Description =
          "Static-envelope unwrap GENERALISED to every plain-scalar position (2026-07-18 — the 0.1.6 pilot found `emphasis` wrapped after `indeterminate` was fixed site-locally; the confusion is generic). A well-formed envelope at a plain-scalar position has exactly one reading; the encoder still emits bare scalars." }

      { Id = "lenient-shape-grid-template-no-cols"
        LenientJson =
          """{"id":"len-tmpl","kind":{"$type":"Box","children":[],"layout":{"$type":"Grid","templateColumns":"1fr 2fr"},"role":"Group"}}"""
        VerboseJson =
          """{"id":"len-tmpl","kind":{"$type":"Box","children":[],"layout":{"$type":"Grid","cols":1,"templateColumns":"1fr 2fr"},"role":"Group"}}"""
        Description =
          "Grid with `templateColumns` but no `cols` — `Cols` is documented-ignored when `templateColumns` is present, so absence defaults to 1 instead of MISSING_FIELD (the 0.1.6 pilot residual). Distinct from the no-column-spec-at-all form, which canonicalises to `Auto`." }

      // ─── 2026-07-19 collision sweep — the `emphasis` cross-vocabulary slip ─
      // Same field name, two meanings: the behavioural BOOL (Fact /
      // LabelValueRow) vs the Emphasis STYLE ENUM (style / Metric). Models
      // cross it in both directions (pilot-4: 'Strong' hard-failed on the
      // bool; the sweep confirmed the class). Both directions coerce
      // one-to-one: enum + Phase-460 aliases project onto the bool
      // (Loud/Strong/Bold ⇒ true; Normal/Quiet/Subtle/Muted ⇒ false), and a
      // bool in the enum slot projects back (true ⇒ Loud, false ⇒ Normal).
      { Id = "lenient-emphasis-cross-vocab"
        LenientJson =
          """{"id":"len-emph","kind":{"$type":"Box","children":[{"id":"len-emph-row","kind":{"$type":"LabelValueRow","emphasis":"Strong","label":"Total","value":{"$type":"Static","value":42}},"style":{"emphasis":true}},{"id":"len-emph-fact","kind":{"$type":"Fact","emphasis":"Loud","label":"Status","value":"Open"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
        VerboseJson =
          """{"id":"len-emph","kind":{"$type":"Box","children":[{"id":"len-emph-row","kind":{"$type":"LabelValueRow","emphasis":true,"label":"Total","value":{"$type":"Static","value":42}},"style":{"emphasis":"Loud"}},{"id":"len-emph-fact","kind":{"$type":"Fact","emphasis":true,"label":"Status","value":"Open"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
        Description =
          "2026-07-19 collision sweep — `emphasis` cross-vocabulary coercion, both directions: the style enum + Phase-460 aliases in the Fact/LabelValueRow BOOL slots (Loud/Strong/Bold ⇒ true, Normal/Quiet/Subtle/Muted ⇒ false) and a bool in the style-enum slot (true ⇒ Loud). One-to-one; the encoder still emits the canonical types." }

      // ─── 0.2.11 / fuaran-core#94 — the pilot-5 lenient wave ──────────────
      // The pilot-5 n=1 census (gemini continuity arm): three near-canonical
      // shapes, each unambiguous. (1) a wrapped column object carrying
      // `values` but no `validity` mask — the same all-present statement as
      // the #88 bare array; (2) flat logical/comparison expression spellings
      // ({"$type":"or","exprs":[…]} variadic, flat eq/gt with left/right)
      // against the canonical nested `binary`; (3) epoch numbers in a
      // declared-`timestamp` column (unit by magnitude, ≥1e11 ⇒ ms).
      { Id = "lenient-transform-values-only-columns"
        LenientJson =
          """{"id":"len-tf-vo","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"values":[100,200]},"dept":{"values":["ops","eng"]}},"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}]}}}}"""
        VerboseJson =
          """{"id":"len-tf-vo","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,200]},"dept":{"validity":[true,true],"values":["ops","eng"]}},"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}]}}}}"""
        Description =
          "fuaran-core#94 — a values-only column object is the all-present shorthand (the canonical wrapped shape minus the mask); the masked form stays canonical" }

      { Id = "lenient-transform-flat-or"
        LenientJson =
          """{"id":"len-tf-or","kind":{"$type":"DataGrid","columns":[{"field":"item","kind":{"$type":"Text"},"label":"Item"}],"rowKeyField":"item","source":{"$type":"Transform","pipeline":[{"$type":"filter","pred":{"$type":"or","exprs":[{"$type":"eq","left":{"$type":"col","name":"status"},"right":{"$type":"lit","cell":{"$type":"Str","value":"low"}}},{"$type":"eq","left":{"$type":"col","name":"status"},"right":{"$type":"lit","cell":{"$type":"Str","value":"critical"}}}]}}],"source":{"columns":{"item":["widget","gadget"],"status":["low","ok"]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-or","kind":{"$type":"DataGrid","columns":[{"field":"item","kind":{"$type":"Text"},"label":"Item"}],"rowKeyField":"item","source":{"$type":"Transform","pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"binary","left":{"$type":"col","name":"status"},"op":"eq","right":{"$type":"lit","cell":{"$type":"Str","value":"low"}}},"op":"or","right":{"$type":"binary","left":{"$type":"col","name":"status"},"op":"eq","right":{"$type":"lit","cell":{"$type":"Str","value":"critical"}}}}}],"source":{"columns":{"item":{"validity":[true,true],"values":["widget","gadget"]},"status":{"validity":[true,true],"values":["low","ok"]}},"schema":[{"name":"item","type":"string"},{"name":"status","type":"string"}]}}}}"""
        Description =
          "fuaran-core#94 — flat variadic `or` (exprs array) + flat `eq` (left/right) left-fold into the canonical nested `binary` tree" }

      { Id = "lenient-transform-flat-scalar-fn"
        LenientJson =
          """{"id":"len-tf-fn","kind":{"$type":"DataGrid","columns":[{"field":"desk","kind":{"$type":"Text"},"label":"Desk"}],"rowKeyField":"desk","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"q"},"name":"q"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"fn","fn":"lower","args":[{"$type":"col","name":"desk"}]},"op":"contains","right":{"$type":"lower","expr":{"$type":"param","name":"q"}}}}],"source":{"columns":{"desk":["A1","B2"]}}}}}"""
        VerboseJson =
          """{"id":"len-tf-fn","kind":{"$type":"DataGrid","columns":[{"field":"desk","kind":{"$type":"Text"},"label":"Desk"}],"rowKeyField":"desk","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"q"},"name":"q"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"apply","args":[{"$type":"col","name":"desk"}],"fn":"lower"},"op":"contains","right":{"$type":"apply","args":[{"$type":"param","name":"q"}],"fn":"lower"}}}],"source":{"columns":{"desk":{"validity":[true,true],"values":["A1","B2"]}},"schema":[{"name":"desk","type":"string"}]}}}}"""
        Description =
          "fuaran-core#94 — flat scalar-fn spellings: `fn` aliases `apply` (same fn/args), and a bare fn-name node ({\"$type\":\"lower\",\"expr\":…}) denotes ApplyFn directly; canonical stays `apply`" }

      // ─── 0.2.12 / pilot-5 n=3 gate — the `Bound` wrapper in bare-Binding slots ─
      // Models transfer the canonical `TextSource.Bound` envelope convention
      // uniformly to slots typed as raw Binding (Metric.value / trend, etc.):
      // {"$type":"Bound","binding":X}. Exactly one payload field ⇒ one-to-one
      // unwrap; the canonical encoder never wraps bare-Binding slots.
      { Id = "lenient-binding-bound-wrapper"
        LenientJson =
          """{"id":"len-bound","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":"Revenue","subtext":"vs last month","tone":"Brand","trend":{"$type":"Bound","binding":{"$type":"Static","value":0.07}},"trendFormat":{"$type":"Percent","decimals":1},"value":{"$type":"Bound","binding":{"$type":"Static","value":1234.5}}}}"""
        VerboseJson =
          """{"id":"len-bound","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":"Revenue","subtext":"vs last month","tone":"Brand","trend":{"$type":"Static","value":0.07},"trendFormat":{"$type":"Percent","decimals":1},"value":{"$type":"Static","value":1234.5}}}"""
        Description =
          "pilot-5 n=3 gate — the Bound wrapper unwraps in bare-Binding slots (the TextSource.Bound convention transferred; one payload field, one-to-one); canonical stays the bare binding" }

      { Id = "lenient-transform-epoch-timestamps"
        LenientJson =
          """{"id":"len-tf-ts","kind":{"$type":"DataGrid","columns":[{"field":"runner","kind":{"$type":"Text"},"label":"Runner"}],"rowKeyField":"runner","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"finish_ts":{"values":[1752000000,1752000000000]},"runner":{"values":["Asha","Beatriz"]}},"schema":[{"name":"finish_ts","type":"timestamp"},{"name":"runner","type":"string"}]}}}}"""
        VerboseJson =
          """{"id":"len-tf-ts","kind":{"$type":"DataGrid","columns":[{"field":"runner","kind":{"$type":"Text"},"label":"Runner"}],"rowKeyField":"runner","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"finish_ts":{"validity":[true,true],"values":["2025-07-08T18:40:00Z","2025-07-08T18:40:00Z"]},"runner":{"validity":[true,true],"values":["Asha","Beatriz"]}},"schema":[{"name":"finish_ts","type":"timestamp"},{"name":"runner","type":"string"}]}}}}"""
        Description =
          "fuaran-core#94 — epoch numbers in a declared-timestamp column decode to the canonical ISO instant (seconds, and milliseconds via the whole-float path; unit by magnitude)" }

      // ─── Phase 719 — compact authoring twins of the pack exemplars ────────
      // The pack's marker blocks + few-shot render fixture INPUT files
      // verbatim; until 719 they rendered the canonical envelope, teaching by
      // example the all-true validity masks + inferable schema the prose calls
      // optional (models imitate exemplars over prose — envelope share
      // claude-opus 81% / grok 70% / gemini 62%, 2026-07-28 census; on
      // data-heavy cells the dead weight reaches 33% of emission chars). Each
      // twin's lenient side is the compact authoring form of one exemplar
      // (bare all-valid columns, Core#88; schema omitted where every type
      // infers); the verbose side is the exemplar's canonical bytes, so the
      // corpus pins decode-equality and the pack renders the compact form
      // without hand-written JSON.

      { Id = "lenient-grid-field-named-compact"
        LenientJson =
          """{"id":"grid-field-named","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"},{"field":"amount","kind":{"$type":"Text"},"label":"Amount"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":[100],"dept":["eng"]}}}}}"""
        VerboseJson =
          """{"id":"grid-field-named","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"},{"field":"amount","kind":{"$type":"Text"},"label":"Amount"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true],"values":[100]},"dept":{"validity":[true],"values":["eng"]}},"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}]}}}}"""
        Description =
          "Phase 719 — the compact authoring twin of `grid-field-named`: all-valid columns as bare arrays (Core#88) and the inferable schema omitted; the canonical envelope stays the encoder's output. The pack teaches this form by example — the exemplar/prose contradiction measurably taught the envelope (claude-opus 81% envelope share, 2026-07-28 census)." }

      { Id = "lenient-grid-transform-param-compact"
        LenientJson =
          """{"id":"grid-transform-param","kind":{"$type":"DataGrid","columns":[],"rowKey":"<closure>","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"dept"},"name":"dept"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}}}],"source":{"columns":{"amount":[100,90],"dept":["eng","sales"]}}}}}"""
        VerboseJson =
          """{"id":"grid-transform-param","kind":{"$type":"DataGrid","columns":[],"rowKey":"<closure>","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"dept"},"name":"dept"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}}}],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,90]},"dept":{"validity":[true,true],"values":["eng","sales"]}},"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}]}}}}"""
        Description =
          "Phase 719 — the compact authoring twin of `grid-transform-param`: all-valid columns as bare arrays (Core#88) and the inferable schema omitted; the canonical envelope stays the encoder's output. The pack teaches this form by example — the exemplar/prose contradiction measurably taught the envelope (claude-opus 81% envelope share, 2026-07-28 census)." }

      { Id = "lenient-filterable-static-dashboard-compact"
        LenientJson =
          """{"id":"filterable-static-dashboard","kind":{"$type":"Box","children":[{"id":"content-filters","kind":{"$type":"Filters","items":[{"kind":{"$type":"Choice","options":{"$type":"Static","value":[{"label":"EMEA","value":"emea"},{"label":"Americas","value":"amer"}]}},"label":"Region","name":"region"},{"kind":{"$type":"Choice","options":{"$type":"Static","value":[{"label":"Drama","value":"drama"},{"label":"Documentary","value":"docs"}]}},"label":"Genre","name":"genre"}]}},{"id":"retention-chart","kind":{"$type":"Chart","kind":"Line","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"region"},"name":"region"},{"from":{"$type":"Filter","name":"genre"},"name":"genre"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"region"},"op":"eq","right":{"$type":"param","name":"region"}}},{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"genre"},"op":"eq","right":{"$type":"param","name":"genre"}}}],"source":{"columns":{"genre":["drama","docs"],"month":["jan","jan"],"region":["emea","amer"],"retention":[0.62,0.55]}}},"stacked":false,"title":"Retention","xField":"month","yFields":["retention"]}},{"id":"episode-grid","kind":{"$type":"DataGrid","columns":[{"field":"month","kind":{"$type":"Text"},"label":"Month"},{"field":"retention","kind":{"$type":"Text"},"label":"Retention"}],"rowKeyField":"month","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"region"},"name":"region"},{"from":{"$type":"Filter","name":"genre"},"name":"genre"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"region"},"op":"eq","right":{"$type":"param","name":"region"}}},{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"genre"},"op":"eq","right":{"$type":"param","name":"genre"}}}],"source":{"columns":{"genre":["drama","docs"],"month":["jan","jan"],"region":["emea","amer"],"retention":[0.62,0.55]}}}}}],"heading":"Content performance","layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        VerboseJson =
          """{"id":"filterable-static-dashboard","kind":{"$type":"Box","children":[{"id":"content-filters","kind":{"$type":"Filters","items":[{"kind":{"$type":"Choice","options":{"$type":"Static","value":[{"label":"EMEA","value":"emea"},{"label":"Americas","value":"amer"}]}},"label":"Region","name":"region"},{"kind":{"$type":"Choice","options":{"$type":"Static","value":[{"label":"Drama","value":"drama"},{"label":"Documentary","value":"docs"}]}},"label":"Genre","name":"genre"}]}},{"id":"retention-chart","kind":{"$type":"Chart","kind":"Line","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"region"},"name":"region"},{"from":{"$type":"Filter","name":"genre"},"name":"genre"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"region"},"op":"eq","right":{"$type":"param","name":"region"}}},{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"genre"},"op":"eq","right":{"$type":"param","name":"genre"}}}],"source":{"columns":{"genre":{"validity":[true,true],"values":["drama","docs"]},"month":{"validity":[true,true],"values":["jan","jan"]},"region":{"validity":[true,true],"values":["emea","amer"]},"retention":{"validity":[true,true],"values":[0.62,0.55]}},"schema":[{"name":"genre","type":"string"},{"name":"month","type":"string"},{"name":"region","type":"string"},{"name":"retention","type":"float"}]}},"stacked":false,"title":"Retention","xField":"month","yFields":["retention"]}},{"id":"episode-grid","kind":{"$type":"DataGrid","columns":[{"field":"month","kind":{"$type":"Text"},"label":"Month"},{"field":"retention","kind":{"$type":"Text"},"label":"Retention"}],"rowKeyField":"month","source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"region"},"name":"region"},{"from":{"$type":"Filter","name":"genre"},"name":"genre"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"region"},"op":"eq","right":{"$type":"param","name":"region"}}},{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"genre"},"op":"eq","right":{"$type":"param","name":"genre"}}}],"source":{"columns":{"genre":{"validity":[true,true],"values":["drama","docs"]},"month":{"validity":[true,true],"values":["jan","jan"]},"region":{"validity":[true,true],"values":["emea","amer"]},"retention":{"validity":[true,true],"values":[0.62,0.55]}},"schema":[{"name":"genre","type":"string"},{"name":"month","type":"string"},{"name":"region","type":"string"},{"name":"retention","type":"float"}]}}}}],"heading":"Content performance","layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        Description =
          "Phase 719 — the compact authoring twin of `filterable-static-dashboard`: all-valid columns as bare arrays (Core#88) and the inferable schema omitted; the canonical envelope stays the encoder's output. The pack teaches this form by example — the exemplar/prose contradiction measurably taught the envelope (claude-opus 81% envelope share, 2026-07-28 census)." }

      { Id = "lenient-master-detail-preselected-compact"
        LenientJson =
          """{"id":"master-detail-preselected","kind":{"$type":"Box","children":[{"id":"ticket-grid","kind":{"$type":"DataGrid","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Ticket"},{"field":"priority","kind":{"$type":"Text"},"label":"Priority"}],"rowKeyField":"id","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"id":["TCK-2041","TCK-2042"],"priority":["high","low"]}}}}},{"id":"ticket-detail","kind":{"$type":"Box","children":[{"id":"detail-ticket","kind":{"$type":"Fact","emphasis":true,"label":"Selected ticket","value":{"$type":"Bound","binding":{"$type":"Selection","defaultValue":"TCK-2041","field":"id","nodeId":"ticket-grid"}}}}],"heading":"Ticket detail","layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}},{"id":"related-grid","kind":{"$type":"DataGrid","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Ticket"},{"field":"priority","kind":{"$type":"Text"},"label":"Priority"}],"rowKeyField":"id","source":{"$type":"Transform","params":[{"from":{"$type":"Selection","defaultValue":"TCK-2041","field":"id","nodeId":"ticket-grid"},"name":"ticketId"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"id"},"op":"eq","right":{"$type":"param","name":"ticketId"}}}],"source":{"columns":{"id":["TCK-2041","TCK-2042"],"priority":["high","low"]}}}}}],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        VerboseJson =
          """{"id":"master-detail-preselected","kind":{"$type":"Box","children":[{"id":"ticket-grid","kind":{"$type":"DataGrid","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Ticket"},{"field":"priority","kind":{"$type":"Text"},"label":"Priority"}],"rowKeyField":"id","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"id":{"validity":[true,true],"values":["TCK-2041","TCK-2042"]},"priority":{"validity":[true,true],"values":["high","low"]}},"schema":[{"name":"id","type":"string"},{"name":"priority","type":"string"}]}}}},{"id":"ticket-detail","kind":{"$type":"Box","children":[{"id":"detail-ticket","kind":{"$type":"Fact","emphasis":true,"label":"Selected ticket","value":{"$type":"Bound","binding":{"$type":"Selection","defaultValue":"TCK-2041","field":"id","nodeId":"ticket-grid"}}}}],"heading":"Ticket detail","layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}},{"id":"related-grid","kind":{"$type":"DataGrid","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Ticket"},{"field":"priority","kind":{"$type":"Text"},"label":"Priority"}],"rowKeyField":"id","source":{"$type":"Transform","params":[{"from":{"$type":"Selection","defaultValue":"TCK-2041","field":"id","nodeId":"ticket-grid"},"name":"ticketId"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"id"},"op":"eq","right":{"$type":"param","name":"ticketId"}}}],"source":{"columns":{"id":{"validity":[true,true],"values":["TCK-2041","TCK-2042"]},"priority":{"validity":[true,true],"values":["high","low"]}},"schema":[{"name":"id","type":"string"},{"name":"priority","type":"string"}]}}}}],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        Description =
          "Phase 719 — the compact authoring twin of `master-detail-preselected`: all-valid columns as bare arrays (Core#88) and the inferable schema omitted; the canonical envelope stays the encoder's output. The pack teaches this form by example — the exemplar/prose contradiction measurably taught the envelope (claude-opus 81% envelope share, 2026-07-28 census)." }

      { Id = "lenient-scalar-transform-composition-compact"
        LenientJson =
          """{"id":"scalar-transform-composition","kind":{"$type":"Box","children":[{"id":"scalar-ticket-grid","kind":{"$type":"DataGrid","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Ticket"},{"field":"severity","kind":{"$type":"Text"},"label":"Severity"}],"rowKeyField":"id","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"alert":["TCK-2041 breaches SLA in 2 hours","TCK-2042 breaches SLA in 5 hours","TCK-2043 breaches SLA in 9 hours"],"id":["TCK-2041","TCK-2042","TCK-2043"],"severity":["critical","high","critical"]}}}}},{"id":"critical-count-badge","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"severity"},"op":"eq","right":{"$type":"lit","cell":{"$type":"Str","value":"critical"}}}},{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"id"}],"keys":[]}],"source":{"columns":{"alert":["TCK-2041 breaches SLA in 2 hours","TCK-2042 breaches SLA in 5 hours","TCK-2043 breaches SLA in 9 hours"],"id":["TCK-2041","TCK-2042","TCK-2043"],"severity":["critical","high","critical"]}}}},"variant":"Critical"}},{"id":"sla-warning","kind":{"$type":"Callout","body":{"$type":"Bound","binding":{"$type":"Transform","params":[{"from":{"$type":"Selection","defaultValue":"TCK-2041","field":"id","nodeId":"scalar-ticket-grid"},"name":"ticketId"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"id"},"op":"eq","right":{"$type":"param","name":"ticketId"}}},{"$type":"project","cols":[{"a":"alert","b":"alert"}]},{"$type":"limit","n":1,"offset":0}],"source":{"columns":{"alert":["TCK-2041 breaches SLA in 2 hours","TCK-2042 breaches SLA in 5 hours","TCK-2043 breaches SLA in 9 hours"],"id":["TCK-2041","TCK-2042","TCK-2043"],"severity":["critical","high","critical"]}}}},"heading":"SLA breach imminent","tone":"Warning"}}],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        VerboseJson =
          """{"id":"scalar-transform-composition","kind":{"$type":"Box","children":[{"id":"scalar-ticket-grid","kind":{"$type":"DataGrid","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Ticket"},{"field":"severity","kind":{"$type":"Text"},"label":"Severity"}],"rowKeyField":"id","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"alert":{"validity":[true,true,true],"values":["TCK-2041 breaches SLA in 2 hours","TCK-2042 breaches SLA in 5 hours","TCK-2043 breaches SLA in 9 hours"]},"id":{"validity":[true,true,true],"values":["TCK-2041","TCK-2042","TCK-2043"]},"severity":{"validity":[true,true,true],"values":["critical","high","critical"]}},"schema":[{"name":"alert","type":"string"},{"name":"id","type":"string"},{"name":"severity","type":"string"}]}}}},{"id":"critical-count-badge","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"severity"},"op":"eq","right":{"$type":"lit","cell":{"$type":"Str","value":"critical"}}}},{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"id"}],"keys":[]}],"source":{"columns":{"alert":{"validity":[true,true,true],"values":["TCK-2041 breaches SLA in 2 hours","TCK-2042 breaches SLA in 5 hours","TCK-2043 breaches SLA in 9 hours"]},"id":{"validity":[true,true,true],"values":["TCK-2041","TCK-2042","TCK-2043"]},"severity":{"validity":[true,true,true],"values":["critical","high","critical"]}},"schema":[{"name":"alert","type":"string"},{"name":"id","type":"string"},{"name":"severity","type":"string"}]}}},"variant":"Critical"}},{"id":"sla-warning","kind":{"$type":"Callout","body":{"$type":"Bound","binding":{"$type":"Transform","params":[{"from":{"$type":"Selection","defaultValue":"TCK-2041","field":"id","nodeId":"scalar-ticket-grid"},"name":"ticketId"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"id"},"op":"eq","right":{"$type":"param","name":"ticketId"}}},{"$type":"project","cols":[{"a":"alert","b":"alert"}]},{"$type":"limit","n":1,"offset":0}],"source":{"columns":{"alert":{"validity":[true,true,true],"values":["TCK-2041 breaches SLA in 2 hours","TCK-2042 breaches SLA in 5 hours","TCK-2043 breaches SLA in 9 hours"]},"id":{"validity":[true,true,true],"values":["TCK-2041","TCK-2042","TCK-2043"]},"severity":{"validity":[true,true,true],"values":["critical","high","critical"]}},"schema":[{"name":"alert","type":"string"},{"name":"id","type":"string"},{"name":"severity","type":"string"}]}}},"heading":"SLA breach imminent","tone":"Warning"}}],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        Description =
          "Phase 719 — the compact authoring twin of `scalar-transform-composition`: all-valid columns as bare arrays (Core#88) and the inferable schema omitted; the canonical envelope stays the encoder's output. The pack teaches this form by example — the exemplar/prose contradiction measurably taught the envelope (claude-opus 81% envelope share, 2026-07-28 census)." }

      // ─── Phase 725 — the DateRange pair's two accepted non-canonical forms ─
      // The canonical Static pair is the BARE {from, to} object (the `Range`
      // posture). Both shorthands below normalise to exactly that.
      { Id = "lenient-daterange-bare-array"
        LenientJson =
          """{"id":"len-dr-arr","kind":{"$type":"Form","fields":[{"id":"stay","kind":{"$type":"DateRange","value":["2026-03-01","2026-03-08"],"variant":"Date"},"label":"Stay","required":false}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Book"}}"""
        VerboseJson =
          """{"id":"len-dr-arr","kind":{"$type":"Form","fields":[{"id":"stay","kind":{"$type":"DateRange","value":{"from":"2026-03-01","to":"2026-03-08"},"variant":"Date"},"label":"Stay","required":false}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Book"}}"""
        Description =
          "Phase 725 — a DateRange Static pair may ride the [from, to] two-element array (the §3.6 bare-array coercion, mirroring `Range`); it normalises to the canonical bare {from, to} object" }

      { Id = "lenient-daterange-static-envelope"
        LenientJson =
          """{"id":"len-dr-env","kind":{"$type":"Form","fields":[{"id":"stay","kind":{"$type":"DateRange","value":{"$type":"Static","value":{"from":"2026-03-01","to":"2026-03-08"}},"variant":"Date"},"label":"Stay","required":false}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Book"}}"""
        VerboseJson =
          """{"id":"len-dr-env","kind":{"$type":"Form","fields":[{"id":"stay","kind":{"$type":"DateRange","value":{"from":"2026-03-01","to":"2026-03-08"},"variant":"Date"},"label":"Stay","required":false}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Book"}}"""
        Description =
          "Phase 725 — a DateRange Static pair wrapped in the explicit {\"$type\":\"Static\"} envelope stays decode-accepted (the `Range` read-compat posture); the bare {from, to} object is the canonical output" }

      // ─── Phase 750 — the declarative pill's three accepted shorthands ────
      //
      // `Pill` + `field`/`map` is the one that MATTERS. Before this phase that
      // document decoded happily as a closure `Pill` and threw `field` and `map`
      // on the floor: the author's whole intent vanished with no error anywhere.
      // Normalising it to `TonedPill` converts silent data loss into the shape
      // the author meant, and it is also the emission an unaided model reaches
      // for first — `Pill` is the word for the thing.
      { Id = "lenient-tonedpill-pill-tag"
        LenientJson =
          """{"id":"shipments","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"Pill","field":"status","map":{"Delayed":"Warning"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        VerboseJson =
          """{"id":"shipments","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","field":"status","map":{"Delayed":"Warning"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        Description =
          "Phase 750 — a `Pill` cell carrying `field` + `map` normalises to `TonedPill`; before this the declarative fields were silently DROPPED into a closure pill (the author's intent lost with no error)" }

      // `toneMap` / `tones` alias the terse canonical `map`. `map` is the shortest
      // honest name for a value→tone dictionary but the least descriptive one, and
      // the aliases cost nothing: the §16 layer already aliases `header`/`title`
      // onto a column's `label` for the same reason.
      { Id = "lenient-tonedpill-tonemap-alias"
        LenientJson =
          """{"id":"shipments","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","field":"status","toneMap":{"Delayed":"Warning"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        VerboseJson =
          """{"id":"shipments","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","field":"status","map":{"Delayed":"Warning"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        Description =
          "Phase 750 — `toneMap` (and `tones`) alias the canonical `map` on a TonedPill cell, the `header`/`title`→`label` field-alias device" }

      // The tone-map VALUES are a tone position like any other, so the Phase 460
      // tone aliases (Danger/Negative→Critical, Positive→Success, Neutral→Default)
      // apply inside the map. Pinned because "the aliases work in the new position
      // too" is the kind of claim that is true by construction until someone
      // hand-rolls a second tone reader.
      { Id = "lenient-tonedpill-tone-aliases"
        LenientJson =
          """{"id":"shipments","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","default":"Neutral","field":"status","map":{"Cancelled":"Danger","On time":"Positive"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        VerboseJson =
          """{"id":"shipments","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","field":"status","map":{"Cancelled":"Critical","On time":"Success"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        Description =
          "Phase 750 — the Phase 460 tone aliases apply inside a TonedPill `map` (Danger→Critical, Positive→Success) and in its `default` (Neutral→Default, which then omits)" } ]
