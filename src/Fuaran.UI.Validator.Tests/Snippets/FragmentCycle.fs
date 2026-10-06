module Snippets.FragmentCycle

open Fuaran.UI
open Fuaran.UI.Types

let loop () : Node<unit> =
    Fuaran.fragmentDecl
        "decl"
        { Defaults.fragmentDecl<unit> with
            Name = "loop"
            Body =
                Fuaran.stack
                    "inner"
                    { Defaults.stack with
                        Children = [ Fuaran.fragmentRef "again" "loop" ] } }
