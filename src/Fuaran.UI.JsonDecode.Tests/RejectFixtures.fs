module Fuaran.UI.JsonDecode.Tests.RejectFixtures

// ============================================================================
//  Reject-fixture corpus (data form).
//
//  Each entry is a hand-authored bad-shape JSON blob that must fail decoding
//  with a specific `DecodeError` Code + Path prefix. They live in this
//  data table so a single source feeds BOTH the corpus emitter
//  (`Corpus.emit` writes one `reject/<id>.json` per entry + manifest
//  metadata) AND the reject-fixture test suite (which loads the corpus).
//
//  The `Id` doubles as the corpus filename stem (`reject/<id>.json`) and the
//  manifest `id`. The `ExpectedCode` / `ExpectedPath` become the manifest's
//  `expectedErrorCode` / `expectedPath` — the conformance contract a second
//  host (e.g. the Wave 9 TS decoder) asserts against without reading F#.
// ============================================================================

open Fuaran.UI.Ops.JsonDecode

type RejectFixture =
    {
        /// Corpus filename stem + manifest id.
        Id: string
        /// The malformed wire payload.
        Json: string
        /// Expected `DecodeError.Code` (manifest `expectedErrorCode`).
        ExpectedCode: DecodeErrorCode
        /// Expected `DecodeError.Path` prefix (manifest `expectedPath`).
        ExpectedPath: string
        /// `true` ⇒ decode via `decodeOp`; `false` ⇒ `decodeNode`.
        IsOp: bool
        /// Human-readable corpus/manifest description.
        Description: string
    }

