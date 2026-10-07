module Snippets.RowTypeMissing

open Fuaran.UI
open Fuaran.UI.Types

type SaleRow = { Id: int; Amount: float }

let build () : Node<unit> =
    Fuaran.dashboard
        "rtm-dashboard"
        { Defaults.dashboard with
            Children =
                [ Fuaran.grid
                      "grid"
                      (fun (r: SaleRow) -> (Map.empty: Row))
                      { Defaults.grid<SaleRow, unit> with
                          Source = binding.query "unknownRows" id } ] }
