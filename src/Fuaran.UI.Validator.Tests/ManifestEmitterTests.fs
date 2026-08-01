module Fuaran.UI.Validator.Tests.ManifestEmitterTests

// ============================================================================
//  Manifest-emitter tests (Phase 377).
//
//  Each test materialises a tiny .fsproj + .fs source under a fresh temp
//  directory and runs the emitter over it. The fixture sources are PARSED,
//  never compiled — the emitter walks the untyped AST — so they declare only
//  what the derivation rules need to see.
//
//  The byte-identity expectation below is written out by hand rather than
//  produced by calling the renderer twice: a test that compares the emitter
//  against itself would pass no matter what the canonical form drifted to.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Validator
open Fuaran.UI.Validator.Findings

let private freshDir (name: string) =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-emitter-%s-%s" name (Guid.NewGuid().ToString("N")))

    Directory.CreateDirectory path |> ignore
    path

let private writeFile (dir: string) (name: string) (contents: string) =
    let path = Path.Combine(dir, name)
    File.WriteAllText(path, contents)
    path

let private writeFsproj (dir: string) (name: string) =
    writeFile
        dir
        name
        """<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"""

let private emit (projectPath: string) (overridesPath: string option) =
    ManifestEmitter.run
        { ProjectPath = projectPath
          OutPath = None
          OverridesPath = overridesPath }
    |> Async.RunSynchronously

// ─── Fixture app ────────────────────────────────────────────────────────────
//
// Declares the three facets at their DECLARATION sites: a `Msg` DU, a
// `QueryResults` registration reached through a `let`-bound map (the idiomatic
// shape — see samples/demo/Main.fs), and a grid whose row lambda is annotated.

let private appSource =
    """module Sample.App

open Fuaran.UI
open Fuaran.UI.Types

type Msg =
    | LoadData
    | SelectRow of int

type SaleRow = { Region: string; Amount: float }

let buildSources (model: Model) =
    let queryMap =
        Map.ofList
            [ "totalRevenue", box model.Revenue
              "salesRows", box model.Rows ]

    { BindingResolver.empty with
        QueryResults = queryMap
        State = Map.empty }

let build () : Node<Msg> =
    Fuaran.dashboard "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.grid "sales"
                    { Defaults.grid with
                        Source = binding.query "salesRows" (fun r -> r)
                        RowKey = fun (row: SaleRow) -> row.Region }
                  Fuaran.button "reload"
                    { Defaults.button<Msg> with
                        OnClick = Action.dispatch LoadData } ] }
"""

/// The same app with the `salesRows` query removed from the registration —
/// the "a developer deleted a query and forgot to regenerate" case. Filtered
/// by line rather than by a substring including a newline: the literal above
/// carries whatever line ending this file was checked out with, so a
/// `Replace` keyed on `\n` silently matches nothing on a CRLF working tree.
let private appSourceWithoutSalesRows =
    appSource.Split('\n')
    |> Array.filter (fun line -> not (line.Contains "\"salesRows\", box"))
    |> String.concat "\n"

/// The canonical on-disk form the emitter must produce for `appSource`:
/// 2-space indent, LF, one trailing newline, facets in a fixed order, names
/// ordinal-sorted. Built from lines so a CRLF checkout cannot silently change
/// what the test asserts.
let private canonicalManifest =
    String.Join(
        "\n",
        [ "{"
          "  \"$generated\": {"
          "    \"by\": \"Fuaran.UI.Validator manifest emitter\","
          "    \"doNotEdit\": \"Derived from source. Regenerate with: Fuaran.UI.Validator emit-manifest <project.fsproj>. Hand-assert entries in fuaran-validator.manifest.overrides.json instead.\""
          "  },"
          "  \"queries\": ["
          "    \"salesRows\","
          "    \"totalRevenue\""
          "  ],"
          "  \"msgCases\": ["
          "    \"LoadData\","
          "    \"SelectRow\""
          "  ],"
          "  \"queryRowTypes\": {"
          "    \"salesRows\": \"SaleRow\""
          "  }"
          "}"
          "" ]
    )

