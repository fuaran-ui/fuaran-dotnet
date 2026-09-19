module Fuaran.UI.Renderer.RuntimeHatches

// ============================================================================
//  Phase 1743 — the RUNTIME half of the escape-hatch report.
//
//  Some doors are facts about a COMPOSITION and a static walk decides them.
//  These three are not. A guest renderer is registered by a host at startup, the
//  development surface is switched on by a compilation symbol or a call, and the
//  content-hash floor is whatever the strictest declaration in this process said
//  — none of which is visible to anything holding only the artefact. A walk that
//  reported these closed because it could not see them would report a hatch
//  closed that the host opened before the first render.
//
//  So they are observed HERE, beside the things they observe, and reported
//  through the in-page introspection surface the host already publishes.
//
//  ── What each predicate answers ─────────────────────────────────────────────
//
//  `custom-renderer-registered` — is any guest renderer reachable at all? Open
//  names every registration, with its scope, because "a renderer is registered"
//  and "THIS renderer is registered in the admin scope" are different facts to
//  whoever is reading. Undecided when the host did not offer its registry: this
//  module cannot enumerate a registry it was not handed, and there is no
//  defensible default for a door nobody looked at.
//
//  `custom-hash-floor-permissive` — is the MEDIATION on that same door in force?
//  The shipped floor refuses a guest whose declared hash disagrees with the
//  registered renderer's. A host that declared the permissive posture by name
//  gets a warning and a render instead, which widens the door without touching
//  a registration — so it is reported as its own finding rather than folded into
//  the one above, because a reader who saw only "no renderers registered" would
//  learn nothing about what happens when one is.
//
//  `development-surface-live` — is the in-page introspection surface live? This
//  one has a property the other two do not, and it is worth stating plainly: a
//  reader who is reading this report THROUGH that surface is holding the proof
//  that it is open. The finding still says so, because the same section is read
//  from a file by tooling that never touched the page.
//
//  ── Pure over what it is handed ─────────────────────────────────────────────
//  Every input is an argument. `observeWith` reads no process state at all, so
//  the whole report is testable without installing a floor or switching a
//  surface on; `observe` is the one-line convenience that reads this process.
//  Fable-clean on both paths.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Hatches

/// The registration predicate's stable name.
[<Literal>]
let CustomRendererRegistered = "custom-renderer-registered"

/// The mediation predicate's stable name.
[<Literal>]
let CustomHashFloorPermissive = "custom-hash-floor-permissive"

/// The development-surface predicate's stable name.
[<Literal>]
let DevelopmentSurfaceLive = "development-surface-live"

/// The inventory entry the guest boundary is enumerated as.
[<Literal>]
let GuestBoundaryHatch = 2

/// The inventory entry the development-mode surface is enumerated as.
[<Literal>]
let DevelopmentSurfaceHatch = 12

/// One registration, rendered for a reader: what it is, where it is reachable
/// from, and whether a declared hash could ever be checked against it.
let describeRegistration (r: Runtime.CustomRendererRegistration) : string =
    let scope =
        match r.Scope with
        | None -> "root scope"
        | Some s -> "scope '" + s + "'"

    let hash =
        if r.HasContentHash then
            "content hash registered"
        else
            "no content hash"

    r.ModuleId + "/" + r.ComponentId + " (" + scope + ", " + hash + ")"

/// Is a guest renderer registered? `None` is the host declining to say.
let customRendererFinding (registrations: Runtime.CustomRendererRegistration list option) : HatchFinding =
    match registrations with
    | None ->
        { Predicate = CustomRendererRegistered
          Hatch = GuestBoundaryHatch
          State = HatchState.Undecided
          Account =
            "the host did not offer its custom-renderer registry to this report, so nothing here enumerates it. "
            + "This is not a claim that none is registered: supply the registry to decide it." }
    | Some [] ->
        { Predicate = CustomRendererRegistered
          Hatch = GuestBoundaryHatch
          State = HatchState.Closed
          Account =
            "the host's custom-renderer registry was read and holds no registration, in any render scope. "
            + "A `Custom` node in a tree rendered by this host reaches the unregistered placeholder." }
    | Some registrations ->
        { Predicate = CustomRendererRegistered
          Hatch = GuestBoundaryHatch
          State = HatchState.Open
          Account =
            string (List.length registrations)
            + " custom renderer(s) registered: "
            + (registrations |> List.map describeRegistration |> String.concat "; ") }

