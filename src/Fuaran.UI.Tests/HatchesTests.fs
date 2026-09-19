/// Phase 1743 — the runtime half of the escape-hatch report.
///
/// What is pinned here is the three-valued answer and the difference between its
/// third value and its second. An inventory's whole worth is that it is
/// complete, so a report that renders "I could not see this door" as "this door
/// is closed" is worse than no report: it puts a claim behind something nobody
/// checked. Every test below that looks like it is about a string is about that.
///
/// The predicates are pure over their arguments (`RuntimeHatches.observeWith`
/// reads no process state), so each branch is reachable without installing a
/// process-wide content-hash floor or switching the in-page surface on — which
/// matters because both of those are process-global and raise-only, and a suite
/// that had to set them would leak across every other suite in this assembly.
module Fuaran.UI.Tests.HatchesTests

open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Hatches
open Fuaran.UI.Renderer

// ─── fixtures ───────────────────────────────────────────────────────────────

let private registration scope moduleId componentId hasHash : Runtime.CustomRendererRegistration =
    { Scope = scope
      ModuleId = moduleId
      ComponentId = componentId
      HasContentHash = hasHash }

/// The posture a host that configured nothing runs under: the shipped enforcing
/// floor, no development surface.
let private unconfigured registrations =
    RuntimeHatches.observeWith registrations HashStrictness.Enforced false

let private findingFor (predicate: string) (section: HatchSection) : HatchFinding =
    match section.Findings |> List.tryFind (fun f -> f.Predicate = predicate) with
    | Some f -> f
    | None -> failtestf "the section carries no finding named '%s' — every predicate reports every run" predicate

