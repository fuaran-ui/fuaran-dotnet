namespace Fuaran.UI.Memo

open System
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.FragmentMemo
open Fuaran.UI.Renderer
open Fuaran.UI.Telemetry.Abstractions

module Introspect = Fuaran.UI.Ops.Introspect
module Projection = Fuaran.UI.Core.Function.Projection

// ============================================================================
//  IncrementalApply — the memoised incremental re-derivation engine (Phase 183).
//
//  A transparent cache + incremental layer over the shipped Phase 180
//  `FragmentApply.apply`. It changes no wire shape and no apply semantics — an
//  application memoised here produces the SAME `FragmentApplication` the bare
//  apply would. What it adds is cheapness:
//
//   - WHOLE-APPLICATION MEMO (`Apply`). The substituted tree is content-keyed
//     (`FragmentKey.structural`) and stored in a bounded LRU. An identical
//     application is a cache HIT — no re-substitution. This is also the path
//     op-stream replay resolves through: replaying a recorded application hits
//     the cache and yields a byte-identical tree (FGP 5 — the both-sinks
//     emission upstream is unaffected; the cache sits purely on the read/derive
//     side).
//
//   - INCREMENTAL RE-DERIVATION (`Reapply`). Given a previous derivation and a
//     new arg-set, the engine exploits the structural fact that the substituted
//     tree depends only on (body, refId, slot args): a VALUE-parameter change
//     leaves the tree untouched, so the prior tree object is reused (reference-
//     identical) and only the hole-addressed (`<refId>.<holeName>`) bindings
//     whose value changed are recomputed. Disjoint value changes don't
//     invalidate each other — an unchanged hole keeps its prior binding object.
//     A SLOT (structural) change falls back to a full `Apply` (the tree genuinely
//     differs). This is the "edit a decision → the chart re-derives" property
//     made concrete and cheap.
//
//   - A KEY THAT COSTS LESS THAN THE WORK IT SAVES (Phase 210). The structural
//     key's body half is memoised per `ParamFragment` reference, so a probe
//     hashes only the ref id + slot args. Before this, a cache HIT paid a
//     whole-tree canonical encode + SHA-256 just to build the key that told it
//     the tree was reusable — for a small fragment, more than the substitution
//     it was avoiding — and a value-only `Reapply` re-hashed the entire tree to
//     establish that nothing structural had changed.
//
//   - THE EFFECT GATE (invariant 3) is Fuaran.Core's (Phase 2308). Admission is
//     `Fuaran.Core.Memo.isMemoisable` over the effect OBSERVED over the
//     fragment's whole subtree joined with every slot argument's: the gate
//     `Fuaran.Core.Function.applyMemo` applies, over `MemoGate.witness`. What a
//     fragment DECLARES does not admit it. A fragment declared pure that nests a
//     network sub-fragment reads the network, so it BYPASSES the store
//     (re-derived every call, never stored) and a host-effecting result is never
//     served stale. The derivation records the verdict (`Admission`).
//
//   - THE REPRESENTABILITY GATE (Phase 1525), after the effect gate and only for
//     an application it admits. The key is a hash of a CANONICAL ENCODING, and that encoding
//     stands a sentinel in for what it cannot represent (`"<closure>"` for a
//     function-typed payload, `"<opaque>"` for an unrecognised CLR value) — so
//     two fragments differing only there key identically, and the store serves
//     one where the other was asked for, closures included. Such an application
//     is never keyed and never stored. Whether it is DERIVED-AND-BYPASSED or
//     REFUSED is the caller's declaration (`StoreReach`): a process-local memo is
//     a private optimisation and quietly declines to optimise; a shared or
//     persisted store is a contract with another session or machine, and its
//     owner is told rather than left with an entry that outlives the process
//     that mis-keyed it.
//
//   - OBSERVABILITY. Every call emits one `CacheStatTelemetry` (hit / miss /
//     incremental / bypass + the cache's size against its bound) to the
//     configured `IFuaranTelemetrySink`.
//
//  THREADING: one `Engine` is single-threaded by contract (the `BoundedLru` it
//  wraps is). The browser authoring loop is single-threaded; a server host that
//  shares an engine across threads serialises access externally.
// ============================================================================

