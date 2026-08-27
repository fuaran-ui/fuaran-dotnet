module Fuaran.UI.JsonDecode.Tests.Generators

// ============================================================================
//  Phase 101 — generative / property-based wire-format fuzzer (generators).
//
//  FsCheck generators for `Node<obj>` / `TreeOp<obj>` covering every shipped
//  `NodeKind` / `LayoutKind` / `DisplayKind` / `InputKind` / `VisKind` / `Spec`
//  / `Binding` / `Action` / `FormFieldKind` / `FilterKind` / `CellKindErased` /
//  `CellFormat` / `TreeOp` arm, with bounded tree depth.
//
//  The property these feed (`IdempotenceProperties.fs`) is the canonical
//  byte-stable round-trip the wire format promises (WIRE_FORMAT.md §1):
//
//      encode (decode (encode x)) = encode x
//
//  NOT structural value-equality of the decoded tree — closures collapse to
//  the `"<closure>"` sentinel and opaque `Binding.Static` collections collapse
//  to `"<opaque>"` (WIRE_FORMAT §4 / §5), so the decoded value is intentionally
//  lossy. The generators therefore produce values inside the round-trip-faithful
//  subspace (the same discipline `Fixtures.fs` follows), exercising every DU arm
//  while fuzzing the value spaces where genuine encoder/decoder/parser
//  asymmetries hide — escaped / unicode / control-char strings, the full IEEE-754
//  float space (incl. NaN / ±Infinity / negative-zero), optional-field
//  presence, and deep nesting.
//
//  Deliberate generator bounds (documented v1 representational details, NOT
//  wire-format defects — see WIRE_FORMAT §4/§5/§10 and JsonDecode.fs):
//   - `Binding.State` defaults use the decoder's per-type placeholder (0 / "" /
//     false / 0.0). The wire carries `defaultValue` but the typed decoders
//     substitute a placeholder, so only the placeholder value round-trips.
//   - Opaque-collection `Binding.Static` slots (`SelectOption list`,
//     `string option`, `float seq`, `obj seq`, `MapMarker seq`) encode to
//     `"<opaque>"`; non-empty collections box to a real reference so the
//     round-trip stays byte-stable (an empty `SelectOption list` boxes to a
//     null reference per F# option/list representation — avoided here).
//   - `LocalBinding.Format` is generated `None` (the decoder always restores
//     `None`; a `Some` closure would encode `"<closure>"` then re-encode absent).
//   - `JVal` payloads are scalars (string / normalised number / bool) — the
//     round-trip-faithful subset the corpus uses.
// ============================================================================

#nowarn "3261"

open System
open FsCheck
open FsCheck.FSharp
open Fuaran.UI.Types
open Fuaran.Core
open Fuaran.UI.Ops.Types

// ─── Primitive generators ──────────────────────────────────────────────────

/// Char generator biased toward the cases that stress the canonical-JSON
/// string-escaping contract (WIRE_FORMAT §2 rule 6): structural quotes,
/// backslash, the un-escaped slash, control chars (escaped `\uXXXX`), plus a
/// spread of non-ASCII that must pass through literally.
let private genChar: Gen<char> =
    Gen.frequency
        [ 6,
          Gen.elements (
              [ 'a' .. 'z' ]
              @ [ 'A' .. 'Z' ]
              @ [ '0' .. '9' ]
              @ [ ' '; '.'; ','; '-'; '_'; ':' ]
          )
          2, Gen.oneof [ Gen.elements [ '"'; '\\'; '/' ]; Gen.map char (Gen.choose (0, 31)) ]
          1, Gen.elements [ 'é'; 'ü'; 'ñ'; '中'; '日'; '€'; '£'; '✓'; 'λ' ] ]

let private genString: Gen<string> =
    Gen.map (fun (cs: char list) -> String(List.toArray cs)) (Gen.listOf genChar)

/// Non-empty string — required for every `NodeId` position (an empty id is
/// `EMPTY_NODE_ID`, WIRE_FORMAT §8) and used for all name / key / id slots.
let private genNonEmptyString: Gen<string> =
    Gen.map (fun (cs: char list) -> String(List.toArray cs)) (Gen.nonEmptyListOf genChar)

let private genBool: Gen<bool> = Gen.elements [ true; false ]

let private genInt: Gen<int> = Gen.choose (-100000, 100000)

/// Finite floats in the plain-decimal int53 range — the magnitudes where the
/// .NET "R" and JS shortest-round-trip forms agree byte-for-byte (WIRE_FORMAT
/// §5). Exponent-notation magnitudes (1e300, Double.MaxValue, …) are
/// deliberately excluded so the SAME generator can feed the cross-host parity
/// harness without tripping the documented int53 scoping (.NET emits `E+NNN`,
/// JS emits `e+NNN`). Within-host idempotence is unaffected.
let private genFiniteFloat: Gen<float> =
    Gen.frequency
        [ 5, Gen.map (fun i -> float i / 100.0) (Gen.choose (-100000000, 100000000))
          2, Gen.elements [ 0.0; -0.0; 1.0; -1.0; 0.5; 100.0; 3.141592653589793 ] ]

/// Full IEEE-754 space including the special-value sentinels (WIRE_FORMAT §5/§7).
let private genFloat: Gen<float> =
    Gen.frequency
        [ 7, genFiniteFloat
          1, Gen.elements [ Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity ] ]

