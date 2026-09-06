module Fuaran.UI.Memo.FragmentKey

open System.Globalization
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  FragmentKey — the stable (function-identity, arg-set) cache key (Phase 183,
//  task 1).
//
//  Reuses the op-stream's canonical-JSON encoder (`CanonicalJson.encodeNode`)
//  for the fragment body + slot-argument subtrees, then the Fable-safe SHA-256
//  (`Hashing.sha256Hex`) over the assembled canonical string — the SAME digest
//  the op-stream hash-chain produces server-side. No second content hash is
//  invented; the key is a deterministic function of the canonical wire shape, so
//  two structurally-identical applications hash identically across machines and
//  runtimes, and op-stream replay re-derives the same key (and thus the same
//  cached tree).
//
//  TWO-LAYER KEYING — the substituted tree depends ONLY on (body, refId, slot
//  args); value arguments never enter the tree (they seed the host-side value
//  bindings keyed `<refId>.<holeName>`). So the STRUCTURAL key omits value args
//  (a value-parameter change is a structural HIT — the tree is reused), and the
//  VALUE key fingerprints them separately for the binding diff. `full` combines
//  both for whole-application identity.
//
//  THE STRUCTURAL KEY IS ITSELF TWO PARTS (Phase 210). The fragment's name +
//  body are IMMUTABLE for a given `ParamFragment`, so their digest is constant
//  across every application of it; the ref id + slot args vary per application.
//  `bodyDigest` computes the constant half and `structuralOf` composes it with
//  the varying half, so a caller that memoises the first pays only the second
//  per probe. `structural` remains the whole-fragment form for a caller with
//  nowhere to keep the digest.
// ============================================================================

// ============================================================================
//  UNREPRESENTABLE VALUES AND THE KEY (Phase 1525).
//
//  Every key below is a hash of a CANONICAL ENCODING, and that encoding is
//  lossy in two named places: `CanonicalJson` renders a function-typed payload
//  (an `Action` callback, a `Column.Value` projection, an `onChange` handler)
//  as the sentinel `"<closure>"`, and an obj-typed value of an unrecognised CLR
//  type as `"<opaque>"`. Where a sentinel appears, the encoder is SAYING that it
//  could not represent part of the value — so two fragment bodies that differ
//  ONLY there encode identically, and hash identically, and therefore key
//  identically.
//
//  A content-addressed store keyed that way does not merely fail to distinguish
//  them; it SERVES one where the other was asked for. The cached subtree carries
//  the first application's closures, so a button dispatches the wrong message
//  and a column formats with the wrong projection — a wrong answer, not a missed
//  optimisation. Persist or share that store and the wrong fragment outlives the
//  process that mis-keyed it, in a store whose whole premise is that a key
//  computed on one machine means the same thing on another.
//
//  So the encoding is INSPECTED before it is allowed to key anything: the
//  `try…` functions below return the same key values as their total siblings
//  when the encoding is complete, and a typed `Unrepresentable list` naming the
//  site and the sentinel when it is not. What to DO about that is the store's
//  question, not the key's — see `StoreReach` in `IncrementalApply.fs`.
//
//  The test is deliberately CONSERVATIVE: it looks for the sentinel as a whole
//  JSON string token, so a fragment whose own text content is literally
//  `<closure>` is reported as unrepresentable when it is merely unlucky. That
//  direction of error costs a cache entry; the other direction costs
//  correctness.
// ============================================================================

/// One thing a canonical encoding could not represent.
type Unrepresentable =
    {
        /// Where it appeared — `"body"`, or `"slot '<name>'"`.
        Site: string
        /// The sentinel the encoder emitted in its place (`"<closure>"` /
        /// `"<opaque>"`).
        Sentinel: string
    }

/// The sentinel JSON tokens `CanonicalJson` emits in place of a value it cannot
/// represent. Whole tokens, quotes included — see the conservativeness note in
/// the header.
let private sentinelTokens =
    [ "<closure>", "\"<closure>\""; "<opaque>", "\"<opaque>\"" ]

/// The unrepresentable values in one canonical encoding, attributed to `site`.
/// Empty means the encoding is complete and safe to key on.
let unrepresentableIn (site: string) (canonical: string) : Unrepresentable list =
    sentinelTokens
    |> List.filter (fun (_, token) -> canonical.Contains token)
    |> List.map (fun (sentinel, _) -> { Site = site; Sentinel = sentinel })

/// Render a refusal for a caller whose error channel is a string (this tier's
/// `Result<_, string>` idiom). Names every site and sentinel — the caller needs
/// to know WHICH part of the application could not be represented, because the
/// remedy differs: a closure in the body is a fragment that cannot be shared
/// across a boundary at all, while one in a slot argument is a call site that
/// can pass a wire-survivable subtree instead.
let describeUnrepresentable (us: Unrepresentable list) : string =
    let sites =
        us
        |> List.map (fun u -> u.Site + " contains " + u.Sentinel)
        |> String.concat "; "

    "Fuaran.UI.Memo: refusing to key a fragment application whose canonical encoding is incomplete — "
    + sites
    + ". Two applications differing only there encode identically, so a shared or persisted store would "
    + "serve one where the other was asked for."

/// Canonical token for a boxed value argument. Self-describing by CLR shape so
/// `i:3` (int) and `s:3` (string) never collide. Mirrors the `CanonicalJson`
/// `encodeScalar` vocabulary (Int / Float / Bool / Str) — key material only, all
/// of which feeds the single `Hashing.sha256Hex`.
let private scalarToken (v: obj) : string =
    match v with
    | :? int as n -> "i:" + string n
    | :? float as f -> "f:" + f.ToString("R", CultureInfo.InvariantCulture)
    | :? bool as b -> "b:" + (if b then "1" else "0")
    | :? string as s -> "s:" + s
    | _ -> "o:opaque"

/// The BODY digest — a content hash of the fragment's IMMUTABLE half: its name
/// and its body subtree, the two things that do not vary across applications of
/// one `ParamFragment`. Split out of `structural` (Phase 210) precisely so a
/// caller holding a stable fragment can compute it ONCE and reuse it: the body
/// never changes for a given `pf`, so re-encoding and re-hashing the whole
/// subtree on every probe was pure waste — a cache HIT paid a full-tree encode +
/// SHA just to discover the tree could be reused.
///
/// Pure and stateless by design. The memoisation lives with whoever owns a
/// lifetime and a concurrency contract (the engine memoises it per fragment
/// reference); a cache hidden inside this module would be a process-global
/// shared across every thread and every engine, retaining every fragment body it
/// ever saw.
///
/// Name is folded in here rather than at the composition step so that two
/// differently-named declarations sharing one body object still key apart.
let bodyDigest (pf: ParamFragment<'Msg>) : string =
    Hashing.sha256Hex (sprintf "fuaran-fragment-body:v1\nname=%s\nbody=%s" pf.Name (CanonicalJson.encodeNode pf.Body))

/// `bodyDigest`, refused when the body's canonical encoding could not represent
/// part of it (Phase 1525). The `Ok` value is byte-identical to `bodyDigest`'s —
/// this adds an admission test, never a different key — and the `Error` names
/// what the encoder could not carry. See the header for why an incomplete
/// encoding must not become a content address.
let tryBodyDigest (pf: ParamFragment<'Msg>) : Result<string, Unrepresentable list> =
    let canonical = CanonicalJson.encodeNode pf.Body

    match unrepresentableIn "body" canonical with
    | [] -> Ok(Hashing.sha256Hex (sprintf "fuaran-fragment-body:v1\nname=%s\nbody=%s" pf.Name canonical))
    | us -> Error us

/// The STRUCTURAL key composed from a PRECOMPUTED body digest — a content hash
/// of (body digest ⊕ ref id ⊕ slot-argument subtrees). Value args are excluded;
/// the substituted tree is independent of them. Slots are sorted by name so
/// binding order is irrelevant.
///
/// Only the slot arguments are encoded here, so a caller re-keying against a
/// fragment it has already digested pays the (small) slot-arg portion and
/// nothing else — which is what makes a value-only `Reapply` cheap.
///
/// The key remains a deterministic, machine-INDEPENDENT function of its inputs
/// (the portable-store property: a store populated on one machine is a hit on
/// another), and still discriminates exactly the same `(body, refId, slot-args)`
/// tuples the pre-210 single-pass hash did — SHA-256 collision resistance is
/// what carries the body identity through its digest. The `:v2` tag records that
/// the COMPOSITION changed: a store snapshot persisted by a pre-210 build keys
/// its entries differently, so it misses rather than mis-hits.
let structuralOf (body: string) (refId: string) (slotArgs: Map<string, Node<'Msg>>) : string =
    let slots =
        slotArgs
        |> Map.toList
        |> List.sortBy fst
        |> List.map (fun (k, v) -> k + "=" + CanonicalJson.encodeNode v)
        |> String.concat "|"

    Hashing.sha256Hex (sprintf "fuaran-fragment-apply:v2\nbody=%s\nrefId=%s\nslots=%s" body refId slots)

/// `structuralOf`, refused when a SLOT ARGUMENT's canonical encoding could not
/// represent part of it (Phase 1525). The `Ok` value is byte-identical to
/// `structuralOf`'s. Slot arguments get the same admission test the body does,
/// and for the same reason: they are substituted INTO the cached tree, so two
/// call sites passing closure-bearing subtrees that differ only in their
/// closures would share one cached tree and one set of handlers.
///
/// Each slot is encoded exactly once here — the check reads the same string the
/// key is built from, so an encoding can never be admitted by one and hashed by
/// the other.
let tryStructuralOf
    (body: string)
    (refId: string)
    (slotArgs: Map<string, Node<'Msg>>)
    : Result<string, Unrepresentable list> =
    let encoded =
        slotArgs
        |> Map.toList
        |> List.sortBy fst
        |> List.map (fun (k, v) -> k, CanonicalJson.encodeNode v)

    let unrepresentable =
        encoded |> List.collect (fun (k, e) -> unrepresentableIn ("slot '" + k + "'") e)

    match unrepresentable with
    | [] ->
        let slots = encoded |> List.map (fun (k, e) -> k + "=" + e) |> String.concat "|"

        Ok(Hashing.sha256Hex (sprintf "fuaran-fragment-apply:v2\nbody=%s\nrefId=%s\nslots=%s" body refId slots))
    | us -> Error us

/// The STRUCTURAL key — a content hash of the fragment body + ref id + slot-
/// argument subtrees (value args excluded; the substituted tree is independent
/// of them). This is the key the substituted-tree cache is keyed on.
///
/// The whole-fragment form: digests the body and composes in one call. A caller
/// applying the same fragment repeatedly should memoise `bodyDigest` and call
/// `structuralOf` instead — that is the entire cost reduction of Phase 210.
let structural (pf: ParamFragment<'Msg>) (refId: string) (slotArgs: Map<string, Node<'Msg>>) : string =
    structuralOf (bodyDigest pf) refId slotArgs

/// `structural`, refused when the body OR any slot argument could not be
/// canonically represented (Phase 1525). The whole-fragment admission test — the
/// one a caller with nowhere to keep a body digest wants. Body first, so the
/// refusal names the more fundamental site when both fail.
let tryStructural
    (pf: ParamFragment<'Msg>)
    (refId: string)
    (slotArgs: Map<string, Node<'Msg>>)
    : Result<string, Unrepresentable list> =
    tryBodyDigest pf
    |> Result.bind (fun body -> tryStructuralOf body refId slotArgs)

/// The VALUE-args fingerprint — the per-application value-binding identity, used
/// to detect which hole-addressed bindings changed on an incremental re-derive.
/// Sorted by hole name; deterministic.
let value (valueArgs: Map<string, obj>) : string =
    valueArgs
    |> Map.toList
    |> List.sortBy fst
    |> List.map (fun (k, v) -> k + "=" + scalarToken v)
    |> String.concat "|"

/// The FULL whole-application key — structural identity ⊕ value identity. Two
/// applications share this key iff they would produce a byte-identical tree AND
/// identical value bindings.
let full
    (pf: ParamFragment<'Msg>)
    (refId: string)
    (valueArgs: Map<string, obj>)
    (slotArgs: Map<string, Node<'Msg>>)
    : string =
    structural pf refId slotArgs + "#" + value valueArgs
