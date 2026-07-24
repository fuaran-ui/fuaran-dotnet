module Fuaran.Samples.Hydration.Gen

open System.IO
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

[<EntryPoint>]
let main _ =
    let here = __SOURCE_DIRECTORY__
    let sampleDir = Path.GetFullPath(Path.Combine(here, ".."))
    let template = File.ReadAllText(Path.Combine(sampleDir, "index.template.html"))
    // Server renders tab 0 active with an inert OnSelect (no host to dispatch to).
    let tree = Tree.build<obj> 0 (fun _ -> Action.Chain [])
    let bodyHtml = Render.render BindingResolver.empty tree
    let html = template.Replace("<!--SSR-->", bodyHtml)
    File.WriteAllText(Path.Combine(sampleDir, "index.html"), html)
    printfn "Wrote index.html — %d bytes of server-rendered HTML embedded." bodyHtml.Length
    0
