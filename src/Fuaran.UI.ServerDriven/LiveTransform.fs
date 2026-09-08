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

/// The bound and the row-identity declaration a store takes when its
/// constructor is given neither. Named rather than repeated: the same two
/// values seed the parameterless `LiveTransformStore()` below and
/// [[LiveTransformOptions.defaults]], and a second literal is a second place
/// for them to disagree.
[<RequireQualifiedAccess>]
module LiveTransformDefaults =

    /// The default bound, in SITES. Generous relative to the number of live
    /// grids one connection renders, and small enough that a session cycling
    /// through many of them cannot grow without limit.
    [<Literal>]
    let Capacity = 64

    /// "no row identity declared" — no column is named, so no row has an
    /// identity and every evaluation runs through the seam's reference path.
    /// Correct on every site, restricting on none.
    [<Literal>]
    let IdentityColumn = ""

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
///
/// Phase 1586 — it is also the estate's implementation of
/// `Fuaran.UI.ILiveTransformStore`, the seam the RENDERER's own
/// `TransformSource.Live` arm consults. That interface takes no identity
/// column, because nothing in a rendered tree declares one and the renderer
/// would have to guess; `identityColumn` is therefore the declaration whoever
/// CONSTRUCTS the store makes on its behalf, once, for every site it serves.
/// The default is the empty string — no column of that name exists, so no row
/// has an identity, and every evaluation runs through the seam's reference path
/// and answers correctly while restricting nothing. A host that wants the
/// saving names its key column; a host that names none loses only the saving.
type LiveTransformStore(capacity: int, identityColumn: string) =
    let states = BoundedLru<IncrementalEval>(capacity)

    /// A store at an explicit bound with no declared row identity — correct on
    /// every site, restricting on none. Preserved at its original arity: the
    /// Phase-1179 call sites construct through it and their behaviour is
    /// unchanged, because the 4-argument `Evaluate` below takes the identity
    /// column per call and never consults this one.
    new(capacity: int) = LiveTransformStore(capacity, "")

    /// The default bound. Generous relative to the number of live grids one
    /// connection renders, and small enough that a session cycling through many
    /// of them cannot grow without limit.
    new() = LiveTransformStore(LiveTransformDefaults.Capacity, LiveTransformDefaults.IdentityColumn)

    /// The row-identity declaration this store applies to the interface-driven
    /// calls that carry none of their own. Empty means "none declared".
    member _.IdentityColumn = identityColumn

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

    // Phase 1586 — the renderer's seam, served by the same method above under
    // the store's own identity declaration. ONE key rule, not two: the `site`
    // string is the key on both paths, and the interface adds no second one.
    // The footprint and the primed/advanced bit are dropped rather than
    // widened onto the seam — a renderer has nowhere to put them, and a seam
    // that carried them would oblige every implementation to mint an account
    // of work it may not have done.
    //
    // A plain comment, not an XML doc: F# attaches no documentation to an
    // interface implementation, and `///` here is diagnostic FS3520 rather than
    // a doc anyone can read. The contract this member serves is documented
    // where it is DECLARED, on `Fuaran.UI.ILiveTransformStore`.
    interface Fuaran.UI.ILiveTransformStore with
        member this.Evaluate(site: string, pipeline: Transform list, source: Table) =
            this.Evaluate(site, identityColumn, pipeline, source) |> Result.map _.Result

/// What a HOST declares, once, about the live-Transform store one of its
/// sessions will hold (Phase 1604).
///
/// Two values and no more, because two is what the store cannot derive for
/// itself. `Capacity` bounds how many SITES one session keeps primed at a time;
/// `IdentityColumn` names the column whose value identifies a row, which is the
/// declaration [[LiveTransformStore]] applies to every seam-driven call — see
/// its own documentation for why row identity is the store's and not the
/// renderer's.
///
/// It is a record rather than two arguments so that a later declaration is a
/// field and not a new arity on every composition site.
type LiveTransformOptions =
    {
        /// How many sites one session keeps primed. At the bound the least
        /// recently used site is evicted, which is correctness-neutral: the next
        /// evaluation of an evicted site re-primes and answers the same table,
        /// having paid for it.
        Capacity: int

        /// The column whose value identifies a row, or the empty string for "none
        /// declared". A store with none declared is CORRECT and simply restricts
        /// nothing, so a host that does not know its key column loses the saving
        /// and never the answer.
        IdentityColumn: string
    }

