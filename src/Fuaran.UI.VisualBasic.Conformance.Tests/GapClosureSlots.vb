Imports Fuaran.UI.VisualBasic

' Phase 873 — the XML-literal half of the §11 step-6 follow-up Phase 801 named. The
' gap-closure wave (861–867) added spec-record FIELDS, and the VB analyzer's pin is
' over kind NAMES, so nothing here could fail while the translator could not author
' them. These checks assert what that pin structurally cannot notice.
'
' Every check carries a negative control, because the narrowing flags are the shape
' where a vacuous pass is easiest: absent and `false` mean different things, so a
' translator that emitted `false` for an unstated column would look correct on every
' check that only asked whether the attribute reached the wire.
Module GapClosureSlots

    Sub Run(h As Harness)

        ' ── Phases 861 / 862 / 863 — the grid's behaviour declarations ──────────
        Dim grid = <DataGrid id="fleet" editable="true"
                       sort-state-key="fleet-sort"
                       default-sort-column="4" default-sort-direction="asc"
                       page-size="20" page-state-key="fleet-page"
                       edit-state-key="fleet-adjustments">
                       <Column type="text" label="Van" field="van"/>
                       <Column type="text" label="Notes" field="notes" sortable="false" editable="false"/>
                   </DataGrid>
        Dim gridJson = FuaranXml.Encode(grid)

        h.Check("861: sort-state-key reaches the wire", gridJson.Contains("""sortStateKey"":""fleet-sort"""), gridJson)
        h.Check("861: default-sort pair reaches the wire",
                gridJson.Contains("""defaultSort"":{""column"":4,""direction"":""asc""}"), gridJson)
        h.Check("862: page-size + page-state-key reach the wire",
                gridJson.Contains("""pageSize"":20") AndAlso gridJson.Contains("""pageStateKey"":""fleet-page"""), gridJson)
        h.Check("863: edit-state-key reaches the wire", gridJson.Contains("""editStateKey"":""fleet-adjustments"""), gridJson)
        h.Check("861/863: a column narrows itself out of both behaviours",
                gridJson.Contains("""editable"":false") AndAlso gridJson.Contains("""sortable"":false"), gridJson)

        ' The control: an undeclared grid declares none of it, and its columns carry
        ' NO narrowing flags — inherit is the absence, never `false`.
        Dim plain = <DataGrid id="fleet"><Column type="text" label="Van" field="van"/></DataGrid>
        Dim plainJson = FuaranXml.Encode(plain)
        h.Check("control: no behaviour keys when undeclared",
                Not plainJson.Contains("sortStateKey") AndAlso Not plainJson.Contains("pageSize") AndAlso
                Not plainJson.Contains("pageStateKey") AndAlso Not plainJson.Contains("editStateKey") AndAlso
                Not plainJson.Contains("defaultSort") AndAlso Not plainJson.Contains("sortable"), plainJson)

        ' ── Phase 801 — the static table's declared sort intent ─────────────────
        Dim table = <Table id="prices" sortable="true" default-sort-column="1" default-sort-direction="desc">
                        <Header>Item</Header>
                        <Header>Price</Header>
                        <Row><Cell>Widget</Cell><Cell>4.00</Cell></Row>
                    </Table>
        Dim tableJson = FuaranXml.Encode(table)
        h.Check("801: static sort intent reaches the wire",
                tableJson.Contains("""sortable"":true") AndAlso
                tableJson.Contains("""defaultSort"":{""column"":1,""direction"":""desc""}"), tableJson)

        Dim bareTable = <Table id="prices"><Header>Item</Header></Table>
        h.Check("801 control: no sort intent when undeclared",
                Not FuaranXml.Encode(bareTable).Contains("sortable"), FuaranXml.Encode(bareTable))

        ' ── Phase 864 — the declared field rule, all four shapes ────────────────
        Dim form = <Form id="hire">
                       <Field kind="text" id="work-email" label="Work email" required="true" rule-format="Email"/>
                       <Field kind="text" id="postcode" label="Postcode"
                           rule-pattern="[A-Z]{1,2}[0-9]" rule-message="Enter a UK postcode"/>
                       <Field kind="text" id="username" label="Username" rule-min-length="3" rule-max-length="24"/>
                       <Field kind="text" id="end-date" label="End date"
                           rule-compare-field="start-date" rule-compare-op="Gte"/>
                   </Form>
        Dim formJson = FuaranXml.Encode(form)

        h.Check("864: format rule reaches the wire", formJson.Contains("""rule"":{""format"":""email""}"), formJson)
        h.Check("864: pattern + message reach the wire",
                formJson.Contains("""pattern"":""[A-Z]{1,2}[0-9]""") AndAlso formJson.Contains("Enter a UK postcode"), formJson)
        h.Check("864: length bounds reach the wire",
                formJson.Contains("""maxLength"":24") AndAlso formJson.Contains("""minLength"":3"), formJson)
        h.Check("864: cross-field compare reaches the wire",
                formJson.Contains("""compare"":{""against"":{""$type"":""State"",""key"":""start-date""},""op"":""gte""}"), formJson)

        ' The control is load-bearing: an EMPTY rule is refused by the wire, so a
        ' translator that emitted one for every field would author documents no host
        ' accepts — and would do it silently, since nothing here reads `rule`.
        Dim ruleless = <Form id="hire"><Field kind="text" id="work-email" label="Work email" required="true"/></Form>
        h.Check("864 control: no rule when no slot is declared",
                Not FuaranXml.Encode(ruleless).Contains("""rule"""), FuaranXml.Encode(ruleless))

        ' ── Phase 867 — trend polarity, and the trend it is a statement about ───
        Dim inverted = <Metric id="wait" label="Avg wait" value="80" tone="Warning"
                           trend="-0.0734" trend-polarity="LowerIsBetter"/>
        Dim invertedJson = FuaranXml.Encode(inverted)
        h.Check("867: trend-polarity reaches the wire", invertedJson.Contains("""trendPolarity"":""LowerIsBetter"""), invertedJson)
        h.Check("867: the trend it qualifies reaches the wire too", invertedJson.Contains("""trend"":"), invertedJson)
        h.Check("867: tone survives an inverted polarity", invertedJson.Contains("""tone"":""Warning"""), invertedJson)

        ' The default is omitted on the wire, so a tile that says nothing about
        ' polarity must not start saying something.
        Dim ordinary = <Metric id="rev" label="Revenue" value="1234.5" trend="0.07"/>
        h.Check("867 control: HigherIsBetter is omitted at default",
                Not FuaranXml.Encode(ordinary).Contains("trendPolarity"), FuaranXml.Encode(ordinary))

    End Sub

End Module
