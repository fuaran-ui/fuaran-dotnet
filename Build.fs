module Fuaran.Build

open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO

let private repoRoot = __SOURCE_DIRECTORY__
let private solution = Path.Combine(repoRoot, "Fuaran.sln")

let private testProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Tests", "Fuaran.UI.Tests.fsproj")

let private opsTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Ops.Tests", "Fuaran.UI.Ops.Tests.fsproj")

let private aiToolsTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.AiTools.Tests", "Fuaran.UI.AiTools.Tests.fsproj")

let private validatorProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Validator", "Fuaran.UI.Validator.fsproj")

let private validatorTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Validator.Tests", "Fuaran.UI.Validator.Tests.fsproj")

let private opStreamTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.OpStream.Tests", "Fuaran.UI.OpStream.Tests.fsproj")

// Phase 178 — branching op-stream DAG (opt-in, rung-4). Its own test project so
// the DAG suite runs alongside the linear op-stream suite without coupling the
// linear consumers to the DAG packages.
let private dagOpStreamTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.OpStream.Dag.Tests", "Fuaran.UI.OpStream.Dag.Tests.fsproj")

// Phase 186 — DAG-aware op-stream inspector substrate suite (render model +
// audition + overlay + arbitrary-coordinate diff).
let private dagInspectTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.OpStream.Dag.Inspect.Tests", "Fuaran.UI.OpStream.Dag.Inspect.Tests.fsproj")

let private layoutObserverTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.LayoutObserver.Tests", "Fuaran.UI.LayoutObserver.Tests.fsproj")

let private styleObserverTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.StyleObserver.Tests", "Fuaran.UI.StyleObserver.Tests.fsproj")

let private themeManifestTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.ThemeManifest.Tests", "Fuaran.UI.ThemeManifest.Tests.fsproj")

let private telemetryTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Telemetry.Tests", "Fuaran.UI.Telemetry.Tests.fsproj")

let private fastPathTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.FastPath.Tests", "Fuaran.UI.FastPath.Tests.fsproj")

let private jsonDecodeTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.JsonDecode.Tests", "Fuaran.UI.JsonDecode.Tests.fsproj")

let private serverRenderTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer.Server.Tests", "Fuaran.UI.Renderer.Server.Tests.fsproj")

let private serverDrivenTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.ServerDriven.Tests", "Fuaran.UI.ServerDriven.Tests.fsproj")

let private serverDrivenAspNetCoreTestProject =
    Path.Combine(
        repoRoot,
        "src",
        "Fuaran.UI.ServerDriven.AspNetCore.Tests",
        "Fuaran.UI.ServerDriven.AspNetCore.Tests.fsproj"
    )

let private serverDrivenWebSocketTestProject =
    Path.Combine(
        repoRoot,
        "src",
        "Fuaran.UI.ServerDriven.WebSocket.Tests",
        "Fuaran.UI.ServerDriven.WebSocket.Tests.fsproj"
    )

let private giraffeTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Giraffe.Tests", "Fuaran.UI.Giraffe.Tests.fsproj")

// Host-neutral validated-exemplar seam (decodeExemplar + 3-gate integrity).
// Corpus-independent — builds a node, encodes, and checks the round-trip
// fixed point; runs unconditionally (single-repo checkout included).
let private contentTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Content.Tests", "Fuaran.UI.Content.Tests.fsproj")

// Page-set layer (SitePage + SiteCheck + Nav projection + RenderPlan + static-
// export planning). Pure + corpus-independent — runs unconditionally.
let private siteTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Site.Tests", "Fuaran.UI.Site.Tests.fsproj")

// Phase 205 — structure-only clean room (content-free skeleton projection +
// structural-op gate + audit). Pure + Fable-clean; no workspace corpus needed.
let private cleanRoomTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Ops.CleanRoom.Tests", "Fuaran.UI.Ops.CleanRoom.Tests.fsproj")

// Phase 169 — catalog wire-JSON round-trip guard. Compile-links the catalog's
// Matrix.fs and asserts every entry's canonical JSON decodes back through the
// canonical decoder, so the "the JSON shown next to the render is real" promise
// is build-time-checked, not hand-maintained. No corpus dependency — runs
// unconditionally (single-repo checkout included).
let private catalogTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Catalog.Tests", "Fuaran.UI.Catalog.Tests.fsproj")

