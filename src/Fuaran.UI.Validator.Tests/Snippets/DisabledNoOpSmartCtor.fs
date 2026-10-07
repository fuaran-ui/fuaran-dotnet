module Snippets.DisabledNoOpSmartCtor

open Fuaran.UI
open Fuaran.UI.Types

type Msg = Reload

let build () : Node<Msg> =
    Fuaran.button
        "btn-reload"
        { Defaults.button<Msg> with
            OnClick = Action.dispatch Reload
            Disabled = Some(binding.``static`` false) }
