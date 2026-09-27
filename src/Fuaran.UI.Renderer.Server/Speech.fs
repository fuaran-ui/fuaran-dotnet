module Fuaran.UI.Renderer.Server.Speech

// ============================================================================
//  Fuaran — the speech projection (Phase 1813): a tree read aloud.
//
//  The counterpart of the email projection (Phase 441) for a surface with no
//  pixels. It walks the same `Node<obj>` tree every other server projection
//  walks, through the same `ServerRenderContext`, and produces a SPOKEN SCRIPT:
//  ordered utterances carrying a pause / emphasis vocabulary small enough to
//  lower to plain text or to SSML without losing anything either can express.
//
//  WHAT IS SAID. Where a node declares `accessibility.speak` (Phase 1812), that
//  is what is said — exactly, whatever the kind. Where it does not, the
//  projection derives speech from what the node already carries, per the
//  per-kind derivation table `Fuaran.UI.RenderFidelity.speechRulings`, which
//  is the machine-readable form of this file's behaviour and is published as
//  the `speech` column of `render-fidelity.json`. The table is authored beside
//  the fidelity rows rather than here so that every host reads one ruling; the
//  test corpus asserts, kind by kind, that this projection does what its row
//  says.
//
//  NOTHING IS DROPPED SILENTLY. A node that contributes nothing to the script
//  — hidden, not visible, a closed toast, a branch not taken, content inside an
//  announced-only surface, a decorative image, a kind with nothing sayable —
//  appears in the omission list by node id, with the reason and the id of the
//  node whose state decided it. "Absent" is measured over the structural walk
//  (`Fuaran.UI.StructuralQuery.children`), the one containment relation the
//  tier uses everywhere else.
//
//  INTERACTIVE KINDS ARE ANNOUNCED, NEVER PRETENDED AT. The 441 rule, carried
//  over: a form is "a form with four fields", a tab set is a tabbed section
//  with its tab count — never a reading of whichever panel happens to be active,
//  which would be a lie about how much the section holds.
//
//  ORDER. Authored order, top to bottom, always. `accessibility.liveRegion`
//  does not move a node: a script is read once, so there is no later update
//  for politeness to schedule.
//
//  DETERMINISM. Same tree, same sources ⇒ same script. There is no clock read
//  (a `Binding.Now` resolves through `sources.Now`, which the host fixes for the
//  whole pass), no identifier minting, and no iteration over an unordered
//  collection. Text resolves through `Render.renderText` and figures through
//  `Render.formatNumber` — the functions the SSR document and the email digest
//  use — so a script cannot disagree with the page about what a number is.
//
//  INJECTION. SSML is markup, and a bound string is data. Every text reaching
//  the SSML lowering passes through `escapeSsml`, which escapes the five XML
//  metacharacters and DROPS every character XML 1.0 cannot carry, so no bound
//  value can open an element, close one, or make the document ill-formed.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.RenderFidelity

// ─── The script ─────────────────────────────────────────────────────────────

/// The pause after an utterance. Three steps, because both lowerings can carry
/// three: plain text ignores all of them (a line break is its only pause), and
/// SSML maps them to `<break>` strengths.
[<RequireQualifiedAccess>]
type Pause =
    | None
    /// Between sentences of one section.
    | Short
    /// After a heading: the listener's cue that a section begins.
    | Long

/// Where an utterance's words came from — the three sources a listener's
/// trust in them differs by.
[<RequireQualifiedAccess>]
type UtteranceSource =
    /// The node's own `accessibility.speak`, read exactly.
    | Speak
    /// Derived from the kind's own content (a `spoken` or `derived` ruling).
    | Derived
    /// An announcement of what the node IS (an `announced-only` ruling).
    | Announced

/// One thing said. `Text` is whitespace-normalised: no line break survives
/// inside it, which is what lets the plain-text lowering put exactly one
/// utterance on each line.
type Utterance =
    { NodeId: string
      Text: string
      Emphasis: bool
      PauseAfter: Pause
      Source: UtteranceSource }

/// The ordered script.
type SpeechScript = { Utterances: Utterance list }

