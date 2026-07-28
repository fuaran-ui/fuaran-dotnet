module Fuaran.UI.Renderer.Server.Resume

// ============================================================================
//  Fuaran — zero-hydration resumability: server-side resume-envelope emission
//  (Phase 177 spike — throwaway-grade, decision-gating).
//
//  The strictly-lazier successor to isomorphic hydration (Phase 143). Where
//  `Hydration.renderHydratable` embeds the *whole* wire tree so the client can
//  reconstruct + `hydrateRoot` the page at load, resumability embeds only a
//  flat **`nodeId → Action` map** for the event-bearing nodes, ships one
//  document-root listener, and executes **zero framework JS at load** — the
//  Elmish loop resumes only on first interaction, on the touched subtree.
//
//  This is possible because a Fuaran `Action<'Msg>` is *serialisable typed
//  data, not an opaque closure*: `Navigate` / `Notify` / `SetState` /
//  `WriteToClipboard` / `AiTool` / `CommitLocal` are values the client reads
//  and runs directly against `IFuaranRuntime` with no view. The rationale is
//  written up in `docs/RESUMABILITY-EXPLORATION.md`; the measured spike verdict
//  is `docs/RESUMABILITY-SPIKE.md`.
//
//  Spike scope note. This module deliberately carries a *bounded slice* of the
//  wire `Action` encoder (the data-shaped cases) rather than calling the
//  canonical `CanonicalJson.encodeAction` — that encoder is `private`, and the
//  flat per-node map wants a small payload + a Fable-portable client decoder.
//  Production would expose the canonical codec and share one decoder; the
//  byte shape here (`$type` discriminator + sorted fields) matches the wire
//  format on purpose so the two converge cleanly.
// ============================================================================

open Feliz.ViewEngine
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

// ─── Resume disposition — how the client treats a node's handler ────────────
//
// Decided server-side, per node, so the client interpreter never re-derives it.
//   Interpret — data-shaped `Action`; run directly against `IFuaranRuntime`,
//               no module boot, no view (the ≈ 0-JS happy path).
//   Boot      — `Dispatch msg`; the msg is a closure sentinel on the wire, so
//               first interaction lazy-loads the module's update/view chunk and
//               hands the touched subtree to Fable/React (§3).
//   Fallback  — `Call` / `ReadFileBody` (obj-erased continuation) — degrades to
//               hydration for *that subtree only* (§5), never a broken page.

[<RequireQualifiedAccess>]
type ResumeDisposition =
    | Interpret
    | Boot
    | Fallback

