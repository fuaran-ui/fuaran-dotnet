module Fuaran.UI.SlotCapability

// ============================================================================
//  The slot capability table (Phase 430) — the declared posture of every
//  closure-bearing wire slot (`WIRE_FORMAT.md` §4).
//
//  The 2026-07-06 decoded-path investigation found one principle violated in
//  five places: a closure was the FLOOR rather than an override, so decoded
//  trees silently shipped dead controls and blank displays. Phases 423–428
//  fixed the instances; this table makes the class un-regressable — every
//  closure-bearing slot declares whether it has a declarative default (and
//  which kind), or is a sanctioned host-only escape. The dead-on-decode lint
//  (`DeadOnDecode.lint`) drives off the postures here, and the completeness
//  test asserts the table covers every slot the decoders reconstruct as a
//  placeholder — a new closure-bearing spec field MUST add its row (the
//  `stateKeysOfBinding` forward-coupling discipline applied to postures), or
//  the completeness test fails.
// ============================================================================

/// The declarative posture of a closure-bearing slot.
[<RequireQualifiedAccess>]
type SlotPosture =
    /// An omitted handler writes the change back to the control's own
    /// writable value binding (`Binding.State` / `Binding.Filter`) — the
    /// Phase 423/426/427 write-back default. The closure is an override.
    | WriteBack
    /// The closure accessor has a declarative field-name twin (`field` /
    /// `rowKeyField`, Phase 425) — the decoded form projects a named row
    /// property. The closure is an override.
    | FieldName
    /// The closure continuation has a declarative result target
    /// (`Call … into State/Query`, Phase 428). The closure is an override.
    | ResultTarget
    /// Closure-only BY DESIGN — a sanctioned host escape with no declarative
    /// twin. Carries the doc rationale so the lint knows silence here is
    /// intentional, not a regression.
    | HostOnlyByDesign of rationale: string

/// One row: a closure-bearing wire slot (the §4 vocabulary, spelled
/// `Spec.slot`), its posture, and the phase that fixed / sanctioned it.
type SlotCapability =
    { Slot: string
      Posture: SlotPosture
      Phase: string }

let private row slot posture phase =
    { Slot = slot
      Posture = posture
      Phase = phase }

