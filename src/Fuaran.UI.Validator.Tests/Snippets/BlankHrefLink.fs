module Snippets.BlankHrefLink

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.dashboard
        "bhl-dashboard"
        { Defaults.dashboard with
            Children = [ Fuaran.link "lnk" "" "About" ] }
