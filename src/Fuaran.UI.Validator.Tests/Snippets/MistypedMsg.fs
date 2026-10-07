module Snippets.MistypedMsg

open Fuaran.UI
open Fuaran.UI.Types

type Msg =
    | LoadDate
    | Reset

/// `LoadDate` compiles (it is a case of THIS Msg); the manifest names the
/// app's real cases, so the validator flags it as a near-miss of `LoadData`.
let build () : Node<Msg> =
    Fuaran.dashboard
        "mm-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "btn"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Go"
                          OnClick = Action.dispatch LoadDate } ] }
