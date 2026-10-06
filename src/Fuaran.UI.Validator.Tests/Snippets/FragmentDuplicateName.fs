module Snippets.FragmentDuplicateName

open Fuaran.UI
open Fuaran.UI.Types

let first () : Node<unit> =
    Fuaran.fragmentDecl
        "decl-a"
        { Defaults.fragmentDecl<unit> with
            Name = "shared"
            Body = Fuaran.markdown "a" "A" }

let second () : Node<unit> =
    Fuaran.fragmentDecl
        "decl-b"
        { Defaults.fragmentDecl<unit> with
            Name = "shared"
            Body = Fuaran.markdown "b" "B" }

let reference () : Node<unit> = Fuaran.fragmentRef "r" "shared"