/// Classify a (possibly nested) `Action` by its laziest safe client handling.
/// A `Chain` is only `Interpret` if every member interprets; a `Dispatch`
/// anywhere forces `Boot`; a `Call` / `ReadFileBody` anywhere forces `Fallback`
/// (the strictest disposition wins, since the subtree must honour all of them).
let rec disposition (action: Action<'Msg>) : ResumeDisposition =
    match action with
    | Action.Navigate _
    | Action.Notify _
    | Action.SetState _
    | Action.AiTool _
    | Action.WriteToClipboard _
    | Action.CommitLocal _ -> ResumeDisposition.Interpret
    | Action.Dispatch _ -> ResumeDisposition.Boot
    | Action.Call _
    | Action.ReadFileBody _
    // Invoke (Phase 283) dispatches a host-registered capability — the client cannot interpret it
    // without the host, so the SSR resume falls back (strictest, like Call).
    | Action.Invoke _ -> ResumeDisposition.Fallback
    | Action.Chain inner ->
        let rank =
            function
            | ResumeDisposition.Interpret -> 0
            | ResumeDisposition.Boot -> 1
            | ResumeDisposition.Fallback -> 2

        inner
        |> List.map disposition
        |> List.sortByDescending rank
        |> List.tryHead
        |> Option.defaultValue ResumeDisposition.Interpret

// ─── Bounded JSON helpers (spike-local; script-injection-safe) ──────────────

/// Escape a string for a JSON string literal AND for safe embedding inside a
/// `<script>` element (`<` / `>` / `&` → `\uXXXX`, same posture as
/// `Hydration.escapeForScript`). JSON parsers read the escapes back, so the
/// decoded value is byte-identical to the original.
let private jsonString (s: string) : string =
    let sb = System.Text.StringBuilder()
    sb.Append '"' |> ignore

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | '\t' -> sb.Append "\\t" |> ignore
        | '<' -> sb.Append "\\u003c" |> ignore
        | '>' -> sb.Append "\\u003e" |> ignore
        | '&' -> sb.Append "\\u0026" |> ignore
        | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

/// Faithful `JVal` → JSON for the data-shaped payloads (`Notify` / `SetState`
/// / `AiTool`): keys Ordinal-sorted, the pinned "R" float layout — matching
/// the canonical codec byte-for-byte, nested payloads included (the old
/// obj-erased version collapsed non-scalars to `"<opaque>"`).
let rec private jsonValueLite (v: Fuaran.Core.JVal) : string =
    match v with
    | Fuaran.Core.JStr s -> jsonString s
    | Fuaran.Core.JBool b -> (if b then "true" else "false")
    | Fuaran.Core.JInt n -> string n
    | Fuaran.Core.JFloat f -> System.String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R}", f)
    | Fuaran.Core.JArr xs -> "[" + (xs |> List.map jsonValueLite |> String.concat ",") + "]"
    | Fuaran.Core.JObj fields ->
        "{"
        + (fields
           |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
           |> List.map (fun (k, v) -> jsonString k + ":" + jsonValueLite v)
           |> String.concat ",")
        + "}"

/// Encode one data-shaped `Action` to the wire `$type`-discriminated shape.
/// `Dispatch` / `Call` / `ReadFileBody` carry the same closure sentinel the
/// canonical codec emits (`"<closure>"`) — the client reads the `$type` and the
/// node's disposition, never the sentinel, so it boots / falls back instead.
let rec encodeAction (action: Action<'Msg>) : string =
    match action with
    | Action.Navigate route -> sprintf "{\"$type\":\"Navigate\",\"route\":%s}" (jsonString route)
    | Action.Notify(channel, payload) ->
        sprintf "{\"$type\":\"Notify\",\"channel\":%s,\"payload\":%s}" (jsonString channel) (jsonValueLite payload)
    | Action.SetState(key, value) ->
        sprintf "{\"$type\":\"SetState\",\"key\":%s,\"value\":%s}" (jsonString key) (jsonValueLite value)
    | Action.AiTool(name, args) ->
        sprintf "{\"$type\":\"AiTool\",\"args\":%s,\"toolName\":%s}" (jsonValueLite args) (jsonString name)
    | Action.WriteToClipboard text -> sprintf "{\"$type\":\"WriteToClipboard\",\"text\":%s}" (jsonString text)
    | Action.CommitLocal nodeId -> sprintf "{\"$type\":\"CommitLocal\",\"nodeId\":%s}" (jsonString nodeId)
    | Action.Chain inner ->
        let ops = inner |> List.map encodeAction |> String.concat ","
        sprintf "{\"$type\":\"Chain\",\"ops\":[%s]}" ops
    | Action.Dispatch _ -> "{\"$type\":\"Dispatch\",\"msg\":\"<closure>\"}"
    | Action.Call(ApiEndpoint ep, onResult, into) ->
        // Phase 428 — mirror the canonical codec: `onResult` only when Some
        // (the closure sentinel), `into` only when Some. Ordinal field order:
        // $type < endpoint < into < onResult.
        let intoJson =
            match into with
            | Some(CallResultTarget.IntoState key) ->
                sprintf ",\"into\":{\"$type\":\"State\",\"key\":%s}" (jsonString key)
            | Some(CallResultTarget.IntoQuery name) ->
                sprintf ",\"into\":{\"$type\":\"Query\",\"name\":%s}" (jsonString name)
            | None -> ""

        let onResultJson =
            match onResult with
            | Some _ -> ",\"onResult\":\"<closure>\""
            | None -> ""

        sprintf "{\"$type\":\"Call\",\"endpoint\":%s%s%s}" (jsonString ep) intoJson onResultJson
    | Action.ReadFileBody _ -> "{\"$type\":\"ReadFileBody\",\"onRead\":\"<closure>\"}"
    | Action.Invoke(capabilityId, args) ->
        // Phase 283 — match the canonical CanonicalJson shape exactly (Ordinal: $type < args <
        // capabilityId; each arg {addr < value}).
        let argsJson =
            args
            |> List.map (fun (a, v) -> sprintf "{\"addr\":%s,\"value\":%s}" (jsonString a) (jsonString v))
            |> String.concat ","

        sprintf "{\"$type\":\"Invoke\",\"args\":[%s],\"capabilityId\":%s}" argsJson (jsonString capabilityId)

// ─── Event-bearing action collection ────────────────────────────────────────
//
// The directly-`Action`-bearing event handlers on the typed tree are
// `ButtonSpec.OnClick` and `FormSpec.OnSubmit` (the canonical click / submit
// cases §2 emphasises). The function-shaped handlers — `Select.OnChange : _ ->
// Action`, choice `OnSelect : int -> Action` — can't be read without applying
// them, so the spike leaves them to the per-subtree hydration fallback. The
// walk mirrors the renderer's container child map (Hydration's `childrenOf`).

let private childrenOf (node: Node<obj>) : Node<obj> list =
    match node.Kind with
    | NodeKind.Box(s) -> s.Children
    | NodeKind.SplitPanel(s) -> s.Children
    | NodeKind.Tabs(s) -> s.Children
    | NodeKind.Stepper(s) -> s.Children
    | NodeKind.SummaryList(s) -> s.Children
    | NodeKind.Disclosure(s) -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child ]
    | NodeKind.FragmentDecl s -> [ s.Body ]
    | _ -> []

