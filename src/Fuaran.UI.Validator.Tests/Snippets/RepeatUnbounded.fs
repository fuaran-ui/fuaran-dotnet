module Snippets.RepeatUnbounded

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.fragmentDecl
        "decl"
        { Defaults.fragmentDecl<unit> with
            Name = "rep"
            Body = Fuaran.markdown "b" "x"
            Holes = [ HoleDecl.Repeat("rows", HoleValueSpace.AnyString) ] }
