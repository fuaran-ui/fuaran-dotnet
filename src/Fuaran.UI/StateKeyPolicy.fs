module Fuaran.UI.StateKeyPolicy

// ============================================================================
//  The State-channel key NAMESPACE POLICY — one definition, for every tier.
//
//  Phase 782 reserved a prefix of the flat State namespace to the host: every
//  tree-originated write addressing a key under it is refused, on every path, in
//  every shipped runtime, with no configuration involved. That policy was
//  defined in `Fuaran.UI.Renderer.Core/StateKeys.fs` because the three ENFORCING
//  paths (the client renderer's `runAction` and write-back, and the bounded
//  server-driven interpreter) all sit at or above that tier.
//
//  Phase 932 added a fourth reader at a LOWER tier: the pre-emit validator, which
//  exempts host-reserved keys from FUARAN098 (a write the runtime refuses
//  outright is "unaddressable", not "unread" — a different finding, in a
//  different place). `Fuaran.UI` is FSharp.Core-only and cannot reference the
//  renderer core, so the definition moves DOWN to the lowest tier that any reader
//  occupies and `Renderer.StateKeys` re-exports it unchanged.
//
//  The alternative — a second copy of the prefix beside the validator — is
//  exactly the drift the original module's "one definition, one prefix" note
//  exists to refuse, and it would be a copy in the one place whose job is to
//  reason ABOUT the policy. Moving the definition keeps every caller's spelling
//  (`StateKeys.HostReservedPrefix` / `StateKeys.isHostReserved`) working.
//
//  See `SANITIZATION.md` and `Fuaran.UI.Renderer.Core/StateKeys.fs`.
// ============================================================================

/// Prefix marking a State key as HOST-OWNED. A tree-originated write —
/// `Action.SetState`, a covered control's write-back default, or a
/// `Call … into State` target — naming a key under this prefix is refused and
/// recorded; only host code writing its store directly can populate it.
[<Literal>]
let HostReservedPrefix = "host."

// `isHostReserved` null-tests a `string` parameter, because a hand-built or
// wire-decoded record can carry a null the type says cannot exist. F# 10's
// nullness checker rejects that test on a non-nullable `string` (FS3261). The
// file-scoped suppression makes the posture travel with the source, matching the
// precedent this definition was moved from. Do NOT drop the `isNull` guard — it
// is the contract.
#nowarn "3261"

/// True when `key` names a host-reserved slot (see [[HostReservedPrefix]]).
/// Total on null: an absent key is not "privileged", it is malformed, and that
/// is a different refusal in a different place.
let isHostReserved (key: string) : bool =
    not (isNull key) && key.StartsWith HostReservedPrefix
