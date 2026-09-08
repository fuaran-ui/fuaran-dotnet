module Fuaran.UI.Tests.TransformSiteWalk

// ============================================================================
//  Phase 1615 — the walk carries the live-Transform SITE, and the schema rules
//  read it instead of re-deriving it.
//
//  Phase 1179's outcome recorded `BindingWalk.fs` as deliberately untouched:
//  the walk enumerated a live-transform site as
//  `BindingUse.TransformStateSource(key, hasDefault)`, which carries no
//  pipeline, so "a refresh site cannot be identified statically". Three
//  consumers re-derived the enumeration by hand. This suite is the evidence
//  that the one enumeration now in the walk says what those hand derivations
//  said.
//
//  The corpus test is the load-bearing one, and it is an INDEPENDENT ORACLE
//  rather than a restatement: `handDerivedSourceSites` below is the pre-1615
//  shape of the two schema rules' own pattern — walk the tree, match the
//  reader's `source` slot for a `Binding.Transform(TransformSource.Data …)`,
//  take the pair. If the walk's enumeration and that pattern ever disagree,
//  FUARAN086 / FUARAN114 have changed window, which is a verdict change and
//  not a refactor.
//
//  Go-red proofs, RUN at authoring rather than reasoned about:
//   - disabling `tagSourceSite` empties the walk side of the corpus equality
//     and reddens it (2 of 8 red), which is also what shows the equality is
//     not vacuous — the corpus really does carry the shape it compares;
//   - deriving `IsLive` from `SiteKey.IsSome` reddens the parameterised-live
//     test (1 of 8 red), which is exactly the misreading that field exists to
//     prevent.
//  The head-only rule in `tagSourceSite` is pinned by the nested-Transform
//  test below rather than by a perturbation.
// ============================================================================

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode

type private Msg = | NoOp

// ── the corpus ──────────────────────────────────────────────────────────────

/// The corpus root — the `wire-format-fixtures/` clone, wherever it sits above
/// the test binary. The same climb every other corpus-driven suite makes.
let private corpusRoot () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures")

            if Directory.Exists(Path.Combine(candidate, "nodes")) then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

/// Every `nodes/` fixture, decoded. A fixture that does not decode is a defect
/// in a different suite (`GeneratedLayerTests` owns the corpus round trip), so
/// it is skipped here rather than double-reported.
let private decodedFixtures () : (string * Node<obj>) list =
    match corpusRoot () with
    | None -> []
    | Some root ->
        let dir = Path.Combine(root, "nodes")

        if not (Directory.Exists dir) then
            []
        else
            let stem (path: string) =
                Path.GetFileNameWithoutExtension path
                |> Option.ofObj
                |> Option.defaultValue path

            Directory.GetFiles(dir, "*.json")
            |> Array.toList
            |> List.sortBy stem
            |> List.choose (fun path ->
                match decodeNodeObj ((File.ReadAllText path).Trim()) with
                | Ok node -> Some(stem path, node)
                | Error _ -> None)

// ── the independent oracle ──────────────────────────────────────────────────

