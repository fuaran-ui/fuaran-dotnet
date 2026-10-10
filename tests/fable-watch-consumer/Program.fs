/// The watch-mode leg (Phase 2174): a Fable consumer of the client tier, compiled by
/// `dotnet fable watch` rather than the one-shot compile every other Fable subject uses.
///
/// The two calls below are the shapes that broke. `binding.selectionField` and the decoder's
/// `Selection` arm both reached `Binding.projectSelectionField`, which reads `typeof<'T>` under
/// Fable; from a non-inline generic function Fable has no type to give it, and watch mode reported
/// it as an error at both sites. Fable compiles every file of a referenced project whether or not a
/// consumer calls into it, so the leg would catch the decoder site with no call here at all; the
/// calls make the consumer's own instantiation part of what is compiled.
module FableWatchConsumer.Program

open Fuaran.UI
open Fuaran.UI.Types

/// A typed selection read at two slot types, so the projection's coercion is instantiated concretely.
let chosenId: Binding<string> = binding.selectionField "orders-grid" "id" None

let chosenTotal: Binding<float> =
    binding.selectionField "orders-grid" "total" (Some 0.0)

/// The wire path: a decoded tree carrying a `Selection` binding with a row-field projection.
let decoded: bool =
    let json =
        """{"id":"root","kind":{"$type":"Markdown","text":{"$type":"Bound","binding":{"$type":"Selection","nodeId":"orders-grid","field":"id"}}}}"""

    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Ok _ -> true
    | Error _ -> false
