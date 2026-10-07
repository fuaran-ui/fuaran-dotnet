module Snippets.UnresolvedQuery

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.dashboard
        "uq-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.metric
                      "metric"
                      { Defaults.metric with
                          Value = binding.query "totalRevneu" (fun (r: {| amount: float |}) -> r.amount) } ] }
