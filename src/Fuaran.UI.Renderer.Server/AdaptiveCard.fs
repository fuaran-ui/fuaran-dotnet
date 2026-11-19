module Fuaran.UI.Renderer.Server.AdaptiveCard

// ============================================================================
//  Fuaran — the Adaptive Card projection (Phase 1814).
//
//  Adaptive Cards has one thing this language cannot build: renderers already
//  shipped inside hosts we do not control. This module reaches those hosts by
//  PROJECTION rather than by competing for them — a lossy, fidelity-declared
//  lowering from a bounded tree to one Adaptive Card, in the `Renderer.Server`
//  family beside the email projection (Phase 441) and on its rule: what the
//  target cannot express becomes a labelled OPEN-LIVE link (`Action.OpenUrl`),
//  never a half-working control, and every degradation is returned to the
//  caller by node id.
//
//  THE TARGET IS SCHEMA 1.4, and only what both Teams and Outlook render. The
//  Adaptive Cards designer targets 1.5 for its Teams host and 1.4 for its
//  Outlook host, and Outlook's own reference names two 1.4 members it does not
//  display: `Action.Submit` and `Input.Time`. So this projection never writes
//  either — every action that runs on a backend is `Action.Execute` (Universal
//  Actions, which both hosts route to the same bot), and a time-of-day field is
//  an `Input.Text` with a pattern, declared. Host-specific extensions (Teams
//  charts, badge, rating, carousel) are out: a projection only one host can
//  read is a Teams projection. `docs/ADAPTIVE-CARD-PROJECTION.md` carries the
//  table and the sources.
//
//  THE PER-KIND RULING IS `RenderFidelity.cardRulings`, published as the
//  `adaptiveCard` column of `render-fidelity.json`, so every host reads one
//  ruling. The test corpus holds this file to it kind by kind, and the match
//  below is exhaustive, so a new `NodeKind` fails to compile here.
//
//  THE CARD IS INERT. Every binding resolves ONCE, at projection time, through
//  the shared `ServerRenderContext` — the same resolution the SSR document, the
//  email digest and the speech script use — so a card cannot disagree with the
//  page it links to about what a number is. Interaction is a round trip:
//  `Action.Execute` posts the declared verb and the node id to the backend that
//  owns the tree, and the backend answers with a new card.
//
//  STRINGS AND URLS. Every string reaches the card as LITERAL text: a `TextRun`
//  (hosts never read a `RichTextBlock` as markdown), or a `Fact` escaped against
//  the markdown hosts do apply there. A `TextBlock` is never written, because a
//  host reads its text as markdown and a bound string could then author a link.
//  Every tree-authored URL passes the context's destination policy (Phases
//  1026 / 1032) under its egress class before it is written, and a card host
//  resolves nothing relative, so a same-origin URL degrades rather than ships.
//  The live-surface URL is the host's own declaration, as in the email
//  projection, and is not policy-checked.
//
//  LIMITS. A tree over the declared `HostLimits` (Phase 1817; `HostLimits.card`
//  by default) is REFUSED through `PreEmitValidate.validateWithLimits` with the
//  1817 code before any card JSON is produced; the projected card's own UTF-8
//  size is then held to the same `MaxSerializedBytes`, with the same code.
//
//  DETERMINISM. Same tree, same sources, same options => same bytes: no clock
//  read, no identifier minting, no iteration over an unordered collection.
// ============================================================================

open System
open System.Text.Json
open System.Text.Json.Nodes
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.RenderFidelity

/// The Adaptive Cards schema version every projected card declares.
[<Literal>]
let SchemaVersion = "1.4"

/// The published schema the card is written against.
[<Literal>]
let SchemaUrl = "http://adaptivecards.io/schemas/adaptive-card.json"

// ─── Degradations and refusals ──────────────────────────────────────────────

/// Why a node is not projected as itself.
[<RequireQualifiedAccess>]
type DegradationRule =
    /// The node is projected to an open-live link (or note).
    | OpenLive
    /// The node sits inside an open-live node, which is linked, not projected.
    | InsideOpenLive
    /// The node's content survives as literal text; its form does not.
    | FlattenedToText
    /// The node's kind is ruled omitted (`Icon`, `Skeleton`, `FragmentDecl`).
    | Omitted
    /// `visible` resolved false on the deciding node.
    | NotVisible
    /// A `Switch` case not selected, or an `ErrorBoundary` fallback.
    | NotTaken
    /// A closed `Toast`.
    | Closed
    /// A `Disclosure` the tree has closed, shown expanded.
    | AlwaysExpanded
    /// A layout the card approximates (a masonry layout as a grid).
    | LayoutApproximated of detail: string
    /// A form field projected through a different input than its own.
    | InputSubstituted of field: string * into: string
    /// A form field's validation rule, which no 1.4 input carries.
    | FieldRuleNotCarried of field: string
    /// An action that becomes `Action.Execute` with a declared verb.
    | ActionAsExecute of action: string * verb: string
    /// An action only the live runtime can perform; the control is open-live.
    | ClientOnlyAction of action: string
    /// A URL the destination policy refused, by slot and log-safe reason.
    | DestinationRefused of slot: string * reason: string
    /// A same-origin URL, which a card host cannot resolve.
    | RelativeDestination of slot: string
    /// A media source whose extension names no MIME type.
    | MediaTypeUnknown
    /// An image inside markdown, which a card can only show as its alt text.
    | MarkdownImageAsText
    /// A disabled control, projected as a note.
    | Disabled
    /// A `FragmentRef` naming no declared fragment.
    | UnresolvedFragment of name: string
    /// Nesting beyond `WireLimits.MaxDepth`.
    | DepthExceeded

/// The stable token a rule is reported as.
let ruleId (rule: DegradationRule) : string =
    match rule with
    | DegradationRule.OpenLive -> "open-live"
    | DegradationRule.InsideOpenLive -> "inside-open-live"
    | DegradationRule.FlattenedToText -> "flattened-to-text"
    | DegradationRule.Omitted -> "omitted"
    | DegradationRule.NotVisible -> "not-visible"
    | DegradationRule.NotTaken -> "not-taken"
    | DegradationRule.Closed -> "closed"
    | DegradationRule.AlwaysExpanded -> "always-expanded"
    | DegradationRule.LayoutApproximated _ -> "layout-approximated"
    | DegradationRule.InputSubstituted _ -> "input-substituted"
    | DegradationRule.FieldRuleNotCarried _ -> "field-rule-not-carried"
    | DegradationRule.ActionAsExecute _ -> "action-as-execute"
    | DegradationRule.ClientOnlyAction _ -> "client-only-action"
    | DegradationRule.DestinationRefused _ -> "destination-refused"
    | DegradationRule.RelativeDestination _ -> "relative-destination"
    | DegradationRule.MediaTypeUnknown -> "media-type-unknown"
    | DegradationRule.MarkdownImageAsText -> "markdown-image-as-text"
    | DegradationRule.Disabled -> "disabled"
    | DegradationRule.UnresolvedFragment _ -> "unresolved-fragment"
    | DegradationRule.DepthExceeded -> "depth-exceeded"

