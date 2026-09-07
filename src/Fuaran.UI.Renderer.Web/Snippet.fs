module Fuaran.UI.Renderer.Web.Snippet

// ============================================================================
//  The HTML a host emits to hydrate a serialised tree in the browser.
//
//  A plain string-producing helper rather than a Razor tag helper, because the
//  hosts this package exists for do not agree on a view layer: a minimal-API
//  app writes HTML from a handler, an MVC app from a view, a Giraffe app from a
//  combinator. A `string` composes into all three; a tag helper composes into
//  one. (A tag helper over this is a few lines of Razor plumbing a consumer can
//  write; the reverse is not true.)
//
//  THE INTERACTION MODEL, because getting it wrong here is silent.
//
//  The browser raises a WIRE action. `Action.Notify(channel, payload)` and
//  `Action.Call(endpoint, into: …)` are the wire-representable signals, and
//  they are what this snippet wires. `Action.Dispatch` is NOT one: it carries a
//  host closure, the encoder drops the payload, and the decoder rebuilds it as
//  the `"<closure>"` sentinel — so the bundle's `dispatch` callback fires with
//  a sentinel and is a DIAGNOSTIC SIGNAL, never a message. This snippet
//  therefore wires `onNotify` and leaves `dispatch` alone.
//
//  Typed dispatch is obtained HOST-SIDE, by binding a handler table to the
//  artifact's declared action holes — uniform across hosts, checked against the
//  artifact's signature, and needing no per-language mechanism. This package
//  deliberately invents nothing of its own for it.
// ============================================================================

open System
open System.Text

/// How the emitted snippet reaches back to the host, and what it may assume
/// about the page it lands in.
type MountOptions =
    {
        /// The `id` of the element the tree mounts into. The snippet emits the
        /// element too, so a host need not keep the two in step by hand.
        ElementId: string
        /// The URL prefix `MapFuaranRenderer` was mounted at, no trailing
        /// slash. Only used to build the asset URLs.
        Prefix: string
        /// Where a `Notify` is POSTed as `{"channel": …, "payload": …}`.
        /// `None` leaves notifications unwired — correct for a read-only page,
        /// and stated rather than defaulted to a guessed route.
        NotifyEndpoint: string option
        /// A request header the notify POST carries, as `(name, value)` — an
        /// antiforgery token, in practice.
        ///
        /// The POST is same-origin and `fetch` sends the host's cookies with
        /// it, so a notify endpoint that mutates anything needs a token or it
        /// is CSRF-open: another origin cannot READ the reply, which is no
        /// comfort when the point of the request was the write. ASP.NET's
        /// default name is `RequestVerificationToken`, with the value from
        /// `IAntiforgery.GetAndStoreTokens(ctx).RequestToken`.
        ///
        /// `None` by default, and deliberately no guessed name and no generated
        /// value: a token this package invented would be one the host's
        /// validator has never seen, so it would fail every request while
        /// looking exactly like protection.
        AntiforgeryHeader: (string * string) option
        /// A CSP nonce for the inline script. `None` emits none, which is right
        /// for a host with no script-src policy and wrong for one that has;
        /// there is no safe default, so the host says.
        Nonce: string option
        /// Whether to emit the fingerprint-drift diagnostics. Pass the host's
        /// own development flag — the warning names internal version state and
        /// is not something to ship to a visitor.
        Development: bool
    }

/// The defaults a host overrides by name: mounted at `/_fuaran`, into
/// `#fuaran-root`, notifications unwired, no antiforgery header, no nonce,
/// diagnostics off.
///
/// Diagnostics default to OFF rather than on. A default that leaks version
/// state into production HTML would be the wrong way round for a package whose
/// consumers are, by construction, new to it.
let defaults =
    { ElementId = "fuaran-root"
      Prefix = "/_fuaran"
      NotifyEndpoint = None
      AntiforgeryHeader = None
      Nonce = None
      Development = false }

// ─── Escaping ─────────────────────────────────────────────────────────────

/// Escape for an HTML attribute value or text node.
let private htmlEscape (s: string) : string =
    let sb = StringBuilder()

    for ch in s do
        match ch with
        | '&' -> sb.Append "&amp;" |> ignore
        | '<' -> sb.Append "&lt;" |> ignore
        | '>' -> sb.Append "&gt;" |> ignore
        | '"' -> sb.Append "&quot;" |> ignore
        | '\'' -> sb.Append "&#39;" |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

