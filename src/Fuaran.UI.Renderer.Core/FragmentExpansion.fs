module Fuaran.UI.Renderer.FragmentExpansion

open Fuaran.UI.Types
open Fuaran.UI.FragmentMemo

// ============================================================================
//  FragmentExpansion — the fragment-expansion memo and its lifetime owner.
//
//  A `FragmentRef` expands by deep-copying the referenced fragment body with
//  every interior `NodeId` rewritten under the ref's own id. That copy is a
//  whole-subtree walk, and it is paid again on every render of every ref — the
//  render-side twin of the memoisation the apply engine already has.
//
//  WHAT THIS CACHES, AND WHERE THE PREVIOUS ATTEMPT WENT WRONG. Caching the
//  expansion per `(fragment NAME, prefix)` on the per-render context is unsound
//  AND ineffective, and both halves matter:
//
//    * INEFFECTIVE — `prefix` is the REF's own node id, so within one render
//      pass every key is already distinct and a per-render cache can never hit.
//    * UNSOUND — the fragment NAME is not the input to the expansion. A
//      different tree may declare a different body under the same name (the
//      registry is first-decl-wins PER TREE), so a name-keyed cache serves one
//      tree's body to another tree's ref.
//
//  So the key here is the BODY INSTANCE plus the prefix. The body is immutable
//  and, with the prefix, is the whole input to the expansion — a different body
//  is a different key BY CONSTRUCTION rather than by a check anyone has to
//  remember. Identity rather than a content digest, because digesting the body
//  costs exactly the subtree walk the memo exists to avoid (the argument
//  `BoundedRefMemo`'s own header makes).
//
//  And because the key is an instance rather than a name, the cache is free to
//  outlive any one render — which is what makes it hit at all. It hits ACROSS
//  renders: a tree decoded from the wire and held in state is re-rendered on
//  every state change against the SAME body instance. Where a host instead
//  rebuilds its tree from a view function each render, the instance changes and
//  the memo misses at the cost of a bounded pointer scan.
//
//  LIFETIME. One process-global cache, owned here — the ownership class the
//  renderer's `StateStore` default store, `ChangeHub` and `Affordances` already
//  occupy. Not `RenderContext` (built fresh at every entry point, so it could
//  never hit, and widening a public record is a breaking change), and not
//  `IFuaranRuntime` (a published host interface; adding a member breaks every
//  implementation, and each host would then own a policy the renderer is better
//  placed to decide once). Deliberately NOT scope-keyed the way `StateStore` is:
//  that store needs `forScope` because its keys are STRINGS a guest and a host
//  can both spell, whereas reference identity cannot collide across scopes at
//  all.
//
//  BOUND. Two levels, each an LRU: 64 distinct bodies, 32 prefixes per body, so
//  at most 2048 namespaced bodies are retained and the strong references the
//  memo holds to bodies are bounded too. Entries rather than bytes — a portable
//  byte estimate of an `'Msg`-carrying tree does not exist under Fable, and a
//  wrong one is worse than a coarse count.
//
//  CONCURRENCY. On .NET every probe and every store runs under a private
//  monitor; the COMPUTE runs outside it, so the critical section is a pointer
//  scan and a dictionary probe rather than a subtree walk. Two threads racing
//  one key both compute and the later store wins — the results are structurally
//  identical because the expansion is a pure function of `(prefix, body)`, so a
//  race costs one recomputation and can neither corrupt the store nor serve a
//  wrong answer. Under Fable the guard compiles out: `lock` is not portable
//  there and the browser is one event loop. That single `#if` is the ENTIRE
//  divergence between the two pipelines, and it is not on the result path —
//  which is what makes "identical output with and without the cache" true on
//  both by construction. Deliberately not `ConcurrentDictionary` (.NET-only,
//  would fork the implementation) and not `[<ThreadStatic>]` (would silently
//  disable the memo on a host that renders on a thread-pool thread per request
//  — precisely the host that benefits most).
// ============================================================================

