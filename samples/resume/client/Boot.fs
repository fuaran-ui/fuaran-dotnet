module Fuaran.Samples.Resume.Boot

// The lazy interactive chunk. Pulled in by `Main.fs`'s `BootSubtree` via a
// dynamic `import('/output/Boot.js')` ONLY on the first click of the Dispatch
// node — so React + Feliz never touch the load path. Mounting a Feliz component
// here is what makes `output/Boot.js` carry the framework weight the resume
// entry doesn't.

open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Feliz

[<Import("createRoot", "react-dom/client")>]
let private createRoot (el: Browser.Types.Element) : obj = jsNative

[<ReactComponent>]
let private Counter () =
    let (n, setN) = React.useState 0

    Html.div
        [ prop.className "fuaran-card"
          prop.children
              [ Html.p [ prop.text (sprintf "Booted island — live React, count %d" n) ]
                Html.button
                    [ prop.className "fuaran-button fuaran-button-primary"
                      prop.text "+1"
                      prop.onClick (fun _ -> setN (n + 1)) ] ] ]

/// Mount the interactive island into the booted node's container.
let boot (containerId: string) : unit =
    let el = document.getElementById containerId

    if not (isNull (box el)) then
        let root = createRoot el
        root?render (Counter())
        window?__fuaranBooted <- true

// Expose without relying on Fable export-name mangling — the dynamic-import
// caller just runs `window.__fuaranBoot(id)` after the chunk loads.
window?__fuaranBoot <- boot
