module Snippets.SegmentedTooManyCase

open Fuaran.UI
open Fuaran.UI.Types

let field: FormFieldKind<unit> =
    FormFieldKind.SegmentedChoice(
        Binding.Static(
            Some
                [ { Value = "a"
                    Label = TextSource.Literal "A" }
                  { Value = "b"
                    Label = TextSource.Literal "B" }
                  { Value = "c"
                    Label = TextSource.Literal "C" }
                  { Value = "d"
                    Label = TextSource.Literal "D" }
                  { Value = "e"
                    Label = TextSource.Literal "E" }
                  { Value = "f"
                    Label = TextSource.Literal "F" }
                  { Value = "g"
                    Label = TextSource.Literal "G" }
                  { Value = "h"
                    Label = TextSource.Literal "H" } ]
        ),
        Some(FieldValue.ofText (binding.state "tier" "a")),
        None,
        Orientation.Vertical
    )