/// Why a node is absent from the script.
[<RequireQualifiedAccess>]
type OmissionReason =
    /// `accessibility.hidden` resolved true on the deciding node.
    | Hidden
    /// `visible` resolved false on the deciding node.
    | NotVisible
    /// A closed `Toast`.
    | Closed
    /// A `Switch` case not selected, or an `ErrorBoundary` fallback.
    | NotTaken
    /// Content inside an `announced-only` node, which is announced rather than
    /// read.
    | InsideAnnounced
    /// An `Image` whose alt is empty: declared decorative by its author.
    | Decorative
    /// The node's kind carries nothing a listener could be told.
    | NothingSayable
    /// Nesting beyond `WireLimits.MaxDepth`.
    | DepthExceeded
    /// A `FragmentRef` naming no declared fragment.
    | UnresolvedFragment of name: string

/// One node absent from the script. `DecidedAt` is the id of the node whose
/// state caused the omission — the node itself, or the ancestor whose subtree
/// was excluded — so a reader can go from any omission to its cause.
type SpeechOmission =
    { NodeId: string
      Kind: string
      Reason: OmissionReason
      DecidedAt: string }

// ─── Text helpers ───────────────────────────────────────────────────────────

/// Collapse every whitespace run (line breaks included) to one space and trim.
let private normalise (s: string) : string =
    if isNull s then
        ""
    else
        let sb = System.Text.StringBuilder(s.Length)
        let mutable pendingSpace = false

        for c in s do
            if System.Char.IsWhiteSpace c then
                pendingSpace <- sb.Length > 0
            else
                if pendingSpace then
                    sb.Append ' ' |> ignore
                    pendingSpace <- false

                sb.Append c |> ignore

        sb.ToString()

/// Is this character carried by XML 1.0? (`Char ::= #x9 | #xA | #xD |
/// [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]`.) Surrogates are
/// judged in pairs by the caller.
let private xmlCharOk (c: char) : bool =
    c = '\t'
    || c = '\n'
    || c = '\r'
    || (c >= ' ' && c <= '퟿')
    || (c >= '' && c <= '�')

/// The projection's own SSML escaper. Escapes `& < > " '` and DROPS every
/// character XML 1.0 cannot carry (C0 controls, a lone surrogate, U+FFFE /
/// U+FFFF), so the result is always well-formed character data: a bound string
/// can neither inject an element nor make the document unparseable.
let escapeSsml (s: string) : string =
    if isNull s then
        ""
    else
        let sb = System.Text.StringBuilder(s.Length + 16)
        let mutable i = 0

        while i < s.Length do
            let c = s.[i]

            if
                System.Char.IsHighSurrogate c
                && i + 1 < s.Length
                && System.Char.IsLowSurrogate s.[i + 1]
            then
                sb.Append(c).Append(s.[i + 1]) |> ignore
                i <- i + 2
            else
                match c with
                | '&' -> sb.Append "&amp;" |> ignore
                | '<' -> sb.Append "&lt;" |> ignore
                | '>' -> sb.Append "&gt;" |> ignore
                | '"' -> sb.Append "&quot;" |> ignore
                | '\'' -> sb.Append "&apos;" |> ignore
                | c when xmlCharOk c -> sb.Append c |> ignore
                | _ -> ()

                i <- i + 1

        sb.ToString()

/// Markdown read aloud: the one deterministic GFM render, with its markup
/// removed and its entities decoded, split into one utterance per block. The
/// render escapes raw HTML by construction, so every `<` in its output opens a
/// tag the renderer itself wrote and stripping by tag is exact.
let private markdownBlocks (policy: Sanitize.EgressPolicy) (source: string) : string list =
    let html = Markdown.toHtmlWithEgress policy source
    let sb = System.Text.StringBuilder(html.Length)
    let mutable i = 0

    while i < html.Length do
        let c = html.[i]

        if c = '<' then
            let close = html.IndexOf('>', i)

            if close < 0 then
                i <- html.Length
            else
                let tag = html.Substring(i + 1, close - i - 1).ToLowerInvariant()

                let isBoundary =
                    tag.StartsWith "/p"
                    || tag.StartsWith "/li"
                    || tag.StartsWith "/h"
                    || tag.StartsWith "/pre"
                    || tag.StartsWith "/blockquote"
                    || tag.StartsWith "/tr"
                    || tag.StartsWith "br"
                    || tag.StartsWith "hr"

                // A block boundary becomes a line break (an utterance boundary);
                // any other tag becomes a space so adjacent words never fuse.
                sb.Append(if isBoundary then '\n' else ' ') |> ignore
                i <- close + 1
        else
            sb.Append c |> ignore
            i <- i + 1

    System.Net.WebUtility.HtmlDecode(sb.ToString()).Split('\n')
    |> Array.map normalise
    |> Array.filter (fun s -> s <> "")
    |> List.ofArray

