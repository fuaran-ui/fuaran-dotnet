module Snippets.ExtraAttributeNameInjection

open Fuaran.UI
open Fuaran.UI.Types

// Phase 2042: the key carries the data- prefix, but its space and = would end
// the attribute name at the emission site and open a live handler. The
// renderer floor drops it, so the build-time check reports it too.
let build () : Node<unit> =
    Fuaran.metric "m" Defaults.metric
    |> Node.withExtraAttribute "data-x onmouseover=alert(1)" "v"
    |> Node.withExtraAttribute "data-testid" "metric"
