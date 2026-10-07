module Snippets.NestedUnresolvedQuery

open Fuaran.UI
open Fuaran.UI.Types

/// One unresolved query three containers deep — one finding, not one per
/// enclosing call.
let build () : Node<unit> =
    Fuaran.dashboard
        "outer"
        { Defaults.dashboard with
            Children =
                [ Fuaran.stack
                      "middle"
                      { Defaults.stack with
                          Children =
                              [ Fuaran.card
                                    "inner"
                                    { Defaults.card with
                                        Children =
                                            [ Fuaran.metric
                                                  "deep"
                                                  { Defaults.metric with
                                                      Value =
                                                          binding.query "totalRevneu" (fun (r: {| amount: float |}) ->
                                                              r.amount) } ] } ] } ] }
