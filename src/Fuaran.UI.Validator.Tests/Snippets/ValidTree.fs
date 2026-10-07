module Snippets.ValidTree

open Fuaran.UI
open Fuaran.UI.Types

type Msg =
    | LoadData
    | SelectRow of int

let build () : Node<Msg> =
    Fuaran.dashboard
        "valid-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.metric
                      "metric-revenue"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Value = binding.query "totalRevenue" (fun (r: {| amount: float |}) -> r.amount) }
                  Fuaran.button
                      "btn-reload"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Reload"
                          OnClick = Action.dispatch LoadData } ] }
