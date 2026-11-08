module Fuaran.UI.OpStream.Tests.OpEncoderPinCases

// The op encoder's case census (Phase 2061): one op per `TreeOp` case and one
// `ReplaceBinding` per `Binding` case and per static-payload population, with
// the edge spellings each of them has (absent payloads, empty lists, escapes,
// non-finite floats). `OpEncoderPinTests` pins the bytes `CanonicalJson.encodeOp`
// gives each one, so a change to how the op encoder is BUILT that moves a byte
// fails here by name.

// The census builds null payloads deliberately, as an in-process caller can.
#nowarn "3261"

open System
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.TestSupport

let private table =
    Fuaran.Core.Embedded
        { Schema = [ Fuaran.Core.Field.create "amount" Fuaran.Core.FloatType ]
          Columns =
            [ Fuaran.Core.Column.ofFloats "amount" (Fuaran.Core.Vector.ofList [ 40.0; 2.0 ]) Fuaran.Core.AllValid ] }

let private param (name: string) (from: Binding<JVal>) : TransformParam = { From = from; Name = name }

let private deptEq =
    Fuaran.Compute.Binary(Fuaran.Compute.Eq, Fuaran.Compute.Col "dept", Fuaran.Compute.ColExpr.Param "dept")

let private rb (b: Binding<obj>) : TreeOp<TestMsg> =
    TreeOp.ReplaceBinding(NodeId "n", "Source", b)

let private stat (v: obj) : Binding<obj> = Binding.Static(Some v)

let private fmt (f: Format) (l: LocaleSource) : Binding<obj> =
    Binding.Format(Binding.State("amount", Some 1.5), f, l)

/// A null payload, as an in-process caller can construct one (the wire has no null).
let private nullObj: obj = Unchecked.defaultof<obj>

type PinRecord = { A: int }

