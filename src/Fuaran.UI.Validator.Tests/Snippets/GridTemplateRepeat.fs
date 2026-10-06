module Snippets.GridTemplateRepeat

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.gridLayoutTemplated "g" "repeat(3, 1fr)" Defaults.gridLayout
