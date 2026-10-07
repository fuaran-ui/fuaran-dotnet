module Snippets.SegmentedTooMany

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetTier of string option

let field: FormFieldKind<Msg> =
    FormFieldKind.segmentedChoice
        (binding.``static``
            [ { Value = "a"; Label = "A" }
              { Value = "b"; Label = "B" }
              { Value = "c"; Label = "C" }
              { Value = "d"; Label = "D" }
              { Value = "e"; Label = "E" }
              { Value = "f"; Label = "F" }
              { Value = "g"; Label = "G" }
              { Value = "h"; Label = "H" } ])
        (binding.state "tier" "a")
        (fun v -> Action.dispatch (SetTier v))
        Orientation.Horizontal
