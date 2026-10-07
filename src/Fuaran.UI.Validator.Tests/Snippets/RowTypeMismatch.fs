module Snippets.RowTypeMismatch

open Fuaran.UI
open Fuaran.UI.Types

type WrongRow = { Other: string }

type Msg = SelectRow of int

/// The source row type is named once, on `toRow`'s parameter.
let build () : Node<Msg> =
    Fuaran.dashboard
        "rt-dashboard"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.grid
                      "grid"
                      (fun (r: WrongRow) -> (Map.empty: Row))
                      { Defaults.grid<WrongRow, Msg> with
                          Source = binding.query "salesRows" id } ] }