[<Tests>]
let tests =
    testList
        "Manifest emitter"
        [
          // ─── Acceptance: byte-identical regeneration ───────────────────
          test "Regenerates a manifest byte-identical to the correct hand-written one" {
              let dir = freshDir "byte-identical"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSource |> ignore

              // The "correct hand-written manifest" — committed in canonical
              // form, exactly what a migrated project holds.
              writeFile dir Manifest.manifestFileName canonicalManifest |> ignore

              let outcome = emit projectPath None

              Expect.equal outcome.Json canonicalManifest "generated form matches the committed manifest byte for byte"

              Expect.isEmpty outcome.Drift "a correct manifest reports no drift"
              Expect.isFalse outcome.FormattingDrift "and no formatting drift"
          }

          test "Derives each facet from its declaration site" {
              let dir = freshDir "facets"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSource |> ignore

              let outcome = emit projectPath None

              Expect.equal
                  outcome.Merged.Queries
                  (Set.ofList [ "totalRevenue"; "salesRows" ])
                  "queries come from the QueryResults registration, reached through the let-bound map"

              Expect.equal
                  outcome.Merged.MsgCases
                  (Set.ofList [ "LoadData"; "SelectRow" ])
                  "msgCases come from the Msg DU declaration"

              Expect.equal
                  outcome.Merged.QueryRowTypes
                  (Map.ofList [ "salesRows", "SaleRow" ])
                  "row type from the grid's annotated lambda"

              let selectRow =
                  outcome.Derivation.MsgCases |> List.find (fun c -> c.Case = "SelectRow")

              Expect.equal
                  selectRow.PayloadTypes
                  [ "int" ]
                  "payload shapes are derived even though v1 does not emit them"
          }

          // ─── Acceptance: the drift gate names what changed ─────────────
          test "Deleting a query from source is reported as drift citing the name" {
              let dir = freshDir "drift-delete"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSourceWithoutSalesRows |> ignore
              writeFile dir Manifest.manifestFileName canonicalManifest |> ignore

              let outcome = emit projectPath None

              Expect.contains
                  outcome.Drift
                  (ManifestEmitter.StaleInCommitted("queries", "salesRows"))
                  "the deleted query is named as stale in the committed manifest"

              let described = outcome.Drift |> List.map ManifestEmitter.describeDrift

              Expect.isTrue
                  (described |> List.exists (fun d -> d.Contains "salesRows"))
                  "the rendered message cites the missing name"
          }

          test "Adding a query without regenerating is reported as drift citing the name" {
              let dir = freshDir "drift-add"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSource |> ignore

              // A committed manifest that predates the `salesRows` registration.
              let stale =
                  canonicalManifest.Replace("    \"salesRows\",\n", "").Replace("    \"salesRows\": \"SaleRow\"\n", "")

              writeFile dir Manifest.manifestFileName stale |> ignore

              let outcome = emit projectPath None

              Expect.contains
                  outcome.Drift
                  (ManifestEmitter.MissingFromCommitted("queries", "salesRows"))
                  "the new query is named as missing from the committed manifest"
          }

          test "A semantically-current manifest in non-canonical form is formatting drift, never a gate failure" {
              let dir = freshDir "formatting"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSource |> ignore

              // A pre-migration hand-written manifest: same contract, no
              // header, different key order and indentation.
              writeFile
                  dir
                  Manifest.manifestFileName
                  """{
    "msgCases": ["SelectRow", "LoadData"],
    "queries": ["totalRevenue", "salesRows"],
    "queryRowTypes": { "salesRows": "SaleRow" }
}
"""
              |> ignore

              let outcome = emit projectPath None

              Expect.isEmpty outcome.Drift "no semantic drift — the contract still describes the app"
              Expect.isTrue outcome.FormattingDrift "but it is not in canonical form"
          }

          // ─── Acceptance: the override tier ─────────────────────────────
          test "An override entry survives regeneration and is marked asserted" {
              let dir = freshDir "overrides"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSource |> ignore

              writeFile
                  dir
                  Manifest.overridesFileName
                  """{
  "queries": ["dynamicallyRegistered"],
  "queryRowTypes": { "dynamicallyRegistered": "AuditRow" },
  "customNodeRatio": 0.12
}
"""
              |> ignore

              let outcome = emit projectPath None

              Expect.isTrue
                  (outcome.Merged.Queries.Contains "dynamicallyRegistered")
                  "the asserted query survives into the merged manifest"

              Expect.equal outcome.Merged.CustomNodeRatio (Some 0.12) "the asserted policy knob survives"

              Expect.equal
                  outcome.Provenance.AssertedQueries
                  (Set.singleton "dynamicallyRegistered")
                  "and is attributed to the override tier, not the derivation"

              Expect.isFalse
                  (outcome.Derivation.Queries.Contains "dynamicallyRegistered")
                  "the derivation itself never saw it"

              Expect.stringContains
                  outcome.Json
                  "\"asserted\""
                  "the emitted file carries the asserted block so a reviewer can tell asserted from derived"

              Expect.stringContains outcome.Json "\"dynamicallyRegistered\"" "naming the asserted entry"
          }

          test "An override the derivation now produces is reported as redundant, not asserted" {
              let dir = freshDir "redundant-override"
              let projectPath = writeFsproj dir "App.fsproj"
              writeFile dir "App.fs" appSource |> ignore

              writeFile dir Manifest.overridesFileName """{ "queries": ["salesRows"] }"""
              |> ignore

              let outcome = emit projectPath None

              Expect.isEmpty
                  outcome.Provenance.AssertedQueries
                  "an entry the source demonstrates is derived, not asserted"

              Expect.contains
                  outcome.Provenance.RedundantOverrides
                  "queries: \"salesRows\""
                  "and the now-redundant override is surfaced for removal"
          }

          test "An override never removes a name the source registers" {
              // Union, not replacement — see Manifest.mergeOverrides. An
              // override that could subtract would silently re-open the hole
              // FUARAN010 exists to close.
              let derived =
                  { Manifest.empty with
                      Queries = Set.ofList [ "a"; "b" ]
                      MsgCases = Set.singleton "LoadData" }

              let overrides =
                  { Manifest.empty with
                      Queries = Set.singleton "c" }

              let merged = Manifest.mergeOverrides derived overrides

              Expect.equal merged.Queries (Set.ofList [ "a"; "b"; "c" ]) "union"
              Expect.equal merged.MsgCases (Set.singleton "LoadData") "untouched facets pass through"
          }

          // ─── Ambiguity surfaces at generation time, never guessed ──────
          test "Conflicting row-type annotations yield a conflict and no entry" {
              let dir = freshDir "row-conflict"
              let projectPath = writeFsproj dir "App.fsproj"

              writeFile
                  dir
                  "App.fs"
                  """module Sample.Conflict

open Fuaran.UI

let buildSources () =
    { BindingResolver.empty with
        QueryResults = Map.ofList [ "rows", box [] ] }

let a () =
    Fuaran.grid "one"
        { Defaults.grid with
            Source = binding.query "rows" (fun r -> r)
            RowKey = fun (row: SaleRow) -> row.Region }

let b () =
    Fuaran.grid "two"
        { Defaults.grid with
            Source = binding.query "rows" (fun r -> r)
            RowKey = fun (row: AuditRow) -> row.Id }
"""
              |> ignore

              let outcome = emit projectPath None

              Expect.isFalse
                  (Map.containsKey "rows" outcome.Merged.QueryRowTypes)
                  "an ambiguous query gets no entry — the emitter does not pick a winner"

              Expect.equal
                  (Map.tryFind "rows" outcome.Derivation.RowTypeConflicts)
                  (Some [ "AuditRow"; "SaleRow" ])
                  "the conflict is reported with both annotations so the author can resolve it"
          }

          // ─── Acceptance: the checks stay live over a generated manifest ─
          test "FUARAN010 / FUARAN020 still fire over a generated manifest" {
              // The whole point of deriving from declaration sites. Had
              // `queries` come from the tree's own `binding.query` references
              // and `msgCases` from its `Action.dispatch` references, both of
              // these hallucinations would resolve by construction and this
              // test would report nothing.
              let dir = freshDir "checks-live"
              let projectPath = writeFsproj dir "App.fsproj"

              writeFile
                  dir
                  "App.fs"
                  """module Sample.Typos

open Fuaran.UI

type Msg =
    | LoadData
    | SelectRow of int

let buildSources () =
    { BindingResolver.empty with
        QueryResults = Map.ofList [ "totalRevenue", box 1.0 ] }

let build () =
    Fuaran.dashboard "root"
        { Defaults.dashboard with
            Children =
                [ Fuaran.metric "m"
                    { Defaults.metric with
                        Source = binding.query "totalRevneu" (fun r -> r) }
                  Fuaran.button "b"
                    { Defaults.button with
                        OnClick = Action.dispatch LoadDate } ] }
"""
              |> ignore

              let outcome = emit projectPath None
              ManifestEmitter.write outcome

              let result =
                  Validator.run
                      { ProjectPath = projectPath
                        ModulePattern = None
                        ManifestPath = Some outcome.OutPath
                        Orchestrated = false }
                  |> Async.RunSynchronously

              let codes = result.Findings |> List.map _.Code

              Expect.contains codes "FUARAN010" "a hallucinated query name is still an error over a generated manifest"

              Expect.contains codes "FUARAN020" "a hallucinated Msg case is still an error over a generated manifest"

              let errors = result.Findings |> List.filter isError

              Expect.isTrue
                  (errors |> List.exists (fun f -> f.AvailableFields = Some [ "totalRevenue" ]))
                  "and the §4d recovery hint still carries the registered names"
          } ]
