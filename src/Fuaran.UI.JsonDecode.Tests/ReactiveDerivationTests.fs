module Fuaran.UI.JsonDecode.Tests.ReactiveDerivation

// ============================================================================
//  Phase 818 — the reactive-derivation first cut, one rule: any read slot that
//  today takes a literal may take a Binding; the runtime evaluates bindings
//  with subscription semantics; the Transform verb set is the only computation
//  vocabulary.
//
//  These tests pin the wire behaviour of the family's four shapes:
//    O1 — LIVE Transform sources (State / Selection / Query preserved, not
//         snapshotted; canonical re-encode byte-for-byte; live re-evaluation
//         against the stores; snapshot fallback when nothing is written);
//    O2 — `Switch.on` (shipped by Phase 768 — smoke-pinned here as part of
//         the family);
//    O3 — `SetState.valueFrom` (value XOR valueFrom, dispatch-time
//         evaluation, didactic on both-present);
//    plus the `sortStateKey` grid-sort affordance (descriptor read + the
//    shared row-sort the client renderer and SSR hosts key off).
// ============================================================================

// Test fixtures box raw store values (`box "ORD-17"`, `box 42.5`) into the
// obj-erased Binding stores — the same erasure boundary BindingResolver
// documents; the F# 10 nullness checker flags every such `box` on a bare
// `obj` (FS3261), per the established file-scoped precedents.
#nowarn "3261"

open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Renderer

let private decodeOk (label: string) (json: string) =
    match JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "%s: decode failed: %A" label e

let private roundTrips (label: string) (json: string) =
    let n = decodeOk label json
    Expect.equal (CanonicalJson.encodeNode n) json (label + ": canonical decode→encode is the identity")
    n

// ─── O1 — live Transform sources ────────────────────────────────────────────

let private rowMajor =
    """[{"medication":"Amoxicillin","quantity":20},{"medication":"Ibuprofen","quantity":50}]"""

let private badgeWith (source: string) =
    sprintf
        """{"id":"count-badge","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"medication"}],"keys":[]}],"source":%s}},"variant":"Neutral"}}"""
        source

let private labelBinding (n: Node<obj>) : Binding<string> =
    match n.Kind with
    | NodeKind.Badge b ->
        (match b.Label with
         | TextSource.Bound binding -> binding
         | other -> failtestf "expected a Bound label, got %A" other)
    | other -> failtestf "expected a Badge, got %A" other

[<Tests>]
let liveTransformTests =
    testList
        "Phase 818 — O1: live Transform sources"
        [ test "a State-shaped source round-trips byte-for-byte AND decodes to a preserved live binding" {
              let wire =
                  badgeWith (sprintf """{"$type":"State","defaultValue":%s,"key":"request-log"}""" rowMajor)

              let n = roundTrips "state-sourced Transform" wire

              match labelBinding n with
              | Binding.Transform(TransformSource.Live(Binding.State("request-log", Some _), Embedded _), _, _) -> ()
              | other -> failtestf "expected a Live State-sourced Transform with an Embedded initial, got %A" other
          }

          test "an EMPTY-array State default decodes to the empty table (0.23.1) and round-trips" {
              // The organic Tier-D r0 shape (terra): a live count over an
              // initially-empty log. Zero rows have no columns to infer, so
              // this maps to the empty table exactly as Query/Selection do.
              let wire = badgeWith """{"$type":"State","defaultValue":[],"key":"request-log"}"""

              let n = roundTrips "empty-array state-sourced Transform" wire

              match labelBinding n with
              | Binding.Transform(TransformSource.Live(Binding.State("request-log", Some _), Embedded t), _, _) ->
                  Expect.equal (Table.rowCount t) 0 "empty default ⇒ the empty table"
              | other -> failtestf "expected a Live State-sourced Transform with the empty initial, got %A" other
          }

          test "a Query-shaped source decodes live with the EMPTY initial snapshot" {
              let wire = badgeWith """{"$type":"Query","name":"request-log"}"""
              let n = roundTrips "query-sourced Transform" wire

              match labelBinding n with
              | Binding.Transform(TransformSource.Live(Binding.Query("request-log", _, _), Embedded t), _, _) ->
                  Expect.equal (Table.rowCount t) 0 "no carried data ⇒ the empty table"
              | other -> failtestf "expected a Live Query-sourced Transform, got %A" other
          }

          test "a Selection-shaped source decodes live and round-trips" {
              let wire = badgeWith """{"$type":"Selection","field":"id","nodeId":"orders-grid"}"""
              let n = roundTrips "selection-sourced Transform" wire

              match labelBinding n with
              | Binding.Transform(TransformSource.Live(Binding.Selection("orders-grid", _, _, Some "id"), _), _, _) ->
                  ()
              | other -> failtestf "expected a Live Selection-sourced Transform, got %A" other
          }

          test "the client runtime re-evaluates when the source key's state changes (subscription semantics)" {
              let wire =
                  badgeWith (sprintf """{"$type":"State","defaultValue":%s,"key":"request-log"}""" rowMajor)

              let binding = labelBinding (decodeOk "live badge" wire)

              let resolveWith (sources: BindingResolver.BindingSources) =
                  match BindingResolver.resolveScalarText sources binding with
                  | BindingResolver.Resolved s -> s
                  | other -> failtestf "expected Resolved, got %A" other

              // Unwritten store: the count derives from the carried defaults.
              Expect.equal (resolveWith BindingResolver.empty) "2" "initial snapshot: two rows"

              // A written store (the shape a SetState leaves after the JVal
              // round-trip on the .NET side): the SAME binding derives the new
              // count — no re-decode, no re-encode.
              let written =
                  Fuaran.UI.Ops.Types.JValObj.toObj (
                      JArr
                          [ JObj [ "medication", JStr "Amoxicillin"; "quantity", JInt 20 ]
                            JObj [ "medication", JStr "Ibuprofen"; "quantity", JInt 50 ]
                            JObj [ "medication", JStr "Paracetamol"; "quantity", JInt 12 ] ]
                  )

              let sources =
                  { BindingResolver.empty with
                      State = Map.ofList [ "request-log", written ] }

              Expect.equal (resolveWith sources) "3" "a state write re-derives the count live"
          }

          test "a live source resolving to a NON-tabular value errors loudly (never a silent wrong value)" {
              let wire =
                  badgeWith (sprintf """{"$type":"State","defaultValue":%s,"key":"request-log"}""" rowMajor)

              let binding = labelBinding (decodeOk "live badge" wire)

              let sources =
                  { BindingResolver.empty with
                      State = Map.ofList [ "request-log", box "not-a-table" ] }

              match BindingResolver.resolveScalarText sources binding with
              | BindingResolver.Errored m -> Expect.isTrue (m.Contains "live source") "the error names the live source"
              | other -> failtestf "expected Errored, got %A" other
          } ]

