module Fuaran.UI.Renderer.UploadStream

// ============================================================================
//  Fuaran — the large-binary upload streaming leg (Phase 1117)
//
//  `FileUploadSpec.Destination` names a host-registered destination. When one
//  is declared, the selected files are streamed to the host's own upload sink
//  and the REFERENCE the sink returns is written to the control's
//  host-reserved state slot. The bytes never enter the message loop, never
//  reach a dispatched `Action`, and never reach the op stream — which is the
//  whole reason the member exists (see `Fuaran.UI.Ops.UploadSink`).
//
//  ── This does not replace the selection path, it runs beside it ───────────
//  `OnSelect` still fires from the input's own change handler in `Render.fs`,
//  with exactly the `FileSelection` list it always got. Streaming is a SECOND
//  FACT about the same gesture, not a second spelling of the first: "the reader
//  chose these files" and "those files now exist at a destination under these
//  references" are different statements, arrive at different times, and are
//  consumed by different code. Trying to fold the reference into `OnSelect`
//  would have meant either firing that handler twice for one gesture or
//  delaying it until the transfer finished — the second of which turns every
//  existing upload handler asynchronous the day a destination is declared.
//
//  ── Why a function component ──────────────────────────────────────────────
//  How many bytes have gone and what refused live for the duration of one
//  transfer and are never part of the document — the `FileUploadDrop`
//  reasoning exactly, and this file follows its shape: the children (the label
//  span and the `<input>`) are built by `Render.fs` and passed in, so there is
//  one definition of the control's markup and this file adds only the
//  behaviour around it.
//
//  It wraps in a `<div>`, deliberately, where the drop zone wraps in a
//  `<label>`: both can be declared on one upload, and a `<label>` inside a
//  `<label>` is invalid markup. The wrapper's `onChange` sees the input's
//  bubbled change exactly as `Render.fs`'s own handler does.
//
//  ── The refusal is never silent, and the reader and the operator are told
//     different things ─────────────────────────────────────────────────────
//  Every failure state is typed (`UploadRefusal`) and rendered on a
//  `role="status"` line, so a reader who cannot see the control still learns
//  that nothing was saved — the Phase 1115 refusal-line precedent. The reader
//  gets `UploadSink.announce` (short, and not a description of the host's
//  configuration); the host's `Warn` channel gets `UploadSink.describe`, which
//  names the class and the destination. Neither carries a byte or a file name.
//
//  ── Cross-pipeline ────────────────────────────────────────────────────────
//  `React.useState` is a Feliz `jsNative` declaration that compiles on both
//  .NET and Fable and throws on .NET if invoked; the renderer's .NET tests
//  never mount React. The SSR floor is the plain control with no status line
//  and no transfer — a stream needs script — and `docs/SSR.md` says so.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz
open Fuaran.Core
open Fuaran.UI.HostPrelude
open Fuaran.UI.Ops.UploadSink

/// The `File` objects an input's change event carries, as a plain array.
/// A change with no files (a cleared picker) comes back empty rather than
/// throwing.
[<Emit("($0 && $0.target && $0.target.files) ? Array.prototype.slice.call($0.target.files) : []")>]
let private changedFiles (ev: obj) : obj array = jsNative

[<Emit("String($0 && $0.name ? $0.name : '')")>]
let private fileName (file: obj) : string = jsNative

[<Emit("String($0 && $0.type ? $0.type : '')")>]
let private fileMime (file: obj) : string = jsNative

[<Emit("Number($0 && $0.size ? $0.size : 0)")>]
let private fileSize (file: obj) : float = jsNative

/// The `FileSelection` a browser `File` projects to. Byte-identical in shape to
/// the one `Render.fs`'s own change handler builds — `Ref.Id` is the
/// index-qualified stable token (the only part that ever serialises) and
/// `Ref.Handle` boxes the blob.
let selectionOf (index: int) (file: obj) : FileSelection =
    { Name = fileName file
      Size = int64 (fileSize file)
      MimeType = fileMime file
      Ref =
        { Id = sprintf "%d:%s" index (fileName file)
          Handle = Some file } }

/// The host-reserved state slot an upload's references are written to. One
/// definition, here, because three readers need to agree on it: this component
/// writes it, an author reads it through `Binding.State`, and the op-stream
/// discipline test asserts what it contains.
///
/// Under the Phase 782 host-reserved prefix, so a tree-originated
/// `Action.SetState` can never forge an upload result — a write naming a key
/// under that prefix is refused on every path. The reference is a fact the HOST
/// observed, and only the host may state it.
let stateKeyFor (nodeId: string) : string =
    Fuaran.UI.StateKeyPolicy.HostReservedPrefix + "upload." + nodeId

/// One completed reference, as the JSON an author reads out of the state slot.
/// Four members, the `UploadedRef` record exactly, and deliberately nothing
/// else: no URL, no file name, no local path. A consumer that needs the reader's
/// filename already has it from `OnSelect`.
let private refJson (r: UploadedRef) : JVal =
    JObj
        [ "fileId", JStr r.FileId
          "hash", JStr r.Hash
          "size", JStr(string r.Size)
          "contentType", JStr r.ContentType ]