/// The node's own directly-readable `Action`, if it is an event-bearing leaf.
let private actionOf (node: Node<obj>) : Action<obj> option =
    match node.Kind with
    | NodeKind.Button(spec) -> Some spec.OnClick
    | NodeKind.Form(spec) -> Some spec.OnSubmit
    | _ -> None

/// DFS collect every `(nodeId, Action)` for event-bearing nodes, in document
/// order — the resume envelope's payload.
let rec private collectActions (node: Node<obj>) : (string * Action<obj>) list =
    let (NodeId id) = node.Id

    let here =
        match actionOf node with
        | Some a -> [ id, a ]
        | None -> []

    here @ (childrenOf node |> List.collect collectActions)

// ─── Init-effect classifier (§4) ────────────────────────────────────────────
//
// SSR pre-satisfies the data-loading half of `init`. What remains is a small
// classifier over the residual effects, deciding *when* each runs at resume.

[<RequireQualifiedAccess>]
type InitEffectInput =
    /// Initial-data load via a query/`Cmd.OfAsync` — SSR already resolved it
    /// into the serialised `Model`.
    | DataLoad
    /// A live subscription the page needs before any interaction.
    | LiveSubscription
    /// A subscription tied to a specific island (boots with that island).
    | IslandSubscription of islandId: string
    /// Anything else.
    | Other

[<RequireQualifiedAccess>]
type InitEffectClass =
    | Skip
    | Eager
    | Deferred
    | Lazy

/// Classify one residual `init` effect into its resume handling (§4 table).
let classifyInitEffect (effect: InitEffectInput) : InitEffectClass =
    match effect with
    | InitEffectInput.DataLoad -> InitEffectClass.Skip
    | InitEffectInput.LiveSubscription -> InitEffectClass.Eager
    | InitEffectInput.IslandSubscription _ -> InitEffectClass.Deferred
    | InitEffectInput.Other -> InitEffectClass.Lazy

let private initClassName =
    function
    | InitEffectClass.Skip -> "skip"
    | InitEffectClass.Eager -> "eager"
    | InitEffectClass.Deferred -> "deferred"
    | InitEffectClass.Lazy -> "lazy"

