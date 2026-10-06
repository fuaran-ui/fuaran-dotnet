module Snippets.CustomRatio

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

/// 4 Custom sites among 21 typed constructions — a ratio far above the 0.05
/// default.
let build () : Node<unit> =
    Fuaran.dashboard
        "ratio"
        { Defaults.dashboard with
            Children =
                [ Fuaran.custom "c1" "reporting" "Tile" Map.empty None []
                  Fuaran.custom "c2" "reporting" "Tile" Map.empty None []
                  Fuaran.custom "c3" "reporting" "Tile" Map.empty None []
                  Fuaran.custom "c4" "reporting" "Tile" Map.empty None []
                  Fuaran.metric "m1" Defaults.metric
                  Fuaran.metric "m2" Defaults.metric
                  Fuaran.metric "m3" Defaults.metric
                  Fuaran.metric "m4" Defaults.metric
                  Fuaran.metric "m5" Defaults.metric
                  Fuaran.metric "m6" Defaults.metric
                  Fuaran.metric "m7" Defaults.metric
                  Fuaran.metric "m8" Defaults.metric
                  Fuaran.metric "m9" Defaults.metric
                  Fuaran.metric "m10" Defaults.metric
                  Fuaran.metric "m11" Defaults.metric
                  Fuaran.metric "m12" Defaults.metric
                  Fuaran.metric "m13" Defaults.metric
                  Fuaran.metric "m14" Defaults.metric
                  Fuaran.metric "m15" Defaults.metric
                  Fuaran.metric "m16" Defaults.metric ] }
