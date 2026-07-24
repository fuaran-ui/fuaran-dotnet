namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 306 — authoring-parity by construction. For the ARIA-free leaf kinds (whose
// smart ctors inject no accessibility), a C#-AUTHORED tree encodes byte-identically
// to the committed corpus fixture — task 2's literal "author via the C# builders and
// assert byte-identical to the committed fixture", for the subset where the bare
// corpus IS the authoring target. ARIA-bearing kinds are proven against the F#
// smart-ctor host in Program.cs / Accessibility.cs (the Phase 307 reconciliation).
internal static class Authoring
{
    public static void Run(Harness h)
    {
        if (!Corpus.Available)
        {
            return;
        }

        Match(h, "badge-1", Fuaran.Badge(new() { Id = "badge-1", Label = "Beta", Variant = BadgeVariant.Info }));

        Match(h, "list-1", Fuaran.List(new()
        {
            Id = "list-1",
            Items = [(Text)"First", (Text)"Second"],
            Ordered = true,
        }));

        // Phase 459 — Divider (→ Box.Separator) and Spacer (→ Box layout gap) retired
        // from the wire; their corpus fixtures are gone, so they no longer author-match here.

        Match(h, "skel-1", Fuaran.Skeleton(new() { Id = "skel-1", Rows = 3 }));

        Match(h, "link-1", Fuaran.Link(new()
        {
            Id = "link-1",
            Href = "/about",
            Label = "About us",
            Rel = "noopener",
            Target = "_blank",
            Download = false,
        }));

        Match(h, "image-1", Fuaran.Image(new()
        {
            Id = "image-1",
            Src = "/avatar.png",
            Alt = "User avatar",
            Variant = ImageVariant.Avatar,
        }));
    }

    private static void Match(Harness h, string fixtureId, FuaranNode authored) =>
        h.ByteEqual($"authoring parity {fixtureId} == corpus", authored.Encode(), Corpus.ReadFixture($"nodes/{fixtureId}.json"));
}