// ─── The projection ─────────────────────────────────────────────────────────

/// What a node says of itself (before its children), per its kind's ruling.
type private Own =
    { Lines: (string * bool * Pause) list
      Source: UtteranceSource }

let private derived lines =
    { Lines = lines
      Source = UtteranceSource.Derived }

let private sentence (s: string) = s, false, Pause.Short
let private heading (s: string) = s, true, Pause.Long

/// "Noun: name, detail", with each optional part omitted cleanly.
let private announcement (noun: string) (name: string) (detail: string option) : Own =
    let head = if name = "" then noun else noun + ": " + name

    let text =
        match detail with
        | Some d when d <> "" -> head + ", " + d
        | _ -> head

    { Lines = [ sentence text ]
      Source = UtteranceSource.Announced }

let private count (n: int) (singular: string) (plural: string) =
    string n + " " + (if n = 1 then singular else plural)

/// Project a tree to a spoken script and the list of nodes absent from it.
///
/// `ctx` is the server render context every server projection uses
/// (`Render.mkContext` and its variants): bindings, fragments, locale, the
/// fixed `now`, and the egress policy a markdown body is rendered under all
/// come from it.
let project (ctx: Render.ServerRenderContext) (root: Node<obj>) : SpeechScript * SpeechOmission list =
    let said = ResizeArray<Utterance>()
    let omitted = ResizeArray<SpeechOmission>()
    let text = Render.renderText ctx

    let kindOf (n: Node<obj>) = wireNameOf n.Kind

    let rec omitSubtree (reason: OmissionReason) (decidedAt: string) (n: Node<obj>) =
        omitted.Add
            { NodeId = n.Id
              Kind = kindOf n
              Reason = reason
              DecidedAt = decidedAt }

        for c in Fuaran.UI.StructuralQuery.children n do
            omitSubtree reason decidedAt c

    let omitOne (reason: OmissionReason) (n: Node<obj>) =
        omitted.Add
            { NodeId = n.Id
              Kind = kindOf n
              Reason = reason
              DecidedAt = n.Id }

    let say (nodeId: string) (source: UtteranceSource) (line: string, emphasis: bool, pause: Pause) : bool =
        let t = normalise line

        if t = "" then
            false
        else
            said.Add
                { NodeId = nodeId
                  Text = t
                  Emphasis = emphasis
                  PauseAfter = pause
                  Source = source }

            true

    let accessibleLabel (n: Node<obj>) : string =
        n.Accessibility
        |> Option.bind _.Label
        |> Option.bind (BindingResolver.tryResolveScalarText ctx.Sources)
        |> Option.map normalise
        |> Option.defaultValue ""

    let isHidden (n: Node<obj>) : bool =
        match n.Accessibility |> Option.bind _.Hidden with
        | Some b ->
            match BindingResolver.resolveScalarBool ctx.Sources b with
            | BindingResolver.Resolved v -> v
            | _ -> false
        | None -> false

    let figure (format: CellFormat) (binding: Binding<float>) : string =
        match BindingResolver.resolveScalarFloat ctx.Sources binding with
        | BindingResolver.Resolved v -> Render.formatNumber format v
        | BindingResolver.NotResolved -> "no value"
        | BindingResolver.Errored _
        | BindingResolver.I18nUnresolved _ -> "unavailable"

    let labelOr (n: Node<obj>) (fallback: string) =
        match accessibleLabel n with
        | "" -> normalise fallback
        | l -> l

    /// Walk one node. Returns whether anything in its subtree was said.
    let rec walk (depth: int) (n: Node<obj>) : bool =
        if depth > Fuaran.UI.WireLimits.MaxDepth then
            omitSubtree OmissionReason.DepthExceeded n.Id n
            false
        elif not (BindingResolver.isNodeVisible ctx.Sources n) then
            omitSubtree OmissionReason.NotVisible n.Id n
            false
        elif isHidden n then
            omitSubtree OmissionReason.Hidden n.Id n
            false
        else
            walkVisible depth n

    and walkChildren (depth: int) (kids: Node<obj> list) : bool =
        // Every child is walked (its omissions matter even after a sibling
        // spoke), so this is a fold rather than a short-circuiting `exists`.
        kids |> List.fold (fun acc k -> walk (depth + 1) k || acc) false

    and walkVisible (depth: int) (n: Node<obj>) : bool =
        let speak =
            n.Accessibility
            |> Option.bind _.Speak
            |> Option.map (text >> normalise)
            |> Option.defaultValue ""

        // `speak` REPLACES what the node says of itself. It does not silence a
        // structural node's children — an author naming a section still wants
        // the section read — and an announced-only node's content stays
        // unread either way.
        let saySelf (own: Own) : bool =
            if speak <> "" then
                say n.Id UtteranceSource.Speak (speak, false, Pause.Short)
            else
                own.Lines |> List.fold (fun acc line -> say n.Id own.Source line || acc) false

        /// A leaf with nothing to say from its own content falls back to its
        /// accessible label; failing that it is reported as nothing sayable.
        let leaf (own: Own) : bool =
            let spoke =
                saySelf own
                || (speak = "" && say n.Id UtteranceSource.Derived (sentence (accessibleLabel n)))

            if not spoke then
                omitOne OmissionReason.NothingSayable n

            spoke

        /// A structure carrier: its own lines (a heading), then its children.
        /// Reported as nothing sayable only when neither it nor any descendant
        /// said anything.
        let container (own: Own) (kids: Node<obj> list) : bool =
            let self = saySelf own
            let fromKids = walkChildren depth kids

            if not (self || fromKids) then
                omitOne OmissionReason.NothingSayable n

            self || fromKids

        /// An announced-only node: the announcement, and every structural
        /// descendant reported as inside it.
        let announced (own: Own) : bool =
            let spoke = saySelf own

            for c in Fuaran.UI.StructuralQuery.children n do
                omitSubtree OmissionReason.InsideAnnounced n.Id c

            if not spoke then
                omitOne OmissionReason.NothingSayable n

            spoke

        /// An omitted-by-ruling node: nothing unless `speak` supplies words.
        let silent () : bool =
            if speak <> "" then
                say n.Id UtteranceSource.Speak (speak, false, Pause.Short)
            else
                omitSubtree OmissionReason.NothingSayable n.Id n
                false

        match n.Kind with

        // ── Structure (derived: a heading at most, then the children) ────────
        | NodeKind.Box spec ->
            let own =
                match spec.Role, spec.Heading with
                | BoxRole.Card, Some h -> derived [ heading (text h) ]
                | _ -> derived []

            container own spec.Children
        | NodeKind.SplitPanel spec -> container (derived []) spec.Children
        | NodeKind.ScrollArea spec -> container (derived []) spec.Children
        | NodeKind.SummaryList spec ->
            container (derived (spec.Heading |> Option.map (text >> heading) |> Option.toList)) spec.Children
        | NodeKind.Disclosure spec -> container (derived [ heading (text spec.Heading) ]) spec.Children
        | NodeKind.ErrorBoundary spec ->
            omitSubtree OmissionReason.NotTaken n.Id spec.Fallback
            container (derived []) [ spec.Child ]
        | NodeKind.Switch spec ->
            let chosen = Email.selectedSwitchBranch ctx spec

            for branch in (spec.Cases |> List.map _.Child) @ [ spec.Default ] do
                if not (LanguagePrimitives.PhysicalEquality branch chosen) then
                    omitSubtree OmissionReason.NotTaken n.Id branch

            container (derived []) [ chosen ]
        | NodeKind.FragmentRef spec ->
            match Map.tryFind spec.Name ctx.Fragments with
            | Some body -> container (derived []) [ body ]
            | None ->
                if speak <> "" then
                    say n.Id UtteranceSource.Speak (speak, false, Pause.Short)
                else
                    omitOne (OmissionReason.UnresolvedFragment spec.Name) n
                    false

        // ── Spoken: authored prose, as written ───────────────────────────────
        | NodeKind.Heading spec -> leaf (derived [ heading (text spec.Text) ])
        | NodeKind.Markdown spec ->
            leaf (derived (markdownBlocks ctx.EgressPolicy (text spec.Text) |> List.map sentence))
        | NodeKind.Callout spec ->
            leaf (
                derived (
                    (spec.Heading |> Option.map (text >> heading) |> Option.toList)
                    @ [ sentence (text spec.Body) ]
                )
            )
        | NodeKind.List spec ->
            let items =
                spec.Items
                |> List.mapi (fun i item ->
                    let t = text item
                    sentence (if spec.Ordered then string (i + 1) + ". " + t else t))

            leaf (derived items)
        | NodeKind.Toast spec ->
            let isOpen =
                BindingResolver.tryResolve ctx.Sources spec.Open |> Option.defaultValue false

            if isOpen then
                leaf (derived [ sentence (text spec.Message) ])
            else
                omitOne OmissionReason.Closed n
                false

        // ── Derived: composed from typed fields ──────────────────────────────
        | NodeKind.Metric spec ->
            let value = figure spec.Format spec.Value

            let trend =
                spec.Trend
                |> Option.bind (BindingResolver.tryResolveScalarFloat ctx.Sources)
                |> Option.map (fun t ->
                    ", trend "
                    + Render.formatNumber (spec.TrendFormat |> Option.defaultValue CellFormat.None) t)
                |> Option.defaultValue ""

            let subtext = spec.Subtext |> Option.map (text >> sentence) |> Option.toList

            leaf (derived ([ sentence (text spec.Label + ": " + value + trend) ] @ subtext))
        | NodeKind.Fact spec ->
            let help = spec.Help |> Option.map (text >> sentence) |> Option.toList
            leaf (derived ([ sentence (text spec.Label + ": " + text spec.Value) ] @ help))
        | NodeKind.LabelValueRow spec ->
            leaf (derived [ sentence (text spec.Label + ": " + figure spec.Format spec.Value) ])
        | NodeKind.Badge spec -> leaf (derived [ sentence (text spec.Label) ])
        | NodeKind.Link spec -> leaf (derived [ sentence ("Link: " + text spec.Label) ])
        | NodeKind.Image spec ->
            match normalise (text spec.Alt) with
            | "" when speak = "" ->
                omitOne OmissionReason.Decorative n
                false
            | alt -> leaf (derived [ sentence ("Image: " + alt) ])
        | NodeKind.Progress spec ->
            let status =
                if spec.Indeterminate then
                    "in progress"
                else
                    match BindingResolver.resolve ctx.Sources spec.Fraction with
                    | BindingResolver.Resolved v ->
                        string (int (System.Math.Round(max 0.0 (min 1.0 v) * 100.0))) + " percent"
                    | _ -> "progress unknown"

            let line =
                match spec.Label |> Option.map (text >> normalise) with
                | Some l when l <> "" -> l + ": " + status
                | _ -> "Progress: " + status

            leaf (derived [ sentence line ])

        // ── Announced-only: what the node is, never its content ─────────────
        | NodeKind.Button spec -> announced (announcement "Button" (labelOr n (text spec.Label)) None)
        | NodeKind.Form spec ->
            announced (announcement "Form" (accessibleLabel n) (Some(count spec.Fields.Length "field" "fields")))
        | NodeKind.Filters spec ->
            announced (announcement "Filters" (accessibleLabel n) (Some(count spec.Items.Length "field" "fields")))
        | NodeKind.Select spec -> announced (announcement "Selection" (labelOr n (text spec.Label)) None)
        | NodeKind.FileUpload spec -> announced (announcement "File upload" (labelOr n (text spec.Label)) None)
        | NodeKind.Tabs spec ->
            announced (
                announcement "Tabbed section" (accessibleLabel n) (Some(count spec.Children.Length "tab" "tabs"))
            )
        | NodeKind.Stepper spec ->
            announced (
                announcement
                    "Step-by-step section"
                    (accessibleLabel n)
                    (Some(count spec.Children.Length "step" "steps"))
            )
        | NodeKind.Modal spec ->
            announced (
                announcement "Dialog" (labelOr n (spec.Heading |> Option.map text |> Option.defaultValue "")) None
            )
        | NodeKind.Chart spec ->
            announced (announcement "Chart" (labelOr n (spec.Title |> Option.map text |> Option.defaultValue "")) None)
        | NodeKind.Map _ -> announced (announcement "Map" (accessibleLabel n) None)
        | NodeKind.Media spec ->
            let noun =
                match spec.Kind with
                | MediaKind.Video _ -> "Video"
                | MediaKind.Audio -> "Audio"

            announced (announcement noun (labelOr n (text spec.Label)) None)
        | NodeKind.Embed spec -> announced (announcement "Embedded view" (labelOr n (text spec.Title)) None)
        | NodeKind.Tree _ -> announced (announcement "Hierarchy" (accessibleLabel n) None)
        | NodeKind.Sparkline _ -> announced (announcement "Trend line" (accessibleLabel n) None)
        | NodeKind.Drawing spec ->
            let own =
                announcement "Diagram" (labelOr n (spec.Title |> Option.map text |> Option.defaultValue "")) None

            let description = spec.Description |> Option.map (text >> sentence) |> Option.toList

            announced
                { own with
                    Lines = own.Lines @ description }
        | NodeKind.Custom spec -> announced (announcement "Component" (labelOr n spec.ComponentId) None)
        | NodeKind.Mount _ -> announced (announcement "Embedded view" (accessibleLabel n) None)
        | NodeKind.Math _ -> announced (announcement "Formula" (accessibleLabel n) None)
        | NodeKind.CodeBlock spec ->
            let lines =
                if System.String.IsNullOrEmpty spec.Code then
                    0
                else
                    spec.Code.TrimEnd('\n', '\r').Split('\n').Length

            let noun =
                if System.String.IsNullOrWhiteSpace spec.Language then
                    "Code block"
                else
                    "Code block (" + normalise spec.Language + ")"

            announced (announcement noun (accessibleLabel n) (Some(count lines "line" "lines")))
        | NodeKind.DataGrid spec ->
            let detail =
                spec.StaticRows
                |> Option.map (fun sr ->
                    count sr.Rows.Length "row" "rows"
                    + ", "
                    + count sr.Headers.Length "column" "columns")

            announced (announcement "Table" (accessibleLabel n) detail)

        | NodeKind.Icon spec -> leaf (derived (spec.Label |> Option.map sentence |> Option.toList))

        // ── Omitted: nothing said unless `speak` supplies words ─────────────
        | NodeKind.Skeleton _
        | NodeKind.FragmentDecl _ -> silent ()

    walk 1 root |> ignore

    { Utterances = List.ofSeq said }, List.ofSeq omitted

