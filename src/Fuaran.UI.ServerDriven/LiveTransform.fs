namespace Fuaran.UI.ServerDriven

open Fuaran.Core
open Fuaran.UI.FragmentMemo

// ============================================================================
//  LiveTransform — the incremental evaluation of a LIVE Transform source
//  (Phase 1179).
//
//  A `TransformSource.Live` binding runs a pipeline over a state-bound table
//  and is read again whenever that state is written. Evaluating it in full on
//  every write is correct and pays for every unchanged row; the columnar
//  substrate ships a seam that avoids exactly that — prime once over the source,
//  then advance the primed state against a delta describing what the edit
//  changed — and until this module nothing in the estate outside that
//  substrate's own tests called it.
//
//  ── WHY THIS TIER ─────────────────────────────────────────────────────────
//  The seam needs somewhere to keep the primed state BETWEEN evaluations, and
//  the render path has nowhere: a resolver call is a pure function of the
//  sources it is handed, by design and worth keeping. The server-driven tier is
//  the one that already holds a connection's state across edits — an inbound
//  event is a state edit, and the loop that folds it is the loop that would ask
//  for the table again — so the store lives here and is owned by whatever holds
//  the session.
//
//  ── WHAT IT PROMISES, AND WHAT IT DOES NOT ────────────────────────────────
//  It promises ONE thing: the table it returns is the table a full evaluation
//  over the current source produces. That is the substrate's own certified
//  property of the seam, not an assertion added here, and this module's tests
//  re-check it over the conformance corpus's own edit streams because a
//  consumer that trusted the property without measuring it would not notice the
//  day it stopped holding.
//
//  It does NOT promise to have done less work. A pipeline the seam declines —
//  one carrying a step whose output for a row is a function of rows the delta
//  does not name — falls back to the reference evaluator INSIDE the seam, which
//  reports the fall-back and its typed reason in the footprint. That is the
//  honest shape: a decline is a measured outcome with a reason, never a gap, and
//  a caller that wants to know before evaluating asks `Incremental.plan`.
//
//  ── THE STORE IS BOUNDED, AND SINGLE-THREADED BY CONTRACT ─────────────────
//  It reuses `FragmentMemo.BoundedLru` rather than minting a second cache: the
//  bound, the recency rule and the hit/miss counters are the same requirement
//  the fragment memo already met, and a long-lived connection cycling through
//  many grids must not grow a map without limit. The LRU's threading contract
//  travels with it — a host sharing one store across threads serialises access.
//  Evicting a site's state is never a correctness question: the next evaluation
//  re-primes and produces the same table, having paid for it.
// ============================================================================

/// What one evaluation of a live Transform site produced, and the account of the
/// work that produced it. `Primed` says which of the two paths ran, so a caller
/// can tell a first render from an advance without decoding the footprint.
type LiveTransformEvaluation =
    { Result: Table
      Footprint: RecomputeFootprint
      Primed: bool }

/// The per-connection store of primed live-Transform evaluations, one per SITE.
///
/// A site is whatever the caller says it is, and the caller should make it
/// identify a reader: two grids over one state key are two sites with two
/// pipelines, and one grid keeps its primed state across every edit to that key.
/// A site whose pipeline or whose source schema has moved is not a defect — the
/// seam notices and re-primes, recording why in the footprint.
type LiveTransformStore(capacity: int) =
    let states = BoundedLru<IncrementalEval>(capacity)

    /// The default bound. Generous relative to the number of live grids one
    /// connection renders, and small enough that a session cycling through many
    /// of them cannot grow without limit.
    new() = LiveTransformStore(64)

    member _.Capacity = states.Capacity
    member _.Count = states.Count

    /// Sites primed and then advanced, rather than re-primed — the observability
    /// the LRU already keeps. A hit rate near zero on a stable set of grids means
    /// the site keys are not stable, which is a caller defect the counts surface.
    member _.Hits = states.Hits

    member _.Misses = states.Misses

    /// Forget every primed state. Correctness-neutral: the next evaluation of any
    /// site re-primes over its current source.
    member _.Clear() = states.Clear()

    /// Evaluate `pipeline` over `source` for one site, priming on the first sight
    /// of the site and advancing the primed state on every later one.
    ///
    /// `identityColumn` is the column whose value identifies a row — the key the
    /// edit stream addresses rows by. It is the caller's declaration and not a
    /// guess: a source keyed by position has no identity, and the seam declines a
    /// positional delta rather than treating it as an identity one, because a
    /// cache keyed by position is invalidated wholesale by any insert.
    ///
    /// The delta is DERIVED here, by diffing the source the primed state was last
    /// evaluated against with the one handed in now, rather than taken from the
    /// caller. A caller-supplied delta would be a second description of a change
    /// the tables already carry, and the seam's whole guarantee is conditioned on
    /// that description being truthful.
    ///
    /// A pipeline reading a HOST-RESOLVED named source is refused by name rather
    /// than served: the everyday seam call resolves nothing, so there is no
    /// answer to give and inventing a footprint for one would be worse than the
    /// refusal. Evaluate such a pipeline through [[LiveTransform.reference]].
    member _.Evaluate
        (site: string, identityColumn: string, pipeline: Transform list, source: Table)
        : Result<LiveTransformEvaluation, string> =

        let idw = RowIdentity.byColumn identityColumn

        let toEvaluation (primed: bool) (state: IncrementalEval) =
            states.Set(site, state)

            { Result = Incremental.result state
              Footprint = Incremental.footprint state
              Primed = primed }

        match states.TryGet site with
        | None ->
            Incremental.primeOn idw pipeline source
            |> Result.map (toEvaluation true)
            |> Result.mapError DataFrame.errorString
        | Some prior ->
            // A source the witness cannot key is not a source with no change; it
            // is a source whose change cannot be DESCRIBED. The honest delta for
            // that is the top element, which the seam answers by evaluating in
            // full and re-priming its caches — so the next edit can restrict
            // again — and records the reason in the footprint rather than
            // silently reusing a cache nothing vouches for.
            let delta =
                match Delta.diff idw prior.Source source with
                | Ok d -> d
                | Error _ -> FullRefresh

            Incremental.refreshOn idw pipeline prior delta source
            |> Result.map (toEvaluation false)
            |> Result.mapError DataFrame.errorString

[<RequireQualifiedAccess>]
module LiveTransform =

    /// A fresh store at the default bound.
    let store () = LiveTransformStore()

    /// The reference answer for one evaluation — a full evaluation of the
    /// pipeline over the source, with no cache consulted and nothing primed.
    ///
    /// This is what the incremental path is measured AGAINST, and it is exposed
    /// so a caller can measure it: the seam's equivalence is certified upstream,
    /// and a consumer that never checks it is a consumer that would not notice
    /// the certification lapsing.
    let reference (pipeline: Transform list) (source: Table) : Result<Table, string> =
        DataFrame.evalPipelineInEnv Map.empty pipeline source
        |> Result.mapError DataFrame.errorString
