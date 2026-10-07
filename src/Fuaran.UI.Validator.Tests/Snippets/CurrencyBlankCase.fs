module Snippets.CurrencyBlankCase

open Fuaran.UI
open Fuaran.UI.Types

let build () : Binding<string> =
    binding.format (binding.``static`` 1234.5) (Format.Currency "") locale.ambient
