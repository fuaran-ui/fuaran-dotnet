module Snippets.ExtraAttributeOnClick

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.metric "m" Defaults.metric
    |> Node.withExtraAttribute "onclick" "alert(1)"
    |> Node.withExtraAttribute "data-testid" "metric"
