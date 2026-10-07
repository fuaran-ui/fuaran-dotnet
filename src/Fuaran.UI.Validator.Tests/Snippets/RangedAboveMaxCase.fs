module Snippets.RangedAboveMaxCase

open Fuaran.UI
open Fuaran.UI.Types

let field: FormFieldKind<unit> =
    FormFieldKind.RangedNumber(Some(Binding.Static(Some 2050.0)), None, Some 1979.0, Some 2028.0, None)
