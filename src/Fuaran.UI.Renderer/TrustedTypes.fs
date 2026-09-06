module Fuaran.UI.Renderer.TrustedTypes

// `isNull` on an interop handle: the policy is a JS object this module holds as
// `obj`, and F# 10's nullness checker rejects the test on a non-nullable
// reference type (FS3261). Same posture, and same reason, as the file-scoped
// suppression in `Fuaran.UI.Renderer.Sanitize`: the property that would settle it
// belongs to the ENTRY project under Fable, so it has to travel with the source.
#nowarn "3261"

// ============================================================================
//  Phase 1546 - the renderer's Trusted Types policy.
//
//  Every raw-HTML seam in the client renderer assigns a string to a DOM sink:
//  React's `dangerouslySetInnerHTML`, or `Element.innerHTML` on the KaTeX
//  enhancement path. A host that sends
//  `Content-Security-Policy: require-trusted-types-for 'script'` makes the
//  browser refuse a plain string at those sinks, and accept only a `TrustedHTML`
//  minted by a named policy the same header lists. This module is that policy.
//
//  The policy is named `fuaran-renderer`. A host pins it beside the require
//  directive:
//
//      Content-Security-Policy: require-trusted-types-for 'script';
//                               trusted-types fuaran-renderer
//
//  WHAT THE POLICY IS. Its `createHTML` is `Sanitize.sanitizeMarkdownHtml`, the
//  same substring floor the markdown seam has always applied, and nothing else.
//  It declares no `createScript` and no `createScriptURL`, so a caller reaching
//  for either through this policy gets a `TypeError` from the browser rather than
//  a value: the renderer mints markup and never script, and the policy says so in
//  the only way a policy can.
//
//  WHY THE FLOOR RUNS EVEN WITHOUT THE API. `html` applies `createHtml` on every
//  path, so a browser with no Trusted Types support, a host that sends no
//  directive, the server renderer and the .NET build all produce the SAME BYTES
//  as a browser that does enforce. That uniformity is not a nicety. The server
//  renderer emits these payloads into the SSR document and the client renderer
//  hydrates over them, so a floor applied on one side only would present as a
//  React hydration mismatch on every seam whose bytes it changed. What the
//  missing API costs is the trusted WRAPPER, which is inert where nothing
//  enforces it, and that is the whole of the no-op fallback.
//
//  WHY THAT IS SAFE FOR SVG, MathML AND CSS. The floor was written for markdown
//  and now runs over drawing SVG, chart SVG, MathML and the theme stylesheet as
//  well. It is safe there because each of those emitters escapes by construction
//  already, so the floor finds nothing to remove: the invariance is pinned as a
//  test (`TrustedTypesTests`) rather than assumed, and that test is what goes red
//  if a payload ever grows a shape the sweep would rewrite. One such shape was
//  found and fixed in the same change: the sweep matched a dangerous element name
//  as a bare prefix, so the drawing builder's `<metadata>` provenance element read
//  as `<meta>` and lost its opening tag. `Sanitize` now requires a tag-name
//  boundary.
//
//  WHAT THIS DOES NOT COVER. A host-registered custom renderer returns its own
//  element and the renderer does not police its output (see `SANITIZATION.md`,
//  "Custom-renderer trust boundary"). If such a renderer reaches a raw-HTML sink
//  it must mint its own trusted value, through this policy or its own. The policy
//  moves no trust boundary; it makes the boundaries the renderer already declared
//  enforceable by the browser instead of by review.
// ============================================================================

/// The Trusted Types policy name the client renderer creates and a host pins in
/// its `trusted-types` directive. Stable: it is part of the contract a host
/// configures against, so it is versioned like any other public surface.
[<Literal>]
let policyName = "fuaran-renderer"

/// The policy's `createHTML` body, callable directly so the fallback path and the
/// enforced path cannot diverge. The floor is `Sanitize.sanitizeMarkdownHtml`,
/// which is defence in depth over input that is already escaped by construction:
/// see that binding's own doc comment for the precondition it rests on.
let createHtml (markup: string) : string = Sanitize.sanitizeMarkdownHtml markup

#if FABLE_COMPILER

open Fable.Core

[<Emit("typeof window !== 'undefined' && !!window.trustedTypes && typeof window.trustedTypes.createPolicy === 'function'")>]
let private trustedTypesAvailable () : bool = jsNative

[<Emit("window.trustedTypes.createPolicy($0, { createHTML: $1 })")>]
let private createPolicy (name: string) (createHtmlRule: string -> string) : obj = jsNative

[<Emit("$0.createHTML($1)")>]
let private mintTrustedHtml (policy: obj) (markup: string) : string = jsNative

[<Emit("typeof console !== 'undefined' && console.warn($0)")>]
let private warn (message: string) : unit = jsNative

/// The policy handle, created once on first use.
///
/// Memoised because a repeat `createPolicy` under the same name THROWS unless the
/// host's directive carries `'allow-duplicates'`, so this is correctness rather
/// than caching. `Lazy` rather than a module-level mutable for the same reason it
/// is preferred elsewhere in this tier: the initialisation runs once, under the
/// binding that owns it, with no second flag to keep in step.
let private policy: Lazy<obj> =
    lazy
        (if not (trustedTypesAvailable ()) then
             null
         else
             try
                 createPolicy policyName createHtml
             with _ ->
                 // The host enforces Trusted Types but its `trusted-types`
                 // directive does not name this policy, so the browser refused to
                 // create it. Say so once, naming the policy and the directive:
                 // the sinks below will then be refused by the browser, and a
                 // silent fallback would leave a blank page with no cause. The
                 // fallback is still taken, because throwing here would break a
                 // host that is merely misconfigured.
                 warn (
                     "Fuaran renderer: the Trusted Types policy '"
                     + policyName
                     + "' could not be created. Add it to the page's `trusted-types` "
                     + "CSP directive, beside `require-trusted-types-for 'script'`."
                 )

                 null)

/// Mint a value for a raw-HTML sink: the sanitisation floor, wrapped as
/// `TrustedHTML` where the browser offers the API and the host has named this
/// policy.
///
/// The return type is `string` because that is what both sinks take. Where the
/// policy exists the value is a `TrustedHTML` object travelling under that type,
/// which is exactly what the sink needs: React assigns
/// `dangerouslySetInnerHTML.__html` through without coercing it, so the object
/// reaches `innerHTML` intact and the browser accepts it. Coercing it to a real
/// string here would throw the trust away at the last step.
let html (markup: string) : string =
    let p = policy.Value

    if isNull p then
        createHtml markup
    else
        mintTrustedHtml p markup

#else

/// The .NET build has no DOM and no Trusted Types API, so `html` is the floor
/// alone. Identical bytes to the Fable path: see the header's uniformity note.
let html (markup: string) : string = createHtml markup

#endif
