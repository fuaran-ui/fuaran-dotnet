module Snippets.CustomStaleEnforced

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.custom
        "heatmap"
        "reporting"
        "HeatmapTab"
        (Map.ofList [ "scale", JInt 1; "palette", JInt 2 ])
        (Some
            { Algorithm = "SHA256"
              Hash = "reporting.HeatmapTab.v1"
              Strictness = HashStrictness.Enforced })
        [ NodeId "cell-grid" ]
