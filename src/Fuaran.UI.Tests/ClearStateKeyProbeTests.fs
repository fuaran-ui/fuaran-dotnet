module Fuaran.UI.Tests.ClearStateKeyProbe

// ============================================================================
//  Phase 1783 — what "this filter is cleared" IS on the wire.
//
//  The charter walk for a declarative clear-a-state-key form needed the
//  irreducibility question decided BY CONSTRUCTION rather than by reading the
//  types: given that the wire model has no null (WIRE_FORMAT.md rule 4 —
//  absence is structural, expressed by a missing key), does an `Action.SetState`
//  that a model CAN already spell return a filtered consumer to its unfiltered
//  state on the reference host?
//
//  These tests decode real wire documents, validate them, and resolve their
//  rows binding through the reference resolver under each store state a Clear
//  control could produce. They are the phase's evidence, so they are written to
//  go RED if any of these semantics changes — which is the point: the charter's
//  verdict is only as durable as the behaviour it was decided against.
//
//  What they establish, and it is not one answer but two:
//
//    * A LIST-shaped param (a multi-select chip, a deselect-all control) IS
//      clearable declaratively. An empty selection resolves to UNBOUND rather
//      than to an empty membership set, so the dependent filter step is pruned
//      and the consumer shows the unfiltered table. `SetState` to the empty
//      list is the spelling.
//
//    * A SCALAR param (a search box, a single-select chip) is NOT. The only
//      thing that unbinds a scalar param is the key being ABSENT, and no
//      `SetState` can make a key absent: the empty string binds as a cell and
//      the filter survives to compare against it, which EMPTIES the table
//      rather than unfiltering it — the opposite of the intent. A declared
//      `defaultValue` does not help either: it is a resolved value like any
//      other, so it is a constraint, not the absence of one.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// The repo's boxing helper for a store value: `BindingSources.State` holds
/// non-nullable `obj`, and this tier compiles nullness-ON with warnings as
/// errors. Same shape the Phase 424 param suite uses.
let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

/// A grid whose rows are a Transform over a two-row embedded table, filtered by
/// a SCALAR param sourced from the state key `dept`. Byte-shaped after the
/// corpus fixture `nodes/grid-transform-param.json`, with the param's source
/// swapped from a `Filter` to a `State` — which is the source a Clear button's
/// `Action.SetState` can actually write.
let private scalarParamDoc (stateBinding: string) =
    """{"id":"clear-probe-scalar","kind":{"$type":"DataGrid","columns":[],"rowKey":"<closure>","source":{"$type":"Transform","params":[{"from":"""
    + stateBinding
    + ""","name":"dept"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}}}],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,90]},"dept":{"validity":[true,true],"values":["eng","sales"]}},"schema":[{"name":"dept","type":"string"},{"name":"amount","type":"int"}]}}}}"""

/// The same table, filtered by a LIST param through the membership test's
/// `param` form — the multi-select chip wiring of `nodes/multiselect-chip-list-param.json`.
let private listParamDoc =
    """{"id":"clear-probe-list","kind":{"$type":"DataGrid","columns":[],"rowKey":"<closure>","source":{"$type":"Transform","params":[{"from":{"$type":"State","key":"depts"},"name":"depts"}],"pipeline":[{"$type":"filter","pred":{"$type":"in","expr":{"$type":"col","name":"dept"},"param":"depts"}}],"source":{"columns":{"amount":{"validity":[true,true],"values":[100,90]},"dept":{"validity":[true,true],"values":["eng","sales"]}},"schema":[{"name":"dept","type":"string"},{"name":"amount","type":"int"}]}}}}"""

/// Decode the document, run the pre-emit validator over it, and hand back the
/// grid's rows binding. A decode or validate failure fails the test: the
/// evidence is only evidence if the document is one a host would actually
/// accept, which is why this probe does not construct the tree in F#.
let private decodedRowsBinding (json: string) : Binding<Fuaran.Core.Row seq> =
    match Fuaran.UI.Generated.decodeNode json with
    | Error e -> failtestf "the probe document did not decode: %s" e
    | Ok node ->
        match Fuaran.UI.PreEmitValidate.validate node with
        | Error defects -> failtestf "the probe document did not validate: %A" defects
        | Ok() ->
            match node.Kind with
            | Fuaran.UI.Generated.NodeKind.DataGrid spec -> spec.Source
            | other -> failtestf "expected a DataGrid, got %A" other

let private rowCount (binding: Binding<Fuaran.Core.Row seq>) (sources: BindingResolver.BindingSources) : int =
    match BindingResolver.resolve sources binding with
    | BindingResolver.Resolved rows -> Seq.length rows
    | other -> failtestf "expected Resolved, got %A" other

let private withState (pairs: (string * obj) list) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList pairs }

let private plainState = """{"$type":"State","key":"dept"}"""

let private defaultedState =
    """{"$type":"State","defaultValue":"eng","key":"dept"}"""