/// How far a fragment store's entries travel beyond the derivation that put
/// them there (Phase 1525). This is the discrimination the unrepresentable-value
/// admission rule turns on, and it is a TYPE rather than a convention because
/// getting it wrong is silent: a store that outlives or escapes its process
/// serves the wrong fragment, and nothing about the wrong fragment says so.
///
/// It is DECLARED by whoever supplies the store and cannot be inferred:
/// `IFragmentStore` is an interface any host may implement, and an adapter over
/// a network cache looks exactly like one over a local dictionary.
[<RequireQualifiedAccess>]
type StoreReach =
    /// The engine owns the store, its entries never leave this process, and it
    /// dies with the engine. An application the key machinery cannot
    /// content-address still DERIVES — it bypasses the store, exactly as an
    /// effecting fragment does — so the optimisation is declined rather than the
    /// work refused. It is NOT served from the store either: a false hit is
    /// possible within one process too (two fragments whose canonical encodings
    /// collide are two fragments, wherever they live), and this is the reason
    /// `ProcessLocal` bypasses rather than stores.
    | ProcessLocal
    /// The store is shared with another engine, another session or another
    /// machine, or persisted for a later one. An application the key machinery
    /// cannot content-address is REFUSED here, not silently un-memoised: the
    /// caller chose to share or persist, so the caller is the party that needs
    /// to learn its fragment cannot travel — and a wrongly-keyed entry written
    /// to such a store outlives the process that wrote it.
    | SharedOrPersisted

