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
//  WHAT THE POLICY IS. It MINTS. Its `createHTML` returns its input, because the
//  renderer's markup is already safe when it gets here (Phase 2069): each
//  producer makes its own output safe, once, and runs identically on the server.
//  Markdown runs `Sanitize.sanitizeMarkdownHtml` inside `Markdown.toHtmlWithEgress`;
//  the drawing and chart SVG builders, MathML and the code highlighter escape by
//  construction; `Theme.toCss` and the KaTeX enhancement apply the floor to their
//  own output. It declares no `createScript` and no `createScriptURL`, so a caller
//  reaching for either through this policy gets a `TypeError` from the browser
//  rather than a value: the renderer mints markup and never script, and the policy
//  says so in the only way a policy can.
//
//  WHY NOT SANITISE HERE. A pass at this sink runs on the client only, so any
//  input it changed was a hydration mismatch and left SSR the less protected
//  tier, and it ran over markdown a second time on every render. `TrustedTypes`
//  therefore cannot change a byte: a browser with no Trusted Types support, a host
//  that sends no directive, the server renderer and the .NET build all carry the
//  same bytes as a browser that enforces. What the missing API costs is the
//  trusted WRAPPER, which is inert where nothing enforces it.
//
//  WHAT THIS DOES NOT COVER. A host-registered custom renderer returns its own
//  element and the renderer does not police its output (see `SANITIZATION.md`,
//  "Custom-renderer trust boundary"). If such a renderer reaches a raw-HTML sink
//  it must make its markup safe and mint its own trusted value, through this
//  policy or its own: minting vouches for the value and checks nothing. The policy
//  moves no trust boundary; it makes the boundaries the renderer already declared
//  enforceable by the browser instead of by review.
// ============================================================================

/// The Trusted Types policy name the client renderer creates and a host pins in
/// its `trusted-types` directive. Stable: it is part of the contract a host
/// configures against, so it is versioned like any other public surface.
[<Literal>]
let policyName = "fuaran-renderer"

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
                 createPolicy policyName id
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

/// Mint a value for a raw-HTML sink: `markup`, wrapped as `TrustedHTML` where the
/// browser offers the API and the host has named this policy. The caller vouches
/// for `markup`; this only mints it.
///
/// The return type is `string` because that is what both sinks take. Where the
/// policy exists the value is a `TrustedHTML` object travelling under that type,
/// which is exactly what the sink needs: React assigns
/// `dangerouslySetInnerHTML.__html` through without coercing it, so the object
/// reaches `innerHTML` intact and the browser accepts it. Coercing it to a real
/// string here would throw the trust away at the last step.
let html (markup: string) : string =
    let p = policy.Value

    if isNull p then markup else mintTrustedHtml p markup

#else

/// The .NET build has no DOM and no Trusted Types API, so `html` is the identity.
let html (markup: string) : string = markup

#endif
