namespace Fuaran.UI.Renderer.Server

// ============================================================================
//  Fuaran — the server renderer's HTML emitter (Phase 2515).
//
//  The one place this package turns structure into HTML text. Every element it
//  serves is written here, from an `ElementPlan` (the per-kind plans both
//  renderers share, Phase 2108) or from the small builders below, so the
//  escaping floor has one definition. It replaces `Feliz.ViewEngine`, which the
//  server renderer used to build an element tree and then serialise it; the
//  package's closure now holds no client DSL.
//
//  THE BYTES ARE THE CONTRACT. The served HTML is parity-locked against the
//  client renderer (the SSR parity corpus, hydration ids, the email corpus), so
//  this emitter writes exactly what the library it replaces wrote:
//
//    * text content and attribute values escape the same five characters, the
//      same way: `<` `&lt;`, `>` `&gt;`, `"` `&quot;`, `'` `&apos;`, `&` `&amp;`;
//    * an attribute is ` name="value"`, in the order given, with the NAME
//      written verbatim — names are renderer-controlled, and the untrusted seam
//      (`ExtraAttributes`) is gated upstream by `Sanitize.isSafeAttributeName`
//      in `ElementPlan.NodePlan`, because HTML has no escape for a name;
//    * a void element is `<tag attrs>`, with no closing tag and no content;
//    * an element's text, when it has one, precedes its children.
//
//  `Markup` is an inert fragment: building one writes nothing, and the HTML is
//  produced once, in document order, by `Markup.toHtml`. Composition is
//  therefore free of the repeated copying a string-concatenating builder pays
//  per nesting level.
// ============================================================================

open System.Text
open Fuaran.UI.Renderer

/// A server HTML fragment: an element, a run of escaped text, trusted markup,
/// or a sequence of them. Built with the `Markup` module; turned into HTML once
/// by `Markup.toHtml`.
[<Sealed>]
type Markup internal (write: StringBuilder -> unit) =
    member internal _.WriteTo(sb: StringBuilder) : unit = write sb

    /// The fragment's HTML (`Markup.toHtml`).
    override _.ToString() : string =
        let sb = StringBuilder()
        write sb
        sb.ToString()

