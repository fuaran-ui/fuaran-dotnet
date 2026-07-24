using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fuaran.UI.Analyzers;

/// <summary>
/// Reads the shared <c>fuaran-validator.manifest.json</c> — the SAME artefact the
/// F# validator reads — for the name-resolution rules (FUARAN010, …). The manifest
/// is supplied as an <c>AdditionalFiles</c> entry named
/// <c>fuaran-validator.manifest.json</c>; a consumer wires it (typically via
/// <c>Directory.Build.props</c>) alongside an <c>.editorconfig</c>
/// <c>fuaran_manifest_path</c> pointer. When absent, name-resolution rules stay
/// silent (matching the F# validator's manifest-missing behaviour).
/// </summary>
/// <remarks>
/// The manifest's <c>queries</c> / <c>msgCases</c> are flat arrays of identifier
/// strings, so they are read with a small dependency-free scanner rather than a
/// JSON library — an analyzer must not drag <c>System.Text.Json</c> (+ its
/// transitive deps) into the compiler/IDE load path, where version conflicts are
/// a well-known failure mode.
/// </remarks>
internal sealed class Manifest
{
    /// <summary>The registered query names (FUARAN010).</summary>
    public ImmutableHashSet<string> Queries { get; }

    /// <summary>The registered Msg case names (FUARAN020, when a veneer surface exists).</summary>
    public ImmutableHashSet<string> MsgCases { get; }

    /// <summary>Whether a manifest was found at all (rules stay silent when false).</summary>
    public bool Present { get; }

    private Manifest(ImmutableHashSet<string> queries, ImmutableHashSet<string> msgCases, bool present)
    {
        Queries = queries;
        MsgCases = msgCases;
        Present = present;
    }

    private static readonly Manifest Empty = new(
        ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty, present: false);

    private const string ManifestFileName = "fuaran-validator.manifest.json";

    public static Manifest Load(AnalyzerOptions options)
    {
        var file = options.AdditionalFiles.FirstOrDefault(f =>
            f.Path.EndsWith(ManifestFileName, StringComparison.OrdinalIgnoreCase));

        var text = file?.GetText()?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        return new Manifest(
            ExtractStringArray(text!, "queries"),
            ExtractStringArray(text!, "msgCases"),
            present: true);
    }

    /// <summary>
    /// Extract the string members of the top-level JSON array named
    /// <paramref name="key"/>. Handles quote escapes; tolerant of whitespace and
    /// key order. Returns empty when the key is absent or not an array.
    /// </summary>
    private static ImmutableHashSet<string> ExtractStringArray(string json, string key)
    {
        var keyToken = "\"" + key + "\"";
        var keyAt = json.IndexOf(keyToken, StringComparison.Ordinal);
        if (keyAt < 0)
        {
            return ImmutableHashSet<string>.Empty;
        }

        var open = json.IndexOf('[', keyAt + keyToken.Length);
        if (open < 0)
        {
            return ImmutableHashSet<string>.Empty;
        }

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        var inString = false;
        var escaped = false;

        for (var i = open + 1; i < json.Length; i++)
        {
            var ch = json[i];

            if (inString)
            {
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    builder.Add(sb.ToString());
                    sb.Clear();
                    inString = false;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inString = true;
            }
            else if (ch == ']')
            {
                break; // end of the array (safe: we are outside any string here)
            }
        }

        return builder.ToImmutable();
    }
}
