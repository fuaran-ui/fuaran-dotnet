module Fuaran.UI.AffordanceInertness

// ============================================================================
//  The decode-side inertness report (Phase 924).
//
//  `WireSurvivability` (Phase 378) names the boundary for an AUTHOR: which
//  vocabulary crosses the wire and which erases. `WireSurvivabilityCheck`
//  (FUARAN084) is its build-time lens, and it reads SOURCE FILES. Neither can
//  help the consumer at the other end of the wire — a host, a differ, a replay
//  engine holding a tree that has ALREADY been decoded, where the callback is
//  gone and the affordance is inert. The tree is not malformed; it is
//  DIMINISHED, and nothing read the classification at the point where that
//  distinction matters.
//
//  This module is that read. Two arms, each derived from an existing table,
//  neither restating one:
//
//    A. the whole-case erasures — `WireSurvivability`'s `HostOnly` verdicts,
//       where the case has no wire payload to decode back into;
//    B. the slot-level ones — `SlotCapability`'s `HostOnlyByDesign` postures,
//       the slots `DeadOnDecode.lint` is deliberately SILENT on because they
//       have no declarative twin to recommend. That silence is correct for a
//       lint whose contract is "here is what to write instead"; it is exactly
//       wrong for a consumer asking "what in this tree does nothing".
//
//  Plus the twin-bearing slots, obtained by CALLING `DeadOnDecode.lint` rather
//  than re-deriving them, so one question ("which of my affordances are
//  inert?") has one answer.
//
//  Verdict semantics — the decoded CONSEQUENCE, which is a different question
//  from the survivability VERDICT and is why this table exists:
//    Inert    — present in the tree, does nothing (or renders the `"<closure>"`
//               sentinel to the user). Author intent was lost.
//    Degraded — reduced fidelity, but something substantive still works,
//               usually because a NEIGHBOURING declarative slot recovers it.
//    Fine     — nothing was lost. The value is host-furnished by design and
//               the wire was never meant to carry it.
//
//  REPORT, NEVER REPAIR. A decoded chart's point click is given a default by
//  Phase 933 as a deliberate design act, one case at a time, each with its own
//  charter argument. A general auto-repair here would hide the class rather
//  than surface it, so this module substitutes nothing.
//
//  Boundary with FUARAN098 (Phase 932): that rule finds a LIVE key with no
//  reader, statically, over any tree. This finds a DEAD callback after a wire
//  round-trip. Both are the fake-affordance family and Phase 866's property is
//  their shared definition, but they have different mechanisms, different
//  detectability, and different evidence — do not merge them.
//
//  Pure data + pattern matching (FSharp.Core only, Fable-safe) — no reflection.
//  The completeness tests do the reflection.
// ============================================================================

open Fuaran.UI.Types

/// What a consumer holding a DECODED tree actually gets from a construct the
/// wire could not carry.
[<RequireQualifiedAccess>]
type DecodedVerdict =
    | Inert
    | Degraded
    | Fine

/// Whether `report` emits a subject, and — when it does not — why not. Held on
/// the row rather than in a side list so a new host-only slot cannot arrive
/// with its coverage question unanswered.
[<RequireQualifiedAccess>]
type Coverage =
    /// `report` emits this subject whenever the tree carries it.
    | Reported
    /// The walk OFFERS this subject and the `Fine` guard drops it — nothing was
    /// lost, so there is nothing to report. Offered rather than skipped so a
    /// later reclassification starts reporting with no edit to the walk.
    | NothingLost
    /// Emitted, but under the named FINER subject — the slot row would be the
    /// same construct at a coarser key.
    | SubsumedBy of subject: string
    /// Not walked yet. The reason is the record, not an apology.
    | NotWalked of reason: string

/// One classified decode consequence. `Subject` is either a
/// `WireSurvivability` CASE name or a `SlotCapability` SLOT name — both are
/// keys owned by those tables, so this table adds verdicts and never names.
type Consequence =
    { Subject: string
      Verdict: DecodedVerdict
      Coverage: Coverage
      Consequence: string }

let private row subject verdict coverage consequence =
    { Subject = subject
      Verdict = verdict
      Coverage = coverage
      Consequence = consequence }