/// The pre-1615 derivation, restated: for every node whose OWN `source` slot
/// is a `Binding.Transform` over a `TransformSource.Data`, the reader id and
/// the `(source, pipeline)` pair the schema rules fed to `SchemaWalk`.
///
/// It matches the slot binding DIRECTLY, exactly as the two rules did — so a
/// Transform reached through a `Format` or a `Local` is not here, and must not
/// be on the walk side either.
let private handDerivedSourceSites (root: Node<'Msg>) : (string * DataSource * Transform list) list =
    let found = ResizeArray<string * DataSource * Transform list>()

    let pair (id: string) (binding: Binding<'T>) =
        match binding with
        | Binding.Transform(TransformSource.Data source, pipeline, _) -> found.Add(id, source, pipeline)
        | _ -> ()

    let rec walk (n: Node<'Msg>) =
        match n.Kind with
        | NodeKind.DataGrid g -> pair n.Id g.Source
        | NodeKind.Chart c -> pair n.Id c.Source
        | NodeKind.Map m -> pair n.Id m.Source
        | _ -> ()

        match Fuaran.UI.Ops.Introspect.getChildren n.Kind with
        | Some kids -> kids |> List.iter walk
        | None -> ()

    walk root
    List.ofSeq found

/// The same pairs, off the walk's own enumeration.
let private walkSourceSites (root: Node<'Msg>) : (string * DataSource * Transform list) list =
    (BindingWalk.collect root).TransformSites
    |> List.filter (fun (d: BindingWalk.TransformSiteDecl) -> d.Site.Slot = Some "source" && not d.Site.IsLive)
    |> List.map (fun d -> d.Reader, d.Site.Source, d.Site.Pipeline)

// ── hand-built trees ────────────────────────────────────────────────────────

let private pipeline: Transform list = [ Transform.Project [ "n", "n" ] ]

/// A grid whose `source` is a LIVE Transform over `$state.<key>`, declaring
/// `rowKeyField` — the shape the whole phase is about.
let private liveGrid
    (id: string)
    (key: string)
    (rowKeyField: string option)
    (parameters: TransformParam list option)
    : Node<Msg> =
    let source: Binding<Row seq> =
        Binding.Transform(
            TransformSource.Live(Binding.State(key, None), Embedded { Schema = []; Columns = [] }),
            pipeline,
            parameters
        )

    { Id = id
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = source
              RowKey = None
              RowKeyField = rowKeyField
              Columns =
                [ { Label = "N"
                    Value = None
                    Field = Some "n"
                    Sortable = None
                    Editable = None
                    Format = CellFormat.None
                    Kind = CellKindErased.Text
                    Width = ColumnWidth.Auto } ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false
              Exportable = false }
        )
      State = None
      Style = None
      Accessibility = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None
      Motion = None }

let private sitesOf (node: Node<Msg>) =
    (BindingWalk.collect node).TransformSites

[<Tests>]
let transformSiteWalkTests =
    testList
        "Phase 1615 — the Transform-site enumeration"
        [
          // ── task 3's proof ──
          test "the walk's source-slot enumeration equals the hand derivation on every corpus fixture" {
              let fixtures = decodedFixtures ()

              Expect.isNonEmpty
                  fixtures
                  "wire-format-fixtures/nodes/ produced no decodable fixture — the corpus clone is missing, and a vacuous pass here would assert nothing"

              let transformBearing =
                  fixtures
                  |> List.filter (fun (_, node) -> not (List.isEmpty (handDerivedSourceSites node)))

              Expect.isNonEmpty
                  transformBearing
                  "no corpus fixture carries a Data-sourced Transform in a row-feed slot — the equality below would hold vacuously"

              for (name, node) in fixtures do
                  Expect.equal
                      (walkSourceSites node |> List.sortBy (fun (id, _, _) -> id))
                      (handDerivedSourceSites node |> List.sortBy (fun (id, _, _) -> id))
                      (sprintf
                          "fixture '%s': the walk's site enumeration and the pre-1615 hand derivation disagree — FUARAN086/FUARAN114 have changed window"
                          name)
          }

          test "the corpus's live-Transform fixtures are enumerated as live sites" {
              let liveSites =
                  decodedFixtures ()
                  |> List.collect (fun (name, node) ->
                      (BindingWalk.collect node).TransformSites
                      |> List.filter (fun d -> d.Site.IsLive)
                      |> List.map (fun d -> name, d))

              Expect.isNonEmpty
                  liveSites
                  "the corpus carries no live-Transform fixture — badge-transform-live and its siblings should be here"
          }

          // ── task 2 ──
          test "a live grid's site is reported under the state key it reads, with its pipeline" {
              let facts = BindingWalk.collect (liveGrid "g" "rows" (Some "id") None)

              match Map.tryFind "rows" facts.StateKeys.LiveTransformSites with
              | None -> failtest "the live-Transform site is not reported under the state key its source reads"
              | Some [ d ] ->
                  Expect.equal d.Reader "g" "the site is tagged with the reading node"
                  Expect.equal d.Site.Pipeline pipeline "the site carries the pipeline the reader will recompute"
                  Expect.isTrue d.Site.IsLive "a live source is reported as live"
              | Some many -> failtestf "expected exactly one site under 'rows', got %d" (List.length many)
          }

          // ── task 1 ──
          test "the site carries the reader's declared row-identity column" {
              match sitesOf (liveGrid "g" "rows" (Some "id") None) with
              | [ d ] ->
                  Expect.equal d.Site.IdentityColumn (Some "id") "a grid's rowKeyField IS the row-identity column"
                  Expect.equal d.Site.Slot (Some "source") "the reading node names the slot the site sits in"
              | other -> failtestf "expected exactly one site, got %d" (List.length other)
          }

          test "a grid declaring no rowKeyField reports no identity column rather than a guess" {
              match sitesOf (liveGrid "g" "rows" None None) with
              | [ d ] -> Expect.equal d.Site.IdentityColumn None "an undeclared row identity is absent, never invented"
              | other -> failtestf "expected exactly one site, got %d" (List.length other)
          }

          test "the site key is the renderer's own, so the walk and the live path name one site" {
              match sitesOf (liveGrid "g" "rows" (Some "id") None) with
              | [ d ] ->
                  let expected =
                      BindingWalk.liveSiteKey (Binding.State("rows", None): Binding<JVal>) pipeline

                  Expect.equal
                      d.Site.SiteKey
                      (Some expected)
                      "the walk must derive the SAME site key the renderer's live path hands the store"
              | other -> failtestf "expected exactly one site, got %d" (List.length other)
          }

          test "a parameterised live source declines its site key rather than naming a site the store never sees" {
              let parameters: TransformParam list option =
                  Some
                      [ { From = (Binding.Static(Some(JFloat 1.0)): Binding<JVal>)
                          Name = "threshold" } ]

              match sitesOf (liveGrid "g" "rows" (Some "id") parameters) with
              | [ d ] ->
                  Expect.equal
                      d.Site.SiteKey
                      None
                      "the effective pipeline of a parameterised source is a render-time fact — the key is declined, not guessed"

                  Expect.isTrue
                      d.Site.IsLive
                      "IsLive must NOT be inferred from SiteKey.IsSome — a parameterised live source declines its key and is still live"
              | other -> failtestf "expected exactly one site, got %d" (List.length other)
          }

          test "the existing TransformStateSource case is untouched beside the new one" {
              let facts = BindingWalk.collect (liveGrid "g" "rows" (Some "id") None)

              Expect.isTrue
                  (facts.StateKeys.Reads.Contains "rows")
                  "a live source is still a READ of its key — nothing about the 865 projection moves"

              Expect.equal
                  facts.StateKeys.TransformInertSources
                  [ "g", "rows" ]
                  "a default-less live source is still FUARAN105's subject — nothing about that verdict moves"
          } ]
