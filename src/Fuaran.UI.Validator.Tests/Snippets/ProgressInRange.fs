module Snippets.ProgressInRange

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.dashboard
        "irp-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.progress
                      "load-bar"
                      { Defaults.progress with
                          Fraction = binding.``static`` 0.75 } ] }