let private genOption (g: Gen<'a>) : Gen<'a option> =
    Gen.frequency [ 1, Gen.constant None; 3, Gen.map Some g ]

let private genSmallList (g: Gen<'a>) : Gen<'a list> =
    gen {
        let! n = Gen.choose (0, 3)
        return! Gen.listOfLength n g
    }

// ─── Bare-string enums ───────────────────────────────────────────────────

let private genOrientation =
    Gen.elements [ Orientation.Vertical; Orientation.Horizontal ]

let private genBadgeVariant =
    Gen.elements
        [ BadgeVariant.Neutral
          BadgeVariant.Brand
          BadgeVariant.Success
          BadgeVariant.Warning
          BadgeVariant.Critical
          BadgeVariant.Info ]

let private genButtonVariant =
    Gen.elements
        [ ButtonVariant.Primary
          ButtonVariant.Secondary
          ButtonVariant.Tertiary
          ButtonVariant.Destructive ]

let private genHeadingVariant =
    Gen.elements
        [ HeadingVariant.Standard
          HeadingVariant.Eyebrow
          HeadingVariant.Caption
          HeadingVariant.Lead ]

let private genTone =
    Gen.elements
        [ ToneVariant.Default
          ToneVariant.Subdued
          ToneVariant.Brand
          ToneVariant.Success
          ToneVariant.Warning
          ToneVariant.Critical
          ToneVariant.Info ]

let private genWeight =
    Gen.elements [ StyleWeight.Compact; StyleWeight.Standard; StyleWeight.Spacious ]

let private genEmphasis =
    Gen.elements [ Emphasis.Quiet; Emphasis.Normal; Emphasis.Loud ]

/// Phase 867 — both polarities, so the omitted-at-default encode leg AND the
/// emitted `LowerIsBetter` leg are both exercised by the round-trip property
/// rather than only by the two hand-authored fixtures.
let private genTrendPolarity =
    Gen.elements [ TrendPolarity.HigherIsBetter; TrendPolarity.LowerIsBetter ]

// Phase 147 — vary role/voice so the property-based round-trip exercises the
// optional-emit wire path (omitted at default, present otherwise).
let private genStyleRole =
    Gen.elements
        [ StyleRole.None
          StyleRole.Eyebrow
          StyleRole.Data
          StyleRole.Lede
          StyleRole.Caption ]

let private genFontVoice =
    Gen.elements [ FontVoice.Default; FontVoice.Display; FontVoice.Structural ]

let private genChartKind =
    Gen.elements
        [ ChartKind.Line
          ChartKind.Bar
          ChartKind.Area
          ChartKind.Pie
          ChartKind.Scatter
          ChartKind.Heatmap ]

let private genChartLegendPosition =
    Gen.elements
        [ ChartLegendPosition.Top
          ChartLegendPosition.Right
          ChartLegendPosition.Bottom
          ChartLegendPosition.None ]

let private genChartDataLabels =
    Gen.elements [ ChartDataLabels.Off; ChartDataLabels.Ends ]

let private genChartXScale =
    Gen.elements [ ChartXScale.Category; ChartXScale.Temporal ]

let private genAriaRole: Gen<AriaRole> =
    Gen.oneof
        [ Gen.elements
              [ AriaRole.Button
                AriaRole.Link
                AriaRole.Dialog
                AriaRole.Alert
                AriaRole.Status
                AriaRole.Banner
                AriaRole.Navigation
                AriaRole.Main
                AriaRole.Form
                AriaRole.Region
                AriaRole.Heading
                AriaRole.Progressbar
                AriaRole.Tab
                AriaRole.Tablist
                AriaRole.Tabpanel ]
          Gen.map AriaRole.Custom genNonEmptyString ]

let private genLiveRegion =
    Gen.elements [ LiveRegionKind.Polite; LiveRegionKind.Assertive; LiveRegionKind.Off ]

let private genHashStrictness =
    Gen.elements
        [ HashStrictness.StrictReplay
          HashStrictness.AdvisoryWarning
          HashStrictness.Enforced ]

// ─── ColumnWidth / CellFormat ──────────────────────────────────────────────

let private genColumnWidth: Gen<ColumnWidth> =
    Gen.oneof
        [ Gen.constant ColumnWidth.Auto
          Gen.map ColumnWidth.Fixed genInt
          Gen.map ColumnWidth.Flex genFiniteFloat ]

// Phase 819 — the Duration format enums (shared by Format + CellFormat).
let private genDurationUnit: Gen<DurationUnit> =
    Gen.elements [ DurationUnit.Seconds; DurationUnit.Minutes; DurationUnit.Hours ]

let private genDurationStyle: Gen<DurationStyle> =
    Gen.elements [ DurationStyle.Compact; DurationStyle.Clock; DurationStyle.Long ]

let private genRelativeTimeUnitCell: Gen<RelativeTimeUnit> =
    Gen.elements
        [ RelativeTimeUnit.Second
          RelativeTimeUnit.Minute
          RelativeTimeUnit.Hour
          RelativeTimeUnit.Day
          RelativeTimeUnit.Week
          RelativeTimeUnit.Month
          RelativeTimeUnit.Year ]

let private genCellFormat: Gen<CellFormat> =
    Gen.oneof
        [ Gen.constant CellFormat.None
          Gen.map CellFormat.Number (genOption genInt)
          Gen.map CellFormat.Currency genNonEmptyString
          Gen.map CellFormat.Percent (genOption genInt)
          Gen.map CellFormat.SignificantDigits genInt
          Gen.map CellFormat.Date genNonEmptyString
          Gen.map2 (fun u s -> CellFormat.Duration(u, s)) genDurationUnit genDurationStyle
          Gen.map CellFormat.RelativeTime genRelativeTimeUnitCell
          Gen.constant (CellFormat.Custom(fun _ -> "<custom>")) ]

/// Deterministic spread covering every `CellFormat` arm — used by the grid
/// column generator so a single generated grid exercises all nine.
let private allCellFormats: CellFormat list =
    [ CellFormat.None
      CellFormat.Number(Some 2)
      CellFormat.Currency "GBP"
      CellFormat.Percent None
      CellFormat.SignificantDigits 3
      CellFormat.Date "yyyy-MM-dd"
      CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact)
      CellFormat.RelativeTime RelativeTimeUnit.Minute
      CellFormat.Custom(fun _ -> "<custom>") ]

// ─── JVal (scalar, round-trip-faithful subset) ───────────────────────────────
//
// Floats normalise through the decoder's number policy (integral in-range →
// `JInt`) so a generated value IS its own decode — round-trip properties
// compare on identity, not on a float/int alias.

let private genJVal: Gen<JVal> =
    Gen.oneof
        [ Gen.map JStr genString
          Gen.map
              (fun (f: float) ->
                  if floor f = f && abs f <= 2147483647.0 then
                      JInt(int f)
                  else
                      JFloat f)
              genFiniteFloat
          Gen.map JBool genBool ]

let private genJValMap: Gen<Map<string, JVal>> =
    gen {
        let! pairs = genSmallList (Gen.zip genNonEmptyString genJVal)
        return Map.ofList pairs
    }

// ─── LocalFlushTrigger ──────────────────────────────────────────────────────

let private genFlushTrigger: Gen<LocalFlushTrigger> =
    Gen.oneof
        [ Gen.constant LocalFlushTrigger.OnBlur
          Gen.constant LocalFlushTrigger.OnSubmit
          Gen.constant LocalFlushTrigger.OnCommitAction
          Gen.map LocalFlushTrigger.OnDebounce (Gen.choose (0, 5000)) ]

// ─── Format / LocaleSource (Phase 102) ───────────────────────────────────────

let private genDateStyle: Gen<DateStyle> =
    Gen.elements [ DateStyle.Short; DateStyle.Medium; DateStyle.Long; DateStyle.Full ]

let private genRelativeTimeUnit: Gen<RelativeTimeUnit> =
    Gen.elements
        [ RelativeTimeUnit.Second
          RelativeTimeUnit.Minute
          RelativeTimeUnit.Hour
          RelativeTimeUnit.Day
          RelativeTimeUnit.Week
          RelativeTimeUnit.Month
          RelativeTimeUnit.Year ]

let private genFormat: Gen<Format> =
    Gen.oneof
        [ Gen.map Format.Number (genOption genInt)
          Gen.map Format.Currency genNonEmptyString
          Gen.map Format.Percent (genOption genInt)
          Gen.map Format.Date genDateStyle
          Gen.map Format.RelativeTime genRelativeTimeUnit
          // Phase 819 — the locale-independent duration arm.
          Gen.map2 (fun u s -> Format.Duration(u, s)) genDurationUnit genDurationStyle ]

let private genLocaleSource: Gen<LocaleSource> =
    Gen.oneof
        [ Gen.constant LocaleSource.Ambient
          Gen.map LocaleSource.Explicit genNonEmptyString ]

// ─── Typed Binding generators ───────────────────────────────────────────────
//
// Every arm of `Binding<'T>` for the primitive slot types. `State` uses the
// decoder's per-type placeholder as `defaultValue` (see module header). `Local`
// uses a faithful `Static` `InitialFrom` and `Format = None`. `Format` wraps a
// faithful `Static` float source (the case is parametric on 'T, so it generates
// in every typed slot — it round-trips uniformly regardless of 'T).

let rec private genBindingWith (genStatic: Gen<'T>) (placeholder: 'T) : Gen<Binding<'T>> =
    Gen.oneof
        [ Gen.map (fun v -> Binding.Static(Some v)) genStatic
          Gen.map (fun n -> Binding.Query(n, (fun _ -> placeholder), None)) genNonEmptyString
          Gen.map (fun n -> Binding.Filter(n, None)) genNonEmptyString
          Gen.map (fun n -> Binding.Selection(n, (fun _ -> placeholder), None, None)) genNonEmptyString
          Gen.map (fun k -> Binding.State(k, Some placeholder)) genNonEmptyString
          Gen.constant (Binding.Computed(fun _ -> placeholder))
          Gen.map (fun k -> Binding.I18n(k, None)) genNonEmptyString
          gen {
              let! fmt = genFormat
              let! loc = genLocaleSource
              let! v = genFiniteFloat
              return Binding.Format(Binding.Static(Some v), fmt, loc)
          }
          gen {
              let! init = genStatic
              let! flush = genFlushTrigger

              return
                  Binding.Local(
                      flush,
                      (fun v -> string (box v)),
                      Binding.Static(Some init),
                      Some(fun _ -> box "<commit>"),
                      (fun _ -> Ok placeholder)
                  )
          } ]

let private genBindingFloat: Gen<Binding<float>> = genBindingWith genFloat 0.0
let private genBindingInt: Gen<Binding<int>> = genBindingWith genInt 0
let private genBindingString: Gen<Binding<string>> = genBindingWith genString ""
let private genBindingBool: Gen<Binding<bool>> = genBindingWith genBool false

/// `Binding<obj>` (the `TreeOp.ReplaceBinding` slot). Skips `State` / `Local`
/// (their obj placeholders are sentinels, not round-trip-faithful for an
/// arbitrary default).
let private genBindingObj: Gen<Binding<obj>> =
    Gen.oneof
        [ Gen.map (fun (s: string) -> Binding.Static(Some(box s))) genString
          Gen.map (fun (f: float) -> Binding.Static(Some(box f))) genFiniteFloat
          Gen.map (fun (b: bool) -> Binding.Static(Some(box b))) genBool
          Gen.map (fun n -> Binding.Query(n, (fun _ -> box "<q>"), None)) genNonEmptyString
          Gen.map (fun n -> Binding.Filter(n, None)) genNonEmptyString
          Gen.map (fun n -> Binding.Selection(n, (fun _ -> box "<s>"), None, None)) genNonEmptyString
          Gen.constant (Binding.Computed(fun _ -> box "<c>"))
          Gen.map (fun k -> Binding.I18n(k, None)) genNonEmptyString ]

// ─── TextSource / SelectOption ──────────────────────────────────────────────

let rec private genTextSource: Gen<TextSource> =
    Gen.oneof
        [ Gen.map TextSource.Literal genString
          Gen.map TextSource.Bound genBindingString
          gen {
              let! k = genNonEmptyString
              let! args = genJValMap
              return TextSource.I18n(k, args)
          } ]

let private genSelectOption: Gen<SelectOption> =
    gen {
        let! v = genString
        let! label = genString
        return { Value = v; Label = label }
    }

/// `Binding<SelectOption list>` — opaque `Static` (non-empty so it boxes to a
/// real reference → `"<opaque>"`), plus the non-collection arms.
let private genBindingSelectOptions: Gen<Binding<SelectOption list>> =
    Gen.oneof
        [ Gen.map (fun opts -> Binding.Static(Some opts)) (Gen.nonEmptyListOf genSelectOption)
          Gen.map (fun n -> Binding.Filter(n, None)) genNonEmptyString
          Gen.constant (
              Binding.Computed(fun _ ->
                  [ { Value = "<opaque>"
                      Label = "<opaque>" } ])
          ) ]

/// Select value slot (stage-4a type swap): the slot is now a REQUIRED
/// `Binding<string>` — the old inner-option shapes map one-to-one
/// (`Static(Some(Some s))` → `Static(Some s)`; the inner-None "no selection"
/// → `Static None`; the chip self-read stays `Filter(n, None)`). Mirrors the
/// old `genBindingStringOpt` arms at the new type.
let private genSelectValue: Gen<Binding<string>> =
    Gen.oneof
        [ Gen.map (fun s -> Binding.Static(Some s)) genString
          Gen.constant (Binding.Static None)
          Gen.map (fun n -> Binding.Filter(n, None)) genNonEmptyString ]

/// Choice / SegmentedChoice value slot (stage-3 type swap): the slot is now
/// `Binding<string> option` — the old inner-option shapes map one-to-one
/// (`Static(Some(Some s))` → `Some(Static(Some s))`; the inner-None
/// "no selection" → `Some(Static None)`; the chip self-read stays
/// `Some(Filter(n, None))`). Mirrors the old `genBindingStringOpt` intent
/// at the new shape — no omitted-slot arm, so the generated bytes stay in
/// the round-trip-faithful subspace outside auto-bind contexts too.
let private genChoiceValue: Gen<Binding<string> option> =
    Gen.oneof
        [ Gen.map (fun s -> Some(Binding.Static(Some s))) genString
          Gen.constant (Some(Binding.Static None))
          Gen.map (fun n -> Some(Binding.Filter(n, None))) genNonEmptyString ]

// ─── Display specs ──────────────────────────────────────────────────────────

let private genHeadingSpec: Gen<HeadingSpec> =
    gen {
        let! level = Gen.choose (1, 6)
        let! text = genTextSource
        let! variant = genHeadingVariant

        return
            { Level = level
              Text = text
              Variant = variant }
    }

let private genMetricSpec: Gen<MetricSpec> =
    gen {
        let! label = genTextSource
        let! source = genBindingFloat
        let! format = genCellFormat
        let! tone = genTone
        let! weight = genWeight
        let! emphasis = genEmphasis
        let! trend = genOption genBindingFloat
        let! trendFormat = genOption genCellFormat
        let! trendPolarity = genTrendPolarity
        let! icon = genOption genNonEmptyString
        let! subtext = genOption genTextSource

        return
            { Label = label
              Value = source
              Format = format
              Tone = tone
              Weight = weight
              Emphasis = emphasis
              Trend = trend
              TrendFormat = trendFormat
              TrendPolarity = trendPolarity
              Icon = icon
              Subtext = subtext }
    }

let private genCalloutSpec: Gen<CalloutSpec> =
    gen {
        let! tone = genTone
        let! heading = genOption genTextSource
        let! body = genTextSource
        let! icon = genOption genNonEmptyString
        let! dismissable = genBool

        return
            { Tone = tone
              Heading = heading
              Body = body
              Icon = icon
              Dismissable = dismissable }
    }

let private genProgressSpec: Gen<ProgressSpec> =
    gen {
        let! fraction = genBindingFloat
        let! label = genOption genTextSource
        let! caveat = genOption genTextSource
        let! indeterminate = genBool
        let! tone = genTone

        return
            { Fraction = fraction
              Label = label
              Caveat = caveat
              Indeterminate = indeterminate
              Tone = tone }
    }

let private genLabelValueRowSpec: Gen<LabelValueRowSpec> =
    gen {
        let! label = genTextSource
        let! source = genBindingFloat
        let! format = genCellFormat
        let! emphasis = genBool
        let! help = genOption genTextSource

        return
            { Label = label
              Value = source
              Format = format
              Emphasis = emphasis
              Help = help }
    }

let private genDisplayKind: Gen<NodeKind<obj>> =
    Gen.oneof
        [ Gen.map NodeKind.Heading genHeadingSpec
          Gen.map (fun t -> NodeKind.Markdown { Text = t }) genTextSource
          Gen.map NodeKind.Metric genMetricSpec
          gen {
              let! label = genTextSource
              let! variant = genBadgeVariant
              return NodeKind.Badge { Label = label; Variant = variant }
          }
          Gen.map
              (fun b -> NodeKind.Sparkline { Source = b })
              (Gen.map (fun s -> Binding.Static(Some s)) (Gen.nonEmptyListOf genFiniteFloat))
          Gen.map NodeKind.Callout genCalloutSpec
          Gen.map NodeKind.Progress genProgressSpec
          Gen.map (fun r -> NodeKind.Skeleton { Rows = r }) (Gen.choose (0, 12))
          // Phase 821 — the standalone icon-only display kind.
          gen {
              let! name = genNonEmptyString
              let! size = Gen.elements [ IconSize.Small; IconSize.Medium; IconSize.Large ]
              let! tone = genTone
              let! label = genOption genNonEmptyString

              return
                  NodeKind.Icon
                      { Icon = name
                        Size = size
                        Tone = tone
                        Label = label }
          }
          Gen.map NodeKind.LabelValueRow genLabelValueRowSpec ]

// ─── Action ─────────────────────────────────────────────────────────────────

let rec private genAction: Gen<Action<obj>> =
    Gen.oneof
        [ Gen.constant (Action.Dispatch(box "<msg>"))
          Gen.map (fun ep -> Action.Call(ep, Some(fun _ -> box "<r>"), None)) genNonEmptyString
          Gen.map2 (fun c p -> Action.Notify(c, p)) genNonEmptyString genJVal
          Gen.map Action.Navigate genNonEmptyString
          Gen.map2 (fun k v -> Action.SetState(k, Some(v), None)) genNonEmptyString genJVal
          Gen.map2 (fun n a -> Action.AiTool(n, a)) genNonEmptyString genJVal
          Gen.map Action.CommitLocal genNonEmptyString
          Gen.map Action.WriteToClipboard genString
          Gen.map
              (fun id -> Action.ReadFileBody(id, None, FileReadEncoding.Base64, Some(fun _ -> box "<r>")))
              genNonEmptyString
          Gen.map Action.Chain (genSmallList (Gen.map Action.Navigate genNonEmptyString)) ]

/// Deterministic chain covering all ten `Action` arms — used as a form's
/// `OnSubmit` so a single generated form exercises every action discriminator.
let private allActionsChain: Action<obj> =
    Action.Chain
        [ Action.Dispatch(box "<msg>")
          Action.Call("/api", Some(fun _ -> box "<r>"), None)
          Action.Notify("ch", JStr "p")
          Action.Navigate "/route"
          Action.SetState("k", Some(JStr "v"), None)
          Action.AiTool("tool", JStr "a")
          Action.Chain []
          Action.CommitLocal "node"
          Action.WriteToClipboard "text"
          Action.ReadFileBody("file:0", None, FileReadEncoding.Base64, Some(fun _ -> box "<r>")) ]

// ─── Input specs ─────────────────────────────────────────────────────────────

let private genNumberConstraints: Gen<NumberFieldConstraints> =
    gen {
        let! mn = genOption genFiniteFloat
        let! mx = genOption genFiniteFloat
        let! st = genOption genFiniteFloat
        return { Min = mn; Max = mx; Step = st }
    }

/// One `FormField` of every `FormFieldKind` arm — collectively exercising all
/// seven discriminators in any generated form.
let private genFormFields: Gen<FormField<obj> list> =
    gen {
        let! label = genTextSource
        let! sText = genBindingString
        let! sNum = genBindingFloat
        let! sBool = genBindingBool
        let! sOpts = genBindingSelectOptions
        let! sVal = genChoiceValue
        let! constraints = genNumberConstraints
        let! rows = Gen.choose (1, 8)
        let! orientation = genOrientation

        let mk id kind : FormField<obj> =
            { Id = id
              Label = label
              Kind = kind
              Required = false
              Help = None
              Rule = None }

        return
            // Phase 426: cover both the `Some` closure (byte-stable `"<closure>"`) and the `None`
            // declarative (handler-free / write-back) shape for the covered handlers.
            [ mk "f-text" (FormFieldKind.Text(Some sText, Some(fun _ -> Action.Chain [])))
              mk "f-number" (FormFieldKind.Number(Some sNum, Some(fun _ -> Action.Chain [])))
              mk "f-checkbox" (FormFieldKind.Checkbox(Some sBool, Some(fun _ -> Action.Chain [])))
              mk "f-choice" (FormFieldKind.Choice(sOpts, sVal, Some(fun _ -> Action.Chain [])))
              mk "f-textarea" (FormFieldKind.TextArea(Some sText, Some(fun _ -> Action.Chain []), rows))
              mk
                  "f-ranged"
                  (FormFieldKind.RangedNumber(
                      Some sNum,
                      Some(fun _ -> Action.Chain []),
                      constraints.Min,
                      constraints.Max,
                      constraints.Step
                  ))
              mk "f-segmented" (FormFieldKind.SegmentedChoice(sOpts, sVal, Some(fun _ -> Action.Chain []), orientation))
              mk "f-text-decl" (FormFieldKind.Text(Some sText, None))
              mk "f-number-decl" (FormFieldKind.Number(Some sNum, None))
              mk "f-checkbox-decl" (FormFieldKind.Checkbox(Some sBool, None))
              mk "f-choice-decl" (FormFieldKind.Choice(sOpts, sVal, None))
              mk "f-textarea-decl" (FormFieldKind.TextArea(Some sText, None, rows))
              mk
                  "f-ranged-decl"
                  (FormFieldKind.RangedNumber(Some sNum, None, constraints.Min, constraints.Max, constraints.Step))
              mk "f-segmented-decl" (FormFieldKind.SegmentedChoice(sOpts, sVal, None, orientation)) ]
    }

let private genFormSpec: Gen<FormSpec<obj>> =
    gen {
        let! fields = genFormFields
        let! submitLabel = genTextSource
        let! disabled = genOption genBindingBool
        // OnSubmit covers all nine Action arms (see allActionsChain).
        return
            { Fields = fields
              OnSubmit = allActionsChain
              SubmitLabel = submitLabel
              Disabled = disabled }
    }

/// One `FilterSpec` of every `FilterKind` arm.
let private genFilters: Gen<FilterSpec<obj> list> =
    gen {
        let! label = genTextSource
        let! sText = genBindingString
        let! sOpts = genBindingSelectOptions
        let! sVal = genChoiceValue
        let! orientation = genOrientation

        let mk name kind : FilterSpec<obj> =
            { Name = name
              Label = label
              Kind = kind }

        return
            // 0.2.0 filters-unification: chips carry FormFieldKind controls.
            // Cover Some-closure + None-declarative shapes; `ft-auto` covers
            // the auto Filter(name) binding the minimal wire synthesizes;
            // `ft-range-typed` carries authorable {min,max} bounds.
            [ mk "ft-text" (FormFieldKind.Text(Some sText, Some(fun _ -> Action.Chain [])))
              mk "ft-choice" (FormFieldKind.Choice(sOpts, sVal, Some(fun _ -> Action.Chain [])))
              mk
                  "ft-range"
                  (FormFieldKind.Range(
                      Some(Binding.Static(Some { Min = 0.0; Max = 0.0 })),
                      Some(fun _ -> Action.Chain []),
                      None,
                      None,
                      None
                  ))
              mk "ft-seg" (FormFieldKind.SegmentedChoice(sOpts, sVal, Some(fun _ -> Action.Chain []), orientation))
              mk "ft-auto" (FormFieldKind.Text(Some(Binding.Filter("ft-auto", None)), None))
              mk "ft-text-decl" (FormFieldKind.Text(Some(Binding.Filter("q", None)), None))
              mk "ft-choice-decl" (FormFieldKind.Choice(sOpts, sVal, None))
              mk
                  "ft-range-typed"
                  (FormFieldKind.Range(Some(Binding.Static(Some { Min = 1.0; Max = 9.0 })), None, None, None, None))
              mk "ft-seg-decl" (FormFieldKind.SegmentedChoice(sOpts, sVal, None, orientation)) ]
    }

let private genButtonSpec: Gen<ButtonSpec<obj>> =
    gen {
        let! label = genTextSource
        let! onClick = genAction
        let! variant = genButtonVariant
        let! icon = genOption genNonEmptyString
        let! tooltip = genOption genTextSource
        let! disabled = genOption genBindingBool

        return
            { Label = label
              OnClick = onClick
              Variant = variant
              Icon = icon
              Tooltip = tooltip
              Disabled = disabled }
    }

let private genFileUploadSpec: Gen<FileUploadSpec<obj>> =
    gen {
        let! label = genTextSource
        let! accept = genSmallList genNonEmptyString
        let! multiple = genBool
        let! disabled = genOption genBindingBool

        return
            { Label = label
              Accept = accept
              Multiple = multiple
              OnSelect = Some(fun _ -> Action.Chain [])
              Disabled = disabled }
    }

let private genSelectSpec: Gen<SelectSpec<obj>> =
    gen {
        let! label = genTextSource
        let! source = genBindingSelectOptions
        let! value = genSelectValue
        let! placeholder = genOption genTextSource
        let! disabled = genOption genBindingBool
        // Phase 291 — exercise both single- and multi-select. `Multiple = true`
        // ties to `Values = Some` + a multi `OnChangeMulti` closure; the
        // generated string-list collapses to `<opaque>` on the wire (the
        // established Static-collection limitation) but round-trips byte-stable.
        // Phase 426 — the handlers are optional; exercise both `Some` (sentinel)
        // and `None` (declarative / write-back) shapes.
        let! multiple = Gen.elements [ false; true ]
        let! withHandlers = Gen.elements [ false; true ]
        let! vlist = genSmallList genNonEmptyString

        return
            { Label = label
              Source = source
              Value = value
              OnChange =
                (if withHandlers then
                     Some(fun _ -> Action.Chain [])
                 else
                     Option.None)
              Placeholder = placeholder
              Disabled = disabled
              Multiple = (if multiple then Some true else Option.None)
              Values =
                (if multiple then
                     Some(Binding.Static(Some vlist))
                 else
                     Option.None)
              OnChangeMulti =
                (if multiple && withHandlers then
                     Some(fun _ -> Action.Chain [])
                 else
                     Option.None) }
    }

let private genInputKind: Gen<NodeKind<obj>> =
    Gen.oneof
        [ Gen.map NodeKind.Form genFormSpec
          Gen.map (fun items -> NodeKind.Filters { Items = items }) genFilters
          Gen.map NodeKind.Button genButtonSpec
          Gen.map NodeKind.FileUpload genFileUploadSpec
          Gen.map NodeKind.Select genSelectSpec ]

// ─── Visualisation specs ─────────────────────────────────────────────────────

/// Grid columns: one of every `CellKindErased` arm, with `CellFormat` cycling
/// through all seven format arms (see allCellFormats).
let private genGridColumns: Gen<ColumnErased<obj> list> =
    gen {
        let! buttonLabel = genTextSource

        let cellKinds: CellKindErased<obj> list =
            [ CellKindErased.Text
              CellKindErased.Numeric
              CellKindErased.Date
              // `Some` closures (Phase 692–694 swap — the handler slots are
              // optional now); Some keeps the `"<closure>"` sentinel bytes.
              CellKindErased.Editable(Some(fun _ -> Action.Chain []))
              CellKindErased.Checkbox((fun _ -> false), Some(fun _ -> Action.Chain []))
              CellKindErased.Button(buttonLabel, Some(fun _ -> Action.Chain []))
              CellKindErased.ButtonGroup
                  [ { Label = buttonLabel
                      OnClick = Some(fun _ -> Action.Chain []) } ]
              CellKindErased.Link((fun _ -> "/href"), (fun _ -> TextSource.Literal "link"))
              CellKindErased.Pill((fun _ -> TextSource.Literal "pill"), (fun _ -> ToneVariant.Brand))
              CellKindErased.Progress((fun _ -> 0.5), Some(fun _ -> TextSource.Literal "p"))
              CellKindErased.Custom(fun _ ->
                  { Id = "cell-custom"
                    Kind = NodeKind.Markdown({ Text = TextSource.Literal "x" })
                    State = None
                    Style = None
                    Accessibility = None
                    Motion = None
                    ExtraAttributes = None }) ]

        return
            cellKinds
            |> List.mapi (fun i kind ->
                { Label = sprintf "col%d" i
                  Value = Some(fun _ -> CellValue.Empty)
                  Field = None
                  Sortable = None
                  Editable = None
                  Format = allCellFormats[i % List.length allCellFormats]
                  Kind = kind
                  Width = ColumnWidth.Auto })
    }

let private genGridSpec: Gen<GridSpec<obj>> =
    gen {
        let! columns = genGridColumns
        let! editable = genBool

        return
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Static(Some Seq.empty)
              RowKey = Some(fun _ -> "<rowkey>")
              RowKeyField = None
              Columns = columns
              OnRowClick = None
              Editable = editable
              Reorderable = false
              StaticRows = None }
    }

