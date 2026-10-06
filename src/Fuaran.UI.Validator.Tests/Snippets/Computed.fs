module Snippets.Computed

open Fuaran.UI
open Fuaran.UI.Types

let tree: Node<unit> =
    Fuaran.metric
        "m1"
        { Defaults.metric with
            Value = Binding.Computed(fun _ -> 42.0) }
