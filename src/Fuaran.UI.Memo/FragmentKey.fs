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

/// The STRUCTURAL key — a content hash of the fragment body + ref id + slot-
/// argument subtrees (value args excluded; the substituted tree is independent
/// of them). This is the key the substituted-tree cache is keyed on.
///
/// The whole-fragment form: digests the body and composes in one call. A caller
/// applying the same fragment repeatedly should memoise `bodyDigest` and call
/// `structuralOf` instead — that is the entire cost reduction of Phase 210.
let structural (pf: ParamFragment<'Msg>) (refId: string) (slotArgs: Map<string, Node<'Msg>>) : string =
    structuralOf (bodyDigest pf) refId slotArgs

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
