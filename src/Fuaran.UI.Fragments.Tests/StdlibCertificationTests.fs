module Fuaran.UI.Fragments.Tests.StdlibCertificationTests

// ============================================================================
//  The certification gate for the curated fragment library.
//
//  Every entry in `Stdlib.all` is driven through
//  `Fuaran.UI.Validator.RecipeCertification.certifyFragment` — the Phase 359
//  verification floor — which enumerates or deterministically samples the
//  fragment's value / repeat hole-space and proves the emitted tree is
//  validator-conformant for EVERY covered binding. A fragment that fails
//  reports its `(binding, defect)` counterexample.
//
//  The suite is quantified over `Stdlib.all` rather than written per fragment
//  on purpose: a new entry added to the library is certified by construction,
//  and cannot ship uncertified by being forgotten here. The per-fragment tests
//  below cover the properties the harness does not — that a slot hole has a
//  marker in the template, that the representative reference binds every
//  required hole, and that the declared effect class matches what the
//  materialised tree actually does.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Fragments
open Fuaran.UI.Validator.RecipeCertification
open Fuaran.UI.OpStream.Abstractions

// ─── Tree walk ───────────────────────────────────────────────────────────────
//
// The tier's one child enumeration (`Fuaran.UI.NodeChildren`), which lives in
// `Fuaran.UI` itself, so no renderer dependency comes with it. Phase 2752
// replaced a local copy of nine container arms with it: the copy saw no `Switch`
// arm, no state alternative and no fragment body, so a node in any of those was
// invisible to every assertion here.

