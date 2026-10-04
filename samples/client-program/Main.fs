module Fuaran.Program.Sample.Main

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime
open Fuaran.Program.UI

// ============================================================================
//  A generated app running CLIENT-ONLY under the bounded program loop.
//
//  No server, no hand-authored `update`, no `'Msg` type. The page holds a
//  wire-decoded tree and a state store; a click becomes a `LiveEvent`, the
//  bounded interpreter folds it against the store, the tree's bindings
//  re-resolve, and the tier-1 renderer draws the result. Every one of those
//  steps is the SAME code the server placement runs — only the transport
//  differs.
//
//  This is the browser half of "one algebra, two placements". The conformance
//  family asserts the two agree; this sample is what makes the claim visible.
// ============================================================================

// ─── DOM + React interop ────────────────────────────────────────────────────
//
// Deliberately raw. The loop under test needs a mount point and a delegated
// click listener, and nothing else — pulling in a UI framework here would put
// a hand-authored update loop back into the one sample whose whole point is
// not having one.

[<Emit("document.getElementById($0)")>]
let private byId (id: string) : obj = jsNative

[<Emit("$0.addEventListener($1, $2)")>]
let private on (target: obj) (event: string) (handler: obj -> unit) : unit = jsNative

/// The clicked element's nearest Fuaran-addressed ancestor, or `null`. The
/// renderer stamps `data-fuaran-node-id` on every node it draws, which is what
/// lets a delegated listener recover the node identity an event belongs to —
/// the same trick the server-driven browser shim uses.
[<Emit("$0.target && $0.target.closest ? $0.target.closest('[data-fuaran-node-id]') : null")>]
let private closestNode (ev: obj) : obj = jsNative

[<Emit("$0 ? $0.getAttribute('data-fuaran-node-id') : null")>]
let private nodeIdOf (el: obj) : string = jsNative

[<Emit("$0 !== null && $0 !== undefined")>]
let private isSome (x: obj) : bool = jsNative

[<ImportMember("react-dom/client")>]
let private createRoot (container: obj) : obj = jsNative

// ─── The host's effect performers ───────────────────────────────────────────
//
// Each mirrors the server-driven browser shim's arm for the same effect, so the
// two transports of the one vocabulary behave alike. Registering is a HOST act
// (DECISIONS.md D3): what is not registered here cannot run, whatever the tree
// asks for and however permissive the gate.

[<Emit("navigator.clipboard && navigator.clipboard.writeText($0)")>]
let private writeClipboard (text: string) : unit = jsNative

[<Emit("window.location.href = $0")>]
let private navigateTo (route: string) : unit = jsNative

/// The `Blank` browsing context. `noopener,noreferrer` is not decoration and is
/// not this sample's invention: the opened document would otherwise hold a live
/// `window.opener` handle back to this one and could navigate it away, and the
/// tier discharges that on every host rather than delegating it. A sample that
/// mirrored the arm without the window features would be demonstrating the one
/// thing about it that must not be copied.
[<Emit("window.open($0, '_blank', 'noopener,noreferrer')")>]
let private openBlank (route: string) : unit = jsNative

[<Emit("history.pushState({ fuaranRoute: $0 }, '', $0)")>]
let private pushState (route: string) : unit = jsNative

[<Emit("(function(){ var e = document.getElementById($0); if (e) e.focus(); })()")>]
let private focusNode (nodeId: string) : unit = jsNative

[<Emit("(function(){ var a = document.createElement('a'); a.href = $0; a.download = $1 || ''; document.body.appendChild(a); a.click(); a.remove(); })()")>]
let private downloadUrl (url: string) (name: string) : unit = jsNative

// ─── The generated app ──────────────────────────────────────────────────────

