module Snippets.LocalInRangedNumber

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetAge of float

let viaCase: FormFieldKind<Msg> =
    FormFieldKind.RangedNumber(
        Some(
            FieldValue.ofNumber (
                binding.local
                    (binding.``static`` 0.0)
                    LocalFlushTrigger.OnSubmit
                    (fun a -> Action.dispatch (SetAge a))
                    (Some string)
                    (fun _ -> Ok 0.0)
            )
        ),
        None,
        None,
        None,
        None
    )

let viaSmartCtor: FormFieldKind<Msg> =
    FormFieldKind.rangedNumberDeclarative
        (binding.local
            (binding.``static`` 0.0)
            LocalFlushTrigger.OnSubmit
            (fun a -> Action.dispatch (SetAge a))
            (Some string)
            (fun _ -> Ok 0.0))
        None
        None
        None
