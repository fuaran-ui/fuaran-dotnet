module Fuaran.UI.Renderer.FileUploadDrop

// ============================================================================
//  Fuaran — the file-upload drop zone and paste target (Phase 1115)
//
//  `FileUploadSpec.DropTarget` and `.AcceptPaste` name two INGRESS ROUTES, not
//  gestures. Under the affordance→op charter the wire names a capability on the
//  node that hosts the gesture and consumes its effect; the drag-over, the drop,
//  the paste, the visible drop state and the refusal announcement are all this
//  file's, and nothing on the wire names an event, a MIME negotiation or a
//  drag image.
//
//  ── The one design decision everything else follows from ──────────────────
//  A dropped or pasted file is written into the control's OWN
//  `<input type="file">` (via a `DataTransfer`), and a bubbling `change` is
//  dispatched from it. It is NOT handed to `OnSelect` directly. Three things
//  fall out of that, and the third is the one that made it non-negotiable:
//
//    * ONE selection path. `OnSelect` fires from the input's existing change
//      handler whatever route the file arrived by, so there is no second
//      code path to drift from the first.
//    * The control's OWN feedback, for free. The user agent renders the chosen
//      filenames in its native file-input chrome, so a reader sees a dropped
//      file exactly as they see a picked one — and `Accept` filtering is
//      visible in the same place, because a refused file is simply not among
//      the names shown.
//    * The server-driven tier reads `input.files`. `Action.ReadFileBody` is
//      performed client-side by the live shim, which looks the element up and
//      reads `input.files[0]`. A renderer that dispatched `OnSelect` without
//      populating the input would leave the file reachable on the client tier
//      and invisible to that one — the divergence would be silent, and it is
//      why the existing `change` / `file-read` allow-list rows cover these
//      routes BY CONSTRUCTION rather than by a new event name.
//
//  ── Why a function component ──────────────────────────────────────────────
//  Whether a drag is currently over the zone, and how many files the last
//  gesture refused, live for the duration of one interaction and are never part
//  of the document. The composition-based renderer holds no hooks, so — exactly
//  as `LocalBindings` and `ComboboxControl` do — the affordance is wrapped as a
//  React function component and invoked through `React.createElement`. The
//  children (the label span and the `<input>`) are built by `Render.fs` and
//  passed in, so there is exactly one definition of the control's markup and
//  this file adds only the affordance around it.
//
//  ── Accessibility ─────────────────────────────────────────────────────────
//  The drop zone is an ADDITIONAL route, never a replacement: the `<input>` and
//  its label are untouched, so click-to-pick and its keyboard equivalent work
//  exactly as before. There is no keyboard equivalent OF A DROP, and none is
//  invented — the picker already is one. The refusal line is `role="status"`,
//  so a reader who cannot see the filename list still hears that files were
//  turned away.
//
//  ── Cross-pipeline ────────────────────────────────────────────────────────
//  `React.useState` is a Feliz `jsNative` declaration that compiles on both
//  .NET and Fable and throws on .NET if invoked; the renderer's .NET tests
//  never mount React. The SSR floor for these flags is the plain picker — a
//  drop needs script, so a no-script host renders exactly what it rendered
//  before this phase and says so in `WIRE_FORMAT.md` §3.6.10.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

/// Write `files` into the `<input type="file">` inside `container`, then fire a
/// bubbling `change` from it. Returns `false` when the browser refuses the
/// assignment (no `DataTransfer` constructor, or a host that rejects a
/// programmatic `files` write) so the caller can decline to claim an ingest it
/// did not perform.
///
/// The whole body is one emit because it is browser plumbing with no F# shape
/// worth modelling: a `DataTransfer` is constructed, the accepted `File`
/// objects are added to it, and its `files` list is assigned. `bubbles: true`
/// is load-bearing — React attaches its own listener at the root container, so
/// a non-bubbling event reaches no `onChange`.
[<Emit("""(function(container, files){
  try {
    var input = container && container.querySelector && container.querySelector('input[type=file]');
    if (!input || typeof DataTransfer === 'undefined') return false;
    var dt = new DataTransfer();
    for (var i = 0; i < files.length; i++) dt.items.add(files[i]);
    input.files = dt.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
  } catch (e) {
    console.warn('[Fuaran] FileUpload drop/paste ingest failed: ' + e);
    return false;
  }
})($0, $1)""")>]
let private assignFiles (container: obj) (files: obj array) : bool = jsNative

/// The `File` objects a `DataTransfer` / `ClipboardData` carries, as a plain
/// array. `null` and an absent `files` list both come back empty rather than
/// throwing — a paste of plain text is the ordinary case, not an error.
[<Emit("($0 && $0.files) ? Array.prototype.slice.call($0.files) : []")>]
let private filesOf (transfer: obj) : obj array = jsNative

[<Emit("String($0 && $0.name ? $0.name : '')")>]
let private fileName (file: obj) : string = jsNative

[<Emit("String($0 && $0.type ? $0.type : '')")>]
let private fileMime (file: obj) : string = jsNative

