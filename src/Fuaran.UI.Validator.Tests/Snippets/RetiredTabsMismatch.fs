module Snippets.RetiredTabsMismatch

open Fuaran.UI
open Fuaran.UI.Types

/// Headers / children / tags out of step, and an orphan ActiveTag: the
/// FUARAN047 / 048 / 049 shapes. The runtime validator (`PreEmitValidate`)
/// reports them under those codes; the build-time walker no longer does.
let build () : Node<unit> =
    Fuaran.tabs
        "t"
        { Defaults.tabs with
            Children = [ Fuaran.markdown "a" "A"; Fuaran.markdown "b" "B" ]
            TabHeaders =
                Some
                    [ { Label = TextSource.Literal "A"
                        Icon = None
                        Disabled = None } ]
            TabTags = None
            ActiveTag = Some(binding.state "tab" "a") }
