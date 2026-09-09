module Fuaran.UI.Renderer.StateKeys

// ============================================================================
//  Fuaran — the State-channel key namespace policy (Phase 782).
//
//  The State channel is one flat key namespace shared by the host and by every
//  tree the host renders, and on the browser path it persists into one flat
//  localStorage namespace (`fuaran.state.<key>`). Before Phase 782 a decoded
//  tree's `Action.SetState` could therefore address ANY key the host owned —
//  overwrite it in memory, and overwrite its persisted value across reloads —
//  and a host had no way to say "this slot is mine". The dispatch gate can
//  refuse such a write, but a gate is policy: it is only as good as the policy
//  the host wrote, and the shipped default is not the place to encode which of
//  a host's own key names are sensitive.
//
//  So the namespace is split structurally instead. Keys under
//  `HostReservedPrefix` are HOST-OWNED: every tree-originated write that
//  addresses one is refused, on every path, in every shipped runtime, with no
//  configuration involved and no way for a host to accidentally allow it. A host
//  that wants a State slot no rendered tree can reach names it `host.<whatever>`
//  and is done.
//
//  ── AND A DECLARED LIST BESIDE THE PREFIX (Phase 1550) ─────────────────────
//
//  The prefix closes exactly the keys a host has RENAMED under it, so a slot
//  named before the convention existed keeps its exposure — and renaming a live
//  state key is a migration through every reader, seed and persisted value,
//  which is why the hardening path was documented and not taken. `declareReserved`
//  lets a host name such a slot instead. The prefix rule SEEDS the list rather
//  than being replaced by it, so the structural closure above is untouched for
//  every key that carries the prefix and no host has to declare what it already
//  covers. See `Fuaran.UI.StateKeyPolicy`.
//
//  What this deliberately does NOT do: re-namespace tree writes themselves. The
//  declarative write-back loop (a control writes `Binding.State k`, every reader
//  of `k` re-resolves) depends on tree writes and tree reads naming the same
//  key, and the host merges its own `BindingSources.State` seed under those same
//  names — so prefixing tree writes would break reactivity, or would need an
//  un-prefixing projection at read time that reintroduces the collision it
//  removed. Reserving a namespace the tree cannot address closes the same class
//  from the other side, at a fraction of the blast radius.
//
//  This module lives in the emission-agnostic core (FSharp.Core only) because
//  three separate paths enforce it: the client renderer's `runAction` /
//  write-back, and the bounded server-driven interpreter. One definition, one
//  prefix, no drift. See `SANITIZATION.md`.
// ============================================================================

/// Prefix marking a State key as HOST-OWNED. A tree-originated write —
/// `Action.SetState`, a covered control's write-back default, or a
/// `Call … into State` target — naming a key under this prefix is refused and
/// recorded; only host code writing its store directly can populate it.
//  Phase 932 — the DEFINITION moved down to `Fuaran.UI.StateKeyPolicy` so the
//  pre-emit validator (a lower tier, FSharp.Core-only) reads the same policy
//  rather than restating it. This module re-exports it unchanged: every caller's
//  spelling still works, and there is still exactly one prefix.
[<Literal>]
let HostReservedPrefix = Fuaran.UI.StateKeyPolicy.HostReservedPrefix

// The null-test posture, and the FS3261 suppression it needs under a
// nullable-enabled Fable entry project, moved with the implementation to
// `Fuaran.UI.StateKeyPolicy` — the guard is stated once, where it runs.

/// True when `key` names a host-reserved slot BY PREFIX (see
/// [[HostReservedPrefix]]). Total on null: an absent key is not "privileged",
/// it is malformed, and that is a different refusal in a different place.
///
/// The prefix half of the rule. [[isReserved]] is the whole of it.
let isHostReserved (key: string) : bool =
    Fuaran.UI.StateKeyPolicy.isHostReserved key

//  Phase 1550 — reservation is a declared LIST seeded by the prefix rule, so a
//  host can close a slot whose name predates the convention without renaming it
//  through every reader, seed and persisted value. Re-exported here for the
//  same reason the prefix is: the enforcing paths sit at or above this tier and
//  spell the policy `StateKeys.*`, while the definition lives at the lowest
//  tier any reader occupies (`Fuaran.UI.StateKeyPolicy`) so the pre-emit
//  validator reads the same list rather than a copy of it.

/// Declare state keys as HOST-OWNED by exact name. Additive, idempotent and
/// process-wide; there is no withdrawal. See
/// `Fuaran.UI.StateKeyPolicy.declareReserved`.
let declareReserved (keys: seq<string>) : unit =
    Fuaran.UI.StateKeyPolicy.declareReserved keys

/// The keys declared reserved by exact name (the prefix rule is not an
/// enumerable set — ask [[isReserved]] about a key).
let reservedKeys () : Set<string> =
    Fuaran.UI.StateKeyPolicy.reservedKeys ()

/// True when `key` is host-owned: under [[HostReservedPrefix]], or declared by
/// exact name. THE question a tree-originated write asks.
let isReserved (key: string) : bool = Fuaran.UI.StateKeyPolicy.isReserved key

/// Withdraw every declaration. **Test isolation only** — see
/// `Fuaran.UI.StateKeyPolicy.clearReservedForTests` for why there is no
/// ordinary withdrawal.
let clearReservedForTests () : unit =
    Fuaran.UI.StateKeyPolicy.clearReservedForTests ()
