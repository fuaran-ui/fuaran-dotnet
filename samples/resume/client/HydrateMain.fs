module Fuaran.Samples.Resume.HydrateMain

// The HYDRATE baseline entry (output/HydrateMain.js) — the Phase 143 path the
// resume entry is measured against. It reconstructs the SAME tree in F# and
// attaches React with `hydrateRoot` at load (pulling Render + Feliz + React into
// the load path), then runs a tiny Elmish loop so the boot button increments.

open Browser.Dom
open Fable.Core.JsInterop
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

window?__fuaranHydrateEntryStart <- (Fable.Core.JsInterop.emitJsExpr () "performance.now()": float)

type private Msg = Boot

let mutable private count = 0
let mutable private root: Hydration.HydrationRoot option = None

let rec private dispatch (msg: Msg) : unit =
    (match msg with
     | Boot -> count <- count + 1)

    match root with
    | Some r -> Hydration.render r (view ())
    | None -> ()

and private view () =
    Render.renderWithSources BindingResolver.empty dispatch (Tree.build<Msg> count (Action.dispatch Boot))

match Hydration.hydrateById "hydration-root" (view ()) with
| Some r ->
    root <- Some r
    console.log "[hydrate] hydrated #hydration-root with hydrateRoot (full tree at load)"
| None -> console.error "[hydrate] container #hydration-root not found"

window?__fuaranHydrateEntryEnd <- (Fable.Core.JsInterop.emitJsExpr () "performance.now()": float)
