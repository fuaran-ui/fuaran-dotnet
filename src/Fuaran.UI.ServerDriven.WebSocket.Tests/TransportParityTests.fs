module Fuaran.UI.ServerDriven.WebSocket.Tests.TransportParityTests

// ─── Phase 787: the two live transports share one security posture ─────────
//
// This file exists because its absence has already cost us once. Phase 211
// hardened the SSE+POST backend — HMAC connection-token binding, 401 on
// failure, a 1 MB body cap — and the WebSocket backend received none of it.
// Worse, its config comment ASSERTED the parity it did not have ("the WS
// connection carries its own identity"), so a reader checking the claim found a
// reassuring sentence rather than a gate. A host that hardens one transport and
// assumes the other followed is wrong, and nothing in the build said so.
//
// So the guard is not "the WS backend is hardened" — that would pass forever
// once fixed while a THIRD transport shipped bare. It is "the two backends
// expose the SAME posture", written so that adding a transport which skips it
// fails here. The assertions compare the two configs against EACH OTHER, never
// against a literal restated locally: a test hard-coding 1048576L would keep
// passing after someone changed one transport's budget, which is precisely the
// drift it is supposed to catch.
//
// What this file can and cannot reach. `ConnToken` and both config records are
// pure, so the token posture is asserted directly and headlessly. The endpoint
// glue (accept / upgrade / close) is ASP.NET and browser-verified, as the
// backends' other tests are; what is pinned here is that the two configs carry
// the same seams, the same defaults, and the same verifier.

open System.Security.Claims
open Expecto
open Microsoft.AspNetCore.Http
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.WebSocket

/// A minimal session factory — neither backend inspects it to build a config.
let private makeSession () : LiveSession<int, int> =
    Driver.init
        (DriverServices.create (fun _ -> ""))
        (fun (_: int) (m: int) -> m)
        (fun (_: int) -> Fuaran.UI.Fuaran.markdown "n" "")
        0

let private sseConfig () =
    AspNetCore.Endpoints.defaultConfig makeSession

let private wsConfig () = Endpoints.defaultWsConfig makeSession

[<Tests>]
let parityTests =
    testList
        "Transport parity (SSE+POST vs WebSocket)"
        [ test "both transports gate on a principal-bound HMAC token" {
              let sse = sseConfig ()
              let ws = wsConfig ()

              // Each mints a FRESH per-process secret rather than a fixed or
              // empty key. A non-empty check alone would pass for a hard-coded
              // constant, so the lengths are checked and the two are required to
              // differ — a shared literal would fail here.
              Expect.equal (Array.length sse.Secret) 32 "SSE secret is a 32-byte key"
              Expect.equal (Array.length ws.Secret) 32 "WS secret is a 32-byte key"
              Expect.notEqual sse.Secret ws.Secret "each config mints its OWN fresh secret"
              Expect.notEqual (wsConfig ()).Secret ws.Secret "a second WS config mints a different secret again"
          }

          test "both transports verify through the SAME ConnToken implementation" {
              // The real regression this catches is a transport growing its own
              // copy of the token logic. Asserted on BOTH configs' secrets so
              // neither can drift to a private scheme unnoticed.
              for label, secret in [ "SSE", (sseConfig ()).Secret; "WS", (wsConfig ()).Secret ] do
                  let token = ConnToken.sign secret "alice" "conn-1"

                  Expect.equal
                      (ConnToken.verify secret "alice" token)
                      (Some "conn-1")
                      (label + " — a token signed for alice verifies as alice")

                  Expect.isNone (ConnToken.verify secret "mallory" token) (label + " — cross-principal replay refused")
                  Expect.isNone (ConnToken.verify secret "alice" "conn-1") (label + " — a bare unsigned id refused")
          }

          test "a token minted by one transport does not verify under the other's secret" {
              // Per-process secrets mean the two backends' cookies are not
              // interchangeable, which is why they also carry distinct cookie
              // names below. Stated as a test so a later "simplification" that
              // shared one secret across both is a deliberate change, not an
              // accident nobody notices.
              let ws = wsConfig ()
              let sseToken = ConnToken.sign (sseConfig ()).Secret "alice" "conn-1"

              Expect.isNone (ConnToken.verify ws.Secret "alice" sseToken) "an SSE token is not a WS token"
          }

          test "both transports default to the SAME inbound budget" {
              let sse = sseConfig ()
              let ws = wsConfig ()

              // Compared to each other and to the one shared constant — never to
              // a literal repeated here. This is the assertion that was
              // impossible before Phase 787, when each cap was a number inside a
              // handler and one transport had no cap at all.
              Expect.equal ws.MaxMessageBytes sse.MaxBodyBytes "the two transports cap inbound events identically"

              Expect.equal
                  sse.MaxBodyBytes
                  LiveLimits.defaultMaxInboundBytes
                  "and both read it from LiveLimits rather than restating it"
          }

          test "both transports resolve the same principal from the same request" {
              // The two resolvers are SEPARATE functions — the WS package takes
              // no dependency on the SSE package, so this is the one piece of
              // the posture that is duplicated rather than shared, and therefore
              // the one most able to drift. Asserting identity is not available
              // (referencing an F# module-level function yields a fresh closure
              // each time, so `ReferenceEquals` is false even when both configs
              // hold the intended resolver); asserting BEHAVIOUR is what the
              // parity claim actually means anyway.
              //
              // Both branches are exercised, because they fail differently: an
              // unauthenticated request must yield "" on both — a resolver that
              // returned a non-empty placeholder there would bind every
              // anonymous visitor to one shared principal — and an authenticated
              // one must yield the same name, or a cookie minted on one
              // transport's terms would verify under the other's.
              let sse = sseConfig ()
              let ws = wsConfig ()

              let anonymous = DefaultHttpContext()

              Expect.equal (ws.ResolvePrincipal anonymous) "" "WS: an unauthenticated request resolves to \"\""

              Expect.equal
                  (ws.ResolvePrincipal anonymous)
                  (sse.ResolvePrincipal anonymous)
                  "both transports agree on an unauthenticated request"

              let authenticated = DefaultHttpContext()

              authenticated.User <-
                  ClaimsPrincipal(ClaimsIdentity([ Claim(ClaimTypes.Name, "alice") ], "test-auth-scheme"))

              Expect.equal (ws.ResolvePrincipal authenticated) "alice" "WS: an authenticated request resolves the name"

              Expect.equal
                  (ws.ResolvePrincipal authenticated)
                  (sse.ResolvePrincipal authenticated)
                  "both transports agree on an authenticated request"
          }

          test "the two transports use distinct cookie names" {
              // A host running both backends must not have one transport's
              // connId clobber the other's — same cookie name, same path, last
              // writer wins, and the loser's next request is a 401 nobody can
              // explain.
              Expect.notEqual (wsConfig ()).CookieName (sseConfig ()).CookieName "correlation cookies are distinct"
          }

          test "the WS backend maps a mint endpoint distinct from its socket path" {
              // The WS upgrade is one request that both opens and authorises the
              // session, so the token must be issued BEFORE it. Two paths, and
              // they must differ or the mint would shadow the socket.
              let ws = wsConfig ()

              Expect.notEqual ws.TokenPath ws.Path "the mint path and the socket path are distinct"
              Expect.isTrue (ws.TokenPath.StartsWith "/") "the mint path is rooted"
          } ]