// ─── O2 — Switch.on (Phase 768; family smoke) ───────────────────────────────

[<Tests>]
let switchOnTests =
    testList
        "Phase 818 — O2: Switch.on (family smoke; shipped by Phase 768)"
        [ test "a Switch selecting on a Selection binding round-trips" {
              let wire =
                  """{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"a","kind":{"$type":"Markdown","text":"A"}},"match":"open"}],"default":{"id":"d","kind":{"$type":"Markdown","text":"D"}},"on":{"$type":"Selection","field":"status","nodeId":"grid-1"}}}"""

              roundTrips "Switch.on Selection" wire |> ignore
          }

          test "the compact stateKey spelling still decodes and stays canonical" {
              let wire =
                  """{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"a","kind":{"$type":"Markdown","text":"A"}},"match":"open"}],"default":{"id":"d","kind":{"$type":"Markdown","text":"D"}},"stateKey":"view"}}"""

              roundTrips "Switch stateKey" wire |> ignore
          } ]

// ─── O3 — SetState.valueFrom ────────────────────────────────────────────────

let private buttonWith (onClick: string) =
    sprintf """{"id":"b1","kind":{"$type":"Button","label":"Go","onClick":%s,"variant":"Primary"}}""" onClick

[<Tests>]
let setStateValueFromTests =
    testList
        "Phase 818 — O3: SetState.valueFrom"
        [ test "the valueFrom shape round-trips byte-for-byte" {
              let wire =
                  buttonWith
                      """{"$type":"SetState","key":"chosen-id","valueFrom":{"$type":"Selection","field":"id","nodeId":"orders-grid"}}"""

              let n = roundTrips "SetState.valueFrom" wire

              match n.Kind with
              | NodeKind.Button { OnClick = Action.SetState("chosen-id", None, Some(Binding.Selection _)) } -> ()
              | other -> failtestf "expected SetState with a Selection valueFrom, got %A" other
          }

          test "the literal value shape is unchanged (byte-identical to pre-818)" {
              let wire = buttonWith """{"$type":"SetState","key":"open","value":true}"""
              let n = roundTrips "SetState.value" wire

              match n.Kind with
              | NodeKind.Button { OnClick = Action.SetState("open", Some(JBool true), None) } -> ()
              | other -> failtestf "expected a literal SetState, got %A" other
          }

          test "BOTH value and valueFrom is refused with a didactic naming both fields" {
              let wire =
                  buttonWith
                      """{"$type":"SetState","key":"k","value":"lit","valueFrom":{"$type":"State","key":"other"}}"""

              match JsonDecode.decodeNodeObj wire with
              | Ok _ -> failtest "both-present must not decode"
              | Error e ->
                  Expect.equal e.Code "WRONG_TYPE" "WRONG_TYPE"
                  Expect.stringContains e.Message "both 'value' and 'valueFrom'" "names both fields"

                  Expect.isTrue
                      (e.ExpectedShape
                       |> Option.exists (fun ex -> ex.Contains "valueFrom" && ex.Contains "value"))
                      "the expected-shape hint teaches both alternatives"
          }

          test "NEITHER value nor valueFrom is a MISSING_FIELD naming the alternative" {
              let wire = buttonWith """{"$type":"SetState","key":"k"}"""

              match JsonDecode.decodeNodeObj wire with
              | Ok _ -> failtest "neither-present must not decode"
              | Error e ->
                  Expect.equal e.Code "MISSING_FIELD" "MISSING_FIELD"

                  Expect.isTrue
                      (e.ExpectedShape |> Option.exists (fun ex -> ex.Contains "valueFrom"))
                      "the hint names the valueFrom alternative"
          }

          test "resolveJVal derives the written value from the selection store at dispatch time" {
              let binding: Binding<JVal> =
                  Binding.Selection("orders-grid", Binding.projectSelectionField<JVal> "id", None, Some "id")

              let selectedRow: Row = Map.ofList [ "id", box "ORD-17"; "total", box 42.5 ]

              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "orders-grid", box selectedRow ] }

              match BindingResolver.resolveJVal sources binding with
              | BindingResolver.Resolved(JStr "ORD-17") -> ()
              | other -> failtestf "expected Resolved (JStr ORD-17), got %A" other
          } ]

