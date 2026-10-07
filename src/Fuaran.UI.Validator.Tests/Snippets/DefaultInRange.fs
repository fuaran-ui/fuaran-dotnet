module Snippets.DefaultInRange

open Fuaran.UI
open Fuaran.UI.Types

let build () : Node<unit> =
    Fuaran.fragmentDecl
        "decl"
        { Defaults.fragmentDecl<unit> with
            Name = "card"
            Body = Fuaran.markdown "b" "x"
            Holes =
                [ HoleDecl.Value("count", HoleValueSpace.IntRange(0, 100), Some(Scalar.Int 7))
                  HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), Some(Scalar.Str "ok")) ] }
