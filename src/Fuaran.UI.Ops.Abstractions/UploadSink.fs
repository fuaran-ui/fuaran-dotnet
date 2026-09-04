module Fuaran.UI.Ops.UploadSink

// ============================================================================
//  `IFuaranUploadSink` (Phase 1117) — the host-owned destination a large file
//  is streamed TO, and the typed reference that comes back.
//
//  Until this phase the only route a selected file's BODY could take was
//  `Action.ReadFileBody` -> a base64 string -> the message loop. That is the
//  right shape for a small payload and structurally wrong for anything else:
//  the body is inflated by a third, it is copied into whatever the host does
//  with a dispatched message, and — the half that settles it — it lands in the
//  durable authoring record. A ten-megabyte video becomes a fourteen-megabyte
//  string inside a hash-chained op stream that replays forever. The op stream
//  must carry a REFERENCE.
//
//  ── The seam is the transport, and that is deliberate ─────────────────────
//  This interface hands the sink a whole `FileSelection` and a progress
//  callback; it does NOT hand it chunks. The chunking, the resumption, the
//  retry, the content hashing and the framing are the sink's, because they are
//  the TRANSPORT's, and a framing declared on this surface is one every host
//  inherits and none can change. (The same sentence `ActionInvocation` uses
//  about retention, for the same reason.) `UploadSink.inMemory` chunks
//  observably — it reports one `UploadProgress` per chunk over a declared chunk
//  size — so "streamed in chunks" is a property a test can assert rather than a
//  claim this file makes about hosts it cannot see.
//
//  ── Callback-shaped, not `Async` ──────────────────────────────────────────
//  `IFuaranRuntime.Call` and `.ReadFileBody` are both callback-shaped for the
//  reason stated there: the read is async at the host level, but the typed
//  dispatch surface stays callback-shaped. A third spelling of the same idea on
//  the seam beside them would be a difference with no meaning, and `Async` does
//  not survive the renderer's Fable pipeline as cheaply as a closure does.
//
//  ── Default-deny, and where the two refusals live ─────────────────────────
//  A destination is a NAME the host has registered, never a URL (see the wire
//  member's declaration in the IDL). Two different questions are asked about
//  it, in this order, and they have two different owners:
//
//    1. MAY THIS TREE CAUSE AN UPLOAD AT ALL? The renderer's dispatch gate —
//       `ActionDescriptor.Upload`, refused by `CanDispatch` exactly as `Call` /
//       `Navigate` / `Export` are, and denied by every shipped runtime.
//    2. IS THIS DESTINATION REGISTERED? The sink's own `Destinations` set. An
//       id the sink does not name is `UnregisteredDestination` and there is NO
//       FALLBACK — the id is not tried as a path, a URL, or a default
//       destination, because a fallback would make the registration advisory,
//       which is indistinguishable from not having it. (The custom-renderer
//       registry's ruling, at a different seam.)
//
//  These are a gate and a resolution, not two gates: the first is policy about
//  the tree, the second is a fact about the host. A host with no sink wired at
//  all refuses with `NoSink`, which is a different sentence from "you named a
//  destination I do not have" and is kept distinct for that reason.
//
//  ── What this file does NOT claim ─────────────────────────────────────────
//  Nothing about what a sink DOES with the bytes: where they are stored, for
//  how long, who can read them back, whether they are scanned or encrypted.
//  Those are the host's, and a promise made here would be one no implementation
//  is bound by. The seam's whole claim is that the bytes go to a destination
//  the host named and that only a reference comes back.
// ============================================================================

open Fuaran.UI.HostPrelude

/// What a completed upload leaves behind — the ONLY thing that may reach the
/// op stream, a telemetry sink, or a state cell. Four fields, and each is there
/// because a consumer cannot do its job without it:
///
///   * `FileId` — the sink's own handle, the token a later fetch or delete
///     names. Opaque to this tier by construction: nothing here parses it.
///   * `Hash` — the content digest the sink computed over the bytes it
///     received. This is what makes the reference VERIFIABLE rather than merely
///     short: a replay can prove the artefact it fetches is the artefact that
///     was uploaded. Format is the sink's and is not constrained here.
///   * `Size` — the byte count the sink accepted, which is not always the byte
///     count the selection claimed (a truncating or transcoding sink is a
///     legitimate sink).
///   * `ContentType` — the type the sink RECORDED, likewise not necessarily the
///     one the browser guessed.
///
/// Deliberately carrying NO URL. A reference that carried a fetchable address
/// would put the destination back on a path a decoded tree can reach, which is
/// the thing the named-destination wire member exists to prevent.
type UploadedRef =
    { FileId: string
      Hash: string
      Size: int64
      ContentType: string }