/// One node not projected as itself. `DecidedAt` is the id of the node whose
/// state or ruling caused it — the node itself, or the ancestor that decided
/// for its subtree — so a reader can go from any degradation to its cause.
type Degradation =
    { NodeId: string
      Kind: string
      Rule: DegradationRule
      DecidedAt: string }

/// Why no card was produced. Never a silent omission: a tree the target cannot
/// carry is a typed refusal naming the declaration that refused it.
[<RequireQualifiedAccess>]
type ProjectionRefusal =
    /// The tree is over the declared host limits (Phase 1817). Each finding is
    /// the `FUARAN*` code and its message, exactly as `PreEmitValidate`
    /// describes the defect.
    | HostLimitsExceeded of limits: string * findings: (string * string) list
    /// The projected card's own UTF-8 size is over the declared
    /// `MaxSerializedBytes`; `code` is the 1817 code for that limit.
    | CardPayloadExceeded of limits: string * code: string * limit: int * measured: int

// ─── Options ────────────────────────────────────────────────────────────────

/// Host-supplied knobs.
type CardOptions =
    {
        /// The live surface an open-live affordance points at (`<LiveUrl>#<node
        /// id>`). `None` writes a labelled NOTE instead of a link: a dangling
        /// URL is a broken promise, a note is honest about what is not here.
        LiveUrl: string option
        /// The declared budget the tree is refused against before projection,
        /// and the projected card's byte ceiling.
        Limits: HostLimits
    }

/// No live surface, and the card host's declared limits.
let defaults: CardOptions =
    { LiveUrl = None
      Limits = HostLimits.card }

// ─── JSON builders ──────────────────────────────────────────────────────────

let private jstr (s: string) : JsonNode = JsonValue.Create(s)
let private jbool (b: bool) : JsonNode = JsonValue.Create(b)
let private jnum (f: float) : JsonNode = JsonValue.Create(f)
let private jint (i: int) : JsonNode = JsonValue.Create(i)

/// A JSON node can belong to one parent only, and the style fragments below are
/// shared values, so a node that is already placed is copied rather than moved.
let private placeable (v: JsonNode) : JsonNode =
    if isNull v || isNull v.Parent then v else v.DeepClone()

let private jobj (props: (string * JsonNode) list) : JsonObject =
    let o = JsonObject()

    for k, v in props do
        o[k] <- placeable v

    o

let private jarr (items: JsonNode list) : JsonNode =
    let a = JsonArray()

    for i in items do
        a.Add(placeable i)

    a

/// A literal `TextRun`. Hosts never read a `RichTextBlock`'s runs as markdown.
let private run (text: string) (style: (string * JsonNode) list) : JsonObject =
    jobj ([ "type", jstr "TextRun"; "text", jstr text ] @ style)

let private richText (runs: JsonObject list) : JsonObject =
    jobj
        [ "type", jstr "RichTextBlock"
          "inlines", jarr (runs |> List.map (fun r -> r :> JsonNode)) ]

let private container (style: string option) (items: JsonObject list) : JsonObject =
    jobj (
        [ "type", jstr "Container"
          "items", jarr (items |> List.map (fun i -> i :> JsonNode)) ]
        @ (style |> Option.map (fun s -> "style", jstr s) |> Option.toList)
    )

let private column (width: JsonNode) (items: JsonObject list) : JsonObject =
    jobj
        [ "type", jstr "Column"
          "width", width
          "items", jarr (items |> List.map (fun i -> i :> JsonNode)) ]

let private columnSet (columns: JsonObject list) : JsonObject =
    jobj
        [ "type", jstr "ColumnSet"
          "columns", jarr (columns |> List.map (fun c -> c :> JsonNode)) ]

let private actionSet (action: JsonObject) : JsonObject =
    jobj [ "type", jstr "ActionSet"; "actions", jarr [ action ] ]

let private openUrl (title: string) (url: string) : JsonObject =
    jobj [ "type", jstr "Action.OpenUrl"; "title", jstr title; "url", jstr url ]

let private bold () = [ "weight", jstr "bolder" ]
let private subtle () = [ "isSubtle", jbool true ]

/// A tone's text colour, from the 1.4 `Colors` enum.
let private toneColour (tone: ToneVariant) : (string * JsonNode) list =
    match tone with
    | ToneVariant.Success -> [ "color", jstr "good" ]
    | ToneVariant.Warning -> [ "color", jstr "warning" ]
    | ToneVariant.Critical -> [ "color", jstr "attention" ]
    | ToneVariant.Info
    | ToneVariant.Brand -> [ "color", jstr "accent" ]
    | ToneVariant.Subdued -> subtle ()
    | ToneVariant.Default -> []

/// A tone's container style, from the 1.4 `ContainerStyle` enum.
let private toneStyle (tone: ToneVariant) : string =
    match tone with
    | ToneVariant.Success -> "good"
    | ToneVariant.Warning -> "warning"
    | ToneVariant.Critical -> "attention"
    | ToneVariant.Info
    | ToneVariant.Brand -> "accent"
    | ToneVariant.Subdued
    | ToneVariant.Default -> "emphasis"

let private badgeColour (variant: BadgeVariant) : (string * JsonNode) list =
    match variant with
    | BadgeVariant.Success -> [ "color", jstr "good" ]
    | BadgeVariant.Warning -> [ "color", jstr "warning" ]
    | BadgeVariant.Critical -> [ "color", jstr "attention" ]
    | BadgeVariant.Info
    | BadgeVariant.Brand -> [ "color", jstr "accent" ]
    | BadgeVariant.Neutral -> []

// ─── Text safety ────────────────────────────────────────────────────────────

/// Collapse every whitespace run (line breaks included) to one space and trim.
let private normalise (s: string) : string =
    if isNull s then
        ""
    else
        let sb = Text.StringBuilder(s.Length)
        let mutable pendingSpace = false

        for c in s do
            if Char.IsWhiteSpace c then
                pendingSpace <- sb.Length > 0
            else
                if pendingSpace then
                    sb.Append ' ' |> ignore
                    pendingSpace <- false

                sb.Append c |> ignore

        sb.ToString()

/// A `Fact`'s title and value are the one place a host reads card text as
/// markdown, so a bound string there is escaped: every character that can open
/// a link, an autolink or emphasis is backslash-escaped, and a line-start block
/// marker cannot form because the text is one normalised line.
let escapeFactText (s: string) : string =
    let line = normalise s
    let sb = Text.StringBuilder(line.Length)

    for c in line do
        match c with
        | '\\'
        | '['
        | ']'
        | '('
        | ')'
        | '<'
        | '>'
        | '*'
        | '_'
        | '~'
        | '`'
        | '#' -> sb.Append('\\').Append(c) |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

// ─── Destinations ───────────────────────────────────────────────────────────

