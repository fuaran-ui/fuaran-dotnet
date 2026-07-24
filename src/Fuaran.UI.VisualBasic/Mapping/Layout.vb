Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Phase 311 — the remaining Layout kinds (Dashboard/Stack/Grid/Card ship in the
' foundation). Each reads attributes into the Wave 45 options record + calls the
' factory, inheriting ARIA + the shared encoder.
Friend Module LayoutMapping

    Friend Sub Register(d As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)))

        d("SplitPanel") = Function(el) Csharp.Fuaran.SplitPanel(
            New Csharp.SplitPanelOptions With {.Id = Attr(el, "id"), .Weight = AttrDouble(el, "weight", 0.5), .Children = Kids(el)})

        ' <Tabs> intentionally authors NO explicit tab headers/tags — labels are inferred
        ' from each child body's heading. FORWARD-COUPLING TRIPWIRE: if you add a
        ' <TabHeader>/<TabTag> child (or header/tag attributes) here, also port the F#
        ' validator's FUARAN047/048 count-mismatch rules into the VB XML analyzer
        ' (Vocabulary.cs Tabs entry + FuaranVbXmlAnalyzer) and add tests in the same
        ' change — the deferral in Phase 315 holds ONLY while this surface stays absent.
        d("Tabs") = Function(el) Csharp.Fuaran.Tabs(
            New Csharp.TabsOptions With {
                .Id = Attr(el, "id"),
                .Orientation = AsEnum(Of Csharp.Orientation)(Attr(el, "orientation"), Csharp.Orientation.Horizontal),
                .ActiveIndex = OptIntBinding(el, "active-index"),
                .Children = Kids(el)})

        d("Stepper") = Function(el) Csharp.Fuaran.Stepper(
            New Csharp.StepperOptions With {.Id = Attr(el, "id"), .ActiveStep = OptIntBinding(el, "active-step"), .Children = Kids(el)})

        d("SummaryList") = Function(el) Csharp.Fuaran.SummaryList(
            New Csharp.SummaryListOptions With {.Id = Attr(el, "id"), .Heading = OptText(el, "heading"), .Children = Kids(el)})

        d("Disclosure") = Function(el) Csharp.Fuaran.Disclosure(
            New Csharp.DisclosureOptions With {
                .Id = Attr(el, "id"),
                .Heading = AsText(Attr(el, "heading")),
                .Open = OptBoolBinding(el, "open"),
                .DefaultOpen = AttrBool(el, "default-open"),
                .Children = Kids(el)})

        d("Modal") = Function(el) Csharp.Fuaran.Modal(
            New Csharp.ModalOptions With {
                .Id = Attr(el, "id"),
                .Open = OptBoolBinding(el, "open"),
                .Heading = OptText(el, "heading"),
                .Dismissable = AttrBool(el, "dismissable", True),
                .Children = Kids(el)})

        d("ScrollArea") = Function(el) Csharp.Fuaran.ScrollArea(
            New Csharp.ScrollAreaOptions With {
                .Id = Attr(el, "id"),
                .Orientation = AsEnum(Of Csharp.ScrollOrientation)(Attr(el, "orientation"), Csharp.ScrollOrientation.Vertical),
                .MaxHeight = OptIntAttr(el, "max-height"),
                .MaxWidth = OptIntAttr(el, "max-width"),
                .Children = Kids(el)})
    End Sub

End Module
