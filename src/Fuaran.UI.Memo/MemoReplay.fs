namespace Fuaran.UI.Memo

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  MemoReplay — op-stream replay as memo-served re-application (Phase 360).
//
//  FGP-5's op-stream already treats replay as re-application; Phase 360
//  GENERALISES that so a recorded UI session replays through the content-
//  addressed store (`fuaran-core#49`): every fragment application in the
//  recorded session is re-derived served from the memo, so replaying a recorded
//  session is cache hits "all the way down" — byte-identical to the original
//  derivation, because totality + the pure/deterministic effect gate are
//  enforced.
//
//  A recorded session is a `RecordedApplication` list — the (fragment, ref-site,
//  arg-set) tuples the session applied, in order. `replay` folds them through an
//  `Engine` (hence through its store): a fragment applied earlier in the SAME
//  session is a store HIT on its second occurrence; a session RESUMED from a
//  persisted `MemoCacheStore` snapshot is a store HIT on its FIRST occurrence
//  (the portable win — the store carries subtrees across the session boundary).
//  `directReplay` re-derives every application WITHOUT a store (a fresh bare
//  `FragmentApply.apply` each time) — the parity oracle: the memo-served trees
//  must equal the direct-replay trees byte-for-byte.
// ============================================================================

/// One recorded fragment application in a session's op-stream — the tuple a
/// replay re-applies. The recorded arg-sets are exactly what the original
/// derivation bound, so replaying them re-derives the original tree.
type RecordedApplication<'Msg> =
    { Fragment: ParamFragment<'Msg>
      RefId: string
      ValueArgs: Map<string, obj>
      SlotArgs: Map<string, Node<'Msg>> }

module MemoReplay =

    /// Replay a recorded session THROUGH the memo store, resolving each
    /// application via the engine (hence via its content-addressed store). Returns
    /// the derived subtree per application, in order, or the first apply error.
    /// A store seeded from a prior session's snapshot serves the first occurrence
    /// of a repeated subtree as a hit (the portability generalisation of replay).
    let replay (engine: Engine<'Msg>) (session: RecordedApplication<'Msg> list) : Result<Node<'Msg> list, string> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | (r: RecordedApplication<'Msg>) :: rest ->
                match engine.Apply(r.Fragment, r.RefId, r.ValueArgs, r.SlotArgs) with
                | Ok d -> go (d.Result.Tree :: acc) rest
                | Error e -> Error e

        go [] session

    /// Replay a recorded session with NO store — a fresh bare `FragmentApply.apply`
    /// per application (the pre-360 store-less path). This is the parity oracle for
    /// `replay`: the memo-served trees must be byte-identical to these.
    let directReplay (session: RecordedApplication<'Msg> list) : Result<Node<'Msg> list, string> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | (r: RecordedApplication<'Msg>) :: rest ->
                match FragmentApply.apply r.Fragment r.RefId r.ValueArgs r.SlotArgs with
                | Ok app -> go (app.Tree :: acc) rest
                | Error e -> Error e

        go [] session
