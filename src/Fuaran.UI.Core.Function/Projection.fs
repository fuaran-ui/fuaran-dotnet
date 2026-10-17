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

/// The absolute lexical address of a fragment-decl hole: `<declId>.<holeName>`. Binding is by
/// address, never by a bare name (hygiene).
let holeAddr (declId: string) (holeName: string) : string = declId + "." + holeName

/// A fragment-decl node's declared holes as the Core function model reads them, each at its
/// absolute address (`holeAddr`). Every other node declares none. The one definition the tier's
/// `ArtifactWitness`es read (Phase 2308).
let holesOf (node: Node<'Msg>) : Fuaran.Core.HoleDecl list =
    match node.Kind with
    | NodeKind.FragmentDecl spec ->
        spec.Holes
        |> List.map (fun h ->
            let name = HoleDecl.name h

            let kind =
                match h with
                | HoleDecl.Value(_, space, _) -> Fuaran.Core.ValueHole(toCoreSpace space)
                | HoleDecl.Slot(_, constraintOpt) -> Fuaran.Core.SlotHole constraintOpt
                | HoleDecl.Repeat(_, space) -> Fuaran.Core.RepeatHole(toCoreSpace space)

            ({ Addr = holeAddr node.Id name
               Name = name
               Kind = kind }
            : Fuaran.Core.HoleDecl))
    | _ -> []

/// A node's declared effect as the Core function model reads it: a fragment decl's declared
/// class, and pure and deterministic for every other node, which declares no effect surface.
/// `Fuaran.Core.Function.observedEffect` joins this over a subtree (Phase 2308).
let effectOf (node: Node<'Msg>) : Fuaran.Core.EffectClass =
    match node.Kind with
    | NodeKind.FragmentDecl spec -> toCoreEffect spec.Effect
    | _ -> Fuaran.Core.Effect.pureDeterministic
