Imports Fuaran.UI.CSharp
Imports Fuaran.UI.VisualBasic
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.Hosting
Imports Microsoft.FSharp.Core

' ============================================================================
'  Phase 577 - a Fuaran tree authored in VB XML LITERALS, rendered live in the
'  browser.
'
'  Same four moving parts as the C# leg: author, encode for transport, serve the
'  embedded renderer, emit the mount snippet. There is no Node toolchain in this
'  directory - no package.json, no bundler, no wwwroot - and that is the claim
'  the sample exists to demonstrate.
'
'  WHAT THE INTERACTION IS, AND WHAT IT IS NOT.
'
'  The disclosure below is a STATE-BOUND control: `open="$state.detailsOpen"`
'  binds it to a key in the reactive store, and because no handler is authored,
'  toggling it writes the new value straight to that key and every reader of the
'  key re-renders. No host code, no round trip - and it survives serialisation,
'  because a state binding is data where a handler would have been a closure.
'
'  The two `$` prefixes differ in DIRECTION, and that is the whole distinction:
'  `open="$panel"` is a QUERY binding, host-fed and read-only, so a toggle has
'  nowhere to write; `open="$state.detailsOpen"` is a writable slot. Both are
'  authorable from the dialect alone.
'
'  It is NOT `Action.Dispatch`. That case carries a host closure; the encoder
'  drops the payload and a decoding browser gets an affordance that fires and
'  does nothing. Full Fable is the one tier where it survives, because there the
'  tree is never serialised. See docs/EMBEDDED-RENDERER.md section 2.
' ============================================================================

Module Program

    ' The tree, as XML. This is the notation the VB tier exists for: an
    ' attribute is a literal, nested elements are the parent's children, and
    ' `FuaranXml.Translate` drives the same shared factory surface every other
    ' authoring tier uses - so the bytes are identical to the C# and F#
    ' equivalents. There is no VB codec.
    Private Function BuildTree() As XElement
        Return <Dashboard id="root">
                   <Heading id="title" text="Embedded renderer - VB"/>
                   <Markdown id="note"
                       text="This tree was authored in **VB XML literals**, encoded as canonical wire JSON, and rendered in your browser by the renderer embedded in `Fuaran.UI.Renderer.Web`. No Node toolchain was involved on this side."/>
                   <Disclosure id="details" heading="How this page was built" open="$state.detailsOpen" default-open="false">
                       <Markdown id="details-body"
                           text="The server translated an `XElement` into a `Node` tree, encoded it with `encodeNodeForTransport`, and inlined the JSON into this page. The browser decoded it and rendered it. This panel's open state lives in the reactive store under `detailsOpen` - toggling it writes that key and made no request."/>
                   </Disclosure>
               </Dashboard>
    End Function

    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)
        Dim app = builder.Build()

        ' Serve the embedded renderer bundle, the reference stylesheet and the
        ' fingerprint under /_fuaran. One call; nothing to install.
        '
        ' Called as a STATIC method rather than as an extension. The extension
        ' form (`app.MapFuaranRenderer()`) is what a C# host writes; VB does not
        ' bind it here, because `Fuaran` is a NAMESPACE as well as a type name in
        ' this runtime and the import resolution goes the other way. Calling the
        ' static form is exact, needs no import at all, and is the same method.
        Global.Fuaran.UI.Renderer.Web.FuaranRendererEndpointExtensions.MapFuaranRenderer(app)

        app.MapGet("/", Function(ctx As HttpContext)
                            ' ENCODE FOR TRANSPORT, not Encode(). The transport
                            ' encoder returns the same canonical bytes and REFUSES
                            ' a tree carrying a closure the wire would drop.
                            ' Failing here is the whole value: the alternative is
                            ' a control that works in every test and does nothing
                            ' in the browser.
                            Dim node As FuaranNode = FuaranXml.Translate(BuildTree())

                            Dim wireJson As String = Nothing
                            Dim lossy As IReadOnlyList(Of LossySlot) = Nothing

                            If Not node.TryEncodeForTransport(wireJson, lossy) Then
                                Dim slots = String.Join(", ", lossy.Select(Function(p) $"{p.NodeId} ({p.Slot})"))
                                Return Results.Problem(
                                    $"This tree carries interaction that would not survive serialisation: {slots}. " &
                                    "Replace it with a wire-representable action, or render it in process with full Fable.")
                            End If

                            Dim env = ctx.RequestServices.GetService(GetType(IWebHostEnvironment))

                            Dim options As New Global.Fuaran.UI.Renderer.Web.Snippet.MountOptions(
                                "fuaran-root",
                                "/_fuaran",
                                FSharpOption(Of String).None,
                                FSharpOption(Of String).None,
                                CType(env, IWebHostEnvironment).IsDevelopment())

                            Dim html =
                                "<!doctype html>" & vbLf &
                                "<html lang=""en"">" & vbLf &
                                "  <head>" & vbLf &
                                "    <meta charset=""utf-8"">" & vbLf &
                                "    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">" & vbLf &
                                "    <title>Fuaran - embedded renderer (VB)</title>" & vbLf &
                                "    " & Global.Fuaran.UI.Renderer.Web.Snippet.styleLink("/_fuaran") & vbLf &
                                "  </head>" & vbLf &
                                "  <body>" & vbLf &
                                "    " & Global.Fuaran.UI.Renderer.Web.Snippet.scriptTag("/_fuaran") & vbLf &
                                Global.Fuaran.UI.Renderer.Web.Snippet.mount(options, Global.Fuaran.UI.Renderer.Theme.vocabularyFingerprint, wireJson) & vbLf &
                                "  </body>" & vbLf &
                                "</html>"

                            Return Results.Content(html, "text/html; charset=utf-8")
                        End Function)

        app.Run()
    End Sub

End Module