/// What the control is doing right now. Never part of the document.
[<RequireQualifiedAccess>]
type private Status =
    | Idle
    /// `total` is `0L` where the sink could not know the size in advance — the
    /// line then says "uploading" without a proportion rather than dividing by
    /// zero.
    | Sending of sent: int64 * total: int64
    | Done of count: int
    | Refused of announcement: string

type UploadStreamProps =
    {| destination: string
       nodeId: string
       sink: IFuaranUploadSink option
       gate: unit -> bool
       warn: string -> unit
       setState: string -> JVal -> unit
       children: ReactElement list |}

let private renderStreamShell (props: UploadStreamProps) : ReactElement =
    let status, setStatus = React.useState Status.Idle

    /// Refuse, once, in both directions: the operator's channel gets the class
    /// and the destination, the reader gets a sentence.
    let refuse (r: UploadRefusal) =
        props.warn ("[Fuaran] " + describe r)
        setStatus (Status.Refused(announce r))

    let beginUpload (ev: obj) =
        let files = changedFiles ev

        if files.Length > 0 then
            // 1. THE GATE — may this tree cause an upload to this destination
            //    at all? Asked before the sink is consulted and before a byte
            //    moves, because a denied upload must not reach a transport even
            //    to be refused by it.
            if not (props.gate ()) then
                refuse (UploadRefusal.DispatchDenied props.destination)
            else
                match props.sink with
                // 2a. No sink wired. A different sentence from an unregistered
                //     destination, and kept distinct because it sends whoever
                //     reads the log somewhere else entirely.
                | None -> refuse (UploadRefusal.NoSink props.destination)
                | Some sink when not (sink.Destinations.Contains props.destination) ->
                    // 2b. THE RESOLUTION — the sink does not name this
                    //     destination. There is no fallback: the id is not
                    //     tried as a path, as a URL, or against a default
                    //     destination. A fallback would make registration
                    //     advisory, which is indistinguishable from not having
                    //     it.
                    refuse (UploadRefusal.UnregisteredDestination props.destination)
                | Some sink ->
                    let selections = files |> Array.mapi selectionOf
                    let total = selections |> Array.sumBy (fun s -> s.Size)
                    let completed = ResizeArray<UploadedRef>()
                    let mutable priorBytes = 0L
                    let mutable stopped = false

                    setStatus (Status.Sending(0L, total))

                    // Sequential, not concurrent, and that is the honest
                    // default: a progress figure over concurrent transfers is
                    // either a lie or a sum that jumps backwards when one
                    // retries, and a sink that wants concurrency owns its own
                    // scheduling behind this seam. `stopped` short-circuits the
                    // remaining files after the first refusal — reporting one
                    // refusal and then three more for the same cause tells the
                    // reader nothing new.
                    for selection in selections do
                        if not stopped then
                            let carried = priorBytes

                            sink.Upload(
                                props.destination,
                                selection,
                                (fun p -> setStatus (Status.Sending(carried + p.BytesSent, total))),
                                (fun result ->
                                    match result with
                                    | Ok reference ->
                                        completed.Add reference
                                        priorBytes <- carried + reference.Size
                                    | Error r ->
                                        stopped <- true
                                        refuse r)
                            )

                    if not stopped then
                        // The references, and only the references, reach the
                        // tree — through the host's own write to its own
                        // reserved slot, which is the existing declarative
                        // write path and the one a tree cannot forge.
                        props.setState (stateKeyFor props.nodeId) (JArr(completed |> Seq.map refJson |> List.ofSeq))

                        setStatus (Status.Done completed.Count)

    let statusLine =
        match status with
        | Status.Idle -> []
        | Status.Sending(sent, total) ->
            [ Html.span
                  [ prop.className "fuaran-upload-stream-progress"
                    prop.role "status"
                    // A `<progress>` element would be the richer control and is
                    // deliberately not used: `total` is `0L` on a sink that
                    // cannot size the transfer, and an indeterminate
                    // `<progress>` renders as an animation with no accessible
                    // text at all. A sentence says the same thing in both
                    // cases and says it to a screen reader.
                    prop.text (
                        if total > 0L then
                            sprintf "Uploading… %d%%" (int (sent * 100L / total))
                        else
                            "Uploading…"
                    ) ] ]
        | Status.Done count ->
            [ Html.span
                  [ prop.className "fuaran-upload-stream-done"
                    prop.role "status"
                    prop.text (
                        if count = 1 then
                            "1 file uploaded."
                        else
                            string count + " files uploaded."
                    ) ] ]
        | Status.Refused announcement ->
            [ Html.span
                  [ prop.className "fuaran-upload-stream-refused"
                    prop.role "status"
                    prop.text announcement ] ]

    Html.div
        [ prop.className "fuaran-upload-stream"
          prop.onChange (fun (ev: Browser.Types.Event) -> beginUpload (box ev))
          prop.children (props.children @ statusLine) ]

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

/// The public surface — `Render.fs` invokes this when a destination is
/// declared, and renders the control without it when none is.
let streamShell (props: UploadStreamProps) : ReactElement =
    reactCreateElement (box renderStreamShell) (box props)
