module Fuaran.UI.Renderer.Csp

// ============================================================================
//  Fuaran — the strict-CSP render mode (Phase 1545).
//
//  The renderers set inline `style` attributes for the handful of slots whose
//  value is CONTINUOUS — a grid track list, a masonry column count, a flex gap,
//  a split-pane weight, a scroll-area ceiling, a progress width — and inject the
//  theme through an inline `<style>` element. Under a Content-Security-Policy
//  that otherwise forbids everything, that forces every deploying host to ship
//  `style-src 'unsafe-inline'`, which is the remaining CSS exfiltration channel:
//  an injected style attribute can read the document with attribute selectors
//  and leak what it finds through a background URL. A language that owns a URL
//  floor should not be the reason a host cannot close it.
//
//  So a render carries a MODE. `Permissive` is what every existing entry point
//  builds and is byte-for-byte the emission this renderer has always produced —
//  the parity corpus does not move, and it cannot, because nothing on that path
//  consults this module. `Strict nonce` is reached BY NAME, and under it no
//  `style` attribute is emitted anywhere: each continuous value becomes a
//  generated CLASS plus a declaration collected into one nonce-bearing `<style>`
//  element per render root, and the theme element takes the same nonce.
//
//  ── Why the class name is derived rather than allocated ───────────────────
//  A counter would make two renders of one tree produce different bytes, which
//  defeats the two properties SSR output is held to (cache-stable, and
//  hydration-parity-safe against the client renderer's own emission — the
//  reasoning `Ids.deterministicCorrelationId` was written for). The name is
//  therefore a pure function of the node id, a slot discriminator, and the
//  declarations themselves. The declarations are in the seed as well as the
//  node id — the phase asks only for determinism, and including them makes it
//  impossible for one node id to name two different rules, which node ids being
//  author-supplied (and therefore not guaranteed unique) would otherwise allow.
//
//  The declarations reaching the seed are the CANONICAL CSS spelling — kebab-case
//  property names, in the order the emission site lists them. That is what lets
//  the client renderer, whose React style object requires camelCase keys, derive
//  the SAME name as the server renderer for the same node: the two tiers spell
//  the emission differently and hash the same canonical pairs.
//
//  ── FSharp.Core only ──────────────────────────────────────────────────────
//  This is the emission-agnostic spine, so nothing here knows about Feliz, React
//  or ViewEngine: the module answers "what class, what declarations, what
//  stylesheet text", and each renderer turns that into its own props.
// ============================================================================

open System.Collections.Generic

/// The Content-Security-Policy posture a render runs under.
///
/// `Permissive` is the default at every convenience entry point and is exactly
/// what the renderers emitted before this mode existed. `Strict` carries the
/// host's per-response nonce — nothing here generates one, for the reason
/// `ScriptRef.Nonce` already gives on the document shell: a nonce the document
/// could derive is a nonce an attacker can derive.
type CspMode =
    /// Today's emission: continuous values ride an inline `style` attribute and
    /// the theme rides an un-nonced `<style>` element.
    | Permissive
    /// No `style` attribute is emitted anywhere; continuous values become
    /// generated classes whose declarations are collected into one `<style>`
    /// element carrying this nonce, and the theme element carries it too.
    | Strict of nonce: string

/// Is this render emitting under the strict posture?
let isStrict (mode: CspMode) : bool =
    match mode with
    | Permissive -> false
    | Strict _ -> true

/// The nonce this render was handed, or `None` under `Permissive`.
let nonce (mode: CspMode) : string option =
    match mode with
    | Permissive -> None
    | Strict n -> Some n

// ─── The declaration text ──────────────────────────────────────────────────