/// Run `f` under the cache's mutual exclusion. On .NET that is a monitor on the
/// owning cache's private sync root; under Fable there is one event loop and
/// `lock` is not portable, so the guard is the identity.
#if FABLE_COMPILER
let inline private guarded (_syncRoot: obj) (f: unit -> 'T) : 'T = f ()
#else
let inline private guarded (syncRoot: obj) (f: unit -> 'T) : 'T = lock syncRoot f
#endif

/// Default bound: distinct fragment BODY INSTANCES retained.
let defaultBodyCapacity = 64

/// Default bound: distinct prefixes retained per body.
let defaultPrefixCapacity = 32

/// A bounded, lifetime-owning memo of namespaced fragment bodies, keyed by
/// `(body instance, prefix)`.
///
/// Values are held as `obj` because the cache is process-global and the tree it
/// caches is generic in `'Msg`. The unbox on the hit path is safe by
/// construction, not by convention: the value under a key was produced by
/// applying the caller's own `compute` to THAT key, so it has the key's type.
/// The same object cannot be a `Node<int>` and a `Node<string>`, and on the
/// Fable pipeline generics are erased so the cast is not emitted at all.
/// Boxing a `Node<'Msg>` allocates nothing — it is a record, so `box` is a cast.
type FragmentExpansionCache(bodyCapacity: int, prefixCapacity: int) =
    let prefixCapacity = max 1 prefixCapacity
    let bodies = BoundedRefMemo<obj, BoundedLru<obj>>(bodyCapacity)
    let syncRoot = obj ()
    let mutable hits = 0
    let mutable misses = 0

    /// The prefix slot for a body, minted on first sight. Callers hold the lock.
    let slotFor (body: obj) : BoundedLru<obj> =
        bodies.GetOrAdd(body, (fun _ -> BoundedLru<obj>(prefixCapacity)))

    /// The configured bound on distinct body instances.
    member _.BodyCapacity = bodies.Capacity

    /// The configured bound on distinct prefixes per body.
    member _.PrefixCapacity = prefixCapacity

    /// Distinct body instances currently retained.
    member _.Count = bodies.Count

    /// Cumulative expansion hits since construction (or the last `Clear`).
    member _.Hits = hits

    /// Cumulative expansion misses since construction (or the last `Clear`).
    member _.Misses = misses

    /// The namespaced body for `(body, prefix)`, computing it with `compute` on
    /// a miss and storing the result.
    ///
    /// `compute` MUST be a pure function of the body it is handed (the renderer
    /// passes the id-rewriting walk, which is), because a hit skips it entirely
    /// and a concurrent miss may run it twice.
    member _.Expand(body: Node<'Msg>, prefix: string, compute: Node<'Msg> -> Node<'Msg>) : Node<'Msg> =
        let key = box body

        let cached =
            guarded syncRoot (fun () ->
                let slot = slotFor key

                match slot.TryGet prefix with
                | Some v ->
                    hits <- hits + 1
                    Some v
                | None ->
                    misses <- misses + 1
                    None)

        match cached with
        | Some v -> unbox<Node<'Msg>> v
        | None ->
            // Outside the lock, deliberately — see the CONCURRENCY note above.
            let expanded = compute body
            guarded syncRoot (fun () -> (slotFor key).Set(prefix, box expanded))
            expanded

    /// Drop every entry and reset the hit/miss counters.
    member _.Clear() : unit =
        guarded syncRoot (fun () ->
            bodies.Clear()
            hits <- 0
            misses <- 0)

/// The process-global expansion cache — the seam that outlives renders. A host
/// borrows it ambiently through `expand`; nothing is threaded through
/// `RenderContext`.
let private defaultCache =
    FragmentExpansionCache(defaultBodyCapacity, defaultPrefixCapacity)

/// The named expansion primitive: the namespaced form of `body` under `prefix`,
/// computed by `compute` on a miss and served from the process-global cache on a
/// hit.
let expand (body: Node<'Msg>) (prefix: string) (compute: Node<'Msg> -> Node<'Msg>) : Node<'Msg> =
    defaultCache.Expand(body, prefix, compute)

/// Drop every cached expansion. A host that wants the memory back calls this;
/// the bound means it never has to.
let clear () : unit = defaultCache.Clear()

/// `(hits, misses)` on the process-global cache since construction or the last
/// `clear`.
let stats () : int * int = defaultCache.Hits, defaultCache.Misses

/// Distinct fragment body instances the process-global cache currently retains.
let count () : int = defaultCache.Count
