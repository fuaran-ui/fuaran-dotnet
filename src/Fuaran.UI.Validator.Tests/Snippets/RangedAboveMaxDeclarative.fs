module Snippets.RangedAboveMaxDeclarative

open Fuaran.UI
open Fuaran.UI.Types

let field: FormFieldKind<unit> =
    FormFieldKind.rangedNumberDeclarative (binding.``static`` 2050.0) (Some 1979.0) (Some 2028.0) None
