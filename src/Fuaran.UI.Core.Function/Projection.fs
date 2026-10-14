/// The projection of the UI hole and effect vocabulary (`Fuaran.UI.Types`) onto the Core
/// function model (`Fuaran.Core`). The two are structural mirrors; this is the one place the
/// translation is written, read by both the AI-tool surface's witness and the build-time
/// validator's recipe certification (D17).
module Fuaran.UI.Core.Function.Projection

open Fuaran.UI.Types

/// A hole's declared value space, as the Core function model reads it.
let toCoreSpace (s: HoleValueSpace) : Fuaran.Core.ValueSpace =
    match s with
    | HoleValueSpace.IntRange(lo, hi) -> Fuaran.Core.IntRange(lo, hi)
    | HoleValueSpace.FloatRange(lo, hi) -> Fuaran.Core.FloatRange(lo, hi)
    | HoleValueSpace.StringLen(lo, hi) -> Fuaran.Core.StringLen(lo, hi)
    | HoleValueSpace.Enum choices -> Fuaran.Core.Enum choices
    | HoleValueSpace.AnyString -> Fuaran.Core.AnyString

/// A fragment's declared effect class, as the Core function model reads it. The UI
/// determinism chain maps to the Core factor set: `Deterministic` is the empty set, each other
/// source its single factor.
let toCoreEffect (e: EffectClass) : Fuaran.Core.EffectClass =
    { Host =
        match e.HostEffect with
        | HostEffect.Pure -> Fuaran.Core.Pure
        | HostEffect.ReadsHost -> Fuaran.Core.ReadsHost
        | HostEffect.WritesHost -> Fuaran.Core.WritesHost
      Determinism =
        match e.Determinism with
        | DeterminismSource.Deterministic -> Fuaran.Core.Effect.deterministic
        | DeterminismSource.Clock -> Fuaran.Core.Effect.clock
        | DeterminismSource.Random -> Fuaran.Core.Effect.random
        | DeterminismSource.Network -> Fuaran.Core.Effect.network }
