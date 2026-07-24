# Phase 64 migration – `IFuaranRuntime` clipboard substrate

**Shipped:** 2026-05-29
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + `Fuaran.UI.Ops.JsonDecode` decoder + `Fuaran.UI.OpStream.Abstractions.CanonicalJson` encoder + `Fuaran.UI.Tests` + `Fuaran.UI.JsonDecode.Tests`.
**Stability impact:** Additive across every surface. No reordering, renaming, or signature changes to existing DU cases, smart-ctor entry points, decoder branches, or runtime substrate members. Pre-Phase-64 consumers see no behavioural change.

## What changes

### 1. `Action<'Msg>.WriteToClipboard` (additive DU case)

`Fuaran.UI/Types.fs` adds:

```fsharp
type Action<'Msg> =
    | ...
    | WriteToClipboard of text: string           // NEW
```

The payload is a literal `string`. A bound-payload variant (`WriteToClipboard of Binding<string>`) is out of scope – every shipped consumer today reaches for the literal shape (the pilot app's copy-link button carries a pre-computed `shareUrl: string` into the click handler). If a future caller needs binding-time resolution we add the bound variant additively then.

### 2. `IFuaranRuntime.WriteToClipboard` (additive substrate member)

`Fuaran.UI.Renderer/Runtime.fs`:

```fsharp
type IFuaranRuntime =
    ...
    abstract WriteToClipboard: text: string -> unit
```

- `DiagnosticRuntime` (the .NET-side default + bootstrap pre-runtime-wiring placeholder): emits `eprintfn` per the same shape used for `Call` / `Notify` / `Navigate` / `SetState` / `InvokeAiTool` substrate-missing diagnostics.
- `BrowserRuntime` (Fable-only): wraps `navigator.clipboard.writeText` via an `[<Emit>]` shim. Falls back to a `document.execCommand("copy")` round-trip against a hidden `<textarea>` when the async-clipboard API is unavailable (non-secure contexts, older browsers). Failures route through `console.warn` – typed dispatch stays synchronous and fire-and-forget; the caller chains a follow-on `Action.dispatch` if it needs an explicit "I just copied" message in the model.
- `MutableRuntime` (the .NET-side Custom-renderer-aware shape): delegates to `diagnostic.WriteToClipboard`.

Hosts that need to override (electron `clipboard` IPC bridge, server-side log capture, test mocks) implement the member directly. Call sites are untouched.

### 3. Renderer `runAction` arm

`Fuaran.UI.Renderer/Render.fs` `runAction`:

```fsharp
| Action.WriteToClipboard text -> ctx.Runtime.WriteToClipboard(text)
```

`containsUnwiredAction` returns `false` for the case – the substrate's failure mode is intrinsic to the host (browser clipboard-permission UX), not the "no substrate wired" shape the `fuaran-button-unwired` tooltip is meant to flag. Hosts that want a visual hint when no clipboard substrate is wired should override `IFuaranRuntime.Warn` and emit one there.

### 4. Wire shape (forward-coupling)

The canonical-JSON wire shape for `Action.WriteToClipboard "Hello"`:

```json
{ "$type": "WriteToClipboard", "text": "Hello" }
```

`Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeAction` emits this shape; `Fuaran.UI.Ops.JsonDecode.decodeAction` accepts it. The `text` field is required; missing or wrong-type fields raise `DecodeError.MISSING_FIELD` / `WRONG_TYPE` per the existing decoder convention. **Wire-shape lock:** per `STABILITY.md` Phase 64, the `$type` discriminator + `text` field shape is part of the structural-decoder stability surface – JsonDecode breakage on either is a major-version bump from `1.0.0` onward.

### 5. Catalog page + Expecto fixture

- `samples/catalog/Clipboard.fs` – Elmish app mounted at `?clipboard=1` rendering a single Fuaran.button whose `OnClick = Action.Chain [ Action.WriteToClipboard "Hello from Fuaran"; Action.dispatch (CopiedSentinel ...) ]`. The mirror panel echoes the dispatched payload so a Playwright spec can assert the chain fired even when the clipboard-permission UX blocks the real write.
- `src/Fuaran.UI.JsonDecode.Tests/Fixtures.fs` – new `buttonClipboard` fixture for the Node round-trip suite (covers the encoder/decoder forward-coupling on the new DU case).
- `src/Fuaran.UI.Tests/ClipboardActionTests.fs` – typed-surface tests confirming the DU case carries its payload, composes inside `Action.Chain`, and routes through `DiagnosticRuntime.WriteToClipboard` without throwing.

## Migration recipe – `CopyLinkButton`-style consumer

Pre-Phase-64 shape (the pilot app's copy-link button):

```fsharp
// Feliz / hand-rolled JS interop
Html.button
    [ prop.onClick (fun _ ->
        Browser.Navigator.clipboard.writeText shareUrl |> Promise.start
        dispatch ClipboardCopied)
      prop.text "Copy link" ]
```

Phase 64 shape:

```fsharp
Fuaran.button
    "copy-link-button"
    { Defaults.button<Msg> with
        Label = TextSource.Literal "Copy link"
        Variant = ButtonVariant.Primary
        OnClick =
            Action.Chain
                [ Action.WriteToClipboard shareUrl
                  Action.dispatch ClipboardCopied ] }
```

Net: zero JS interop in the consumer's `update` handler, clipboard intent now expressible through the typed surface, and the host can override `IFuaranRuntime.WriteToClipboard` for a non-browser dispatch path without touching call sites.

## Anti-patterns deliberately avoided

- **No `Action.ReadFromClipboard` pairing.** Reading clipboard has different security implications (user-grant-required in most browsers) and a different return-channel shape – track separately if/when needed.
- **No async semantics on the typed dispatch.** Clipboard write IS async at the browser level but the typed surface stays synchronous-dispatch. Failure is signalled via a follow-on `Action.Notify` on a `clipboard-result` channel if the consumer needs explicit feedback. Adding `Async<unit>` to `Action.*` values would propagate complexity across every DU consumer.
- **No `Binding<string>` payload variant in this phase.** Every shipped use case carries a literal string today; the bound variant is a follow-on if demand materialises.
