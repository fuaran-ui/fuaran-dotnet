# Phase 191 – `Fuaran.UI.Ops` made Fable-portable (in-place)

**Owner repo:** fuaran · **Shipped:** 2026-06-16 (`fuaran@7547072`, `@8af04a5`)

## What changed

`Fuaran.UI.Ops` – the canonical decode + apply engine – now compiles **and
coerces correctly** under `dotnet fable`, so a Fable browser host can run the
local `decode → apply → setState` loop. This is the substrate the F# half of
[Phase 90](../in-page-introspection-repl.md)'s `window.__fuaran.apply(op)`
needed.

No package split was needed. Running `dotnet fable` showed the package was
**not** wholesale Fable-hostile (`ErrorRender.fs` was already
`#if !FABLE_COMPILER`-gated with a Fable-safe mirror); the blockers were a small
set of localized sites, fixed in place. `.NET` behaviour is byte-identical
throughout – every change is `#if`-fenced or message-only.

### 1. Exception filters → `isCastMismatch` (`fuaran@47a4b19`, the down payment)

Fable cannot type-test exceptions (`with :? System.InvalidCastException` is a
compile error). The 15 `Apply.fs` cast-mismatch handlers now use
`with ex when isCastMismatch ex`, an `#if`-gated predicate: `.NET` narrows to
`InvalidCastException` exactly as before; Fable accepts any (its `unbox` never
throws, so the guarded recovery is dead there anyway). Also replaced three
`v.GetType().FullName` coercion messages with `%A` (Fable resolves types only
at compile time).

### 2. `Coerce.objToJson` Map detection (`fuaran@7547072`)

The `:? Map<string, obj>` type-test was the single remaining compile blocker.
**Fable refuses *every* generic type-test** – `:? Map<string,obj>` *and*
`:? IDictionary<string,obj>` both fail with `Cannot type test (evals to
false)`; only `:? string/bool/float/int` and the non-generic
`:? System.Collections.IEnumerable` compile, and the latter can't distinguish a
decoded JObject-map from a JArray-list (both enumerate). The Fable branch
detects the map by comparing the **JS constructor reference** against an empty
F# map:

```fsharp
[<Fable.Core.Emit("$0 != null && $1 != null && $0.constructor === $1.constructor")>]
let private sameCtor (_a: obj) (_b: obj) : bool = Fable.Core.Util.jsNative

let private emptyStringMap: obj = box (Map.empty<string, obj>)
// … | _ when sameCtor v emptyStringMap -> (v :?> Map<string,obj>) |> …
```

Comparing constructor *references* (not `constructor.name` strings) is robust
under production minification and assumes nothing about Fable-internal field
names. Added a `Fable.Core` `PackageReference` to Ops (used only under the
Fable fence; inert on `.NET`).

### 3. `tryUnbox<'T>` → `coerceField` (`fuaran@7547072`) – the correctness fix

This is the subtle one. The old `tryUnbox<'T>` recovered from a failed `unbox`
by dispatching on `typeof<'T>` to the right `JsonDecode.Coerce.*` decoder. That
**compiled** under Fable but was **dead code** there: Fable's `unbox` is a
runtime no-op that never throws, so the `InvalidCastException`-guarded fallback
never fired – *and* Fable erases the generic arg, so `typeof<'T>` couldn't pick
the decoder anyway. The net effect under Fable: every coercion-needing wire
value (`TextSource`, `Binding<_>`, `CellFormat`, …) passed through `unbox`
**structurally un-coerced** – a silent mis-shaping bug.

The fix names the coercer **statically at the call site**:

```fsharp
let inline private coerceField (coerce: obj -> Result<'T, string>) (v: obj) : Result<'T, string> =
#if FABLE_COMPILER
    coerce v                                   // run unconditionally
#else
    try Ok(unbox<'T> v) with ex when isCastMismatch ex -> coerce v   // fast path, then fallback
#endif
```

All 53 `tryUnbox<T> v` call sites became `coerceField JsonDecode.Coerce.tryX v`.
Three call-site types had **no** `Coerce.try*` (they had relied on the `.NET`
direct-`unbox` fast path): added `tryString`, `tryStringOption`,
`tryBindingString`, each traced against the canonical encoder
(`Anchor.Rel/Target` encode as bare strings → `viaJsonOpt requireString`;
`Anchor.Href` as `encodeBinding<string>` → `viaJson decodeBindingString`;
`FragmentDecl/Ref.Name` + `GridLayout.TemplateColumns` sugar as bare strings).
Net `.NET` effect: identical for the 19 previously-dispatched types, and a
latent **fix** for wire-decoded `Href`/`Rel`/`Target` (which previously errored
on the fallback path).

## Verification

- **Fable compile:** `dotnet fable src/Fuaran.UI.Ops/Fuaran.UI.Ops.fsproj` →
  **0 errors** (was 1, was 19 before the down payment).
- **`.NET` tests:** 294 green (259 `JsonDecode.Tests` + 35 `Ops.Tests`).
- **Browser (live, `preview_*` harness, `fuaran@8af04a5`):** `samples/apply-demo`
  (a `Node<obj>` Elmish host registering `window.__fuaran` with a real
  `ApplyHandler`) ran `__fuaran.apply` on a `TextSource` field (`Label` →
  "Mutated!") **and** a `Binding<float>` field (`Source` → 99); each coerced to
  the correct typed value and re-rendered. Malformed JSON → `decodeFailed`
  envelope; unknown target → `rejected` envelope; both left the tree unchanged.

  > **Reproduction caveat:** build with `dotnet fable … --define DEBUG`. The
  > renderer's `DebugGlobal.compiledInDebug` is `#if DEBUG`, and Fable does
  > **not** inherit the entry project's `DEBUG` symbol into referenced projects,
  > so without the explicit `--define` the `window.__fuaran` registration is
  > dead-code-eliminated even from a debug-intent host.

## Follow-up (fast-follow, tracked in `roadmap/TIDY-UP.md`)

`samples/catalog` still ships an inline `JsonDecode.fs` (a narrow `Node<unit>`
decoder) + `JsonShape.fs`, written when Ops was believed non-Fable. With Ops now
Fable-clean, the catalog can drop the inline decoder for
`Ops.JsonDecode.decodeNode` and switch `JsonShape` to `CanonicalJson.encodeNode`
(pending confirmation that `Fuaran.UI.OpStream.Abstractions` is itself
Fable-clean). That dedup shifts the catalog's decoded tree from `Node<unit>` to
`Node<obj>` and needs its own catalog re-verification, so it is deferred. The
stale "Ops is .NET-only" comment in `Catalog.fsproj` was corrected in this phase.
