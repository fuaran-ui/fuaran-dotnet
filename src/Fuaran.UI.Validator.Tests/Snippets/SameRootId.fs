module Snippets.SameRootId

open Fuaran.UI
open Fuaran.UI.Types

let real () : Node<unit> =
    Fuaran.dashboard
        "doc-root"
        { Defaults.dashboard with
            Children = [ Fuaran.metric "clause-1" Defaults.metric ] }

let standIn () : Node<unit> =
    Fuaran.dashboard
        "doc-root"
        { Defaults.dashboard with
            Children = [ Fuaran.metric "clause-1" Defaults.metric ] }
