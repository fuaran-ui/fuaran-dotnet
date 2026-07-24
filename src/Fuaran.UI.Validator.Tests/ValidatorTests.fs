module Fuaran.UI.Validator.Tests.ValidatorTests

// ============================================================================
//  End-to-end validator tests.
//
//  Each test materialises a tiny .fsproj + .fs source under a fresh temp
//  directory, optionally writes a manifest sibling, runs the validator, and
//  asserts the expected Finding set. The .fsproj exists for path discipline
//  only — the validator reads sources from the project directory (per
//  AstWalker.discoverSourceFiles); no MSBuild evaluation needed.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Validator
open Fuaran.UI.Validator.Findings

let private freshDir (name: string) =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-validator-%s-%s" name (Guid.NewGuid().ToString("N")))

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

let private runValidator (projectPath: string) (manifestPath: string option) =
    Validator.run
        { ProjectPath = projectPath
          ModulePattern = None
          ManifestPath = manifestPath
          Orchestrated = false }
    |> Async.RunSynchronously

let private runValidatorOrch (orchestrated: bool) (projectPath: string) =
    Validator.run
        { ProjectPath = projectPath
          ModulePattern = None
          ManifestPath = None
          Orchestrated = orchestrated }
    |> Async.RunSynchronously

/// A tree whose Metric source is a `Binding.Computed` closure — the host-only
/// escape FUARAN084 (wire-survivability) flags.
let private computedBindingSource =
    """module Sample
open Fuaran.UI
open Fuaran.UI.Types

let tree =
    Fuaran.metric "m1"
        { Defaults.metric with
            Source = Binding.Computed(fun _ -> 42.0) }
"""

let private codesOnly (findings: Finding list) =
    findings |> List.map _.Code |> List.sort

let private severityCount (severity: Severity) (findings: Finding list) =
    findings |> List.filter (fun f -> f.Severity = severity) |> List.length

let private hasCode (code: string) (findings: Finding list) =
    findings |> List.exists (fun f -> f.Code = code)

// ─── Test fixtures ─────────────────────────────────────────────────────────

let private validTreeSource =
    """module Sample.Valid

open Fuaran.UI
open Fuaran.UI.Types

type Msg =
    | LoadData
    | SelectRow of int

let build () : Node<Msg> =
    Fuaran.dashboard "valid-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.metric "metric-revenue"
                    { Defaults.metric with
                        Label = TextSource.Literal "Revenue"
                        Source = binding.query "totalRevenue" (fun (r: {| amount: float |}) -> r.amount) }
                  Fuaran.button "btn-reload"
                    { Defaults.button<Msg> with
                        Label = TextSource.Literal "Reload"
                        OnClick = Action.dispatch LoadData } ] }
"""

let private duplicateNodeIdSource =
    """module Sample.Dup

open Fuaran.UI

let build () =
    Fuaran.dashboard "dup-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.metric "shared-id" Defaults.metric
                  Fuaran.metric "shared-id" Defaults.metric ] }
"""

let private crossTreeDuplicateSource =
    """module Sample.CrossTree

open Fuaran.UI

let build1 () = Fuaran.dashboard "tree-a" { Defaults.dashboard with Children = [ Fuaran.metric "shared" Defaults.metric ] }
let build2 () = Fuaran.dashboard "tree-b" { Defaults.dashboard with Children = [ Fuaran.metric "shared" Defaults.metric ] }
"""

let private unresolvedQuerySource =
    """module Sample.UnresolvedQuery

open Fuaran.UI
open Fuaran.UI.Types

type Msg = LoadData

let build () : Node<Msg> =
    Fuaran.dashboard "uq-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.metric "metric"
                    { Defaults.metric with
                        Source = binding.query "totalRevneu" (fun (r: {| amount: float |}) -> r.amount) } ] }
"""

let private blankHrefLinkSource =
    """module Sample.BlankHrefLink

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let build () : Node<Msg> =
    Fuaran.dashboard "bhl-dashboard"
        { Defaults.dashboard<Msg> with
            Children = [ Fuaran.link "lnk" "" "About" ] }
"""

let private validLinkSource =
    """module Sample.ValidLink

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let build () : Node<Msg> =
    Fuaran.dashboard "vl-dashboard"
        { Defaults.dashboard<Msg> with
            Children = [ Fuaran.link "lnk" "/about" "About" ] }
"""

let private mistypedMsgCaseSource =
    """module Sample.MistypedMsg

open Fuaran.UI
open Fuaran.UI.Types

type Msg =
    | LoadData
    | Reset

let build () : Node<Msg> =
    Fuaran.dashboard "mm-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button "btn"
                    { Defaults.button<Msg> with
                        Label = TextSource.Literal "Go"
                        OnClick = Action.dispatch LoadDate } ] }
"""