/// A counter, expressed the way an emitter would express one: a button whose
/// `OnClick` is a bounded `SetState`, and a Markdown node whose text is BOUND
/// to that state key. Nothing here is a callback — the interactivity is
/// entirely in the data.
let private authored: Node<obj> =
    let heading =
        Fuaran.heading
            "title"
            { Defaults.heading with
                Text = TextSource.Literal "Client-only bounded program" }

    let readout =
        let n = Fuaran.markdown "readout" "placeholder"

        { n with
            Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State("clicks", Some "not clicked yet")) }) }

    let bump =
        Fuaran.button
            "bump"
            { Defaults.button<obj> with
                Label = TextSource.Literal "Click me"
                OnClick = Action.SetState("clicks", Some(Fuaran.Core.JStr "clicked!"), None) }

    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = [ heading; bump; readout ] }

/// The wire JSON an emitter would hand us. Produced here by encoding the
/// authored tree, so the sample cannot drift from the format the way a pasted
/// literal would — the DECODE below is the real thing either way, and it is the
/// decode that matters: what runs is a tree whose closures are inert sentinels.
let private wireJson: string = CanonicalJson.encodeNode authored

// ─── Boot ───────────────────────────────────────────────────────────────────

let private start () =
    match JsonDecode.decodeNode wireJson with
    | Error err -> printfn "decode failed: %A" err
    | Ok wire ->
        let container = byId "fuaran-program-root"
        let root = createRoot container

        // The loop holds the program; the render seam draws whatever it is
        // handed. `mutable` is the honest shape here — the browser event loop
        // is the thing threading state, and pretending otherwise would mean
        // inventing the update function this sample exists to do without.
        let mutable program = Unchecked.defaultof<Program>

        // A note on what you will see in the console. The renderer has its OWN
        // dispatch gate, and since it defaults to deny, React's `onClick` on the
        // button logs "dispatch denied by policy gate: SetState(clicks)". That
        // is correct and is not the loop failing: the renderer is refusing a
        // path the bounded loop deliberately owns instead. The state change you
        // see on screen came from the delegated listener below, through the
        // shared interpreter — which is exactly the separation the bounded path
        // exists to enforce.
        let render (node: Node<obj>) =
            let sources =
                { empty with
                    State =
                        (if obj.ReferenceEquals(program, null) then
                             Map.empty
                         else
                             program.Store.State) }

            root?render (Render.renderWithSources sources ignore node)

        // The closed, default-deny effect registry. `ReadFileBody` is
        // deliberately NOT registered: this sample has no file input, so the
        // capability is absent from the host rather than present-and-refused,
        // and a tree asking for one gets a recorded `Unregistered` denial.
        let effects =
            EffectRegistry.denyAll
            |> EffectRegistry.register "WriteToClipboard" (fun fx ->
                match fx with
                | ClientEffect.WriteToClipboard text -> writeClipboard text
                | _ -> ())
            |> EffectRegistry.register "Navigate" (fun fx ->
                match fx with
                | ClientEffect.Navigate(route, NavigateTarget.Self) -> navigateTo route
                | ClientEffect.Navigate(route, NavigateTarget.Blank) -> openBlank route
                | _ -> ())
            |> EffectRegistry.register "PushState" (fun fx ->
                match fx with
                | ClientEffect.PushState route -> pushState route
                | _ -> ())
            |> EffectRegistry.register "Focus" (fun fx ->
                match fx with
                | ClientEffect.Focus nodeId -> focusNode nodeId
                | _ -> ())
            |> EffectRegistry.register "Download" (fun fx ->
                match fx with
                | ClientEffect.Download(url, name) -> downloadUrl url name
                | _ -> ())
            |> EffectRegistry.permissive
            |> EffectRegistry.onDenied (fun d -> printfn "effect refused: %s" (EffectDenial.describe d))

        let services =
            { ProgramServices.createPermissive render with
                Effects = effects
                OnApply = fun ops -> printfn "journal: %d op(s)" (List.length ops) }

        program <- Program.mkBounded services empty wire

        on container "click" (fun ev ->
            let el = closestNode ev

            if isSome el then
                let event: LiveEvent =
                    { ConnId = "client-only"
                      NodeId = nodeIdOf el
                      Event = "click"
                      Payload = Map.empty
                      LastSeq = 0 }

                let next, out = Program.handleEvent program event
                program <- next

                match out.Rejected with
                | Some reject -> printfn "refused: %A" reject
                | None -> ())

start ()
