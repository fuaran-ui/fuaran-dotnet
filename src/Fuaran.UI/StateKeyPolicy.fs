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

/// True when `key` names a host-reserved slot BY PREFIX (see
/// [[HostReservedPrefix]]). Total on null: an absent key is not "privileged",
/// it is malformed, and that is a different refusal in a different place.
///
/// This is the PREFIX rule alone. [[isReserved]] is the whole rule — the prefix
/// plus whatever the host has declared by exact name — and is what an
/// enforcement path consults.
let isHostReserved (key: string) : bool =
    not (isNull key) && key.StartsWith HostReservedPrefix

// ============================================================================
//  RESERVATION BECOMES A DECLARED LIST (Phase 1550).
//
//  The prefix rule closes exactly the keys a host has RENAMED under `host.`, so
//  a host-owned key that predates the convention keeps its exposure until
//  somebody renames it — and renaming a live state key is a migration through
//  every reader, every persisted value and every seed that names it. The
//  hardening path was therefore a rename, which is why in practice it was not
//  taken.
//
//  A host now names the slot instead: `declareReserved [ "sessionToken" ]` and
//  every tree-originated write to it is refused on the same path, with the same
//  record, that has refused `host.*` since Phase 782.
//
//  THE PREFIX RULE SEEDS THE LIST rather than being replaced by it. `isReserved`
//  is `isHostReserved key || declared`, so nothing a host has today stops being
//  reserved, no host has to declare what the prefix already covers, and the
//  structural closure — no configuration involved, no way to accidentally allow
//  it — survives untouched for every key that carries the prefix. What the list
//  adds is reach, not a replacement mechanism.
//
//  PROCESS-GLOBAL, like the prefix it extends and like `CustomHash`'s floor, and
//  for the same reason: the enforcement point is a tree-originated write, which
//  holds no store handle and no host object. It is deliberately NOT per
//  `StateStoreInstance` — a scoped store declaring policy that binds the
//  bounded server-driven interpreter would be a surprise, and the two are
//  different questions (WHICH STORE a key lives in versus WHETHER A TREE MAY
//  ADDRESS IT AT ALL).
//
//  ADDITIVE AND IDEMPOTENT, with no withdrawal. There is no `undeclareReserved`
//  and there must not be: a function that un-reserves is the same fail-open
//  hazard as a floor setter that lowers, and it would be reachable from any
//  assembly loaded into the process. A host states its set at startup, or names
//  keys as the surfaces that own them are composed.
// ============================================================================

let mutable private declaredReserved: Set<string> = Set.empty

/// Declare state keys as HOST-OWNED by exact name (Phase 1550). Additive,
/// idempotent, and process-wide; there is no way to withdraw a declaration.
///
/// A null or empty key is ignored rather than stored: neither can be written by
/// a tree-originated write that reaches the refusal path, so storing one would
/// only make [[reservedKeys]] misreport what is reserved.
let declareReserved (keys: seq<string>) : unit =
    for k in keys do
        if not (isNull k) && k <> "" then
            declaredReserved <- Set.add k declaredReserved

/// The keys declared reserved by exact name. Does NOT include the prefix rule,
/// which is not an enumerable set — [[isReserved]] is the question to ask about
/// a key; this is for a host that wants to show or check its own declaration,
/// and for the validator, which reasons about whether the host has adopted the
/// list at all.
let reservedKeys () : Set<string> = declaredReserved

/// True when `key` is host-owned: under [[HostReservedPrefix]], or declared by
/// exact name through [[declareReserved]]. THE question an enforcement path
/// asks — a tree-originated write naming such a key is refused and recorded.
let isReserved (key: string) : bool =
    isHostReserved key || (not (isNull key) && declaredReserved.Contains key)

/// The keys declared by exact name that the prefix rule does NOT already cover
/// — a host's PRE-CONVENTION slots, the ones that had no protection at all
/// before Phase 1550.
///
/// It is what the validator reasons from: a non-empty set means this host has
/// adopted the declared list for legacy-named slots, so a warning about an
/// undeclared key of that shape is actionable. An empty set means either that
/// the host declared nothing or that everything it declared was already covered
/// by the prefix, and in both cases the validator has nothing useful to say.
let preConventionReservedKeys () : Set<string> =
    declaredReserved |> Set.filter (isHostReserved >> not)

/// Withdraw every declaration, restoring the prefix rule alone.
///
/// **Test isolation only, and the name says so** — the posture
/// `CustomHash.clearCustomHashFloorForTests` takes, for the same reason and with
/// one difference worth stating. That one restores the SHIPPED DEFAULT, so it
/// cannot loosen anything; this one genuinely un-reserves, which is why
/// `declareReserved` has no paired withdrawal on the ordinary surface and why
/// this carries the fence in its name. A host does not stop owning its own
/// slots, and an `undeclareReserved` reachable from any assembly in the process
/// would be the fail-open shape the reservation exists to remove.
///
/// The suite needs it because the declaration is process-global and additive: a
/// case that declares a pre-convention key would otherwise change what the
/// pre-emit validator reports for every case that runs after it, in a
/// runner-order-dependent way. A suite that declares restores through this.
let clearReservedForTests () : unit = declaredReserved <- Set.empty
