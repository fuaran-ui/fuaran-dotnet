module Fuaran.UI.Tests.EgressPolicy

// ============================================================================
//  Destination policy — the typed egress allowlist at the render seam.
//
//  The scheme floor (`SanitizeTests.fs`) asks whether a URL is safe to HAVE.
//  These pin the second, orthogonal question: whether the destination is one
//  the composition DECLARED. Every case here uses a URL the scheme floor
//  accepts, because a policy that only refuses what the floor already refused
//  would be indistinguishable from no policy at all.
//
//  Three things are pinned deliberately as negative results, because each is a
//  spelling that walks past a plausible weaker implementation:
//
//    - `https://good.example@evil.example/x` — the credential-confusion form.
//      A first-`@` split reads the allowed host; the request goes to the other
//      one.
//    - `https://notexample.com/` against a `HostSuffix "example.com"` rule —
//      a substring match admits it; a label-boundary match does not.
//    - `https://example.com./` — the dotted-root spelling of a host an exact
//      rule names, which an unnormalised comparison refuses (and, worse, which
//      an unnormalised DENY-list would admit).
// ============================================================================

open Expecto
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Sanitize

/// One exact-host rule and one suffix rule, scoped to DIFFERENT classes and
/// sitting on unrelated hosts. The separation is deliberate: with the exact
/// host inside the suffix rule's tree, the class-scoping test would pass for
/// the wrong reason (the suffix rule would admit it), which is how the first
/// draft of this fixture read.
let private declared =
    Sanitize.denyNonLocalEgress
    |> Sanitize.allowOrigin (ExactHost "cdn.assets.test") [ EgressClass.Media ]
    |> Sanitize.allowOrigin (HostSuffix "example.com") [ EgressClass.Hyperlink ]

let private refusedHost (v: EgressVerdict) =
    match v with
    | EgressVerdict.UndeclaredOrigin(h, _) -> Some h
    | _ -> None