/// Does `accept` admit this file? The list is the wire's `accept` — the same
/// value the `<input accept>` attribute carries — so this reproduces the user
/// agent's own picker filter for the routes the picker is not on.
///
/// An EMPTY list admits everything, exactly as an absent `accept` attribute
/// does. Three entry shapes are recognised, and they are the three HTML
/// defines: an extension (`.csv`), a wildcard MIME (`image/*`), and an exact
/// MIME (`text/csv`). Anything else matches nothing rather than matching
/// everything — a spelling the picker would not honour must not open a route
/// the picker does not.
let admits (accept: string list) (name: string) (mime: string) : bool =
    if List.isEmpty accept then
        true
    else
        let name = name.ToLowerInvariant()
        let mime = mime.ToLowerInvariant()

        accept
        |> List.exists (fun entry ->
            let entry = entry.Trim().ToLowerInvariant()

            if entry = "" then
                false
            elif entry.StartsWith "." then
                name.EndsWith entry
            elif entry.EndsWith "/*" then
                mime <> "" && mime.StartsWith(entry.Substring(0, entry.Length - 1))
            else
                mime <> "" && mime = entry)

type UploadDropProps =
    {| className: string
       dropTarget: bool
       acceptPaste: bool
       accept: string list
       multiple: bool
       children: ReactElement list |}

let private renderDropZone (props: UploadDropProps) : ReactElement =
    // `over` is a COUNTER, not a flag. `dragenter` / `dragleave` fire for every
    // descendant the pointer crosses, so a boolean flickers off the moment the
    // drag passes over the label's own `<span>`; counting enters against leaves
    // is the standard fix and the reason this is not two `setState true/false`
    // calls.
    let depth, setDepth = React.useState 0
    let refused, setRefused = React.useState 0

    /// The one ingest path. Filters by `Accept`, writes what survives into the
    /// control's own input, and records what did not so the reader is told.
    let ingest (container: obj) (all: obj array) =
        if all.Length = 0 then
            false
        else
            let accepted =
                all |> Array.filter (fun f -> admits props.accept (fileName f) (fileMime f))

            // A single-file control takes the FIRST accepted file, matching the
            // picker: a `<input type=file>` without `multiple` never holds more
            // than one, and silently keeping a later one instead of the first
            // would make the drop order-sensitive in a way the picker is not.
            let kept =
                if props.multiple || accepted.Length <= 1 then
                    accepted
                else
                    Array.sub accepted 0 1

            setRefused (all.Length - kept.Length)

            if kept.Length = 0 then
                // Nothing was ingested — but the gesture is still CONSUMED, so
                // the browser does not navigate to the dropped file, which is
                // what a default-action drop does and is far worse than a
                // refusal the reader can read.
                true
            else
                assignFiles container kept

    let handlers: IReactProperty list =
        [ if props.dropTarget then
              // `preventDefault` on BOTH enter and over is what makes the
              // element a drop target at all; omitting it on `dragover` is the
              // classic silent failure — the zone highlights and then refuses
              // the drop.
              prop.onDragEnter (fun e ->
                  e.preventDefault ()
                  setDepth (depth + 1))

              prop.onDragOver (fun e -> e.preventDefault ())

              prop.onDragLeave (fun e ->
                  e.preventDefault ()
                  setDepth (max 0 (depth - 1)))

              prop.onDrop (fun e ->
                  e.preventDefault ()
                  setDepth 0
                  ingest (box e.currentTarget) (filesOf (box e.dataTransfer)) |> ignore)
          if props.acceptPaste then
              // Only a paste CARRYING FILES is consumed. A text paste keeps its
              // default action untouched: this element may contain nothing
              // editable today, but swallowing every paste that reaches it
              // would be a route to a bug nobody could see the cause of.
              prop.onPaste (fun e ->
                  let files = filesOf (box e.clipboardData)

                  if files.Length > 0 then
                      e.preventDefault ()
                      ingest (box e.currentTarget) files |> ignore) ]

    let stateClasses =
        [ Some props.className
          Some "fuaran-upload-drop"
          (if depth > 0 then Some "fuaran-upload-drop-active" else None)
          (if refused > 0 then
               Some "fuaran-upload-drop-refused"
           else
               None) ]
        |> List.choose id
        |> String.concat " "

    // The refusal line, and the reason it is not silent. `Accept` filtering on
    // the picker is done by the user agent BEFORE the reader chooses, so a
    // refused file never appears; on these routes the reader has already
    // committed the gesture, so the only honest answer is to say what happened.
    let refusalLine =
        if refused = 0 then
            []
        else
            [ Html.span
                  [ prop.className "fuaran-upload-drop-hint"
                    prop.role "status"
                    prop.text (
                        if refused = 1 then
                            "1 file was not accepted by this upload."
                        else
                            string refused + " files were not accepted by this upload."
                    ) ] ]

    Html.label (
        [ prop.className stateClasses ]
        @ handlers
        @ [ prop.children (props.children @ refusalLine) ]
    )

/// The public surface — `Render.fs` invokes this when either gesture flag is
/// set, and renders the plain label itself when neither is.
let dropZone (props: UploadDropProps) : ReactElement =
    reactCreateElement (box renderDropZone) (box props)