let rec private descendants (node: Node<'Msg>) : Node<'Msg> list =
    node
    :: (NodeChildren.children NodeChildren.Reach.keyed node |> List.collect descendants)

/// The slot names a template body marks with an unbound `FragmentRef`.
let private slotMarkersIn (body: Node<'Msg>) : string list =
    descendants body
    |> List.choose (fun n ->
        match n.Kind with
        | NodeKind.FragmentRef spec -> Some spec.Name
        | _ -> None)

let private declSpecOf (f: StdlibFragment<'Msg>) : FragmentDeclSpec<'Msg> =
    match f.Decl.Kind with
    | NodeKind.FragmentDecl spec -> spec
    | other -> failtestf "fragment '%s' declaration is a %A, not a FragmentDecl" f.Name other

let private refSpecOf (f: StdlibFragment<'Msg>) : FragmentRefSpec<'Msg> =
    match f.Example.Kind with
    | NodeKind.FragmentRef spec -> spec
    | other -> failtestf "fragment '%s' example is a %A, not a FragmentRef" f.Name other

/// `validateValueArg` takes a non-null `obj`; `box` yields `objnull` under F#
/// 10 nullable reference types, so the boxing is narrowed once here rather than
/// at four call sites.
let private boxed (v: 'T) : obj = box v |> Unchecked.nonNull

let private isPureDeterministic (e: EffectClass) : bool =
    e.HostEffect = HostEffect.Pure
    && e.Determinism = DeterminismSource.Deterministic

// ─── Phase 2471 helpers ──────────────────────────────────────────────────────

/// A reference's scalar arguments, boxed the way the certification harness
/// hands them to `Materialize`.
let private argsOf (node: Node<'Msg>) : Map<string, obj> =
    match node.Kind with
    | NodeKind.FragmentRef spec ->
        spec.Args
        |> Option.defaultValue Map.empty
        |> Map.toList
        |> List.choose (fun (name, arg) ->
            match arg with
            | FragmentArg.Int v -> Some(name, boxed v)
            | FragmentArg.Float v -> Some(name, boxed v)
            | FragmentArg.Bool v -> Some(name, boxed v)
            | FragmentArg.Str v -> Some(name, boxed v)
            | FragmentArg.SlotArg _ -> None)
        |> Map.ofList
    | _ -> Map.empty

/// `CellFormat` carries a closure arm and so has no equality; compare the
/// printed case, which is what these tests are about.
let private fmt (c: CellFormat) : string = sprintf "%A" c

/// An option label's text; these fragments write literal labels only.
let private labelText (label: TextSource) : string =
    match label with
    | TextSource.Literal s -> s
    | other -> failwithf "not a literal label: %A" other

let private metricsIn (tree: Node<'Msg>) : MetricSpec list =
    descendants tree
    |> List.choose (fun n ->
        match n.Kind with
        | NodeKind.Metric m -> Some m
        | _ -> None)

let private chipsIn (tree: Node<'Msg>) : FilterSpec<'Msg> list =
    match tree.Kind with
    | NodeKind.Filters spec -> spec.Items
    | other -> failtestf "expected a Filters node, got %A" other

// ─── Phase 2752 helpers ──────────────────────────────────────────────────────

/// The filter chips a materialised panel declares.
let private panelControls (tree: Node<'Msg>) : FilterSpec<'Msg> list =
    descendants tree
    |> List.collect (fun n ->
        match n.Kind with
        | NodeKind.Filters spec -> spec.Items
        | _ -> [])

let private gridIn (tree: Node<'Msg>) : Node<'Msg> * DataGridSpec<'Msg> =
    descendants tree
    |> List.pick (fun n ->
        match n.Kind with
        | NodeKind.DataGrid g -> Some(n, g)
        | _ -> None)

/// The pre-emit variants a tree draws, by name — `[]` on a clean tree.
let private preEmitVariants (tree: Node<'Msg>) : string list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error defects -> defects |> List.map PreEmitValidate.describeVariant

let private expectClean (what: string) (tree: Node<'Msg>) =
    match PreEmitValidate.validate tree with
    | Ok() -> ()
    | Error defects ->
        failtestf
            "%s: pre-emit validation refused it: %s"
            what
            (defects |> List.map (fun d -> sprintf "%A" d) |> String.concat "; ")

/// A small inline table: item, units, price.
let private scenarioRows: Fuaran.Core.DataSource =
    Fuaran.Core.Embedded
        { Schema =
            [ Fuaran.Core.Field.create "item" Fuaran.Core.StringType
              Fuaran.Core.Field.create "units" Fuaran.Core.FloatType
              Fuaran.Core.Field.create "price" Fuaran.Core.FloatType ]
          Columns =
            [ Fuaran.Core.Column.ofStrs "item" (Fuaran.Core.Vector.ofList [ "north"; "south" ]) Fuaran.Core.AllValid
              Fuaran.Core.Column.ofFloats "units" (Fuaran.Core.Vector.ofList [ 100.0; 40.0 ]) Fuaran.Core.AllValid
              Fuaran.Core.Column.ofFloats "price" (Fuaran.Core.Vector.ofList [ 2.0; 5.0 ]) Fuaran.Core.AllValid ] }

/// A two-parameter revenue model: `units * (1 + growth) * (price + uplift)`.
let private revenueModel: ScenarioModel =
    { Source = scenarioRows
      Key = "item"
      Model =
        { Name = "revenue"
          Params =
            [ { Name = "units"; Default = None }
              { Name = "price"; Default = None }
              { Name = "growth"; Default = None }
              { Name = "uplift"; Default = None } ]
          Body =
            Fuaran.Compute.Binary(
                Fuaran.Compute.Mul,
                Fuaran.Compute.Binary(
                    Fuaran.Compute.Mul,
                    Fuaran.Compute.Arg "units",
                    Fuaran.Compute.Binary(
                        Fuaran.Compute.Add,
                        Fuaran.Compute.ColExpr.Lit(Fuaran.Core.Float 1.0),
                        Fuaran.Compute.Arg "growth"
                    )
                ),
                Fuaran.Compute.Binary(Fuaran.Compute.Add, Fuaran.Compute.Arg "price", Fuaran.Compute.Arg "uplift")
            ) }
      Columns = [ "units", "units"; "price", "price" ] }

let private fixedSide (label: string) (growth: float) (uplift: float) : ScenarioSide =
    { Label = label
      Parameters =
        [ "growth", Binding.Static(Some(Fuaran.Core.JFloat growth))
          "uplift", Binding.Static(Some(Fuaran.Core.JFloat uplift)) ] }

/// The live side: each parameter reads the panel control of the same name.
let private liveSide: ScenarioSide =
    { Label = "Adjusted"
      Parameters =
        [ "growth", Binding.Filter("growth", None)
          "uplift", Binding.Filter("uplift", None) ] }

/// A panel over the named numeric parameters, materialised.
let private panelOver (names: string list) : Node<unit> =
    let args =
        Map.ofList (
            ("count", boxed (List.length names))
            :: (names |> List.mapi (fun i n -> sprintf "param-%d-name" (i + 1), boxed n))
        )

    Stdlib.parameterPanel<unit>.Materialize args

let private page (children: Node<unit> list) : Node<unit> =
    Fuaran.stack
        "page"
        { Defaults.stack with
            Children = children }

let private comparisonOrFail (a: ScenarioSide) (b: ScenarioSide) : Node<unit> =
    match Stdlib.scenarioComparisonOf<unit> "compare" "Revenue" revenueModel a b with
    | Ok tree -> tree
    | Error e -> failtestf "the comparison was refused: %s" e

// ─── Phase 2753 helpers ──────────────────────────────────────────────────────

/// A nullable column of one type.
let private nullableColumn (name: string) (ty: Fuaran.Core.ColumnType) (cells: Fuaran.Core.Cell list) =
    Fuaran.Core.Column.ofCells name ty cells
    |> Result.defaultWith (fun e -> failwithf "the fixture column must build: %A" e)

let private strCell (v: string option) : Fuaran.Core.Cell =
    match v with
    | Some s -> Fuaran.Core.Str s
    | None -> Fuaran.Core.Cell.Null

let private floatCell (v: float option) : Fuaran.Core.Cell =
    match v with
    | Some f -> Fuaran.Core.Float f
    | None -> Fuaran.Core.Cell.Null

/// Seven orders. The customer names differ in case and one is missing, and the
/// totals tie and one is missing, so a sort that folded case differently, put
/// an empty cell elsewhere or broke a tie another way would show.
let private orderRows: Fuaran.Core.DataSource =
    Fuaran.Core.Embedded
        { Schema =
            [ Fuaran.Core.Field.create "order" Fuaran.Core.StringType
              Fuaran.Core.Field.create "customer" Fuaran.Core.StringType
              Fuaran.Core.Field.create "region" Fuaran.Core.StringType
              Fuaran.Core.Field.create "total" Fuaran.Core.FloatType ]
          Columns =
            [ nullableColumn
                  "order"
                  Fuaran.Core.StringType
                  ([ "o1"; "o2"; "o3"; "o4"; "o5"; "o6"; "o7" ] |> List.map (Some >> strCell))
              nullableColumn
                  "customer"
                  Fuaran.Core.StringType
                  ([ Some "beta"
                     Some "Acme"
                     None
                     Some "acme"
                     Some "Zed"
                     Some "alpha"
                     Some "Beta" ]
                   |> List.map strCell)
              nullableColumn
                  "region"
                  Fuaran.Core.StringType
                  ([ "north"; "south"; "north"; "east"; "south"; "north"; "east" ]
                   |> List.map (Some >> strCell))
              nullableColumn
                  "total"
                  Fuaran.Core.FloatType
                  ([ Some 30.0; Some 10.0; Some 30.0; None; Some 5.5; Some 120.0; Some 10.0 ]
                   |> List.map floatCell) ] }

let private regions: SelectOption list =
    [ "north"; "south"; "east" ]
    |> List.map (fun r ->
        ({ Label = TextSource.Literal r
           Value = r }
        : SelectOption))

let private ordersTable: RefinableTable =
    { Source = orderRows
      Key = "order"
      Columns =
        [ { Field = "customer"
            Label = "Customer"
            Kind = RefinableKind.Text
            Sortable = true
            Filter = None }
          { Field = "region"
            Label = "Region"
            Kind = RefinableKind.Text
            Sortable = true
            Filter = Some regions }
          { Field = "total"
            Label = "Total"
            Kind = RefinableKind.Number
            Sortable = true
            Filter = None } ]
      PageSize = 3 }

let private ordersOf (sources: string -> Fuaran.Core.Schema option) (table: RefinableTable) =
    Stdlib.refinableTableOf<unit> "orders" "Orders" sources table

let private tableOrFail (table: RefinableTable) : Node<unit> =
    match ordersOf Fuaran.Compute.SchemaWalk.noSources table with
    | Ok tree -> tree
    | Error e -> failtestf "the table was refused: %s" e

/// The host's state for one view: the sort and page descriptors the grid
/// writes, and the region chip's selection.
let private sourcesFor (sort: (int * string) option) (page: int option) (selected: string list option) =
    let state: (string * obj) list =
        [ match sort with
          | Some(column, direction) ->
              Stdlib.refinableSortKey "orders",
              boxed (Fuaran.Core.JObj [ "column", Fuaran.Core.JInt column; "direction", Fuaran.Core.JStr direction ])
          | None -> ()
          match page with
          | Some p -> Stdlib.refinablePageKey "orders", boxed (Fuaran.Core.JObj [ "page", Fuaran.Core.JInt p ])
          | None -> () ]

    let filters: (string * obj) list =
        [ match selected with
          | Some values ->
              Stdlib.refinableFilterKey "orders" "region", boxed (Fuaran.Core.JArr(values |> List.map Fuaran.Core.JStr))
          | None -> () ]

    { Fuaran.UI.Renderer.BindingResolver.empty with
        State = Map.ofList state
        Filters = Map.ofList filters }

/// The rows a grid source resolves to under `sources`.
let private resolvedRows (sources: BindingSources) (source: Binding<Row seq>) : Row list =
    match Fuaran.UI.Renderer.BindingResolver.resolveRowsWithFields sources source with
    | Fuaran.UI.Renderer.BindingResolver.Resolved rows, _ -> Seq.toList rows
    | other, _ -> failtestf "the grid source did not resolve: %A" other

/// What the grid shows under `sources`, read through the renderer's own grid
/// model (the resolved rows, sorted by the effective descriptor, sliced to the
/// clamped page), and the view the host reads off the same state.
let private shownAndView (tree: Node<unit>) (sources: BindingSources) (selected: string list) =
    let _, spec = gridIn tree

    let descriptor =
        Fuaran.UI.Renderer.BindingResolver.effectiveSortDescriptor spec.SortStateKey spec.DefaultSort sources

    let sorted =
        resolvedRows sources spec.Source
        |> Fuaran.UI.Renderer.GridModel.sortRowsByDescriptor spec.Columns descriptor

    let page =
        match Fuaran.UI.Renderer.BindingResolver.gridPage sources spec (List.length sorted) with
        | Some(_, _, page, _, _) -> page
        | None -> failtest "the grid is not paged"

    Fuaran.UI.Renderer.GridModel.sliceRowsToPage (defaultArg spec.PageSize 0) page sorted,
    ({ Sort = descriptor
       Page = page
       Filters = [ "region", selected ] }
    : RefinableView)

/// Evaluate a pipeline over the order rows with nothing bound.
let private evaluated (pipeline: Fuaran.Compute.Transform list) : Row list =
    resolvedRows
        Fuaran.UI.Renderer.BindingResolver.empty
        (Binding.Transform(TransformSource.Data orderRows, pipeline, None))

/// A row as the cells a reader sees: the key and each displayed field.
let private visible (row: Row) : Fuaran.UI.HostPrelude.CellValue list =
    [ "order"; "customer"; "region"; "total" ]
    |> List.map (Fuaran.UI.HostPrelude.RowProjection.value row)

// The certification budget. 256 bindings is comfortably above the largest
// finite hole-space in the set and low enough that the whole suite stays inside
// a normal test run; the seed is fixed so a counterexample reproduces.
[<Literal>]
let private MaxCases = 256

[<Literal>]
let private Seed = 20260826

let private library: StdlibFragment<unit> list = Stdlib.all

// ─── Tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 380 — the certified fragment library"
        [ test "the library is non-empty and the curated set is the size the charter claims" {
              Expect.isGreaterThanOrEqual
                  (List.length library)
                  6
                  "the charter promises a curated set of 6-12 fragments; fewer is not a library"

              // Phase 2753 raised the ceiling from ten: the data-in-the-interface
              // fragments (Phases 2752-2754) were each accepted as an entry of
              // their own, and twelve holds that set.
              Expect.isLessThanOrEqual
                  (List.length library)
                  12
                  "the charter promises a curated set of 6-12 fragments; more wants a second look at whether every entry earns its place"
          }

          test "fragment names and declaration ids are unique" {
              let names = library |> List.map (fun f -> f.Name)
              Expect.equal (List.distinct names |> List.length) (List.length names) "two fragments share a name"

              let ids = library |> List.map (fun f -> f.Decl.Id)

              Expect.equal
                  (List.distinct ids |> List.length)
                  (List.length ids)
                  "two declarations share a node id — the id is also the hole-address prefix, so a collision is not cosmetic"

              for f in library do
                  Expect.isNotEmpty f.Name "a fragment name is empty"
                  Expect.isNotEmpty f.Summary (sprintf "fragment '%s' carries no summary" f.Name)
          }

          testList
              "every fragment certifies valid-for-all-bindings"
              [ for f in library ->
                    test f.Name {
                        let verdict = certifyFragment f.Name f.Decl f.Materialize MaxCases Seed

                        Expect.isTrue
                            verdict.Certified
                            (sprintf "fragment '%s' failed certification: %s" f.Name (renderVerdict verdict))

                        Expect.isNone
                            verdict.Counterexample
                            (sprintf "a certified fragment carries no counterexample (%s)" f.Name)

                        // A verdict over ZERO bindings is vacuously `Certified`
                        // and says nothing at all. Pin the coverage so an
                        // enumeration that silently stops producing cases
                        // surfaces as a failure rather than as a green run.
                        let covered =
                            match verdict.Coverage with
                            | Exhaustive n -> n
                            | Sampled(drawn, _) -> drawn

                        Expect.isGreaterThan
                            covered
                            0
                            (sprintf
                                "fragment '%s' certified over ZERO bindings — a vacuous verdict, not a proof (%s)"
                                f.Name
                                (renderVerdict verdict))
                    } ]

          testList
              "the honesty boundary — an effecting fragment is certified for STRUCTURE only"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f
                        let effect = spec.Effect
                        let verdict = certifyFragment f.Name f.Decl f.Materialize MaxCases Seed

                        Expect.equal
                            verdict.StructureOnly
                            (not (isPureDeterministic effect))
                            (sprintf
                                "fragment '%s' declares %A but its verdict's StructureOnly flag disagrees — the flag IS the claim a consumer reads"
                                f.Name
                                effect)
                    } ]

          test "the set carries at least one effecting fragment, so the boundary is exercised and not merely stated" {
              let effecting =
                  library
                  |> List.filter (fun f -> declSpecOf f |> fun s -> s.Effect |> isPureDeterministic |> not)

              Expect.isNonEmpty
                  effecting
                  "an all-pure library never exercises the structure-only path, so a regression there would ship green"
          }

          testList
              "the declaration is in the CANONICAL wire shape — the ENCODER omits, not this library"
              [ for f in library ->
                    test f.Name {
                        // `WIRE_FORMAT.md`, "Parameterised fragments": `effect` is
                        // "Omitted when pure-deterministic", and a zero-hole decl
                        // omits `holes`.
                        //
                        // This test used to inspect the RECORD — `Effect` and
                        // `Holes` were options, `Some pureDeterministic` and
                        // `Some []` were expressible, and this library canonicalised
                        // them by hand on the way in. That was one library's
                        // discipline standing in for the host's rule: any other
                        // author could build the redundant form and the F# encoder
                        // emitted it verbatim, which is how the first cut of these
                        // fixtures broke the Rust host while every F# suite stayed
                        // green (F# was the encoder that produced the bytes it was
                        // checking against).
                        //
                        // Phase 1670 moved the rule into the host: both slots are
                        // omit-at-default, so the redundant form is unrepresentable
                        // and the canonicalisation happens on encode. The assertion
                        // therefore moved with it — it reads the BYTES now, which is
                        // the only place the claim was ever about.
                        let bytes = CanonicalJson.encodeNode f.Decl

                        if isPureDeterministic (declSpecOf f).Effect then
                            Expect.isFalse
                                (bytes.Contains "\"effect\"")
                                (sprintf
                                    "fragment '%s' is pure-deterministic but its canonical bytes carry an `effect` key; the canonical wire form omits it (WIRE_FORMAT, Parameterised fragments)"
                                    f.Name)

                        if List.isEmpty (declSpecOf f).Holes then
                            Expect.isFalse
                                (bytes.Contains "\"holes\"")
                                (sprintf
                                    "fragment '%s' declares no holes but its canonical bytes carry a `holes` key; the canonical form omits it"
                                    f.Name)
                    } ]

          testList
              "TOTALITY (invariant 1) — every repeat count is bounded"
              [ for f in library ->
                    test f.Name {
                        Expect.isTrue
                            (Fragment.isTotal (declSpecOf f))
                            (sprintf
                                "fragment '%s' declares an unbounded Repeat count — binding it could expand without limit"
                                f.Name)
                    } ]

          testList
              "every declared slot hole has a marker in the template body"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f

                        let declaredSlots =
                            spec.Holes
                            |> List.choose (fun h ->
                                match h with
                                | HoleDecl.Slot(n, _) -> Some n
                                | _ -> None)

                        let markers = slotMarkersIn spec.Body

                        for slot in declaredSlots do
                            Expect.contains
                                markers
                                slot
                                (sprintf
                                    "fragment '%s' declares slot '%s' but its body carries no unbound FragmentRef marker for it — the apply would silently bind nothing"
                                    f.Name
                                    slot)

                        for marker in markers do
                            Expect.contains
                                declaredSlots
                                marker
                                (sprintf
                                    "fragment '%s' body marks slot '%s' which it does not declare — the marker would survive into the rendered tree as a dangling ref"
                                    f.Name
                                    marker)
                    } ]

          testList
              "the representative reference targets its own fragment and binds every required hole"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f
                        let refSpec = refSpecOf f

                        Expect.equal
                            refSpec.Name
                            f.Name
                            (sprintf "fragment '%s' example targets a different fragment" f.Name)

                        let bound =
                            refSpec.Args |> Option.defaultValue Map.empty |> Map.toList |> List.map fst

                        for hole in Fragment.requiredHoles spec do
                            Expect.contains
                                bound
                                hole
                                (sprintf
                                    "fragment '%s' example leaves required hole '%s' unbound — the apply would refuse it"
                                    f.Name
                                    hole)

                        for name in bound do
                            let known = spec.Holes |> List.exists (fun h -> HoleDecl.name h = name)

                            Expect.isTrue
                                known
                                (sprintf
                                    "fragment '%s' example binds '%s', which the declaration has no hole for"
                                    f.Name
                                    name)
                    } ]

          testList
              "every value argument in the representative reference satisfies its hole's value-space"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f
                        let refSpec = refSpecOf f

                        for (name, arg) in refSpec.Args |> Option.defaultValue Map.empty |> Map.toList do
                            match arg with
                            | FragmentArg.SlotArg _ -> () // a slot's shape is checked by the apply, not a value-space
                            | FragmentArg.Int v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = %d is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                            | FragmentArg.Float v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = %g is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                            | FragmentArg.Bool v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = %b is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                            | FragmentArg.Str v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = '%s' is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                    } ]

          test "apply builds a ref at the caller's id — the id is what keeps two applications apart" {
              let f = List.head library

              let a = Stdlib.apply<unit> "left" f.Name [ "label", FragmentArg.Str "Left" ]

              let b = Stdlib.apply<unit> "right" f.Name [ "label", FragmentArg.Str "Right" ]

              Expect.equal a.Id "left" "apply honours the caller's node id"
              Expect.equal b.Id "right" "apply honours the caller's node id"

              Expect.notEqual
                  a.Id
                  b.Id
                  "two applications of one fragment must carry distinct ids — the id is the hygiene prefix, so sharing it is how they capture each other"

              match a.Kind, b.Kind with
              | NodeKind.FragmentRef sa, NodeKind.FragmentRef sb ->
                  Expect.equal sa.Name f.Name "apply targets the named fragment"
                  Expect.equal sb.Name f.Name "apply targets the named fragment"
                  // `FragmentArg` carries a `Node`, which holds closures and so
                  // supports no equality constraint — compare the bound scalar,
                  // which is the part this test is about.
                  let labelOf (spec: FragmentRefSpec<unit>) =
                      match spec.Args |> Option.defaultValue Map.empty |> Map.tryFind "label" with
                      | Some(FragmentArg.Str s) -> s
                      | _ -> "<unbound>"

                  Expect.notEqual (labelOf sa) (labelOf sb) "the two applications bind different arguments"
              | _ -> failtest "apply must build a FragmentRef"

              // A zero-arg application carries the degenerate wire shape.
              match (Stdlib.apply<unit> "bare" f.Name []).Kind with
              | NodeKind.FragmentRef spec ->
                  Expect.isNone spec.Args "a zero-arg ref omits `args` rather than carrying an empty map"
              | _ -> failtest "apply must build a FragmentRef"
          }

          test "lookup by name finds every fragment and refuses one that is not there" {
              for f in library do
                  match Stdlib.tryFind<unit> f.Name with
                  | Some found -> Expect.equal found.Name f.Name "tryFind returned a different fragment"
                  | None -> failtestf "tryFind could not find '%s', which is in `all`" f.Name

              Expect.isNone (Stdlib.tryFind<unit> "no-such-fragment") "tryFind invented a fragment"
          }

          test "the derived signatures cover the library and carry each fragment's declared effect" {
              let signatures = Stdlib.signatures<unit> ()

              Expect.equal (List.length signatures) (List.length library) "one signature per fragment"

              for (f, s) in List.zip library signatures do
                  Expect.equal s.Name f.Name "signature order tracks the library"

                  let declared = (declSpecOf f).Effect

                  Expect.equal s.Effect declared (sprintf "signature for '%s' reports a different effect class" f.Name)

                  let holeCount = (declSpecOf f).Holes |> List.length
                  Expect.equal (List.length s.Holes) holeCount (sprintf "signature for '%s' lost a hole" f.Name)
          }

          // ── Phase 2471 — the kpi-strip and filter-bar holes ────────────────
          testList
              "Phase 2471 — kpi-strip and filter-bar fit real screens"
              [ test "every bound example targets a library fragment and binds only declared holes, inside their spaces" {
                    Expect.isNonEmpty (Stdlib.boundExamples<unit>) "the bound examples exist"

                    for (name, _, node) in Stdlib.boundExamples<unit> do
                        let f =
                            match Stdlib.tryFind<unit> name with
                            | Some f -> f
                            | None -> failtestf "bound example names '%s', which is not in the library" name

                        let spec = declSpecOf f

                        let args =
                            match node.Kind with
                            | NodeKind.FragmentRef r ->
                                Expect.equal r.Name name "the example targets the fragment it is filed under"
                                r.Args |> Option.defaultValue Map.empty
                            | other -> failtestf "bound example for '%s' is a %A, not a FragmentRef" name other

                        for KeyValue(hole, arg) in args do
                            let raw =
                                match arg with
                                | FragmentArg.Int v -> boxed v
                                | FragmentArg.Float v -> boxed v
                                | FragmentArg.Bool v -> boxed v
                                | FragmentArg.Str v -> boxed v
                                | FragmentArg.SlotArg _ -> failtestf "'%s' binds a slot it has none of" name

                            Expect.isOk
                                (Fragment.validateValueArg spec hole raw)
                                (sprintf "'%s' bound example: '%s' is undeclared or outside its space" name hole)
                }

                test "kpi-strip: a reference binding no new hole expands exactly as before them" {
                    let tree = Stdlib.kpiStrip<unit>.Materialize(argsOf Stdlib.kpiStrip<unit>.Example)
                    let figures = metricsIn tree

                    Expect.equal (List.length figures) 3 "the example's count"

                    for m in figures do
                        Expect.equal
                            (fmt m.Format)
                            (fmt (CellFormat.Number(Some 0, None)))
                            "the fixed format the strip always had"

                        Expect.isNone m.Trend "no trend unless one is bound"
                        Expect.isNone m.TrendFormat "no trend format unless one is bound"
                        Expect.equal m.TrendPolarity TrendPolarity.HigherIsBetter "the default polarity"
                }

                test "kpi-strip: a currency row with week-on-week trends is ONE reference" {
                    // A design's KPI row: revenue in GBP, orders, shoppers and the
                    // average order value, each against last week.
                    let row =
                        Stdlib.apply<unit>
                            "kpis"
                            Stdlib.KpiStrip
                            [ "heading", FragmentArg.Str "Yesterday"
                              "count", FragmentArg.Int 4
                              "currency", FragmentArg.Str "GBP"
                              "format-1", FragmentArg.Str "currency"
                              "format-4", FragmentArg.Str "currency"
                              for i in 1..4 do
                                  sprintf "trend-%d" i, FragmentArg.Str "percent-1" ]

                    let figures = metricsIn (Stdlib.kpiStrip<unit>.Materialize(argsOf row))

                    Expect.equal
                        (figures |> List.map (fun m -> fmt m.Format))
                        ([ CellFormat.Currency("GBP", None, None)
                           CellFormat.Number(Some 0, None)
                           CellFormat.Number(Some 0, None)
                           CellFormat.Currency("GBP", None, None) ]
                         |> List.map fmt)
                        "per-figure formats"

                    for (i, m) in List.indexed figures do
                        match m.Trend with
                        | Some(Binding.State(key, _)) ->
                            Expect.equal key (sprintf "kpi-%d-trend" (i + 1)) "the trend reads its figure's state key"
                        | other -> failtestf "figure %d trend is %A, not a state binding" (i + 1) other

                        Expect.equal
                            (Option.map fmt m.TrendFormat)
                            (Some(fmt (CellFormat.Percent(Some 1, None))))
                            "the trend reads as a percent"
                }

                test "kpi-strip: polarity and an own-format trend bind per figure; a blank currency falls back" {
                    let b =
                        Map.ofList
                            [ "count", boxed 2
                              "currency", boxed "   "
                              "format-1", boxed "currency"
                              "trend-2", boxed "value"
                              "polarity-2", boxed "lower-is-better" ]

                    match metricsIn (Stdlib.kpiStrip<unit>.Materialize b) with
                    | [ first; second ] ->
                        Expect.equal
                            (fmt first.Format)
                            (fmt (CellFormat.Currency("USD", None, None)))
                            "a blank code is never emitted (FUARAN061)"

                        Expect.isSome second.Trend "trend bound"
                        Expect.isNone second.TrendFormat "`value` reads the trend in the figure's own format"
                        Expect.equal second.TrendPolarity TrendPolarity.LowerIsBetter "polarity bound"
                    | other -> failtestf "expected two figures, got %d" (List.length other)
                }

                test "filter-bar: a reference binding no new hole is the search + status bar it always was" {
                    let chips =
                        chipsIn (Stdlib.filterBar<unit>.Materialize(argsOf Stdlib.filterBar<unit>.Example))

                    Expect.equal (chips |> List.map (fun c -> c.Name)) [ "search"; "status" ] "the default shape"
                }

                test "filter-bar: a two-choice bar with no search chip is ONE reference" {
                    // A design's Site / Comparison bar.
                    let bar =
                        Stdlib.apply<unit>
                            "filters"
                            Stdlib.FilterBar
                            [ "search", FragmentArg.Str "hidden"
                              "choices", FragmentArg.Int 2
                              "choice-1-label", FragmentArg.Str "Site"
                              "choice-1-key", FragmentArg.Str "site"
                              "choice-1-options", FragmentArg.Str "=Both sites|north|south"
                              "choice-2-label", FragmentArg.Str "Comparison"
                              "choice-2-key", FragmentArg.Str "compare"
                              "choice-2-options", FragmentArg.Str "lw=vs last week|ly=vs last year" ]

                    match chipsIn (Stdlib.filterBar<unit>.Materialize(argsOf bar)) with
                    | [ site; compare ] ->
                        Expect.equal (site.Name, compare.Name) ("site", "compare") "each chip writes its own key"

                        let optionsOf (c: FilterSpec<unit>) =
                            match c.Kind with
                            | FormFieldKind.Choice(Binding.Static(Some o), _, _) ->
                                o |> List.map (fun o -> o.Value, labelText o.Label)
                            | other -> failtestf "chip '%s' is %A, not a static choice" c.Name other

                        Expect.equal
                            (optionsOf site)
                            [ "", "Both sites"; "north", "north"; "south", "south" ]
                            "value=Label, bare labels and the empty value"

                        Expect.equal
                            (optionsOf compare)
                            [ "lw", "vs last week"; "ly", "vs last year" ]
                            "the second chip's options"
                    | other -> failtestf "expected two chips, got %A" (other |> List.map (fun c -> c.Name))
                }

                test "filter-bar: the options grammar is total — no string yields an option-less chip" {
                    for raw in [ ""; "|"; "||="; "a="; "=  "; "x=|y" ] do
                        Expect.isNonEmpty (Stdlib.parseFilterOptions raw) (sprintf "'%s' produced no option" raw)

                    Expect.equal
                        (Stdlib.parseFilterOptions "a=|y"
                         |> List.map (fun o -> o.Value, labelText o.Label))
                        [ "y", "y" ]
                        "an entry with a blank label is dropped"

                    Expect.equal
                        (Stdlib.parseFilterOptions "k=a=b"
                         |> List.map (fun o -> o.Value, labelText o.Label))
                        [ "k", "a=b" ]
                        "the first `=` separates value from label"
                } ]

          // ── Phase 2752 — the scenario fragments ────────────────────────────
          testList
              "Phase 2752 — a parameter panel and a two-scenario comparison"
              [ test "pre-emit validation accepts every fixture of both fragments, and the trees they yield" {
                    // A panel on its own is a set of controls nobody reads yet,
                    // and says so: each of its controls draws FUARAN074 (a
                    // Warning) until a pipeline reads it — the same check the
                    // refusal below relies on, and the shape filter-bar and
                    // confirm-action-pair already take alone. Nothing else, and
                    // nothing at Error severity, is admitted.
                    let expectAccepted (what: string) (allowedWarnings: string list) (tree: Node<unit>) =
                        match PreEmitValidate.validate tree with
                        | Ok() -> ()
                        | Error defects ->
                            for d in defects do
                                let code, severity, message = PreEmitValidate.describe d
                                let variant = PreEmitValidate.describeVariant d

                                Expect.isTrue
                                    (severity = Fuaran.Core.Severity.Warning && List.contains variant allowedWarnings)
                                    (sprintf "%s: pre-emit validation refused it: %s (%s) %s" what code variant message)

                    let panelAlone = [ "DecorativeFilter" ]

                    for f, allowed in [ Stdlib.parameterPanel<unit>, panelAlone; Stdlib.scenarioComparison<unit>, [] ] do
                        expectAccepted (sprintf "%s declaration" f.Name) allowed f.Decl
                        expectAccepted (sprintf "%s example" f.Name) allowed f.Example

                        expectAccepted
                            (sprintf "%s example, materialised" f.Name)
                            allowed
                            (f.Materialize(argsOf f.Example))

                    for (name, what, node) in Stdlib.boundExamples<unit> do
                        if name = Stdlib.ParameterPanel || name = Stdlib.ScenarioComparison then
                            expectAccepted (sprintf "%s bound example (%s)" name what) panelAlone node
                }

                test "parameter-panel: each control writes the filter key its parameter is named" {
                    let mixed =
                        Stdlib.boundExamples<unit>
                        |> List.pick (fun (n, _, node) -> if n = Stdlib.ParameterPanel then Some node else None)

                    match panelControls (Stdlib.parameterPanel<unit>.Materialize(argsOf mixed)) with
                    | [ growth; region ] ->
                        Expect.equal (growth.Name, region.Name) ("growth", "region") "control names"

                        match growth.Kind with
                        | FormFieldKind.RangedNumber(Some(Binding.Filter(key, Some start)), None, lo, hi, step) ->
                            Expect.equal key "growth" "the number control writes its own filter key"
                            Expect.equal start (Fuaran.Core.JFloat 0.05) "the default is the starting value"
                            Expect.equal (lo, hi, step) (Some -0.1, Some 0.2, Some 0.01) "bounds and step"
                        | other -> failtestf "growth is %A, not a write-back RangedNumber" other

                        match region.Kind with
                        | FormFieldKind.Choice(Binding.Static(Some options), Some(Binding.Filter(key, _)), None) ->
                            Expect.equal key "region" "the choice control writes its own filter key"

                            Expect.equal
                                (options |> List.map (fun o -> o.Value))
                                [ ""; "north"; "south" ]
                                "the filter-bar options grammar"
                        | other -> failtestf "region is %A, not a write-back Choice" other
                    | other -> failtestf "expected two controls, got %d" (List.length other)
                }

                test
                    "parameter-panel: every binding materialises — bounds ordered, default clamped, blank and repeated names handled" {
                    let b =
                        Map.ofList
                            [ "count", boxed 3
                              "param-1-name", boxed "rate"
                              "param-1-min", boxed 10.0
                              "param-1-max", boxed -10.0
                              "param-1-default", boxed 50.0
                              "param-2-name", boxed "   "
                              "param-3-name", boxed "rate" ]

                    match panelControls (Stdlib.parameterPanel<unit>.Materialize b) with
                    | [ rate; second ] ->
                        match rate.Kind with
                        | FormFieldKind.RangedNumber(Some(Binding.Filter(_, Some start)), _, lo, hi, _) ->
                            Expect.equal (lo, hi) (Some -10.0, Some 10.0) "bounds in order"
                            Expect.equal start (Fuaran.Core.JFloat 10.0) "the default clamped into them"
                        | other -> failtestf "rate is %A" other

                        Expect.equal second.Name "param-2" "a blank name falls back to param-N"
                    | other ->
                        failtestf
                            "a repeated name keeps its FIRST control only; got %A"
                            (other |> List.map (fun c -> c.Name))
                }

                test
                    "scenario-comparison: the difference is a pipeline step, and evaluating it gives both scenarios and their difference" {
                    let defined = Stdlib.scenarioPipelineDefined revenueModel

                    match List.item 3 defined with
                    | Fuaran.Compute.Derive(name,
                                            Fuaran.Compute.Binary(Fuaran.Compute.Sub,
                                                                  Fuaran.Compute.Col b,
                                                                  Fuaran.Compute.Col a)) ->
                        Expect.equal
                            (name, b, a)
                            (Stdlib.DifferenceColumn, Stdlib.ScenarioBColumn, Stdlib.ScenarioAColumn)
                            "difference = b - a"
                    | other -> failtestf "the fourth step is %A, not the difference derivation" other

                    let defines =
                        defined
                        |> List.filter (fun t ->
                            match t with
                            | Fuaran.Compute.Define _ -> true
                            | _ -> false)

                    Expect.equal (List.length defines) 1 "ONE model, defined once"

                    let env =
                        Map.ofList
                            [ Stdlib.scenarioParamName "a" "growth", Fuaran.Core.Float 0.0
                              Stdlib.scenarioParamName "a" "uplift", Fuaran.Core.Float 0.0
                              Stdlib.scenarioParamName "b" "growth", Fuaran.Core.Float 0.5
                              Stdlib.scenarioParamName "b" "uplift", Fuaran.Core.Float 1.0 ]

                    // What the comparison EMITS is the expansion: no definition and
                    // no call reach the wire, so a host whose codec carries
                    // neither still reads it.
                    let pipeline =
                        match Stdlib.scenarioPipeline revenueModel with
                        | Ok p -> p
                        | Error e -> failtestf "the model did not expand: %s" e

                    Expect.equal
                        (sprintf "%A" pipeline)
                        (sprintf "%A" (Fuaran.Compute.FnDef.expand defined |> Result.defaultValue []))
                        "the emitted pipeline is the defined one, expanded"

                    let text = sprintf "%A" pipeline
                    Expect.isFalse (text.Contains "Define") "no definition is emitted"
                    Expect.isFalse (text.Contains "Call") "no call is emitted"

                    let input =
                        match scenarioRows with
                        | Fuaran.Core.Embedded t -> t
                        | other -> failtestf "%A" other

                    match Fuaran.Compute.DataFrame.evalPipelineInEnv env pipeline input with
                    | Error e -> failtestf "the pipeline did not evaluate: %A" e
                    | Ok out ->
                        Expect.equal
                            (Fuaran.Compute.DataFrame.evalPipelineInEnv env defined input
                             |> Result.map (sprintf "%A"))
                            (Ok(sprintf "%A" out))
                            "the defined and the emitted pipeline evaluate alike"

                        let cells name =
                            match Fuaran.Core.Table.tryColumn name out with
                            | Some c -> Fuaran.Core.Column.toCells c
                            | None -> failtestf "no column '%s' in %A" name (Fuaran.Core.Table.columnNames out)

                        Expect.equal
                            (Fuaran.Core.Table.columnNames out)
                            [ "item"
                              Stdlib.ScenarioAColumn
                              Stdlib.ScenarioBColumn
                              Stdlib.DifferenceColumn ]
                            "the key and the three figures"

                        // north: 100 * 1 * 2 = 200 against 100 * 1.5 * 3 = 450.
                        // south:  40 * 1 * 5 = 200 against  40 * 1.5 * 6 = 360.
                        Expect.equal
                            (cells Stdlib.ScenarioAColumn)
                            [ Fuaran.Core.Float 200.0; Fuaran.Core.Float 200.0 ]
                            "scenario A"

                        Expect.equal
                            (cells Stdlib.ScenarioBColumn)
                            [ Fuaran.Core.Float 450.0; Fuaran.Core.Float 360.0 ]
                            "scenario B"

                        Expect.equal
                            (cells Stdlib.DifferenceColumn)
                            [ Fuaran.Core.Float 250.0; Fuaran.Core.Float 160.0 ]
                            "the difference, computed by the pipeline"
                }

                test "a panel composed with a comparison reading its parameters is accepted by pre-emit validation" {
                    expectClean
                        "panel + comparison"
                        (page
                            [ panelOver [ "growth"; "uplift" ]
                              comparisonOrFail (fixedSide "Plan" 0.0 0.0) liveSide ])
                }

                test "a panel naming a parameter the pipeline does not read is REFUSED by pre-emit validation" {
                    let refused =
                        page
                            [ panelOver [ "growth"; "uplift"; "discount" ]
                              comparisonOrFail (fixedSide "Plan" 0.0 0.0) liveSide ]

                    match PreEmitValidate.validate refused with
                    | Ok() -> failtest "a control no pipeline reads must not pass pre-emit validation"
                    | Error defects ->
                        let decorative =
                            defects
                            |> List.choose (fun d ->
                                match d with
                                | PreEmitValidate.PreEmitDefect.DecorativeFilter(_, name) -> Some name
                                | _ -> None)

                        Expect.equal decorative [ "discount" ] "FUARAN074 names exactly the unread parameter"

                    // And the reverse: a pipeline reading a parameter no control declares.
                    let dangling =
                        page [ panelOver [ "growth" ]; comparisonOrFail (fixedSide "Plan" 0.0 0.0) liveSide ]

                    Expect.contains (preEmitVariants dangling) "DanglingFilterReference" "FUARAN075 for 'uplift'"
                }

                test "changing one control re-evaluates only the sites that read that parameter — the binding walk" {
                    // The comparison reads `growth` and `uplift`; a second figure
                    // reads `uplift` alone. A change to `growth` dirties the
                    // comparison and nothing else.
                    let upliftFigure =
                        Fuaran.metric
                            "uplift-figure"
                            { Defaults.metric with
                                Label = TextSource.Literal "Uplift"
                                Value = Binding.Filter("uplift", Some 0.0) }

                    let comparison = comparisonOrFail (fixedSide "Plan" 0.0 0.0) liveSide
                    let tree = page [ panelOver [ "growth"; "uplift" ]; comparison; upliftFigure ]

                    expectClean "the readout page" tree

                    let facts = BindingWalk.collect tree

                    // The control declaring a key is its writer, not a site that
                    // re-evaluates when it moves.
                    let readersOf (name: string) =
                        facts.Uses
                        |> List.choose (fun u ->
                            match u.Use with
                            | BindingWalk.BindingUse.TransformParamFilter n
                            | BindingWalk.BindingUse.Filter n when n = name -> Some u.Reader
                            | _ -> None)
                        |> List.distinct
                        |> List.filter (fun r -> r <> "parameter-panel-controls")
                        |> List.sort

                    let gridId = (fst (gridIn comparison)).Id

                    Expect.equal (readersOf "growth") [ gridId ] "growth: the comparison only"
                    Expect.equal (readersOf "uplift") (List.sort [ gridId; "uplift-figure" ]) "uplift: both sites"
                }

                test "scenario-comparison: the stdlib entry reads the source, key and measure its holes name" {
                    let tree =
                        Stdlib.scenarioComparison<unit>.Materialize(argsOf Stdlib.scenarioComparison<unit>.Example)

                    let _, grid = gridIn tree

                    match grid.Source with
                    | Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Ref source), pipeline, Some ps) ->
                        Expect.equal source "budget-lines" "the rows the host resolves"
                        Expect.equal grid.RowKeyField (Some "line") "the key column"

                        // The growth model expanded: no definition is left, and
                        // each scenario's derivation reads the measure column.
                        let scenarios =
                            pipeline
                            |> List.choose (fun t ->
                                match t with
                                | Fuaran.Compute.Derive(name, expr) when
                                    name = Stdlib.ScenarioAColumn || name = Stdlib.ScenarioBColumn
                                    ->
                                    Some(sprintf "%A" expr)
                                | Fuaran.Compute.Define _ -> failtest "the growth model's expansion was refused"
                                | _ -> None)

                        Expect.equal (List.length scenarios) 2 "one derivation per scenario"

                        for expr in scenarios do
                            Expect.stringContains expr "Col \"cost\"" "the measure feeds the model's base"

                        Expect.equal
                            (ps |> List.map (fun p -> p.Name, sprintf "%A" p.From))
                            [ Stdlib.scenarioParamName "a" "growth",
                              sprintf "%A" (Binding<Fuaran.Core.JVal>.Static(Some(Fuaran.Core.JFloat 0.0)))
                              Stdlib.scenarioParamName "b" "growth",
                              sprintf "%A" (Binding<Fuaran.Core.JVal>.Static(Some(Fuaran.Core.JFloat 0.03))) ]
                            "each scenario's growth reaches its own parameter"
                    | other -> failtestf "the grid source is %A" other

                    Expect.equal
                        (grid.Columns |> List.map (fun c -> c.Label))
                        [ "Item"; "Flat"; "Inflation"; "Difference" ]
                        "the headings"
                }

                test "scenarioComparisonOf refuses a side that does not bind exactly the model's scenario parameters" {
                    let refusedWith (a: ScenarioSide) (b: ScenarioSide) =
                        match Stdlib.scenarioComparisonOf<unit> "c" "h" revenueModel a b with
                        | Ok _ -> failtest "expected a refusal"
                        | Error e -> e

                    let missing =
                        refusedWith
                            { Label = "Thin"
                              Parameters = [ "growth", Binding.Static None ] }
                            liveSide

                    Expect.stringContains missing "'uplift'" "an unbound parameter is named"

                    let extra =
                        refusedWith
                            (fixedSide "Plan" 0.0 0.0)
                            { liveSide with
                                Parameters = ("units", Binding.Static None) :: liveSide.Parameters }

                    Expect.stringContains extra "'units'" "a column parameter is not a scenario's to bind"

                    match
                        Stdlib.scenarioComparisonOf<unit>
                            "c"
                            "h"
                            { revenueModel with
                                Columns = ("volume", "units") :: revenueModel.Columns }
                            (fixedSide "Plan" 0.0 0.0)
                            liveSide
                    with
                    | Ok _ -> failtest "a column feeding no model parameter must be refused"
                    | Error e -> Expect.stringContains e "'volume'" "the stray column parameter is named"
                } ]

          testList
              "Phase 2753 — a refinable table whose view state is a pipeline"
              [ test "pre-emit validation accepts the declaration, the example and the trees the table yields" {
                    let f = Stdlib.refinableTable<unit>
                    expectClean "refinable-table declaration" f.Decl
                    expectClean "refinable-table example" f.Example
                    expectClean "refinable-table example, materialised" (f.Materialize(argsOf f.Example))
                    expectClean "a refinable table over inline rows" (tableOrFail ordersTable)
                }

                test "the stdlib entry reads the source, key, columns and page size its holes name" {
                    let f = Stdlib.refinableTable<unit>
                    let tree = f.Materialize(argsOf f.Example)
                    let _, grid = gridIn tree
                    let id = Stdlib.RefinableTableName
                    let regionKey = Stdlib.refinableFilterKey id "region"

                    Expect.equal grid.RowKeyField (Some "order") "the key column"
                    Expect.equal grid.PageSize (Some 25) "the page size"
                    Expect.equal grid.SortStateKey (Some(Stdlib.refinableSortKey id)) "the grid's own sort key"
                    Expect.equal grid.PageStateKey (Some(Stdlib.refinablePageKey id)) "the grid's own page key"

                    Expect.equal
                        (grid.Columns |> List.map (fun c -> c.Field, c.Label))
                        [ Some "customer", "Customer"; Some "region", "Region"; Some "total", "Total" ]
                        "the columns, in order"

                    Expect.equal
                        (grid.Columns |> List.map (fun c -> sprintf "%A" c.Kind))
                        ([ CellKindErased.Text; CellKindErased.Text; CellKindErased.Numeric ]
                         |> List.map (sprintf "%A"))
                        "a number column is numeric"

                    match grid.Source with
                    | Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Ref source), pipeline, Some ps) ->
                        Expect.equal source "orders" "the rows the host resolves"

                        Expect.equal
                            (sprintf "%A" pipeline)
                            (sprintf
                                "%A"
                                [ Fuaran.Compute.Filter(Fuaran.Compute.InParam(Fuaran.Compute.Col "region", regionKey)) ])
                            "one membership filter, over the one filterable column"

                        Expect.equal (ps |> List.map _.Name) [ regionKey ] "the filter's list parameter"
                        Expect.equal (panelControls tree |> List.map _.Name) [ regionKey ] "its chip writes that key"
                    | other -> failtestf "the grid reads %A" other
                }

                test "every binding keeps one column per field, so no filter key has two writers" {
                    let tree =
                        Stdlib.refinableTable<unit>
                            .Materialize(
                                Map.ofList
                                    [ "count", boxed 3
                                      "column-1-field", boxed "region"
                                      "column-1-filter", boxed "membership"
                                      "column-2-field", boxed "region"
                                      "column-2-filter", boxed "membership"
                                      "column-3-field", boxed " " ]
                            )

                    let _, grid = gridIn tree

                    Expect.equal
                        (grid.Columns |> List.map _.Field)
                        [ Some "region"; Some "column-3" ]
                        "a repeated field keeps its first column; a blank one falls back"

                    Expect.equal (panelControls tree |> List.length) 1 "one chip"
                }

                test "a fixed column opts out of the header sort" {
                    let tree =
                        tableOrFail
                            { ordersTable with
                                Columns =
                                    ordersTable.Columns
                                    |> List.map (fun c -> if c.Field = "total" then { c with Sortable = false } else c) }

                    let _, grid = gridIn tree

                    Expect.equal
                        (grid.Columns |> List.map _.Sortable)
                        [ None; None; Some false ]
                        "only the fixed column"
                }

                test "the substituted pipeline evaluates to exactly the rows the grid shows, for every view" {
                    let tree = tableOrFail ordersTable
                    expectClean "the table" tree

                    let sorts =
                        None
                        :: [ for column in 0..2 do
                                 for direction in [ "asc"; "desc" ] -> Some(column, direction) ]

                    let selections = [ None; Some []; Some [ "north" ]; Some [ "south"; "east" ] ]
                    let pages = [ None; Some 1; Some 2; Some 3; Some 9 ]
                    let mutable compared = 0

                    for sort in sorts do
                        for selected in selections do
                            for page in pages do
                                let shown, view =
                                    shownAndView tree (sourcesFor sort page selected) (defaultArg selected [])

                                let pipeline = Stdlib.refinedPipeline "orders" ordersTable view

                                // The canonical form reads no parameter: every value
                                // the view holds is written into it, and it is
                                // evaluated below with nothing bound.
                                Expect.isEmpty
                                    (Fuaran.Compute.Transform.paramsOf pipeline)
                                    (sprintf "the substituted pipeline still reads a parameter: %A" pipeline)

                                Expect.equal
                                    (evaluated pipeline |> List.map visible)
                                    (shown |> List.map visible)
                                    (sprintf "sort %A, page %A, selection %A: %A" sort page selected pipeline)

                                compared <- compared + 1

                    Expect.equal compared (7 * 4 * 5) "every view was compared"
                }

                test "the comparison measures the sort and the case folding — go-red" {
                    // Dropping the sort from the projection, or sorting text
                    // ordinally (the evaluator's own text order), disagrees with
                    // the grid: the equality above is not vacuous, and the
                    // lower-cased sort key is load-bearing.
                    let tree = tableOrFail ordersTable
                    let shown, view = shownAndView tree (sourcesFor (Some(0, "desc")) (Some 1) None) []
                    let shownCells = shown |> List.map visible

                    Expect.equal
                        (evaluated (Stdlib.refinedPipeline "orders" ordersTable view) |> List.map visible)
                        shownCells
                        "the projection agrees"

                    Expect.notEqual
                        (evaluated (Stdlib.refinedPipeline "orders" ordersTable { view with Sort = None })
                         |> List.map visible)
                        shownCells
                        "without the sort it disagrees"

                    let ordinal =
                        [ Fuaran.Compute.Sort [ Fuaran.Compute.Slot.Lit "customer", Fuaran.Compute.Desc ]
                          Fuaran.Compute.Limit(Fuaran.Compute.Slot.Lit 3, Fuaran.Compute.Slot.Lit 0) ]

                    Expect.notEqual (evaluated ordinal |> List.map visible) shownCells "an ordinal sort disagrees"
                }

                test "refused: a sortable column the pipeline's static schema does not produce" {
                    let margin =
                        { Field = "margin"
                          Label = "Margin"
                          Kind = RefinableKind.Number
                          Sortable = true
                          Filter = None }

                    let plus (c: RefinableColumn) (table: RefinableTable) =
                        { table with
                            Columns = table.Columns @ [ c ] }

                    let refused (what: string) (expected: string) (result: Result<Node<unit>, string>) =
                        match result with
                        | Ok _ -> failtestf "%s must be refused" what
                        | Error e -> Expect.stringContains e expected (sprintf "%s: the refusal names it" what)

                    ordersOf Fuaran.Compute.SchemaWalk.noSources (plus margin ordersTable)
                    |> refused "a sortable column the schema does not produce" "sortable column 'margin'"

                    ordersOf
                        Fuaran.Compute.SchemaWalk.noSources
                        (plus
                            { margin with
                                Sortable = false
                                Filter = Some [] }
                            ordersTable)
                    |> refused "a filterable column the schema does not produce" "filterable column 'margin'"

                    match
                        ordersOf Fuaran.Compute.SchemaWalk.noSources (plus { margin with Sortable = false } ordersTable)
                    with
                    | Ok _ -> ()
                    | Error e ->
                        failtestf "a fixed, unfiltered column is the grid's grounding check, not this one: %s" e

                    ordersOf Fuaran.Compute.SchemaWalk.noSources (plus { margin with Field = "total" } ordersTable)
                    |> refused "two columns over one field" "one field"

                    ordersOf
                        Fuaran.Compute.SchemaWalk.noSources
                        { ordersTable with
                            Columns =
                                ordersTable.Columns
                                |> List.map (fun c -> if c.Field = "total" then { c with Filter = Some [] } else c) }
                    |> refused "a membership filter over a number column" "'total' is not text"

                    ordersOf Fuaran.Compute.SchemaWalk.noSources { ordersTable with Key = "id" }
                    |> refused "a row key the schema does not produce" "row key 'id'"

                    ordersOf Fuaran.Compute.SchemaWalk.noSources { ordersTable with PageSize = 0 }
                    |> refused "a page size of zero" "page size"

                    // A Ref with no declared schema refutes nothing; given its
                    // schema, it refutes exactly as the inline rows do.
                    let overRef =
                        { plus margin ordersTable with
                            Source = Fuaran.Core.DataSource.Ref "orders" }

                    match ordersOf Fuaran.Compute.SchemaWalk.noSources overRef with
                    | Ok _ -> ()
                    | Error e -> failtestf "an undeclared Ref refutes nothing: %s" e

                    let declared =
                        match orderRows with
                        | Fuaran.Core.Embedded t -> Fuaran.Compute.SchemaWalk.ofMap (Map.ofList [ "orders", t.Schema ])
                        | other -> failtestf "%A" other

                    ordersOf declared overRef
                    |> refused "a column a declared Ref schema does not produce" "sortable column 'margin'"
                } ]

          test "the certification harness can still go red — a deliberately broken materialiser is REJECTED" {
              // The go-red self-test. Every assertion above is a green one, and a
              // suite of green assertions cannot distinguish "the fragments are
              // correct" from "the harness certifies anything". Feed it a
              // materialiser that emits two nodes with the same id — the
              // duplicate-id rule is an Error-severity defect at every binding —
              // and require the rejection.
              let f = List.head library

              let broken: Map<string, obj> -> Node<unit> =
                  fun _ ->
                      Fuaran.stack
                          "collide"
                          { Defaults.stack with
                              // fuaran-validator: disable-next-line FUARAN-DUP-ID — negative fixture: the duplicate id is the defect under test
                              Children = [ Fuaran.markdown "same-id" "first"; Fuaran.markdown "same-id" "second" ] }

              let verdict = certifyFragment f.Name f.Decl broken MaxCases Seed

              Expect.isFalse
                  verdict.Certified
                  "a materialiser emitting duplicate node ids must be REJECTED; if it certifies, the harness is not measuring what this suite claims"

              Expect.isSome verdict.Counterexample "a rejected fragment must surface a (binding, defect) counterexample"
          } ]
