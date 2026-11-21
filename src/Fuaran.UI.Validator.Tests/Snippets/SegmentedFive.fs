module Snippets.SegmentedFive

open Fuaran.UI
open Fuaran.UI.Types

let field: FormFieldKind<unit> =
    FormFieldKind.segmentedChoiceDeclarative
        (binding.``static``
            [ { Value = "a"
                Label = TextSource.Literal "A" }
              { Value = "b"
                Label = TextSource.Literal "B" }
              { Value = "c"
                Label = TextSource.Literal "C" }
              { Value = "d"
                Label = TextSource.Literal "D" }
              { Value = "e"
                Label = TextSource.Literal "E" } ])
        (binding.state "tier" "a")
        Orientation.Horizontal
