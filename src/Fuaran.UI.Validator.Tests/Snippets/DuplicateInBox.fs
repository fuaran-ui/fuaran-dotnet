module Snippets.DuplicateInBox

open Fuaran.UI
open Fuaran.UI.Types

/// A tree rooted at the unified container — the shape the old hand-listed
/// tree-root set (`dashboard` only) never checked.
let build () : Node<unit> =
    Fuaran.box
        "box-root"
        { Children =
            [ Fuaran.stack
                  "inner"
                  { Defaults.stack with
                      Children = [ Fuaran.metric "twice" Defaults.metric ] }
              Fuaran.metric "twice" Defaults.metric ]
          Heading = None
          Layout = BoxLayout.Auto
          Role = BoxRole.Group
          KeepTogether = false
          BreakBefore = false }
