module Fuaran.Samples.Resume.Main

// The RESUME entry (output/Main.js). At load it installs ONE document-root
// listener and executes nothing else — no tree reconstruction, no React, no
// Render. Deliberately references only `BrowserRuntime` + `Resume`; it never
// imports `Render`/Feliz, so Vite tree-shakes the framework out of this entry's
// load path (the measurable core of Phase 177).
//
// `interpret`-disposition nodes (the four Navigate buttons) run directly against
// the runtime on first click — no chunk. The one `boot`-disposition node lazily
// `import()`s `output/Boot.js` (React + Feliz) and records the first-interaction
// latency.

open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Fuaran.UI.Renderer

// Stamp entry-evaluation start so the harness can measure entry-eval cost.
window?__fuaranResumeEntryStart <- (emitJsExpr () "performance.now()": float)

// A sample-local minimal `IFuaranRuntime` — deliberately NOT `BrowserRuntime`,
// which transitively imports `StateStore` (→ react) and the Custom registry
// (→ Feliz `Html`). Returning `None`/`true` for the Custom/observer members
// keeps Fable from pulling any framework code into THIS entry — the property
// the harness measures. The interpret path only ever calls Navigate/Warn here.
let private runtime: Runtime.IFuaranRuntime =
    { new Runtime.IFuaranRuntime with
        member _.Call(_, _) =
            console.warn "[resume] Call reached the minimal runtime (no HTTP substrate)"

        member _.Notify(channel, _) =
            console.log (sprintf "[resume] notify → %s" channel)

        member _.Navigate(route) = window.location.hash <- route

        member _.SetState(key, _) =
            console.log (sprintf "[resume] setState → %s" key)

        member _.InvokeAiTool(name, _) =
            console.log (sprintf "[resume] aiTool → %s" name)

        member _.WriteToClipboard(_) = console.log "[resume] writeToClipboard"

        member _.ReadFileBody(_, _, _) =
            console.warn "[resume] ReadFileBody reached the minimal runtime"

        member _.Warn(message) = console.warn message
        member _.LayoutObserver = None
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.CanDispatch(_) = true }

let private bootSubtree (nodeId: string) : unit =
    // Lazy-load the interactive chunk on first interaction and time the round
    // trip. Kept in JS so the F# side stays free of Promise plumbing.
    emitJsStatement
        nodeId
        """
        var t0 = performance.now();
        import('/output/Boot.js').then(function () {
          window.__fuaranBoot('bootcard');
          window.__fuaranBootLatency = performance.now() - t0;
          console.log('[resume] booted ' + $0 + ' in ' + window.__fuaranBootLatency.toFixed(1) + ' ms (lazy chunk + mount)');
        });
        """

let private config: Resume.ResumeConfig =
    { Runtime = runtime
      BootSubtree = bootSubtree
      HydrateSubtree = fun nodeId -> console.warn (sprintf "[resume] hydrate fallback requested for %s" nodeId)
      OnMismatch = fun () -> console.error "[resume] tree-hash mismatch — client render fallback" }

let private installed = Resume.install "rroot" config

console.log (sprintf "[resume] installed=%b — 0 framework JS executed at load" installed)
window?__fuaranResumeInstalled <- installed
window?__fuaranResumeEntryEnd <- (emitJsExpr () "performance.now()": float)
