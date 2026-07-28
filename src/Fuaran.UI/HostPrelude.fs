// The host prelude — the small set of HOST types the IDL's `THosted` slots name
// (Accessibility.role / .liveRegion, StateBehaviour.onError's arg), compiled AHEAD
// of `Generated.fs` so the generated code can reference them and their wire codecs.
// `Fuaran.UI.Types` re-exposes each as an alias, so consumers are unaffected.
//
// A byte-identical stub lives in Fuaran-Core's test assembly
// (`tests/Fuaran.Core.Tests/UiHostPrelude.fs`) so the generated snapshot compiles
// there too — keep the two in sync; the corpus pins the wire bytes on both sides.
module Fuaran.UI.HostPrelude

open Fuaran.Core

/// Wire-adjacent error taxonomy for `StateBehaviour.onError` payloads.
type ErrorKind =
    | NotFound
    | Forbidden
    | Server
    | Network
    | Timeout
    /// Client-side: the renderer encountered a `Binding` it could not
    /// resolve (accessor threw, value did not unbox to expected type,
    /// computed binding threw). Distinct from `Server` — the failure
    /// did not cross the wire. Downstream observability filters keyed
    /// on `Server` should NOT fire for these.
    | BindingResolution

/// The payload handed to a `StateBehaviour.OnError` fallback closure.
type ErrorPayload =
    { Kind: ErrorKind
      Message: string
      CorrelationId: string }

/// ARIA role — a closed convenience list plus `Custom` verbatim passthrough
/// (the wire position admits any string; canonical cases emit lower-case).
[<RequireQualifiedAccess>]
type AriaRole =
    | Button
    | Link
    | Dialog
    | Alert
    | Status
    | Banner
    | Navigation
    | Main
    | Form
    | Region
    | Heading
    | Progressbar
    | Tab
    | Tablist
    | Tabpanel
    | Custom of role: string

/// `aria-live` politeness — closed, lower-case on the wire.
[<RequireQualifiedAccess>]
type LiveRegionKind =
    | Polite
    | Assertive
    | Off

let encAriaRole (r: AriaRole) : JVal =
    JStr(
        match r with
        | AriaRole.Button -> "button"
        | AriaRole.Link -> "link"
        | AriaRole.Dialog -> "dialog"
        | AriaRole.Alert -> "alert"
        | AriaRole.Status -> "status"
        | AriaRole.Banner -> "banner"
        | AriaRole.Navigation -> "navigation"
        | AriaRole.Main -> "main"
        | AriaRole.Form -> "form"
        | AriaRole.Region -> "region"
        | AriaRole.Heading -> "heading"
        | AriaRole.Progressbar -> "progressbar"
        | AriaRole.Tab -> "tab"
        | AriaRole.Tablist -> "tablist"
        | AriaRole.Tabpanel -> "tabpanel"
        | AriaRole.Custom raw -> raw
    )

let decAriaRole (j: JVal) : Result<AriaRole, string> =
    match j with
    | JStr "button" -> Ok AriaRole.Button
    | JStr "link" -> Ok AriaRole.Link
    | JStr "dialog" -> Ok AriaRole.Dialog
    | JStr "alert" -> Ok AriaRole.Alert
    | JStr "status" -> Ok AriaRole.Status
    | JStr "banner" -> Ok AriaRole.Banner
    | JStr "navigation" -> Ok AriaRole.Navigation
    | JStr "main" -> Ok AriaRole.Main
    | JStr "form" -> Ok AriaRole.Form
    | JStr "region" -> Ok AriaRole.Region
    | JStr "heading" -> Ok AriaRole.Heading
    | JStr "progressbar" -> Ok AriaRole.Progressbar
    | JStr "tab" -> Ok AriaRole.Tab
    | JStr "tablist" -> Ok AriaRole.Tablist
    | JStr "tabpanel" -> Ok AriaRole.Tabpanel
    | JStr other -> Ok(AriaRole.Custom other)
    | _ -> Error "expected JSON string for aria role"

let encLiveRegionKind (k: LiveRegionKind) : JVal =
    JStr(
        match k with
        | LiveRegionKind.Polite -> "polite"
        | LiveRegionKind.Assertive -> "assertive"
        | LiveRegionKind.Off -> "off"
    )

let decLiveRegionKind (j: JVal) : Result<LiveRegionKind, string> =
    match j with
    | JStr "polite" -> Ok LiveRegionKind.Polite
    | JStr "assertive" -> Ok LiveRegionKind.Assertive
    | JStr "off" -> Ok LiveRegionKind.Off
    | JStr other -> Error("unknown liveRegion '" + other + "' (expected polite | assertive | off)")
    | _ -> Error "expected JSON string for liveRegion"