let all: RejectFixture list =
    [
      // ─── INVALID_JSON ────────────────────────────────────────────────
      { Id = "reject-invalid-garbage"
        Json = "this is not json"
        ExpectedCode = DecodeErrorCode.INVALID_JSON
        ExpectedPath = "$"
        IsOp = false
        Description = "garbage input" }
      { Id = "reject-invalid-truncated"
        Json = "{\"id\":"
        ExpectedCode = DecodeErrorCode.INVALID_JSON
        ExpectedPath = "$"
        IsOp = false
        Description = "truncated object" }
      { Id = "reject-invalid-empty"
        Json = ""
        ExpectedCode = DecodeErrorCode.INVALID_JSON
        ExpectedPath = "$"
        IsOp = false
        Description = "empty string" }

      // ─── MISSING_FIELD ───────────────────────────────────────────────
      { Id = "reject-missing-node-id"
        Json =
          """{"kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.id"
        IsOp = false
        Description = "Node missing id" }
      { Id = "reject-missing-node-kind"
        Json = """{"id":"x","state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind"
        IsOp = false
        Description = "Node missing kind" }
      // Phase 460 retired `reject-missing-style-tone`: a `style` object missing
      // `tone` is now VALID (restore-on-absence to `ToneVariant.Default`, §3.6),
      // so the old reject fixture contradicts the shipped decoder. The read-compat
      // + omit-when-default behaviour is pinned by StyleOmitDefaultTests.fs.
      { Id = "reject-missing-markdown-text"
        Json =
          """{"id":"x","kind":{"$type":"Markdown"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.text"
        IsOp = false
        Description = "Markdown spec missing text" }
      { Id = "reject-missing-metric-value"
        Json =
          """{"id":"x","kind":{"$type":"Metric","label":{"$type":"Literal","text":"L"},"format":{"$type":"None"},"tone":"Default","weight":"Standard","emphasis":"Normal"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.value"
        IsOp = false
        Description = "Metric spec missing value" }
      { Id = "reject-missing-binding-type"
        Json =
          """{"id":"x","kind":{"$type":"Metric","label":{"$type":"Literal","text":"L"},"format":{"$type":"None"},"tone":"Default","weight":"Standard","emphasis":"Normal","value":{"value":1.0}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.value.$type"
        IsOp = false
        Description = "Binding missing $type" }
      { Id = "reject-missing-custom-moduleid"
        Json =
          """{"id":"x","kind":{"$type":"Custom","componentId":"c","props":{}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.moduleId"
        IsOp = false
        Description = "Custom missing moduleId" }
      // Switch (Phase 392) — each required field isolated (the decoder checks
      // stateKey, then cases, then default; duplicate `match` values are a
      // validator error FUARAN082, NOT a decode reject — first-match-wins keeps
      // decode structural, mirroring FragmentDecl name collisions).
      { Id = "reject-missing-switch-statekey"
        Json =
          """{"id":"x","kind":{"$type":"Switch","cases":[{"match":"a","child":{"id":"c","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"A"}}}}],"default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"D"}}}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.stateKey"
        IsOp = false
        Description = "Switch missing stateKey" }
      { Id = "reject-missing-switch-cases"
        Json =
          """{"id":"x","kind":{"$type":"Switch","stateKey":"v","default":{"id":"d","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"D"}}}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.cases"
        IsOp = false
        Description = "Switch missing cases" }
      { Id = "reject-missing-switch-default"
        Json =
          """{"id":"x","kind":{"$type":"Switch","stateKey":"v","cases":[{"match":"a","child":{"id":"c","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"A"}}}}]},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.default"
        IsOp = false
        Description = "Switch missing default Node" }
      { Id = "reject-missing-mount-scopeid"
        Json =
          """{"id":"x","kind":{"$type":"Mount","channel":{"direction":"OutOnly"},"capabilities":[],"onBubble":"<closure>"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.kind.scopeId"
        IsOp = false
        Description = "Mount missing scopeId (§4o)" }

      // ─── WRONG_TYPE ──────────────────────────────────────────────────
      { Id = "reject-wrongtype-id"
        Json =
          """{"id":42,"kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.id"
        IsOp = false
        Description = "Node id is integer not string" }
      { Id = "reject-wrongtype-kind"
        Json =
          """{"id":"x","kind":"Markdown","state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind"
        IsOp = false
        Description = "Node kind is a bare string not an object" }
      { Id = "reject-wrongtype-heading-level"
        Json =
          """{"id":"x","kind":{"$type":"Heading","level":"two","text":{"$type":"Literal","text":"H"},"variant":"Standard"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.level"
        IsOp = false
        Description = "Heading level is string not number" }
      { Id = "reject-wrongtype-style-tone"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":7,"weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.style.tone"
        IsOp = false
        Description = "Style.tone is integer not string" }
      { Id = "reject-wrongtype-box-children"
        Json =
          """{"id":"x","kind":{"$type":"Box","children":{},"layout":{"$type":"Auto"},"role":"Group"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.children"
        IsOp = false
        Description = "Box children is object not array" }
      { Id = "reject-wrongtype-discriminator"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":42,"text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.text.$type"
        IsOp = false
        Description = "Discriminator under Style is number" }
      { Id = "reject-wrongtype-static-sort-column"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"source":{"$type":"Static","value":[]},"staticRows":{"defaultSort":{"column":-1,"direction":"asc"},"headers":["A"],"rows":[["1"],["2"]]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.staticRows.defaultSort.column"
        IsOp = false
        Description =
          "staticRows defaultSort column -1 — a header index is non-negative; the schema says the same with minimum:0 (Phase 801)" }
      { Id = "reject-wrongtype-grid-default-sort-column"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"defaultSort":{"column":-1,"direction":"asc"},"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.defaultSort.column"
        IsOp = false
        Description =
          "bound-grid defaultSort column -1 — the same non-negative bound the staticRows spelling carries, same record, same message (Phase 861)" }
      // Phase 863 — the grid-behaviour family's NEAR MISSES. Each of these
      // decoded silently before: rule 2 tolerates unknown keys, so a model that
      // reached for the wrong name got a tree that rendered while the
      // declaration did nothing. The didactic names the canonical form.
      { Id = "reject-nearmiss-grid-current-page"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"currentPage":1,"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.currentPage"
        IsOp = false
        Description =
          "grid 'currentPage' — the sharpest near miss: a LITERAL page number is not expressible, the position lives in State so a pager can move it (Phase 863)" }
      { Id = "reject-nearmiss-grid-sortable"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"sortable":true,"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.sortable"
        IsOp = false
        Description =
          "grid-level 'sortable' — the staticRows spelling reached for on the bound path, where sortability is sortStateKey + per-column narrowing (Phase 863)" }
      { Id = "reject-nearmiss-column-readonly"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[{"field":"note","kind":{"$type":"Text"},"label":"Note","readOnly":true}],"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.columns[0].readOnly"
        IsOp = false
        Description =
          "column 'readOnly' — named by the census row itself; deliberately NOT aliased to editable:false, because an inverting alias that guesses wrong makes a read-only column editable (Phase 863)" }
      { Id = "reject-nearmiss-grid-behaviour-record"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","behaviour":{"page":{}},"columns":[],"source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.behaviour"
        IsOp = false
        Description =
          "grid 'behaviour' record — charter option O1-C, rejected by design: three optional sibling fields, not a nested record (Phase 860 charter / 863)" }
      { Id = "reject-wrongtype-grid-page-size-zero"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"pageSize":0,"pageStateKey":"p","source":{"$type":"Static","value":[]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.pageSize"
        IsOp = false
        Description =
          "grid pageSize 0 — a page of no rows names no page; the schema says the same with minimum:1 (Phase 862)" }

      // ─── UNKNOWN_DU_CASE ─────────────────────────────────────────────
      { Id = "reject-unknown-tone"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Magenta","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.style.tone"
        IsOp = false
        Description = "ToneVariant value 'Magenta'" }
      // Phase 1073 — the RESERVED case. Phase 867 admitted `trendPolarity` as a
      // two-case enum with `Neutral` reserved-not-admitted and authored no reject
      // vector, so five hosts each decided the question in their own test suite
      // rather than against the corpus. They happened to agree; this fixture is
      // what makes the agreement ARBITRATED rather than coincidental, and is what
      // a sixth host will be measured against. The path is the bare slot, per §6
      // and the Phase 1073 ruling: a bare enum carries no discriminator on the
      // wire, so there is no `.$type` to name.
      { Id = "reject-unknown-trend-polarity"
        Json =
          """{"id":"m","kind":{"$type":"Metric","label":"Avg wait","trend":{"$type":"Static","value":-0.0734},"trendPolarity":"Neutral","value":{"$type":"Static","value":80}}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.trendPolarity"
        IsOp = false
        Description =
          "TrendPolarity value 'Neutral' — RESERVED, not admitted (§3.6.1 clause 5); refused exactly like a name nobody has proposed, so a later admission is an ADDITION rather than a re-meaning of shipped bytes" }
      { Id = "reject-unknown-binding"
        Json =
          """{"id":"x","kind":{"$type":"Metric","label":{"$type":"Literal","text":"L"},"format":{"$type":"None"},"tone":"Default","weight":"Standard","emphasis":"Normal","value":{"$type":"Bogus"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.value.$type"
        IsOp = false
        Description = "Binding $type 'Bogus'" }
      { Id = "reject-unknown-textsource"
        Json =
          """{"id":"x","kind":{"$type":"Markdown","text":{"$type":"TemplateString","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.text.$type"
        IsOp = false
        Description = "TextSource $type 'TemplateString'" }
      { Id = "reject-unknown-mount-direction"
        Json =
          """{"id":"x","kind":{"$type":"Mount","scopeId":"g","channel":{"direction":"Sideways"},"capabilities":[],"onBubble":"<closure>"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.channel.direction"
        IsOp = false
        Description = "Mount ChannelDirection value 'Sideways' (§4o)" }
      { Id = "reject-unknown-drawing-shape"
        Json =
          """{"id":"x","kind":{"$type":"Drawing","shapes":[{"$type":"Blob","style":{}}],"style":{},"viewBox":{"height":100,"minX":0,"minY":0,"width":100}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.shapes[0].$type"
        IsOp = false
        Description = "Drawing Shape $type 'Blob' — the closed-shape default-deny (Phase 524)" }
      { Id = "reject-unknown-drawing-curve-command"
        Json =
          """{"id":"x","kind":{"$type":"Drawing","shapes":[{"$type":"Curve","commands":[{"$type":"ArcTo"}],"style":{}}],"style":{},"viewBox":{"height":100,"minX":0,"minY":0,"width":100}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.shapes[0].commands[0].$type"
        IsOp = false
        Description = "Drawing CurveCommand $type 'ArcTo' — typed command list default-deny (Phase 524)" }
      { Id = "reject-unknown-link-protection"
        Json =
          """{"id":"x","kind":{"$type":"Link","download":false,"href":{"$type":"Static","value":"mailto:a@example.com"},"label":{"$type":"Literal","text":"Email"},"protection":"rot13"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.protection"
        IsOp = false
        Description = "LinkProtection value 'rot13' — the closed protection DU default-denies (Phase 812)" }
      // fuaran#815 / Phase 822 — the leniency's own boundary: a State wrapper
      // carrying only a key has no defaultValue/value to unwrap, so
      // `normaliseTransformSource` leaves it untouched and Core's columnar
      // codec refuses it (surfaced through the `coreError` wrap — WRONG_TYPE,
      // no ExpectedShape; see `coreWrappedHintlessRejects`).
      { Id = "reject-transform-source-empty-wrapper"
        Json =
          """{"id":"rej-tf-empty-wrapper","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"Dept"}],"rowKeyField":"dept","source":{"$type":"Transform","pipeline":[],"source":{"$type":"State","key":"rows"}}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.source.source"
        IsOp = false
        Description =
          "fuaran#815 — a State wrapper carrying only a key (no defaultValue/value) is NOT unwrappable: the Transform source stays a binding object and the columnar decode refuses it" }
      { Id = "reject-unknown-static-sort-direction"
        Json =
          """{"id":"x","kind":{"$type":"DataGrid","columns":[],"source":{"$type":"Static","value":[]},"staticRows":{"defaultSort":{"column":0,"direction":"sideways"},"headers":["A"],"rows":[["1"],["2"]]}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.staticRows.defaultSort.direction"
        IsOp = false
        Description =
          "staticRows defaultSort direction 'sideways' — the closed asc|desc pair default-denies (Phase 801)" }

      // ─── Phase 818 — SetState value XOR valueFrom ────────────────────
      { Id = "reject-setstate-value-and-valuefrom"
        Json =
          """{"id":"b-both","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"SetState","key":"chosen","value":"literal","valueFrom":{"$type":"State","key":"other"}},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.valueFrom"
        IsOp = false
        Description =
          "SetState carrying BOTH 'value' and 'valueFrom' — exactly one is allowed; the didactic names both fields and how each is used (Phase 818)" }

      // ─── WRONG_NODE_KIND ─────────────────────────────────────────────
      { Id = "reject-wrongnodekind-widget"
        Json =
          """{"id":"x","kind":{"$type":"Widget"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_NODE_KIND
        ExpectedPath = "$.kind.$type"
        IsOp = false
        Description = "Top-level kind 'Widget' is not a recognised node kind" }
      { Id = "reject-wrongnodekind-sparkler"
        Json =
          """{"id":"x","kind":{"$type":"Sparkler"},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_NODE_KIND
        ExpectedPath = "$.kind.$type"
        IsOp = false
        Description =
          "Plausible-but-invalid kind 'Sparkler' — on the flat wire an unknown primitive is WRONG_NODE_KIND, not a nested DisplayKind miss" }

      // ─── EMPTY_NODE_ID ───────────────────────────────────────────────
      { Id = "reject-emptynodeid"
        Json =
          """{"id":"","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""
        ExpectedCode = DecodeErrorCode.EMPTY_NODE_ID
        ExpectedPath = "$.id"
        IsOp = false
        Description = "Empty top-level id" }

      // ─── TreeOp rejects ──────────────────────────────────────────────
      { Id = "reject-op-unknown-yeet"
        Json = """{"$type":"Yeet","target":"x"}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.$type"
        IsOp = true
        Description = "Unknown op discriminator 'Yeet'" }
      { Id = "reject-op-updateprop-missing-path"
        Json = """{"$type":"UpdateProp","target":"x","value":1}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.path"
        IsOp = true
        Description = "UpdateProp missing path" }
      { Id = "reject-op-insertchild-missing-parentid"
        Json =
          """{"$type":"InsertChild","child":{"id":"new","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.parentId"
        IsOp = true
        Description = "InsertChild missing parentId" }
      { Id = "reject-op-reorderchildren-wrongtype"
        Json = """{"$type":"ReorderChildren","parentId":"x","newOrder":"abc"}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.newOrder"
        IsOp = true
        Description = "ReorderChildren newOrder is string not array" }
      { Id = "reject-op-missing-type"
        Json = """{"target":"x"}"""
        ExpectedCode = DecodeErrorCode.MISSING_FIELD
        ExpectedPath = "$.$type"
        IsOp = true
        Description = "Op missing $type entirely" }
      { Id = "reject-op-insertchild-empty-childid"
        Json =
          """{"$type":"InsertChild","parentId":"x","child":{"id":"","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.EMPTY_NODE_ID
        ExpectedPath = "$.child.id"
        IsOp = true
        Description = "InsertChild child carries empty id" }

      // ─── the RETIRED positional slot (Phase 687) ──────────────────────────
      //
      // Phase 681 removed `position` / `newPosition`; 681–686 left every
      // decoder accepting and ignoring it so the hosts could adopt
      // independently. These two fixtures are the window's close: the field is
      // now refused BY NAME, which is the only way to close it — the tolerance
      // was silence (these decoders read named fields and ignore the rest), so
      // there was never a read to delete.
      //
      // Both payloads are otherwise WELL-FORMED, deliberately. A fixture that
      // also lacked a required field would pass whether the host checked the
      // retired name first or merely happened to fail earlier, and would
      // certify nothing about the refusal.
      { Id = "reject-op-insertchild-retired-position"
        Json =
          """{"$type":"InsertChild","parentId":"x","position":0,"child":{"id":"new","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.position"
        IsOp = true
        Description = "InsertChild carries the retired 'position' — the migration window is closed (Phase 687)" }
      { Id = "reject-op-movenode-retired-newposition"
        Json = """{"$type":"MoveNode","newParentId":"q","newPosition":2,"target":"n"}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.newPosition"
        IsOp = true
        Description = "MoveNode carries the retired 'newPosition' — the migration window is closed (Phase 687)" }

      // ─── null in structured JVal positions (rule 12: no null on the wire) ──
      //
      // The wire model has no null — a JSON null in any structured payload
      // position rejects as WRONG_TYPE at the null's exact path. Before these
      // fixtures the rule lived only in per-host unit tests, so a host could
      // silently accept nulls while passing full corpus certification.
      { Id = "reject-null-custom-prop"
        Json =
          """{"id":"c1","kind":{"$type":"Custom","componentId":"trend-card","moduleId":"analytics","props":{"k":null}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.props.k"
        IsOp = false
        Description = "null Custom prop value (structured JVal position)" }
      { Id = "reject-null-updateprop-value"
        Json = """{"$type":"UpdateProp","path":"Text","target":"m","value":null}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.value"
        IsOp = true
        Description = "null UpdateProp value (structured JVal position)" }
      { Id = "reject-null-action-setstate-value"
        Json =
          """{"id":"b1","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"SetState","key":"open","value":null},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.value"
        IsOp = false
        Description = "null Action.SetState value (structured JVal position)" }
      { Id = "reject-null-action-notify-payload"
        Json =
          """{"id":"b2","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"Notify","channel":"toast","payload":null},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.payload"
        IsOp = false
        Description = "null Action.Notify payload (structured JVal position)" }
      { Id = "reject-null-action-aitool-args"
        Json =
          """{"id":"b3","kind":{"$type":"Button","label":{"$type":"Literal","text":"Go"},"onClick":{"$type":"AiTool","args":null,"toolName":"fuaran.getNodeState"},"variant":"Primary"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.onClick.args"
        IsOp = false
        Description = "null Action.AiTool args (structured JVal position)" }
      { Id = "reject-null-i18n-arg"
        Json =
          """{"id":"m1","kind":{"$type":"Markdown","text":{"$type":"I18n","args":{"name":null},"key":"greeting"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.text.args.name"
        IsOp = false
        Description = "null I18n arg value (structured JVal position)" }

      // ─── DateRange ordering (Phase 725) ─────────────────────────────────
      //
      // A LITERAL date-range pair is ordered: `from <= to`. Same-variant
      // ISO-8601 strings sort lexicographically in chronological order, so the
      // check is an ordinal compare — no date parsing, no locale, total for
      // every variant. Didactic by design: the message names the rule and the
      // fix, because a reversed pair is the natural first mistake for a control
      // whose two ends look interchangeable.
      { Id = "reject-daterange-unordered"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"stay","kind":{"$type":"DateRange","value":{"from":"2026-03-08","to":"2026-03-01"},"variant":"Date"},"label":"Stay","required":false}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Book"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].kind.value"
        IsOp = false
        Description = "DateRange literal pair with start after end — the ordered-pair rule (Phase 725)" }

      // ─── TonedPill tone-map values (Phase 750) ───────────────────────────
      //
      // The declarative pill's `map` VALUES are `ToneVariant`s, and a tone name
      // is the one part of the shape an author has to know rather than infer:
      // "Urgent" / "Red" / "Error" are all plausible-sounding and all wrong. The
      // reject is deliberately routed through the same `decodeTone` every other
      // tone position uses, so the message enumerates the seven legal names — an
      // author who guesses gets the answer, not just a refusal.
      { Id = "reject-tonedpill-unknown-tone"
        Json =
          """{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"status","kind":{"$type":"TonedPill","field":"status","map":{"Delayed":"Urgent"}},"label":"Status"}],"rowKeyField":"status","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"status":{"validity":[true],"values":["Delayed"]}},"schema":[{"name":"status","type":"string"}]}}}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.kind.columns[0].kind.map.Delayed"
        IsOp = false
        Description =
          "TonedPill tone-map value outside ToneVariant — the message names the seven legal tones (Phase 750)" }

      // ─── FieldRule well-formedness (Phase 864) ───────────────────────────
      //
      // Three refusals, and each is a relation BETWEEN slots rather than a
      // shape, which is why none of them is expressible in the IDL and all
      // three live in the policy decoder beside the DateRange `from <= to`
      // check above.
      //
      // (1) A rule with every slot absent. A rule that constrains nothing is a
      // defect and not a no-op: it decodes, validates and renders while
      // declaring nothing, so the author believes a constraint is in force and
      // the field accepts anything. `message` alone does not rescue it — the
      // message is the prose shown when some OTHER slot is unmet, so a
      // message-only rule is precisely the help-text failure this phase exists
      // to fix, wearing the new vocabulary's clothes.
      { Id = "reject-fieldrule-empty"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text"},"label":"Work email","required":true,"rule":{"message":"Must be a valid email"}}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].rule"
        IsOp = false
        Description = "FieldRule declaring no constraint — a message alone is not a rule (Phase 864)" }

      // (2) An inverted length pair. The DateRange ordered-pair rule above,
      // applied to a length bound: `minLength` over `maxLength` admits no value
      // at all, so the field can never be submitted and the form is dead on
      // arrival. Same didactic posture — the message names the two numbers and
      // says what the inversion costs.
      { Id = "reject-fieldrule-length-unordered"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"username","kind":{"$type":"Text"},"label":"Username","required":true,"rule":{"minLength":24,"maxLength":3}}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].rule"
        IsOp = false
        Description = "FieldRule minLength above maxLength — the ordered-pair rule on a length pair (Phase 864)" }

      // (3) The near miss. Rule 2's tolerance of unknown keys is right for a
      // field a future profile may add and wrong for a near miss of one that
      // exists: the tree decodes and renders while the constraint does nothing,
      // and silence is worse here than anywhere else in the vocabulary, because
      // the failure this phase exists to fix is authors putting the rule
      // somewhere a host cannot act on. `validation` / `constraints` /
      // `validate` are refused by name and pointed at `rule`.
      { Id = "reject-formfield-near-miss-validation"
        Json =
          """{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text"},"label":"Work email","required":true,"validation":{"format":"email"}}],"onSubmit":{"$type":"Dispatch"},"submitLabel":"Save"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fields[0].validation"
        IsOp = false
        Description = "'validation' near-miss on a FormField — the canonical key is 'rule' (Phase 864)" }

      // ─── Accessibility trait (Phase 955) ─────────────────────────────────
      //
      // Enumerated against the trait's DECLARATION in WIRE_FORMAT §3.1 — one
      // vector per shape the six slots can be malformed in — rather than
      // against any host's current behaviour, which is the whole reason the
      // family exists: two hosts read `label` as a bare string only, and their
      // own fixtures authored that shape, so nothing was red anywhere.
      //
      // The pair that carries the most weight is the last two. §3.1's 2026-08-25
      // ruling makes the trait's `Binding` slots ordinary `Binding` slots, so
      // the §3.6 bare-scalar coercion applies — `"hidden": true` is legal
      // shorthand for `{"$type":"Static","value":true}`. That leniency is about
      // SHAPE and not about TYPE, and the distinction is invisible until
      // something pins it: `"hidden": "yes"` takes the same lenient arm and must
      // still be refused, because a `Binding<bool>` slot's Static parser is
      // `requireBool`. Without this vector a host could implement "any scalar
      // becomes Static" and pass every fixture in the corpus.

      // (1) The closed token set. `liveRegion` is one of exactly three strings;
      // `aria-live`'s real HTML vocabulary is the same three, so a near miss
      // here is an author reaching for a token that does not exist rather than a
      // typo, and UNKNOWN_DU_CASE (which names the legal set) is the didactic
      // refusal rather than a bare WRONG_TYPE.
      { Id = "reject-a11y-liveregion-unknown"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"liveRegion":"urgent"}}"""
        ExpectedCode = DecodeErrorCode.UNKNOWN_DU_CASE
        ExpectedPath = "$.accessibility.liveRegion"
        IsOp = false
        Description =
          "accessibility.liveRegion outside the closed set — the message names polite | assertive | off (Phase 955)" }

      // (2) The same slot, wrong JSON kind. Distinct from (1) on purpose: a
      // non-string is not a near miss of a token, so it is WRONG_TYPE and not
      // UNKNOWN_DU_CASE, and a host that collapses the two loses the difference
      // between "no such token" and "not a token at all".
      { Id = "reject-a11y-liveregion-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"liveRegion":true}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.liveRegion"
        IsOp = false
        Description = "accessibility.liveRegion not a JSON string (Phase 955)" }

      // (3) `role` is the OPEN slot — any string decodes, as `AriaRole.Custom`
      // verbatim — so a non-string is the only way to malform it, and pinning
      // that is what stops a host from stringifying a number and inventing a
      // role the author never wrote.
      { Id = "reject-a11y-role-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"role":42}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.role"
        IsOp = false
        Description =
          "accessibility.role not a JSON string — the slot is open to any role NAME, not to any value (Phase 955)" }

      // (4) The NodeId-reference slots. One vector covers the class: `labelledBy`
      // and `describedBy` are the same decoder at two keys.
      { Id = "reject-a11y-labelledby-nonstring"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"labelledBy":["h1"]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.labelledBy"
        IsOp = false
        Description = "accessibility.labelledBy not a NodeId string (Phase 955)" }

      // (5) The trait itself. `accessibility` is an object or absent; a bare
      // string there is the shape an author reaches for when they think the slot
      // IS the label ("accessibility": "Home"), which is why it earns a vector of
      // its own rather than being left to the per-slot cases above.
      { Id = "reject-a11y-not-object"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":"Home"}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility"
        IsOp = false
        Description = "accessibility is not an object — the trait is a record, not a bare label (Phase 955)" }

      // (6) The lenient-shape / strict-type boundary, per the preamble above.
      { Id = "reject-a11y-hidden-nonbool"
        Json =
          """{"id":"n1","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"accessibility":{"hidden":"yes"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.accessibility.hidden"
        IsOp = false
        Description =
          "accessibility.hidden bare scalar of the wrong type — §3.6 leniency is about SHAPE, not type (Phase 955)" }

      // ─── The NUMERIC `Binding<'T>` slots (Phase 1064) ─────────────────────
      //
      //  Phase 955/956 closed the SCALAR half of a general defect: five hosts
      //  named a typed `Binding` decoder at every string / bool slot and checked
      //  nothing, so `{"href": 7}` decoded. The fix landed at the mechanism, and
      //  the six a11y vectors above are what pins it. The NUMERIC positions the
      //  reference host types the same way — `decodeBindingFloat` at
      //  `Metric.value` / `Metric.trend` / `LabelValueRow.value` /
      //  `Progress.fraction` / `Drawing.strokeWidth` / `Drawing.opacity` / the
      //  Number + RangedNumber control values, and `decodeBindingInt` at
      //  `Tabs.activeIndex` / `Stepper.activeStep` — had NO fixture at all, so a
      //  host tightening them had nothing to be measured against.
      //
      //  The family below is enumerated against §7's accept sets, not against
      //  any host's current behaviour, and the enumeration is the point:
      //
      //    a FLOAT slot accepts  { JSON number } ∪ { "NaN", "Infinity", "-Infinity" }
      //    an INT   slot accepts { JSON number }                     (§7, truncating)
      //
      //  So "a string at a numeric slot rejects" is FALSE at a float slot and
      //  TRUE at an int slot, and a vector written the naive way would have
      //  contradicted the three §5/§7 sentinel ACCEPT fixtures Phase 1063 landed
      //  (`drawing-nonfinite-sentinels`, `spark-nonfinite-sentinel`,
      //  `metric-nonfinite-sentinel`) on the same corpus days earlier. Each
      //  vector below is therefore a MEMBER OF THE COMPLEMENT of one of those two
      //  sets — the wrong JSON kind, a string outside the sentinel set, a
      //  correctly-spelled sentinel at the slot class that has none, and a
      //  sentinel whose CASE is wrong.
      //
      //  Both `Binding` shapes are covered on purpose: the `Static` envelope and
      //  the §3.6 bare scalar reach the slot's parser by different arms, and a
      //  host that types one and not the other passes half a family.

      // (1) FLOAT — a string outside the sentinel set, behind the `Static`
      // envelope. The plainest statement of the boundary §7 draws: the accept
      // set is three named tokens, not "strings that might parse".
      { Id = "reject-binding-float-string"
        Json =
          """{"id":"n1","kind":{"$type":"Metric","format":{"$type":"None"},"label":{"$type":"Literal","text":"L"},"value":{"$type":"Static","value":"lots"}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.value"
        IsOp = false
        Description =
          "Binding<float> Static payload is a non-sentinel string — §7's float accept set is JSON number plus exactly \"NaN\" / \"Infinity\" / \"-Infinity\" (Phase 1064)" }

      // (2) FLOAT — a sentinel spelled in the wrong CASE, arriving through the
      // §3.6 bare-scalar arm. The sharpest vector in the family: it is one
      // `ToLowerInvariant` away from a fixture that must ACCEPT, so a host that
      // case-folds the sentinel comparison passes (1) and fails here. It also
      // proves the bare-scalar coercion routes through the slot's own parser
      // rather than round-tripping the scalar untouched.
      { Id = "reject-binding-float-sentinel-case"
        Json = """{"id":"n1","kind":{"$type":"Progress","fraction":"nan"}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.fraction"
        IsOp = false
        Description =
          "Binding<float> bare scalar is a MIS-CASED sentinel (\"nan\") — §7's three tokens are exact, and a case-folding host would accept a document no encoder emits (Phase 1064)" }

      // (3) FLOAT — the wrong JSON kind entirely. A bool is not coercible to a
      // number under any rule in §7, and it is the value a host's untyped
      // pass-through would carry straight into a numeric render slot.
      { Id = "reject-binding-float-bool"
        Json =
          """{"id":"n1","kind":{"$type":"Metric","format":{"$type":"None"},"label":{"$type":"Literal","text":"L"},"trend":{"$type":"Static","value":true},"value":{"$type":"Static","value":1.0}}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.trend"
        IsOp = false
        Description =
          "Binding<float> Static payload is a JSON boolean — a second float slot on the same spec, so a host guarding `value` and not `trend` is caught (Phase 1064)" }

      // (4) INT — the wrong JSON kind, at the slot whose §3.6 bare-scalar arm the
      // lenient profile explicitly cites (`activeIndex: 1`). The leniency admits
      // the SHAPE; the slot's `Binding<int>` still governs the value.
      { Id = "reject-binding-int-bool"
        Json =
          """{"id":"n1","kind":{"$type":"Tabs","activeIndex":{"$type":"Static","value":true},"children":[{"id":"m1","kind":{"$type":"Markdown","text":"x"}}]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.activeIndex"
        IsOp = false
        Description =
          "Binding<int> Static payload is a JSON boolean — §7 gives an integer slot no non-numeric form (Phase 1064)" }

      // (5) INT — a CORRECTLY-spelled non-finite sentinel at an integer slot.
      // The vector that makes the two accept sets distinguishable rather than
      // merely stated: §7 confines the sentinels to a float slot, an integer slot
      // truncates a parsed number and has no representation for NaN at all, and
      // Phase 1063's published schema widened only the float slot for exactly
      // this reason. A host that implements "accept the three tokens wherever a
      // number is expected" passes every fixture in the corpus except this one.
      { Id = "reject-binding-int-sentinel-string"
        Json =
          """{"id":"n1","kind":{"$type":"Stepper","activeStep":{"$type":"Static","value":"NaN"},"children":[{"id":"m1","kind":{"$type":"Markdown","text":"x"}}]}}"""
        ExpectedCode = DecodeErrorCode.WRONG_TYPE
        ExpectedPath = "$.kind.activeStep"
        IsOp = false
        Description =
          "Binding<int> Static payload is a §7 non-finite sentinel — the sentinels are FLOAT-slot only; an integer slot has no non-finite form (Phase 1064)" } ]