let private genChartSpec: Gen<ChartSpec<obj>> =
    gen {
        let! kind = genChartKind
        let! xField = genNonEmptyString
        let! yFields = genSmallList genNonEmptyString
        let! title = genOption genTextSource
        // Phase 876 — the value axis's declared number format.
        let! valueFormat = genOption genFormat
        // Phase 878 — the axis names + the subtitle.
        let! xTitle = genOption genTextSource
        let! yTitle = genOption genTextSource
        let! subtitle = genOption genTextSource
        // Phase 880 — which edge the legend occupies (absent = the host
        // style's default, which is why the option arm matters here).
        let! legendPosition = genOption genChartLegendPosition
        // Phase 881 — whether the values are written onto the picture (absent
        // = `Off`, which is also the default, so the option arm matters here).
        let! dataLabels = genOption genChartDataLabels
        // Phase 882 — what the x column means (absent = `Category`, which is
        // also the default, so the option arm matters here too).
        let! xScale = genOption genChartXScale
        let! stacked = genBool

        return
            { Source = Binding.Static(Some Seq.empty)
              Kind = kind
              XField = xField
              YFields = yFields
              Title = title
              ValueFormat = valueFormat
              XTitle = xTitle
              YTitle = yTitle
              Subtitle = subtitle
              LegendPosition = legendPosition
              DataLabels = dataLabels
              XScale = xScale
              OnPointClick = None
              Stacked = stacked }
    }

