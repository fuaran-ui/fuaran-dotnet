module Fuaran.UI.WireSurvivability

// ============================================================================
//  The wire-survivability boundary (Phase 378) — named in code.
//
//  `Binding.Computed`, typed accessors, and callbacks erase to the `"<closure>"`
//  sentinel on the wire (and non-enumerated `Binding.Static` payloads to
//  `"<opaque>"`) — invisible to op-stream replay, structural diffing, AiTools
//  introspection, and the TypeScript / Python hosts. The boundary between
//  wire-survivable and host-only constructs already EXISTS (`WIRE_FORMAT.md`
//  §4/§10 define the sentinels; `SlotCapability` (Phase 430) declares each
//  closure SLOT's posture) — but nothing NAMED it across the whole author-facing
//  vocabulary where authors and orchestrators look.
//
//  This module is that name: a whole-vocabulary verdict for every author-facing
//  DU case. It is the single authoritative classification the survivability
//  table in `WIRE_FORMAT.md`, the `AI_AUTHORING_GUIDE.md` boundary section, and
//  the `Fuaran.UI.Validator` `WireSurvivabilityCheck` are projections of, and
//  the `WireSurvivabilityCoverage` test asserts it covers every union case — a
//  new DU case cannot ship unclassified (the forward-coupling seam, mirroring
//  `SlotCapability`'s completeness test).
//
//  Verdict semantics:
//    Survivable — round-trips value-faithfully through the canonical JSON wire.
//    HostOnly   — the whole case erases to a sentinel; a decoded / diffed /
//                 introspected tree sees an inert placeholder (dead on decode).
//    Partial    — a survivable skeleton with a closure/opaque SUB-field that
//                 erases (the field's posture is in `SlotCapability`).
//
//  Pure data (FSharp.Core only, Fable-safe) — no reflection here; the coverage
//  test does the reflection.
// ============================================================================

[<RequireQualifiedAccess>]
type Survivability =
    | Survivable
    | HostOnly
    | Partial

/// One classified author-facing DU case.
type Classification =
    {
        /// DU-qualified case name, e.g. `"Binding.Computed"`, `"NodeKind.Custom"`.
        Case: string
        Verdict: Survivability
        /// For `HostOnly` / `Partial`: the wire-survivable construct an author
        /// should prefer. `None` when survivable, or a sanctioned host-only
        /// escape whose closure has no declarative twin (a host-composition seam).
        Alternative: string option
    }

let private sv case =
    { Case = case
      Verdict = Survivability.Survivable
      Alternative = None }

let private ho case alt =
    { Case = case
      Verdict = Survivability.HostOnly
      Alternative = alt }

let private pt case alt =
    { Case = case
      Verdict = Survivability.Partial
      Alternative = alt }

// The write-back-default hint shared by the omittable value-handler slots (the
// Phase 423/426/427 family — omit the closure and the renderer writes the change
// back to the control's own writable `Binding.State` / `Binding.Filter` slot).
let private writeBack =
    Some
        "omit the handler — the renderer's write-back default writes the change to the control's writable Binding.State / Binding.Filter(value, None) slot"

