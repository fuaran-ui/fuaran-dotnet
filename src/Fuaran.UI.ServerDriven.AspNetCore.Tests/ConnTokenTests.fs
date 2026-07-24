module Fuaran.UI.ServerDriven.AspNetCore.Tests.ConnTokenTests

// ─── Phase 211 (C1): the connId authz binding ──────────────────────────────
//
// The cookie value is no longer a bare GUID — it is `connId.HMAC(secret, connId|
// principal)`. A forged / cross-principal / tampered token fails verification,
// so the POST handler answers 401 instead of routing it. `ConnToken` is pure, so
// the binding is unit-tested headlessly (the ASP.NET handler is glue around it).

open Expecto
open Fuaran.UI.ServerDriven.AspNetCore

[<Tests>]
let connTokenTests =
    let secret = ConnToken.freshSecret ()

    testList
        "ConnToken (connId authz binding)"
        [ test "a token signed for a principal verifies back to its connId" {
              let token = ConnToken.sign secret "alice" "conn-1"

              match ConnToken.verify secret "alice" token with
              | Some connId -> Expect.equal connId "conn-1" "round-trips to the bound connId"
              | None -> failtest "a genuine token must verify"
          }

          test "a bare GUID (no signature) is rejected — the pre-211 forgery" {
              Expect.isNone (ConnToken.verify secret "alice" "conn-1") "unsigned bare id → None (401)"
          }

          test "a token replayed under a DIFFERENT principal is rejected (the binding)" {
              let token = ConnToken.sign secret "alice" "conn-1"
              Expect.isNone (ConnToken.verify secret "mallory" token) "cross-principal replay → None (401)"
          }

          test "a tampered signature is rejected" {
              let token = ConnToken.sign secret "alice" "conn-1"
              let tampered = token.Substring(0, token.Length - 1) + "X"
              Expect.isNone (ConnToken.verify secret "alice" tampered) "flipped signature → None"
          }

          test "a swapped connId (same signature) is rejected" {
              let token = ConnToken.sign secret "alice" "conn-1"
              // Keep the signature, swap the connId half — must not verify.
              let sigPart = token.Substring(token.LastIndexOf '.' + 1)

              Expect.isNone
                  (ConnToken.verify secret "alice" ("conn-2." + sigPart))
                  "connId not covered by the mac → None"
          }

          test "a token from a different secret is rejected (unforgeable without the key)" {
              let token = ConnToken.sign secret "alice" "conn-1"
              let otherSecret = ConnToken.freshSecret ()
              Expect.isNone (ConnToken.verify otherSecret "alice" token) "wrong key → None"
          }

          test "a token with no separator is rejected" {
              Expect.isNone (ConnToken.verify secret "alice" "nodothere") "no '.' → None"
          }

          test "the anonymous principal ('') still binds — a forged id is unforgeable even with no auth" {
              let token = ConnToken.sign secret "" "conn-anon"
              Expect.equal (ConnToken.verify secret "" token) (Some "conn-anon") "anon token verifies"
              Expect.isNone (ConnToken.verify secret "" "conn-anon") "but a bare id under anon is still rejected"
          } ]
