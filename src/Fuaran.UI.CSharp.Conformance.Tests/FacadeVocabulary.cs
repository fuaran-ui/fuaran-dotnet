using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.FSharp.Reflection;
using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 1532 — the facade ⊇ wire-DU pin.
//
// Coverage.cs pins the NODE vocabulary: every `NodeKind` case has a `Fuaran.X`
// factory. Nothing pinned the VALUE vocabularies the nodes are built out of —
// `Binding`, `Action`, `Format`, `CellFormat` — and the consequence was a veneer
// that could author every node shape in the language and almost none of the
// declarative primitives the language leads with. A C# (and so a VB) author had no
// spelling for "now", for a translated string with arguments, for a form field's
// local buffer, for a host capability, for writing a reactive slot, for committing
// a field, for a duration. Each gap was invisible because no check quantified over
// the DUs at all.
//
// This is that check, on Coverage.cs's shape: reflect the F# union cases, assert a
// public static member of the corresponding facade type authors each one. A case
// added to any of the four DUs fails here with no edit to this file — which is what
// makes the §11 forward-coupling rule reach the veneer rather than stopping at the
// F# tier.
//
// EXCLUSIONS are a table with a reason per entry, and the reasons are all one
// reason: the case's payload is a HOST CLOSURE with no wire projection, so a veneer
// whose trees ARE serialised must not be able to mint it (the argument
// `FuaranAction`'s own remarks make about `Dispatch`). The table is asserted in BOTH
// directions — an excluded case must have no member, so an exclusion cannot quietly
// outlive its reason.
internal static class FacadeVocabulary
{
    /// <summary>A DU case the veneer deliberately cannot author, and why.</summary>
    private readonly record struct Exclusion(string Du, string Case, string Reason);

    private static readonly Exclusion[] Exclusions =
    [
        new("Binding", "Computed",
            "carries a host closure as its whole payload; `{\"$type\":\"Computed\",\"fn\":\"<closure>\"}` is the entire encoding, so a serialised Computed arrives as a binding that resolves to nothing"),
        new("Action", "Dispatch",
            "carries a host message as its payload; the canonical encoder drops it and a decoding host rebuilds the `<closure>` sentinel, so a serialised Dispatch is an affordance that renders, fires, and does nothing"),
        new("CellFormat", "Custom",
            "carries a host `CellValue -> string` closure; the wire cannot carry the function, so a decoded Custom formats nothing"),
    ];

    /// <summary>
    /// Cases whose facade member is named differently from the DU case, or which one
    /// member cannot author alone. A case maps to the SET of members that author it,
    /// and every listed member is REQUIRED, not any-of — the `Coverage.Alias` rule,
    /// for the same reason: a half-authored case is exactly what this pin exists to
    /// catch.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Alias =
        new Dictionary<string, string[]>
        {
            // `Call` is three members because the wire's `into:` target is a closed
            // two-case DU and the veneer authors one member per target rather than
            // one taking a discriminator (the `Media` precedent in Coverage.cs).
            ["Action.Call"] = ["Call", "CallIntoState", "CallIntoQuery"],
            // `SetState` carries `value` XOR `valueFrom`; two members, so the
            // exclusivity the wire enforces is unreachable rather than merely unused.
            ["Action.SetState"] = ["SetState", "SetStateFrom"],
            // `Selection` is authorable with a typed accessor or with the declarative
            // row-field projection; the latter is the wire-expressible one.
            ["Binding.Selection"] = ["Selection", "SelectionField"],
        };

    public static void Run(Harness h)
    {
        Pin(h, "Binding", typeof(FsGen.Binding<object>), typeof(Binding));
        Pin(h, "Action", typeof(FsGen.Action<object>), typeof(FuaranAction));
        Pin(h, "Format", typeof(FsGen.Format), typeof(LocaleFormat));
        Pin(h, "CellFormat", typeof(FsGen.CellFormat), typeof(CellFormat));

        ExclusionsAreLive(h);
        WireCases(h);
        NegativeControl(h);
    }

