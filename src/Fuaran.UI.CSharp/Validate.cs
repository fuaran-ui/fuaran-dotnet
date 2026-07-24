using System.Collections.Generic;
using FsCanon = Fuaran.UI.OpStream.Abstractions.CanonicalJson;
using FsDecode = Fuaran.UI.Ops.JsonDecode;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Phase 559 — the C# runtime validation facade.
//
//  HONEST SCOPE. The FUARAN* validator (`Fuaran.UI.Validator`) is a *build-time
//  F#-AST walker*: it needs F# source + a `.fsproj` + FSharp.Compiler.Service,
//  so it cannot inspect a runtime `Node<obj>` tree and there is nothing for a C#
//  facade to delegate to for it. What a non-F# author CAN run at runtime is the
//  pre-emit self-check the PoC findings recommend
//  (`docs/csharp-authoring-poc-findings.md` §6 "Validator parity"): encode the
//  tree canonically and decode it back through the shared F# codec. A tree that
//  round-trips clean is wire-survivable; a decode reject (or a non-stable
//  re-encode) is a structural finding carrying the canonical §6 code + path.
//
//  This is delegation, not a parallel checker — it runs the same F# encoder +
//  decoder every host runs, so a "clean" verdict means the same thing on every
//  tier. It does NOT replace the build-time FUARAN* rules (accessibility, binding
//  resolution, custom-health, …), which stay an F#-authoring-time gate.
// ============================================================================

/// <summary>One runtime validation finding — a structural / wire-survivability issue.</summary>
public sealed record ValidationFinding
{
    /// <summary>A stable code: a canonical decode code (e.g. <c>"MISSING_FIELD"</c>) or <c>"NOT_WIRE_STABLE"</c>.</summary>
    public required string Code { get; init; }

    /// <summary>A <c>$</c>-rooted path to the issue, when the decoder localised it (else <c>"$"</c>).</summary>
    public required string Path { get; init; }

    /// <summary>A human/AI-readable description.</summary>
    public required string Message { get; init; }
}

/// <summary>The C# runtime pre-emit self-check over the shared F# codec.</summary>
public static partial class Fuaran
{
    /// <summary>Validate a node's wire-survivability: encode it canonically, decode it back
    /// through the shared F# codec, and confirm the re-encode is byte-stable. Returns an
    /// empty list for a clean tree; otherwise a finding per structural problem. This is the
    /// runtime self-check, NOT the build-time FUARAN* AST validator (which is F#-only).</summary>
    public static IReadOnlyList<ValidationFinding> Validate(FuaranNode node)
    {
        var findings = new List<ValidationFinding>();

        var json = node.Encode();
        var decoded = FsDecode.decodeNodeObj(json);

        if (!decoded.IsOk)
        {
            var e = DecodeError.FromFs(decoded.ErrorValue);
            findings.Add(new ValidationFinding { Code = e.Code, Path = e.Path, Message = e.Message });
            return findings;
        }

        var reencoded = FsCanon.encodeNode(decoded.ResultValue);
        if (reencoded != json)
        {
            findings.Add(new ValidationFinding
            {
                Code = "NOT_WIRE_STABLE",
                Path = "$",
                Message = "The node does not re-encode to identical bytes after a decode round-trip — it is not wire-stable.",
            });
        }

        return findings;
    }

    /// <summary>Whether <paramref name="node"/> passes the runtime self-check (no findings).</summary>
    public static bool IsValid(FuaranNode node) => Validate(node).Count == 0;
}
