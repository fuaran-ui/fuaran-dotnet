Imports Fuaran.UI.VisualBasic
Imports Csharp = Fuaran.UI.CSharp

' Phase 310 smoke coverage (Phase 312 grows this to the full corpus). Every check
' authors a tree as a VB XML literal, translates it, and asserts its wire bytes.
Module Program

    Function Main() As Integer
        Dim h As New Harness()

        ' ── ARIA-free leaf kinds: the VB XML tree byte-matches the bare corpus. ──
        If Corpus.Available Then
            Dim heading = <Heading id="heading-1" text="Channel performance" level="2"/>
            h.ByteEqual("heading-1 == corpus", FuaranXml.Encode(heading), Corpus.ReadFixture("nodes/heading-1.json"))

            Dim markdown = <Markdown id="markdown-1" text="Updated hourly."/>
            h.ByteEqual("markdown-1 == corpus", FuaranXml.Encode(markdown), Corpus.ReadFixture("nodes/markdown-1.json"))
        Else
            Console.WriteLine("[smoke] wire-format-fixtures corpus absent — skipping corpus byte-compare.")
        End If

        ' ── A composite tree (Card > Metric): translates, carries injected ARIA,
        '    and round-trips through the shared decode facade. ────────────────────
        Dim card = <Card id="insights" heading="Insights">
                       <Metric id="revenue" label="Revenue" value="1234.5" format-currency="GBP" tone="Brand"/>
                   </Card>
        Dim cardJson = FuaranXml.Encode(card)
        h.Check("card encodes its id", cardJson.Contains("insights"), cardJson)
        h.Check("card contains nested metric", cardJson.Contains("Metric"))
        h.Check("metric carries injected ARIA", cardJson.Contains("liveRegion"), cardJson)
        h.Check("card round-trips through decode", Csharp.Decode.NodeRoundTrips(cardJson))

        ' ── Unknown-element error surfaces (not a silent drop). ─────────────────
        Dim unknown = <NotAKind id="x"/>
        Dim threw = False
        Try
            FuaranXml.Encode(unknown)
        Catch ex As FuaranXmlException
            threw = ex.Message.Contains("Unknown Fuaran element")
        End Try
        h.Check("unknown element raises FuaranXmlException", threw)

        ' A near-miss suggests the nearest kind.
        Dim typo = <Metrik id="x" label="y" value="1"/>
        Dim suggested = False
        Try
            FuaranXml.Encode(typo)
        Catch ex As FuaranXmlException
            suggested = ex.Message.Contains("Did you mean <Metric>?")
        End Try
        h.Check("near-miss element suggests the nearest kind", suggested)

        ' ── Negative control. ───────────────────────────────────────────────────
        If Corpus.Available Then
            Dim mutated = <Heading id="heading-1" text="MUTATED" level="2"/>
            h.Check("negative control: mutated heading != corpus",
                    FuaranXml.Encode(mutated) <> Corpus.ReadFixture("nodes/heading-1.json"))
        End If

        ' ── Full node-shape mapping-completeness guard (Phase 311). ─────────────
        Coverage.Run(h)

        ' ── The 861–867 behaviour + constraint slots (Phase 873). Ordered AHEAD of
        '    the corpus pass deliberately: the orphaned §21 shape-limit fixture family
        '    throws System.Text.Json's depth-64 default inside Conformance and aborts
        '    the process before Report runs — a pre-existing defect enumerated by
        '    fuaran#864, not repaired here and not a reason to leave these unreachable. ─
        GapClosureSlots.Run(h)

        ' ── Full corpus conformance + authoring parity (Phase 312). ─────────────
        Conformance.Run(h)
        Authoring.Run(h)

        ' ── Accessibility mirror-parity drift guard (Phase 313). ────────────────
        AccessibilityParity.Run(h)

        ' ── Fluent-from-VB parity: the doc's dual-authoring sample byte-matches. ─
        FluentAuthoring.Run(h)

        ' ── Runtime facade reaches VB through the C# veneer (Phase 559): a VB author
        '    validates / applies ops / verifies a hash chain via Csharp.* — VB writes
        '    no F# and gets the single shared engine through the established layering. ─
        Dim vbCard = FuaranXml.Translate(<Card id="k">
                                             <Metric id="a" label="A" value="1" tone="Brand"/>
                                         </Card>)
        h.Check("vb: validate reaches the C# veneer", Csharp.Fuaran.IsValid(vbCard))

        Dim m2 = FuaranXml.Translate(<Metric id="b" label="B" value="2" tone="Default"/>)
        Dim insertOp = Csharp.Ops.InsertChild("k", m2)
        Dim applied = Csharp.Fuaran.Apply(vbCard, insertOp)
        h.Check("vb: apply reaches the C# engine",
                applied.IsOk AndAlso applied.Value.Encode().Contains("""id"":""b"""),
                If(applied.Error Is Nothing, "", applied.Error.Message))

        Dim chain = New Csharp.OpStreamChain("vb-stream")
        Dim actor = Csharp.FuaranActor.Human("vb-user")
        Dim e1 = chain.Append(insertOp, actor)
        h.Check("vb: op-stream reaches the C# hash chain", chain.Verify().IsIntact AndAlso e1.Sequence = 1)

        Return h.Report("vb-conformance")
    End Function

End Module