/// Project with a fresh context over the given sources (the common case).
let projectWith (sources: BindingResolver.BindingSources) (root: Node<obj>) : SpeechScript * SpeechOmission list =
    project (Render.mkContext sources root) root

/// Project a tree with no dynamic bindings.
let projectStatic (root: Node<obj>) : SpeechScript * SpeechOmission list = projectWith BindingResolver.empty root

// ─── Lowerings ──────────────────────────────────────────────────────────────

/// Plain text: one utterance per line, in order, each line ending in `\n`.
/// Pauses and emphasis have no plain-text form beyond the line break, and are
/// dropped rather than approximated with punctuation a synthesiser would read.
let toPlainText (script: SpeechScript) : string =
    script.Utterances |> List.map (fun u -> u.Text + "\n") |> String.concat ""

/// SSML: a `<speak>` document, one `<s>` per utterance, emphasis as
/// `<emphasis level="moderate">`, and pauses as `<break>` strengths. Every text
/// passes `escapeSsml`; the only markup in the document is written here.
let toSsml (script: SpeechScript) : string =
    let sb = System.Text.StringBuilder()
    sb.Append "<speak>\n" |> ignore

    for u in script.Utterances do
        let body = escapeSsml u.Text

        let body =
            if u.Emphasis then
                "<emphasis level=\"moderate\">" + body + "</emphasis>"
            else
                body

        sb.Append("<s>").Append(body).Append("</s>") |> ignore

        match u.PauseAfter with
        | Pause.None -> ()
        | Pause.Short -> sb.Append "<break strength=\"medium\"/>" |> ignore
        | Pause.Long -> sb.Append "<break strength=\"strong\"/>" |> ignore

        sb.Append '\n' |> ignore

    sb.Append "</speak>\n" |> ignore
    sb.ToString()

/// The class a projected node's OWN utterances carry, for the conformance
/// check that holds this file to its `render-fidelity.json` rows: `spoken` and
/// `derived` rulings produce `Derived` utterances, `announced-only` produces
/// `Announced`, and `omitted` produces none.
let expectedSource (ruling: SpeechRuling) : UtteranceSource option =
    match ruling with
    | SpeechRuling.Spoken _
    | SpeechRuling.Derived _ -> Some UtteranceSource.Derived
    | SpeechRuling.AnnouncedOnly _ -> Some UtteranceSource.Announced
    | SpeechRuling.Omitted _ -> None
