module Snippets.DefaultOutOfRange

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.fragmentDecl
        "decl"
        { Defaults.fragmentDecl<unit> with
            Name = "card"
            Body = Fuaran.markdown "b" "x"
            Holes = [ HoleDecl.Value("count", HoleValueSpace.IntRange(0, 10), Some(Scalar.Int 50)) ] }