// ─── sortStateKey — the grid-sort affordance ────────────────────────────────

[<Tests>]
let sortStateKeyTests =
    testList
        "Phase 818 — sortStateKey (data-bound grid sort)"
        [ test "a sortStateKey grid round-trips byte-for-byte" {
              let wire =
                  """{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"month","kind":{"$type":"Text"},"label":"Month"},{"field":"revenue","kind":{"$type":"Numeric"},"label":"Revenue"}],"rowKeyField":"month","sortStateKey":"inventory-sort","source":{"$type":"State","key":"inventory"}}}"""

              let n = roundTrips "sortStateKey grid" wire

              match n.Kind with
              | NodeKind.DataGrid g -> Expect.equal g.SortStateKey (Some "inventory-sort") "the key decodes"
              | other -> failtestf "expected a DataGrid, got %A" other
          }

          test "an undeclared grid's wire is unchanged (sortStateKey omitted)" {
              let wire =
                  """{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"month","kind":{"$type":"Text"},"label":"Month"}],"rowKeyField":"month","source":{"$type":"State","key":"inventory"}}}"""

              roundTrips "plain grid" wire |> ignore
          }

          test "readSortDescriptor validates rather than trusts the state value" {
              let sources (v: obj) =
                  { BindingResolver.empty with
                      State = Map.ofList [ "sort", v ] }

              let descriptor = JObj [ "column", JInt 1; "direction", JStr "desc" ]

              Expect.equal
                  (BindingResolver.readSortDescriptor (sources (box descriptor)) "sort")
                  (Some(1, SortDirection.Desc))
                  "a well-formed descriptor reads"

              Expect.equal
                  (BindingResolver.readSortDescriptor
                      (sources (box (JObj [ "column", JInt -1; "direction", JStr "asc" ])))
                      "sort")
                  None
                  "a negative column is ignored (authored order stands)"

              Expect.equal
                  (BindingResolver.readSortDescriptor
                      (sources (box (JObj [ "column", JInt 0; "direction", JStr "sideways" ])))
                      "sort")
                  None
                  "an unknown direction is ignored"

              Expect.equal (BindingResolver.readSortDescriptor BindingResolver.empty "sort") None "no state ⇒ no sort"
          }

          test "sortRowsByDescriptor sorts by the addressed column's field; empty cells last; field-less columns inert" {
              let col (field: string option) : ColumnErased<obj> =
                  { Label = "c"
                    Value = None
                    Field = field
                    Format = CellFormat.None
                    Kind = CellKindErased.Text
                    Width = ColumnWidth.Auto }

              let row (m: string) (r: float option) : Row =
                  match r with
                  | Some v -> Map.ofList [ "month", box m; "revenue", box v ]
                  | None -> Map.ofList [ "month", box m ]

              let rows = [ row "Jan" (Some 20.0); row "Feb" None; row "Mar" (Some 5.0) ]
              let columns = [ col (Some "month"); col (Some "revenue") ]

              let months (rs: Row list) =
                  rs |> List.map (fun r -> string r["month"])

              Expect.equal
                  (months (BindingResolver.sortRowsByDescriptor columns (Some(1, SortDirection.Asc)) rows))
                  [ "Mar"; "Jan"; "Feb" ]
                  "ascending by revenue, the unmeasured row LAST"

              Expect.equal
                  (months (BindingResolver.sortRowsByDescriptor columns (Some(1, SortDirection.Desc)) rows))
                  [ "Jan"; "Mar"; "Feb" ]
                  "descending by revenue, the unmeasured row still last"

              Expect.equal
                  (months (BindingResolver.sortRowsByDescriptor [ col None ] (Some(0, SortDirection.Asc)) rows))
                  [ "Jan"; "Feb"; "Mar" ]
                  "a field-less closure column cannot be honoured — authored order stands"

              Expect.equal
                  (months (BindingResolver.sortRowsByDescriptor columns None rows))
                  [ "Jan"; "Feb"; "Mar" ]
                  "no descriptor ⇒ natural source order"
          } ]