let private genMapSpec: Gen<MapSpec<obj>> =
    gen {
        let! lat = genFiniteFloat
        let! lon = genFiniteFloat
        let! zoom = Gen.choose (0, 20)

        return
            { Source = Binding.Static(Some([]: MapMarker list))
              CentreLatitude = lat
              CentreLongitude = lon
              Zoom = zoom
              OnMarkerClick = None }
    }

let private genVisKind: Gen<NodeKind<obj>> =
    Gen.oneof
        [ Gen.map NodeKind.DataGrid genGridSpec
          Gen.map NodeKind.Chart genChartSpec
          Gen.map NodeKind.Map genMapSpec ]

// ─── Custom / fragments ──────────────────────────────────────────────────────

let private genContentHash: Gen<ContentHash> =
    gen {
        let! hash = genNonEmptyString
        let! strictness = genHashStrictness

        return
            { Algorithm = "SHA256"
              Hash = hash
              Strictness = strictness }
    }

let private genCustom: Gen<NodeKind<obj>> =
    gen {
        let! moduleId = genNonEmptyString
        let! componentId = genNonEmptyString
        let! props = genJValMap
        let! contentHash = genOption genContentHash
        // `None` ≡ the old `[]` — an empty list would encode differently
        // (`"exposedNodeIds":[]`), so the omitted-key shape maps to None.
        let! exposed = genSmallList genNonEmptyString

        return
            NodeKind.Custom(
                { ModuleId = moduleId
                  ComponentId = componentId
                  Props = props
                  ContentHash = contentHash
                  ExposedNodeIds = (if List.isEmpty exposed then None else Some exposed) }
            )
    }

