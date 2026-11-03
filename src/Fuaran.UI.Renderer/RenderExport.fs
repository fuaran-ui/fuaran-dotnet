module Fuaran.UI.Renderer.RenderExport

// ============================================================================
//  Phase 2577 — the browser rasterise performer for the two conventional
//  rendered-artefact export ids:
//
//      render.copy-image   the picture of a node, as a PNG, to the clipboard
//      render.save-image   the same picture, to a file
//
//  Phase 2576 made the EFFECT route real: a tree carrying
//  `Button(onClick = Action.Invoke("render.copy-image", …))` reaches whatever a
//  host registers under that id, gated per name. Until this module nothing was
//  registered, so the vocabulary promised a route the estate shipped nothing on.
//
//  THREE HALVES, and the split is the testability.
//
//   1. The DECLARATIONS — two `Fuaran.Core.Capability` values with bounded
//      signatures (`node` required; `format` one of `png`; `scale` in
//      [MinScale, MaxScale]) — so the registry refuses an out-of-space argument
//      BEFORE any body runs. A rasterise at an unbounded scale is a memory DoS
//      from a decoded tree, which is what the bound is for.
//   2. The PURE half — `parseArgs`, `plan` and `planCanvas` — which turns a
//      picture's on-screen size and a scale into the canvas dimensions, the
//      `data:` source (for an SVG) and the suggested file name. Testable on
//      .NET; nothing here touches the DOM.
//   3. The BROWSER half, behind `FABLE_COMPILER` exactly as the grid's CSV
//      delivery is (Phase 1125): find the node's container by
//      `data-fuaran-node-id` and take its LARGEST drawable — an inline `<svg>`
//      (the first-party chart, Phase 525) or a `<canvas>` (an adapter-drawn
//      chart) — never the first, because an adapter chart's legend glyph and
//      empty-state overlay are small SVGs beside the canvas that IS the
//      picture. An SVG is serialised and loaded as an `<img>`; either is drawn
//      to an offscreen `<canvas>` at the scale, then `toBlob("image/png")` and
//      either `navigator.clipboard.write([ClipboardItem])` — a secure context
//      and a user gesture are the browser's conditions, and a REFUSED write
//      falls back to the download rather than failing silently — or the
//      anchor-with-`download` route the CSV export already takes.
//
//  `invoker` composes the three into a `BindingSources.CapabilityInvoker` over
//  this module's own registry, so a host registers both ids by one assignment:
//  `{ sources with CapabilityInvoker = RenderExport.invoker }`. The dispatch runs
//  through `CapabilityRegistry.dispatch`, so the id must resolve (default-deny)
//  and the arguments must validate before the body is reached — the same path
//  `Fuaran.UI.AiTools.Capabilities.makeInvoker` takes (Phase 2311); its eight-line
//  `Deferred` mapping is restated here so a browser host needs nothing beyond
//  this package.
//
//  WHAT COMES BACK is the string "started": the effect route discards it
//  (Phase 2576), and nothing in the vocabulary can ask a host to hand rendered
//  bytes back into tree-readable state — `docs/security/ESCAPE-HATCHES.md`'s
//  standing limit. A node that addresses nothing, one with nothing drawable, a
//  host with no canvas: each is a typed refusal (`Deferred.Error`) the effect
//  arm surfaces as a refusal.
//
//  THE EGRESS DECISION (the phase's design point 1). The vocabulary row said a
//  rendered-artefact export honours the node's egress policy. Egress has nothing
//  to say here: a `data:` or `blob:` artefact is not a destination the
//  sanitiser classifies (`Sanitize.sanitizeUrl` refuses `data:` outright), which
//  is why the CSV export consults the `Export` descriptor and not egress. The
//  obligation is therefore the per-name DESCRIPTOR gate — `CanDispatch
//  (Invoke "render.copy-image")` — which the effect route asks on the tree's
//  behalf and a host-chrome affordance must ask for itself. No egress class is
//  added.
//
//  FONTS, recorded rather than solved: a system font stack survives
//  serialisation; a web font does not unless it is inlined into the SVG, which
//  this module does not do. A chart rendered in a web font rasterises in the
//  fallback face. A canvas carries its own pixels and has no such limit.
// ============================================================================

open Fable.Core
open Fuaran.UI.Types

/// The clipboard half's conventional id (`VOCABULARY.md`, the `ExportRendered` row).
[<Literal>]
let CopyImageId = "render.copy-image"

/// The file half's conventional id.
[<Literal>]
let SaveImageId = "render.save-image"

/// The smallest scale a reader may ask for.
let MinScale = 0.5

