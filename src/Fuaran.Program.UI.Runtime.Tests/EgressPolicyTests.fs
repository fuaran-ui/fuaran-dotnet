module Fuaran.Program.Runtime.Tests.EgressPolicy

// ============================================================================
//  The destination policy at the effect seam.
//
//  The discriminator gate's own suite pins "may this host do that at all".
//  These pin the second question the discriminator structurally could not ask:
//  "may it do that THERE". Every URL here passes the scheme floor, because a
//  policy that only refuses what the floor already refused would be
//  indistinguishable from no policy at all.
//
//  Two orderings are pinned as behaviour rather than left to reading order,
//  because both are easy to get subtly wrong and neither shows up as a failure
//  until someone is reading a log during an incident: the gate is consulted
//  before the destination, and exactly ONE denial is recorded per dispatch.
// ============================================================================

open Expecto
// `NavigateTarget` — the browsing context the effect's route now carries. The
// destinations under test are unchanged by it, which is the point: every case
// here names `Self`, and the one that would not is a target question rather
// than a destination one.
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.Program.Runtime
open Fuaran.Program.UI

/// A registry that records what it performed and why it refused.
let private probe (build: EffectRegistry -> EffectRegistry) =
    let performed = ResizeArray<ClientEffect>()
    let denied = ResizeArray<EffectDenial>()

    let registry =
        EffectRegistry.denyAll
        |> EffectRegistry.register "Navigate" performed.Add
        |> EffectRegistry.register "Download" performed.Add
        |> EffectRegistry.register "ReadFileBody" performed.Add
        |> EffectRegistry.register "WriteToClipboard" performed.Add
        |> EffectRegistry.withGate (fun _ -> true)
        |> EffectRegistry.onDenied denied.Add
        |> build

    registry, performed, denied

let private deniedOrigin (denials: ResizeArray<EffectDenial>) =
    match List.ofSeq denials with
    | [ EffectDenial.DestinationRefused(_, origin) ] -> Some origin
    | _ -> None