// Phase 172 — C# fluent-builder authoring-shape PoC (§4e evidence). A console
// harness that proves C#-authored Fuaran trees encode byte-identically to the
// corpus. PoC posture: under samples/, deletable without touching any shipped
// suite; runs in the Test target as a wire-identity gate (exit non-zero on
// divergence).
let private csharpAuthoringPocProject =
    Path.Combine(repoRoot, "samples", "csharp-authoring-poc", "Fuaran.UI.CSharp.Poc.csproj")

// Phase 306 — the C# authoring veneer's corpus-conformance suite (the supportable
// promotion of the PoC's byte-compare harness). Console-Exe, gated on the
// workspace corpus like the JsonDecode + PoC suites.
let private csharpConformanceTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.CSharp.Conformance.Tests", "Fuaran.UI.CSharp.Conformance.Tests.csproj")

// Phase 314 — the Roslyn analyzer's test suite (drives the analyzer over source
// snippets). Corpus-independent, so it runs unconditionally.
let private analyzerTestProject =
    Path.Combine(repoRoot, "src", "Fuaran.UI.Analyzers.Tests", "Fuaran.UI.Analyzers.Tests.csproj")

// Phase 315 — the VB XML-literal analyzer's test suite. Corpus-independent.
let private vbAnalyzerTestProject =
    Path.Combine(
        repoRoot,
        "src",
        "Fuaran.UI.Analyzers.VisualBasic.Tests",
        "Fuaran.UI.Analyzers.VisualBasic.Tests.csproj"
    )

// Phase 312 — the VB XML-literal veneer's corpus-conformance suite (a VB console
// runner, since XML literals are a VB language feature). Corpus-gated.
let private vbConformanceTestProject =
    Path.Combine(
        repoRoot,
        "src",
        "Fuaran.UI.VisualBasic.Conformance.Tests",
        "Fuaran.UI.VisualBasic.Conformance.Tests.vbproj"
    )

// Phase 169 — the public component-reference catalog. Its static site is a
// Fable transpile of these sources followed by a Vite bundle; the Vite half
// lives in the catalog publish workflow, this `catalogDir` is the Fable half.
let private catalogDir = Path.Combine(repoRoot, "samples", "catalog")