/// The memo's admission gate, which is Fuaran.Core's (Phase 2308).
///
/// `Fuaran.Core.Function.applyMemo` admits an application only when
/// `Fuaran.Core.Memo.isMemoisable` holds of the effect OBSERVED over the
/// function's whole subtree (`Function.observedEffect`), joined with the observed
/// effect of every slot argument, which the result contains. The engine keeps its
/// own derivation record (structural key, value-only `Reapply`, `StoreReach`,
/// telemetry), so it does not route through `applyMemo` itself: it applies the
/// same predicate to the same join, over `witness`. The tier's conformance suite
/// runs Core's memo laws over that witness and checks the engine's verdict
/// against `applyMemo`'s.
module MemoGate =

    /// The UI's `ArtifactWitness` for memoisation. A `FragmentDecl` node is the
    /// function; its holes are the shared projection's (`Projection.holesOf`) and
    /// its effect the decl's declared class (`Projection.effectOf`).
    ///
    /// `Tree` is the KEYED traversal (`Introspect.keyedWitness` over
    /// `Introspect.nodeWitness`), the one the effect audit walks, so the observed
    /// effect sees a sub-fragment held in a switch case, an error-boundary arm, a
    /// state alternative or a slot argument, not only in an ordered child list.
    ///
    /// `Bind` is the renderer's `FragmentApply` for one hole. A slot argument is
    /// substituted for its marker, namespaced by the decl's id (`<declId>.<id>`).
    /// A value argument never enters the tree (the host seeds it as the
    /// `<refId>.<hole>` binding), so binding one only closes the hole. Applying a
    /// decl whose id is the ref site's id therefore yields, as its body, the tree
    /// `FragmentApply.apply` yields.
    let witness<'Msg> : Fuaran.Core.ArtifactWitness<Node<'Msg>, NodeId> =
        let bind (addr: string) (arg: Fuaran.Core.Arg<Node<'Msg>>) (node: Node<'Msg>) : Result<Node<'Msg>, string> =
            match node.Kind with
            | NodeKind.FragmentDecl spec ->
                let prefix = node.Id + "."

                if not (addr.StartsWith prefix) then
                    Error(sprintf "hole address '%s' is not under fragment decl '%s'" addr node.Id)
                else
                    let name = addr.Substring prefix.Length
                    let residual = spec.Holes |> List.filter (fun h -> HoleDecl.name h <> name)

                    let closed (body: Node<'Msg>) =
                        { node with
                            Kind =
                                NodeKind.FragmentDecl
                                    { spec with
                                        Body = body
                                        Holes = residual } }

                    match spec.Holes |> List.tryFind (fun h -> HoleDecl.name h = name), arg with
                    | Some(HoleDecl.Slot _ as hole), Fuaran.Core.SlotArg sub ->
                        FragmentApply.apply { spec with Holes = [ hole ] } node.Id Map.empty (Map.ofList [ name, sub ])
                        |> Result.map (fun app -> closed app.Tree)
                    | Some(HoleDecl.Value _ | HoleDecl.Repeat _), Fuaran.Core.ValueArg _ -> Ok(closed spec.Body)
                    | Some _, _ -> Error(sprintf "hole '%s' bound with the wrong kind of argument" addr)
                    | None, _ -> Error(sprintf "no open hole '%s' on fragment decl '%s'" name node.Id)
            | _ -> Error "a fragment binding targets a FragmentDecl node"

        { Tree = Fuaran.Core.Tree.traversal Introspect.nodeWitness Introspect.keyedWitness
          IdW = Introspect.idWitness
          Holes = Projection.holesOf
          Effect = Projection.effectOf
          Bind = bind }

    /// The fragment as the function node `witness` applies: a `FragmentDecl` at
    /// the ref site's id.
    let declNode (refId: string) (pf: ParamFragment<'Msg>) : Node<'Msg> = Fuaran.fragmentDecl refId pf

    /// The effect observed over the fragment's own subtree: its declaration joined
    /// with every nested decl's (`Function.observedEffect`). Constant for a given
    /// fragment, which is why the engine memoises it per fragment reference.
    let fragmentEffect (pf: ParamFragment<'Msg>) : Fuaran.Core.EffectClass =
        Fuaran.Core.Function.observedEffect witness (declNode pf.Name pf)

    /// A fragment's observed effect joined with the observed effect of every slot
    /// argument: the effect `applyMemo` gates an application on.
    let joinSlots (fragment: Fuaran.Core.EffectClass) (slotArgs: Map<string, Node<'Msg>>) : Fuaran.Core.EffectClass =
        (fragment, slotArgs)
        ||> Map.fold (fun acc _ sub -> Fuaran.Core.Effect.join acc (Fuaran.Core.Function.observedEffect witness sub))

    /// The effect one application is gated on.
    let observed (pf: ParamFragment<'Msg>) (slotArgs: Map<string, Node<'Msg>>) : Fuaran.Core.EffectClass =
        joinSlots (fragmentEffect pf) slotArgs

    /// Whether an application with this observed effect may be served from or
    /// stored in the memo: Core's predicate, unchanged.
    let admits (observedEffect: Fuaran.Core.EffectClass) : bool =
        Fuaran.Core.Memo.isMemoisable observedEffect

/// What the engine's gates decided for one application (Phase 2308), so a
/// bypass says why it happened.
[<RequireQualifiedAccess>]
type Admission =
    /// Both gates admitted it: it was served from the store, stored, or reused
    /// incrementally.
    | Admitted
    /// Core's memoisability gate refused it. The effect observed over the
    /// fragment's subtree and its slot arguments (`observed`) is not pure and
    /// deterministic, whatever the fragment declares. Derived, never stored.
    | EffectBypassed of observed: Fuaran.Core.EffectClass
    /// The effect gate admitted it, but its canonical encoding could not be
    /// content-addressed (Phase 1525) and the engine is `ProcessLocal`, so it was
    /// derived and never stored. A `SharedOrPersisted` engine refuses it instead.
    | Unkeyable of unrepresentable: FragmentKey.Unrepresentable list

/// A recorded derivation — the application result plus the keys/args the engine
/// needs to re-derive incrementally against a later arg-set. `ValueArgs` is
/// retained for the binding diff.
type Derivation<'Msg> =
    {
        Result: FragmentApplication<'Msg>
        /// The content address of (body, refId, slot args), or `None` when the
        /// application was not keyed: the effect gate bypassed it (Phase 2308), or
        /// it could not be content-addressed at all (Phase 1525: its canonical
        /// encoding carried an unrepresentable-value sentinel, so a `ProcessLocal`
        /// engine derived it and bypassed the store). `None` never equals a key,
        /// so `Reapply` declines to reuse a tree it cannot prove is the same tree —
        /// which is the behaviour an option forces and a magic string would have
        /// left to a comment.
        StructuralKey: string option
        ValueArgs: Map<string, obj>
        /// Why the application was served, stored or routed around the store
        /// (Phase 2308).
        Admission: Admission
    }

/// Defaults for the engine's key machinery (Phase 210).
module KeyMemoDefaults =

    /// How many distinct `ParamFragment` instances one engine memoises body
    /// digests for. This bounds a DIFFERENT population from the substituted-tree
    /// store's capacity, and a much smaller one: the store is keyed by content,
    /// so it grows once per distinct (fragment, ref site, slot-arg) application,
    /// whereas this grows once per distinct fragment DECLARATION in play on the
    /// surface. Bounded (rather than unbounded-because-small) because the memo
    /// holds a strong reference to every fragment in it, and a bound is what
    /// makes that safe to say. A surface with more live declarations than this
    /// degrades to the pre-210 cost on the overflow — never to a wrong key.
    [<Literal>]
    let bodyDigestCapacity = 64

/// The memoised incremental re-derivation engine. One instance per logical
/// artifact surface (so its cache + telemetry name scope to that surface).
///
/// The substituted-tree cache is a caller-supplied `IFragmentStore<'Msg>`
/// (Phase 360 — the `Fuaran.Core.Function.applyMemo` cache model in the UI tier):
/// the SAME content-hash key (`FragmentKey.structural`) the Phase-183 engine
/// already used, but the store is INJECTED so it can be portable across a
/// session / machine boundary. The shipped `Engine(capacity, sink)` constructor
/// still yields the process-local bounded LRU (`InProcessStore`), preserving the
/// exact pre-360 behaviour; a host after portability injects a
/// `MemoCacheStore` (or any `IFragmentStore`) via `Engine(store, sink)`.
///
/// `sink` receives one `CacheStatTelemetry` per call; `cacheName` (default
/// `"fragment.apply"`) tags those records. The `Capacity` surfaced on a
/// telemetry record is the store's `Count` for an unbounded portable store (it
/// carries no fixed bound).
///
/// `reach` declares how far the store's entries travel (Phase 1525) and decides
/// what happens to an application whose canonical encoding is incomplete — see
/// `StoreReach`. The shipped `Engine(capacity, sink)` constructor builds its own
/// process-local LRU and therefore declares `ProcessLocal` on its own authority.
/// The injected-store constructor `Engine(store, sink)` declares
/// `SharedOrPersisted`, because injection exists to make the store portable and
/// a store of unknown provenance must be assumed to be one; a host injecting a
/// genuinely process-local store says so with `Engine(store, reach, sink)`.
type Engine<'Msg>(store: IFragmentStore<'Msg>, reach: StoreReach, sink: IFuaranTelemetrySink, ?cacheName: string) =
    let cacheName = defaultArg cacheName "fragment.apply"

    // Phase 210 — the IMMUTABLE half of the structural key, memoised by fragment
    // REFERENCE IDENTITY. `pf.Name` + `pf.Body` cannot change for a given
    // `ParamFragment` instance, so their digest is constant across every
    // application of it; without this memo a structural cache HIT still paid a
    // whole-tree canonical encode + SHA-256 merely to build the key that
    // discovered the tree was reusable, and a value-only `Reapply` re-hashed the
    // entire tree to discover that nothing structural had changed.
    //
    // Owned by the ENGINE, deliberately, rather than hidden inside `FragmentKey`:
    // this type already declares a single-threaded contract (see the header) and
    // a bounded lifetime, so the memo inherits both. A module-level static in the
    // key module would be shared across every thread and every engine in the
    // process and would retain every fragment body it ever saw.
    //
    // A fragment instance the memo has not seen (or has evicted) simply costs
    // what it cost before — the digest is recomputed, never guessed.
    //
    // Phase 1525 — the memoised value is the ADMISSION RESULT, not a bare
    // digest. A body whose canonical encoding carries an unrepresentable-value
    // sentinel cannot key anything, and that verdict is as constant per fragment
    // instance as the digest is, so it is memoised beside it rather than
    // recomputed on every probe.
    let bodyDigests =
        BoundedRefMemo<ParamFragment<'Msg>, Result<string, FragmentKey.Unrepresentable list>>(
            KeyMemoDefaults.bodyDigestCapacity
        )

    // Phase 2308 — the effect observed over a fragment's own subtree, memoised by
    // fragment reference beside its digest and for the same reason: it is
    // constant for a given `ParamFragment` instance, and walking the whole body
    // on every probe would put back the per-hit whole-tree cost Phase 210 took
    // out. Only the slot arguments are walked per application.
    let fragmentEffects =
        BoundedRefMemo<ParamFragment<'Msg>, Fuaran.Core.EffectClass>(KeyMemoDefaults.bodyDigestCapacity)

    /// The effect one application is gated on (`MemoGate.observed`), with the
    /// fragment's half served from `fragmentEffects`.
    let observedEffect (pf: ParamFragment<'Msg>) (slotArgs: Map<string, Node<'Msg>>) : Fuaran.Core.EffectClass =
        MemoGate.joinSlots (fragmentEffects.GetOrAdd(pf, MemoGate.fragmentEffect)) slotArgs

    /// The structural key for one application, with the body half served from
    /// `bodyDigests` and only the ref id + slot args hashed per probe. Identical
    /// in value to `FragmentKey.structural` — the memo changes what it costs, not
    /// what it says.
    ///
    /// `Error` means the application cannot be content-addressed at all: the
    /// canonical encoding of the body or of a slot argument carried a sentinel
    /// standing in for a value it could not represent, so this key would not
    /// distinguish this application from a different one (Phase 1525).
    let structuralKey
        (pf: ParamFragment<'Msg>)
        (refId: string)
        (slotArgs: Map<string, Node<'Msg>>)
        : Result<string, FragmentKey.Unrepresentable list> =
        bodyDigests.GetOrAdd(pf, FragmentKey.tryBodyDigest)
        |> Result.bind (fun body -> FragmentKey.tryStructuralOf body refId slotArgs)

    let emit (outcome: CacheOutcome) : unit =
        sink.RecordCacheStat
            { CacheName = cacheName
              Outcome = outcome
              Size = store.Count
              Capacity = store.Capacity
              Timestamp = DateTimeOffset.UtcNow }

    /// The host-seeded value bindings for an application — `<refId>.<holeName>`
    /// keyed, exactly as `FragmentApply.apply` produces them.
    let bindings (refId: string) (valueArgs: Map<string, obj>) : Map<string, obj> =
        valueArgs
        |> Map.toList
        |> List.map (fun (n, v) -> refId + "." + n, v)
        |> Map.ofList

    /// Recompute only the CHANGED hole-address bindings against `prevBindings`,
    /// reusing the prior boxed value object (reference-identical) for every
    /// unchanged hole. Returns the new binding map + the count of addresses
    /// actually recomputed (changed or newly-added). A dropped hole simply does
    /// not appear in `newValueArgs`, so it falls out.
    let incrementalBindings
        (refId: string)
        (prevBindings: Map<string, obj>)
        (newValueArgs: Map<string, obj>)
        : Map<string, obj> * int =
        let mutable recomputed = 0

        let m =
            newValueArgs
            |> Map.toList
            |> List.map (fun (holeName, v) ->
                let addr = refId + "." + holeName

                match Map.tryFind addr prevBindings with
                | Some old when Object.Equals(old, v) -> addr, old // unchanged — reuse, not recomputed
                | _ ->
                    recomputed <- recomputed + 1
                    addr, v)
            |> Map.ofList

        m, recomputed

    /// Apply under an already-observed effect — the body of `Apply`, shared with
    /// `Reapply` so the effect is observed once per call.
    let applyObserved
        (observed: Fuaran.Core.EffectClass)
        (pf: ParamFragment<'Msg>)
        (refId: string)
        (valueArgs: Map<string, obj>)
        (slotArgs: Map<string, Node<'Msg>>)
        : Result<Derivation<'Msg>, string> =
        // Derive without keying, consulting or populating the store, recording
        // the bypass and why — the shape the effect gate and the un-keyable case
        // share.
        let deriveBypassing (why: Admission) : Result<Derivation<'Msg>, string> =
            match FragmentApply.apply pf refId valueArgs slotArgs with
            | Ok app ->
                store.NoteBypass()
                emit CacheOutcome.Bypass

                Ok
                    { Result = app
                      StructuralKey = None
                      ValueArgs = valueArgs
                      Admission = why }
            | Error e -> Error e

        // Serve / store under a real content address — the shipped path.
        let keyed (sk: string) : Result<Derivation<'Msg>, string> =
            match store.TryGet sk with
            | Some tree ->
                // Structural HIT — reuse the cached tree; the value bindings are
                // a pure function of (refId, valueArgs), rebuilt cheaply.
                emit CacheOutcome.Hit

                Ok
                    { Result =
                        { Tree = tree
                          ValueBindings = bindings refId valueArgs }
                      StructuralKey = Some sk
                      ValueArgs = valueArgs
                      Admission = Admission.Admitted }
            | None ->
                match FragmentApply.apply pf refId valueArgs slotArgs with
                | Ok app ->
                    store.Set(sk, app.Tree)
                    emit CacheOutcome.Miss

                    Ok
                        { Result = app
                          StructuralKey = Some sk
                          ValueArgs = valueArgs
                          Admission = Admission.Admitted }
                | Error e -> Error e

        if not (MemoGate.admits observed) then
            // Not memoisable as observed: never key, consult or populate the
            // store, under either reach, as `applyMemo` never keys a bypass.
            deriveBypassing (Admission.EffectBypassed observed)
        else
            match structuralKey pf refId slotArgs with
            | Ok sk -> keyed sk
            | Error unrepresentable ->
                // Un-keyable. The store is neither consulted nor populated under
                // EITHER reach; what differs is whether the caller is told.
                match reach with
                | StoreReach.ProcessLocal -> deriveBypassing (Admission.Unkeyable unrepresentable)
                | StoreReach.SharedOrPersisted -> Error(FragmentKey.describeUnrepresentable unrepresentable)

    /// The shipped constructor — a process-local bounded LRU store (`capacity`
    /// bounds it), preserving the exact pre-360 behaviour. Additive (GP 11).
    /// Declares `ProcessLocal` on its own authority: the engine builds this store
    /// itself, holds the only reference to it, and discards it with itself.
    new(capacity: int, sink: IFuaranTelemetrySink, ?cacheName: string) =
        let s = InProcessStore<'Msg>(capacity) :> IFragmentStore<'Msg>

        match cacheName with
        | Some n -> Engine<'Msg>(s, StoreReach.ProcessLocal, sink, n)
        | None -> Engine<'Msg>(s, StoreReach.ProcessLocal, sink)

    /// The injected-store constructor (Phase 360), which reads as
    /// `SharedOrPersisted` (Phase 1525). Injection exists so a store CAN outlive
    /// or escape the process — that is what `MemoCacheStore.Snapshot` is for — so a
    /// store whose provenance the engine cannot see is assumed to be one of
    /// those. The asymmetry is deliberate: assuming shared costs a declined
    /// optimisation and says so out loud, while assuming local costs a wrong
    /// fragment served silently from someone else's cache.
    ///
    /// A host whose injected store is genuinely process-local says so with
    /// `Engine(store, StoreReach.ProcessLocal, sink)`.
    new(store: IFragmentStore<'Msg>, sink: IFuaranTelemetrySink, ?cacheName: string) =
        match cacheName with
        | Some n -> Engine<'Msg>(store, StoreReach.SharedOrPersisted, sink, n)
        | None -> Engine<'Msg>(store, StoreReach.SharedOrPersisted, sink)

    /// How far this engine's store reaches, and therefore what it does with an
    /// application it cannot content-address (Phase 1525).
    member _.Reach = reach

    /// The configured cache name (one engine = one named cache surface).
    member _.CacheName = cacheName

    /// The caller-supplied content-addressed store (Phase 360) — exposed for
    /// observability (Count / Hits / Misses / Bypasses). Mutating it directly is
    /// not part of the contract. For the default `InProcessStore` this wraps the
    /// pre-360 `BoundedLru`; a portable engine holds a `MemoCacheStore`.
    member _.Store = store

    /// The underlying store's observable size + counters, in the shape the
    /// pre-360 `BoundedLru` exposed (`Count` / `Capacity` / `Hits` / `Misses`) so
    /// existing observability reads keep working. `Capacity` is the store's live
    /// `Count` for an unbounded portable store.
    member _.Cache = store

    /// The body-digest memo (Phase 210) — exposed for observability on the same
    /// footing as `Cache` (`Hits` / `Misses` / `Count` / `Capacity`). A hit here
    /// is one whole-subtree canonical encode + SHA-256 not paid. Its hit rate is
    /// also the regression signal: a change that reverted the engine to hashing
    /// the whole body per probe would show up as this counter going flat.
    /// Mutating it directly is not part of the contract.
    member _.KeyMemo = bodyDigests

    /// The fragment-effect memo (Phase 2308): the effect observed over each
    /// fragment's own subtree, memoised per fragment reference beside
    /// `KeyMemo`. Exposed for observability on the same footing. Mutating it
    /// directly is not part of the contract.
    member _.EffectMemo = fragmentEffects

    /// Apply with whole-application memoisation, served from the caller-supplied
    /// content-addressed store (Phase 360). Admission is Fuaran.Core's gate
    /// (Phase 2308, `MemoGate`): `Fuaran.Core.Memo.isMemoisable` over the effect
    /// observed over the fragment's subtree joined with every slot argument's,
    /// exactly as `Fuaran.Core.Function.applyMemo` gates. An admitted
    /// application's substituted tree is content-keyed (`FragmentKey.structural`)
    /// and cached in the store (HIT on an identical application, MISS + store
    /// otherwise). An application whose observed effect is not pure and
    /// deterministic BYPASSES the store, re-derived every call and never stored,
    /// even when the fragment declares itself pure (Fork 3). Returns the
    /// derivation, whose `Admission` says which, or the bare apply's error
    /// verbatim (validation / totality failures are NOT cached).
    ///
    /// A SECOND admission gate follows the effect gate (Phase 1525). Where the
    /// canonical encoding of the body or of a slot argument carries an
    /// unrepresentable-value sentinel, the key does not distinguish this
    /// application from a different one, so serving from the store would serve
    /// the WRONG fragment — the cached subtree carries the first application's
    /// closures. What happens then is `StoreReach`'s to decide: `ProcessLocal`
    /// derives and bypasses the store, `SharedOrPersisted` refuses with a message
    /// naming what was unrepresentable. Neither ever stores under such a key. An
    /// application the effect gate bypasses is never keyed, so it reaches
    /// neither outcome.
    member _.Apply
        (pf: ParamFragment<'Msg>, refId: string, valueArgs: Map<string, obj>, slotArgs: Map<string, Node<'Msg>>)
        : Result<Derivation<'Msg>, string> =
        applyObserved (observedEffect pf slotArgs) pf refId valueArgs slotArgs

    /// Incrementally re-derive against a previous derivation. When only value
    /// parameters changed (the structural key is unchanged), the prior tree
    /// object is reused and ONLY the changed hole-address bindings are
    /// recomputed (emits `Incremental recomputed`). When a slot argument changed
    /// (the structural key differs), falls back to a full `Apply`. An application
    /// the effect gate bypasses (Phase 2308: its OBSERVED effect, slot arguments
    /// included) always full-re-derives, never reused.
    member _.Reapply
        (
            prev: Derivation<'Msg>,
            pf: ParamFragment<'Msg>,
            refId: string,
            newValueArgs: Map<string, obj>,
            newSlotArgs: Map<string, Node<'Msg>>
        ) : Result<Derivation<'Msg>, string> =
        let observed = observedEffect pf newSlotArgs

        if not (MemoGate.admits observed) then
            applyObserved observed pf refId newValueArgs newSlotArgs
        else
            // Phase 210 — the body digest is served from the engine's memo, so a
            // value-only re-derive hashes only the (small) slot-arg portion to
            // establish that the structural key is unchanged.
            // An un-keyable application (Phase 1525) yields `Error` here and
            // `None` on `prev`, and neither is ever equal to a content address —
            // so the incremental path is taken only where a real key proves the
            // tree is the same tree, and every other case falls through to a full
            // `Apply`, which applies the reach rule.
            let sk =
                match structuralKey pf refId newSlotArgs with
                | Ok k -> Some k
                | Error _ -> None

            if sk.IsSome && sk = prev.StructuralKey then
                // Structural tree unchanged → reuse it; recompute only the
                // changed hole-address bindings.
                let newBindings, recomputed =
                    incrementalBindings refId prev.Result.ValueBindings newValueArgs

                emit (CacheOutcome.Incremental recomputed)

                Ok
                    { Result =
                        { prev.Result with
                            ValueBindings = newBindings }
                      StructuralKey = sk
                      ValueArgs = newValueArgs
                      Admission = Admission.Admitted }
            else
                // Slot / structural change — full re-derive (and refresh the cache).
                applyObserved observed pf refId newValueArgs newSlotArgs
