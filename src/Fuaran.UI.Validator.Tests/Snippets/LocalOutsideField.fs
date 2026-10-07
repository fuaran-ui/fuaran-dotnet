module Snippets.LocalOutsideField

open Fuaran.UI
open Fuaran.UI.Types

type Msg = NoOp

let misplacedLocal: Binding<string> =
    binding.local (binding.state "salary" "0") LocalFlushTrigger.OnBlur (fun _ -> Action.dispatch NoOp) (Some id) Ok