// ─── Tree hash (resume-mismatch detection, §6) ──────────────────────────────

/// A deterministic hash of the canonical wire tree. The server stamps this on
/// the envelope; on first interaction the client compares it against the
/// `data-fuaran-resume-hash` marker on the resume root. A mismatch (the host
/// swapped the DOM after render) degrades to a client render rather than
/// interpreting a stale map. Reuses the Phase 138 deterministic-id hash (FNV-1a)
/// over the Phase 143 canonical encoding — the same parity contract, second
/// beneficiary.
let treeHash (node: Node<obj>) : string =
    CanonicalJson.encodeNode node
    |> Fuaran.UI.Renderer.Ids.deterministicCorrelationId

// ─── Envelope emission ──────────────────────────────────────────────────────

/// The id of the resume envelope `<script>` for a render root.
let scriptId (rootId: string) : string = sprintf "fuaran-resume-%s" rootId

/// Build the resume envelope JSON for `node`: the module id, the SSR-resolved
/// model (host-supplied, already serialised + allowlist-filtered — see §8 of
/// the exploration doc), the flat `nodeId → { action, disposition }` map, the
/// classified residual init effects, and the tree hash. Script-injection-safe.
let encodeEnvelope
    (moduleId: string)
    (modelJson: string)
    (initEffects: (string * InitEffectInput) list)
    (node: Node<obj>)
    : string =
    let actions =
        collectActions node
        |> List.map (fun (id, a) ->
            let disp =
                match disposition a with
                | ResumeDisposition.Interpret -> "interpret"
                | ResumeDisposition.Boot -> "boot"
                | ResumeDisposition.Fallback -> "fallback"

            sprintf "%s:{\"action\":%s,\"disposition\":%s}" (jsonString id) (encodeAction a) (jsonString disp))
        |> String.concat ","

    let effects =
        initEffects
        |> List.map (fun (channel, input) ->
            sprintf "%s:%s" (jsonString channel) (jsonString (initClassName (classifyInitEffect input))))
        |> String.concat ","

    sprintf
        "{\"moduleId\":%s,\"treeHash\":%s,\"model\":%s,\"actions\":{%s},\"initEffects\":{%s}}"
        (jsonString moduleId)
        (jsonString (treeHash node))
        // model is host-supplied JSON; embed verbatim (host owns its escaping +
        // serialisation allowlist). Falls back to `{}` when the host has no model.
        (if System.String.IsNullOrWhiteSpace modelJson then
             "{}"
         else
             modelJson)
        actions
        effects

/// The resume envelope `<script type="application/json">` element, carrying the
/// flat action map keyed to the render root's stable id, plus the tree-hash
/// marker the client compares for resume-mismatch detection.
let envelopeElement
    (moduleId: string)
    (modelJson: string)
    (initEffects: (string * InitEffectInput) list)
    (node: Node<obj>)
    : ReactElement =
    let (NodeId rootId) = node.Id
    let json = encodeEnvelope moduleId modelJson initEffects node

    Html.script
        [ prop.custom ("type", "application/json")
          prop.id (scriptId rootId)
          prop.custom ("data-fuaran-resume-root", rootId)
          prop.custom ("data-fuaran-resume-hash", treeHash node)
          prop.dangerouslySetInnerHTML json ]

/// Render a **resumable** HTML string: the inert server-rendered body (the same
/// `Render.render` path hydration uses — crawlable, no-JS HTML) plus the resume
/// envelope `<script>`. The page boots zero framework JS; the client
/// `Renderer.Resume.install` mount resumes the Elmish loop on first interaction.
/// The host places this where it wants the resumable surface; the document
/// shell + reference CSS stay host-owned.
let renderResumable
    (sources: BindingResolver.BindingSources)
    (moduleId: string)
    (modelJson: string)
    (initEffects: (string * InitEffectInput) list)
    (node: Node<obj>)
    : string =
    Render.render sources node
    + Render.htmlView (envelopeElement moduleId modelJson initEffects node)