/// The largest — the bound that keeps a decoded tree from asking for a canvas the
/// host cannot allocate.
let MaxScale = 4.0

/// The scale when the tree names none: a slide-worthy picture, twice the on-screen size.
let DefaultScale = 2.0

/// The formats this performer produces. One, deliberately: the `format` argument
/// exists so the convention is stable, not because a second format ships here.
let Formats = [ "png" ]

/// The format when the tree names none.
let DefaultFormat = "png"

/// The longest node id the signature admits.
let MaxNodeIdLength = 256

/// The longest side a canvas may have; the browsers' own ceiling is near this, and
/// a plan past it would draw nothing and say nothing.
let MaxCanvasSide = 16384

// ─── 1. The declarations ─────────────────────────────────────────────────────

let private signature (id: string) : Fuaran.Core.Signature =
    let hole (addr: string) (space: Fuaran.Core.ValueSpace) (required: bool) : Fuaran.Core.SigEntry =
        { Addr = addr
          Name = addr
          Kind = Fuaran.Core.ValueHole space
          Required = required }

    { Name = id
      Holes =
        [ hole "node" (Fuaran.Core.StringLen(1, MaxNodeIdLength)) true
          hole "format" (Fuaran.Core.Enum Formats) false
          hole "scale" (Fuaran.Core.FloatRange(MinScale, MaxScale)) false ]
      // Writes the host (the clipboard, a download); the realised VALUE is the
      // constant "started", so the invocation is deterministic for replay — the
      // picture never enters the journal, which is the escape-hatch limit above.
      Effect =
        { Host = Fuaran.Core.WritesHost
          Determinism = Fuaran.Core.Effect.deterministic } }

/// `render.copy-image`, declared.
let copyImage: Fuaran.Core.Capability =
    Fuaran.Core.Capability.create
        CopyImageId
        (signature CopyImageId)
        (Fuaran.Core.ClientIsland Fuaran.Core.IslandKind.Js)

/// `render.save-image`, declared.
let saveImage: Fuaran.Core.Capability =
    Fuaran.Core.Capability.create
        SaveImageId
        (signature SaveImageId)
        (Fuaran.Core.ClientIsland Fuaran.Core.IslandKind.Js)

/// The registry holding exactly the two. A host composing a wider registry
/// registers `copyImage` and `saveImage` into its own; this one backs `invoker`.
let registry: Fuaran.Core.CapabilityRegistry =
    Fuaran.Core.CapabilityRegistry.empty
    |> Fuaran.Core.CapabilityRegistry.register copyImage
    |> Result.bind (Fuaran.Core.CapabilityRegistry.register saveImage)
    |> function
        | Ok r -> r
        | Error e -> failwithf "RenderExport: the fixed registry did not build: %s" (Fuaran.Core.InvokeError.describe e)

// ─── 2. The pure half ────────────────────────────────────────────────────────

/// What the browser half is asked to draw and deliver.
type Plan =
    {
        /// Canvas width in device pixels: the on-screen width at the scale, rounded.
        Width: int
        /// Canvas height, likewise.
        Height: int
        /// For an SVG picture, the `data:image/svg+xml` source the `<img>` loads;
        /// `None` for a canvas picture, which is drawn from its own pixels.
        Source: string option
        /// The file name the download half suggests.
        SuggestedName: string
    }

/// The file name the download half suggests: the node id, trimmed and bounded,
/// with the `.png` extension — the CSV export's rule with the other extension.
let filename (nodeId: string) : string =
    let trimmed = nodeId.Trim()

    let stem =
        if trimmed = "" then "export"
        elif trimmed.Length > 80 then trimmed.Substring(0, 80)
        else trimmed

    stem + ".png"

/// Read the registry-validated arguments into the body's inputs. `node` is
/// required (the registry refuses its absence first; this is the second line);
/// `format` and `scale` default. The bounds are re-checked here so the pure half
/// is safe to call without the registry in front of it.
let parseArgs (args: (string * string) list) : Result<string * string * float, string> =
    let find (addr: string) =
        args |> List.tryFind (fun (a, _) -> a = addr) |> Option.map snd

    match find "node" with
    | None
    | Some "" -> Error "render export: the `node` argument names no node"
    | Some node ->
        let format = find "format" |> Option.defaultValue DefaultFormat

        if not (List.contains format Formats) then
            Error(sprintf "render export: format '%s' is not one of %s" format (String.concat ", " Formats))
        else
            match find "scale" with
            | None -> Ok(node, format, DefaultScale)
            | Some s ->
                match System.Double.TryParse s with
                | true, v when v >= MinScale && v <= MaxScale -> Ok(node, format, v)
                | _ -> Error(sprintf "render export: scale '%s' is not a number between %g and %g" s MinScale MaxScale)