/// The family sweep. One row for every `WireSurvivability.HostOnly` case and
/// every `SlotCapability.HostOnlyByDesign` slot; the completeness tests assert
/// both directions, so a new host-only case or slot cannot ship without its
/// decoded consequence stated.
///
/// Every verdict below was read off the decoder, not inferred from the tables.
let family: Consequence list =
    [
      // ── Whole-case erasures (WireSurvivability `HostOnly`) ───────────────
      row
          "CellKindErased.Editable"
          DecodedVerdict.Inert
          Coverage.Reported
          "the edit affordance renders and dispatches an empty action — the cell looks editable and commits nothing"

      row
          "CellKindErased.Checkbox"
          DecodedVerdict.Inert
          Coverage.Reported
          "BOTH halves die: `get` decodes to a constant `false`, so every row renders unchecked regardless of its data, and `onToggle` dispatches an empty action. The display half is the silent-constant class the field-driven `Progress` decode closed for that cell and left standing here"

      row
          "CellKindErased.Link"
          DecodedVerdict.Inert
          Coverage.Reported
          "both closures are required, so every row renders the literal text `<closure>` linking to `<closure>` — the sentinel reaches the end user"

      row
          "CellKindErased.Pill"
          DecodedVerdict.Inert
          Coverage.Reported
          "renders the literal `<closure>` at the default tone. Mitigated but not fixed: §16 lenient ingest reroutes a `Pill` CARRYING a tone map to `TonedPill`, so the common emitted spelling is rescued and a genuinely closure-authored one is not"

      row
          "CellKindErased.Progress"
          DecodedVerdict.Degraded
          Coverage.Reported
          "the only context-dependent row. With a column-level `field` the fraction is recovered from that row property; without one every bar fills to zero. The recovery comes from a NEIGHBOURING declarative slot, which is why the whole-case survivability verdict stays `HostOnly` while the decoded consequence differs — the distinction this module exists to draw"

      row
          "CellKindErased.Custom"
          DecodedVerdict.Inert
          Coverage.Reported
          "the cell body decodes to a placeholder node whose text is `<closure>`; the last-resort host escape has nothing to fall back to"

      row
          "CellFormat.Custom"
          DecodedVerdict.Inert
          Coverage.Reported
          "the formatter decodes to a constant `<closure>`, so every cell in the column renders the sentinel instead of its value"

      row
          "Binding.Computed"
          DecodedVerdict.Inert
          Coverage.Reported
          "the computation decodes to a placeholder value. FUARAN084 already refuses this on the SOURCE path; this is the same fact observed from the other end, where refusing is no longer possible"

      row
          "Action.Dispatch"
          DecodedVerdict.Inert
          Coverage.Reported
          "no wire payload BY CONSTRUCTION — the encoder and the resume path both stub it — so the decoded action carries a boxed `<closure>` string where the host expects its own message type. The gesture survives; what it was for does not"

      // ── Slot-level erasures (SlotCapability `HostOnlyByDesign`) ──────────
      row
          "Binding.Query.accessor"
          DecodedVerdict.Fine
          Coverage.NothingLost
          "decodes to an identity projection — the host-fed value flows through unchanged. The typed projection was an F#-author refinement, never wire state"

      row
          "Binding.Selection.accessor"
          DecodedVerdict.Fine
          Coverage.NothingLost
          "decodes to an identity projection — the store-written row flows through unchanged, exactly as `Query`'s does"

      row
          "Action.Dispatch.msg"
          DecodedVerdict.Inert
          (Coverage.SubsumedBy "Action.Dispatch")
          "the slot view of the whole-case row above — the payload IS the case"

      row
          "Action.ReadFileBody.onRead"
          DecodedVerdict.Inert
          Coverage.Reported
          "the continuation decodes to a closure returning a boxed `<closure>`, and the blob handle decodes to `None`, so the read completes into nothing"

      row
          "FileUploadSpec.onSelect"
          DecodedVerdict.Inert
          Coverage.Reported
          "the picker opens and the selection dispatches an empty action — the file never reaches the tree. Explicitly out of the write-back scope: the payload is a `FileSelection list`, not a scalar"

      row
          "StepperSpec.onSelect"
          DecodedVerdict.Inert
          Coverage.Reported
          "the step gesture dispatches an empty action AND suppresses nothing, because the `activeStep` write-back default has not shipped — the one slot here whose remedy is a phase rather than a rewrite"

      row
          "MapSpec.onMarkerClick"
          DecodedVerdict.Inert
          Coverage.Reported
          "marker clicks dispatch an empty action. The chart's twin of this was closed by extending the selection write default to `Chart`; the map is the same shape with no such phase yet"

      row
          "MountSpec.onBubble"
          DecodedVerdict.Inert
          Coverage.Reported
          "the guest→host bubble channel decodes to an empty action, so a guest can raise nothing to its host across a decoded boundary"

      row
          "StateBehaviour.onError"
          DecodedVerdict.Inert
          Coverage.Reported
          "the error branch decodes to a placeholder node whose text is `<closure>`, so an error state renders the sentinel. NOT a fallback to a default error surface — that is what the capability row's rationale claimed until this sweep read the decoder"

      row
          "CellKindErased.handlers"
          DecodedVerdict.Inert
          (Coverage.SubsumedBy "the six CellKindErased case rows above")
          "the capability table keys the interactive-cell closures as one slot; the case rows above say what each of them costs, which is the granularity a consumer can act on"

      row
          "Binding.Computed.fn"
          DecodedVerdict.Inert
          (Coverage.SubsumedBy "Binding.Computed")
          "the slot view of the whole-case row above — the closure IS the case"

      row
          "Binding.Local.onCommit"
          DecodedVerdict.Inert
          (Coverage.NotWalked
              "reaching it needs a `Binding.Local` usage the shared binding walk does not surface — it recurses into `initialFrom` and emits no `Local` use. Widening that walk's usage DU would change the input of five shipped Error-severity consumption rules, which is well outside an additive report's remit")
          "the commit continuation decodes to a closure returning a boxed `<closure>`, so a buffered edit commits a sentinel"

      row
          "Binding.Local.format"
          DecodedVerdict.Degraded
          (Coverage.NotWalked "same binding-walk gap as `Binding.Local.onCommit`")
          "decodes to the generic value-to-string default, so a value still renders — the author's formatting is what is lost. `Binding.Format` is the declarative twin"

      row
          "Binding.Local.parse"
          DecodedVerdict.Inert
          (Coverage.NotWalked "same binding-walk gap as `Binding.Local.onCommit`")
          "decodes to a function returning `Error \"<closure>\"`, so EVERY edit fails to parse and the buffered value can never commit — a hard block, not a degradation"

      row
          "CellFormat.Custom.fn"
          DecodedVerdict.Inert
          (Coverage.SubsumedBy "CellFormat.Custom")
          "the slot view of the whole-case row above — the formatter IS the case" ]

