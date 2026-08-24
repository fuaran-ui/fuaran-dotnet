module Fuaran.UI.ServerDriven.ConnToken

// ============================================================================
//  Connection token — the SSE+POST connId authz binding (Phase 211, C1).
//
//  Before Phase 211 the correlation cookie was a bare `Guid.NewGuid()`: the POST
//  event handler routed on whatever `connId` the cookie carried, and the
//  registry lookup was the ENTIRE gate. A cookie is client-held, so the routing
//  token was effectively bearer — unbound to any principal and (for a leaked /
//  guessed id) replayable. For the business / regulated / multi-tenant classes
//  the server-driven tier targets, that is an authorization-bypass surface.
//
//  This module makes the connId **unforgeable and principal-bound by
//  construction** (default-deny by shape, not by discipline). The cookie value
//  is `connId "." base64url(HMAC-SHA256(secret, connId "|" principal))`:
//
//   - The HMAC is keyed by a server secret the client never sees, so a forged /
//     guessed `connId` carries no valid signature — `verify` returns `None` and
//     the handler answers 401 instead of routing it.
//   - The principal is folded INTO the signed pre-image, so the token is bound
//     to the user it was issued to. When the host layers auth in front of
//     `/live/*` (the documented floor), a cookie lifted by one user does not
//     verify under another's principal — the binding is cryptographic, not a
//     lookup. With no auth wired, the principal is the empty string for everyone
//     and the HMAC still closes the forgeability gap (the strict improvement
//     over a bare GUID).
//
//  Pure + headlessly testable — no ASP.NET type leaks in; the endpoint glue
//  resolves the principal from `HttpContext` and calls `sign` / `verify`.
//
//  It lives in the transport-agnostic CORE (Phase 787), not beside one backend.
//  Both live transports gate on this one implementation: SSE+POST mints at
//  stream-open and verifies on each POST; WebSocket mints at its token endpoint
//  and verifies at the upgrade. A per-backend copy is how the two postures
//  diverged in the first place — the WS backend shipped with none of Phase 211
//  while its own config comment claimed parity. One module, two callers, and a
//  transport-parity test that fails if either drifts.
//
//  `Fuaran.UI.ServerDriven.AspNetCore.ConnToken` remains as a forwarding module,
//  so a host that referenced the old path keeps compiling.
//
//  Server-only by construction (HMACSHA256 has no Fable mapping), so the whole
//  module sits under `#if !FABLE_COMPILER` — the core's established idiom for
//  server-side code that must not reach the client shim's transpile.
// ============================================================================

#if !FABLE_COMPILER

open System
open System.Security.Cryptography
open System.Text

/// A fresh 32-byte HMAC key from a cryptographic RNG — the secure-by-default
/// per-process secret `defaultConfig` uses when the host supplies none. Tokens
/// signed with it do not survive a process restart (a reconnecting client simply
/// re-opens the stream and is re-issued one); a host that needs
/// restart-stable / multi-node tokens supplies its own stable secret.
let freshSecret () : byte[] = RandomNumberGenerator.GetBytes 32

/// The base64url-encoded HMAC-SHA256 of `connId "|" principal` under `secret`.
let private mac (secret: byte[]) (connId: string) (principal: string) : string =
    use h = new HMACSHA256(secret)
    let bytes = h.ComputeHash(Encoding.UTF8.GetBytes(connId + "|" + principal))
    // base64url (cookie-safe: no '+' '/' '=' that need escaping).
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

/// Sign a freshly-minted `connId`, binding it to `principal`. The result is the
/// cookie value the stream-open handler sets; the POST handler round-trips it
/// through `verify`.
let sign (secret: byte[]) (principal: string) (connId: string) : string =
    connId + "." + mac secret connId principal

/// Verify a cookie token against the request's resolved `principal`, returning
/// the bound `connId` only when the signature recomputes (constant-time compare)
/// AND was issued to this principal. `None` on any tamper: no separator, an
/// altered `connId`, a forged / absent signature, or a principal mismatch. The
/// caller treats `None` as 401 (default-deny) and never routes the event.
let verify (secret: byte[]) (principal: string) (token: string) : string option =
    match token.LastIndexOf '.' with
    | i when i > 0 ->
        let connId = token.Substring(0, i)
        let presented = token.Substring(i + 1)
        let expected = mac secret connId principal

        if
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes presented, Encoding.UTF8.GetBytes expected)
        then
            Some connId
        else
            None
    | _ -> None
#endif
