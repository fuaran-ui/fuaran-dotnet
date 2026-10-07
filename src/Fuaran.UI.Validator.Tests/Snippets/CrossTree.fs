module Snippets.CrossTree

open Fuaran.UI
open Fuaran.UI.Types

let build1 () : Node<unit> =
    Fuaran.dashboard
        "tree-a"
        { Defaults.dashboard with
            Children = [ Fuaran.metric "shared" Defaults.metric ] }

let build2 () : Node<unit> =
    Fuaran.dashboard
        "tree-b"
        { Defaults.dashboard with
            Children = [ Fuaran.metric "shared" Defaults.metric ] }
