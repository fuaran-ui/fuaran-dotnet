module Snippets.BlankHrefLinkSpec

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.linkSpec
        "lnk"
        { Defaults.link with
            Href = Binding.Static(Some "  ") }