/// The canvas dimensions for an on-screen size at a scale. Refuses a degenerate
/// size, a scale outside the bound and a canvas past the browsers' ceiling.
let private dimensions (nodeId: string) (width: float) (height: float) (scale: float) : Result<int * int, string> =
    if not (width > 0.0 && height > 0.0) then
        Error(sprintf "render export: node '%s' has no on-screen size to rasterise (%gx%g)" nodeId width height)
    elif not (scale >= MinScale && scale <= MaxScale) then
        Error(sprintf "render export: scale %g is outside [%g, %g]" scale MinScale MaxScale)
    else
        let w = int (System.Math.Round(width * scale))
        let h = int (System.Math.Round(height * scale))

        if w > MaxCanvasSide || h > MaxCanvasSide then
            Error(
                sprintf "render export: a %dx%d canvas is past the %d-pixel side a browser will draw" w h MaxCanvasSide
            )
        else
            Ok(w, h)

/// The plan for an SVG picture: the serialised markup becomes the `<img>` source.
/// Refuses an empty document on top of `dimensions`' refusals.
let plan (nodeId: string) (svg: string) (width: float) (height: float) (scale: float) : Result<Plan, string> =
    if System.String.IsNullOrWhiteSpace svg then
        Error(sprintf "render export: node '%s' serialised to an empty document" nodeId)
    else
        dimensions nodeId width height scale
        |> Result.map (fun (w, h) ->
            { Width = w
              Height = h
              Source = Some("data:image/svg+xml;charset=utf-8," + System.Uri.EscapeDataString svg)
              SuggestedName = filename nodeId })

/// The plan for a canvas picture: no source, the canvas is drawn from its own pixels.
let planCanvas (nodeId: string) (width: float) (height: float) (scale: float) : Result<Plan, string> =
    dimensions nodeId width height scale
    |> Result.map (fun (w, h) ->
        { Width = w
          Height = h
          Source = None
          SuggestedName = filename nodeId })

// ─── 3. The browser half ─────────────────────────────────────────────────────

/// Which delivery the body performs.
[<RequireQualifiedAccess>]
type Mode =
    | Copy
    | Save

let private modeName (mode: Mode) : string =
    match mode with
    | Mode.Copy -> "copy"
    | Mode.Save -> "save"

#if FABLE_COMPILER
[<Emit("new XMLSerializer().serializeToString($0)")>]
let private serialise (element: Browser.Types.Element) : string = jsNative

// The whole asynchronous pipeline in one place, fire-and-forget: the effect
// route discards the result, and every failure past this point reports through
// console.warn (the renderer's diagnostic channel of last resort — the
// `WriteToClipboard` precedent in BrowserRuntime). `source` is either a string
// (an SVG picture's data URL, loaded as an image first) or a canvas element
// (drawn directly). The clipboard write FALLS BACK to the download on refusal
// or absence of an image clipboard, so a reader who asked for a picture gets
// one either way.
[<Emit("""(function(source, w, h, mode, name){
  var deliver = function(drawable){
    try {
      var canvas = document.createElement('canvas');
      canvas.width = w; canvas.height = h;
      var context = canvas.getContext('2d');
      context.drawImage(drawable, 0, 0, w, h);
      canvas.toBlob(function(blob){
        if (!blob) { console.warn('[Fuaran] render export: the canvas produced no PNG'); return; }
        var download = function(){
          var url = URL.createObjectURL(blob);
          var a = document.createElement('a');
          a.setAttribute('href', url);
          a.setAttribute('download', name);
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          setTimeout(function(){ URL.revokeObjectURL(url); }, 1000);
        };
        if (mode === 'copy') {
          try {
            if (navigator.clipboard && navigator.clipboard.write && typeof ClipboardItem !== 'undefined') {
              navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]).catch(function(e){
                console.warn('[Fuaran] render.copy-image: the clipboard write was refused (' + e + '); falling back to a download');
                download();
              });
            } else {
              console.warn('[Fuaran] render.copy-image: this host has no image clipboard; falling back to a download');
              download();
            }
          } catch (e) {
            console.warn('[Fuaran] render.copy-image threw (' + e + '); falling back to a download');
            download();
          }
        } else {
          download();
        }
      }, 'image/png');
    } catch (e) {
      console.warn('[Fuaran] render export: rasterise threw: ' + e);
    }
  };
  if (typeof source === 'string') {
    var img = new Image();
    img.onload = function(){ deliver(img); };
    img.onerror = function(){ console.warn('[Fuaran] render export: the serialised SVG did not load as an image'); };
    img.src = source;
  } else {
    deliver(source);
  }
})($0, $1, $2, $3, $4)""")>]
let private rasterise (source: obj) (width: int) (height: int) (mode: string) (suggestedName: string) : unit = jsNative

