module Fuaran.UI.Validator.Tests.ValidatorTests

// ============================================================================
//  End-to-end validator tests.
//
//  Every source a test validates is a COMPILED snippet: `Snippets/*.fs` are
//  compile items of this project, so a snippet that does not type-check
//  against the current `Fuaran.UI` breaks this build rather than quietly
//  testing a shape no author can write (Phase 2053 — the walker's rules had
//  drifted from the types precisely because their tests parsed snippets that
//  would not build). A test copies the snippets it needs into a fresh temp
//  project directory, optionally writes a manifest beside them, and runs the
//  validator over it.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open FSharp.Compiler.CodeAnalysis
open Expecto
open Fuaran.UI.Validator
open Fuaran.UI.Validator.Findings

// ─── Harness ────────────────────────────────────────────────────────────────

let private freshDir (name: string) =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-validator-%s-%s" name (Guid.NewGuid().ToString("N")))

    Directory.CreateDirectory path |> ignore
    path

let private writeFile (dir: string) (name: string) (contents: string) =
    let path = Path.Combine(dir, name)
    File.WriteAllText(path, contents)
    path

let private writeFsproj (dir: string) =
    writeFile
        dir
        "Snippet.fsproj"
        """<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
"""

let private snippetSource (name: string) =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "Snippets", name + ".fs"))

/// The manifest most tests run against.
let private validManifest =
    """{
  "queries": ["totalRevenue", "salesRows"],
  "msgCases": ["LoadData", "SelectRow", "Reset"],
  "queryRowTypes": { "salesRows": "SaleRow" }
}"""

/// Queries registered, but no row types — the FUARAN030 shape.
let private manifestNoRowType =
    """{
  "queries": ["totalRevenue", "salesRows", "unknownRows"],
  "msgCases": ["LoadData", "SelectRow", "Reset"]
}"""

type private Setup =
    {
        Snippets: string list
        Manifest: string option
        Orchestrated: bool
        /// Text prepended to the first snippet (a suppression pragma).
        Prefix: string
    }

let private setup snippets =
    { Snippets = snippets
      Manifest = Some validManifest
      Orchestrated = false
      Prefix = "" }

let private materialise (s: Setup) =
    let dir = freshDir "snippet"
    let projectPath = writeFsproj dir

    s.Snippets
    |> List.iteri (fun i name ->
        let text = snippetSource name
        writeFile dir (name + ".fs") (if i = 0 then s.Prefix + text else text) |> ignore)

    let manifestPath =
        s.Manifest |> Option.map (writeFile dir "fuaran-validator.manifest.json")

    dir, projectPath, manifestPath

let private runWithParser (parse: Syntax.Parser) (s: Setup) : Validator.RunResult =
    let _, projectPath, manifestPath = materialise s

    Validator.runWith
        parse
        { ProjectPath = projectPath
          ModulePattern = None
          ManifestPath = manifestPath
          Orchestrated = s.Orchestrated }
    |> Async.RunSynchronously

let private run (s: Setup) : Validator.RunResult =
    let _, projectPath, manifestPath = materialise s

    Validator.run
        { ProjectPath = projectPath
          ModulePattern = None
          ManifestPath = manifestPath
          Orchestrated = s.Orchestrated }
    |> Async.RunSynchronously

let private findings (s: Setup) = (run s).Findings

let private codes (code: string) (fs: Finding list) =
    fs |> List.filter (fun f -> f.Code = code)

let private hasCode (code: string) (fs: Finding list) = not (List.isEmpty (codes code fs))

let private errorCount (fs: Finding list) =
    fs |> List.filter (fun f -> f.Severity = Error) |> List.length

let private single (code: string) (fs: Finding list) =
    match codes code fs with
    | [ f ] -> f
    | other -> failtestf "expected exactly one %s, got %d: %A" code other.Length (fs |> List.map _.Code)

// ─── The census: every code the walker can emit fires on a compiled snippet ──

