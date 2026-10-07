module Snippets.DupWithTwin

open Fuaran.UI
open Fuaran.UI.Types

let withDup () : Node<unit> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard with
            Children = [ Fuaran.metric "same" Defaults.metric; Fuaran.metric "same" Defaults.metric ] }

let clean () : Node<unit> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard with
            Children = [ Fuaran.metric "other" Defaults.metric ] }