/// A tree-authored URL, through the context's destination policy under `cls`.
/// A card host resolves nothing relative, so an allowed same-origin URL is
/// still not written.
let private cardUrl
    (ctx: Render.ServerRenderContext)
    (cls: Sanitize.EgressClass)
    (slot: string)
    (raw: string)
    : Result<string, DegradationRule> =
    match Sanitize.checkDestination ctx.Policy.Egress cls raw with
    | Sanitize.EgressVerdict.Allowed url ->
        match Sanitize.classifyDestination url with
        | Sanitize.Destination.Remote _
        | Sanitize.Destination.NonNetwork _ -> Ok url
        | Sanitize.Destination.Local
        | Sanitize.Destination.Rejected -> Error(DegradationRule.RelativeDestination slot)
    | refused -> Error(DegradationRule.DestinationRefused(slot, Sanitize.describeEgressVerdict refused))

/// The MIME type a media source's extension names, for the closed set the
/// card's `Media` element is documented against. Anything else is unknown.
let private mediaMimeType (url: string) : string option =
    let path =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, u -> u.AbsolutePath
        | _ -> url

    match IO.Path.GetExtension(path).ToLowerInvariant() with
    | ".mp4" -> Some "video/mp4"
    | ".webm" -> Some "video/webm"
    | ".ogv" -> Some "video/ogg"
    | ".mp3" -> Some "audio/mpeg"
    | ".m4a" -> Some "audio/mp4"
    | ".wav" -> Some "audio/wav"
    | ".oga"
    | ".ogg" -> Some "audio/ogg"
    | _ -> None

// ─── Actions ────────────────────────────────────────────────────────────────

/// What a tree action becomes on a card.
[<RequireQualifiedAccess>]
type private CardAction =
    | OpenUrl of url: string
    | Execute of verb: string * action: string
    | NotProjectable of DegradationRule

// `Action.Dispatch` is marked in-process-only by the IDL annotation, which
// renders as `[<Obsolete(…, false)>]`: FS0044 at every mention. The match below
// is a TOTAL classification of the union, and it names that case precisely to
// rule it client-only. Scoped off for this one declaration; `#warnon` follows.
#nowarn "44"

/// Classify an action. `Navigate` to an allowed absolute URL opens it; an
/// action a backend can run becomes `Action.Execute` with a declared verb (the
/// capability id for `Invoke`, `fuaran.<case>` otherwise); an action only the
/// live runtime can perform — the clipboard, print, focus, a file read, a
/// confirmation dialog, an in-process message — is not projectable.
let rec private classifyAction (ctx: Render.ServerRenderContext) (action: Action<obj>) : CardAction =
    match action with
    | Action.Navigate(route, _) ->
        match cardUrl ctx Sanitize.EgressClass.Hyperlink "navigate" (Render.renderText ctx route) with
        | Ok url -> CardAction.OpenUrl url
        | Error rule -> CardAction.NotProjectable rule
    | Action.Invoke(capabilityId, _) -> CardAction.Execute(capabilityId, "Invoke")
    | Action.Call _ -> CardAction.Execute("fuaran.call", "Call")
    | Action.SetState _ -> CardAction.Execute("fuaran.setState", "SetState")
    | Action.CommitLocal _ -> CardAction.Execute("fuaran.commitLocal", "CommitLocal")
    | Action.Notify _ -> CardAction.Execute("fuaran.notify", "Notify")
    | Action.AiTool _ -> CardAction.Execute("fuaran.aiTool", "AiTool")
    | Action.Chain ops ->
        let parts = ops |> List.map (classifyAction ctx)

        match
            parts
            |> List.tryPick (function
                | CardAction.NotProjectable r -> Some r
                | _ -> None)
        with
        | Some rule -> CardAction.NotProjectable rule
        | None ->
            if
                parts
                |> List.forall (function
                    | CardAction.Execute _ -> true
                    | _ -> false)
            then
                CardAction.Execute("fuaran.chain", "Chain")
            else
                CardAction.NotProjectable(DegradationRule.ClientOnlyAction "Chain")
    | Action.WriteToClipboard _ -> CardAction.NotProjectable(DegradationRule.ClientOnlyAction "WriteToClipboard")
    | Action.Print -> CardAction.NotProjectable(DegradationRule.ClientOnlyAction "Print")
    | Action.Focus _ -> CardAction.NotProjectable(DegradationRule.ClientOnlyAction "Focus")
    | Action.ReadFileBody _ -> CardAction.NotProjectable(DegradationRule.ClientOnlyAction "ReadFileBody")
    | Action.Confirm _ -> CardAction.NotProjectable(DegradationRule.ClientOnlyAction "Confirm")
    | Action.Dispatch _ -> CardAction.NotProjectable(DegradationRule.ClientOnlyAction "Dispatch")

#warnon "44"

/// The `Action.Execute` a backend action becomes. `data` carries the node id,
/// so the backend that owns the tree knows which node fired.
let private execute (title: string) (verb: string) (nodeId: string) (associatedInputs: string) : JsonObject =
    jobj
        [ "type", jstr "Action.Execute"
          "title", jstr title
          "verb", jstr verb
          "data", jobj [ "fuaranNodeId", jstr nodeId ]
          "associatedInputs", jstr associatedInputs ]

// ─── Markdown ───────────────────────────────────────────────────────────────

/// The value of `name="…"` inside one HTML tag, decoded.
let private attrOf (tag: string) (name: string) : string option =
    let needle = " " + name + "=\""
    let i = tag.IndexOf(needle, StringComparison.Ordinal)

    if i < 0 then
        None
    else
        let start = i + needle.Length
        let close = tag.IndexOf('"', start)

        if close < 0 then
            None
        else
            Some(Net.WebUtility.HtmlDecode(tag.Substring(start, close - start)))

