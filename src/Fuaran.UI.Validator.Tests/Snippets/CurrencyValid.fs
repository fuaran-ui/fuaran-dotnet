module Snippets.CurrencyValid

open Fuaran.UI
open Fuaran.UI.Types

let build () : Binding<string> =
    binding.format (binding.``static`` 1234.5) (Format.Currency "GBP") (locale.explicit "en-GB")
