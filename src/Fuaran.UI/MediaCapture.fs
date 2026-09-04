module Fuaran.UI.MediaCapture

open Fuaran.UI.Types

// ============================================================================
//  Phase 1116 — the `FileUploadSpec.Capture` projection, in ONE place.
//
//  Two questions live here, and they are separate on purpose:
//
//    * `keyword` — what the HTML `capture` attribute's value is for a given
//      device. Both renderers read this, so the hydrated control and the
//      zero-JS floor cannot disagree about which attribute value a document
//      produces.
//
//    * `acceptSelectsDevice` — whether the `Accept` list can actually select
//      that device. The renderers do NOT consult it: they project both members
//      exactly as declared and repair neither. It is read by the pre-emit
//      validator (FUARAN134) and by the server-driven authoring check, which
//      REPORT the incoherent pair. A renderer that quietly synthesised an
//      `accept` would put a filter on the wire's behalf that nobody wrote, make
//      the emitted bytes depend on renderer defaults, and — the half that
//      settles it — make the one case the rule most needs to catch, an empty
//      `Accept`, unreportable, because the repair would have happened before
//      anything could see it. One rule, checked where the pair becomes visible;
//      a coercion at neither place. (The Phase 1130 `Color` posture.)
//
//  WHY A FACING KEYWORD AT ALL. The HTML `capture` attribute is an enumerated
//  attribute whose keywords name a camera FACING — `user` and `environment` —
//  and whose non-keyword values are non-conforming markup. The device itself is
//  chosen by `accept`. So the wire carries the DEVICE, which is the fact the
//  document knows, and the projection picks the conforming keyword that is
//  least wrong for it: `Camera` is environment-facing, because a document
//  asking for a photo is asking for a photo OF something and a self-portrait is
//  the other keyword; `Microphone` is user-facing, because a recording made by
//  the reader is by construction the reader's own side, and the keyword
//  constrains nothing on a device that has no facing.
//
//  A facing case of its own is a deliberate NON-addition here. It would be a
//  second thing this enum names — which device, and which way it points — and
//  the second is only ever meaningful for one of the two cases. If the demand
//  arrives it is an addition to a closed set, which is what the enum bought.
// ============================================================================

/// The HTML `capture` attribute value for a device. Both keywords are
/// conforming enumerated-attribute values, so no host emits invalid markup.
let keyword (source: CaptureSource) : string =
    match source with
    | CaptureSource.Camera -> "environment"
    | CaptureSource.Microphone -> "user"

/// The MIME top-level type whose files this device produces. `Camera` is
/// deliberately BOTH `image` and `video`: the platform camera on every handset
/// takes stills and clips from one surface, and `accept="video/*"` with a
/// camera capture is an entirely ordinary "record a short clip" upload.
let private mediaTypesOf (source: CaptureSource) : string list =
    match source with
    | CaptureSource.Camera -> [ "image"; "video" ]
    | CaptureSource.Microphone -> [ "audio" ]

/// Whether one `accept` entry admits files of the given MIME top-level type.
///
/// The entries an `accept` list may hold are a MIME type (`image/png`), a MIME
/// wildcard (`image/*`) or a filename extension (`.csv`). Only the first two can
/// select a device, and that is the honest limit of what this predicate claims:
/// an extension list names files on a disk, and a platform camera hands back
/// whatever container it likes — so `accept=".jpg"` with a camera capture is
/// exactly the incoherent pair the rule exists to report, not a false positive.
///
/// `*/*` is likewise NOT a selection, for the same reason the empty list is not
/// one: it admits the device without choosing it, and choosing is the point.
let private entryAdmits (mediaType: string) (entry: string) : bool =
    let e = entry.Trim().ToLowerInvariant()
    e = mediaType + "/*" || (e.StartsWith(mediaType + "/") && not (e.EndsWith "/"))

/// Whether an `Accept` list selects the declared capture device.
///
/// An EMPTY list is `false`, and that is the rule's most useful answer rather
/// than an edge case. An empty `accept` admits every file, so it does not
/// exclude the device — but it does not SELECT it either, and selection is the
/// whole of what a capture declaration is for: with nothing narrowing it, which
/// device the platform opens is the user agent's guess, which is precisely the
/// uncertainty the author declared `capture` to remove. An upload asking for a
/// microphone and getting a camera is the fake affordance in its purest form.
let acceptSelectsDevice (source: CaptureSource) (accept: string list) : bool =
    let wanted = mediaTypesOf source

    accept
    |> List.exists (fun entry -> wanted |> List.exists (fun mt -> entryAdmits mt entry))
