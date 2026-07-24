module Fuaran.UI.Ops.Benchmarks.HitRate

open Fuaran.UI.Memo
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  HitRate (Phase 200) — memo reuse measurement, off the BenchmarkDotNet hot
//  path.
//
//  The memoisation HIT-RATE is the number that proves the Phase 183 "edit a
//  decision → the chart re-derives cheaply" claim: over a realistic authoring
//  session (mostly value edits, occasional structural change), what fraction of
//  derivations were served from cache (HIT) or done incrementally (INCREMENTAL)
//  rather than fully re-derived (MISS)?
//
//  This is measurement infrastructure, not a benchmark: running `measure`
//  produces the `memo.hit_rate.edit_session` baseline metric. The RUN is the
//  deferred step; the code below compile-checks the engine + telemetry API.
// ============================================================================

/// A counting telemetry sink — tallies cache outcomes by reuse-vs-rederive.
/// Every channel except `RecordCacheStat` is a no-op.
type CountingSink() =
    let mutable reuse = 0
    let mutable rederive = 0

    member _.Reuse = reuse
    member _.Rederive = rederive
    member _.Total = reuse + rederive

    /// Reuse fraction over all observed outcomes; `0.0` if nothing was observed.
    member _.HitRate =
        if reuse + rederive = 0 then
            0.0
        else
            float reuse / float (reuse + rederive)

    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()

        member _.RecordCacheStat t =
            if CacheOutcome.isReuse t.Outcome then
                reuse <- reuse + 1
            else
                rederive <- rederive + 1

/// Run a representative edit session over one scenario and return the memo
/// reuse fraction. The session shape: one cold derivation (MISS), then
/// `valueEdits` value-only edits (each INCREMENTAL — the cheap path), with a
/// structural slot edit every `structuralEvery` steps (a MISS — the genuine
/// re-derivation). A higher reuse fraction means the engine spared more
/// whole-tree work.
let measure (scenario: Corpus.Scenario) (valueEdits: int) (structuralEvery: int) : float =
    let sink = CountingSink()
    let engine = Engine<unit>(256, sink)

    let mutable derivation =
        match engine.Apply(scenario.Fragment, scenario.RefId, scenario.BaseArgs, scenario.SlotArgs) with
        | Ok d -> d
        | Error e -> failwith $"hit-rate session seed apply failed: {e}"

    for step in 1..valueEdits do
        let isStructural = structuralEvery > 0 && step % structuralEvery = 0

        let slots, args =
            if isStructural then
                scenario.SlotEdit, scenario.BaseArgs
            else
                scenario.SlotArgs, scenario.OneValueEdit

        derivation <-
            match engine.Reapply(derivation, scenario.Fragment, scenario.RefId, args, slots) with
            | Ok d -> d
            | Error e -> failwith $"hit-rate session reapply failed: {e}"

    sink.HitRate