/// Markdown as RichTextBlocks: the one deterministic GFM render (which escapes
/// raw HTML by construction, so every `<` in its output opens a tag the
/// renderer wrote, and which has already put every link and image destination
/// through the policy), walked tag by tag into styled `TextRun`s, one block per
/// paragraph, heading, list item, table row or code block.
let private markdownBlocks (ctx: Render.ServerRenderContext) (source: string) : JsonObject list * DegradationRule list =
    let html = Markdown.toHtmlWithEgress ctx.Policy.Egress source
    let blocks = ResizeArray<JsonObject>()
    let runs = ResizeArray<JsonObject>()
    let notes = ResizeArray<DegradationRule>()
    let mutable strong = 0
    let mutable em = 0
    let mutable strike = 0
    let mutable code = 0
    let mutable pre = 0
    let mutable heading = 0
    let mutable link: string option = None
    let lists = Collections.Generic.Stack<int ref option>()

    let flush () =
        if runs.Count > 0 then
            blocks.Add(richText (List.ofSeq runs))
            runs.Clear()

    let emit (raw: string) =
        let text = if pre > 0 then raw else raw.Replace('\n', ' ')

        if text <> "" && not (runs.Count = 0 && String.IsNullOrWhiteSpace text) then
            let style =
                [ if strong > 0 || heading > 0 then
                      yield! bold ()
                  if em > 0 then
                      "italic", jbool true
                  if strike > 0 then
                      "strikethrough", jbool true
                  if code > 0 || pre > 0 then
                      "fontType", jstr "monospace"
                  match heading with
                  | 1 -> "size", jstr "extraLarge"
                  | 2 -> "size", jstr "large"
                  | 3 -> "size", jstr "medium"
                  | _ -> ()
                  match link with
                  | Some url -> "selectAction", jobj [ "type", jstr "Action.OpenUrl"; "url", jstr url ]
                  | None -> () ]

            runs.Add(run text style)

    let mutable i = 0

    while i < html.Length do
        if html.[i] = '<' then
            let close = html.IndexOf('>', i)

            if close < 0 then
                i <- html.Length
            else
                let tag = html.Substring(i, close - i + 1)

                let name =
                    let body = tag.TrimStart('<', '/')
                    let stop = body.IndexOfAny([| ' '; '>'; '/' |])
                    (if stop < 0 then body else body.Substring(0, stop)).ToLowerInvariant()

                let closing = tag.StartsWith "</"

                match name, closing with
                | ("p" | "blockquote" | "tr" | "br" | "hr" | "table" | "ul" | "ol"), _ -> flush ()
                | ("h1" | "h2" | "h3" | "h4" | "h5" | "h6"), false ->
                    flush ()
                    heading <- int name.[1] - int '0'
                | ("h1" | "h2" | "h3" | "h4" | "h5" | "h6"), true ->
                    flush ()
                    heading <- 0
                | "li", false ->
                    flush ()

                    match (if lists.Count > 0 then lists.Peek() else None) with
                    | Some n ->
                        n.Value <- n.Value + 1
                        emit (string n.Value + ". ")
                    | None -> emit "• "
                | "li", true -> flush ()
                | "pre", false ->
                    flush ()
                    pre <- pre + 1
                | "pre", true ->
                    flush ()
                    pre <- max 0 (pre - 1)
                | ("strong" | "b"), false -> strong <- strong + 1
                | ("strong" | "b"), true -> strong <- max 0 (strong - 1)
                | ("em" | "i"), false -> em <- em + 1
                | ("em" | "i"), true -> em <- max 0 (em - 1)
                | ("del" | "s"), false -> strike <- strike + 1
                | ("del" | "s"), true -> strike <- max 0 (strike - 1)
                | "code", false -> code <- code + 1
                | "code", true -> code <- max 0 (code - 1)
                | ("td" | "th"), false ->
                    if runs.Count > 0 then
                        emit " | "
                | "a", false ->
                    match attrOf tag "href" with
                    | Some href when tag.Contains Sanitize.egressRefusalAttribute || href.StartsWith "about:blank" ->
                        notes.Add(
                            DegradationRule.DestinationRefused("markdown link", "refused by the destination policy")
                        )

                        link <- None
                    | Some href ->
                        match Sanitize.classifyDestination href with
                        | Sanitize.Destination.Remote _
                        | Sanitize.Destination.NonNetwork _ -> link <- Some href
                        | _ ->
                            notes.Add(DegradationRule.RelativeDestination "markdown link")
                            link <- None
                    | None -> link <- None
                | "a", true -> link <- None
                | "img", _ ->
                    notes.Add DegradationRule.MarkdownImageAsText

                    match attrOf tag "alt" with
                    | Some alt when alt <> "" -> emit ("[" + alt + "]")
                    | _ -> ()
                | "input", _ -> emit (if tag.Contains "checked" then "☑ " else "☐ ")
                | _ -> ()

                // A list opens AFTER its flush above, and closes after it too.
                match name, closing with
                | "ul", false -> lists.Push None
                | "ol", false -> lists.Push(Some(ref 0))
                | ("ul" | "ol"), true when lists.Count > 0 -> lists.Pop() |> ignore
                | _ -> ()

                i <- close + 1
        else
            let next = html.IndexOf('<', i)
            let stop = if next < 0 then html.Length else next
            emit (Net.WebUtility.HtmlDecode(html.Substring(i, stop - i)))
            i <- stop

    flush ()
    List.ofSeq blocks, List.ofSeq notes |> List.distinct

// ─── The projection ─────────────────────────────────────────────────────────

/// What a node projects to, before adjacent fact rows are merged.
[<RequireQualifiedAccess>]
type private Piece =
    | Element of JsonObject
    | FactRow of title: string * value: string

/// Assemble pieces into card elements: adjacent fact rows share one `FactSet`.
let private assemble (pieces: Piece list) : JsonObject list =
    let out = ResizeArray<JsonObject>()
    let facts = ResizeArray<JsonNode>()

    let flushFacts () =
        if facts.Count > 0 then
            out.Add(jobj [ "type", jstr "FactSet"; "facts", jarr (List.ofSeq facts) ])
            facts.Clear()

    for p in pieces do
        match p with
        | Piece.FactRow(title, value) ->
            facts.Add(jobj [ "type", jstr "Fact"; "title", jstr title; "value", jstr value ])
        | Piece.Element e ->
            flushFacts ()
            out.Add e

    flushFacts ()
    List.ofSeq out

/// The textual value of a resolved field value.
let private jvalText (v: Fuaran.Core.JVal) : string =
    match v with
    | Fuaran.Core.JStr s -> s
    | Fuaran.Core.JBool b -> if b then "true" else "false"
    | Fuaran.Core.JInt i -> string i
    | Fuaran.Core.JFloat f -> f.ToString("R", Globalization.CultureInfo.InvariantCulture)
    | Fuaran.Core.JArr items ->
        items
        |> List.choose (function
            | Fuaran.Core.JStr s -> Some s
            | Fuaran.Core.JInt i -> Some(string i)
            | _ -> None)
        |> String.concat ","
    | Fuaran.Core.JObj _ -> ""

