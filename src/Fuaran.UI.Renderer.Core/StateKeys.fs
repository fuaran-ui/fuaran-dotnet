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
[<Literal>]
let HostReservedPrefix = "host."

// `isHostReserved` null-tests a `string` parameter, because a hand-built or
// wire-decoded record can carry a null the type says cannot exist. F# 10's
// nullness checker rejects that test on a non-nullable `string` (FS3261). This
// project declares `<Nullable>disable</Nullable>`, but under Fable that property
// is the ENTRY project's: a nullable-enabled entry transpiles this source with
// nullness ON and the file stops compiling. The file-scoped suppression makes the
// posture travel with the source, matching the precedent in Sanitize.fs +
// Markdown.fs. Do NOT drop the `isNull` guard — it is the contract.
#nowarn "3261"

/// True when `key` names a host-reserved slot (see [[HostReservedPrefix]]).
/// Total on null: an absent key is not "privileged", it is malformed, and that
/// is a different refusal in a different place.
let isHostReserved (key: string) : bool =
    not (isNull key) && key.StartsWith HostReservedPrefix
