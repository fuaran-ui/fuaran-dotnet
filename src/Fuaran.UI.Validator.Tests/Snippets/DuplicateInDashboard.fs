module Snippets.DuplicateInDashboard

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.dashboard
        "dup-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.metric "shared-id" Defaults.metric
                  Fuaran.metric "shared-id" Defaults.metric ] }
