module Fuaran.UI.Renderer.Diagnostics

// ============================================================================
//  The renderer's last-resort diagnostic line.
//
//  Several places in this tier ISOLATE a failure on purpose, and every one of
//  those decisions is right: one island that will not hydrate must not stop the
//  others, one throwing change-hub listener must not stop the notification, one
//  badly-behaved affordance provider must not bring down a read of what a page
//  declares, and a telemetry sink that throws must not take the render with it.
//  Isolation is the whole design.
//
//  What was wrong is that the isolation was SILENT. `with _ -> ()` discards the
//  exception, so nothing anywhere ever mentions it — and each of these failures
//  is indistinguishable, from the outside, from the thing having worked: an
//  island that stayed static looks like an island with no interactivity, a
//  listener that stopped hearing looks like a tree that stopped changing, a
//  provider that threw looks like a module that declares nothing, and a
//  telemetry sink that threw looks like a render that never failed. Two of them
//  are diagnostics machinery, so the failure hides the very signal someone was
//  looking for.
//
//  ── Why the console and not the host's `Warn` seam ─────────────────────────
//  `IFuaranRuntime.Warn` is the right channel where a runtime is in scope, and
//  the render path uses it. It is NOT in scope at any of these sites: island
//  hydration, the change hub, the KaTeX upgrade and the affordance registry are
//  page-global and are reached without a runtime, by design — they are
//  enhancements over DOM the renderer already emitted. Threading a runtime
//  through them to carry a diagnostic would be a new obligation on every host
//  for a case none of them configure, and would make the enhancements
//  runtime-dependent for no other reason.
//
//  So this is deliberately the FLOOR: one line, on the channel a browser
//  developer already has open, naming what was isolated and carrying the
//  exception object so the browser renders its own stack rather than a
//  stringified copy. A host that wants these on its own channel wraps the
//  console; nothing here prevents that, and nothing here promises it.
//
//  ── Not a failure surface ─────────────────────────────────────────────────
//  This never throws and never returns a value. A diagnostic that can fail
//  would need isolating in turn, and the site it is reporting from is already
//  inside a `with`.
// ============================================================================

#nowarn "3261"

#if FABLE_COMPILER
open Fable.Core

[<Emit("console.warn($0, $1)")>]
let private consoleWarn (message: string) (detail: obj) : unit = jsNative
#endif

/// Report an isolated failure. `context` names WHAT was isolated, in the
/// present tense and specifically enough to act on ("island hydration failed
/// for 'summary'"); `detail` is the exception, passed as an object.
let warn (context: string) (detail: obj) : unit =
#if FABLE_COMPILER
    try
        consoleWarn ("[fuaran] " + context) detail
    with _ ->
        // A console that is not there, or a page that has replaced it with
        // something that throws. There is nowhere left to report to.
        ()
#else
    // The .NET leg exists so this compiles in the tests' build of the renderer
    // and so a server-side consumer of a shared source file is not silently
    // different. `stderr` is the same choice `Runtime`'s default diagnostic
    // sink makes.
    eprintfn "[Fuaran] %s: %A" context detail
#endif
