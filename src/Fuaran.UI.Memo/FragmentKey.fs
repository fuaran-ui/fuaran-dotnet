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

/// The STRUCTURAL key — a content hash of the fragment body + ref id + slot-
/// argument subtrees (value args excluded; the substituted tree is independent
/// of them). Slots are sorted by name so binding order is irrelevant. This is
/// the key the substituted-tree cache is keyed on.
let structural (pf: ParamFragment<'Msg>) (refId: string) (slotArgs: Map<string, Node<'Msg>>) : string =
    let (FragmentId name) = pf.Name
    let body = CanonicalJson.encodeNode pf.Body

    let slots =
        slotArgs
        |> Map.toList
        |> List.sortBy fst
        |> List.map (fun (k, v) -> k + "=" + CanonicalJson.encodeNode v)
        |> String.concat "|"

    Hashing.sha256Hex (sprintf "fuaran-fragment-apply:v1\nname=%s\nrefId=%s\nbody=%s\nslots=%s" name refId body slots)

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
