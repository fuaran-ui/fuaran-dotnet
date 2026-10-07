module Snippets.FragmentUnresolved

open Fuaran.UI
open Fuaran.UI.Types

let decl () : Node<unit> =
    Fuaran.fragmentDecl
        "decl"
        { Defaults.fragmentDecl<unit> with
            Name = "header"
            Body = Fuaran.markdown "h" "Header" }

let resolved () : Node<unit> = Fuaran.fragmentRef "r1" "header"

let typo () : Node<unit> = Fuaran.fragmentRef "r2" "headr"
