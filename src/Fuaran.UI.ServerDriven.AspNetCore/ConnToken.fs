module Fuaran.UI.ServerDriven.AspNetCore.ConnToken

// ============================================================================
//  Forwarding module (Phase 787).
//
//  The implementation moved to `Fuaran.UI.ServerDriven.ConnToken` — the
//  transport-agnostic core — so that BOTH live transports gate on one copy
//  rather than two. See that module's header for the reasoning and for the
//  Phase 211 design it carries.
//
//  This shim exists so a host that referenced the old path keeps compiling.
//  New code should call `Fuaran.UI.ServerDriven.ConnToken` directly.
// ============================================================================

/// See `Fuaran.UI.ServerDriven.ConnToken.freshSecret`.
let freshSecret () : byte[] =
    Fuaran.UI.ServerDriven.ConnToken.freshSecret ()

/// See `Fuaran.UI.ServerDriven.ConnToken.sign`.
let sign (secret: byte[]) (principal: string) (connId: string) : string =
    Fuaran.UI.ServerDriven.ConnToken.sign secret principal connId

/// See `Fuaran.UI.ServerDriven.ConnToken.verify`.
let verify (secret: byte[]) (principal: string) (token: string) : string option =
    Fuaran.UI.ServerDriven.ConnToken.verify secret principal token