// ─── tests ──────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 1743 — the runtime escape-hatch report"
        [ testList
              "the guest boundary"
              [ test "a registry that was read and is empty reports the door CLOSED" {
                    let section = unconfigured (Some [])
                    let finding = findingFor RuntimeHatches.CustomRendererRegistered section

                    Expect.equal finding.State HatchState.Closed "read and empty is a positive statement"
                    Expect.equal finding.Hatch RuntimeHatches.GuestBoundaryHatch "the inventory entry it mechanises"

                    Expect.stringContains
                        finding.Account
                        "holds no registration"
                        "a closed finding says what was checked, so the document is a statement rather than an absence"
                }

                test "a registered renderer reports the door OPEN and NAMES what admitted it" {
                    let section =
                        unconfigured (Some [ registration None "acme.charts" "Sparkline" true ])

                    let finding = findingFor RuntimeHatches.CustomRendererRegistered section

                    Expect.equal finding.State HatchState.Open "a registration is an open door"

                    Expect.stringContains
                        finding.Account
                        "acme.charts/Sparkline"
                        "an open hatch names what admitted it — 'something is registered' is half a finding"

                    Expect.stringContains finding.Account "root scope" "and where it is reachable from"

                    Expect.stringContains
                        finding.Account
                        "content hash registered"
                        "and whether a declared hash could ever be checked against it"
                }

                test "a scoped registration without a hash is reported as exactly that" {
                    let section =
                        unconfigured (Some [ registration (Some "admin") "acme.admin" "Console" false ])

                    let account = (findingFor RuntimeHatches.CustomRendererRegistered section).Account
                    Expect.stringContains account "scope 'admin'" "the scope is part of the fact"

                    Expect.stringContains
                        account
                        "no content hash"
                        "a renderer no declared hash can match is a different posture from one that carries a hash"
                }

                test "a host that offered NO registry reports UNDECIDED, never closed" {
                    let section = unconfigured None
                    let finding = findingFor RuntimeHatches.CustomRendererRegistered section

                    Expect.equal
                        finding.State
                        HatchState.Undecided
                        "the interface exposes per-key lookups and no enumeration, so an unoffered registry is undecidable — and reporting it closed would be the exact failure this inventory exists to prevent"

                    Expect.stringContains
                        finding.Account
                        "not a claim that none is registered"
                        "an undecided finding says why, and says what would decide it"
                } ]

          testList
              "the mediation on that same door"
              [ test "the shipped enforcing floor is reported CLOSED, and says what it refuses" {
                    let finding =
                        unconfigured (Some []) |> findingFor RuntimeHatches.CustomHashFloorPermissive

                    Expect.equal finding.State HatchState.Closed "enforcing is the mediation being in force"
                    Expect.stringContains finding.Account "enforced" "the floor is named"
                    Expect.stringContains finding.Account "REFUSED" "and what it does with a disagreeing hash"

                    Expect.stringContains
                        finding.Account
                        "still renders"
                        "and what it deliberately does NOT refuse — the common legitimate case, which is what makes the default shippable"
                }

                test "strict-replay is closed AND reports the stronger refusal" {
                    let finding =
                        RuntimeHatches.observeWith (Some []) HashStrictness.StrictReplay false
                        |> findingFor RuntimeHatches.CustomHashFloorPermissive

                    Expect.equal finding.State HatchState.Closed "strictest is still closed"

                    Expect.stringContains
                        finding.Account
                        "no hash at all is refused too"
                        "the one floor that refuses an ABSENCE says so, because that is a different claim"
                }

                test "the permissive floor is an OPEN door even with nothing registered" {
                    let section =
                        RuntimeHatches.observeWith (Some []) HashStrictness.AdvisoryWarning false

                    let finding = findingFor RuntimeHatches.CustomHashFloorPermissive section

                    Expect.equal
                        finding.State
                        HatchState.Open
                        "a relaxed mediation widens the door without touching a registration, which is why it is its own finding"

                    Expect.stringContains
                        finding.Account
                        "BY NAME"
                        "the permissive posture is only reachable deliberately, and the account says so"

                    Expect.equal
                        (findingFor RuntimeHatches.CustomRendererRegistered section).State
                        HatchState.Closed
                        "and the registration finding beside it is unaffected — a reader who saw only that one would learn nothing about this"
                } ]

          testList
              "the development surface"
              [ test "off reports CLOSED" {
                    let finding =
                        unconfigured (Some []) |> findingFor RuntimeHatches.DevelopmentSurfaceLive

                    Expect.equal finding.State HatchState.Closed "neither half of the opt-in"
                    Expect.equal finding.Hatch RuntimeHatches.DevelopmentSurfaceHatch "the inventory entry"
                }

                test "on reports OPEN and names the evidence a reader is already holding" {
                    let finding =
                        RuntimeHatches.observeWith (Some []) HashStrictness.Enforced true
                        |> findingFor RuntimeHatches.DevelopmentSurfaceLive

                    Expect.equal finding.State HatchState.Open "the typed layer is readable"

                    Expect.stringContains
                        finding.Account
                        "holding the evidence"
                        "a reader who obtained the report THROUGH the surface has proof of this finding, and the account says so rather than leaving it to be noticed"
                } ]

          testList
              "the summary line"
              [ test "nothing open and nothing undecided renders the bare phrase" {
                    Expect.equal
                        (summary (unconfigured (Some [])))
                        "hatches open: none"
                        "the sentence this phase exists to make computable — and it is a claim about the whole section, which is why it is only reachable when every finding is closed"
                }

                test "an open hatch names itself and its inventory entry" {
                    let line =
                        summary (unconfigured (Some [ registration None "acme.charts" "Sparkline" true ]))

                    Expect.stringContains line "custom-renderer-registered" "the predicate names itself"
                    Expect.stringContains line "hatch 2" "beside the inventory entry it mechanises"

                    Expect.isFalse
                        (line.Contains "open: none")
                        "and the bare phrase is unreachable while anything is open"
                }

                test "an undecided hatch rides the same line, so 'none' can never read as a clean bill of health" {
                    let line = summary (unconfigured None)

                    Expect.stringContains line "hatches open: none" "nothing was found open, which is true"

                    Expect.stringContains
                        line
                        "undecided: custom-renderer-registered"
                        "and the one door the walk could not see is on the same line — a summary that dropped it would report a partial walk as a complete one"
                } ]

          testList
              "the document"
              [ test "the section renders as the declared kind and version, with lowercase state tokens" {
                    let json =
                        renderSection (
                            RuntimeHatches.observeWith
                                (Some [ registration None "acme.charts" "Sparkline" true ])
                                HashStrictness.Enforced
                                true
                        )

                    match Json.parse json with
                    | Error why -> failtestf "the section did not render as parseable JSON: %s" why
                    | Ok(JObj fields) ->
                        let field name =
                            fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

                        Expect.equal (field "kind") (Some(JStr Kind)) "the document carries its own kind"
                        Expect.equal (field "version") (Some(JInt Version)) "and its version"

                        Expect.equal
                            (field "section")
                            (Some(JStr RuntimeSection))
                            "and which walk produced it — no composition walk can see a host's startup, and a reader must be able to tell which question was asked"

                        match field "findings" with
                        | Some(JArr findings) ->
                            Expect.equal (List.length findings) 3 "every predicate reports, whatever its answer"
                        | other -> failtestf "findings is not an array: %A" other
                    | Ok other -> failtestf "the section is not a JSON object: %A" other
                }

                test "every state token round-trips, and an unrecognised one is REFUSED" {
                    for state in [ HatchState.Open; HatchState.Closed; HatchState.Undecided ] do
                        Expect.equal
                            (tryStateOfWire (stateWire state))
                            (Some state)
                            "a reader in another tier reads this document by shape, so the token set is the contract"

                    Expect.equal
                        (tryStateOfWire "unknown")
                        None
                        "an unrecognised state is refused rather than defaulted — every plausible default is a lie in one direction"
                } ]

          testList
              "the registry enumeration this report rests on"
              [ test "Registrations names every scope, and is deterministically ordered" {
                    let registry = Runtime.CustomRendererRegistry()
                    registry.RegisterInScope("admin", "zeta", "Widget", (fun _ -> Feliz.Html.none))
                    registry.Register("alpha", "Widget", (fun _ -> Feliz.Html.none))

                    let listed = registry.Registrations

                    Expect.equal
                        (List.length listed)
                        2
                        "both scopes are enumerated — Count would have said 2 and named neither"

                    Expect.equal
                        (listed |> List.map (fun r -> r.Scope, r.ModuleId))
                        [ None, "alpha"; Some "admin", "zeta" ]
                        "ordered deterministically: a dictionary's enumeration order is not, and a report whose findings reorder between two runs reads as a change when nothing changed"

                    Expect.isFalse
                        (listed |> List.forall (fun r -> r.HasContentHash))
                        "a registration made without a hash is reported without one"
                }

                test "a registration's content hash is carried as a flag, not as the renderer" {
                    let registry = Runtime.CustomRendererRegistry()

                    registry.Register(
                        "alpha",
                        "Widget",
                        (fun _ -> Feliz.Html.none),
                        { Algorithm = "SHA256"
                          Hash = "abc123"
                          Strictness = HashStrictness.Enforced }
                    )

                    match registry.Registrations with
                    | [ one ] -> Expect.isTrue one.HasContentHash "the mediation half of the fact rides beside the ids"
                    | other -> failtestf "expected exactly one registration, got %A" other
                } ]

          testList
              "the shipped defaults"
              [ test "a surface built from DebugOptions.defaults leaves the guest door UNDECIDED" {
                    // The wiring decision, pinned: `defaults` does not carry a
                    // registry, so an unconfigured host's report says so rather
                    // than reporting a door closed on the strength of a default.
                    Expect.isNone
                        DebugGlobal.DebugOptions.defaults.Registry
                        "an option rather than a default, and this is why"

                    Expect.equal
                        (RuntimeHatches.observe DebugGlobal.DebugOptions.defaults.Registry false
                         |> findingFor RuntimeHatches.CustomRendererRegistered)
                            .State
                        HatchState.Undecided
                        "the default wiring decides nothing about this door and reports exactly that"
                } ]

          testList
              "the predicates can go red"
              [ test "the empty-registry reading is distinguishable from the unoffered one" {
                    // A confirming result is the least-examined kind of evidence:
                    // if these two collapsed, every test above asserting CLOSED
                    // would pass for the wrong reason.
                    Expect.notEqual
                        (unconfigured (Some []) |> findingFor RuntimeHatches.CustomRendererRegistered).State
                        (unconfigured None |> findingFor RuntimeHatches.CustomRendererRegistered).State
                        "'read it, found nothing' and 'never read it' are different facts, and this whole report is that distinction"
                } ] ]
