module Fuaran.UI.Validator.Validator

// ============================================================================
//  Validator orchestration.
//
//  Loads the manifest, walks every F# source under the target project, runs
//  each check, and returns the merged finding list.
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis
open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Manifest

/// Configuration for one validator run.
type RunOptions =
    {
        /// Path to a .fsproj.
        ProjectPath: string
        /// Optional substring filter on F# source file paths — only files
        /// containing this substring participate. `None` = all files.
        ModulePattern: string option
        /// Explicit manifest path. `None` triggers convention-based discovery
        /// (sibling `fuaran-validator.manifest.json` of the .fsproj).
        ManifestPath: string option
        /// Orchestrated / AI-emitted context. Escalates the wire-survivability
        /// advisories (FUARAN084) from Warning to Error — an AI author must stay
        /// on the wire-survivable substrate. Default `false` (hand-authored).
        Orchestrated: bool
    }

type RunResult =
    { Findings: Finding list
      ManifestPath: string option
      ManifestLoaded: bool
      FilesWalked: int }

let private projectDirectory (projectPath: string) =
    let full = Path.GetFullPath projectPath

    match Path.GetDirectoryName full with
    | null -> full
    | dir -> dir

let private resolveManifest (options: RunOptions) =
    match options.ManifestPath with
    | Some explicit when File.Exists explicit -> Some explicit
    | Some _ -> None
    | None -> options.ProjectPath |> projectDirectory |> discover