/// Companion values for [[LiveTransformOptions]].
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LiveTransformOptions =

    /// The bound the parameterless store takes, with no row identity declared —
    /// the shape a host gets by opting in and declaring nothing else.
    let defaults: LiveTransformOptions =
        { Capacity = LiveTransformDefaults.Capacity
          IdentityColumn = LiveTransformDefaults.IdentityColumn }

    /// [[defaults]] with the host's key column named — the one-line opt-in for a
    /// host that knows how its rows are identified.
    let identifiedBy (column: string) : LiveTransformOptions =
        { defaults with
            IdentityColumn = column }

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

    // ── Phase 1604 — the session composition ────────────────────────────────
    //
    //  Phase 1179 built the store and Phase 1586 gave the RENDERER a seam that
    //  consults one, but nothing constructed a store and threaded it through a
    //  session — so `BindingSources.LiveTransforms` was `None` on every path
    //  this tier served, and a live source still paid full evaluation in
    //  practice. This is that wiring, and it is one function on purpose.
    //
    //  ── WHO OWNS THE STORE, AND WHEN IT GOES AWAY ─────────────────────────
    //  The SESSION owns it. `initSession` mints one store per call and it is
    //  reachable from exactly two places: the render closure inside the
    //  session's own `DriverServices`, and the handle returned to the caller.
    //  There is no registry, no static, no `Map<sessionId, store>` — and that
    //  absence is the design rather than an omission, because such a map is
    //  precisely how a per-session cache becomes a cross-tenant one and how it
    //  outlives the session it was minted for. Drop the session and the store
    //  is unreachable with it.
    //
    //  It holds no unmanaged resource, no handle and no thread, so it is not
    //  `IDisposable` and adding that would promise a teardown with nothing to
    //  tear down. What an explicit session-end has is `Clear()`, which releases
    //  the primed tables early and is correctness-neutral. The ONE way to leak
    //  a store is for a caller to keep the returned handle past its session's
    //  end, which is why the handle is returned for OBSERVATION — the bound,
    //  the counts, an early `Clear()` — and never as something to pass to a
    //  second session.
    //
    //  ── WHY IT BUILDS THE WHOLE SESSION ───────────────────────────────────
    //  The smaller function — hand back the store and the render, let the
    //  caller call `Driver.init` — is hoistable, and the sample host in this
    //  repo already hoists its `renderFragment` to a module-level value shared
    //  by every connection. Hoisted, that smaller function gives every session
    //  ONE store: a cross-tenant cache, silently, from a change that looks like
    //  tidying. Building the session is the gesture that cannot be hoisted,
    //  because a session cannot be.
    //
    //  ── WHY THE SOURCES ARE A THUNK ───────────────────────────────────────
    //  Read PER RENDER, never captured — the shape `DriverServices`'
    //  `CorrelationContext` already takes, and for the same reason. Services
    //  are built once per connection while a host's binding sources move with
    //  its state, so a `BindingSources` taken by value here would freeze the
    //  state a session renders against, which is a REGRESSION relative to what
    //  a host can do today by closing over its own cell.
    //
    //  ── WHY THIS TIER SETS THE SLOT, RATHER THAN THE HOST ─────────────────
    //  The host supplies its render as a function OF sources (`Render.render`
    //  already has that shape) and its own sources; the slot is set here, on
    //  every render this session serves. A seam that handed the host a store
    //  and trusted it to put it in the record would be satisfied by a host that
    //  quietly did not, and the failure would be invisible: a correct answer at
    //  full price, forever.

    /// Compose one server-driven session that holds its OWN live-`Transform`
    /// store, and return both.
    ///
    /// `sources` is read once per render; `render` is the host's renderer as a
    /// function of the sources it resolves against (server-side, `Render.render`);
    /// `servicesFrom` is how the host builds its `DriverServices` from a
    /// `renderFragment` — `DriverServices.create`, `DriverServices.createPermissive`,
    /// or its own lambda closing over a dispatch gate. The remaining three
    /// arguments are `Driver.init`'s own.
    ///
    /// The returned store is the session's, for as long as the session lives —
    /// see the note above on ownership. Call this once per session; passing one
    /// session's store to another is not possible through this surface, which is
    /// the point of it.
    let initSession
        (options: LiveTransformOptions)
        (sources: unit -> Fuaran.UI.BindingSources)
        (render: Fuaran.UI.BindingSources -> Fuaran.UI.Types.Node<'Msg> -> string)
        (servicesFrom: (Fuaran.UI.Types.Node<'Msg> -> string) -> Driver.DriverServices<'Msg>)
        (update: 'Msg -> 'Model -> 'Model)
        (view: 'Model -> Fuaran.UI.Types.Node<'Msg>)
        (model: 'Model)
        : Driver.LiveSession<'Model, 'Msg> * LiveTransformStore =

        let store = LiveTransformStore(options.Capacity, options.IdentityColumn)
        let seam = store :> Fuaran.UI.ILiveTransformStore

        let renderFragment (node: Fuaran.UI.Types.Node<'Msg>) : string =
            render
                { sources () with
                    LiveTransforms = Some seam }
                node

        Driver.init (servicesFrom renderFragment) update view model, store
