module Snippets.ValidLink

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.dashboard
        "vl-dashboard"
        { Defaults.dashboard with
            Children = [ Fuaran.link "lnk" "/about" "About" ] }