/// Every case, named. Names are stable: a pin is found by its name.
let cases: (string * TreeOp<TestMsg>) list =
    let md = Fuaran.markdown "m" "Body"

    [
      // ── Binding cases through ReplaceBinding ──
      "static-none", rb (Binding.Static None)
      "static-some-null", rb (Binding.Static(Some nullObj))
      "static-string", rb (stat (box "a\"b\\c\u0001é"))
      "static-bool", rb (stat (box true))
      "static-int", rb (stat (box -42))
      "static-int64", rb (stat (box 9007199254740993L))
      "static-float", rb (stat (box 3.25))
      "static-float-whole", rb (stat (box 2.0))
      "static-float-big", rb (stat (box 1e21))
      "static-float-small", rb (stat (box 1e-7))
      "static-float-negzero", rb (stat (box -0.0))
      "static-float-nan", rb (stat (box nan))
      "static-float-inf", rb (stat (box infinity))
      "static-float-neginf", rb (stat (box -infinity))
      "static-float32", rb (stat (box 1.5f))
      "static-datetimeoffset", rb (stat (box (DateTimeOffset.FromUnixTimeSeconds 1700000000L)))
      "static-datetime-utc", rb (stat (box (DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc))))
      "static-list-opaque", rb (stat (box [ 1; 2 ]))
      "static-record-opaque", rb (stat (box { A = 1 }))
      "static-jval-opaque", rb (stat (box (JStr "x")))
      "query-bare", rb (Binding.Query("rows", (fun _ -> nullObj), None))
      "query-deps-empty", rb (Binding.Query("rows", (fun _ -> nullObj), Some []))
      "query-deps", rb (Binding.Query("rows", (fun _ -> nullObj), Some [ "b"; "a" ]))
      "filter-bare", rb (Binding.Filter("status", None))
      "filter-default", rb (Binding.Filter("status", Some(box "open")))
      "filter-default-null", rb (Binding.Filter("status", Some nullObj))
      "selection-bare", rb (Binding.Selection("grid", (fun _ -> nullObj), None, None))
      "selection-full", rb (Binding.Selection("grid", (fun _ -> nullObj), Some(box 7), Some "id"))
      "selection-default-null", rb (Binding.Selection("grid", (fun _ -> nullObj), Some nullObj, None))
      "state-bare", rb (Binding.State("k", None))
      "state-default", rb (Binding.State("k", Some(box 1.25)))
      "state-default-null", rb (Binding.State("k", Some nullObj))
      "now-bare", rb (Binding.Now((fun _ -> nullObj), None))
      "now-second", rb (Binding.Now((fun _ -> nullObj), Some TimeGrain.Second))
      "now-day", rb (Binding.Now((fun _ -> nullObj), Some TimeGrain.Day))
      "computed", rb (Binding.Computed(fun _ -> nullObj))
      "i18n-bare", rb (Binding.I18n("greet", None))
      "i18n-args-empty", rb (Binding.I18n("greet", Some Map.empty))
      "i18n-args",
      rb (
          Binding.I18n(
              "greet",
              Some(
                  Map.ofList
                      [ "name", Binding.Static(Some(JStr "Ada"))
                        "n", Binding.State("count", None)
                        "o", Binding.Static(Some(JObj [ "z", JInt 1; "a", JBool false ])) ]
              )
          )
      )
      "local-minimal",
      rb (
          Binding.Local(
              LocalFlushTrigger.OnBlur,
              (fun _ -> ""),
              Binding.State("k", None),
              None,
              (fun s -> Ok(box s)),
              None,
              None
          )
      )
      "local-full",
      rb (
          Binding.Local(
              LocalFlushTrigger.OnDebounce 250,
              (fun _ -> ""),
              Binding.State("k", Some(box "a")),
              Some(fun _ -> box 1),
              (fun s -> Ok(box s)),
              Some(Format.Number(Some 2)),
              Some "target"
          )
      )
      "local-onsubmit",
      rb (
          Binding.Local(
              LocalFlushTrigger.OnSubmit,
              (fun _ -> ""),
              Binding.Static None,
              None,
              (fun s -> Ok(box s)),
              None,
              None
          )
      )
      "local-oncommitaction",
      rb (
          Binding.Local(
              LocalFlushTrigger.OnCommitAction,
              (fun _ -> ""),
              Binding.Static(Some(box 3)),
              None,
              (fun s -> Ok(box s)),
              None,
              None
          )
      )
      "format-number-bare", rb (fmt (Format.Number None) LocaleSource.Ambient)
      "format-number", rb (fmt (Format.Number(Some 2)) (LocaleSource.Explicit "en-GB"))
      "format-currency-bare", rb (fmt (Format.Currency("GBP", None)) LocaleSource.Ambient)
      "format-currency", rb (fmt (Format.Currency("EUR", Some 0)) LocaleSource.Ambient)
      "format-percent", rb (fmt (Format.Percent(Some 1)) LocaleSource.Ambient)
      "format-percent-bare", rb (fmt (Format.Percent None) LocaleSource.Ambient)
      "format-datetime-bare", rb (fmt (Format.DateTime(None, None)) LocaleSource.Ambient)
      for d in [ DateStyle.Short; DateStyle.Medium; DateStyle.Long; DateStyle.Full ] do
          $"format-datestyle-%A{d}", rb (fmt (Format.DateTime(Some d, None)) LocaleSource.Ambient)
      for t in [ TimeStyle.Short; TimeStyle.Medium; TimeStyle.Long; TimeStyle.Full ] do
          $"format-timestyle-%A{t}", rb (fmt (Format.DateTime(None, Some t)) LocaleSource.Ambient)
      for u in
          [ RelativeTimeUnit.Second
            RelativeTimeUnit.Minute
            RelativeTimeUnit.Hour
            RelativeTimeUnit.Day
            RelativeTimeUnit.Week
            RelativeTimeUnit.Month
            RelativeTimeUnit.Year ] do
          $"format-relative-%A{u}", rb (fmt (Format.RelativeTime u) LocaleSource.Ambient)
          $"format-since-%A{u}", rb (fmt (Format.Since(Some u)) LocaleSource.Ambient)
      "format-since-bare", rb (fmt (Format.Since None) LocaleSource.Ambient)
      for u in [ DurationUnit.Seconds; DurationUnit.Minutes; DurationUnit.Hours ] do
          for s in [ DurationStyle.Compact; DurationStyle.Clock; DurationStyle.Long ] do
              $"format-duration-%A{u}-%A{s}", rb (fmt (Format.Duration(u, s)) LocaleSource.Ambient)
      "format-source-static", rb (Binding.Format(Binding.Static(Some 0.1), Format.Number None, LocaleSource.Ambient))
      "transform-data", rb (Binding.Transform(TransformSource.Data table, [], None))
      "transform-params-empty", rb (Binding.Transform(TransformSource.Data table, [], Some []))
      "transform-live",
      rb (
          Binding.Transform(
              TransformSource.Live(Binding.Query("rows", (fun _ -> JArr []), None), table),
              [ Fuaran.Compute.Filter deptEq ],
              Some [ param "dept" (Binding.State("d", Some(JStr "x"))) ]
          )
      )
      "expr-bare", rb (Binding.Expr(deptEq, None))
      "expr-params-empty", rb (Binding.Expr(deptEq, Some []))
      "expr-params", rb (Binding.Expr(deptEq, Some [ param "dept" (Binding.Static(Some(JStr "eng"))) ]))
      "invoke-noargs", rb (Binding.Invoke("cap.x", []))
      "invoke-args", rb (Binding.Invoke("cap.x", [ { Addr = "b"; Value = "2" }; { Addr = "a"; Value = "1\n" } ]))

      // ── Every other TreeOp case ──
      "editnode", TreeOp.EditNode(NodeId "m", md.Kind)
      "updateprop-native-null", TreeOp.UpdateProp(NodeId "m", "Label", PropValue.Native nullObj)
      "updateprop-native-string", TreeOp.UpdateProp(NodeId "m", "Label", PropValue.Native(box "x\ty"))
      "updateprop-native-int", TreeOp.UpdateProp(NodeId "m", "Rows", PropValue.Native(box 3))
      "updateprop-native-int64", TreeOp.UpdateProp(NodeId "m", "Rows", PropValue.Native(box 5L))
      "updateprop-native-float", TreeOp.UpdateProp(NodeId "m", "Rows", PropValue.Native(box 3.5))
      "updateprop-native-nan", TreeOp.UpdateProp(NodeId "m", "Rows", PropValue.Native(box nan))
      "updateprop-native-bool", TreeOp.UpdateProp(NodeId "m", "Rows", PropValue.Native(box false))
      "updateprop-native-opaque", TreeOp.UpdateProp(NodeId "m", "Rows", PropValue.Native(box [ "a" ]))
      "updateprop-wire",
      TreeOp.UpdateProp(
          NodeId "m",
          "Columns[0].Label",
          PropValue.Wire(JObj [ "z", JArr [ JInt 1; JFloat 0.5; JStr "s" ]; "a", JBool true ])
      )
      "updatestyle-default", TreeOp.UpdateStyle(NodeId "m", Defaults.style)
      "updatestyle-tone",
      TreeOp.UpdateStyle(
          NodeId "m",
          { Defaults.style with
              Tone = ToneVariant.Critical
              Emphasis = Emphasis.Loud }
      )
      "patchstyle-empty", TreeOp.PatchStyle(NodeId "m", StyleDelta.empty)
      "patchstyle-tone",
      TreeOp.PatchStyle(
          NodeId "m",
          { StyleDelta.empty with
              Tone = Some ToneVariant.Default
              Emphasis = Some Emphasis.Quiet }
      )
      "updatestate-empty",
      TreeOp.UpdateState(
          NodeId "m",
          { OnLoading = None
            OnEmpty = None
            OnError = None }
      )
      "updatestate-nodes",
      TreeOp.UpdateState(
          NodeId "m",
          { OnLoading = Some(Fuaran.markdown "l" "Loading")
            OnEmpty = Some(Fuaran.markdown "e" "Empty")
            OnError = None }
      )
      "updateaccessibility",
      TreeOp.UpdateAccessibility(
          NodeId "m",
          { DescribedBy = Some "d"
            Hidden = Some(Binding.State("h", Some true))
            Label = Some(Binding.Static(Some "L"))
            LabelledBy = None
            LiveRegion = None
            Role = None
            Speak = None }
      )
      "updatetooltip-some", TreeOp.UpdateTooltip(NodeId "m", Some(TextSource.Literal "Tip"))
      "updatetooltip-none", TreeOp.UpdateTooltip(NodeId "m", None)
      "insertchild", TreeOp.InsertChild(NodeId "dash", md)
      "removenode", TreeOp.RemoveNode(NodeId "m\"q")
      "movenode", TreeOp.MoveNode(NodeId "m", NodeId "dash")
      "reorderchildren", TreeOp.ReorderChildren(NodeId "dash", [ NodeId "right"; NodeId "left" ])
      "reorderchildren-empty", TreeOp.ReorderChildren(NodeId "dash", [])
      "replaceroot", TreeOp.ReplaceRoot(buildDashboard ())
      "batch-empty", TreeOp.Batch []
      "batch-nested",
      TreeOp.Batch
          [ TreeOp.RemoveNode(NodeId "left")
            TreeOp.Batch [ TreeOp.MoveNode(NodeId "right", NodeId "dash") ]
            rb (Binding.State("k", Some(box "v"))) ] ]
