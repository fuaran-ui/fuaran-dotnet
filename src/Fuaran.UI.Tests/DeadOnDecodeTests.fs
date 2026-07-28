module Fuaran.UI.Tests.DeadOnDecode

// ============================================================================
//  Phase 430 — the slot capability table + the dead-on-decode lint.
//
//  Completeness: every slot the decoders reconstruct as a placeholder
//  (`SlotCapability.decoderPlaceholderSlots`, the WIRE_FORMAT §4 enumeration)
//  has exactly one capability row — a new placeholder without a row fails
//  here, so the dead-on-decode class cannot silently grow.
//
//  Lint: sentinel slots on a decoded-shaped tree produce FUARAN080/081
//  findings naming the declarative remedy; a fully-declarative (423–428
//  idiom) tree and the HostOnlyByDesign escapes stay silent.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

type private Msg = NoOp

let private node (id: string) (kind: NodeKind<Msg>) : Node<Msg> =
    { Id = NodeId id
      Kind = kind
      State = Defaults.stateBehaviour<Msg>
      Style = Defaults.style
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

let private stack (id: string) (children: Node<Msg> list) : Node<Msg> =
    node
        id
        (NodeKind.Box(
            { Layout =
                BoxLayout.Flex
                    { Direction = Vertical
                      Wrap = false
                      Gap = None }
              Role = BoxRole.Group
              Heading = None
              Children = children }
        ))

/// The shape a decoded closure takes — an inert placeholder.
let private placeholder: 'a -> Action<Msg> = fun _ -> Action.Chain []

[<Tests>]
let tests =
    testList
        "Phase 430 — slot capability table + dead-on-decode lint"
        [ test "the capability table covers every decoder placeholder slot exactly once" {
              let slots = SlotCapability.all |> List.map _.Slot

              Expect.equal (List.length slots) (List.length (List.distinct slots)) "no duplicate rows"

              for placeholderSlot in SlotCapability.decoderPlaceholderSlots do
                  Expect.isTrue
                      (slots |> List.contains placeholderSlot)
                      (sprintf
                          "decoder placeholder slot '%s' has no capability row — add one (WriteBack / FieldName / ResultTarget / HostOnlyByDesign)"
                          placeholderSlot)
          }

          test "a placeholder slot WITHOUT a row is detected (the probe the completeness seam promises)" {
              let covered (slot: string) =
                  SlotCapability.all |> List.exists (fun r -> r.Slot = slot)

              Expect.isFalse (covered "ProbeSpec.onProbe") "an unregistered slot is uncovered — the seam works"
          }

          test "the lint flags sentinel event, display, and call slots with declarative remedies" {
              let deadForm =
                  node
                      "frm"
                      (NodeKind.Form(
                          { Fields =
                              [ { Id = "name"
                                  Label = TextSource.Literal "Name"
                                  Kind = FormFieldKind.Text(Binding.Static(Some ""), Some placeholder)
                                  Required = false
                                  Help = None } ]
                            OnSubmit = Action.Chain []
                            SubmitLabel = TextSource.Literal "Save"
                            Disabled = None }
                      ))

              let closureGrid =
                  node
                      "grid"
                      (NodeKind.DataGrid(
                          { Source = Binding.Static(Some Seq.empty)
                            RowKey = Some(fun _ -> "<closure>")
                            RowKeyField = None
                            Columns =
                              [ { Label = "Dept"
                                  Value = Some(fun _ -> CellValue.Empty)
                                  Field = None
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto } ]
                            OnRowClick = None
                            Editable = false
                            StaticRows = None }
                      ))

              let closureCall =
                  node
                      "fetch"
                      (NodeKind.Button(
                          { Defaults.button<Msg> with
                              Label = TextSource.Literal "Fetch"
                              OnClick = Action.Call(ApiEndpoint "/api/x", Some(fun (_: obj) -> NoOp), None) }
                      ))

              let findings =
                  DeadOnDecode.lint (stack "root" [ deadForm; closureGrid; closureCall ])

              let byCode code =
                  findings |> List.filter (fun f -> f.Code = code)

              Expect.isTrue
                  (byCode "FUARAN080"
                   |> List.exists (fun f -> f.Node = "frm" && f.Slot = "FormFieldKind.onChange"))
                  "sentinel form handler flagged"

              Expect.isTrue
                  (byCode "FUARAN081"
                   |> List.exists (fun f -> f.Node = "grid" && f.Slot = "ColumnErased.value"))
                  "sentinel column accessor flagged"

              Expect.isTrue
                  (byCode "FUARAN081"
                   |> List.exists (fun f -> f.Node = "grid" && f.Slot = "GridSpec.rowKey"))
                  "sentinel row key flagged"

              Expect.isTrue
                  (byCode "FUARAN080"
                   |> List.exists (fun f -> f.Node = "fetch" && f.Slot = "Action.Call.onResult"))
                  "sentinel call continuation flagged"

              for f in findings do
                  Expect.isTrue (f.Remedy.Length > 0) "every finding names a remedy"
          }

          test "a fully-declarative tree (423–428 idioms) lints clean; HostOnly escapes stay silent" {
              let declarativeForm =
                  node
                      "frm"
                      (NodeKind.Form(
                          { Fields =
                              [ { Id = "name"
                                  Label = TextSource.Literal "Name"
                                  Kind = FormFieldKind.Text(Binding.State("name", Some ""), None)
                                  Required = false
                                  Help = None } ]
                            OnSubmit = Action.Chain []
                            SubmitLabel = TextSource.Literal "Save"
                            Disabled = None }
                      ))

              let fieldGrid =
                  node
                      "grid"
                      (NodeKind.DataGrid(
                          { Source = Binding.Static(Some Seq.empty)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns =
                              [ { Label = "Dept"
                                  Value = None
                                  Field = Some "dept"
                                  Format = CellFormat.None
                                  Kind = CellKindErased.Text
                                  Width = ColumnWidth.Auto } ]
                            OnRowClick = None
                            Editable = false
                            StaticRows = None }
                      ))

              let declarativeFetch =
                  node
                      "fetch"
                      (NodeKind.Button(
                          { Defaults.button<Msg> with
                              Label = TextSource.Literal "Fetch"
                              OnClick = Action.Call(ApiEndpoint "/api/x", None, Some(CallResultTarget.IntoState "x")) }
                      ))

              // A FileUpload's onSelect is HostOnlyByDesign — silent.
              let upload =
                  node
                      "up"
                      (NodeKind.FileUpload(
                          { Label = TextSource.Literal "Upload"
                            Accept = []
                            Multiple = false
                            OnSelect = (fun _ -> Action.Chain [])
                            Disabled = None }
                      ))

              let findings =
                  DeadOnDecode.lint (stack "root" [ declarativeForm; fieldGrid; declarativeFetch; upload ])

              Expect.isEmpty findings "a declarative-floor tree lints clean"
          } ]