/// (code, setup) — the snippet that makes the code fire. One row per code the
/// walker's sources can emit; the census test below fails when a code appears
/// in the sources without a row here.
let private firing: (string * Setup) list =
    [ "FUARAN001", setup [ "DuplicateInBox" ]
      "FUARAN002", setup [ "CrossTree" ]
      "FUARAN010", setup [ "UnresolvedQuery" ]
      "FUARAN020", setup [ "MistypedMsg" ]
      "FUARAN030",
      { setup [ "RowTypeMissing" ] with
          Manifest = Some manifestNoRowType }
      "FUARAN031", setup [ "RowTypeMismatch" ]
      "FUARAN042", setup [ "LocalNoFormat" ]
      "FUARAN043", setup [ "LocalNoCommitRef" ]
      "FUARAN044", setup [ "LocalOutsideField" ]
      "FUARAN045", setup [ "SegmentedTooMany" ]
      "FUARAN046", setup [ "GridTemplateRepeat" ]
      "FUARAN050", setup [ "ProgressOutOfRange" ]
      "FUARAN051", setup [ "RangedBelowMin" ]
      "FUARAN053", setup [ "CustomRecordForm" ]
      "FUARAN054", setup [ "CustomRatio" ]
      "FUARAN055", setup [ "CustomNoHash" ]
      "FUARAN056", setup [ "FragmentDuplicateName" ]
      "FUARAN057", setup [ "FragmentUnresolved" ]
      "FUARAN058", setup [ "FragmentCycle" ]
      "FUARAN059", setup [ "RepeatUnbounded" ]
      "FUARAN060", setup [ "ExtraAttributeOnClick" ]
      "FUARAN061", setup [ "CurrencyBlankCase" ]
      "FUARAN062", setup [ "CustomStaleEnforced" ]
      "FUARAN063", setup [ "BlankHrefLink" ]
      "FUARAN064", setup [ "DisabledNoOp" ]
      "FUARAN065", setup [ "DefaultOutOfRange" ]
      "FUARAN084", setup [ "Computed" ]
      "FUARAN900",
      { setup [ "ValidTree" ] with
          Manifest = None } ]

/// Codes the walker's sources name without emitting: a documentation constant.
let private notEmitted = set [ "FUARAN052" ]

/// The codes the walker's own sources emit, read the way the repository's
/// code-allocation script reads them: a quoted `FUARAN###` literal.
let private walkerCodes () =
    let dir = Path.Combine(__SOURCE_DIRECTORY__, "..", "Fuaran.UI.Validator")

    Directory.GetFiles(dir, "*.fs")
    |> Seq.collect (fun f ->
        Regex.Matches(File.ReadAllText f, "\"(FUARAN\\d{3})\"")
        |> Seq.map _.Groups[1].Value)
    |> Set.ofSeq
    |> fun all -> Set.difference all notEmitted

let private census =
    testList
        "census"
        [ test "every code the walker emits has a firing snippet" {
              let emitted = walkerCodes ()
              let covered = firing |> List.map fst |> Set.ofList

              Expect.isGreaterThan emitted.Count 20 "the source scan found the walker's codes (probe sanity)"

              Expect.isEmpty (Set.difference emitted covered) "a code with no compiled snippet that makes it fire"

              Expect.isEmpty (Set.difference covered emitted) "a census row for a code the walker no longer emits"
          }

          yield!
              firing
              |> List.map (fun (code, s) ->
                  test (sprintf "%s fires on %s" code (String.concat ", " s.Snippets)) {
                      Expect.isTrue (hasCode code (findings s)) (sprintf "%s raised" code)
                  }) ]

// ─── Retired codes ──────────────────────────────────────────────────────────

let private retired =
    testList
        "retired build-time rules"
        [ test "the Tabs shapes are left to the runtime validator (no FUARAN047 / 048 / 049)" {
              let fs = findings (setup [ "RetiredTabsMismatch" ])

              for code in [ "FUARAN047"; "FUARAN048"; "FUARAN049" ] do
                  Expect.isFalse (hasCode code fs) (sprintf "%s is a runtime code now" code)
          }

          test "the accessibility shapes raise no FUARAN040 / 041" {
              let fs = findings (setup [ "RetiredAccessibility" ])
              Expect.isFalse (hasCode "FUARAN040" fs) "FUARAN040 retired (runtime FUARAN109)"
              Expect.isFalse (hasCode "FUARAN041" fs) "FUARAN041 withdrawn"
          } ]

// ─── Parse once ─────────────────────────────────────────────────────────────

