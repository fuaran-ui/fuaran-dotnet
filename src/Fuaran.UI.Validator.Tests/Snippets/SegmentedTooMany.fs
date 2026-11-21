module Snippets.SegmentedTooMany

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetTier of string option

let field: FormFieldKind<Msg> =
    FormFieldKind.segmentedChoice
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
                Label = TextSource.Literal "E" }
              { Value = "f"
                Label = TextSource.Literal "F" }
              { Value = "g"
                Label = TextSource.Literal "G" }
              { Value = "h"
                Label = TextSource.Literal "H" } ])
        (binding.state "tier" "a")
        (fun v -> Action.dispatch (SetTier v))
        Orientation.Horizontal