let private packableProjects =
    [ "Fuaran.UI"
      "Fuaran.UI.Renderer.Core"
      // Phase 526 — Chart → Drawing lowering. Renderer + Renderer.Server project-reference
      // it, so their nupkgs declare a dependency on it; it must pack alongside them.
      "Fuaran.UI.Charts"
      "Fuaran.UI.Renderer"
      "Fuaran.UI.Renderer.Server"
      "Fuaran.UI.Ops.Abstractions"
      "Fuaran.UI.Ops"
      "Fuaran.UI.AiTools"
      // Phase 512 — the public signature-searchable pattern bank (a Fable-clean
      // façade over Fuaran.Core.FunctionRegistry + a domain-neutral seed catalogue).
      "Fuaran.UI.FastPath"
      "Fuaran.UI.Validator"
      "Fuaran.UI.OpStream.Abstractions"
      "Fuaran.UI.OpStream.InMemory"
      "Fuaran.UI.OpStream.Sqlite"
      "Fuaran.UI.OpStream.Replay"
      // Phase 178 — opt-in (rung-4) branching DAG packages. Reference the
      // linear abstractions; nothing in the light path references them back.
      "Fuaran.UI.OpStream.Dag.Abstractions"
      "Fuaran.UI.OpStream.Dag.InMemory"
      "Fuaran.UI.OpStream.Dag.Sqlite"
      "Fuaran.UI.OpStream.Dag.Merge"
      // Phase 186 — DAG-aware op-stream inspector substrate (render model +
      // audition + primacy/retention overlay + arbitrary-coordinate diff).
      // Derived read-only view; rung-4 (requires the DAG packages).
      "Fuaran.UI.OpStream.Dag.Inspect"
      "Fuaran.UI.LayoutObserver.Abstractions"
      "Fuaran.UI.LayoutObserver"
      "Fuaran.UI.Telemetry.Abstractions"
      "Fuaran.UI.Telemetry.Default"
      "Fuaran.UI.Telemetry.Drift"
      // Phase 183 — incremental re-derivation engine (effect-aware memoisation
      // over FragmentApply.apply). Consumes Fuaran.UI + Renderer + OpStream +
      // Telemetry abstractions.
      "Fuaran.UI.Memo"
      "Fuaran.UI.StyleObserver.Abstractions"
      "Fuaran.UI.StyleObserver"
      "Fuaran.UI.ThemeManifest"
      "Fuaran.UI.ServerDriven"
      "Fuaran.UI.ServerDriven.AspNetCore"
      "Fuaran.UI.ServerDriven.WebSocket"
      "Fuaran.UI.Giraffe"
      // Phase 205 — structure-only clean room: content-free skeleton projection
      // + substitutable structural-op gate + audit. Additive + Fable-clean.
      "Fuaran.UI.Ops.CleanRoom"
      // Host-neutral validated-exemplar seam (decode + pre-emit-validate +
      // canonical round-trip). Graduated out of the fuaran-ui.io docs site; no
      // Renderer / Giraffe / Markdig dependency.
      "Fuaran.UI.Content"
      // The page-set layer for pure-SSR sites (page model + frontmatter +
      // route derivation + SiteCheck gate + RenderPlan + auto-nav + static
      // export) and its Giraffe host adapter — Giraffe isolated to the
      // adapter, matching the Fuaran.UI.Giraffe precedent.
      "Fuaran.UI.Site"
      "Fuaran.UI.Site.Giraffe" ]
    |> List.map (fun name -> Path.Combine(repoRoot, "src", name, $"{name}.fsproj"))
    // Phase 304 — the C# authoring veneer packs alongside the F# tier. It is a
    // .csproj (appended after the .fsproj map). Phase 314 appends the Roslyn
    // analyzer (also a .csproj, packed as a NuGet analyzer).
    |> fun fsprojs ->
        fsprojs
        @ [ Path.Combine(repoRoot, "src", "Fuaran.UI.CSharp", "Fuaran.UI.CSharp.csproj")
            Path.Combine(repoRoot, "src", "Fuaran.UI.Analyzers", "Fuaran.UI.Analyzers.csproj")
            // Phase 310 — the VB XML-literal veneer (a .vbproj).
            Path.Combine(repoRoot, "src", "Fuaran.UI.VisualBasic", "Fuaran.UI.VisualBasic.vbproj") ]

let private dotnet args workingDir =
    CreateProcess.fromRawCommand "dotnet" args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

let private init (args: string array) =
    args
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "Build.fs"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