/// One progress report. `TotalBytes` is what the sink expects to receive, which
/// may be `0L` where a sink cannot know it in advance — a consumer renders an
/// indeterminate state rather than dividing by zero.
type UploadProgress = { BytesSent: int64; TotalBytes: int64 }

/// Why an upload did not produce a reference. Every arm is a REFUSAL a consumer
/// can render and a reader can act on; there is no "something went wrong" case,
/// because a failure state that cannot be told apart from another failure state
/// is the silent failure this phase exists to remove.
[<RequireQualifiedAccess>]
type UploadRefusal =
    /// No sink is wired on this host at all. Distinct from
    /// `UnregisteredDestination` on purpose: "this host does no uploads" and
    /// "this host does uploads and not that one" send a reader to different
    /// places.
    | NoSink of destination: string
    /// The host has a sink and it does not name this destination. Default-deny
    /// with no fallback — see this file's header.
    | UnregisteredDestination of destination: string
    /// The sink's own size limit, enforced sink-side because the limit is the
    /// destination's fact and nothing on the wire may state it. `limitBytes` is
    /// the sink's bound, so a reader is told what would fit.
    | TooLarge of destination: string * limitBytes: int64
    /// The sink's own type limit, likewise sink-side. Carries the type that was
    /// refused — a MIME string the browser assigned, not user-typed content.
    | UnacceptableType of destination: string * contentType: string
    /// The transport failed. `detail` is a sink-supplied, LOG-SAFE description:
    /// implementations must not put the file's name, its bytes or a credential
    /// in it. This is the one arm whose text a sink chooses, so the constraint
    /// is stated where the constraint binds.
    | TransportFailed of destination: string * detail: string
    /// The dispatch gate refused this tree an upload to this destination
    /// (`ActionDescriptor.Upload`). Recorded as a refusal rather than as
    /// nothing, so a denied upload and a broken one are never the same
    /// observation — the Phase 782 "deny is RECORDED, not silent" line.
    | DispatchDenied of destination: string

/// The upload seam. One member of registry and one of transport.
///
/// Implementations MUST NOT throw: like the telemetry and action sinks, a
/// failure is reported through the completion callback, never raised into a
/// renderer's event handler where nothing can catch it usefully.
///
/// `Upload` MUST call `onComplete` exactly once, whatever happens. A sink that
/// completes zero times leaves the control saying "uploading" forever, which is
/// worse than any refusal it could have reported.
type IFuaranUploadSink =
    /// The destination ids this sink serves. The registry, and the whole of it:
    /// a destination not in this set is refused, never resolved another way.
    ///
    /// A SET rather than a `TryResolve` member because a consumer needs to
    /// answer "is this id registered" before it streams anything — the refusal
    /// must reach the reader before a byte moves, not after.
    abstract member Destinations: Set<string>

    /// Stream one selection to one destination. `onProgress` may be called any
    /// number of times, including zero; `onComplete` is called exactly once.
    abstract member Upload:
        destination: string *
        selection: FileSelection *
        onProgress: (UploadProgress -> unit) *
        onComplete: (Result<UploadedRef, UploadRefusal> -> unit) ->
            unit

/// A short, LOG-SAFE description of a refusal — the class and the
/// author-declared destination NAME, never a file name and never a byte.
/// The destination is grade B in `docs/ACTION-LOG-PRIVACY.md`'s vocabulary
/// (an author-declared name), which is what makes the line diagnosable at
/// all; everything the reader chose stays out of it.
let describe (r: UploadRefusal) : string =
    match r with
    | UploadRefusal.NoSink d -> sprintf "no upload sink is wired on this host (destination '%s')" d
    | UploadRefusal.UnregisteredDestination d -> sprintf "upload destination '%s' is not registered" d
    | UploadRefusal.TooLarge(d, limit) ->
        sprintf "the file is larger than destination '%s' accepts (limit %d bytes)" d limit
    | UploadRefusal.UnacceptableType(d, ct) -> sprintf "destination '%s' does not accept files of type '%s'" d ct
    | UploadRefusal.TransportFailed(d, detail) -> sprintf "upload to '%s' failed: %s" d detail
    | UploadRefusal.DispatchDenied d -> sprintf "upload to '%s' denied by policy" d

