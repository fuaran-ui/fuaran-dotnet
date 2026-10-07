module Snippets.CustomNoHash

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.custom "heatmap" "reporting" "HeatmapTab" Map.empty None []