/// Is the mediation on the guest boundary in force? Reported for every floor,
/// including the shipped one, so the finding is a positive statement about the
/// posture rather than an absence of complaint.
let customHashFloorFinding (floor: HashStrictness) : HatchFinding =
    let named =
        match floor with
        | HashStrictness.AdvisoryWarning -> "advisory-warning"
        | HashStrictness.Enforced -> "enforced"
        | HashStrictness.StrictReplay -> "strict-replay"

    if Fuaran.UI.Renderer.CustomHash.isEnforcing floor then
        { Predicate = CustomHashFloorPermissive
          Hatch = GuestBoundaryHatch
          State = HatchState.Closed
          Account =
            "the process content-hash floor is '"
            + named
            + "': a guest whose declared hash disagrees with the registered renderer's is REFUSED rather than rendered"
            + (if Fuaran.UI.Renderer.CustomHash.refusesUnverifiable floor then
                   ", and a guest declaring no hash at all is refused too."
               else
                   ". A guest declaring no hash at all still renders — the common legitimate case.") }
    else
        { Predicate = CustomHashFloorPermissive
          Hatch = GuestBoundaryHatch
          State = HatchState.Open
          Account =
            "the process content-hash floor is '"
            + named
            + "' — a host declared the permissive posture BY NAME. A guest whose declared hash disagrees with "
            + "the registered renderer's warns and renders anyway, so a declared hash mediates nothing here." }

/// Is the in-page introspection surface live?
let developmentSurfaceFinding (live: bool) : HatchFinding =
    if live then
        { Predicate = DevelopmentSurfaceLive
          Hatch = DevelopmentSurfaceHatch
          State = HatchState.Open
          Account =
            "the in-page introspection surface is enabled on this host — by its compilation symbol, by a "
            + "runtime call, or by both. The running application's typed layer is readable, and writable "
            + "where the host wired an apply path. A reader who obtained this report THROUGH that surface "
            + "is holding the evidence." }
    else
        { Predicate = DevelopmentSurfaceLive
          Hatch = DevelopmentSurfaceHatch
          State = HatchState.Closed
          Account =
            "neither half of the introspection surface's opt-in is present on this host, so it registers "
            + "nothing and the relay it would publish is not installed." }

/// The runtime section, over observations supplied as arguments. Reads no
/// process state — which is what makes every branch above reachable from a test.
let observeWith
    (registrations: Runtime.CustomRendererRegistration list option)
    (floor: HashStrictness)
    (developmentSurfaceLive: bool)
    : HatchSection =
    { Section = RuntimeSection
      Findings =
        [ customRendererFinding registrations
          customHashFloorFinding floor
          developmentSurfaceFinding developmentSurfaceLive ] }

/// The runtime section for THIS process: the host's registry where it offered
/// one, the floor in force, and the surface state the caller reports.
///
/// The surface state is an argument rather than a read because the module that
/// owns it is compiled after this one — and the separation is worth keeping
/// anyway, since a host that publishes this report from somewhere other than the
/// in-page surface answers that question for itself.
let observe (registry: Runtime.CustomRendererRegistry option) (developmentSurfaceLive: bool) : HatchSection =
    observeWith
        (registry |> Option.map (fun r -> r.Registrations))
        (Fuaran.UI.Renderer.CustomHash.currentCustomHashFloor ())
        developmentSurfaceLive