    /// <summary>
    /// The pin above proves a member EXISTS. This proves each Phase-1532 member
    /// builds the case it is named for — the failure a name-only pin cannot see is a
    /// member that compiles, ships, and constructs the wrong DU case, which reaches
    /// an author as a tree that renders something else.
    /// </summary>
    private static void WireCases(Harness h)
    {
        void Encodes(string what, string json, string expected) =>
            h.Check(
                $"wire: {what} encodes as {expected}",
                json.Contains(expected, StringComparison.Ordinal),
                json);

        string Href(Binding<string> href) =>
            Fuaran.Link(new() { Id = "l", Href = href, Label = "x" }).Encode();

        string Click(FuaranAction action) =>
            Fuaran.Button(new() { Id = "b", Label = "x", OnClick = action }).Encode();

        Encodes("Binding.Now", Href(Binding.Now()), "\"$type\":\"Now\"");
        Encodes("Binding.Now(grain)", Href(Binding.Now(TimeGrain.Day)), "\"grain\":\"Day\"");
        Encodes("Binding.I18n", Href(Binding.I18n("greet")), "\"$type\":\"I18n\"");
        Encodes(
            "Binding.I18n(args)",
            Href(Binding.I18n("greet", ("count", Binding.State<Payload>("n")))),
            "\"args\"");
        Encodes("Binding.Local", Href(Binding.Local("x", LocalFlush.OnBlur)), "\"$type\":\"Local\"");
        Encodes(
            "Binding.Local(debounce)",
            Href(Binding.Local("x", LocalFlush.OnDebounce(250))),
            "\"milliseconds\":250");
        Encodes("Binding.Invoke", Href(Binding.Invoke<string>("cap", ("a", "1"))), "\"$type\":\"Invoke\"");
        Encodes(
            "Binding.Expr",
            Href(Binding.Expr<string>(global::Fuaran.Core.ColExpr.NewCol("amount"))),
            "\"$type\":\"Expr\"");
        Encodes(
            "Binding.Transform",
            Href(Binding.Transform<string>(
                TransformSource.Data(global::Fuaran.Core.DataSource.NewRef("sales")),
                [global::Fuaran.Core.Transform.NewLimit(1, 0)])),
            "\"$type\":\"Transform\"");

        Encodes("FuaranAction.SetState", Click(FuaranAction.SetState("k", 1)), "\"$type\":\"SetState\"");
        Encodes(
            "FuaranAction.SetStateFrom",
            Click(FuaranAction.SetStateFrom("k", Binding.State<Payload>("other"))),
            "\"valueFrom\"");
        Encodes("FuaranAction.CommitLocal", Click(FuaranAction.CommitLocal("field")), "\"$type\":\"CommitLocal\"");
        Encodes("FuaranAction.Invoke", Click(FuaranAction.Invoke("cap")), "\"$type\":\"Invoke\"");
        Encodes(
            "FuaranAction.ReadFileBody",
            Click(FuaranAction.ReadFileBody("f", FileEncoding.Base64)),
            "\"encoding\":\"Base64\"");
        Encodes("FuaranAction.AiTool", Click(FuaranAction.AiTool("summarise", "x")), "\"$type\":\"AiTool\"");

        Encodes(
            "LocaleFormat.Duration",
            Href(Binding.Format(1.0, LocaleFormat.Duration(DurationUnit.Minutes, DurationStyle.Clock))),
            "\"$type\":\"Duration\"");
        Encodes(
            "LocaleFormat.Since",
            Href(Binding.Format(1.0, LocaleFormat.Since(RelativeTimeUnit.Day))),
            "\"$type\":\"Since\"");
        Encodes(
            "CellFormat.Duration",
            Fuaran.Metric(new()
            {
                Id = "m",
                Label = "x",
                Value = 1.0,
                Format = CellFormat.Duration(DurationUnit.Hours, DurationStyle.Long),
            }).Encode(),
            "\"$type\":\"Duration\"");
        Encodes(
            "CellFormat.RelativeTime",
            Fuaran.Metric(new()
            {
                Id = "m",
                Label = "x",
                Value = 1.0,
                Format = CellFormat.RelativeTime(RelativeTimeUnit.Week),
            }).Encode(),
            "\"$type\":\"RelativeTime\"");

        // The negative control for this block: an unadorned tree must carry none of
        // the discriminators above, so a `Contains` that always matched would show.
        var plain = Fuaran.Link(new() { Id = "l", Href = "/x", Label = "x" }).Encode();

        h.Check(
            "negative control: a plain Link carries none of the Phase 1532 discriminators",
            !plain.Contains("\"$type\":\"Now\"", StringComparison.Ordinal)
            && !plain.Contains("\"$type\":\"Transform\"", StringComparison.Ordinal),
            plain);
    }