let private genFragmentRef: Gen<NodeKind<obj>> =
    // Degenerate (zero-arg) refs — the parameterised hole/arg surface is covered
    // by the explicit Phase 180 fixtures, not the property generator.
    Gen.map
        (fun n ->
            NodeKind.FragmentRef
                { Name = n
                  // `None` ≡ the old empty Map — the zero-arg wire shape.
                  Args = None })
        genNonEmptyString

// ─── Style / accessibility ───────────────────────────────────────────────────

let private genSemanticStyle: Gen<SemanticStyle> =
    gen {
        let! tone = genTone
        let! weight = genWeight
        let! emphasis = genEmphasis
        let! role = genStyleRole
        let! voice = genFontVoice

        return
            { Tone = tone
              Weight = weight
              Emphasis = emphasis
              Role = role
              Voice = voice }
    }

/// Node.State / Node.Style are `option` post-swap (Phase 692–694). The pre-swap
/// encoder omitted an empty state / default style, and omission is the `None`
/// shape now — mapping the degenerate records to `None` keeps the generated
/// bytes in the same round-trip-faithful subspace as before.
let private someState (s: StateBehaviour<obj>) : StateBehaviour<obj> option =
    if s.OnLoading.IsNone && s.OnEmpty.IsNone && s.OnError.IsNone then
        None
    else
        Some s