/// The whole-vocabulary classification. One row per author-facing DU case.
let all: Classification list =
    [
      // ── NodeKind (structural cases; Phase 692 — the four category wrappers
      // are gone, their kinds are the flat rows below) ─────────────────────
      sv "NodeKind.Custom" // structural: props (Map<string,JVal>) round-trip; the rendered body is a host registry reference
      sv "NodeKind.ErrorBoundary"
      sv "NodeKind.Switch"
      sv "NodeKind.FragmentDecl"
      sv "NodeKind.FragmentRef"
      pt "NodeKind.Mount" None // scopeId/inputs/channel/capabilities survive; OnBubble closure + guest interior are host composition (§4o)

      // ── Layout kinds ─────────────────────────────────────────────────────
      sv "NodeKind.Box"
      sv "NodeKind.SplitPanel"
      pt "NodeKind.Tabs" writeBack // OnSelect / OnSelectTag closures erase
      pt "NodeKind.Stepper" None // OnSelect closure erases; ActiveStep write-back unshipped — host wiring
      sv "NodeKind.SummaryList"
      pt "NodeKind.Disclosure" writeBack // OnToggle closure erases
      sv "NodeKind.Modal" // OnDismiss is an Action (wire-survivable), not a closure
      sv "NodeKind.ScrollArea"

      // ── Display kinds (pure presentation — all survivable) ───────────────
      sv "NodeKind.Heading"
      sv "NodeKind.Markdown"
      sv "NodeKind.Metric"
      sv "NodeKind.Badge"
      sv "NodeKind.Sparkline"
      sv "NodeKind.Callout"
      sv "NodeKind.Progress"
      sv "NodeKind.Skeleton"
      sv "NodeKind.LabelValueRow"
      sv "NodeKind.Fact"
      sv "NodeKind.Link"
      sv "NodeKind.Image"
      sv "NodeKind.List"
      sv "NodeKind.Toast"
      sv "NodeKind.CodeBlock"
      sv "NodeKind.Math"
      sv "NodeKind.Drawing" // closed/typed Shape DU; geometry static, colours are survivable Bindings (Phase 524)

      // ── Input kinds ──────────────────────────────────────────────────────
      sv "NodeKind.Form" // OnSubmit is an Action (wire-survivable)
      sv "NodeKind.Filters"
      sv "NodeKind.Button" // OnClick is an Action (wire-survivable)
      pt "NodeKind.FileUpload" None // OnSelect closure carries a non-scalar payload — host wiring
      pt "NodeKind.Select" writeBack // OnChange / OnChangeMulti closures erase

      // ── FormFieldKind (value survives; onChange/onToggle closure erases) ──
      pt "FormFieldKind.Text" writeBack
      pt "FormFieldKind.Number" writeBack
      pt "FormFieldKind.Checkbox" writeBack
      // Phase 766 — the switch affordance; identical survivability to Checkbox
      // (the bool value survives, the onToggle closure erases).
      pt "FormFieldKind.Toggle" writeBack
      pt "FormFieldKind.Choice" writeBack
      pt "FormFieldKind.TextArea" writeBack
      pt "FormFieldKind.RangedNumber" writeBack
      pt "FormFieldKind.SegmentedChoice" writeBack
      pt "FormFieldKind.Date" writeBack
      pt "FormFieldKind.DateRange" writeBack // Phase 725 — the pair survives; the onChange closure erases

      // 0.2.0 filters-unification: chips carry FormFieldKind controls — the
      // FormFieldKind rows above cover them; Range is the absorbed range chip.
      pt "FormFieldKind.Range" writeBack

      // ── Visualisation kinds ──────────────────────────────────────────────
      pt
          "NodeKind.DataGrid"
          (Some
              "use Column.Field + CellFormat instead of a closure Value; RowKeyField instead of RowKey; the click write-back default for OnRowClick")
      pt "NodeKind.Chart" None // OnPointClick selection host-only
      pt "NodeKind.Map" None // OnMarkerClick host-only

      // ── CellKindErased (grid cells) ──────────────────────────────────────
      sv "CellKindErased.Text"
      sv "CellKindErased.Numeric"
      sv "CellKindErased.Date"
      ho "CellKindErased.Editable" (Some "Column.Field + CellFormat for display; interactive edit needs host wiring")
      ho "CellKindErased.Checkbox" None
      pt "CellKindErased.Button" None // label survives; onClick closure erases
      pt "CellKindErased.ButtonGroup" None // labels survive; per-entry onClick closures erase
      ho "CellKindErased.Link" (Some "Column.Field projecting the href row property + a Text cell")
      // Phase 750 — `Pill` finally HAS a declarative twin. Until now its
      // `Alternative` was honestly `None`: both its fields are closures, so the whole
      // case erased and no wire construct said the same thing. `TonedPill` does.
      ho "CellKindErased.Pill" (Some "CellKindErased.TonedPill (field + value→tone map, fully wire-expressible)")
      sv "CellKindErased.TonedPill"
      ho "CellKindErased.Progress" None
      ho "CellKindErased.Custom" None // last-resort host escape

      // ── CellFormat ───────────────────────────────────────────────────────
      sv "CellFormat.None"
      sv "CellFormat.Number"
      sv "CellFormat.Currency"
      sv "CellFormat.Percent"
      sv "CellFormat.SignificantDigits"
      sv "CellFormat.Date"
      ho "CellFormat.Custom" (Some "one of the six typed CellFormat cases — they are the declarative set")

      // ── Binding ──────────────────────────────────────────────────────────
      pt
          "Binding.Static"
          (Some
              "a language-enumerated slot payload round-trips; a non-enumerated value (host records, obj-seq grid/chart sources) erases to \"<opaque>\" — prefer a typed slot, Binding.State / Binding.Filter, or Binding.Transform")
      pt "Binding.Query" None // the dependency edge (name/dependsOn) survives; the accessor decodes to an identity projection
      sv "Binding.Filter"
      pt "Binding.Selection" None // nodeId survives; the accessor decodes to an identity projection
      sv "Binding.State"
      // Phase 765 — there are no wire fields to lose (`{"$type":"Now"}` is the
      // whole form); only the accessor closure erases, exactly as Query's and
      // Selection's do. The VALUE is host-furnished at resolve time by design,
      // not something the wire was supposed to carry.
      pt "Binding.Now" None
      ho
          "Binding.Computed"
          (Some
              "Binding.State / Binding.Filter(for, None) reactive values; Binding.Transform for derivation; Binding.Format for formatting")
      sv "Binding.I18n"
      pt "Binding.Local" (Some "Binding.Format is the declarative twin of the Local format/parse closures") // the Phase 62 buffered-commit closures erase
      sv "Binding.Format"
      sv "Binding.Transform" // serialised as data via Fuaran.Core codecs — no closure on the wire
      sv "Binding.Invoke"

      // ── Action ───────────────────────────────────────────────────────────
      ho
          "Action.Dispatch"
          (Some
              "the substrate actions — Action.SetState / Action.Call / Action.Notify / Action.Navigate / Action.AiTool")
      pt "Action.Call" (Some "Action.Call with into: IntoState / IntoQuery is the declarative result target") // onResult closure erases
      sv "Action.Notify"
      sv "Action.Navigate"
      sv "Action.SetState"
      sv "Action.AiTool"
      sv "Action.Chain"
      sv "Action.CommitLocal"
      sv "Action.WriteToClipboard"
      pt "Action.ReadFileBody" None // fileRef.Id + encoding survive; the blob + onRead continuation are host-side
      sv "Action.Invoke"

      // ── TextSource ───────────────────────────────────────────────────────
      sv "TextSource.Literal"
      sv "TextSource.Bound"
      sv "TextSource.I18n" ]

/// Lookup by DU-qualified case name.
let byCase: Map<string, Classification> =
    all |> List.map (fun c -> c.Case, c) |> Map.ofList

/// The host-only + partial cases an author should be steered away from in
/// orchestrated contexts (everything with a recoverable alternative).
let steerable: Classification list =
    all
    |> List.filter (fun c ->
        match c.Verdict with
        | Survivability.Survivable -> false
        | _ -> true)
