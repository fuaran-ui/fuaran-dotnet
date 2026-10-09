module Snippets.SegmentedTooManyCase

open Fuaran.UI
open Fuaran.UI.Types

let field: FormFieldKind<unit> =
    FormFieldKind.SegmentedChoice(
        Binding.Static(
            Some
                [ { Value = "a"; Label = "A" }
                  { Value = "b"; Label = "B" }
                  { Value = "c"; Label = "C" }
                  { Value = "d"; Label = "D" }
                  { Value = "e"; Label = "E" }
                  { Value = "f"; Label = "F" }
                  { Value = "g"; Label = "G" }
                  { Value = "h"; Label = "H" } ]
        ),
        Some(FieldValue.ofText (binding.state "tier" "a")),
        None,
        Orientation.Vertical
    )