/// Builders for server markup, and the emitter for `ElementPlan`.
[<RequireQualifiedAccess>]
module Markup =

    let private needsEscaping (s: string) : bool =
        let mutable found = false
        let mutable i = 0

        while not found && i < s.Length do
            match s.[i] with
            | '<'
            | '>'
            | '"'
            | '\''
            | '&' -> found <- true
            | _ -> ()

            i <- i + 1

        found

    let private appendEscaped (sb: StringBuilder) (s: string) : unit =
        if isNull s || s.Length = 0 then
            ()
        elif not (needsEscaping s) then
            sb.Append s |> ignore
        else
            for c in s do
                match c with
                | '<' -> sb.Append "&lt;" |> ignore
                | '>' -> sb.Append "&gt;" |> ignore
                | '"' -> sb.Append "&quot;" |> ignore
                | '\'' -> sb.Append "&apos;" |> ignore
                | '&' -> sb.Append "&amp;" |> ignore
                | c -> sb.Append c |> ignore

    /// The escaping floor: `s` with `<` `>` `"` `'` `&` replaced by their
    /// entities, as text content or as a double-quoted attribute value. A null
    /// string escapes to the empty string.
    let escape (s: string) : string =
        if isNull s then
            ""
        elif not (needsEscaping s) then
            s
        else
            let sb = StringBuilder(s.Length + 8)
            appendEscaped sb s
            sb.ToString()

    /// Nothing at all.
    let empty: Markup = Markup(ignore)

    /// Escaped text.
    let text (s: string) : Markup = Markup(fun sb -> appendEscaped sb s)

    /// Markup written exactly as given — the host trust boundary. The caller
    /// vouches for `html`: nothing here escapes or inspects it. A registered
    /// `Custom` renderer built with another HTML library reaches the server
    /// renderer this way.
    let raw (html: string) : Markup =
        Markup(fun sb ->
            if not (isNull html) then
                sb.Append html |> ignore)

    /// Fragments in sequence, with no element around them.
    let concat (items: Markup seq) : Markup =
        let items = List.ofSeq items

        Markup(fun sb ->
            for item in items do
                item.WriteTo sb)

    let private appendAttrs (sb: StringBuilder) (attrs: (string * string) list) : unit =
        for name, value in attrs do
            sb.Append(' ').Append(name).Append("=\"") |> ignore
            appendEscaped sb value
            sb.Append '"' |> ignore

    let private write (tag: string) (attrs: (string * string) list) (content: StringBuilder -> unit) : Markup =
        Markup(fun sb ->
            sb.Append('<').Append(tag) |> ignore
            appendAttrs sb attrs
            sb.Append '>' |> ignore
            content sb
            sb.Append("</").Append(tag).Append('>') |> ignore)

    /// `<tag attrs>children</tag>`. Attribute values are escaped; names are
    /// written verbatim (see the file header).
    let element (tag: string) (attrs: (string * string) list) (children: Markup list) : Markup =
        write tag attrs (fun sb ->
            for child in children do
                child.WriteTo sb)

    /// `<tag attrs>escaped text</tag>`.
    let textElement (tag: string) (attrs: (string * string) list) (content: string) : Markup =
        write tag attrs (fun sb -> appendEscaped sb content)

    /// `<tag attrs>html</tag>` with `html` written raw — a `<style>` or
    /// `<script>` body the caller has already made safe for its element.
    let rawElement (tag: string) (attrs: (string * string) list) (html: string) : Markup =
        write tag attrs (fun sb ->
            if not (isNull html) then
                sb.Append html |> ignore)

    /// `<tag attrs>`: a void element, which has no content and no closing tag.
    let voidElement (tag: string) (attrs: (string * string) list) : Markup =
        Markup(fun sb ->
            sb.Append('<').Append(tag) |> ignore
            appendAttrs sb attrs
            sb.Append '>' |> ignore)

    /// The fragment's HTML.
    let toHtml (markup: Markup) : string = markup.ToString()

    // ─── The plan emitter (Phase 2108, string-emitting since Phase 2515) ────

    /// The boolean attributes the server wrote through its element library's
    /// boolean props before Phase 2278, which spell them `name="true"`; every
    /// other flag was a custom attribute with an empty value. Both spellings are
    /// kept, which keeps the served bytes.
    let private libraryBooleans =
        set [ "disabled"; "controls"; "loop"; "autoplay"; "muted"; "required" ]

    /// A planned property on an element of `tag`, as the attributes it serves.
    /// A behaviour attachment point writes nothing: every affordance the server
    /// serves is inert (Phase 2278). A controlled value lands where the document
    /// can show it (Phase 2290): the `value` attribute on an input, nothing on a
    /// `<select>` or a `<textarea>`, whose selection and text the plan carries
    /// elsewhere.
    let private attributes (tag: string) (p: ElementPlan.Prop) : (string * string) list =
        match p with
        | ElementPlan.Prop.Attr(name, value) -> [ name, value ]
        | ElementPlan.Prop.Style declarations ->
            [ "style", declarations |> List.map (fun (k, v) -> k + ":" + v) |> String.concat ";" ]
        | ElementPlan.Prop.Flag name when libraryBooleans.Contains name -> [ name, "true" ]
        | ElementPlan.Prop.Flag name -> [ name, "" ]
        | ElementPlan.Prop.Value value when tag <> "select" && tag <> "textarea" -> [ "value", value ]
        | ElementPlan.Prop.Checked(true, served) -> [ "checked", served ]
        | ElementPlan.Prop.Selected -> [ "selected", "selected" ]
        | ElementPlan.Prop.Value _
        | ElementPlan.Prop.Values _
        | ElementPlan.Prop.Checked _
        | ElementPlan.Prop.Behaviour _ -> []

    /// A `<textarea>`'s served text is its controlled value (Phase 2290).
    let private textareaText (spec: ElementPlan.ElementSpec<Markup>) : string option =
        if spec.Tag = "textarea" then
            spec.Props
            |> List.tryPick (function
                | ElementPlan.Prop.Value v -> Some v
                | _ -> None)
        else
            None

    /// Emit a plan. Attributes land in plan order; a void tag takes no content;
    /// a reconciliation key is the client's alone and is dropped; pre-built
    /// markup (`Content.InnerHtml`) is written raw; a `Plan.Host` is markup this
    /// renderer already built.
    let rec ofPlan (plan: ElementPlan.Plan<Markup>) : Markup =
        match plan with
        | ElementPlan.Plan.Host markup -> markup
        | ElementPlan.Plan.Nothing -> empty
        | ElementPlan.Plan.TextNode s -> text s
        | ElementPlan.Plan.Element spec ->
            let attrs = spec.Props |> List.collect (attributes spec.Tag)

            if ElementPlan.voidTags.Contains spec.Tag then
                voidElement spec.Tag attrs
            else
                match spec.Content, textareaText spec with
                | ElementPlan.Content.Empty, Some s -> textElement spec.Tag attrs s
                | ElementPlan.Content.Empty, None -> element spec.Tag attrs []
                | ElementPlan.Content.Text s, _ -> textElement spec.Tag attrs s
                | ElementPlan.Content.Children children, _ -> element spec.Tag attrs (children |> List.map ofPlan)
                | ElementPlan.Content.InnerHtml html, _ -> rawElement spec.Tag attrs html