/// Make a JSON document safe to sit inside a `<script>` element.
///
/// The hazard is `</script` appearing anywhere in the payload — inside a string
/// literal is enough — because the HTML tokenizer does not parse JavaScript and
/// closes the element there, dumping the remainder of the tree into the
/// document as markup. Escaping `<` to its `\u003c` form is the standard
/// remedy and leaves the JSON value identical: the two parse to the same
/// character. `&` and the two line separators are escaped for the neighbouring
/// hazards (an HTML-escaping proxy, and U+2028/U+2029 terminating a JS line).
let private scriptSafeJson (json: string) : string =
    let sb = StringBuilder()

    for ch in json do
        match ch with
        | '<' -> sb.Append "\\u003c" |> ignore
        | '>' -> sb.Append "\\u003e" |> ignore
        | '&' -> sb.Append "\\u0026" |> ignore
        // U+2028 / U+2029 are legal in JSON and terminate a JavaScript line.
        | c when int c = 0x2028 -> sb.Append "\\u2028" |> ignore
        | c when int c = 0x2029 -> sb.Append "\\u2029" |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

/// Render `s` as a complete double-quoted JavaScript string literal — the
/// QUOTES ARE PART OF THE RESULT, so a call site cannot pair this escaper with
/// the wrong ones.
///
/// A JS string literal is not an HTML attribute, and escaping one as the other
/// is wrong in both directions at once. The HTML escaper turns `&` into
/// `&amp;`, which inside a script is five literal characters: an endpoint of
/// `/notify?a=1&b=2` became a fetch to `/notify?a=1&amp;b=2`, a URL nobody
/// routes, and the page reported nothing because the request was well-formed
/// and simply wrong. In the other direction it leaves `\` and `"` untouched,
/// and either of those closes or breaks the literal — a backslash in a
/// Windows-shaped path, or an apostrophe-free `"` in an id, produced a
/// SyntaxError that took the whole inline script with it, including the mount.
///
/// `<` `>` `&` are escaped as their `\u00XX` forms for the reason
/// `scriptSafeJson` escapes them: the HTML tokenizer does not parse JavaScript,
/// so `</script` anywhere in the literal — including inside a string — ends the
/// element. The two line separators are escaped because they terminate a
/// JavaScript line while being legal in the source they came from.
let private jsStringLiteral (s: string) : string =
    let sb = StringBuilder()
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
        | c when int c = 0x2028 -> sb.Append "\\u2028" |> ignore
        | c when int c = 0x2029 -> sb.Append "\\u2029" |> ignore
        | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> sb.Append c |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

let private nonceAttr (nonce: string option) =
    match nonce with
    | Some n -> sprintf " nonce=\"%s\"" (htmlEscape n)
    | None -> ""

let private trimPrefix (prefix: string) =
    if prefix.EndsWith("/", StringComparison.Ordinal) then
        prefix.TrimEnd '/'
    else
        prefix

// ─── The emitted fragments ────────────────────────────────────────────────

/// The `<link>` for the reference stylesheet. Emit it in `<head>`.
let styleLink (prefix: string) : string =
    sprintf "<link rel=\"stylesheet\" href=\"%s/fuaran-reference.css\">" (htmlEscape (trimPrefix prefix))

/// The `<script>` for the browser renderer. Emit it before any `mount` call.
///
/// Not `async` and not `defer`: `mount` below is an inline script that calls
/// the global this tag defines, and both attributes would let the page reach
/// the call first. A host that wants deferred loading calls `mount` from its
/// own `DOMContentLoaded` handler.
let scriptTag (prefix: string) : string =
    sprintf "<script src=\"%s/fuaran-renderer.js\"></script>" (htmlEscape (trimPrefix prefix))

/// Both asset tags, in the order a document wants them.
let assetTags (prefix: string) : string =
    styleLink prefix + "\n" + scriptTag prefix