[<Tests>]
let tests =
    testList
        "Phase 1783 — what a cleared state key is on the wire"
        [
          // ── The baseline: ABSENCE is what unfilters a scalar param ──────────
          test "an ABSENT scalar state key prunes the filter step (the unfiltered table)" {
              let b = decodedRowsBinding (scalarParamDoc plainState)
              Expect.equal (rowCount b BindingResolver.empty) 2 "both rows — nothing constrains them"
          }

          test "a WRITTEN scalar state key scopes the rows" {
              let b = decodedRowsBinding (scalarParamDoc plainState)
              Expect.equal (rowCount b (withState [ "dept", nn "eng" ])) 1 "only the eng row"
          }

          // ── The finding: no SetState reproduces that absence for a scalar ───
          test "SetState to the EMPTY STRING binds a cell and EMPTIES the table — it does not unfilter" {
              // This is the irreducibility verdict's load-bearing assertion. The
              // empty string is a value, so the filter survives the prune and
              // compares `dept = ""`, which no row satisfies. A model reaching
              // for the obvious spelling gets the opposite of the intent: not a
              // cleared filter but an empty result.
              let b = decodedRowsBinding (scalarParamDoc plainState)
              Expect.equal (rowCount b (withState [ "dept", nn "" ])) 0 "no row has an empty dept"
          }

          test "SetState to the EMPTY LIST unbinds even a SCALAR param — the unfiltered table" {
              // The decisive assertion. A param's value is lifted to a scalar
              // Cell first and to a Cell LIST second; an array is not a scalar,
              // so it falls through to the list arm, and the EMPTY array is
              // UNBOUND there. That rule was written for a deselected
              // multi-select chip, but it is keyed on the VALUE and not on how
              // the pipeline reads the name — so writing the empty list to a
              // key a scalar `=`/`param` reads prunes that filter too.
              //
              // This is what makes the intent expressible TODAY: `SetState` to
              // `[]` is a clear for any Transform param, whatever its arity.
              let b = decodedRowsBinding (scalarParamDoc plainState)
              Expect.equal (rowCount b (withState [ "dept", nn ([]: obj list) ])) 2 "both rows — the filter is pruned"
          }

          test "the empty list clears a defaulted scalar key too (it beats the declared default)" {
              // The default only applies when the key is unwritten. Once `[]` is
              // written the key IS resolved — to an empty list, which unbinds —
              // so the clear works on a key whose default would otherwise
              // re-impose a constraint.
              let b = decodedRowsBinding (scalarParamDoc defaultedState)
              Expect.equal (rowCount b (withState [ "dept", nn ([]: obj list) ])) 2 "the declared default is overridden"
          }

          test "SetState to a declared defaultValue is a CONSTRAINT, not the absence of one" {
              // A declared default resolves like any other value, so "write the
              // key back to its default" scopes the rows to that default rather
              // than restoring the unfiltered view. Where the default is itself
              // a filter value this is visibly wrong; where a phase author
              // assumed the default meant "unset", it is invisibly wrong.
              let b = decodedRowsBinding (scalarParamDoc defaultedState)
              Expect.equal (rowCount b BindingResolver.empty) 1 "the declared default filters to eng"
              Expect.equal (rowCount b (withState [ "dept", nn "eng" ])) 1 "and writing it back changes nothing"
          }

          // ── The other half: a LIST param IS clearable, and that is the teaching ──
          test "SetState to the EMPTY LIST unbinds a list param — the unfiltered table" {
              // An empty selection is UNBOUND rather than a membership set no
              // row satisfies, so deselecting everything shows every row. This
              // is the one clear a model can already spell correctly.
              let b = decodedRowsBinding listParamDoc
              Expect.equal (rowCount b (withState [ "depts", nn ([]: obj list) ])) 2 "both rows — the chip is unset"
          }

          test "a non-empty list param still scopes the rows" {
              let b = decodedRowsBinding listParamDoc
              Expect.equal (rowCount b (withState [ "depts", nn ([ nn "eng" ]: obj list) ])) 1 "only the eng row"
          }

          // ── The taught fragments, pinned against the decoder ────────────────
          //
          // The teaching this phase landed carries three JSON fragments. The two
          // it tells an author to WRITE must decode, and the one it warns
          // against must be REFUSED — a warning about a shape the decoder
          // actually accepts would be teaching a superstition. Both directions
          // are asserted, because only the pair is evidence.
          test "the taught Clear-filter button decodes" {
              let taught =
                  """{"id":"clear-service-filter","kind":{"$type":"Button","icon":"x","label":"Clear filter","onClick":{"$type":"SetState","key":"service","value":[]},"variant":"Tertiary"}}"""

              match Fuaran.UI.Generated.decodeNode taught with
              | Error e -> failtestf "the taught clear fragment does not decode: %s" e
              | Ok _ -> ()
          }

          test "the taught boolean close decodes" {
              let taught =
                  """{"id":"close-quick-add","kind":{"$type":"Button","label":"Close","onClick":{"$type":"SetState","key":"quick-add-open","value":false},"variant":"Tertiary"}}"""

              match Fuaran.UI.Generated.decodeNode taught with
              | Error e -> failtestf "the taught boolean-close fragment does not decode: %s" e
              | Ok _ -> ()
          }

          test "the near-miss the teaching warns about is genuinely REFUSED" {
              // The exact shape `stress-003` emitted: a `SetState` carrying
              // neither carrier. If this ever decoded, the teaching's headline
              // warning would be false.
              let nearMiss =
                  """{"id":"clear-service-filter","kind":{"$type":"Button","label":"Clear filter","onClick":{"$type":"SetState","key":"service"},"variant":"Tertiary"}}"""

              match Fuaran.UI.Generated.decodeNode nearMiss with
              | Ok _ -> failtest "a valueless SetState decoded — the teaching's central warning is stale"
              | Error _ -> ()
          } ]