let private registerTargets () =
    Target.create "Format" (fun _ -> dotnet [ "fantomas"; "." ] repoRoot)

    Target.create "Build" (fun _ -> dotnet [ "build"; solution; "-c"; "Release" ] repoRoot)

    Target.create "Test" (fun _ ->
        dotnet [ "run"; "--project"; testProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; opsTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; aiToolsTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; fastPathTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; validatorTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; opStreamTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; dagOpStreamTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; dagInspectTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; layoutObserverTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; styleObserverTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; themeManifestTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; telemetryTestProject; "-c"; "Release" ] repoRoot

        // The JsonDecode conformance suite loads the wire-format-fixtures corpus
        // from the workspace root (a sibling of this repo). In a single-repo CI
        // checkout (e.g. the publish-packages workflow) that corpus is absent and
        // the suite crashes at startup. Run it only when the corpus is present —
        // full conformance locally / in a workspace checkout, gracefully skipped
        // in a bare single-repo checkout. The corpus-present path keeps the
        // suite's own fail-loud-if-absent contract intact.
        let corpusManifest =
            Path.Combine(repoRoot, "..", "wire-format-fixtures", "manifest.json")

        if File.Exists corpusManifest then
            dotnet [ "run"; "--project"; jsonDecodeTestProject; "-c"; "Release" ] repoRoot
        else
            Trace.traceImportant
                "SKIPPING Fuaran.UI.JsonDecode.Tests — wire-format-fixtures corpus absent (single-repo checkout; conformance runs where the workspace corpus is present)."

        dotnet [ "run"; "--project"; serverRenderTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; serverDrivenTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; serverDrivenAspNetCoreTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; serverDrivenWebSocketTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; giraffeTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; contentTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; siteTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; cleanRoomTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; catalogTestProject; "-c"; "Release" ] repoRoot

        // The C# authoring PoC emits canonical nodes against the workspace
        // wire-format-fixtures corpus, so it too needs the workspace checkout —
        // skip it in a single-repo checkout for the same reason as the
        // JsonDecode suite above.
        if File.Exists corpusManifest then
            dotnet [ "run"; "--project"; csharpAuthoringPocProject; "-c"; "Release" ] repoRoot
        else
            Trace.traceImportant
                "SKIPPING Fuaran.UI.CSharp.Poc — wire-format-fixtures corpus absent (single-repo checkout)."

        // The C# authoring veneer's corpus-conformance suite (Phase 306) — same
        // workspace-corpus gate as the PoC + JsonDecode suites.
        if File.Exists corpusManifest then
            dotnet [ "run"; "--project"; csharpConformanceTestProject; "-c"; "Release" ] repoRoot
        else
            Trace.traceImportant
                "SKIPPING Fuaran.UI.CSharp.Conformance.Tests — wire-format-fixtures corpus absent (single-repo checkout)."

        // The Roslyn analyzer's tests are corpus-independent — always run.
        dotnet [ "run"; "--project"; analyzerTestProject; "-c"; "Release" ] repoRoot
        dotnet [ "run"; "--project"; vbAnalyzerTestProject; "-c"; "Release" ] repoRoot

        // The VB XML-literal veneer's corpus-conformance suite — same corpus gate.
        if File.Exists corpusManifest then
            dotnet [ "run"; "--project"; vbConformanceTestProject; "-c"; "Release" ] repoRoot
        else
            Trace.traceImportant
                "SKIPPING Fuaran.UI.VisualBasic.Conformance.Tests — wire-format-fixtures corpus absent (single-repo checkout).")

    Target.create "Validate" (fun _ ->
        let srcDir = Path.Combine(repoRoot, "src")

        let candidateProjects =
            System.IO.Directory.EnumerateFiles(srcDir, "*.fsproj", System.IO.SearchOption.AllDirectories)
            |> Seq.filter (fun p ->
                not (
                    p.EndsWith("Fuaran.UI.Validator.fsproj")
                    || p.EndsWith("Fuaran.UI.Validator.Tests.fsproj")
                ))
            |> Seq.toList

        for project in candidateProjects do
            printfn "Fuaran.UI.Validator: %s" project
            dotnet [ "run"; "--project"; validatorProject; "-c"; "Release"; "--"; project ] repoRoot)

    Target.create "Pack" (fun _ ->
        // Default: the workspace-shared inner-loop feed, so a local `-t Pack`
        // keeps shadowing released packages for downstream consumers exactly as
        // before.
        //
        // `FUARAN_PACK_OUTPUT` overrides it, and the publish workflow sets it to
        // a repo-local dir. That is load-bearing rather than tidy: the push step
        // used to glob the SHARED feed, so its input was "whatever .nupkg files
        // happen to sit in that folder" — safe on a runner only because the
        // folder is minted empty there, and on a developer machine that glob
        // reaches ~1900 packs, nearly all of them private. Packing to a
        // repo-local dir makes the push step's input this repo's own output by
        // construction.
        let feed =
            match System.Environment.GetEnvironmentVariable "FUARAN_PACK_OUTPUT" with
            | null
            | "" -> Path.Combine(repoRoot, "..", "..", "..", "local-nuget-feed")
            | dir -> dir

        for project in packableProjects do
            dotnet [ "pack"; project; "-c"; "Release"; "-o"; feed ] repoRoot)

    // SSR client/server class+ARIA parity gate (Phase 142). Runs the server
    // renderer's parity corpus — the executable lock keeping the Feliz client
    // renderer and the Feliz.ViewEngine server renderer on the same class+ARIA
    // contract. CI runs this target alongside Test.
    Target.create "SsrParity" (fun _ ->
        dotnet [ "run"; "--project"; serverRenderTestProject; "-c"; "Release" ] repoRoot)

    // DomPatch lowering conformance gate (Phase 158 QW6). Runs the server-driven
    // test project, whose golden TreeOp→DomPatch corpus locks the lowering — the
    // cheap CI gate against silent patch-lowering drift (the DomPatch analogue of
    // SsrParity). CI runs this target alongside Test.
    Target.create "DomPatchCorpus" (fun _ ->
        dotnet [ "run"; "--project"; serverDrivenTestProject; "-c"; "Release" ] repoRoot)

    // Phase 169 — catalog static-build Fable gate. `dotnet fable` transpiles the
    // public component-reference catalog so a "builds clean on .NET but breaks
    // under Fable" regression (a server-only API leaking into a client file, an
    // F# 10 nullable cascade through a pre-nullable Fable lib) fails the
    // pipeline rather than only a manual browser session. Dotnet-pure — the
    // fable tool is restored by `dotnet tool restore`; no Node/Vite here (the
    // catalog publish workflow bundles this output into the static site). Run
    // standalone (`-- Catalog`) like SsrParity / DomPatchCorpus; not folded into
    // All/Check so the inner loop stays lean.
    Target.create "Catalog" (fun _ -> dotnet [ "fable"; "-o"; "output"; "--noCache" ] catalogDir)

    Target.create "All" ignore

    Target.create "Check" ignore

    "Format" ==> "Build" ==> "Test" ==> "All" |> ignore
    "Build" ==> "Pack" |> ignore
    "Build" ==> "Validate" |> ignore
    "Build" ==> "SsrParity" |> ignore
    "Build" ==> "DomPatchCorpus" |> ignore
    "Build" ==> "Catalog" |> ignore
    "Test" ==> "Validate" ==> "Check" |> ignore

    // Phase 110 — AI-authoring pack drift check. Runs docs/tools/authoring-pack.fsx in
    // --check mode: fails the build if any corpus-derived wire example in the authoring
    // guide or prompt pack diverged from the wire-format-fixtures corpus. No Build dep —
    // it is a pure fsi pass over docs/ + wire-format-fixtures/.
    Target.create "AuthoringPack" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check" ]
            repoRoot)

    "AuthoringPack" ==> "Check" |> ignore

    // Phase 840 — the lenient-dialect pack variant's drift check. Unlike AuthoringPack
    // it NEEDS the build: every dialect example block is proved loss-free by running
    // the real decoder over (canonical, dialect) pairs (docs/tools/dialect-verify.fsx
    // consumes the Release outputs of Fuaran.UI.JsonDecode.Tests), so the target
    // depends on Build rather than being a pure fsi pass.
    Target.create "AuthoringPackDialect" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check"
              "--dialect"
              "lenient" ]
            repoRoot)

    "Build" ==> "AuthoringPackDialect" ==> "Check" |> ignore

    // Phase 843 — the per-family compiled pack variants' drift check. A pure fsi pass
    // like AuthoringPack: each variant is compiled from committed inputs only (the
    // section-demand index, the flip record's per-family dialect verdicts, and the
    // canonical or lenient pack it selects), so nothing here needs the decoder. It IS
    // in the gate rather than left to the operator: a pack regen that did not
    // recompile the variants would leave every compiled artefact silently describing
    // a pack that no longer exists, and a variant's whole value is being attributable.
    Target.create "AuthoringPackFamilies" (fun _ ->
        dotnet
            [ "fsi"
              Path.Combine(repoRoot, "docs", "tools", "authoring-pack.fsx")
              "--check"
              "--family"
              "all" ]
            repoRoot)

    "AuthoringPackFamilies" ==> "Check" |> ignore

[<EntryPoint>]
let main args =
    init args
    registerTargets ()

    // FAKE's CLI selects a target only via `-t <name>` / `--target <name>`; a bare
    // positional (`dotnet run -- Validate`) falls through to <targetargs>, so the
    // default target ran regardless of the argument. Dispatch the documented
    // `-- <Target>` form by hand; flag-shaped args (`-t Pack`, `--list`) still go
    // through FAKE's own parser.
    let target =
        match args |> Array.tryHead with
        | Some t when not (t.StartsWith "-") -> t
        | _ -> "All"

    Target.runOrDefaultWithArguments target
    0