/// Lookup by subject.
let bySubject: Map<string, Consequence> =
    family |> List.map (fun r -> r.Subject, r) |> Map.ofList

/// One inert (or degraded) affordance found on a decoded tree.
///
/// `Alternative` is READ from `WireSurvivability.byCase` — never copied — so a
/// change to the recommended substitute reaches this report with no edit here.
/// It is `None` for a slot-keyed subject, because the survivability table
/// classifies cases and inventing a slot-level alternative would be the second
/// list this phase exists to avoid.
type InertAffordance =
    { Node: string
      Subject: string
      Verdict: DecodedVerdict
      Consequence: string
      Alternative: string option }

let private finding (nodeId: string) (subject: string) (verdictOverride: DecodedVerdict option) : InertAffordance list =
    match Map.tryFind subject bySubject with
    | None -> []
    | Some c ->
        let verdict = defaultArg verdictOverride c.Verdict

        if verdict = DecodedVerdict.Fine then
            []
        else
            [ { Node = nodeId
                Subject = subject
                Verdict = verdict
                Consequence = c.Consequence
                Alternative =
                  Fuaran.UI.WireSurvivability.byCase
                  |> Map.tryFind subject
                  |> Option.bind (fun s -> s.Alternative) } ]

// Phase 1152 — `Action.Dispatch` is marked in-process-only by the IDL
// annotation, which renders as `[<Obsolete(…, false)>]`: FS0044 at every
// mention. Scoped off for this ONE declaration. This is the sharpest case of
// "not an authoring site" in the repo: `actionFindings` exists precisely TO
// report the marked case as inert on the wire, so warning it about the hazard
// it is the detector for adds nothing a reader does not already have. `#warnon`
// immediately below.
#nowarn "44"