let private someStyle (s: SemanticStyle) : SemanticStyle option =
    let isDefault =
        s.Tone = ToneVariant.Default
        && s.Weight = StyleWeight.Standard
        && s.Emphasis = Emphasis.Normal
        && s.Role = StyleRole.None
        && s.Voice = FontVoice.Default

    if isDefault then None else Some s

let private genAccessibility: Gen<Accessibility> =
    gen {
        let! label = genOption genBindingString
        let! labelledBy = genOption genNonEmptyString
        let! describedBy = genOption genNonEmptyString
        let! role = genOption genAriaRole
        let! live = genOption genLiveRegion
        let! hidden = genOption genBindingBool

        return
            { Label = label
              LabelledBy = labelledBy
              DescribedBy = describedBy
              Role = role
              LiveRegion = live
              Hidden = hidden }
    }

// ─── Layout specs / node recursion ───────────────────────────────────────────

let private genChildren (sub: Gen<Node<obj>>) : Gen<Node<obj> list> =
    gen {
        let! n = Gen.choose (0, 3)
        return! Gen.listOfLength n sub
    }

let rec private genLayoutKind (size: int) : Gen<NodeKind<obj>> =
    let sub = genNodeSized (size - 1)

    Gen.oneof
        [ gen {
              // Phase 390: the retired Dashboard/Stack/GridLayout/Card kinds all
              // collapse to `Box`. Pick a random layout mode (Flex / Grid / Auto)
              // and a random role (Group / Card / Dashboard — never Separator, a
              // reserved role no encoder emits this phase). `Gap = None` always
              // (reserved field). `Heading` is `Some` only sometimes; `None` is
              // always fine.
              let! layout =
                  Gen.oneof
                      [ gen {
                            let! direction = genOrientation
                            let! wrap = genBool
                            return BoxLayout.Flex(direction, wrap, None)
                        }
                        gen {
                            let! cols = Gen.choose (1, 12)
                            let! template = genOption genNonEmptyString
                            return BoxLayout.Grid(cols, template, None)
                        }
                        Gen.constant BoxLayout.Auto ]

              let! role = Gen.elements [ BoxRole.Group; BoxRole.Card; BoxRole.Dashboard ]
              let! heading = genOption genTextSource
              let! children = genChildren sub

              return
                  NodeKind.Box
                      { Layout = layout
                        Role = role
                        Heading = heading
                        Children = children }
          }
          gen {
              let! weight = genFiniteFloat
              let! children = genChildren sub
              return NodeKind.SplitPanel { Weight = weight; Children = children }
          }
          gen {
              let! orientation = genOrientation
              let! children = genChildren sub
              let! active = genBindingInt
              // Phase 426: exercise both the `Some` closure and the `None`
              // (declarative / write-back) handler shape.
              let! withHandler = genBool

              return
                  NodeKind.Tabs
                      { Orientation = orientation
                        Children = children
                        ActiveIndex = active
                        OnSelect = (if withHandler then Some(fun _ -> Action.Chain []) else None)
                        TabHeaders = None
                        TabTags = None
                        ActiveTag = None
                        OnSelectTag = None }
          }
          gen {
              let! active = genBindingInt
              let! children = genChildren sub

              return
                  NodeKind.Stepper
                      { ActiveStep = active
                        Children = children
                        // `Some` — keeps the `"onSelect":"<closure>"` sentinel
                        // on the wire (the pre-swap required-closure bytes).
                        OnSelect = Some(fun _ -> Action.Chain []) }
          }
          gen {
              let! heading = genOption genTextSource
              let! children = genChildren sub

              return
                  NodeKind.SummaryList
                      { Heading = heading
                        Children = children }
          }
          gen {
              let! heading = genTextSource
              let! isOpen = genBindingBool
              let! children = genChildren sub
              let! defaultOpen = genBool
              // Phase 426: exercise both handler shapes.
              let! withHandler = genBool

              return
                  NodeKind.Disclosure
                      { Heading = heading
                        Open = isOpen
                        OnToggle = (if withHandler then Some(fun _ -> Action.Chain []) else None)
                        Children = children
                        DefaultOpen = defaultOpen }
          } ]