/// Is this value safe to write into raw `<style>` element CONTENT?
///
/// Two gates, and the second is the one this sink adds. `Sanitize.isSafeCssValue`
/// is the shared emission grammar (`Fuaran.UI.EmissionGrammar`) every host
/// agrees on: it denies `;`, `{`, `}`, `\` and the C0 range, which is what stops
/// a value closing its own declaration or its rule. It does NOT deny `<`, and
/// deliberately so — in an ATTRIBUTE value `<` is escaped by React and by
/// ViewEngine, so it can neither open a tag nor end one.
///
/// A `<style>` element's content is not escaped, and the HTML parser looks for
/// `</style` inside it BEFORE any CSS parser sees the text. So a value carrying
/// that sequence would end the element and put the remainder of the stylesheet
/// into the document as markup — the Phase 1523 resume-envelope `</script`
/// finding, at a new sink. `<` and `>` are therefore refused here on top of the
/// shared grammar.
///
/// In practice this never fires: every value that reaches the collector has
/// already passed its own emission-site gate or is a renderer-formatted number.
/// It is the floor that makes that true by construction rather than by audit.
let isCollectableValue (value: string) : bool =
    if isNull value then
        false
    elif not (Sanitize.isSafeCssValue value) then
        false
    else
        let mutable ok = true

        for ch in value do
            if ch = '<' || ch = '>' then
                ok <- false

        ok

/// The canonical declaration text for a rule body — `prop:value;prop:value`,
/// in the order the emission site listed them. This is both what the class name
/// is derived from and what the collected stylesheet writes, so the two cannot
/// disagree about what a class means.
let declarationText (declarations: (string * string) list) : string =
    declarations
    |> List.map (fun (property, value) -> property + ":" + value)
    |> String.concat ";"

// ─── The generated class name ──────────────────────────────────────────────

// A CONCATENATION ROOT, never a complete class name. Two reasons, and the
// second is a live constraint rather than a preference: the completions are
// derived per render so no fixed vocabulary member exists to name, and
// `CssCoverageTests` scans every renderer source for complete `fuaran-a-b`
// string literals and requires the reference stylesheet to carry a rule for
// each one it finds. A trailing-hyphen root is excluded by that scan's own
// token shape, which is exactly the shape the tone / kind / weight roots
// already rely on.
[<Literal>]
let classRoot = "fuaran-csp-"

/// The class a node's continuous declarations are emitted under in strict mode.
///
/// Deterministic: the same node, slot and declarations always produce the same
/// name, on both renderers and on both pipelines (the hash is
/// `Ids.deterministicCorrelationId`, whose Fable parity is measured by
/// `tests/ids-parity-probe/` rather than asserted). `slot` discriminates two
/// declarations minted under one node id — a split panel's two panes are the
/// worked case.
let generatedClass (nodeId: string) (slot: string) (declarations: (string * string) list) : string =
    let seed =
        (if isNull nodeId then "" else nodeId)
        + "|"
        + slot
        + "|"
        + declarationText declarations

    classRoot + Ids.deterministicCorrelationId seed

// ─── The canonical declarations, per slot ──────────────────────────────────

/// The declaration set each continuous-value site contributes, in the CANONICAL
/// CSS spelling — kebab-case property names, the server renderer's own value
/// formatting, in emission order.
///
/// **One definition, called by both renderers**, and that is the whole reason
/// this module exists rather than each tier building its own pairs. The class
/// name is a hash of these pairs, so a tier whose spelling drifted by one
/// character would derive a name the other tier never generated — and the
/// failure would be a silently unstyled element in a hydrated document, not a
/// compile error or a red test. The tiers legitimately EMIT different bytes
/// here (the client's React style object needs camelCase keys; its progress
/// fill renders `width: 50%` where the server writes `width:50.000000%`), so
/// making the emission shared was never available; making the HASH INPUT shared
/// is, and it is the half that has to agree.
///
/// Each builder is a plain function of the values the arm already resolved, so
/// neither tier passes its own tree vocabulary in — the same posture `Css.fs`
/// takes for the class-string builders beside it.
[<RequireQualifiedAccess>]
module Declarations =

    /// A `Box` in `Grid` layout: the (already sanitised) track list, plus the
    /// gap when one is declared.
    let grid (templateColumns: string) (gap: int option) : (string * string) list =
        [ "grid-template-columns", templateColumns ]
        @ (match gap with
           | Some n -> [ "gap", string n + "px" ]
           | None -> [])

    /// A `Box` in `Masonry` layout: the column count, plus the gap when one is
    /// declared (WIRE_FORMAT §3.6.7's multi-column family).
    let masonry (columns: int) (gap: int option) : (string * string) list =
        [ "column-count", string columns ]
        @ (match gap with
           | Some n -> [ "gap", string n + "px" ]
           | None -> [])

    /// A `Box` in `Flex` layout: the gap alone, and nothing at all when none is
    /// declared — which is what keeps a gap-free stack free of any style
    /// attribute (and, in strict mode, of any generated class).
    let flex (gap: int option) : (string * string) list =
        match gap with
        | Some n -> [ "gap", string n + "px" ]
        | None -> []

    /// One pane of a `SplitPanel`, given that pane's share of the row.
    let splitPane (weight: float) : (string * string) list = [ "flex", sprintf "%f 1 0" weight ]

    /// A `ScrollArea`'s optional pixel ceilings.
    let scrollArea (maxHeight: int option) (maxWidth: int option) : (string * string) list =
        (match maxHeight with
         | Some h -> [ "max-height", string h + "px" ]
         | None -> [])
        @ (match maxWidth with
           | Some w -> [ "max-width", string w + "px" ]
           | None -> [])

    /// A `Progress` bar's fill width, as a percentage of the bar.
    let progressFill (fraction: float) : (string * string) list =
        [ "width", sprintf "%f%%" (fraction * 100.0) ]

