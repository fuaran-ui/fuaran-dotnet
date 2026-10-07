module Snippets.DisabledPlaceholder

open Fuaran.UI
open Fuaran.UI.Types

type Msg = Reload

let build () : Node<Msg> =
    Fuaran.button
        "btn-reload"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Reload"
            OnClick = Action.dispatch Reload
            Disabled = Some(Binding.Static(Some true)) }
