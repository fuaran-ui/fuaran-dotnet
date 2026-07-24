Imports Fuaran.UI.VisualBasic
Imports Csharp = Fuaran.UI.CSharp

' Phase 312 — authoring-parity by construction. For the ARIA-free leaf kinds (whose
' factories inject no accessibility), a VB XML-literal tree encodes byte-identically
' to the committed corpus fixture — the literal "author the fixture and byte-compare"
' for the subset where the bare corpus IS the authoring target. ARIA-bearing kinds are
' proven against the smart-ctor host (Program.vb / the C# Phase 307 parity test).
Module Authoring

    Sub Run(h As Harness)
        If Not Corpus.Available Then Return

        Match(h, "badge-1", <Badge id="badge-1" label="Beta" variant="Info"/>)

        Match(h, "list-1", <List id="list-1" ordered="true"><Item>First</Item><Item>Second</Item></List>)

        ' Phase 459 — Divider (→ Box.Separator) and Spacer (→ Box layout gap) retired
        ' from the wire; their corpus fixtures are gone, so they no longer author-match here.

        Match(h, "skel-1", <Skeleton id="skel-1" rows="3"/>)

        Match(h, "link-1", <Link id="link-1" href="/about" label="About us" rel="noopener" target="_blank" download="false"/>)

        Match(h, "image-1", <Image id="image-1" src="/avatar.png" alt="User avatar" variant="Avatar"/>)

        ' Phase 392 — Switch (state-bound conditional region). The fixture's Callout
        ' default carries injected live-region ARIA, so bare-corpus parity does not
        ' apply (the Mount posture); instead the switch-1-shaped XML-literal tree is
        ' proven byte-identical to the same tree built on the C# smart-ctor host —
        ' the full mapping (state-key, ordered cases, default) exercised end-to-end.
        Dim vbSwitch =
            <Switch id="switch-1" state-key="view">
                <Case match="details"><Markdown id="switch-details" text="Details view"/></Case>
                <Case match="summary"><Markdown id="switch-summary" text="Summary view"/></Case>
                <Default><Callout id="switch-default" heading="Pick a view" body="No view selected" tone="Info" dismissable="false"/></Default>
            </Switch>

        Dim csSwitch = Csharp.Fuaran.Switch(New Csharp.SwitchOptions With {
            .Id = "switch-1",
            .StateKey = "view",
            .Cases = {
                New Csharp.SwitchCase With {
                    .Match = "details",
                    .Child = Csharp.Fuaran.Markdown(New Csharp.MarkdownOptions With {.Id = "switch-details", .Text = "Details view"})},
                New Csharp.SwitchCase With {
                    .Match = "summary",
                    .Child = Csharp.Fuaran.Markdown(New Csharp.MarkdownOptions With {.Id = "switch-summary", .Text = "Summary view"})}},
            .Default = Csharp.Fuaran.Callout(New Csharp.CalloutOptions With {
                .Id = "switch-default",
                .Heading = "Pick a view",
                .Body = "No view selected",
                .Tone = Csharp.Tone.Info,
                .Dismissable = False})})

        h.ByteEqual("authoring parity switch-1 == smart-ctor host", FuaranXml.Encode(vbSwitch), csSwitch.Encode())
    End Sub

    Private Sub Match(h As Harness, fixtureId As String, el As System.Xml.Linq.XElement)
        h.ByteEqual($"authoring parity {fixtureId} == corpus", FuaranXml.Encode(el), Corpus.ReadFixture($"nodes/{fixtureId}.json"))
    End Sub

End Module