let private rowTypeMismatchSource =
    """module Sample.RowTypeMismatch

open Fuaran.UI
open Fuaran.UI.Types

type SaleRow = { Id: int; Amount: float }
type WrongRow = { Other: string }
type Msg = SelectRow of int

let build () : Node<Msg> =
    Fuaran.dashboard "rt-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.grid "grid"
                    { Defaults.grid<SaleRow, Msg> with
                        Source = binding.query "salesRows" id
                        RowKey = (fun (r: WrongRow) -> r.Other)
                        OnRowClick = Some (fun (r: WrongRow) -> Action.dispatch (SelectRow 0)) } ] }
"""

let private rowTypeMissingManifestEntrySource =
    """module Sample.RowTypeMissing

open Fuaran.UI
open Fuaran.UI.Types

type SaleRow = { Id: int; Amount: float }
type Msg = SelectRow of int

let build () : Node<Msg> =
    Fuaran.dashboard "rtm-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.grid "grid"
                    { Defaults.grid<SaleRow, Msg> with
                        Source = binding.query "unknownRows" id
                        RowKey = (fun (r: SaleRow) -> string r.Id) } ] }
"""

let private outOfRangeProgressSource =
    """module Sample.OutOfRangeProgress

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.dashboard "orp-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.progress "load-bar"
                    { Defaults.progress with Fraction = Binding.Static 75.0 } ] }
"""

let private inRangeProgressSource =
    """module Sample.InRangeProgress

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.dashboard "irp-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.progress "load-bar"
                    { Defaults.progress with Fraction = Binding.Static 0.75 } ] }
"""

let private validManifest =
    """{
  "queries": ["totalRevenue", "salesRows"],
  "msgCases": ["LoadData", "SelectRow", "Reset"],
  "queryRowTypes": { "salesRows": "SaleRow" }
}"""

let private validManifestNoRowType =
    """{
  "queries": ["totalRevenue", "salesRows", "unknownRows"],
  "msgCases": ["LoadData", "SelectRow", "Reset"]
}"""