/// The reader-facing sentence for a refusal. Deliberately SHORTER and
/// vaguer than `describe`: this text reaches a rendered `role="status"`
/// line in a document a reader is looking at, and a reader is owed "this
/// did not happen and it is not your fault", not a host's configuration.
/// The diagnosable detail goes to the host's `Warn` channel through
/// `describe`, which is where an operator looks.
let announce (r: UploadRefusal) : string =
    match r with
    | UploadRefusal.TooLarge _ -> "This file is too large to upload here."
    | UploadRefusal.UnacceptableType _ -> "This kind of file cannot be uploaded here."
    | UploadRefusal.TransportFailed _ -> "The upload did not finish. Nothing was saved."
    | UploadRefusal.NoSink _
    | UploadRefusal.UnregisteredDestination _
    | UploadRefusal.DispatchDenied _ -> "Uploading is not available here."

/// The default sink — serves no destination and refuses every upload with
/// `NoSink`. This is what an unwired host has, and it is a real object
/// rather than `None` so that a consumer holding a sink and a consumer
/// holding none take the same code path.
///
/// Note the renderer still models an ABSENT sink as `None` on its context:
/// the two are the same refusal, and the option is what keeps an unwired
/// host from paying for a member it never calls (GP 13).
let noSink: IFuaranUploadSink =
    { new IFuaranUploadSink with
        member _.Destinations = Set.empty

        member _.Upload(destination, _, _, onComplete) =
            onComplete (Error(UploadRefusal.NoSink destination)) }

/// One record of what an in-memory sink accepted — the reference it minted
/// plus the selection's name, for a test that needs to say WHICH file a
/// reference belongs to. Never the bytes: this sink does not keep them,
/// which is the point of it being a reference sink.
type InMemoryUpload =
    { Destination: string
      Name: string
      Ref: UploadedRef }

/// The in-memory REFERENCE sink — the shape a test drives and the shape a
/// production sink is measured against.
///
/// It genuinely chunks: it reports one `UploadProgress` per `chunkBytes` of
/// the selection's declared size, so a consumer's progress rendering is
/// exercised rather than asserted. It computes no real digest — it mints a
/// deterministic one from the selection's own metadata, so a test's
/// expected reference is a value the test can write down. A production sink
/// digests the bytes it received; this one has none, and says so rather
/// than pretending.
///
/// `limitBytes` and `acceptTypes` exist because size and type limits are
/// SINK-SIDE facts (nothing on the wire states them), and a reference
/// implementation that could not refuse would leave both refusal arms
/// unexercised. `acceptTypes` empty means "accept any type".
type InMemorySink(destinations: string seq, ?chunkBytes: int, ?limitBytes: int64, ?acceptTypes: string seq) =
    let destinationSet = Set.ofSeq destinations
    let chunk = max 1 (defaultArg chunkBytes 65536)
    let limit = defaultArg limitBytes System.Int64.MaxValue
    let accepted = acceptTypes |> Option.map Set.ofSeq |> Option.defaultValue Set.empty
    let accepts = ResizeArray<InMemoryUpload>()

    /// Everything this sink has accepted, oldest first.
    member _.Accepted: InMemoryUpload list = List.ofSeq accepts

    interface IFuaranUploadSink with
        member _.Destinations = destinationSet

        member _.Upload(destination, selection, onProgress, onComplete) =
            if not (destinationSet.Contains destination) then
                onComplete (Error(UploadRefusal.UnregisteredDestination destination))
            elif selection.Size > limit then
                onComplete (Error(UploadRefusal.TooLarge(destination, limit)))
            elif not (Set.isEmpty accepted) && not (accepted.Contains selection.MimeType) then
                onComplete (Error(UploadRefusal.UnacceptableType(destination, selection.MimeType)))
            else
                // The chunk loop. `sent` never exceeds the declared size, so
                // a consumer rendering `sent / total` cannot be handed a
                // ratio above one — the last chunk is short, as a real one
                // is.
                let mutable sent = 0L

                while sent < selection.Size do
                    sent <- min selection.Size (sent + int64 chunk)

                    onProgress
                        { BytesSent = sent
                          TotalBytes = selection.Size }

                // A zero-byte file is a legitimate selection and the loop
                // above reports nothing for it, so it gets one terminal
                // report — a consumer that renders progress only on a
                // callback would otherwise never leave its initial state.
                if selection.Size = 0L then
                    onProgress { BytesSent = 0L; TotalBytes = 0L }

                let reference =
                    { FileId = destination + "/" + selection.Ref.Id
                      Hash = sprintf "inmemory:%s:%d" selection.Name selection.Size
                      Size = selection.Size
                      ContentType = selection.MimeType }

                accepts.Add
                    { Destination = destination
                      Name = selection.Name
                      Ref = reference }

                onComplete (Ok reference)

/// Convenience constructor for the common test shape — a sink that serves
/// the named destinations and refuses nothing else.
let inMemory (destinations: string seq) = InMemorySink(destinations)