/// Project with explicit options over an explicit render context.
///
/// `ctx` is the server render context every server projection uses
/// (`Render.mkContext` and its variants): bindings, fragments, locale, the
/// fixed `now`, and the destination policy every URL is checked against all
/// come from it. A host that declares egress builds it with
/// `Render.mkContextWithEgress`; the default context denies every non-local
/// destination, so a decoded tree's images and links degrade until the host
/// declares where the card may point.
let projectWith
    (options: CardOptions)
    (ctx: Render.ServerRenderContext)
    (root: Node<obj>)
    : Result<JsonObject * Degradation list, ProjectionRefusal> =
    let limitCodes =
        HostLimits.kinds |> List.map PreEmitValidate.hostLimitCode |> Set.ofList

    let overLimits =
        match PreEmitValidate.validateWithLimits options.Limits root with
        | Ok() -> []
        | Error defects ->
            defects
            |> List.map PreEmitValidate.describe
            |> List.filter (fun (code, _, _) -> limitCodes.Contains code)
            |> List.map (fun (code, _, message) -> code, message)

    if not overLimits.IsEmpty then
        Error(ProjectionRefusal.HostLimitsExceeded(options.Limits.Identity, overLimits))
    else
        let degraded = ResizeArray<Degradation>()
        let text = Render.renderText ctx
        let kindOf (n: Node<obj>) = wireNameOf n.Kind

        let degrade (rule: DegradationRule) (decidedAt: string) (n: Node<obj>) =
            degraded.Add
                { NodeId = n.Id
                  Kind = kindOf n
                  Rule = rule
                  DecidedAt = decidedAt }

        let rec degradeSubtree (rule: DegradationRule) (decidedAt: string) (n: Node<obj>) =
            degrade rule decidedAt n

            for c in StructuralQuery.children n do
                degradeSubtree rule decidedAt c

        let accessibleLabel (n: Node<obj>) : string =
            n.Accessibility
            |> Option.bind _.Label
            |> Option.bind (BindingResolver.tryResolveScalarText ctx.Sources)
            |> Option.map normalise
            |> Option.defaultValue ""

        let resolvedTrue (b: Binding<bool> option) : bool =
            b
            |> Option.bind (BindingResolver.tryResolve ctx.Sources)
            |> Option.defaultValue false

        let figure (format: CellFormat) (binding: Binding<float>) : string =
            match BindingResolver.resolveScalarFloat ctx.Sources binding with
            | BindingResolver.Resolved v -> Render.formatNumber ctx format v
            | BindingResolver.NotResolved -> "—"
            | BindingResolver.Errored _
            | BindingResolver.I18nUnresolved _ -> "unavailable"

        let line (s: string) (style: (string * JsonNode) list) : JsonObject = richText [ run (normalise s) style ]

        /// The open-live affordance: a link when the host declared a live
        /// surface, else a labelled note. The node's accessible label wins.
        let openLiveElement (n: Node<obj>) (fallback: string) : JsonObject =
            let label =
                match accessibleLabel n with
                | "" ->
                    match normalise fallback with
                    | "" -> kindOf n
                    | f -> f
                | l -> l

            let href =
                options.LiveUrl
                |> Option.bind (fun baseUrl -> Sanitize.sanitizeUrl (baseUrl + "#" + n.Id))

            match href with
            | Some url -> actionSet (openUrl (label + " — open live") url)
            | None -> line (label + " — available in the live view") (subtle ())

        /// An open-live node: the affordance, and every structural descendant
        /// reported as inside it.
        let openLive (n: Node<obj>) (rule: DegradationRule) (fallback: string) : Piece list =
            degrade rule n.Id n

            for c in StructuralQuery.children n do
                degradeSubtree DegradationRule.InsideOpenLive n.Id c

            [ Piece.Element(openLiveElement n fallback) ]

        let flattened (n: Node<obj>) (elements: JsonObject list) : Piece list =
            degrade DegradationRule.FlattenedToText n.Id n
            elements |> List.map Piece.Element

        /// One form field as card inputs. Ids are `<form id>.<field id>`, so two
        /// forms on one card cannot collide.
        let fieldInputs (form: Node<obj>) (field: FormField<obj>) : JsonObject list =
            let id = form.Id + "." + field.Id
            let label = normalise (text field.Label)

            let substituted (into: string) =
                degrade (DegradationRule.InputSubstituted(field.Id, into)) form.Id form

            let initial (value: Binding<Fuaran.Core.JVal> option) : (string * JsonNode) list =
                match value |> Option.map (BindingResolver.resolveJVal ctx.Sources) with
                | Some(BindingResolver.Resolved v) ->
                    match jvalText v with
                    | "" -> []
                    | s -> [ "value", jstr s ]
                | _ -> []

            /// `Input.Number`'s value is a JSON number, never a string; a value
            /// that is not a finite number is left out rather than coerced.
            let initialNumber (value: Binding<Fuaran.Core.JVal> option) : (string * JsonNode) list =
                let parsed =
                    match value |> Option.map (BindingResolver.resolveJVal ctx.Sources) with
                    | Some(BindingResolver.Resolved(Fuaran.Core.JInt i)) -> Some(float i)
                    | Some(BindingResolver.Resolved(Fuaran.Core.JFloat f)) -> Some f
                    | Some(BindingResolver.Resolved(Fuaran.Core.JStr s)) ->
                        match
                            Double.TryParse(
                                s,
                                Globalization.NumberStyles.Float,
                                Globalization.CultureInfo.InvariantCulture
                            )
                        with
                        | true, f -> Some f
                        | _ -> None
                    | _ -> None

                match parsed with
                | Some f when Double.IsFinite f -> [ "value", jnum f ]
                | _ -> []

            let common =
                [ "id", jstr id; "label", jstr label ]
                @ (if field.Required then [ "isRequired", jbool true ] else [])

            let input (kind: string) (props: (string * JsonNode) list) : JsonObject =
                jobj ([ "type", jstr kind ] @ common @ props)

            let suffixed (suffix: string) (kind: string) (props: (string * JsonNode) list) : JsonObject =
                jobj (
                    [ "type", jstr kind
                      "id", jstr (id + "." + suffix)
                      "label", jstr (label + " (" + suffix + ")") ]
                    @ props
                )

            let timeProps =
                [ "regex", jstr "^([01][0-9]|2[0-3]):[0-5][0-9]$"; "placeholder", jstr "HH:MM" ]

            let choices (options: SelectOption list) : JsonNode =
                jarr [ for o in options -> jobj [ "title", jstr (normalise o.Label); "value", jstr o.Value ] ]

            let bounds (lo: float option) (hi: float option) =
                (lo |> Option.map (fun v -> "min", jnum v) |> Option.toList)
                @ (hi |> Option.map (fun v -> "max", jnum v) |> Option.toList)

            let dateBounds (lo: string option) (hi: string option) =
                (lo |> Option.map (fun v -> "min", jstr v) |> Option.toList)
                @ (hi |> Option.map (fun v -> "max", jstr v) |> Option.toList)

            let temporal (variant: DateTimeVariant) (lo: string option) (hi: string option) (prefix: string) =
                let named (part: string) =
                    if prefix = "" then part else prefix + "-" + part

                match variant with
                | DateTimeVariant.Date ->
                    if prefix = "" then
                        [ input "Input.Date" (dateBounds lo hi) ]
                    else
                        [ suffixed prefix "Input.Date" (dateBounds lo hi) ]
                | DateTimeVariant.Time ->
                    if prefix = "" then
                        [ input "Input.Text" timeProps ]
                    else
                        [ suffixed prefix "Input.Text" timeProps ]
                | DateTimeVariant.DateTime ->
                    [ suffixed (named "date") "Input.Date" []
                      suffixed (named "time") "Input.Text" timeProps ]

            let inputs =
                match field.Kind with
                | FormFieldKind.Text(value, _) -> [ input "Input.Text" (initial value) ]
                | FormFieldKind.TextArea(value, _, _) ->
                    [ input "Input.Text" ([ "isMultiline", jbool true ] @ initial value) ]
                | FormFieldKind.Number(value, _) -> [ input "Input.Number" (initialNumber value) ]
                | FormFieldKind.RangedNumber(value, _, lo, hi, _) ->
                    [ input "Input.Number" (bounds lo hi @ initialNumber value) ]
                | FormFieldKind.Range(value, _, lo, hi, _) ->
                    substituted "Input.Number"
                    [ input "Input.Number" (bounds lo hi @ initialNumber value) ]
                | FormFieldKind.Checkbox(value, _)
                | FormFieldKind.Toggle(value, _) ->
                    let on =
                        match value |> Option.map (BindingResolver.resolveJVal ctx.Sources) with
                        | Some(BindingResolver.Resolved(Fuaran.Core.JBool true)) -> "true"
                        | _ -> "false"

                    [ jobj (
                          [ "type", jstr "Input.Toggle"
                            "id", jstr id
                            "title", jstr label
                            "value", jstr on
                            "valueOn", jstr "true"
                            "valueOff", jstr "false" ]
                          @ (if field.Required then [ "isRequired", jbool true ] else [])
                      ) ]
                | FormFieldKind.Choice(options, value, _) ->
                    [ input
                          "Input.ChoiceSet"
                          ([ "choices", choices (BindingResolver.resolveOptions ctx.Sources options)
                             "style", jstr "compact" ]
                           @ initial value) ]
                | FormFieldKind.SegmentedChoice(options, value, _, _) ->
                    [ input
                          "Input.ChoiceSet"
                          ([ "choices", choices (BindingResolver.resolveOptions ctx.Sources options)
                             "style", jstr "expanded" ]
                           @ initial value) ]
                | FormFieldKind.Combobox(allowFreeText, _, options, value) ->
                    if allowFreeText then
                        substituted "Input.ChoiceSet (no free text)"

                    [ input
                          "Input.ChoiceSet"
                          ([ "choices", choices (BindingResolver.resolveOptions ctx.Sources options)
                             "style", jstr "compact" ]
                           @ initial value) ]
                | FormFieldKind.Tokens(_, _, Some suggestions, value) ->
                    substituted "Input.ChoiceSet (multi-select)"

                    [ input
                          "Input.ChoiceSet"
                          ([ "choices", choices (BindingResolver.resolveOptions ctx.Sources suggestions)
                             "isMultiSelect", jbool true ]
                           @ initial value) ]
                | FormFieldKind.Tokens(_, _, None, value) ->
                    substituted "Input.Text (comma-separated)"
                    [ input "Input.Text" (initial value) ]
                | FormFieldKind.Rating(allowHalf, max, _, value) ->
                    substituted "Input.ChoiceSet (expanded scale)"
                    let step = if allowHalf then 0.5 else 1.0

                    let scale =
                        [ for k in 1 .. int (float max / step) ->
                              let v = (float k * step).ToString("0.#", Globalization.CultureInfo.InvariantCulture)
                              jobj [ "title", jstr v; "value", jstr v ] :> JsonNode ]

                    [ input "Input.ChoiceSet" ([ "choices", jarr scale; "style", jstr "expanded" ] @ initial value) ]
                | FormFieldKind.Color(_, value) ->
                    substituted "Input.Text (#rrggbb)"

                    [ input
                          "Input.Text"
                          ([ "regex", jstr "^#[0-9a-fA-F]{6}$"; "placeholder", jstr "#rrggbb" ]
                           @ initial value) ]
                | FormFieldKind.DateTime(value, _, variant, lo, hi, _) ->
                    let projected = temporal variant lo hi ""

                    if variant = DateTimeVariant.Time then
                        substituted "Input.Text (HH:MM)"
                    elif variant = DateTimeVariant.DateTime then
                        substituted "Input.Date + Input.Text (HH:MM)"

                    // A single input carries the resolved value; a date + time
                    // pair has no single value to split honestly, so it starts empty.
                    match projected with
                    | [ single ] ->
                        for k, v in initial value do
                            single[k] <- v

                        [ single ]
                    | inputs -> inputs
                | FormFieldKind.DateTimeRange(_, _, variant, lo, hi, _) ->
                    substituted "two inputs (start, end)"
                    temporal variant lo hi "start" @ temporal variant lo hi "end"

            if field.Rule.IsSome then
                degrade (DegradationRule.FieldRuleNotCarried field.Id) form.Id form

            let help =
                field.Help |> Option.map (fun h -> line (text h) (subtle ())) |> Option.toList

            inputs @ help

        /// Walk one node to its pieces.
        let rec walk (depth: int) (n: Node<obj>) : Piece list =
            if depth > WireLimits.MaxDepth then
                degradeSubtree DegradationRule.DepthExceeded n.Id n
                []
            elif not (BindingResolver.isNodeVisible ctx.Sources n) then
                degradeSubtree DegradationRule.NotVisible n.Id n
                []
            else
                walkVisible depth n

        and walkChildren (depth: int) (kids: Node<obj> list) : JsonObject list =
            kids |> List.collect (walk (depth + 1)) |> assemble

        and walkVisible (depth: int) (n: Node<obj>) : Piece list =
            let structural (style: string option) (headingText: string option) (kids: Node<obj> list) =
                let head =
                    headingText
                    |> Option.map normalise
                    |> Option.filter (fun h -> h <> "")
                    |> Option.map (fun h -> line h (bold ()))
                    |> Option.toList

                match head @ walkChildren depth kids with
                | [] -> []
                | items -> [ Piece.Element(container style items) ]

            let rowsOf (perRow: int) (kids: Node<obj> list) =
                let cells = kids |> List.map (fun k -> walk (depth + 1) k |> assemble)

                cells
                |> List.chunkBySize (max 1 perRow)
                |> List.map (fun row -> columnSet (row |> List.map (column (jstr "stretch"))))

            match n.Kind with

            // ── Structure ────────────────────────────────────────────────────
            | NodeKind.Box spec ->
                let style =
                    match spec.Role with
                    | BoxRole.Card -> Some "emphasis"
                    | _ -> None

                let head =
                    match spec.Role, spec.Heading with
                    | BoxRole.Card, Some h -> [ line (text h) (bold ()) ]
                    | _ -> []

                match spec.Role, spec.Layout with
                | BoxRole.Separator, _ ->
                    [ Piece.Element(jobj [ "type", jstr "Container"; "items", jarr []; "separator", jbool true ]) ]
                | _, BoxLayout.Flex(Orientation.Horizontal, _, _) ->
                    let cols =
                        spec.Children
                        |> List.map (fun k -> walk (depth + 1) k |> assemble)
                        |> List.filter (fun items -> not items.IsEmpty)
                        |> List.map (column (jstr "stretch"))

                    match head, cols with
                    | [], [] -> []
                    | [], cols -> [ Piece.Element(columnSet cols) ]
                    | head, [] -> [ Piece.Element(container style head) ]
                    | head, cols -> [ Piece.Element(container style (head @ [ columnSet cols ])) ]
                | _, BoxLayout.Grid(cols, _, _) -> [ Piece.Element(container style (head @ rowsOf cols spec.Children)) ]
                | _, BoxLayout.Masonry(cols, _) ->
                    degrade (DegradationRule.LayoutApproximated "masonry as a grid of rows") n.Id n
                    [ Piece.Element(container style (head @ rowsOf cols spec.Children)) ]
                | _, BoxLayout.Flex _
                | _, BoxLayout.Auto ->
                    match head @ walkChildren depth spec.Children with
                    | [] -> []
                    | items -> [ Piece.Element(container style items) ]
            | NodeKind.SplitPanel spec ->
                let weight = max 0.0 (min 1.0 spec.Weight)
                let left = int (Math.Round(weight * 100.0))

                let leftKids, rightKids =
                    match spec.Children with
                    | [] -> [], []
                    | a :: rest -> [ a ], rest

                [ Piece.Element(
                      columnSet
                          [ column (jint (max 1 left)) (walkChildren depth leftKids)
                            column (jint (max 1 (100 - left))) (walkChildren depth rightKids) ]
                  ) ]
            | NodeKind.ScrollArea spec -> structural None None spec.Children
            | NodeKind.SummaryList spec -> structural None (spec.Heading |> Option.map text) spec.Children
            | NodeKind.Disclosure spec ->
                let isOpen =
                    BindingResolver.tryResolve ctx.Sources spec.Open
                    |> Option.defaultValue spec.DefaultOpen

                if not isOpen then
                    degrade DegradationRule.AlwaysExpanded n.Id n

                structural None (Some(text spec.Heading)) spec.Children
            | NodeKind.ErrorBoundary spec ->
                degradeSubtree DegradationRule.NotTaken n.Id spec.Fallback
                walk (depth + 1) spec.Child
            | NodeKind.Switch spec ->
                let chosen = Email.selectedSwitchBranch ctx spec

                for branch in (spec.Cases |> List.map _.Child) @ [ spec.Default ] do
                    if not (LanguagePrimitives.PhysicalEquality branch chosen) then
                        degradeSubtree DegradationRule.NotTaken n.Id branch

                walk (depth + 1) chosen
            | NodeKind.FragmentRef spec ->
                match Map.tryFind spec.Name ctx.Fragments with
                | Some body -> walk (depth + 1) body
                | None ->
                    degrade (DegradationRule.UnresolvedFragment spec.Name) n.Id n
                    [ Piece.Element(line ("[fragment unresolved: " + spec.Name + "]") (subtle ())) ]

            // ── Mapped display ───────────────────────────────────────────────
            | NodeKind.Heading spec ->
                let size =
                    match spec.Level with
                    | 1 -> [ "size", jstr "extraLarge" ]
                    | 2 -> [ "size", jstr "large" ]
                    | 3 -> [ "size", jstr "medium" ]
                    | _ -> []

                [ Piece.Element(line (text spec.Text) (bold () @ size)) ]
            | NodeKind.Markdown spec ->
                let blocks, notes = markdownBlocks ctx (text spec.Text)

                for rule in notes do
                    degrade rule n.Id n

                blocks |> List.map Piece.Element
            | NodeKind.Fact spec ->
                [ Piece.FactRow(escapeFactText (text spec.Label), escapeFactText (text spec.Value)) ]
                @ (spec.Help
                   |> Option.map (fun h -> Piece.Element(line (text h) (subtle ())))
                   |> Option.toList)
            | NodeKind.LabelValueRow spec ->
                [ Piece.FactRow(escapeFactText (text spec.Label), escapeFactText (figure spec.Format spec.Value)) ]
                @ (spec.Help
                   |> Option.map (fun h -> Piece.Element(line (text h) (subtle ())))
                   |> Option.toList)
            | NodeKind.Callout spec ->
                let head =
                    spec.Heading |> Option.map (fun h -> line (text h) (bold ())) |> Option.toList

                [ Piece.Element(container (Some(toneStyle spec.Tone)) (head @ [ line (text spec.Body) [] ])) ]
            | NodeKind.Metric spec ->
                let trend =
                    spec.Trend
                    |> Option.bind (BindingResolver.tryResolveScalarFloat ctx.Sources)
                    |> Option.map (fun t ->
                        line
                            (Render.formatNumber ctx (spec.TrendFormat |> Option.defaultValue CellFormat.None) t)
                            (toneColour spec.Tone))
                    |> Option.toList

                let comparisons =
                    spec.Comparisons
                    |> List.choose (fun c ->
                        BindingResolver.tryResolveScalarFloat ctx.Sources c.Value
                        |> Option.map (fun v ->
                            line (Render.formatNumber ctx c.Format v + " " + text c.Label) (toneColour spec.Tone)))

                let subtext =
                    spec.Subtext |> Option.map (fun s -> line (text s) (subtle ())) |> Option.toList

                [ Piece.Element(
                      container
                          None
                          ([ line (text spec.Label) (subtle ())
                             line
                                 (figure spec.Format spec.Value)
                                 (bold () @ [ "size", jstr "extraLarge" ] @ toneColour spec.Tone) ]
                           @ trend
                           @ comparisons
                           @ subtext)
                  ) ]
            | NodeKind.Link spec ->
                let label =
                    match normalise (text spec.Label) with
                    | "" -> kindOf n
                    | l -> l

                let raw = BindingResolver.tryResolve ctx.Sources spec.Href |> Option.defaultValue ""

                match cardUrl ctx Sanitize.EgressClass.Hyperlink "href" raw with
                | Ok url ->
                    [ Piece.Element(
                          richText
                              [ run label [ "selectAction", jobj [ "type", jstr "Action.OpenUrl"; "url", jstr url ] ] ]
                      ) ]
                | Error rule ->
                    degrade rule n.Id n
                    [ Piece.Element(line label []) ]
            | NodeKind.Image spec ->
                let alt = normalise (text spec.Alt)
                let raw = BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

                let caption =
                    spec.Caption
                    |> Option.map (fun c -> Piece.Element(line (text c) (subtle ())))
                    |> Option.toList

                match cardUrl ctx Sanitize.EgressClass.Media "src" raw with
                | Ok url ->
                    [ Piece.Element(jobj [ "type", jstr "Image"; "url", jstr url; "altText", jstr alt ]) ]
                    @ caption
                | Error rule ->
                    degrade rule n.Id n

                    (if alt = "" then
                         []
                     else
                         [ Piece.Element(line ("[" + alt + "]") (subtle ())) ])
                    @ caption
            | NodeKind.Media spec ->
                let label = normalise (text spec.Label)
                let raw = BindingResolver.tryResolve ctx.Sources spec.Src |> Option.defaultValue ""

                match cardUrl ctx Sanitize.EgressClass.Media "src" raw with
                | Error rule -> openLive n rule label
                | Ok url ->
                    match mediaMimeType url with
                    | None -> openLive n DegradationRule.MediaTypeUnknown label
                    | Some mime ->
                        let poster =
                            match spec.Kind with
                            | MediaKind.Video(_, Some posterBinding) ->
                                let rawPoster =
                                    BindingResolver.tryResolve ctx.Sources posterBinding |> Option.defaultValue ""

                                match cardUrl ctx Sanitize.EgressClass.Media "poster" rawPoster with
                                | Ok p -> [ "poster", jstr p ]
                                | Error rule ->
                                    degrade rule n.Id n
                                    []
                            | _ -> []

                        [ Piece.Element(
                              jobj (
                                  [ "type", jstr "Media"
                                    "sources", jarr [ jobj [ "mimeType", jstr mime; "url", jstr url ] ]
                                    "altText", jstr label ]
                                  @ poster
                              )
                          ) ]
            | NodeKind.DataGrid spec ->
                match spec.StaticRows with
                | None -> openLive n DegradationRule.OpenLive "Table"
                | Some sr ->
                    let row (style: (string * JsonNode) list) (cells: TextSource list) =
                        columnSet [ for c in cells -> column (jstr "stretch") [ line (text c) style ] ]

                    [ Piece.Element(container None (row (bold ()) sr.Headers :: (sr.Rows |> List.map (row [])))) ]
            | NodeKind.Button spec ->
                let label = normalise (text spec.Label)

                if resolvedTrue spec.Disabled then
                    degrade DegradationRule.Disabled n.Id n
                    [ Piece.Element(line (label + " (unavailable)") (subtle ())) ]
                else
                    match classifyAction ctx spec.OnClick with
                    | CardAction.OpenUrl url -> [ Piece.Element(actionSet (openUrl label url)) ]
                    | CardAction.Execute(verb, action) ->
                        degrade (DegradationRule.ActionAsExecute(action, verb)) n.Id n
                        [ Piece.Element(actionSet (execute label verb n.Id "none")) ]
                    | CardAction.NotProjectable rule -> openLive n rule label
            | NodeKind.Form spec ->
                let submit = normalise (text spec.SubmitLabel)

                if resolvedTrue spec.Disabled then
                    degrade DegradationRule.Disabled n.Id n
                    [ Piece.Element(line (submit + " (unavailable)") (subtle ())) ]
                else
                    match classifyAction ctx spec.OnSubmit with
                    | CardAction.Execute(verb, action) ->
                        degrade (DegradationRule.ActionAsExecute(action, verb)) n.Id n
                        let inputs = spec.Fields |> List.collect (fieldInputs n)

                        [ Piece.Element(container None (inputs @ [ actionSet (execute submit verb n.Id "auto") ])) ]
                    // A navigation cannot carry inputs, and a client-only submit
                    // reaches nothing: either way the inputs would be controls
                    // that cannot fire.
                    | CardAction.OpenUrl _ -> openLive n (DegradationRule.ClientOnlyAction "Navigate") "Form"
                    | CardAction.NotProjectable rule -> openLive n rule "Form"

            // ── Flattened to text ────────────────────────────────────────────
            | NodeKind.Badge spec -> flattened n [ line (text spec.Label) (bold () @ badgeColour spec.Variant) ]
            | NodeKind.List spec ->
                flattened
                    n
                    (spec.Items
                     |> List.mapi (fun i item ->
                         line ((if spec.Ordered then string (i + 1) + ". " else "• ") + text item) []))
            | NodeKind.CodeBlock spec ->
                flattened n [ richText [ run (spec.Code.TrimEnd('\n', '\r')) [ "fontType", jstr "monospace" ] ] ]
            | NodeKind.Math spec -> flattened n [ line spec.Source [ "fontType", jstr "monospace" ] ]
            | NodeKind.Progress spec ->
                let status =
                    if spec.Indeterminate then
                        "in progress"
                    else
                        match BindingResolver.resolve ctx.Sources spec.Fraction with
                        | BindingResolver.Resolved v -> string (int (Math.Round(max 0.0 (min 1.0 v) * 100.0))) + "%"
                        | _ -> "progress unknown"

                let label =
                    spec.Label
                    |> Option.map (text >> normalise)
                    |> Option.filter (fun l -> l <> "")
                    |> Option.defaultValue "Progress"

                flattened n [ line (label + ": " + status) [] ]
            | NodeKind.Toast spec ->
                if BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false then
                    flattened n [ container (Some(toneStyle spec.Tone)) [ line (text spec.Message) [] ] ]
                else
                    degrade DegradationRule.Closed n.Id n
                    []

            // ── Open-live ────────────────────────────────────────────────────
            | NodeKind.Chart spec ->
                openLive n DegradationRule.OpenLive (spec.Title |> Option.map text |> Option.defaultValue "Chart")
            | NodeKind.Select spec -> openLive n DegradationRule.OpenLive (text spec.Label)
            | NodeKind.FileUpload spec -> openLive n DegradationRule.OpenLive (text spec.Label)
            | NodeKind.Filters _ -> openLive n DegradationRule.OpenLive "Filters"
            | NodeKind.Tabs _ -> openLive n DegradationRule.OpenLive "Tabbed section"
            | NodeKind.Stepper _ -> openLive n DegradationRule.OpenLive "Step-by-step section"
            | NodeKind.Modal spec ->
                openLive n DegradationRule.OpenLive (spec.Heading |> Option.map text |> Option.defaultValue "Dialog")
            | NodeKind.Map _ -> openLive n DegradationRule.OpenLive "Map"
            | NodeKind.Embed spec -> openLive n DegradationRule.OpenLive (text spec.Title)
            | NodeKind.Tree _ -> openLive n DegradationRule.OpenLive "Hierarchy"
            | NodeKind.Sparkline _ -> openLive n DegradationRule.OpenLive "Trend"
            | NodeKind.Drawing spec ->
                openLive n DegradationRule.OpenLive (spec.Title |> Option.map text |> Option.defaultValue "Diagram")
            | NodeKind.Custom spec -> openLive n DegradationRule.OpenLive spec.ComponentId
            | NodeKind.Mount _ -> openLive n DegradationRule.OpenLive "Embedded view"

            // ── Omitted ──────────────────────────────────────────────────────
            | NodeKind.Icon _
            | NodeKind.Skeleton _
            | NodeKind.FragmentDecl _ ->
                degradeSubtree DegradationRule.Omitted n.Id n
                []

        let body = walk 1 root |> assemble

        let card =
            jobj
                [ "type", jstr "AdaptiveCard"
                  "$schema", jstr SchemaUrl
                  "version", jstr SchemaVersion
                  "body", jarr (body |> List.map (fun e -> e :> JsonNode)) ]

        let bytes = Text.Encoding.UTF8.GetByteCount(card.ToJsonString())

        match options.Limits.MaxSerializedBytes with
        | Some limit when bytes > limit ->
            Error(
                ProjectionRefusal.CardPayloadExceeded(
                    options.Limits.Identity,
                    PreEmitValidate.hostLimitCode HostLimitKind.SerializedBytes,
                    limit,
                    bytes
                )
            )
        | _ -> Ok(card, List.ofSeq degraded)

/// Project under the default options (no live surface, `HostLimits.card`).
let project
    (ctx: Render.ServerRenderContext)
    (root: Node<obj>)
    : Result<JsonObject * Degradation list, ProjectionRefusal> =
    projectWith defaults ctx root

/// Project a tree with no dynamic bindings, under the default context and
/// options. The default context denies every non-local destination.
let projectStatic (root: Node<obj>) : Result<JsonObject * Degradation list, ProjectionRefusal> =
    project (Render.mkContext BindingResolver.empty root) root

/// The card's canonical JSON text: compact, in the projection's member order.
let toJson (card: JsonObject) : string = card.ToJsonString()

/// A refusal as one log-safe line.
let describeRefusal (refusal: ProjectionRefusal) : string =
    match refusal with
    | ProjectionRefusal.HostLimitsExceeded(limits, findings) ->
        sprintf
            "the tree is over the '%s' host limits: %s"
            limits
            (findings |> List.map (fun (code, msg) -> code + " " + msg) |> String.concat "; ")
    | ProjectionRefusal.CardPayloadExceeded(limits, code, limit, measured) ->
        sprintf
            "%s the projected card is %d UTF-8 bytes, over the '%s' host limits' maxSerializedBytes of %d"
            code
            measured
            limits
            limit
