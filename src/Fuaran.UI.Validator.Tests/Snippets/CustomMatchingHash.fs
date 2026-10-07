module Snippets.CustomMatchingHash

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

/// The hash is `computeBodyShapeHash "reporting" "HeatmapTab" ["scale"; "palette"]
/// ["cell-grid"]`; the test pins that equality, so the literal cannot rot.
let build () : Node<unit> =
    Fuaran.custom
        "heatmap"
        "reporting"
        "HeatmapTab"
        (Map.ofList [ "scale", JInt 1; "palette", JInt 2 ])
        (Some
            { Algorithm = "SHA256"
              Hash = "4c0b07a6bf08be365e3ea08d126c667b07882cc383d91505ff76ec4e16135a97"
              Strictness = HashStrictness.Enforced })
        [ NodeId "cell-grid" ]