[<Tests>]
let tests =
    testList
        "Destination policy — typed egress allowlists"
        [

          testList
              "classification"
              [ test "a relative path is local" {
                    Expect.equal (Sanitize.classifyDestination "/next") Destination.Local "relative"
                }

                test "a fragment is local" {
                    Expect.equal (Sanitize.classifyDestination "#section") Destination.Local "fragment"
                }

                test "an empty URL is local" {
                    Expect.equal (Sanitize.classifyDestination "") Destination.Local "empty"
                }

                test "an absolute https URL resolves to its host" {
                    Expect.equal
                        (Sanitize.classifyDestination "https://cdn.example.com/img.png?a=1")
                        (Destination.Remote "cdn.example.com")
                        "host"
                }

                test "the port and the userinfo are stripped" {
                    Expect.equal
                        (Sanitize.classifyDestination "https://user:pw@cdn.example.com:8443/x")
                        (Destination.Remote "cdn.example.com")
                        "authority reduced to host"
                }

                test "userinfo containing a host does NOT become the host" {
                    // The credential-confusion spelling. The request goes to
                    // `evil.example`; a first-`@` split would report the other one.
                    Expect.equal
                        (Sanitize.classifyDestination "https://cdn.example.com@evil.example/x")
                        (Destination.Remote "evil.example")
                        "last @ wins"
                }

                test "the host is lowercased and the root dot dropped" {
                    Expect.equal
                        (Sanitize.classifyDestination "https://CDN.Example.COM./x")
                        (Destination.Remote "cdn.example.com")
                        "normalised"
                }

                test "mailto has no network host" {
                    Expect.equal
                        (Sanitize.classifyDestination "mailto:a@example.com")
                        (Destination.NonNetwork "mailto")
                        "non-network"
                }

                test "a javascript URL never reaches classification" {
                    Expect.equal (Sanitize.classifyDestination "javascript:alert(1)") Destination.Rejected "rejected"
                }

                test "a protocol-relative URL is rejected, not read as local" {
                    // `//evil.example/x` leaves the origin despite having no
                    // scheme — the scheme floor refuses it, and the policy layer
                    // must not silently re-admit it as a same-origin path.
                    Expect.equal (Sanitize.classifyDestination "//evil.example/x") Destination.Rejected "rejected"
                } ]

          testList
              "deny-non-local (the decoded-tree default)"
              [ test "a same-origin path is allowed" {
                    Expect.equal
                        (Sanitize.checkDestination Sanitize.denyNonLocalEgress EgressClass.Hyperlink "/next")
                        (EgressVerdict.Allowed "/next")
                        "local allowed"
                }

                test "an undeclared origin is refused with its host" {
                    let v =
                        Sanitize.checkDestination
                            Sanitize.denyNonLocalEgress
                            EgressClass.Media
                            "https://collector.example/?s=secret"

                    Expect.equal (refusedHost v) (Some "collector.example") "host recorded"
                }

                test "the refusal never carries the path or query" {
                    let v =
                        Sanitize.checkDestination
                            Sanitize.denyNonLocalEgress
                            EgressClass.Media
                            "https://collector.example/beacon?s=the-secret"

                    let described = Sanitize.describeEgressVerdict v
                    Expect.isFalse (described.Contains "the-secret") "payload absent from the description"
                    Expect.isFalse (described.Contains "beacon") "path absent from the description"

                    match Sanitize.egressRefusalMarker v with
                    | Some(_, value) ->
                        Expect.isFalse (value.Contains "the-secret") "payload absent from the marker"
                        Expect.isFalse (value.Contains "beacon") "path absent from the marker"
                    | None -> failtest "a refusal must produce a marker"
                }

                test "mailto is refused by default" {
                    match
                        Sanitize.checkDestination Sanitize.denyNonLocalEgress EgressClass.Hyperlink "mailto:a@b.example"
                    with
                    | EgressVerdict.NonNetworkDenied("mailto", EgressClass.Hyperlink) -> ()
                    | other -> failtestf "expected NonNetworkDenied, got %A" other
                }

                test "an unsafe URL is refused as unsafe, not as undeclared" {
                    // The two refusals mean different things to whoever reads the
                    // record; collapsing them would lose which gate fired.
                    Expect.equal
                        (Sanitize.checkDestination Sanitize.denyNonLocalEgress EgressClass.Route "javascript:alert(1)")
                        EgressVerdict.UnsafeUrl
                        "unsafe"
                } ]

          testList
              "declared origins"
              [ test "an exact rule admits exactly its host" {
                    Expect.equal
                        (Sanitize.checkDestination declared EgressClass.Media "https://cdn.assets.test/i.png")
                        (EgressVerdict.Allowed "https://cdn.assets.test/i.png")
                        "declared"
                }

                test "an exact rule does NOT admit a subdomain" {
                    Expect.equal
                        (refusedHost (
                            Sanitize.checkDestination declared EgressClass.Media "https://a.cdn.assets.test/i.png"
                        ))
                        (Some "a.cdn.assets.test")
                        "subdomain refused"
                }

                test "an exact rule admits the dotted-root spelling of its host" {
                    Expect.equal
                        (Sanitize.checkDestination declared EgressClass.Media "https://cdn.assets.test./i.png")
                        (EgressVerdict.Allowed "https://cdn.assets.test./i.png")
                        "normalised match"
                }

                test "a suffix rule admits the apex and its subdomains" {
                    Expect.equal
                        (Sanitize.checkDestination declared EgressClass.Hyperlink "https://example.com/docs")
                        (EgressVerdict.Allowed "https://example.com/docs")
                        "apex"

                    Expect.equal
                        (Sanitize.checkDestination declared EgressClass.Hyperlink "https://a.b.example.com/docs")
                        (EgressVerdict.Allowed "https://a.b.example.com/docs")
                        "subdomain"
                }

                test "a suffix rule requires a label boundary" {
                    // `notexample.com` ends with `example.com` as a SUBSTRING.
                    Expect.equal
                        (refusedHost (
                            Sanitize.checkDestination declared EgressClass.Hyperlink "https://notexample.com/x"
                        ))
                        (Some "notexample.com")
                        "substring is not a suffix match"
                }

                test "a rule is scoped to its classes" {
                    // `cdn.example.com` is declared for Media only. The same host
                    // in a hyperlink is undeclared — which is the whole point of
                    // per-class scoping: an image host is not a navigation target.
                    Expect.equal
                        (refusedHost (
                            Sanitize.checkDestination declared EgressClass.Hyperlink "https://cdn.assets.test/x"
                        ))
                        (Some "cdn.assets.test")
                        "class-scoped"
                }

                test "a rule with no classes permits nothing" {
                    let empty =
                        { Sanitize.denyNonLocalEgress with
                            Rules =
                                [ { Origin = ExactHost "example.com"
                                    Classes = [] } ] }

                    Expect.equal
                        (refusedHost (Sanitize.checkDestination empty EgressClass.Media "https://example.com/x"))
                        (Some "example.com")
                        "an empty class list is not a wildcard"
                }

                test "allowOrigin with no classes means every class" {
                    let all =
                        Sanitize.denyNonLocalEgress |> Sanitize.allowOrigin (ExactHost "example.com") []

                    for cls in EgressClass.all do
                        Expect.equal
                            (Sanitize.checkDestination all cls "https://example.com/x")
                            (EgressVerdict.Allowed "https://example.com/x")
                            (EgressClass.name cls)
                } ]

          testList
              "permissive (the hand-authored posture)"
              [ test "any origin is allowed" {
                    Expect.equal
                        (Sanitize.checkDestination
                            Sanitize.permissiveEgress
                            EgressClass.Media
                            "https://anything.example/x")
                        (EgressVerdict.Allowed "https://anything.example/x")
                        "permissive"
                }

                test "an unsafe URL is STILL refused" {
                    // Permissive is a destination policy, not a scheme policy.
                    // Reaching it must not re-open the injection floor beneath it.
                    Expect.equal
                        (Sanitize.checkDestination Sanitize.permissiveEgress EgressClass.Route "javascript:alert(1)")
                        EgressVerdict.UnsafeUrl
                        "floor intact"
                } ]

          testList
              "the render seam renders the refusal"
              [ test "an allowed destination emits the normalised URL and no marker" {
                    let url, attrs =
                        Sanitize.sanitizeUrlForEgress declared EgressClass.Media "https://cdn.assets.test/i.png"

                    Expect.equal url "https://cdn.assets.test/i.png" "url"
                    Expect.isEmpty attrs "no marker on an allowed destination"
                }

                test "a refused destination emits the refusal marker, not a bare blank" {
                    let url, attrs =
                        Sanitize.sanitizeUrlForEgress declared EgressClass.Media "https://collector.example/x"

                    Expect.equal url Sanitize.egressRefusalUrl "the refusal is visible in the URL"
                    Expect.notEqual url "about:blank" "not a silent neuter"
                    Expect.equal attrs [ Sanitize.egressRefusalAttribute, "media:collector.example" ] "marker"
                }

                test "the refusal attribute survives the ExtraAttributes gate" {
                    // The marker is only useful if the emission path it travels
                    // does not itself drop it.
                    Expect.isTrue (Sanitize.isAllowedExtraAttributeKey Sanitize.egressRefusalAttribute) "key admissible"

                    Expect.isTrue (Sanitize.isSafeExtraAttributeValue "media:collector.example") "value admissible"
                } ]

          testList
              "manifest projection"
              [ test "the projection is deterministic regardless of rule order" {
                    let a =
                        Sanitize.denyNonLocalEgress
                        |> Sanitize.allowOrigin (ExactHost "b.example") [ EgressClass.Media ]
                        |> Sanitize.allowOrigin (HostSuffix "a.example") [ EgressClass.Hyperlink ]

                    let b =
                        Sanitize.denyNonLocalEgress
                        |> Sanitize.allowOrigin (HostSuffix "a.example") [ EgressClass.Hyperlink ]
                        |> Sanitize.allowOrigin (ExactHost "b.example") [ EgressClass.Media ]

                    Expect.equal (Sanitize.encodeEgressPolicy a) (Sanitize.encodeEgressPolicy b) "order-independent"
                }

                test "the projection carries the declared shape" {
                    let p =
                        Sanitize.denyNonLocalEgress
                        |> Sanitize.allowOrigin (HostSuffix "Example.COM.") [ EgressClass.Media; EgressClass.Hyperlink ]

                    Expect.equal
                        (Sanitize.encodeEgressPolicy p)
                        "{\"allowAnyOrigin\":false,\"allowLocal\":true,\"allowNonNetwork\":false,\"rules\":[{\"classes\":[\"hyperlink\",\"media\"],\"match\":\"suffix\",\"origin\":\"example.com\"}]}"
                        "canonical bytes"
                }

                test "the deny-non-local default projects as declaring nothing" {
                    Expect.equal
                        (Sanitize.encodeEgressPolicy Sanitize.denyNonLocalEgress)
                        "{\"allowAnyOrigin\":false,\"allowLocal\":true,\"allowNonNetwork\":false,\"rules\":[]}"
                        "empty"

                    Expect.isFalse (Sanitize.hasNonLocalEgress Sanitize.denyNonLocalEgress) "nothing leaves"
                }

                test "a declared origin makes the policy non-local" {
                    Expect.isTrue (Sanitize.hasNonLocalEgress declared) "declared egress"
                } ]

          testList
              "class names round-trip"
              [ test "every class parses back from its wire name" {
                    for cls in EgressClass.all do
                        Expect.equal (EgressClass.parse (EgressClass.name cls)) (Some cls) (EgressClass.name cls)
                }

                test "an unknown class name is None, not a silently-dropped rule" {
                    Expect.isNone (EgressClass.parse "websocket") "unknown"
                } ] ]
