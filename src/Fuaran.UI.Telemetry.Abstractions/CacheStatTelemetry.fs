namespace Fuaran.UI.Telemetry.Abstractions

open System

// ============================================================================
//  CacheStatTelemetry — one structured record per memoised fragment-apply
//  (Phase 183).
//
//  Emitted from the incremental re-derivation engine (`Fuaran.UI.Fragment.Memo`)
//  at every `apply` / `reapply` call. Makes the memo cache's behaviour
//  observable: a hit/miss/incremental/bypass outcome plus the cache's current
//  size against its bound. The "edit a decision → the chart re-derives cheaply"
//  property is only trustworthy if its hit rate is visible — this is that
//  signal.
//
//  `CacheOutcome` is closed + structural (mirrors `ProviderCallOutcome`): a host
//  sink encodes it without a host-error-type-aware codec. `Incremental` carries
//  the number of hole-addressed (`<refId>.<holeName>`) subtrees that were
//  recomputed — 0 would be a degenerate full hit, so the incremental path always
//  reports the count it actually touched.
//
//  FGP 4 (diagnostics under both pipelines): the record shape is Fable-
//  compatible — no closures, no `System.Security.Cryptography`; `DateTimeOffset`
//  rounds through Fable cleanly per the `ProviderCallTelemetry` precedent.
// ============================================================================

/// Closed, host-neutral classification of a memo-cache outcome.
[<RequireQualifiedAccess>]
type CacheOutcome =
    /// Served from the cache — the substituted tree was reused unchanged, no
    /// recompute.
    | Hit
    /// Not cached — a full derivation ran and the result was stored.
    | Miss
    /// The fragment's effect class is not pure-deterministic, so the cache was
    /// neither consulted nor populated — the result was re-derived from scratch
    /// (an effecting / clock / random / network result must never be memoised).
    | Bypass
    /// Incremental re-derivation: the structural tree was reused (a value-
    /// parameter change leaves it untouched) and only `recomputed` hole-address
    /// bindings were recomputed.
    | Incremental of recomputed: int

[<RequireQualifiedAccess>]
module CacheOutcome =
    /// Stable, key-free classification token for op-stream filters + host
    /// aggregates.
    let name (outcome: CacheOutcome) : string =
        match outcome with
        | CacheOutcome.Hit -> "hit"
        | CacheOutcome.Miss -> "miss"
        | CacheOutcome.Bypass -> "bypass"
        | CacheOutcome.Incremental _ -> "incremental"

    /// True for the outcomes that served (some of) the result from cache — `Hit`
    /// and `Incremental`. A `Miss` (full derivation) and a `Bypass` (effecting,
    /// uncacheable) both did real derivation work.
    let isReuse (outcome: CacheOutcome) : bool =
        match outcome with
        | CacheOutcome.Hit
        | CacheOutcome.Incremental _ -> true
        | CacheOutcome.Miss
        | CacheOutcome.Bypass -> false

/// One memoised fragment-apply's worth of telemetry, surfaced to the configured
/// `IFuaranTelemetrySink` from the incremental re-derivation engine. `CacheName`
/// identifies the engine instance (one per logical artifact surface); `Size` /
/// `Capacity` expose the bound so a host can watch eviction pressure.
type CacheStatTelemetry =
    { CacheName: string
      Outcome: CacheOutcome
      Size: int
      Capacity: int
      Timestamp: DateTimeOffset }
