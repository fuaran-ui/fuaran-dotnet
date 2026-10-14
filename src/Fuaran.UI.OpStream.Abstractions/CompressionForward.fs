namespace Fuaran.UI

// ============================================================================
//  Phase 2058 — the one-release forward for the byte codec that moved out of
//  the core package.
//
//  `Utf8`, `Base64Url` and `Deflate` lived in `Fuaran.UI` (Compression.fs) and
//  had one consumer, the teleport codec in this package. They now live here, in
//  `Fuaran.UI.OpStream.Abstractions`, so the core package carries none of them.
//  These obsolete modules keep the old `Fuaran.UI.*` paths compiling for one
//  release, for a consumer that references this package; each member delegates
//  to its new home, so there is one implementation. Delete this file in the
//  release after 0.95.0.
// ============================================================================

open System

/// Obsolete forward for `Fuaran.UI.OpStream.Abstractions.Utf8` (Phase 2058).
[<Obsolete("Moved to Fuaran.UI.OpStream.Abstractions.Utf8 (Phase 2058); this forward is removed in the release after 0.95.0.")>]
module Utf8 =

    /// Forwards to `Fuaran.UI.OpStream.Abstractions.Utf8.encode`.
    let encode (s: string) : byte[] =
        Fuaran.UI.OpStream.Abstractions.Utf8.encode s

    /// Forwards to `Fuaran.UI.OpStream.Abstractions.Utf8.decode`.
    let decode (bytes: byte[]) : Result<string, string> =
        Fuaran.UI.OpStream.Abstractions.Utf8.decode bytes

/// Obsolete forward for `Fuaran.UI.OpStream.Abstractions.Base64Url` (Phase 2058).
[<Obsolete("Moved to Fuaran.UI.OpStream.Abstractions.Base64Url (Phase 2058); this forward is removed in the release after 0.95.0.")>]
module Base64Url =

    /// Forwards to `Fuaran.UI.OpStream.Abstractions.Base64Url.encode`.
    let encode (bytes: byte[]) : string =
        Fuaran.UI.OpStream.Abstractions.Base64Url.encode bytes

    /// Forwards to `Fuaran.UI.OpStream.Abstractions.Base64Url.decode`.
    let decode (s: string) : Result<byte[], string> =
        Fuaran.UI.OpStream.Abstractions.Base64Url.decode s

/// Obsolete forward for `Fuaran.UI.OpStream.Abstractions.Deflate` (Phase 2058).
[<Obsolete("Moved to Fuaran.UI.OpStream.Abstractions.Deflate (Phase 2058); this forward is removed in the release after 0.95.0.")>]
module Deflate =

    /// The same type as `Fuaran.UI.OpStream.Abstractions.Deflate.InflateError`.
    type InflateError = Fuaran.UI.OpStream.Abstractions.Deflate.InflateError

    /// Forwards to `Fuaran.UI.OpStream.Abstractions.Deflate.compress`.
    let compress (input: byte[]) : byte[] =
        Fuaran.UI.OpStream.Abstractions.Deflate.compress input

    /// Forwards to `Fuaran.UI.OpStream.Abstractions.Deflate.inflate`.
    let inflate (maxOutput: int) (data: byte[]) : Result<byte[], InflateError> =
        Fuaran.UI.OpStream.Abstractions.Deflate.inflate maxOutput data
