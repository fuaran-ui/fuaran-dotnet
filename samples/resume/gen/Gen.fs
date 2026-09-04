module Fuaran.Samples.Resume.Gen

// Server-render the SAME tree two ways so the harness can compare load
// strategies apples-to-apples (Phase 177 §7):
//   resume.html   — `Resume.renderResumable`  → inert HTML + flat Action envelope
//   hydrate.html  — `Hydration.renderHydratable` → inert HTML + full wire tree
// Both are plain .NET renders (no React, no Fable); the difference is purely
// what the client must execute at load.

open System.IO
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

[<EntryPoint>]
let main _ =
    let here = __SOURCE_DIRECTORY__
    let sampleDir = Path.GetFullPath(Path.Combine(here, ".."))

    let render (templateName: string) (outName: string) (body: string) =
        let template = File.ReadAllText(Path.Combine(sampleDir, templateName))
        let html = template.Replace("<!--SSR-->", body)
        File.WriteAllText(Path.Combine(sampleDir, outName), html)
        body.Length

    // ── Resume page ──────────────────────────────────────────────────────────
    // The Dispatch handler is opaque on the wire (boot disposition); the model +
    // one classified live-subscription init effect ride along in the envelope.
    let resumeTree = Tree.build<obj> 0 (Action.dispatch (box "boot"))

    let resumeBody =
        Resume.renderResumable
            BindingResolver.empty
            "sample.resume"
            "{\"count\":0}"
            [ "ticker", Resume.InitEffectInput.LiveSubscription ]
            resumeTree

    let resumeLen = render "index.template.html" "resume.html" resumeBody

    // ── Hydrate baseline ─────────────────────────────────────────────────────
    // Server renders an inert OnClick (no host to dispatch to); the client
    // reconstructs the tree and hydrates the whole thing.
    let hydrateTree = Tree.build<obj> 0 (Action.Chain [])
    let hydrateBody = Hydration.renderHydratable BindingResolver.empty hydrateTree
    let hydrateLen = render "hydrate.template.html" "hydrate.html" hydrateBody

    printfn "Wrote resume.html  — %d bytes server-rendered + resume envelope." resumeLen
    printfn "Wrote hydrate.html — %d bytes server-rendered + full wire tree." hydrateLen
    0