// ─── Tests ──────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Validator end-to-end"
        [ test "Valid tree against full manifest produces no findings" {
              let dir = freshDir "valid"
              let projectPath = writeFsproj dir "Valid.fsproj"
              writeFile dir "Source.fs" validTreeSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isEmpty result.Findings "expected zero findings on a clean tree"
          }

          test "Binding.Computed is an advisory Warning by default (FUARAN084)" {
              let dir = freshDir "computed-advisory"
              let projectPath = writeFsproj dir "Computed.fsproj"
              writeFile dir "Source.fs" computedBindingSource |> ignore

              let result = runValidatorOrch false projectPath
              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN084")
              Expect.equal f.Severity Warning "hand-authored Binding.Computed is advisory"
              Expect.isSome f.Suggestion "the finding names a recoverable alternative"
          }

          test "Binding.Computed escalates to Error in an orchestrated context (FUARAN084)" {
              let dir = freshDir "computed-orch"
              let projectPath = writeFsproj dir "Computed.fsproj"
              writeFile dir "Source.fs" computedBindingSource |> ignore

              let result = runValidatorOrch true projectPath
              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN084")
              Expect.equal f.Severity Error "orchestrated Binding.Computed is an error"
          }

          test "Duplicate NodeId within one tree is an Error (FUARAN001)" {
              let dir = freshDir "dup"
              let projectPath = writeFsproj dir "Dup.fsproj"
              writeFile dir "Source.fs" duplicateNodeIdSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN001" result.Findings) "FUARAN001 raised"
              Expect.isGreaterThan (severityCount Error result.Findings) 0 "at least one Error"
          }

          test "Cross-tree duplicate NodeId is a Warning (FUARAN002), not an Error" {
              let dir = freshDir "cross"
              let projectPath = writeFsproj dir "Cross.fsproj"
              writeFile dir "Source.fs" crossTreeDuplicateSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN002" result.Findings) "FUARAN002 raised"
              Expect.equal (severityCount Error result.Findings) 0 "no Errors"
          }

          test "Unresolved binding.query is an Error (FUARAN010) with a suggestion" {
              let dir = freshDir "uq"
              let projectPath = writeFsproj dir "Uq.fsproj"
              writeFile dir "Source.fs" unresolvedQuerySource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              let unresolved = result.Findings |> List.tryFind (fun f -> f.Code = "FUARAN010")

              Expect.isSome unresolved "FUARAN010 raised"

              match unresolved with
              | Some f ->
                  Expect.isSome f.AvailableFields "available_fields populated"
                  Expect.isSome f.Suggestion "best-guess suggestion populated"

                  match f.Suggestion with
                  | Some s -> Expect.equal s "totalRevenue" "suggestion is the closest registered name"
                  | None -> ()
              | None -> ()
          }

          test "Mistyped Msg case in Action.Dispatch is an Error (FUARAN020)" {
              let dir = freshDir "mm"
              let projectPath = writeFsproj dir "Mm.fsproj"
              writeFile dir "Source.fs" mistypedMsgCaseSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              let mistyped = result.Findings |> List.tryFind (fun f -> f.Code = "FUARAN020")

              Expect.isSome mistyped "FUARAN020 raised"

              match mistyped with
              | Some f ->
                  match f.Suggestion with
                  | Some s -> Expect.equal s "LoadData" "suggestion fixes the typo"
                  | None -> failtest "expected a suggestion for the typo'd case"
              | None -> ()
          }

          test "Blank Href on Fuaran.link is a Warning (FUARAN063)" {
              let dir = freshDir "blank-href"
              let projectPath = writeFsproj dir "BlankHref.fsproj"
              writeFile dir "Source.fs" blankHrefLinkSource |> ignore

              let result = runValidator projectPath None

              Expect.isTrue (hasCode "FUARAN063" result.Findings) "FUARAN063 raised for blank href"
              Expect.equal (severityCount Error result.Findings) 0 "advisory only — no Errors"
          }

          test "Non-blank Href on Fuaran.link raises no FUARAN063" {
              let dir = freshDir "valid-link"
              let projectPath = writeFsproj dir "ValidLink.fsproj"
              writeFile dir "Source.fs" validLinkSource |> ignore

              let result = runValidator projectPath None

              Expect.isFalse (hasCode "FUARAN063" result.Findings) "a real href is not flagged"
          }

          test "Annotated row-type mismatch in Fuaran.grid is an Error (FUARAN031)" {
              let dir = freshDir "rt"
              let projectPath = writeFsproj dir "Rt.fsproj"
              writeFile dir "Source.fs" rowTypeMismatchSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN031" result.Findings) "FUARAN031 raised"
              Expect.isGreaterThan (severityCount Error result.Findings) 0 "Errors present"
          }

          test "Missing queryRowTypes entry downgrades the row-type check to a Warning (FUARAN030)" {
              let dir = freshDir "rt-missing-rowtype"
              let projectPath = writeFsproj dir "RtMissing.fsproj"
              writeFile dir "Source.fs" rowTypeMissingManifestEntrySource |> ignore

              let manifestPath =
                  writeFile dir "fuaran-validator.manifest.json" validManifestNoRowType

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN030" result.Findings) "FUARAN030 raised"
              Expect.equal (severityCount Error result.Findings) 0 "no Errors"
          }

          test "Missing manifest entirely raises FUARAN900 Warning + silences schema checks" {
              let dir = freshDir "no-manifest"
              let projectPath = writeFsproj dir "NoManifest.fsproj"
              writeFile dir "Source.fs" unresolvedQuerySource |> ignore

              let result = runValidator projectPath None

              Expect.isTrue (hasCode "FUARAN900" result.Findings) "FUARAN900 raised (manifest missing)"
              Expect.isFalse (hasCode "FUARAN010" result.Findings) "FUARAN010 silenced (no manifest)"
              Expect.equal (severityCount Error result.Findings) 0 "no Errors when manifest is absent"
          }

          test "AI-recovery shape: FUARAN010 finding carries available_fields + suggestion" {
              let dir = freshDir "ai-shape"
              let projectPath = writeFsproj dir "AiShape.fsproj"
              writeFile dir "Source.fs" unresolvedQuerySource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN010")

              let json = ErrorRender.renderJson f

              Expect.stringContains json "\"available_fields\":" "rendered JSON has available_fields"
              Expect.stringContains json "\"suggestion\":" "rendered JSON has suggestion"
              Expect.stringContains json "\"code\":\"FUARAN010\"" "code field present"
          }

          test "Plain-format rendering of an Error includes severity + code + message" {
              let dir = freshDir "plain-fmt"
              let projectPath = writeFsproj dir "Plain.fsproj"
              writeFile dir "Source.fs" duplicateNodeIdSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN001")

              let plain = ErrorRender.renderPlain f

              Expect.stringContains plain "error" "severity rendered"
              Expect.stringContains plain "FUARAN001" "code rendered"
              Expect.stringContains plain "shared-id" "duplicate id surfaces in the message"
          }

          test "FilesWalked counts only the .fs sources under the project dir (ignores bin/obj)" {
              let dir = freshDir "files-counted"
              let projectPath = writeFsproj dir "Files.fsproj"
              writeFile dir "A.fs" validTreeSource |> ignore
              writeFile dir "B.fs" "module Sample.B" |> ignore
              Directory.CreateDirectory(Path.Combine(dir, "obj")) |> ignore
              writeFile (Path.Combine(dir, "obj")) "Generated.fs" "module Generated" |> ignore

              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.equal result.FilesWalked 2 "B.fs + A.fs, obj/Generated.fs ignored"
          }

          test "Empty Fuaran.UI tree (zero smart-ctor calls) yields no findings" {
              let dir = freshDir "empty-tree"
              let projectPath = writeFsproj dir "Empty.fsproj"

              writeFile dir "Source.fs" "module Sample.Empty\nlet x = 1" |> ignore

              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isEmpty result.Findings "no findings when the project has no Fuaran calls"
          }

          test "Out-of-[0,1] progress Fraction literal is an advisory Warning (FUARAN050)" {
              let dir = freshDir "progress-out-of-range"
              let projectPath = writeFsproj dir "Progress.fsproj"
              writeFile dir "Source.fs" outOfRangeProgressSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN050" result.Findings) "FUARAN050 raised"
              Expect.equal (severityCount Error result.Findings) 0 "advisory only — no Errors"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN050")
              Expect.stringContains f.Message "supportedRange=" "carries supportedRange"
              Expect.isSome f.Suggestion "carries a recovery suggestion"
          }

          test "In-range progress Fraction literal raises no FUARAN050" {
              let dir = freshDir "progress-in-range"
              let projectPath = writeFsproj dir "ProgressOk.fsproj"
              writeFile dir "Source.fs" inRangeProgressSource |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isFalse (hasCode "FUARAN050" result.Findings) "no advisory for an in-range fraction"
          }

          test "button with Disabled = Some (Binding.Static false) is a no-op Warning (FUARAN064)" {
              let source =
                  """module Sample.DisabledNoOp

open Fuaran.UI
open Fuaran.UI.Types

type Msg = Reload

let build () =
    Fuaran.button "btn-reload"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Reload"
            OnClick = Action.dispatch Reload
            Disabled = Some (Binding.Static false) }
"""

              let dir = freshDir "button-disabled-noop"
              let projectPath = writeFsproj dir "DisabledNoOp.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isTrue (hasCode "FUARAN064" result.Findings) "FUARAN064 raised for constant-false Disabled"
              Expect.equal (severityCount Error result.Findings) 0 "advisory only — no Errors"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN064")
              Expect.isSome f.Suggestion "carries a recovery suggestion"
          }

          test "button with Disabled = Some (Binding.Static true) is a legitimate placeholder (no FUARAN064)" {
              // A permanently-disabled placeholder button is a real use; only
              // the constant-FALSE no-op is flagged.
              let source =
                  """module Sample.DisabledPlaceholder

open Fuaran.UI
open Fuaran.UI.Types

type Msg = Reload

let build () =
    Fuaran.button "btn-reload"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Reload"
            OnClick = Action.dispatch Reload
            Disabled = Some (Binding.Static true) }
"""

              let dir = freshDir "button-disabled-placeholder"
              let projectPath = writeFsproj dir "DisabledPlaceholder.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isFalse
                  (hasCode "FUARAN064" result.Findings)
                  "Static true is a legitimate permanent-disable, not flagged"
          }

          test "button with Disabled bound to state is the intended shape (no FUARAN064)" {
              let source =
                  """module Sample.DisabledBound

open Fuaran.UI
open Fuaran.UI.Types

type Msg = Reload

let build () =
    Fuaran.button "btn-reload"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Reload"
            OnClick = Action.dispatch Reload
            Disabled = Some (binding.state "loading" false) }
"""

              let dir = freshDir "button-disabled-bound"
              let projectPath = writeFsproj dir "DisabledBound.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isFalse
                  (hasCode "FUARAN064" result.Findings)
                  "a live state binding is the intended shape, not flagged"
          }

          test "binding.local with format = None inside a Text field raises FUARAN042" {
              let source =
                  """module Sample.LocalNoFormat

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetSalary of decimal

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "salary"
                      Kind =
                          FormFieldKind.Text(
                              binding.local
                                  (binding.state "salary" "0")
                                  LocalFlushTrigger.OnBlur
                                  (fun s -> Action.dispatch (SetSalary 0m))
                                  None
                                  (fun s -> Ok s),
                              (fun _ -> Action.Chain [])) } ] }
"""

              let dir = freshDir "local-noformat"
              let projectPath = writeFsproj dir "LocalNoFormat.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue
                  (hasCode "FUARAN042" result.Findings)
                  "FUARAN042 raised for binding.local with format = None"
          }

          test "binding.local with OnCommitAction and no Action.CommitLocal raises FUARAN043" {
              let source =
                  """module Sample.LocalNoCommitRef

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetSalary of decimal

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "salary"
                      Kind =
                          FormFieldKind.Text(
                              binding.local
                                  (binding.state "salary" "0")
                                  LocalFlushTrigger.OnCommitAction
                                  (fun s -> Action.dispatch (SetSalary 0m))
                                  (Some id)
                                  (fun s -> Ok s),
                              (fun _ -> Action.Chain [])) } ] }
"""

              let dir = freshDir "local-no-commit-ref"
              let projectPath = writeFsproj dir "LocalNoCommitRef.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue
                  (hasCode "FUARAN043" result.Findings)
                  "FUARAN043 raised when no Action.CommitLocal partner exists in the project"
          }

          test "binding.local outside a FormFieldKind enclosing context raises FUARAN044" {
              // Force the binding into a bare let-binding so the walker sees
              // no enclosing FormFieldKind.Text / .Number — exactly the
              // misplaced-binding shape FUARAN044 guards against.
              let source =
                  """module Sample.LocalOnNonInput

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let misplacedLocal: Binding<string> =
    binding.local
        (binding.state "salary" "0")
        LocalFlushTrigger.OnBlur
        (fun s -> Action.dispatch NoOp)
        (Some id)
        (fun s -> Ok s)
"""

              let dir = freshDir "local-on-noninput"
              let projectPath = writeFsproj dir "LocalOnNonInput.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue
                  (hasCode "FUARAN044" result.Findings)
                  "FUARAN044 raised for binding.local outside FormFieldKind.Text / .Number"
          }

          test "FormFieldKind.rangedNumber with value below min raises FUARAN051" {
              let source =
                  """module Sample.NumberBelowMin

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetYear of float

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "year"
                      Kind =
                          FormFieldKind.rangedNumber
                              (binding.``static`` 1900.0)
                              (fun v -> Action.dispatch (SetYear v))
                              (min = 1979.0)
                              (max = 2028.0) } ] }
"""

              let dir = freshDir "rangednumber-below-min"
              let projectPath = writeFsproj dir "NumberBelowMin.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN051" result.Findings) "FUARAN051 raised for value below declared min"
              Expect.equal (severityCount Error result.Findings) 0 "advisory only — no Errors"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN051")
              Expect.stringContains f.Message "supportedRange=" "carries supportedRange"
              Expect.isSome f.Suggestion "carries a recovery suggestion"
          }

          test "FormFieldKind.rangedNumber with value above max raises FUARAN051" {
              let source =
                  """module Sample.NumberAboveMax

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetYear of float

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "year"
                      Kind =
                          FormFieldKind.rangedNumber
                              (binding.``static`` 2050.0)
                              (fun v -> Action.dispatch (SetYear v))
                              (min = 1979.0)
                              (max = 2028.0) } ] }
"""

              let dir = freshDir "rangednumber-above-max"
              let projectPath = writeFsproj dir "NumberAboveMax.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN051" result.Findings) "FUARAN051 raised for value above declared max"
          }

          test "FormFieldKind.rangedNumber with in-range value raises no FUARAN051" {
              let source =
                  """module Sample.NumberInRange

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetYear of float

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "year"
                      Kind =
                          FormFieldKind.rangedNumber
                              (binding.``static`` 2024.0)
                              (fun v -> Action.dispatch (SetYear v))
                              (min = 1979.0)
                              (max = 2028.0) } ] }
"""

              let dir = freshDir "rangednumber-in-range"
              let projectPath = writeFsproj dir "NumberInRange.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isFalse (hasCode "FUARAN051" result.Findings) "no advisory for an in-range rangedNumber value"
          }

          // ── SegmentedChoice option-count advisory (FUARAN045) ──

          test "FormFieldKind.segmentedChoice with >7 static options raises FUARAN045" {
              let source =
                  """module Sample.SegmentedTooMany

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetTier of string option

let opts: SelectOption list =
    [ { Value = "a"; Label = TextSource.Literal "A" }
      { Value = "b"; Label = TextSource.Literal "B" }
      { Value = "c"; Label = TextSource.Literal "C" }
      { Value = "d"; Label = TextSource.Literal "D" }
      { Value = "e"; Label = TextSource.Literal "E" }
      { Value = "f"; Label = TextSource.Literal "F" }
      { Value = "g"; Label = TextSource.Literal "G" }
      { Value = "h"; Label = TextSource.Literal "H" } ]

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "tier"
                      Kind =
                          FormFieldKind.segmentedChoice
                              (binding.``static``
                                  [ { Value = "a"; Label = TextSource.Literal "A" }
                                    { Value = "b"; Label = TextSource.Literal "B" }
                                    { Value = "c"; Label = TextSource.Literal "C" }
                                    { Value = "d"; Label = TextSource.Literal "D" }
                                    { Value = "e"; Label = TextSource.Literal "E" }
                                    { Value = "f"; Label = TextSource.Literal "F" }
                                    { Value = "g"; Label = TextSource.Literal "G" }
                                    { Value = "h"; Label = TextSource.Literal "H" } ])
                              (binding.``static`` None)
                              (fun v -> Action.dispatch (SetTier v))
                              Orientation.Horizontal } ] }
"""

              let dir = freshDir "segmented-too-many"
              let projectPath = writeFsproj dir "SegmentedTooMany.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN045" result.Findings) "FUARAN045 raised for SegmentedChoice with 8 options"

              Expect.equal (severityCount Error result.Findings) 0 "advisory only — no Errors"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN045")
              Expect.stringContains f.Message "8 options" "carries the offending count"
              Expect.isSome f.Suggestion "carries a recovery suggestion"
          }

          test "FormFieldKind.segmentedChoice with 5 static options raises no FUARAN045" {
              let source =
                  """module Sample.SegmentedFive

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetTier of string option

let build () =
    Fuaran.form "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "tier"
                      Kind =
                          FormFieldKind.segmentedChoice
                              (binding.``static``
                                  [ { Value = "a"; Label = TextSource.Literal "A" }
                                    { Value = "b"; Label = TextSource.Literal "B" }
                                    { Value = "c"; Label = TextSource.Literal "C" }
                                    { Value = "d"; Label = TextSource.Literal "D" }
                                    { Value = "e"; Label = TextSource.Literal "E" } ])
                              (binding.``static`` None)
                              (fun v -> Action.dispatch (SetTier v))
                              Orientation.Horizontal } ] }
"""

              let dir = freshDir "segmented-five"
              let projectPath = writeFsproj dir "SegmentedFive.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isFalse
                  (hasCode "FUARAN045" result.Findings)
                  "no advisory for a SegmentedChoice within the recommended threshold"
          }

          // ── Tabs-shape rules (FUARAN047 / FUARAN048 / FUARAN049) ──

          test "Fuaran.tabs with TabHeaders.Length < Children.Length raises FUARAN047" {
              let source =
                  """module Sample.TabsHeaderMismatch

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let build () =
    Fuaran.dashboard "tabs-dash"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.tabs "results-tabs"
                    { Defaults.tabs<Msg> with
                        Children =
                            [ Fuaran.markdown "overview" "Overview"
                              Fuaran.markdown "detail" "Detail" ]
                        TabHeaders =
                            Some
                                [ { Defaults.tabHeader with
                                      Label = TextSource.Literal "Overview" } ] } ] }
"""

              let dir = freshDir "tabs-header-mismatch"
              let projectPath = writeFsproj dir "TabsHeader.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN047" result.Findings) "FUARAN047 raised"
              Expect.isGreaterThan (severityCount Error result.Findings) 0 "at least one Error"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN047")
              Expect.stringContains f.Message "TabHeaders with 1 entries but 2 Children" "carries the offending counts"
          }

          test "Fuaran.tabs with TabTags.Length < Children.Length raises FUARAN048" {
              let source =
                  """module Sample.TabsTagMismatch

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let build () =
    Fuaran.dashboard "tabs-dash"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.tabs "results-tabs"
                    { Defaults.tabs<Msg> with
                        Children =
                            [ Fuaran.markdown "overview" "Overview"
                              Fuaran.markdown "detail" "Detail" ]
                        TabTags = Some [ "overview" ] } ] }
"""

              let dir = freshDir "tabs-tag-mismatch"
              let projectPath = writeFsproj dir "TabsTag.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN048" result.Findings) "FUARAN048 raised"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN048")
              Expect.stringContains f.Message "TabTags with 1 entries but 2 Children" "carries the offending counts"
          }

          test "Fuaran.tabs with ActiveTag = Some _ but TabTags = None raises FUARAN049 (Warning)" {
              let source =
                  """module Sample.TabsActiveTagOrphan

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let build () =
    Fuaran.dashboard "tabs-dash"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.tabs "results-tabs"
                    { Defaults.tabs<Msg> with
                        Children = [ Fuaran.markdown "overview" "Overview" ]
                        ActiveTag = Some (Binding.Static "overview") } ] }
"""

              let dir = freshDir "tabs-active-tag-orphan"
              let projectPath = writeFsproj dir "TabsActiveTag.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN049" result.Findings) "FUARAN049 raised"
              Expect.equal (severityCount Error result.Findings) 0 "FUARAN049 is Warning, not Error"
          }

          test "Aligned Fuaran.tabs spec (headers + tags + active-tag all parallel to children) raises no Tabs findings" {
              let source =
                  """module Sample.TabsAligned

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let build () =
    Fuaran.dashboard "tabs-dash"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.tabs "results-tabs"
                    { Defaults.tabs<Msg> with
                        Children =
                            [ Fuaran.markdown "overview" "Overview"
                              Fuaran.markdown "detail" "Detail" ]
                        TabHeaders =
                            Some
                                [ { Defaults.tabHeader with
                                      Label = TextSource.Literal "Overview" }
                                  { Defaults.tabHeader with
                                      Label = TextSource.Literal "Detail" } ]
                        TabTags = Some [ "overview"; "detail" ]
                        ActiveTag = Some (Binding.Static "overview") } ] }
"""

              let dir = freshDir "tabs-aligned"
              let projectPath = writeFsproj dir "TabsAligned.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isFalse (hasCode "FUARAN047" result.Findings) "no header-count finding on aligned spec"
              Expect.isFalse (hasCode "FUARAN048" result.Findings) "no tag-count finding on aligned spec"
              Expect.isFalse (hasCode "FUARAN049" result.Findings) "no active-tag-orphan finding on aligned spec"
          }

          test "Module-pattern filter restricts the walked file set" {
              let dir = freshDir "module-pattern"
              let projectPath = writeFsproj dir "Pattern.fsproj"
              writeFile dir "Kept.fs" duplicateNodeIdSource |> ignore
              writeFile dir "Skipped.fs" duplicateNodeIdSource |> ignore

              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result =
                  Validator.run
                      { ProjectPath = projectPath
                        ModulePattern = Some "Kept"
                        ManifestPath = Some manifestPath
                        Orchestrated = false }
                  |> Async.RunSynchronously

              Expect.equal result.FilesWalked 1 "only Kept.fs walked"
          }

          test "Format.Currency with a blank ISO code raises FUARAN061 (Error)" {
              let source =
                  """module Sample.BlankCurrency

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    binding.format (binding.``static`` 1234.5) (Format.Currency "") locale.ambient
"""

              let dir = freshDir "format-blank-currency"
              let projectPath = writeFsproj dir "BlankCurrency.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN061" result.Findings) "FUARAN061 raised for blank ISO code"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN061")
              Expect.equal f.Severity Error "blank currency code is an Error"
              Expect.isSome f.Suggestion "carries a recovery suggestion"
          }

          test "localeFormat.currency with a blank ISO code raises FUARAN061" {
              let source =
                  """module Sample.BlankCurrencySmartCtor

open Fuaran.UI
open Fuaran.UI.Types

let build () = localeFormat.currency "   "
"""

              let dir = freshDir "format-blank-currency-ctor"
              let projectPath = writeFsproj dir "BlankCurrencyCtor.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN061" result.Findings) "FUARAN061 raised for whitespace ISO code"
          }

          test "Format.Currency with a valid ISO code raises no FUARAN061" {
              let source =
                  """module Sample.ValidCurrency

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    binding.format (binding.``static`` 1234.5) (Format.Currency "GBP") (locale.explicit "en-GB")
"""

              let dir = freshDir "format-valid-currency"
              let projectPath = writeFsproj dir "ValidCurrency.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isFalse (hasCode "FUARAN061" result.Findings) "no finding for a valid ISO code"
          }

          // ─── FUARAN062 — build-time Custom content-hash (Phase 134) ──────

          test "computeBodyShapeHash is deterministic and shape-sensitive" {
              let baseHash =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "scale"; "palette" ]
                      [ "cell-grid" ]

              // Same shape (keys re-ordered) → same hash: order-insensitive.
              let reordered =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "palette"; "scale" ]
                      [ "cell-grid" ]

              Expect.equal reordered baseHash "prop-key order does not change the hash"

              // Added prop key → different hash: a changed body changes the hash.
              let bodyChanged =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "scale"; "palette"; "legend" ]
                      [ "cell-grid" ]

              Expect.notEqual bodyChanged baseHash "an added prop key changes the hash"
              Expect.equal baseHash.Length 64 "SHA-256 renders as 64 hex chars"
          }

          test "Stale Custom contentHash under Enforced raises FUARAN062 (Error)" {
              let source =
                  """module Sample.StaleEnforced

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.custom "heatmap" "reporting" "HeatmapTab"
        (Map.ofList [ "scale", JsonValue 1; "palette", JsonValue 2 ])
        (Some { Algorithm = "SHA256"; Hash = "reporting.HeatmapTab.v1"; Strictness = HashStrictness.Enforced })
        [ NodeId "cell-grid" ]
"""

              let dir = freshDir "custom-hash-stale-enforced"
              let projectPath = writeFsproj dir "StaleEnforced.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN062" result.Findings) "FUARAN062 raised for stale hash"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN062")
              Expect.equal f.Severity Error "Enforced strictness escalates the stale hash to Error"
              Expect.isSome f.Suggestion "carries the computed hash as a recovery suggestion"
          }

          test "Stale Custom contentHash under AdvisoryWarning raises FUARAN062 (Warning)" {
              let source =
                  """module Sample.StaleAdvisory

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.custom "heatmap" "reporting" "HeatmapTab"
        (Map.ofList [ "scale", JsonValue 1 ])
        (Some { Algorithm = "SHA256"; Hash = "0000000000000000000000000000000000000000000000000000000000000000"; Strictness = HashStrictness.AdvisoryWarning })
        []
"""

              let dir = freshDir "custom-hash-stale-advisory"
              let projectPath = writeFsproj dir "StaleAdvisory.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isTrue (hasCode "FUARAN062" result.Findings) "FUARAN062 raised for stale hash"

              let f = result.Findings |> List.find (fun f -> f.Code = "FUARAN062")
              Expect.equal f.Severity Warning "AdvisoryWarning strictness keeps the stale hash a Warning"
          }

          test "Matching computed Custom contentHash raises no FUARAN062" {
              let computed =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "scale"; "palette" ]
                      [ "cell-grid" ]

              let source =
                  sprintf
                      """module Sample.MatchingHash

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.custom "heatmap" "reporting" "HeatmapTab"
        (Map.ofList [ "scale", JsonValue 1; "palette", JsonValue 2 ])
        (Some { Algorithm = "SHA256"; Hash = "%s"; Strictness = HashStrictness.Enforced })
        [ NodeId "cell-grid" ]
"""
                      computed

              let dir = freshDir "custom-hash-match"
              let projectPath = writeFsproj dir "MatchingHash.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              Expect.isFalse (hasCode "FUARAN062" result.Findings) "a matching computed hash produces no drift finding"
          }

          test "Custom node with no contentHash raises no FUARAN062" {
              let source =
                  """module Sample.NoHash

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.custom "heatmap" "reporting" "HeatmapTab" Map.empty None []
"""

              let dir = freshDir "custom-hash-none"
              let projectPath = writeFsproj dir "NoHash.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              // No hash → FUARAN062 stays silent (that absence is FUARAN055's job).
              Expect.isFalse (hasCode "FUARAN062" result.Findings) "no hash means no content-hash drift finding"
          }

          test "Custom node with non-literal props is skipped by FUARAN062" {
              let source =
                  """module Sample.DynamicProps

open Fuaran.UI
open Fuaran.UI.Types

let build (dynamicProps: Map<string, JsonValue>) =
    Fuaran.custom "heatmap" "reporting" "HeatmapTab"
        dynamicProps
        (Some { Algorithm = "SHA256"; Hash = "reporting.HeatmapTab.v1"; Strictness = HashStrictness.Enforced })
        [ NodeId "cell-grid" ]
"""

              let dir = freshDir "custom-hash-dynamic"
              let projectPath = writeFsproj dir "DynamicProps.fsproj"
              writeFile dir "Source.fs" source |> ignore
              let manifestPath = writeFile dir "fuaran-validator.manifest.json" validManifest

              let result = runValidator projectPath (Some manifestPath)

              // The props shape isn't statically resolvable → conservative skip.
              Expect.isFalse (hasCode "FUARAN062" result.Findings) "unresolvable body shape is not flagged"
          }

          test "Parameterised fragment with an unbounded Repeat count raises FUARAN059 (totality)" {
              let source =
                  """module Sample.RepeatUnbounded

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.fragmentDecl "decl"
        { Defaults.fragmentDecl with
            Name = FragmentId "rep"
            Body = Fuaran.markdown "b" "x"
            Holes = [ HoleDecl.Repeat("rows", HoleValueSpace.AnyString) ] }
"""

              let dir = freshDir "frag-repeat-unbounded"
              let projectPath = writeFsproj dir "RepeatUnbounded.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isTrue (hasCode "FUARAN059" result.Findings) "FUARAN059 raised for an unbounded Repeat count"
          }

          test "Parameterised fragment with a bounded IntRange Repeat count raises no FUARAN059" {
              let source =
                  """module Sample.RepeatBounded

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.fragmentDecl "decl"
        { Defaults.fragmentDecl with
            Name = FragmentId "rep"
            Body = Fuaran.markdown "b" "x"
            Holes = [ HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 12)) ] }
"""

              let dir = freshDir "frag-repeat-bounded"
              let projectPath = writeFsproj dir "RepeatBounded.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isFalse (hasCode "FUARAN059" result.Findings) "no totality finding for a bounded IntRange count"
          }

          test "Value hole whose default is outside its value-space raises FUARAN065" {
              let source =
                  """module Sample.DefaultOutOfRange

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.fragmentDecl "decl"
        { Defaults.fragmentDecl with
            Name = FragmentId "card"
            Body = Fuaran.markdown "b" "x"
            Holes = [ HoleDecl.Value("count", HoleValueSpace.IntRange(0, 10), Some(box 50)) ] }
"""

              let dir = freshDir "frag-default-oor"
              let projectPath = writeFsproj dir "DefaultOutOfRange.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isTrue (hasCode "FUARAN065" result.Findings) "FUARAN065 raised for an out-of-range default"
          }

          test "Value hole whose default is inside its value-space raises no FUARAN065" {
              let source =
                  """module Sample.DefaultInRange

open Fuaran.UI
open Fuaran.UI.Types

let build () =
    Fuaran.fragmentDecl "decl"
        { Defaults.fragmentDecl with
            Name = FragmentId "card"
            Body = Fuaran.markdown "b" "x"
            Holes =
                [ HoleDecl.Value("count", HoleValueSpace.IntRange(0, 100), Some(box 7))
                  HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), Some(box "ok")) ] }
"""

              let dir = freshDir "frag-default-inrange"
              let projectPath = writeFsproj dir "DefaultInRange.fsproj"
              writeFile dir "Source.fs" source |> ignore

              let result = runValidator projectPath None

              Expect.isFalse (hasCode "FUARAN065" result.Findings) "no value-space finding for an in-range default"
          } ]
