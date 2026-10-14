module Fuaran.UI.Renderer.StateSeeds

// ============================================================================
//  Fuaran — the state-seeding pass both renderers run (Phase 1075; one
//  definition since Phase 2067).
//
//  A `Binding.State` carrying a `defaultValue` DECLARES the value of its slot
//  rather than merely falling back to it for itself, so a grid bound to
//  `$state.members` and a `Transform` deriving over the same key read the same
//  rows. The declared values are collected once for the whole tree and laid
//  UNDER whatever the caller already furnished.
//
//  **Precedence, per the shared-data-source charter §4:** a host-furnished
//  value (and, in a scoped render, a value written into the scope's store) wins
//  over a seed. A seed is the value before anything else has said anything,
//  never an override.
//
//  **Order-independent, per §5.** The pass runs over the whole tree before any
//  binding resolves, so document order carries no meaning.
//
//  Both renderers' context builders call `apply`, so the client's and the
//  server's first render read an identical `State` map, which is what keeps
//  hydration mismatch-free (`docs/SSR.md`).
// ============================================================================

open Fuaran.UI.Types

/// Lay `node`'s declared state seeds under `sources.State`: a furnished value
/// wins over a seed. Zero-cost when the tree declares nothing: the caller's
/// sources come back unchanged, by reference.
let apply<'Msg> (node: Node<'Msg>) (sources: BindingResolver.BindingSources) : BindingResolver.BindingSources =
    let seeds = Fuaran.UI.BindingWalk.stateSeeds node

    if Map.isEmpty seeds then
        sources
    else
        { sources with
            State = sources.State |> Map.fold (fun acc k v -> Map.add k v acc) seeds }