[<Tests>]
let tests =
    testList
        "Effect seam — destination policy"
        [

          testList
              "classification by arm"
              [ test "Navigate and PushState classify their route" {
                    Expect.equal
                        (ClientEffectDestination.destinationOf (
                            ClientEffect.Navigate("https://a.example/x", NavigateTarget.Self)
                        ))
                        (EffectDestination.Remote "a.example")
                        "navigate"

                    Expect.equal
                        (ClientEffectDestination.destinationOf (ClientEffect.PushState "/local"))
                        EffectDestination.Local
                        "pushstate"
                }

                test "Download classifies its url, not its filename" {
                    Expect.equal
                        (ClientEffectDestination.destinationOf (
                            ClientEffect.Download("https://cdn.assets.test/f.pdf", "https://decoy.example/x")
                        ))
                        (EffectDestination.Remote "cdn.assets.test")
                        "url is the destination"
                }

                test "ReadFileBody is local — it carries no URL" {
                    Expect.equal
                        (ClientEffectDestination.destinationOf (ClientEffect.ReadFileBody("node", "Text")))
                        EffectDestination.Local
                        "local"
                }

                test "the destination-less arms are Absent, not Local" {
                    // Distinct on purpose: `Absent` says the question does not
                    // apply, `Local` says it does and the answer is the host's
                    // own origin. Collapsing them would let a policy that
                    // denies local egress silently disable the clipboard.
                    Expect.equal
                        (ClientEffectDestination.destinationOf (ClientEffect.WriteToClipboard "text"))
                        EffectDestination.Absent
                        "clipboard"

                    Expect.equal
                        (ClientEffectDestination.destinationOf (ClientEffect.Focus "node"))
                        EffectDestination.Absent
                        "focus"
                }

                test "userinfo containing a host does NOT become the host" {
                    // The credential-confusion spelling: the request goes to
                    // `evil.example`, and a first-`@` split reads the other one.
                    Expect.equal
                        (ClientEffectDestination.destinationOf (
                            ClientEffect.Navigate("https://cdn.assets.test@evil.example/x", NavigateTarget.Self)
                        ))
                        (EffectDestination.Remote "evil.example")
                        "last @ wins"
                } ]

          testList
              "the default refuses what the discriminator gate could not"
              [ test "a gate-permitted Navigate to an undeclared origin is refused" {
                    let registry, performed, denied = probe id

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://collector.example/?s=secret", NavigateTarget.Self))

                    Expect.isEmpty performed "not performed"
                    Expect.equal (deniedOrigin denied) (Some "collector.example") "origin recorded"
                }

                test "the denial carries the origin and never the URL" {
                    let registry, _, denied = probe id

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://collector.example/beacon?s=the-secret", NavigateTarget.Self))

                    let described = EffectDenial.describe (Seq.exactlyOne denied)
                    Expect.stringContains described "collector.example" "origin present"
                    Expect.isFalse (described.Contains "the-secret") "payload absent"
                    Expect.isFalse (described.Contains "beacon") "path absent"
                }

                test "a same-origin route is still performed" {
                    let registry, performed, denied = probe id
                    EffectRegistry.perform registry (ClientEffect.Navigate("/next", NavigateTarget.Self))

                    Expect.equal
                        (List.ofSeq performed)
                        [ ClientEffect.Navigate("/next", NavigateTarget.Self) ]
                        "performed"

                    Expect.isEmpty denied "no denial"
                }

                test "a destination-less effect is unaffected by the egress policy" {
                    let registry, performed, denied = probe id
                    EffectRegistry.perform registry (ClientEffect.WriteToClipboard "text")

                    Expect.equal (List.ofSeq performed) [ ClientEffect.WriteToClipboard "text" ] "performed"
                    Expect.isEmpty denied "no denial"
                }

                test "an unsafe URL is refused at the effect seam too" {
                    // The floor runs inside the classification, so the effect
                    // seam does not depend on the driver having sanitised first.
                    let registry, performed, denied = probe id
                    EffectRegistry.perform registry (ClientEffect.Navigate("javascript:alert(1)", NavigateTarget.Self))

                    Expect.isEmpty performed "not performed"
                    Expect.equal (deniedOrigin denied) (Some "unparseable") "refused"
                } ]

          testList
              "declared origins"
              [ test "a declared origin is performed" {
                    let registry, performed, denied =
                        probe (EffectRegistry.allowOrigin (ExactHost "cdn.assets.test") [ "Download" ])

                    EffectRegistry.perform registry (ClientEffect.Download("https://cdn.assets.test/f.pdf", "f.pdf"))

                    Expect.equal (List.length (List.ofSeq performed)) 1 "performed"
                    Expect.isEmpty denied "no denial"
                }

                test "a rule is scoped to its effects" {
                    // `cdn.assets.test` is declared for Download. Navigating to
                    // it is a different act, and an undeclared one — which is the
                    // whole point of per-effect scoping: a download host is not a
                    // navigation target.
                    let registry, performed, denied =
                        probe (EffectRegistry.allowOrigin (ExactHost "cdn.assets.test") [ "Download" ])

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://cdn.assets.test/x", NavigateTarget.Self))

                    Expect.isEmpty performed "not performed"
                    Expect.equal (deniedOrigin denied) (Some "cdn.assets.test") "effect-scoped"
                }

                test "an exact rule does not admit a subdomain" {
                    let registry, performed, denied =
                        probe (EffectRegistry.allowOrigin (ExactHost "cdn.assets.test") [ "Download" ])

                    EffectRegistry.perform registry (ClientEffect.Download("https://a.cdn.assets.test/f", "f"))

                    Expect.isEmpty performed "not performed"
                    Expect.equal (deniedOrigin denied) (Some "a.cdn.assets.test") "subdomain refused"
                }

                test "a suffix rule admits the apex and subdomains" {
                    let registry, performed, _ =
                        probe (EffectRegistry.allowOrigin (HostSuffix "example.com") [ "Navigate" ])

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://example.com/a", NavigateTarget.Self))

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://x.y.example.com/b", NavigateTarget.Self))

                    Expect.equal (List.length (List.ofSeq performed)) 2 "both performed"
                }

                test "a suffix rule requires a label boundary" {
                    // `notexample.com` ends with `example.com` as a SUBSTRING.
                    let registry, performed, denied =
                        probe (EffectRegistry.allowOrigin (HostSuffix "example.com") [ "Navigate" ])

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://notexample.com/x", NavigateTarget.Self))

                    Expect.isEmpty performed "not performed"
                    Expect.equal (deniedOrigin denied) (Some "notexample.com") "substring is not a suffix match"
                }

                test "the dotted-root spelling of a declared host is admitted" {
                    let registry, performed, _ =
                        probe (EffectRegistry.allowOrigin (ExactHost "example.com") [ "Navigate" ])

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://Example.COM./x", NavigateTarget.Self))

                    Expect.equal (List.length (List.ofSeq performed)) 1 "normalised match"
                }

                test "allowOrigin with no effects means every effect" {
                    let registry, performed, _ =
                        probe (EffectRegistry.allowOrigin (ExactHost "example.com") [])

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://example.com/a", NavigateTarget.Self))

                    EffectRegistry.perform registry (ClientEffect.Download("https://example.com/f", "f"))

                    Expect.equal (List.length (List.ofSeq performed)) 2 "both performed"
                }

                test "a rule naming no effect permits nothing" {
                    let registry, performed, denied =
                        probe (
                            EffectRegistry.withEgress
                                { EgressPolicy.denyNonLocal with
                                    Rules =
                                        [ { Origin = ExactHost "example.com"
                                            Effects = [] } ] }
                        )

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://example.com/a", NavigateTarget.Self))

                    Expect.isEmpty performed "not performed"
                    Expect.equal (deniedOrigin denied) (Some "example.com") "an empty effect list is not a wildcard"
                } ]

          testList
              "postures"
              [ test "permissive opens BOTH gates" {
                    let performed = ResizeArray<ClientEffect>()

                    let registry =
                        EffectRegistry.denyAll
                        |> EffectRegistry.register "Navigate" performed.Add
                        |> EffectRegistry.permissive

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://anywhere.example/x", NavigateTarget.Self))

                    Expect.equal (List.length (List.ofSeq performed)) 1 "performed"
                }

                test "permissive still cannot reach an unregistered effect" {
                    let denied = ResizeArray<EffectDenial>()

                    let registry =
                        EffectRegistry.denyAll
                        |> EffectRegistry.permissive
                        |> EffectRegistry.onDenied denied.Add

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://anywhere.example/x", NavigateTarget.Self))

                    match List.ofSeq denied with
                    | [ EffectDenial.Unregistered "Navigate" ] -> ()
                    | other -> failtestf "expected Unregistered, got %A" other
                }

                test "denying local egress denies a same-origin route AND a file read" {
                    let registry, performed, denied =
                        probe (
                            EffectRegistry.withEgress
                                { EgressPolicy.denyNonLocal with
                                    AllowLocal = false }
                        )

                    EffectRegistry.perform registry (ClientEffect.Navigate("/next", NavigateTarget.Self))
                    EffectRegistry.perform registry (ClientEffect.ReadFileBody("node", "Text"))

                    Expect.isEmpty performed "neither performed"
                    Expect.equal (List.length (List.ofSeq denied)) 2 "both recorded"
                }

                test "denying local egress does NOT deny a destination-less effect" {
                    let registry, performed, denied =
                        probe (
                            EffectRegistry.withEgress
                                { EgressPolicy.denyNonLocal with
                                    AllowLocal = false }
                        )

                    EffectRegistry.perform registry (ClientEffect.WriteToClipboard "text")

                    Expect.equal (List.length (List.ofSeq performed)) 1 "performed"
                    Expect.isEmpty denied "no denial"
                } ]

          testList
              "ordering"
              [ test "the discriminator gate is consulted before the destination" {
                    // A gate-refused effect must record GateRefused, not a
                    // destination refusal — otherwise a host reading its log
                    // concludes its allowlist is wrong when its gate is closed.
                    let denied = ResizeArray<EffectDenial>()

                    let registry =
                        EffectRegistry.denyAll
                        |> EffectRegistry.register "Navigate" ignore
                        |> EffectRegistry.withGate (fun _ -> false)
                        |> EffectRegistry.onDenied denied.Add

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://collector.example/x", NavigateTarget.Self))

                    match List.ofSeq denied with
                    | [ EffectDenial.GateRefused "Navigate" ] -> ()
                    | other -> failtestf "expected GateRefused, got %A" other
                }

                test "exactly one denial is recorded per dispatch" {
                    let registry, _, denied = probe id

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://collector.example/x", NavigateTarget.Self))

                    Expect.equal (List.length (List.ofSeq denied)) 1 "one denial"
                }

                test "no performer runs before the destination decision" {
                    let mutable ran = 0

                    let denied = ResizeArray<EffectDenial>()

                    let registry =
                        EffectRegistry.denyAll
                        |> EffectRegistry.register "Navigate" (fun _ -> ran <- ran + 1)
                        |> EffectRegistry.withGate (fun _ -> true)
                        |> EffectRegistry.onDenied denied.Add

                    EffectRegistry.perform
                        registry
                        (ClientEffect.Navigate("https://collector.example/x", NavigateTarget.Self))

                    Expect.equal ran 0 "the performer never ran"
                    Expect.equal (List.length (List.ofSeq denied)) 1 "and the refusal was recorded"
                } ] ]
