module Snippets.ProgressOutOfRange

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.dashboard
        "orp-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.progress
                      "load-bar"
                      { Defaults.progress with
                          Fraction = Binding.Static(Some 75.0) } ] }