let private parseOnce =
    testList
        "parse once"
        [ test "each source file is parsed exactly once per run, whatever the number of checks" {
              let checker = FSharpChecker.Create()
              let inner = Syntax.parser checker
              let counts = Collections.Concurrent.ConcurrentDictionary<string, int>()

              let counting: Syntax.Parser =
                  fun file ->
                      counts.AddOrUpdate(file, 1, (fun _ n -> n + 1)) |> ignore
                      inner file

              let snippets = [ "ValidTree"; "CustomRatio"; "FragmentCycle"; "LocalNoFormat" ]
              let result = runWithParser counting (setup snippets)

              Expect.equal result.FilesWalked 4 "four sources walked"
              Expect.equal counts.Count 4 "every source parsed"
              Expect.allEqual counts.Values 1 "no source parsed twice"
          } ]

// ─── Behaviour ──────────────────────────────────────────────────────────────

let private behaviour =
    testList
        "behaviour"
        [ test "a valid tree against the full manifest produces no findings" {
              Expect.isEmpty (findings (setup [ "ValidTree" ])) "zero findings on a clean tree"
          }

          test "Binding.Computed is an advisory Warning by default (FUARAN084)" {
              let f = single "FUARAN084" (findings (setup [ "Computed" ]))
              Expect.equal f.Severity Warning "hand-authored Binding.Computed is advisory"
              Expect.isSome f.Suggestion "the finding names a recoverable alternative"
          }

          test "Binding.Computed escalates to Error in an orchestrated context (FUARAN084)" {
              let f =
                  single
                      "FUARAN084"
                      (findings
                          { setup [ "Computed" ] with
                              Orchestrated = true })

              Expect.equal f.Severity Error "orchestrated Binding.Computed is an error"
          }

          test "a state-bound metric raises no FUARAN084" {
              Expect.isFalse (hasCode "FUARAN084" (findings (setup [ "ComputedNone" ]))) "no closure, no finding"
          }

          // ── NodeId uniqueness ─────────────────────────────────────────────

          test "a duplicate id under Fuaran.box is an Error (FUARAN001)" {
              let fs = findings (setup [ "DuplicateInBox" ])
              Expect.equal (codes "FUARAN001" fs).Length 2 "both duplicate sites reported"
              Expect.stringContains (codes "FUARAN001" fs).Head.Message "\"box-root\"" "named by the root's id"
          }

          test "a duplicate id under Fuaran.dashboard is an Error (FUARAN001)" {
              let fs = findings (setup [ "DuplicateInDashboard" ])
              Expect.isTrue (hasCode "FUARAN001" fs) "FUARAN001 raised"
              Expect.isGreaterThan (errorCount fs) 0 "at least one Error"
          }

          test "a cross-tree duplicate id is a Warning (FUARAN002), not an Error" {
              let fs = findings (setup [ "CrossTree" ])
              Expect.isTrue (hasCode "FUARAN002" fs) "FUARAN002 raised"
              Expect.equal (errorCount fs) 0 "no Errors"
          }

          test "two trees sharing a root id are two trees (FUARAN002), not one with duplicates (FUARAN001)" {
              let fs = findings (setup [ "SameRootId" ])
              Expect.isFalse (hasCode "FUARAN001" fs) "the two dashboards are distinct trees"
              Expect.isTrue (hasCode "FUARAN002" fs) "the shared ids surface as the cross-tree warning"
              Expect.equal (errorCount fs) 0 "no Errors"
          }

          test "a duplicate inside ONE tree is still an Error when a same-id sibling tree exists" {
              Expect.isTrue (hasCode "FUARAN001" (findings (setup [ "DupWithTwin" ]))) "the intra-tree duplicate errors"
          }

          test "a one-argument root call closes its tree (no FUARAN001 on later loose calls)" {
              // The old walker pushed the root of a one-argument call and never
              // popped it, so every later call in the file joined that tree.
              let fs = findings (setup [ "UnpoppedRoot" ])
              Expect.isFalse (hasCode "FUARAN001" fs) "loose helpers are in no tree"
          }

          // ── Schema-coupled checks ─────────────────────────────────────────

          test "an unresolved binding.query is an Error (FUARAN010) with a suggestion" {
              let f = single "FUARAN010" (findings (setup [ "UnresolvedQuery" ]))
              Expect.isSome f.AvailableFields "available_fields populated"
              Expect.equal f.Suggestion (Some "totalRevenue") "the closest registered name"
          }

          test "an unresolved query nested four calls deep is reported once (FUARAN010)" {
              // The old walker filed a query under every enclosing call, so the
              // one defect was reported once per nesting depth.
              let fs = findings (setup [ "NestedUnresolvedQuery" ])
              Expect.equal (codes "FUARAN010" fs).Length 1 "one finding for one reference"
          }

          test "a mistyped Msg case in Action.dispatch is an Error (FUARAN020)" {
              let f = single "FUARAN020" (findings (setup [ "MistypedMsg" ]))
              Expect.equal f.Suggestion (Some "LoadData") "the suggestion fixes the typo"
          }

          test "a toRow row-type mismatch in Fuaran.grid is an Error (FUARAN031)" {
              let fs = findings (setup [ "RowTypeMismatch" ])
              Expect.stringContains (single "FUARAN031" fs).Message "WrongRow" "names the annotated type"
          }

          test "a missing queryRowTypes entry downgrades the row-type check to a Warning (FUARAN030)" {
              let fs =
                  findings
                      { setup [ "RowTypeMissing" ] with
                          Manifest = Some manifestNoRowType }

              Expect.isTrue (hasCode "FUARAN030" fs) "FUARAN030 raised"
              Expect.equal (errorCount fs) 0 "no Errors"
          }

          test "a missing manifest raises FUARAN900 and silences the schema checks" {
              let fs =
                  findings
                      { setup [ "UnresolvedQuery"; "RowTypeMissing" ] with
                          Manifest = None }

              Expect.isTrue (hasCode "FUARAN900" fs) "FUARAN900 raised"
              Expect.isFalse (hasCode "FUARAN010" fs) "FUARAN010 silenced"
              Expect.isFalse (hasCode "FUARAN030" fs) "FUARAN030 silenced"
              Expect.equal (errorCount fs) 0 "no Errors"
          }

          test "AI-recovery shape: a FUARAN010 finding renders available_fields + suggestion" {
              let json =
                  ErrorRender.renderJson (single "FUARAN010" (findings (setup [ "UnresolvedQuery" ])))

              Expect.stringContains json "\"available_fields\":" "available_fields"
              Expect.stringContains json "\"suggestion\":" "suggestion"
              Expect.stringContains json "\"code\":\"FUARAN010\"" "code"
          }

          test "plain rendering of an Error carries severity, code and message" {
              let plain =
                  ErrorRender.renderPlain (codes "FUARAN001" (findings (setup [ "DuplicateInDashboard" ]))).Head

              Expect.stringContains plain "error" "severity"
              Expect.stringContains plain "FUARAN001" "code"
              Expect.stringContains plain "shared-id" "the duplicate id"
          }

          // ── File discovery ────────────────────────────────────────────────

          test "FilesWalked counts the .fs sources under the project dir, ignoring bin/obj" {
              let dir, projectPath, manifestPath = materialise (setup [ "ValidTree" ])
              writeFile dir "B.fs" "module Sample.B" |> ignore
              Directory.CreateDirectory(Path.Combine(dir, "obj")) |> ignore
              writeFile (Path.Combine(dir, "obj")) "Generated.fs" "module Generated" |> ignore

              let result =
                  Validator.run
                      { ProjectPath = projectPath
                        ModulePattern = None
                        ManifestPath = manifestPath
                        Orchestrated = false }
                  |> Async.RunSynchronously

              Expect.equal result.FilesWalked 2 "ValidTree.fs + B.fs, obj/Generated.fs ignored"
          }

          test "the module-pattern filter restricts the walked file set" {
              let _, projectPath, manifestPath =
                  materialise (setup [ "DuplicateInDashboard"; "CrossTree" ])

              let result =
                  Validator.run
                      { ProjectPath = projectPath
                        ModulePattern = Some "CrossTree"
                        ManifestPath = manifestPath
                        Orchestrated = false }
                  |> Async.RunSynchronously

              Expect.equal result.FilesWalked 1 "only CrossTree.fs walked"
              Expect.isFalse (hasCode "FUARAN001" result.Findings) "the filtered-out file is not checked"
          }

          // ── Scalar / link / button ────────────────────────────────────────

          test "an out-of-[0,1] progress Fraction is an advisory Warning (FUARAN050)" {
              let fs = findings (setup [ "ProgressOutOfRange" ])
              let f = single "FUARAN050" fs
              Expect.equal (errorCount fs) 0 "advisory only"
              Expect.stringContains f.Message "supportedRange=" "carries supportedRange"
              Expect.isSome f.Suggestion "carries a recovery suggestion"
          }

          test "an in-range progress Fraction raises no FUARAN050" {
              Expect.isFalse (hasCode "FUARAN050" (findings (setup [ "ProgressInRange" ]))) "in range"
          }

          test "a blank href is a Warning on Fuaran.link and on Fuaran.linkSpec (FUARAN063)" {
              let fs = findings (setup [ "BlankHrefLink"; "BlankHrefLinkSpec" ])
              Expect.equal (codes "FUARAN063" fs).Length 2 "positional and record forms"
              Expect.equal (errorCount fs) 0 "advisory only"
          }

          test "a real href raises no FUARAN063" {
              Expect.isFalse (hasCode "FUARAN063" (findings (setup [ "ValidLink" ]))) "not flagged"
          }

          test "Disabled = Some (Binding.Static (Some false)) is a no-op Warning (FUARAN064)" {
              let fs = findings (setup [ "DisabledNoOp"; "DisabledNoOpSmartCtor" ])
              Expect.equal (codes "FUARAN064" fs).Length 2 "the case and the smart constructor"
              Expect.isSome (codes "FUARAN064" fs).Head.Suggestion "carries a recovery suggestion"
          }

          test "a constant-true or state-bound Disabled raises no FUARAN064" {
              let fs = findings (setup [ "DisabledPlaceholder"; "DisabledBound" ])
              Expect.isFalse (hasCode "FUARAN064" fs) "placeholder and live binding are legitimate"
          }

          // ── Local bindings ────────────────────────────────────────────────

          test "binding.local with format = None inside a Text field raises FUARAN042 only" {
              let fs = findings (setup [ "LocalNoFormat" ])
              Expect.isTrue (hasCode "FUARAN042" fs) "FUARAN042 raised"
              Expect.isFalse (hasCode "FUARAN044" fs) "a Text field hosts the buffer"
          }

          test "binding.local with OnCommitAction and no Action.CommitLocal raises FUARAN043" {
              Expect.isTrue (hasCode "FUARAN043" (findings (setup [ "LocalNoCommitRef" ]))) "FUARAN043 raised"
          }

          test "binding.local inside RangedNumber, case or smart constructor, raises no FUARAN044" {
              Expect.isFalse (hasCode "FUARAN044" (findings (setup [ "LocalInRangedNumber" ]))) "hosted"
          }

          // ── Ranged number / segmented choice ──────────────────────────────

          test "a static value outside [min, max] raises FUARAN051 in every constructor shape" {
              let fs =
                  findings (setup [ "RangedBelowMin"; "RangedAboveMaxDeclarative"; "RangedAboveMaxCase" ])

              let found = codes "FUARAN051" fs
              Expect.equal found.Length 3 "rangedNumber, rangedNumberDeclarative and the case"
              Expect.equal (errorCount fs) 0 "advisory only"
              Expect.stringContains found.Head.Message "supportedRange=" "carries supportedRange"
          }

          test "an in-range static value raises no FUARAN051" {
              Expect.isFalse (hasCode "FUARAN051" (findings (setup [ "RangedInRange" ]))) "in range"
          }

          test "more than 7 static segmented options raises FUARAN045, smart constructor and case" {
              let fs = findings (setup [ "SegmentedTooMany"; "SegmentedTooManyCase" ])
              let found = codes "FUARAN045" fs
              Expect.equal found.Length 2 "both shapes"
              Expect.stringContains found.Head.Message "8 options" "carries the count"
          }

          test "five segmented options raise no FUARAN045" {
              Expect.isFalse (hasCode "FUARAN045" (findings (setup [ "SegmentedFive" ]))) "within threshold"
          }

          // ── Format / grid template / extra attributes ─────────────────────

          test "a blank currency code is an Error (FUARAN061), case and smart constructor" {
              let fs = findings (setup [ "CurrencyBlankCase"; "CurrencyBlankSmartCtor" ])
              let found = codes "FUARAN061" fs
              Expect.equal found.Length 2 "both shapes"
              Expect.equal found.Head.Severity Error "an Error"
          }

          test "a valid currency code raises no FUARAN061" {
              Expect.isFalse (hasCode "FUARAN061" (findings (setup [ "CurrencyValid" ]))) "valid"
          }

          test "an irregular template raises no FUARAN046" {
              Expect.isFalse (hasCode "FUARAN046" (findings (setup [ "GridTemplateIrregular" ]))) "irregular"
          }

          test "only the disallowed extra-attribute key is flagged (FUARAN060)" {
              let f = single "FUARAN060" (findings (setup [ "ExtraAttributeOnClick" ]))
              Expect.stringContains f.Message "\"onclick\"" "the on* key, not the data-* one"
          }

          // ── Custom ────────────────────────────────────────────────────────

          test "computeBodyShapeHash is deterministic and shape-sensitive" {
              let baseHash =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "scale"; "palette" ]
                      [ "cell-grid" ]

              let reordered =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "palette"; "scale" ]
                      [ "cell-grid" ]

              let changed =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "scale"; "palette"; "legend" ]
                      [ "cell-grid" ]

              Expect.equal reordered baseHash "prop-key order does not change the hash"
              Expect.notEqual changed baseHash "an added prop key changes the hash"
              Expect.equal baseHash.Length 64 "SHA-256 renders as 64 hex chars"
          }

          test "a stale contentHash is an Error under Enforced and a Warning under AdvisoryWarning (FUARAN062)" {
              let enforced = single "FUARAN062" (findings (setup [ "CustomStaleEnforced" ]))
              let advisory = single "FUARAN062" (findings (setup [ "CustomStaleAdvisory" ]))
              Expect.equal enforced.Severity Error "Enforced"
              Expect.isSome enforced.Suggestion "carries the computed hash"
              Expect.equal advisory.Severity Warning "AdvisoryWarning"
          }

          test "the NodeKind.Custom record form is checked too (FUARAN062, FUARAN053, FUARAN055)" {
              let fs = findings (setup [ "CustomRecordStaleHash"; "CustomRecordForm" ])
              Expect.isTrue (hasCode "FUARAN062" fs) "stale hash on the record form"
              Expect.isTrue (hasCode "FUARAN053" fs) "exposed ids with no registered renderer"
              Expect.isTrue (hasCode "FUARAN055" fs) "no contentHash"
          }

          test "a matching computed contentHash raises no FUARAN062" {
              let expected =
                  CustomContentHashCheck.computeBodyShapeHash
                      "reporting"
                      "HeatmapTab"
                      [ "scale"; "palette" ]
                      [ "cell-grid" ]

              Expect.stringContains (snippetSource "CustomMatchingHash") expected "the snippet pins the computed hash"
              Expect.isFalse (hasCode "FUARAN062" (findings (setup [ "CustomMatchingHash" ]))) "no drift"
          }

          test "no contentHash or non-literal props raise no FUARAN062" {
              let fs = findings (setup [ "CustomNoHash"; "CustomDynamicProps" ])
              Expect.isFalse (hasCode "FUARAN062" fs) "nothing to verify"
          }

          test "the Custom ratio is reported once for the project (FUARAN054)" {
              let f = single "FUARAN054" (findings (setup [ "CustomRatio" ]))
              Expect.stringContains f.Message "4 Custom" "counts the Custom sites"
          }

          // ── Fragments ─────────────────────────────────────────────────────

          test "fragment names resolve: a duplicate name errors, a resolved reference does not (FUARAN056 / 057)" {
              let dup = findings (setup [ "FragmentDuplicateName" ])
              Expect.equal (codes "FUARAN056" dup).Length 2 "both declarations"
              Expect.isFalse (hasCode "FUARAN057" dup) "the reference resolves"

              let unresolved = findings (setup [ "FragmentUnresolved" ])
              Expect.stringContains (single "FUARAN057" unresolved).Message "'headr'" "only the typo"
          }

          test "a fragment cycle errors (FUARAN058)" {
              Expect.isTrue (hasCode "FUARAN058" (findings (setup [ "FragmentCycle" ]))) "cycle"
          }

          test "a bounded Repeat count raises no FUARAN059" {
              Expect.isFalse (hasCode "FUARAN059" (findings (setup [ "RepeatBounded" ]))) "bounded"
          }

          test "an in-range Scalar default raises no FUARAN065" {
              Expect.isFalse (hasCode "FUARAN065" (findings (setup [ "DefaultInRange" ]))) "in range"
          }

          // ── Suppression pragmas ───────────────────────────────────────────

          test "a file-scoped disable pragma suppresses the named code and is counted" {
              let result =
                  run
                      { setup [ "DuplicateInDashboard" ] with
                          Prefix = "// fuaran-validator: disable FUARAN001 — negative-test fixture\n" }

              Expect.isFalse (hasCode "FUARAN001" result.Findings) "suppressed"
              Expect.equal (errorCount result.Findings) 0 "no Errors survive"
              Expect.isGreaterThan result.Suppressed 0 "the run reports what it suppressed"
          }

          test "a disable pragma naming a different code suppresses nothing" {
              let result =
                  run
                      { setup [ "DuplicateInDashboard" ] with
                          Prefix = "// fuaran-validator: disable FUARAN057\n" }

              Expect.isTrue (hasCode "FUARAN001" result.Findings) "untouched"
              Expect.equal result.Suppressed 0 "nothing suppressed"
          }

          test "disable-next-line suppresses only the line that follows it" {
              let dir, projectPath, _ = materialise { setup [] with Manifest = None }

              writeFile
                  dir
                  "NextLine.fs"
                  """module Sample.NextLine

open Fuaran.UI

let build () : Node<unit> =
    Fuaran.dashboard
        "nl-dashboard"
        { Defaults.dashboard with
            Children =
                [
                  // fuaran-validator: disable-next-line FUARAN001
                  Fuaran.metric "shared-id" Defaults.metric
                  Fuaran.metric "shared-id" Defaults.metric ] }
"""
              |> ignore

              let result =
                  Validator.run
                      { ProjectPath = projectPath
                        ModulePattern = None
                        ManifestPath = None
                        Orchestrated = false }
                  |> Async.RunSynchronously

              Expect.equal (codes "FUARAN001" result.Findings).Length 1 "only the un-pragma'd site"
              Expect.equal result.Suppressed 1 "exactly one suppressed"
          }

          test "a bare disable with no code suppresses nothing" {
              let result =
                  run
                      { setup [ "DuplicateInDashboard" ] with
                          Prefix = "// fuaran-validator: disable\n" }

              Expect.isTrue (hasCode "FUARAN001" result.Findings) "not a blanket disable"
              Expect.equal result.Suppressed 0 "nothing suppressed"
          } ]

// ─── The derived constructor surface ────────────────────────────────────────

let private surface =
    testList
        "derived constructor surface"
        [ test "the smart-constructor set is the Fuaran module's Node-returning functions" {
              let names = AstWalker.SmartCtors.names

              for ctor in
                  [ "box"
                    "stack"
                    "dashboard"
                    "metric"
                    "button"
                    "grid"
                    "custom"
                    "fragmentRef" ] do
                  Expect.contains names ctor (sprintf "%s is recognised" ctor)

              // Values of the module that are not constructors are not in it.
              Expect.isFalse (names.Contains "buildNode") "private helpers are not constructors"
          }

          test "the tree-root set is the constructors that take a child node" {
              let containers = AstWalker.SmartCtors.containers

              for ctor in [ "box"; "stack"; "dashboard"; "card"; "tabs"; "errorBoundary" ] do
                  Expect.contains containers ctor (sprintf "%s holds children" ctor)

              for leaf in [ "metric"; "button"; "markdown"; "link"; "grid" ] do
                  Expect.isFalse (containers.Contains leaf) (sprintf "%s is a leaf" leaf)
          } ]

[<Tests>]
let tests =
    testList "Fuaran.UI.Validator end-to-end" [ census; retired; parseOnce; behaviour; surface ]
