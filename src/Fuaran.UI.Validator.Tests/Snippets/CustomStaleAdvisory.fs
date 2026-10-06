module Snippets.CustomStaleAdvisory

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.custom
        "heatmap"
        "reporting"
        "HeatmapTab"
        (Map.ofList [ "scale", JInt 1 ])
        (Some
            { Algorithm = "SHA256"
              Hash = "0000000000000000000000000000000000000000000000000000000000000000"
              Strictness = HashStrictness.AdvisoryWarning })
        []
