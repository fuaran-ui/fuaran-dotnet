module Snippets.CustomDynamicProps

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let build (dynamicProps: Map<string, JVal>) : Node<unit> =
    Fuaran.custom
        "heatmap"
        "reporting"
        "HeatmapTab"
        dynamicProps
        (Some
            { Algorithm = "SHA256"
              Hash = "reporting.HeatmapTab.v1"
              Strictness = HashStrictness.Enforced })
        [ NodeId "cell-grid" ]
