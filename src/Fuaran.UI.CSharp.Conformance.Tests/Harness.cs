using System;
using System.Collections.Generic;
using System.IO;

namespace Fuaran.UI.CSharp.Conformance.Tests;

/// <summary>A tiny assertion runner — the console-Exe shape the F# Expecto suites
/// use (run via <c>dotnet run</c>, nonzero exit on failure), so this suite hooks
/// into the same Build.fs Test target.</summary>
internal sealed class Harness
{
    private int _passed;
    private readonly List<string> _failures = new();

    public void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
        }
        else
        {
            _failures.Add(detail is null ? name : $"{name}: {detail}");
        }
    }

    public void ByteEqual(string name, string actual, string expected)
    {
        if (actual == expected)
        {
            _passed++;
            return;
        }

        int i = 0;
        int min = Math.Min(actual.Length, expected.Length);
        while (i < min && actual[i] == expected[i])
        {
            i++;
        }

        _failures.Add($"{name}: first diff at byte {i}\n    expected: {Excerpt(expected, i)}\n    actual:   {Excerpt(actual, i)}");
    }

    private static string Excerpt(string s, int at)
    {
        int start = Math.Max(0, at - 20);
        int len = Math.Min(60, s.Length - start);
        return (start > 0 ? "…" : "") + s.Substring(start, len) + (start + len < s.Length ? "…" : "");
    }

    public int Report(string suite)
    {
        Console.WriteLine($"[{suite}] {_passed} passed, {_failures.Count} failed.");
        foreach (var f in _failures)
        {
            Console.WriteLine($"  FAIL {f}");
        }

        return _failures.Count == 0 ? 0 : 1;
    }
}

/// <summary>Locates + reads the shared wire-format-fixtures corpus, walking up
/// from the test assembly so the lookup is independent of the working directory.</summary>
internal static class Corpus
{
    private static readonly Lazy<string?> RootLazy = new(Locate);

    /// <summary>The corpus root, or null when the corpus is absent (single-repo checkout).</summary>
    public static string? Root => RootLazy.Value;

    public static bool Available => Root is not null;

    private static string? Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "wire-format-fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Read a fixture file's exact bytes (as UTF-8 text, no trailing newline handling —
    /// the corpus files carry no trailing newline).</summary>
    public static string ReadFixture(string relativePath) =>
        File.ReadAllText(Path.Combine(Root!, relativePath));
}
