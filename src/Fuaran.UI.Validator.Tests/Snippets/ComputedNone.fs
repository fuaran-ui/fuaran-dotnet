module Snippets.ComputedNone

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.metric
        "m"
        { Defaults.metric with
            Value = binding.state "revenue" 0.0 }