and private genNodeKind (size: int) : Gen<NodeKind<obj>> =
    let leaves = [ genDisplayKind; genInputKind; genVisKind; genCustom; genFragmentRef ]

    let branches =
        if size > 0 then
            [ genLayoutKind size
              gen {
                  let! child = genNodeSized (size - 1)
                  let! fallback = genNodeSized (size - 1)
                  return NodeKind.ErrorBoundary { Child = child; Fallback = fallback }
              }
              gen {
                  let! name = genNonEmptyString
                  let! body = genNodeSized (size - 1)

                  return
                      NodeKind.FragmentDecl
                          { Name = name
                            Body = body
                            // `None` ≡ the old zero-holes / pure-deterministic
                            // defaults — both keys stay off the wire.
                            Holes = None
                            Effect = None }
              } ]
        else
            []

    Gen.oneof (leaves @ branches)

and private genStateBehaviour (size: int) : Gen<StateBehaviour<obj>> =
    if size <= 0 then
        gen {
            let! onError = genBool

            return
                { OnLoading = None
                  OnEmpty = None
                  OnError =
                    (if onError then
                         Some(fun _ -> placeholderErrorNode)
                     else
                         None) }
        }
    else
        gen {
            let! onLoading = genOption (genNodeSized (size - 1))
            let! onEmpty = genOption (genNodeSized (size - 1))
            let! onError = genBool

            return
                { OnLoading = onLoading
                  OnEmpty = onEmpty
                  OnError =
                    (if onError then
                         Some(fun _ -> placeholderErrorNode)
                     else
                         None) }
        }

