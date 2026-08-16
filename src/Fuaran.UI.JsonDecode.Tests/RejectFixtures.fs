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
          """{"$type":"InsertChild","position":0,"child":{"id":"new","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
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
          """{"$type":"InsertChild","parentId":"x","position":0,"child":{"id":"","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}}"""
        ExpectedCode = DecodeErrorCode.EMPTY_NODE_ID
        ExpectedPath = "$.child.id"
        IsOp = true
        Description = "InsertChild child carries empty id" }

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
          "TonedPill tone-map value outside ToneVariant — the message names the seven legal tones (Phase 750)" } ]