/// Run all checks; return a `RunResult` so callers can decide formatting +
/// exit-code policy. Does not call `Environment.Exit`.
let run (options: RunOptions) : Async<RunResult> =
    async {
        let projectDir = projectDirectory options.ProjectPath
        let manifestPath = resolveManifest options

        let manifest =
            match manifestPath with
            | Some path -> load path
            | None -> empty

        let sourceFiles =
            projectDir
            |> discoverSourceFiles
            |> List.filter (fun file ->
                match options.ModulePattern with
                | Some pattern -> file.Contains pattern
                | None -> true)

        let checker = FSharpChecker.Create()

        let! allCalls = sourceFiles |> List.map (fun file -> walkFile checker file) |> Async.Parallel

        let calls = allCalls |> Array.toList |> List.concat

        // Local-binding rules walk their own narrow AST pattern
        // (`binding.local` invocations + `Action.CommitLocal` references)
        // rather than reuse the Fuaran.X smart-ctor walker. The walker is
        // cheap (single pass per file) and isolates the rule's lexical
        // pattern from the existing Fuaran.X tree-walker scope.
        let! localBindingFindings = LocalBindingCheck.checkSources checker sourceFiles

        // FUARAN051 (Number-field static range check). Walks
        // its own narrow `FormFieldKind.rangedNumber` AST pattern, like
        // the LocalBindingCheck — kept separate from the main
        // Fuaran.X smart-ctor walker so the rule's lexical scope stays
        // independent.
        let! numberFieldRangeFindings = NumberFieldRangeCheck.checkSources checker sourceFiles

        // FUARAN045 (SegmentedChoice option-count advisory).
        // Walks its own `FormFieldKind.segmentedChoice` AST pattern;
        // surfaces a Warning when the static options list exceeds 7
        // entries (segmented controls work best with ≤5 visible options;
        // >7 should reach for `FormFieldKind.Choice` dropdown instead).
        let! segmentedChoiceFindings = SegmentedChoiceCheck.checkSources checker sourceFiles

        // FUARAN060 (ExtraAttributes sanitization). Walks
        // `Node.withExtraAttribute "key" "value"` call shapes and warns
        // when the literal key violates the data-* / aria-* allowlist or
        // names a known dangerous attribute (on* / style). The renderer-
        // time Sanitize.sanitizeExtraAttributes filter is the floor; this
        // is the build-time signal.
        let! extraAttributesFindings = ExtraAttributesCheck.checkSources checker sourceFiles

        // FUARAN053 / FUARAN054 / FUARAN055 (Custom bounded-
        // escape health). Walks `Fuaran.custom` / `NodeKind.Custom(...)`
        // construction sites + `RegisterCustomRenderer(...)` call sites,
        // surfaces the missing-contentHash / unverifiable-exposedNodeIds /
        // project-Custom-ratio advisories. The cultural-posture mitigation
        // against Custom-creep.
        let! customHealthFindings = CustomHealthCheck.checkSources checker manifest sourceFiles

        // FUARAN062 (Custom build-time content-hash). Walks
        // `Fuaran.custom` / `NodeKind.Custom(...)` sites that carry a literal
        // contentHash, computes a deterministic SHA-256 over the body's
        // declared shape (props schema + exposedNodeIds + moduleId/componentId),
        // and surfaces drift between the hand-set hash and the computed one.
        // Error when Strictness = Enforced; Warning otherwise. Makes the
        // bounded-escape integrity check mechanical rather than advisory.
        let! customContentHashFindings = CustomContentHashCheck.checkSources checker sourceFiles

        // FUARAN056 / FUARAN057 / FUARAN058 (Fragment-reuse
        // health). Walks `Fuaran.fragmentDecl` / `Fuaran.fragmentRef`
        // construction sites; surfaces duplicate decl names, unresolved
        // refs, and reference cycles at build time so the runtime
        // placeholders never ship to production.
        let! fragmentFindings = FragmentCheck.checkSources checker sourceFiles

        // FUARAN046 (Grid template-columns advisory). Walks
        // `Fuaran.gridLayoutTemplated` call sites and surfaces a Warning
        // when the verbatim `templateColumns` string is equivalent to the
        // typed `Cols: int` shape (`repeat(N, 1fr)`). The author is paying
        // the unbounded-string escape's review tax for zero expressivity
        // gain — should reach for `Fuaran.gridLayout` instead.
        let! gridTemplateColumnsFindings = GridTemplateColumnsCheck.checkSources checker sourceFiles

        // FUARAN061 (Format currency-coherence). Walks
        // `Format.Currency` / `localeFormat.currency` construction sites and
        // errors when the ISO-4217 code is a blank string literal (Phase 102).
        let! formatCoherenceFindings = FormatCoherenceCheck.checkSources checker sourceFiles

        // FUARAN084 (wire-survivability). Walks for `Binding.Computed`
        // — the host-only closure escape that erases to "<closure>" on the
        // wire. Advisory (Warning) for hand-authored source; Error when the
        // run is flagged orchestrated (an AI author must stay wire-survivable).
        let! wireSurvivabilityFindings = WireSurvivabilityCheck.checkSources options.Orchestrated checker sourceFiles

        let findings =
            [ yield! NodeIdCheck.check calls
              yield! BindingResolution.check manifest calls
              yield! MsgPayloadCheck.check manifest calls
              yield! RowTypeCheck.check manifest calls
              yield! AccessibilityCheck.check calls
              yield! ButtonDisabledCheck.check calls
              yield! ScalarRangeCheck.check calls
              yield! LinkCheck.check calls
              yield! TabsCheck.check calls
              yield! localBindingFindings
              yield! numberFieldRangeFindings
              yield! segmentedChoiceFindings
              yield! extraAttributesFindings
              yield! customHealthFindings
              yield! customContentHashFindings
              yield! fragmentFindings
              yield! gridTemplateColumnsFindings
              yield! formatCoherenceFindings
              yield! wireSurvivabilityFindings ]

        let preamble =
            match manifestPath with
            | None ->
                [ { Severity = Warning
                    Code = "FUARAN900"
                    Location =
                      { File = options.ProjectPath
                        Line = 1
                        Column = 1 }
                    Message =
                      "No fuaran-validator.manifest.json found next to the .fsproj — schema-coupled checks (Binding.Query name resolution, Action.Dispatch case names, RowType match) are silenced. See Fuaran/docs/VALIDATOR-MANIFEST.md."
                    AvailableFields = None
                    Suggestion = None } ]
            | Some _ -> []

        return
            { Findings = preamble @ findings
              ManifestPath = manifestPath
              ManifestLoaded = manifestPath.IsSome
              FilesWalked = sourceFiles.Length }
    }
