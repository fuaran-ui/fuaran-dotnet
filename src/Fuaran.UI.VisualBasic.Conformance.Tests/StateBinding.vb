Imports Fuaran.UI.VisualBasic
Imports Csharp = Fuaran.UI.CSharp

' ============================================================================
'  Phase 1154 — the state-binding spelling, asserted by ENCODING.
'
'  The claim under test is not "the translator calls Binding.State". It is that a
'  VB-authored writable control is the SAME WIRE DOCUMENT as the F#/C#-authored
'  one, because that is what the control write-back default keys on: handler
'  omitted AND the value binding directly a State source. Nothing about the
'  renderer's behaviour is asserted in prose here — the bytes decide it, and the
'  bytes are checked three ways:
'
'    1. against the shared C# factory host (the same surface the F# smart
'       constructors sit behind, and the one the VB translator drives);
'    2. against the COMMITTED CORPUS's own encoding of a no-default State slot,
'       so the expected fragment comes from outside this repo's VB tier;
'    3. through the shared decoder, so the document is one a host can read back.
'
'  Each positive check is paired with a negative control that must fail for the
'  right reason — a "$name" query binding, which is the shape this dialect
'  produced before this phase and the shape a write-back default cannot use.
' ============================================================================
Module StateBinding

    Sub Run(h As Harness)
        Disclosure(h)
        EditableGrid(h)
        Spelling(h)
    End Sub

    ' ── 1. A state-bound Disclosure. ────────────────────────────────────────
    '
    ' This is the control the Phase 577 VB sample said it could not express: an
    ' `open` slot the toggle can write back to. The VB tree and the C# host tree
    ' are the same document byte for byte.
    Private Sub Disclosure(h As Harness)
        Dim vb = <Disclosure id="details" heading="Advanced" open="$state.advancedOpen" default-open="false">
                     <Markdown id="details-body" text="Updated hourly."/>
                 </Disclosure>

        Dim cs = Csharp.Fuaran.Disclosure(New Csharp.DisclosureOptions With {
            .Id = "details",
            .Heading = CType("Advanced", Csharp.Text),
            .Open = Csharp.Binding.State(Of Boolean)("advancedOpen"),
            .DefaultOpen = False,
            .Children = {Csharp.Fuaran.Markdown(New Csharp.MarkdownOptions With {
                .Id = "details-body", .Text = "Updated hourly."})}})

        Dim json = FuaranXml.Encode(vb)
        h.ByteEqual("state-bound Disclosure == the shared factory host", json, cs.Encode())

        ' The write-back default's precondition, stated as bytes: the value
        ' binding is DIRECTLY a State source, and there is no handler to
        ' override it. Either half missing and the control is inert (FUARAN069).
        h.Check("state-bound Disclosure: open is a direct State slot",
                json.Contains(StateFragment("open", "advancedOpen")), json)
        h.Check("state-bound Disclosure: no handler suppresses the write-back default",
                Not json.Contains("onToggle"), json)

        h.Check("state-bound Disclosure round-trips through the shared decoder",
                Csharp.Decode.NodeRoundTrips(json))

        ' Negative control — the pre-1154 spelling. `$advancedOpen` is a host-fed
        ' query: a real binding, and one the write-back default cannot write to.
        ' It must NOT satisfy the check above, or that check proves nothing.
        Dim queryBound = FuaranXml.Encode(
            <Disclosure id="details" heading="Advanced" open="$advancedOpen" default-open="false">
                <Markdown id="details-body" text="Updated hourly."/>
            </Disclosure>)
        h.Check("negative control: a $name-bound Disclosure is NOT a State slot",
                Not queryBound.Contains(StateFragment("open", "advancedOpen")) AndAlso
                queryBound.Contains("""$type"":""Query"""), queryBound)
    End Sub

    ' ── 2. An editable-cell grid. ───────────────────────────────────────────
    '
    ' A grid's edits commit to a State key, so an editable grid over a read-only
    ' query source is display-only — the source binding is exactly what decides
    ' it. The two trees below differ in that one attribute and nothing else, so
    ' substituting the source fragment turns one into the other; a difference
    ' anywhere else would fail this and is what makes it more than a substring
    ' check.
    Private Sub EditableGrid(h As Harness)
        Dim stateGrid = FuaranXml.Encode(
            <DataGrid id="plan-grid" source="$state.planRows" editable="true" edit-state-key="planRows">
                <Column type="text" field="month" label="Month"/>
                <Column type="numeric" field="revenue" label="Revenue"/>
            </DataGrid>)

        Dim queryGrid = FuaranXml.Encode(
            <DataGrid id="plan-grid" source="$planRows" editable="true" edit-state-key="planRows">
                <Column type="text" field="month" label="Month"/>
                <Column type="numeric" field="revenue" label="Revenue"/>
            </DataGrid>)

        h.Check("editable grid: source is a direct State slot",
                stateGrid.Contains(StateFragment("source", "planRows")), stateGrid)

        h.Check("editable grid: the source binding is the ONLY difference from the query form",
                stateGrid.Replace(StateFragment("source", "planRows"), QueryFragment("source", "planRows")) = queryGrid,
                stateGrid)

        h.Check("editable grid round-trips through the shared decoder",
                Csharp.Decode.NodeRoundTrips(stateGrid))
    End Sub

    ' ── 3. The spelling itself. ─────────────────────────────────────────────
    Private Sub Spelling(h As Harness)
        ' A query whose NAME begins with the same letters is untouched — the
        ' discriminator is the "$state." prefix, not a "starts with $state" test.
        Dim stateful = FuaranXml.Encode(<Metric id="m" label="Load" value="$stateful"/>)
        h.Check("'$stateful' is still a query named 'stateful'",
                stateful.Contains("""$type"":""Query""") AndAlso stateful.Contains("""stateful"""),
                stateful)

        ' The prefix with no key is a translation error, not a query named "state"
        ' and not a silent State slot keyed on the empty string.
        h.Check("'$state' (no key) is a translation error", Malformed("$state"))
        h.Check("'$state.' (empty key) is a translation error", Malformed("$state."))

        ' A text slot takes the spelling too (AsText, not only the typed bindings).
        Dim heading = FuaranXml.Encode(<Heading id="h" text="$state.title" level="2"/>)
        h.Check("a text slot accepts the state spelling",
                heading.Contains("""$type"":""State""") AndAlso heading.Contains("""key"":""title"""),
                heading)
    End Sub

    Private Function Malformed(value As String) As Boolean
        Try
            FuaranXml.Encode(<Disclosure id="d" heading="H" open=<%= value %>><Markdown id="b" text="x"/></Disclosure>)
            Return False
        Catch ex As FuaranXmlException
            Return ex.Message.Contains("names no state key")
        End Try
    End Function

    ' The canonical no-default State encoding, taken from the committed corpus
    ' rather than written out here: `controls-declarative.json` carries a select
    ' whose `value` is a State slot with no declared default, which is exactly the
    ' document the dialect's `$state.<key>` produces. Reading the shape from the
    ' corpus means this suite cannot agree with a VB tier that has drifted away
    ' from the wire format — the key is the only thing substituted.
    Private ReadOnly CorpusStateShape As String = LoadCorpusStateShape()

    Private Function LoadCorpusStateShape() As String
        ' The same bytes, written out, for a checkout with no corpus beside it.
        Const fallback As String = """<<slot>>"":{""$type"":""State"",""key"":""<<key>>""}"
        If Not Corpus.Available Then Return fallback

        Const marker As String = """value"":{""$type"":""State"",""key"":""region""}"
        If Not Corpus.ReadFixture("nodes/controls-declarative.json").Contains(marker) Then Return fallback

        Return marker.Replace("value", "<<slot>>").Replace("region", "<<key>>")
    End Function

    Private Function StateFragment(slot As String, key As String) As String
        Return CorpusStateShape.Replace("<<slot>>", slot).Replace("<<key>>", key)
    End Function

    Private Function QueryFragment(slot As String, name As String) As String
        Return $"""{slot}"":{{""$type"":""Query"",""name"":""{name}""}}"
    End Function

End Module
