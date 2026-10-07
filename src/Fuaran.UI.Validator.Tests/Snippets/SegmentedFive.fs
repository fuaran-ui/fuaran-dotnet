module Snippets.SegmentedFive

open Fuaran.UI
open Fuaran.UI.Types

let field: FormFieldKind<unit> =
    FormFieldKind.segmentedChoiceDeclarative
        (binding.``static``
            [ { Value = "a"; Label = "A" }
              { Value = "b"; Label = "B" }
              { Value = "c"; Label = "C" }
              { Value = "d"; Label = "D" }
              { Value = "e"; Label = "E" } ])
        (binding.state "tier" "a")
        Orientation.Horizontal