and private placeholderErrorNode: Node<obj> =
    { Id = "err"
      Kind = NodeKind.Markdown({ Text = TextSource.Literal "err" })
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None }

and private genNodeSized (size: int) : Gen<Node<obj>> =
    gen {
        let! id = genNonEmptyString
        let! kind = genNodeKind size
        let! state = genStateBehaviour size
        let! style = genSemanticStyle
        let! accessibility = genOption genAccessibility

        return
            { Id = id
              Kind = kind
              State = someState state
              Style = someStyle style
              Accessibility = accessibility
              Motion = None
              ExtraAttributes = None }
    }

/// Top-level Node generator — depth capped at 4 regardless of the FsCheck size.
let genNode: Gen<Node<obj>> = Gen.sized (fun s -> genNodeSized (min s 4))

// ─── TreeOp ──────────────────────────────────────────────────────────────────

let rec private genOpSized (size: int) : Gen<TreeOp<obj>> =
    let nonBatch =
        [ gen {
              let! target = genNonEmptyString
              let! kind = genNodeKind 2
              return TreeOp.EditNode(NodeId target, kind)
          }
          gen {
              let! target = genNonEmptyString
              let! path = genNonEmptyString
              let! value = genJVal
              return TreeOp.UpdateProp(NodeId target, path, PropValue.Wire value)
          }
          gen {
              let! target = genNonEmptyString
              let! slot = genNonEmptyString
              let! binding = genBindingObj
              return TreeOp.ReplaceBinding(NodeId target, slot, binding)
          }
          gen {
              let! target = genNonEmptyString
              let! style = genSemanticStyle
              return TreeOp.UpdateStyle(NodeId target, style)
          }
          gen {
              let! target = genNonEmptyString
              let! state = genStateBehaviour 1
              return TreeOp.UpdateState(NodeId target, state)
          }
          gen {
              let! parent = genNonEmptyString
              let! child = genNodeSized 2
              return TreeOp.InsertChild(NodeId parent, child)
          }
          Gen.map (fun t -> TreeOp.RemoveNode(NodeId t)) genNonEmptyString
          gen {
              let! target = genNonEmptyString
              let! parent = genNonEmptyString
              return TreeOp.MoveNode(NodeId target, NodeId parent)
          }
          gen {
              let! parent = genNonEmptyString
              let! order = genSmallList (Gen.map NodeId genNonEmptyString)
              return TreeOp.ReorderChildren(NodeId parent, order)
          } ]

    if size > 0 then
        Gen.oneof (nonBatch @ [ Gen.map TreeOp.Batch (genSmallList (genOpSized (size - 1))) ])
    else
        Gen.oneof nonBatch

let genOp: Gen<TreeOp<obj>> = genOpSized 2

// ─── Shrinkers — reduce a failing tree toward a minimal counterexample ───────

let rec shrinkNode (n: Node<obj>) : Node<obj> seq =
    // Offer each immediate child Node as a smaller candidate. FsCheck recurses,
    // converging on the minimal failing subtree.
    let childrenOf (k: NodeKind<obj>) : Node<obj> list =
        match k with
        | NodeKind.Box s -> s.Children
        | NodeKind.SplitPanel s -> s.Children
        | NodeKind.Tabs s -> s.Children
        | NodeKind.Stepper s -> s.Children
        | NodeKind.SummaryList s -> s.Children
        | NodeKind.Disclosure s -> s.Children
        | NodeKind.Modal s -> s.Children
        | NodeKind.ScrollArea s -> s.Children
        | NodeKind.ErrorBoundary s -> [ s.Child; s.Fallback ]
        | NodeKind.FragmentDecl s -> [ s.Body ]
        | _ -> []

    let stateChildren =
        match n.State with
        | Some s -> [ s.OnLoading; s.OnEmpty ] |> List.choose id
        | None -> []

    Seq.append (childrenOf n.Kind) stateChildren

let shrinkOp (op: TreeOp<obj>) : TreeOp<obj> seq =
    match op with
    | TreeOp.Batch ops -> Seq.ofList ops
    | _ -> Seq.empty

// ─── FsCheck Arbitraries ─────────────────────────────────────────────────────

let nodeArb: Arbitrary<Node<obj>> = Arb.fromGenShrink (genNode, shrinkNode)
let opArb: Arbitrary<TreeOp<obj>> = Arb.fromGenShrink (genOp, shrinkOp)