/// The node's largest drawable — an inline `<svg>` or a `<canvas>` — with its
/// on-screen size. Largest, not first: an adapter-drawn chart keeps small SVGs
/// (a legend glyph, an empty-state overlay) beside the canvas that is the picture.
let private largestDrawable (container: Browser.Types.Element) : (Browser.Types.Element * float * float) option =
    let candidates = container.querySelectorAll "svg, canvas"
    let mutable best: (Browser.Types.Element * float * float) option = None

    for i in 0 .. candidates.length - 1 do
        let element: Browser.Types.Element = candidates.[i]
        let rect = element.getBoundingClientRect ()

        match best with
        | Some(_, w, h) when w * h >= rect.width * rect.height -> ()
        | _ -> best <- Some(element, rect.width, rect.height)

    best

/// The plan for the node's picture, from the live DOM.
let private planFor (nodeId: string) (scale: float) : Result<Plan * obj, string> =
    let selector =
        "[data-fuaran-node-id=\""
        + nodeId.Replace("\\", "\\\\").Replace("\"", "\\\"")
        + "\"]"

    let container = Browser.Dom.document.querySelector selector

    if isNull container then
        Error(sprintf "render export: node '%s' addresses nothing rendered" nodeId)
    else
        match largestDrawable container with
        | None -> Error(sprintf "render export: node '%s' holds no inline SVG or canvas to rasterise" nodeId)
        | Some(element, width, height) ->
            if element.tagName.ToLowerInvariant() = "canvas" then
                planCanvas nodeId width height scale |> Result.map (fun p -> p, box element)
            else
                plan nodeId (serialise element) width height scale
                |> Result.map (fun p -> p, box (Option.defaultValue "" p.Source))
#endif

/// Perform one delivery for registry-validated arguments. In a browser the
/// pipeline is started and `"started"` is the settled value; on a host with no
/// canvas the answer is the typed refusal, which is the ordinary case for a
/// server-driven or headless renderer.
let perform (mode: Mode) (args: (string * string) list) : Deferred<obj> =
    match parseArgs args with
    | Error message -> Deferred.Error message
    | Ok(node, _format, scale) ->
#if FABLE_COMPILER
        match planFor node scale with
        | Error message -> Deferred.Error message
        | Ok(p, source) ->
            rasterise source p.Width p.Height (modeName mode) p.SuggestedName
            Deferred.Ready("started" :> obj)
#else
        ignore (node, scale, modeName mode)
        Deferred.Error "render export needs a browser host: this host has no canvas"
#endif

// ─── The invoker ─────────────────────────────────────────────────────────────

/// Core's settled/pending/failed envelope, qualified: `Failed` alone also names a resolver fault.
type private CoreDeferred = Fuaran.Core.Deferred<obj>

let private toCore (d: Deferred<obj>) : CoreDeferred =
    match d with
    | Deferred.Pending -> CoreDeferred.Pending
    | Deferred.Ready v -> CoreDeferred.Ready v
    | Deferred.Error m -> CoreDeferred.Failed m

/// A `BindingSources.CapabilityInvoker` over this module's registry: the id must
/// resolve and the arguments must validate before either body runs, and every
/// refusal arrives as `Deferred.Error` carrying the registry's sentence — the
/// mapping `Fuaran.UI.AiTools.Capabilities.makeInvoker` performs, restated here
/// so a browser host needs nothing beyond this package to register both ids.
let invoker: string -> (string * string) list -> Deferred<obj> =
    fun capabilityId args ->
        let body (capability: Fuaran.Core.Capability) () : CoreDeferred =
            if capability.Id = CopyImageId then
                toCore (perform Mode.Copy args)
            else
                toCore (perform Mode.Save args)

        match Fuaran.Core.CapabilityRegistry.dispatch registry capabilityId args body with
        | Ok(CoreDeferred.Ready v) -> Deferred.Ready v
        | Ok CoreDeferred.Pending -> Deferred.Pending
        | Ok(CoreDeferred.Failed m)
        | Error(Fuaran.Core.BodyFailed m) -> Deferred.Error m
        | Error e -> Deferred.Error(Fuaran.Core.InvokeError.describe e)