/// The development-time drift diagnostic, or `""`.
///
/// Emits an HTML comment AND a `console.warn`: the comment survives "view
/// source" on a page whose console nobody opened, and the warn reaches a
/// developer who never views source. Both name the repair.
let private diagnostics (options: MountOptions) (authoringVocabulary: string) : string =
    if not options.Development then
        ""
    else
        let lines =
            match Assets.fingerprint () with
            | Error message -> [ message ]
            | Ok fp -> Fingerprint.check authoringVocabulary fp |> List.map Fingerprint.describe

        if List.isEmpty lines then
            ""
        else
            let comment =
                lines
                // A `--` inside an HTML comment is not legal and some parsers
                // close the comment at it; the messages carry em-dashes, not
                // double hyphens, but a version string is host-supplied text.
                |> List.map (fun l -> "  Fuaran renderer: " + l.Replace("--", "––"))
                |> String.concat "\n"

            let warns =
                lines
                // Through the same literal escaper as everything else in the
                // script. These messages carry host-supplied version strings
                // and file paths, and the pair of `Replace` calls this used to
                // do left `<` and the line separators alone — enough for one
                // drift warning to close the script element it sits in.
                |> List.map (fun l -> sprintf "console.warn(\"[Fuaran] \" + %s);" (jsStringLiteral l))
                |> String.concat "\n"

            sprintf "<!--\n%s\n-->\n<script%s>\n%s\n</script>\n" comment (nonceAttr options.Nonce) warns

/// The mount element plus the inline script that renders `wireJson` into it.
///
/// `wireJson` is the canonical wire JSON of the tree — produced by
/// `CanonicalJson.encodeNodeForTransport`, which REFUSES a tree whose
/// interaction would not survive the trip, rather than by `encodeNode`, whose
/// closure-blindness is deliberate for the hash chain and silent here.
///
/// `authoringVocabulary` is `Fuaran.UI.Renderer.Theme.vocabularyFingerprint`.
/// Passed in rather than read here so this package does not take a dependency
/// on the renderer assembly just to name a constant, and so a test can drive
/// the mismatch path.
/// The handle a mount is reachable through is keyed BY ELEMENT ID
/// (`window.fuaranHandles[id]`), not a single `window.fuaranHandle`. Two trees
/// on one page is an ordinary composition — a dashboard beside a filter panel —
/// and with one global the second mount silently replaced the first's handle,
/// leaving the first tree rendered, interactive, and unreachable from host
/// script.
let mount (options: MountOptions) (authoringVocabulary: string) (wireJson: string) : string =
    let headers =
        match options.AntiforgeryHeader with
        | None -> "{ 'Content-Type': 'application/json' }"
        | Some(name, value) ->
            sprintf "{ 'Content-Type': 'application/json', %s: %s }" (jsStringLiteral name) (jsStringLiteral value)

    let notify =
        match options.NotifyEndpoint with
        | None -> ""
        | Some endpoint ->
            // Both failure arms reach `report`, which is the snippet's own
            // `onError`. A bare `fetch(...)` reported neither: a rejected
            // promise (offline, DNS, CORS, a connection reset) surfaced as an
            // unhandled rejection somewhere else entirely, and a 4xx/5xx did
            // not reject at all — `fetch` resolves for any response that
            // arrived — so a notify the host REFUSED looked, from the page,
            // exactly like one it had accepted.
            sprintf
                """
    onNotify: function (channel, payload) {
      fetch(%s, {
        method: 'POST',
        headers: %s,
        body: JSON.stringify({ channel: channel, payload: payload })
      }).then(function (response) {
        if (!response.ok) {
          report('Notify(' + channel + ') was refused by the host: ' + response.status + ' ' + response.statusText);
        }
      }).catch(function (error) {
        report('Notify(' + channel + ') could not be delivered: ' + error);
      });
    },"""
                (jsStringLiteral endpoint)
                headers

    let script =
        sprintf
            """<script type="application/json" id="%s-tree">%s</script>
<script%s>
(function () {
  var id = %s;
  var report = function (message) { console.error('[Fuaran] ' + message); };
  var el = document.getElementById(id);
  var json = document.getElementById(id + '-tree').textContent;
  window.fuaranHandles = window.fuaranHandles || {};
  window.fuaranHandles[id] = FuaranRenderer.mount(el, json, {%s
    onError: report
  });
})();
</script>"""
            (htmlEscape options.ElementId)
            (scriptSafeJson wireJson)
            (nonceAttr options.Nonce)
            (jsStringLiteral options.ElementId)
            notify

    diagnostics options authoringVocabulary
    + sprintf "<div id=\"%s\"></div>\n" (htmlEscape options.ElementId)
    + script