    /// <summary>
    /// The public static members of <paramref name="facade"/> that author a facade
    /// value — methods and properties alike, since a payload-free case authors as a
    /// property (`FuaranAction.Print`, `CellFormat.None`).
    /// </summary>
    private static HashSet<string> AuthoringMembers(Type facade)
    {
        // The facade's own wrapper type is the return type for a struct facade
        // (LocaleFormat / CellFormat); for the class facades it is the wrapper
        // (Binding<T> / FuaranAction). `Binding.Query<T>` returns `Binding<T>`, an
        // open generic instantiation, so compare on the open definition.
        bool Authors(Type returned) =>
            returned == facade
            || (returned.IsGenericType && returned.GetGenericTypeDefinition().Name.StartsWith(facade.Name, StringComparison.Ordinal))
            || returned.Name.StartsWith(facade.Name, StringComparison.Ordinal);

        var names = facade
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && Authors(m.ReturnType))
            .Select(m => m.Name);

        var properties = facade
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => Authors(p.PropertyType))
            .Select(p => p.Name);

        return names.Concat(properties).ToHashSet(StringComparer.Ordinal);
    }

    private static string[] MembersFor(string du, string caseName) =>
        Alias.TryGetValue($"{du}.{caseName}", out var mapped) ? mapped : [caseName];

    private static bool IsExcluded(string du, string caseName) =>
        Exclusions.Any(e => e.Du == du && e.Case == caseName);

    private static void Pin(Harness h, string du, Type fsUnion, Type facade)
    {
        var members = AuthoringMembers(facade);

        foreach (var unionCase in FSharpType.GetUnionCases(fsUnion, null))
        {
            if (IsExcluded(du, unionCase.Name))
            {
                continue;
            }

            var expected = MembersFor(du, unionCase.Name);
            var missing = expected.Where(n => !members.Contains(n)).ToList();

            h.Check(
                $"vocabulary: {facade.Name}.{string.Join(" + ", expected)} authors {du}.{unionCase.Name}",
                missing.Count == 0,
                $"no public static {string.Join(" / ", missing.Select(n => $"{facade.Name}.{n}(…)"))} — "
                + $"the veneer cannot author {du}.{unionCase.Name}, so a C# (or VB) author has no spelling for it. "
                + "Add the member, or add an Exclusion here with the reason its payload cannot cross the wire");
        }
    }

    /// <summary>
    /// The exclusion table read the other way: an excluded case must have NO facade
    /// member. Without this an exclusion outlives its reason silently — the member
    /// lands, the pin stays quiet, and the table now documents a boundary that is not
    /// there.
    /// </summary>
    private static void ExclusionsAreLive(Harness h)
    {
        var facades = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["Binding"] = typeof(Binding),
            ["Action"] = typeof(FuaranAction),
            ["Format"] = typeof(LocaleFormat),
            ["CellFormat"] = typeof(CellFormat),
        };

        var unions = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["Binding"] = typeof(FsGen.Binding<object>),
            ["Action"] = typeof(FsGen.Action<object>),
            ["Format"] = typeof(FsGen.Format),
            ["CellFormat"] = typeof(FsGen.CellFormat),
        };

        foreach (var exclusion in Exclusions)
        {
            // The case still exists on the DU — an exclusion naming a case that was
            // renamed or removed is a stale table entry, not a boundary.
            var cases = FSharpType.GetUnionCases(unions[exclusion.Du], null).Select(c => c.Name);
            h.Check(
                $"exclusion: {exclusion.Du}.{exclusion.Case} is still a case of the wire DU",
                cases.Contains(exclusion.Case, StringComparer.Ordinal),
                "the exclusion table names a case the DU no longer has — remove the entry");

            h.Check(
                $"exclusion: {exclusion.Du}.{exclusion.Case} has no facade member ({exclusion.Reason})",
                !AuthoringMembers(facades[exclusion.Du]).Contains(exclusion.Case),
                $"a member now authors {exclusion.Du}.{exclusion.Case}, which the exclusion table says cannot cross "
                + "the wire. Either the member mints an affordance that arrives dead, or the exclusion is stale — "
                + "decide which and edit the table");
        }
    }

    /// <summary>
    /// The pin is only worth its green if it can go red. A case name the facade
    /// certainly does not author must be reported missing by the same lookup the
    /// checks above use.
    /// </summary>
    private static void NegativeControl(Harness h)
    {
        var members = AuthoringMembers(typeof(FuaranAction));

        h.Check(
            "negative control: the member lookup reports a fabricated case as missing",
            !members.Contains("NoSuchActionCase"),
            "the authoring-member lookup matched a case that does not exist — it is matching too broadly to fail");

        h.Check(
            "negative control: the member lookup finds a case that IS authored",
            members.Contains("Notify"),
            "the authoring-member lookup found nothing for `Notify`, so an empty result proves nothing");
    }
}