/// The wire-survivable action slots, recursing `Chain` — the same slot set the
/// shared binding walk uses for `Action.Call` collection. A closure-held action
/// is invisible by construction: the walk sees what the wire sees.
let rec private actionFindings (nodeId: string) (action: Action<'Msg>) : InertAffordance list =
    match action with
    | Action.Chain actions -> actions |> List.collect (actionFindings nodeId)
    | Action.Dispatch _ -> finding nodeId "Action.Dispatch" None
    | Action.ReadFileBody(_, _, _, Some _) -> finding nodeId "Action.ReadFileBody.onRead" None
    | _ -> []

#warnon "44"

/// The grid-cell arm: six whole-case cell erasures plus the custom cell format.
/// `Progress` is verdict-shifted by the column's own `field`, which is the one
/// place a neighbouring declarative slot changes the answer.
let private columnFindings (nodeId: string) (col: ColumnErased<'Msg>) : InertAffordance list =
    let kindSubject =
        match col.Kind with
        | CellKindErased.Editable _ -> Some("CellKindErased.Editable", None)
        | CellKindErased.Checkbox _ -> Some("CellKindErased.Checkbox", None)
        | CellKindErased.Link _ -> Some("CellKindErased.Link", None)
        | CellKindErased.Pill _ -> Some("CellKindErased.Pill", None)
        | CellKindErased.Progress _ ->
            // The Phase 425 field-driven fraction: with a column `field` the
            // fill is real and only the optional label is a sentinel.
            Some(
                "CellKindErased.Progress",
                (if col.Field.IsSome then
                     Some DecodedVerdict.Degraded
                 else
                     Some DecodedVerdict.Inert)
            )
        | CellKindErased.Custom _ -> Some("CellKindErased.Custom", None)
        | CellKindErased.Text
        | CellKindErased.Numeric
        | CellKindErased.Date
        | CellKindErased.Button _
        | CellKindErased.ButtonGroup _
        | CellKindErased.TonedPill _ -> None

    let fromKind =
        match kindSubject with
        | Some(subject, verdict) -> finding nodeId subject verdict
        | None -> []

    let fromFormat =
        match col.Format with
        | CellFormat.Custom _ -> finding nodeId "CellFormat.Custom" None
        | _ -> []

    fromKind @ fromFormat

/// The per-node arm: the host-only slots a node's own spec carries.
let private nodeFindings (n: Node<'Msg>) : InertAffordance list =
    let id = n.Id

    let fromState =
        match n.State with
        | Some s when s.OnError.IsSome -> finding id "StateBehaviour.onError" None
        | _ -> []

    let fromKind =
        match n.Kind with
        | NodeKind.FileUpload s when s.OnSelect.IsSome -> finding id "FileUploadSpec.onSelect" None
        | NodeKind.Stepper s when s.OnSelect.IsSome -> finding id "StepperSpec.onSelect" None
        | NodeKind.Map s when s.OnMarkerClick.IsSome -> finding id "MapSpec.onMarkerClick" None
        | NodeKind.Mount s when s.OnBubble.IsSome -> finding id "MountSpec.onBubble" None
        | NodeKind.DataGrid g -> g.Columns |> List.collect (columnFindings id)
        | NodeKind.Button b -> actionFindings id b.OnClick
        | NodeKind.Form f -> actionFindings id f.OnSubmit
        | NodeKind.Modal m -> m.OnDismiss |> Option.map (actionFindings id) |> Option.defaultValue []
        | _ -> []

    fromState @ fromKind

/// The binding arm, taken from the shared binding walk's reader-tagged usages
/// rather than by re-walking every binding-bearing slot.
///
/// The two identity-projection accessors are OFFERED here even though both are
/// classified `Fine`: the `finding` guard drops them, so the report stays
/// silent, and a decoder change that made either of them lossy would start
/// reporting the moment its verdict row moved — with no edit to this walk. A
/// walk that skipped them would have to be remembered instead.
let private bindingFindings (root: Node<'Msg>) : InertAffordance list =
    (BindingWalk.collect root).Uses
    |> List.collect (fun u ->
        match u.Use with
        | BindingWalk.BindingUse.Computed -> finding u.Reader "Binding.Computed" None
        | BindingWalk.BindingUse.Query _ -> finding u.Reader "Binding.Query.accessor" None
        | BindingWalk.BindingUse.Selection _ -> finding u.Reader "Binding.Selection.accessor" None
        | _ -> [])
    |> List.distinct

/// Ask a DECODED tree which of its affordances are inert.
///
/// Three arms, none of them a second classification:
///   1. the twin-bearing slots, delegated to `DeadOnDecode.lint` — a decoded
///      sentinel in one of those is inert AND suppresses the declarative
///      default that would otherwise have fired;
///   2. the host-only slots that lint is deliberately silent on;
///   3. the whole-case erasures.
///
/// Containment follows `StructuralQuery.children` — the tier's single
/// containment relation — so a node reachable only through a `State`
/// alternative arm or a `Mount` guest's interior is outside this walk, exactly
/// as it is outside the dead-on-decode lint's.
///
/// Run it on a decoded / wire-ingested tree. On an F#-authored tree the
/// closures are real and every finding here is a false accusation, which is the
/// same precondition `DeadOnDecode.lint` carries and for the same reason.
let report<'Msg> (root: Node<'Msg>) : InertAffordance list =
    let fromLint =
        DeadOnDecode.lint root
        |> List.map (fun f ->
            { Node = f.Node
              Subject = f.Slot
              Verdict = DecodedVerdict.Inert
              Consequence = f.Remedy
              Alternative = None })

    let rec walk (n: Node<'Msg>) : InertAffordance list =
        nodeFindings n @ (StructuralQuery.children n |> List.collect walk)

    fromLint @ walk root @ bindingFindings root
