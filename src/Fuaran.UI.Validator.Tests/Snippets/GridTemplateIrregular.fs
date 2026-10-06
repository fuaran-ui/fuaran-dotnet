module Snippets.GridTemplateIrregular

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.gridLayoutTemplated "g" "1fr 2fr" Defaults.gridLayout