// ─── The per-render collector ──────────────────────────────────────────────

/// The rules one render generated, in the order the walk produced them.
///
/// MUTABLE, and threaded on the render context rather than returned. The
/// alternative — making every `renderNode` arm return its declarations
/// alongside its element — would have rewritten several thousand call sites
/// across two renderers to carry a value that is empty for all but seven of
/// them, and the emission is a single-threaded walk over one tree per context,
/// so a per-render accumulator is the smaller and the safer change. One
/// collector belongs to one context and never outlives the render that built
/// it.
///
/// Determinism comes from the walk, not from sorting: a tree is walked in
/// document order, a class is registered the first time it is seen and ignored
/// afterwards, so two renders of one tree emit one byte sequence. Sorting would
/// have been equally deterministic and strictly less useful — document order is
/// what a reader debugging the emitted stylesheet expects.
type StyleCollector() =
    let order = ResizeArray<string * string>()
    let seen = HashSet<string>()

    /// Register `declarations` under `className`. First write wins; a repeat of
    /// the same class is a no-op, which is the common case — a class is derived
    /// from its own declarations, so two registrations of one name carry the
    /// same body by construction.
    ///
    /// A declaration whose VALUE is not collectable is dropped (see
    /// `isCollectableValue`). When that leaves nothing, the class is registered
    /// with no rule at all rather than with an empty body: the element keeps a
    /// class that styles nothing, which is what a refused value should look
    /// like, and the emission site has already marked the refusal in the
    /// document.
    member _.Register(className: string, declarations: (string * string) list) : unit =
        if not (seen.Contains className) then
            seen.Add className |> ignore

            let safe = declarations |> List.filter (fun (_, value) -> isCollectableValue value)

            if not (List.isEmpty safe) then
                order.Add(className, declarationText safe)

    /// The collected rules, in walk order.
    member _.Rules: (string * string) list = order |> List.ofSeq

    /// Did this render generate any rule at all? A tree with no continuous
    /// value generates none, and then strict mode emits no `<style>` element —
    /// so the mode costs an empty document nothing.
    member _.IsEmpty: bool = order.Count = 0

/// The CSS text for a collected rule set — `.cls{decls}` concatenated in walk
/// order, with no whitespace between rules so the bytes are a function of the
/// tree alone.
let stylesheetText (rules: (string * string) list) : string =
    rules
    |> List.map (fun (className, declarations) -> "." + className + "{" + declarations + "}")
    |> String.concat ""

// ─── The host's side of the contract ───────────────────────────────────────

/// The `style-src` directive a host sends for a document rendered under
/// `Strict nonce` — its own origin for the packaged reference stylesheet, and
/// this render's nonce for the collected element and the theme. No
/// `'unsafe-inline'`, which is the whole point of the mode.
///
/// A convenience, not a policy builder: a host composes the rest of its
/// Content-Security-Policy itself. It is here so that the sentence this mode
/// exists to make true — "a nonce source for styles and nothing else" — is one
/// call rather than a string a host has to get right from prose.
let styleSrcDirective (nonceValue: string) : string =
    "style-src 'self' 'nonce-" + nonceValue + "'"