/// The full table — one row per closure-bearing slot in `WIRE_FORMAT.md` §4.
let all: SlotCapability list =
    [ // ── Write-back defaults (the 423/426/427 family) ──
      row "FilterKind.onChange" SlotPosture.WriteBack "423"
      row "FormFieldKind.onChange" SlotPosture.WriteBack "426"
      row "FormFieldKind.onToggle" SlotPosture.WriteBack "426"
      row "SelectSpec.onChange" SlotPosture.WriteBack "426"
      row "SelectSpec.onChangeMulti" SlotPosture.WriteBack "426"
      row "TabsSpec.onSelect" SlotPosture.WriteBack "426"
      row "TabsSpec.onSelectTag" SlotPosture.WriteBack "426"
      row "DisclosureSpec.onToggle" SlotPosture.WriteBack "426"
      row "GridSpec.onRowClick" SlotPosture.WriteBack "427"
      // Phase 933 extended the 427 write default from `DataGrid` to `Chart`: a
      // chart whose `onPointClick` is omitted publishes the clicked datum under
      // its OWN NodeId. So this slot stopped being a sanctioned escape and
      // became an override, exactly as `GridSpec.onRowClick` is — the row it
      // now mirrors. It read `HostOnlyByDesign "point-click selection semantics
      // unshipped"` until Phase 924's sweep, which is why a decoded chart
      // carrying the sentinel was the one dead affordance the lint beside this
      // table stayed silent about.
      row "ChartSpec.onPointClick" SlotPosture.WriteBack "933"

      // ── Field-name display floors (425) ──
      row "ColumnErased.value" SlotPosture.FieldName "425"
      row "GridSpec.rowKey" SlotPosture.FieldName "425"

      // ── Result targets (428) ──
      row "Action.Call.onResult" SlotPosture.ResultTarget "428"

      // ── Identity-projection accessors (data-preserving on decode; no lint) ──
      row
          "Binding.Query.accessor"
          (SlotPosture.HostOnlyByDesign
              "decodes to an identity projection (421) — the host-fed / Call-written queryResults value flows through; the typed projection is an F#-author refinement")
          "421"
      row
          "Binding.Selection.accessor"
          (SlotPosture.HostOnlyByDesign
              "decodes to an identity projection (427) — the store-written row flows through; the typed projection is an F#-author refinement")
          "427"

      // ── Sanctioned host-only escapes (silence is intentional) ──
      row
          "Action.Dispatch.msg"
          (SlotPosture.HostOnlyByDesign
              "the typed 'Msg payload is the F#-author dispatch channel; AI authors use the substrate actions (SetState / Call / Notify / …)")
          "—"
      row
          "Action.ReadFileBody.onRead"
          (SlotPosture.HostOnlyByDesign
              "the continuation feeds a browser-held blob read (136); the AI-facing ingestion path is a host tool, not a tree closure")
          "136"
      row
          "FileUploadSpec.onSelect"
          (SlotPosture.HostOnlyByDesign
              "non-scalar payload (FileSelection list) — explicitly out of the 426 write-back scope; host wiring required")
          "426"
      row
          "StepperSpec.onSelect"
          (SlotPosture.HostOnlyByDesign
              "mirrors Tabs but the ActiveStep write-back default has not shipped — a candidate follow-up, tracked as host-only until then")
          "—"
      row
          "MapSpec.onMarkerClick"
          (SlotPosture.HostOnlyByDesign "marker-click selection semantics unshipped; host wiring required")
          "—"
      row
          "CellKindErased.handlers"
          (SlotPosture.HostOnlyByDesign
              "interactive-cell handlers (onEdit / onToggle / onClick / get / labelFn / hrefFn / fractionFn / fn) mutate or project HOST data — 425 scoped cell mutation out of the declarative floor. Phase 750 removed `toneFn` from this list: `CellKindErased.TonedPill` is a declarative twin of the whole `Pill` case (field + value→tone map), so a value-conditional tone is now floor, not escape — the closure Pill survives as the override")
          "425"
      row
          "Binding.Computed.fn"
          (SlotPosture.HostOnlyByDesign
              "the F#-only computed escape; AI authors use State / Filter / Transform / Format bindings")
          "—"
      row
          "Binding.Local.onCommit"
          (SlotPosture.HostOnlyByDesign "the Phase 62 buffered-commit pipeline — consumer-side by construction")
          "62"
      row
          "Binding.Local.format"
          (SlotPosture.HostOnlyByDesign "consumer-side display formatter (62); Binding.Format is the declarative twin")
          "62"
      row "Binding.Local.parse" (SlotPosture.HostOnlyByDesign "consumer-side reverse parser (62)") "62"
      row
          "StateBehaviour.onError"
          (SlotPosture.HostOnlyByDesign
              "the ErrorPayload → Node callback is host render policy. It is STILL host-only, but the decoded consequence recorded here was wrong until Phase 924's sweep read the decoder: a decoded `onError` is a placeholder node whose text is the `<closure>` sentinel, NOT a fallback to a default error surface. `AffordanceInertness` carries the corrected verdict")
          "—"
      row
          "CellFormat.Custom.fn"
          (SlotPosture.HostOnlyByDesign "arbitrary format function — the typed CellFormat cases are the declarative set")
          "—"
      row
          "MountSpec.onBubble"
          (SlotPosture.HostOnlyByDesign "the guest→host bubble channel is host composition (§4o)")
          "—" ]

/// The slots the decoders reconstruct as inert/identity placeholders — the
/// `WIRE_FORMAT.md` §4 enumeration, spelled with the same `Spec.slot` names as
/// the table. The completeness test asserts `all` covers every entry (and a
/// new §4 placeholder must be added HERE + get a row, or the test fails —
/// the seam that keeps the class from silently growing).
let decoderPlaceholderSlots: string list =
    [ "Action.Dispatch.msg"
      "Action.Call.onResult"
      "Action.ReadFileBody.onRead"
      "FormFieldKind.onChange"
      "FormFieldKind.onToggle"
      "SelectSpec.onChange"
      "SelectSpec.onChangeMulti"
      "FileUploadSpec.onSelect"
      "TabsSpec.onSelect"
      "TabsSpec.onSelectTag"
      "StepperSpec.onSelect"
      "DisclosureSpec.onToggle"
      "FilterKind.onChange"
      "CellKindErased.handlers"
      "GridSpec.onRowClick"
      "ChartSpec.onPointClick"
      "MapSpec.onMarkerClick"
      "Binding.Query.accessor"
      "Binding.Selection.accessor"
      "Binding.Computed.fn"
      "ColumnErased.value"
      "GridSpec.rowKey"
      "Binding.Local.onCommit"
      "Binding.Local.format"
      "Binding.Local.parse"
      "StateBehaviour.onError"
      "CellFormat.Custom.fn"
      "MountSpec.onBubble" ]
