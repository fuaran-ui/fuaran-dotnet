module Snippets.RangedBelowMin

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetYear of float

let field: FormFieldKind<Msg> =
    FormFieldKind.rangedNumber
        (Binding.Static(Some 1900.0))
        (fun v -> Action.dispatch (SetYear v))
        (Some 1979.0)
        (Some 2028.0)
        None
