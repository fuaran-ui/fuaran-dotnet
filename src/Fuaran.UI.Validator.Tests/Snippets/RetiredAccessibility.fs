module Snippets.RetiredAccessibility

open Fuaran.UI
open Fuaran.UI.Types

type Msg = Go

/// A button with no accessible name and a Critical callout with its
/// announcement opted out: the FUARAN040 / 041 intents, written as they
/// compile today (accessibility is a node-level slot, not a spec field).
let build () : Node<Msg> =
    Fuaran.dashboard
        "a11y"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "unnamed"
                      { Defaults.button<Msg> with
                          OnClick = Action.dispatch Go }
                  |> Node.withAccessibility None
                  Fuaran.callout
                      "alert"
                      { Defaults.callout with
                          Tone = ToneVariant.Critical }
                  |> Node.withAccessibility None ] }
