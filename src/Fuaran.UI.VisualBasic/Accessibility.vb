Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' ============================================================================
'  Phase 313 — accessibility-defaults posture. MIRROR F# BY CONSTRUCTION.
'
'  The VB veneer maintains NO independent ARIA table. The translator routes each
'  XML element through the Wave 45 C# factory for its kind, which calls the F#
'  smart constructor — so a VB-authored node inherits exactly the per-component
'  `accessibility` shape the F# host emits (`Defaults.Accessibility.*`). There is
'  nothing here to keep in sync with F# — the values ARE F#'s, twice removed
'  (VB → C# factory → F# smart ctor).
'
'  ── Corpus reconciliation decision (cross-host) ─────────────────────────────
'  Identical to the C# decision (Phase 307): the shared wire-format-fixtures corpus
'  is authored BARE (no `accessibility` key); the smart-ctor hosts (F#, C#, and this
'  VB veneer) all inject the same per-component ARIA and so diverge from the bare
'  fixture identically. DECISION: keep the corpus bare — no regeneration. The
'  veneer's byte-identity target is the smart-ctor host (asserted by the mirror-
'  parity test); corpus conformance uses the ARIA-agnostic decode round-trip.
' ============================================================================

''' <summary>Inspection helper for the ARIA a VB-authored tree inherits from the F#
''' smart constructors (there is no independent VB table).</summary>
Public Module Accessibility

    ''' <summary>The canonical <c>accessibility</c> JSON fragment the XML-authored tree
    ''' carries (as it appears on the wire), or Nothing when it emits no ARIA. Delegates
    ''' to the shared inspection surface, so it is exactly what a conformant host sees.</summary>
    Public Function AriaJson(el As XElement) As String
        Return Csharp.Accessibility.AriaJson(FuaranXml.Translate(el))
    End Function

End Module
