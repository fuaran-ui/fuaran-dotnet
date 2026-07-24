# Phase 136 migration – `Action.ReadFileBody` file-read portability seam

**Shipped:** 2026-06-06
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI.Renderer` runtime + `Fuaran.UI.Ops.JsonDecode` decoder + `Fuaran.UI.Ops.SchemaGen` + `Fuaran.UI.OpStream.Abstractions.CanonicalJson` encoder + `Fuaran.UI.Tests` + `Fuaran.UI.JsonDecode.Tests` + the TypeScript reference implementation (`@fuaran-ui/schema` + `@fuaran-ui/ops` + `@fuaran-ui/ui` + `@fuaran-ui/renderer`).
**Stability impact:** Additive across every surface. `FileSelection` gains a required `Ref` field, but the renderer is the only constructor of `FileSelection` (consumers receive it in `OnSelect`), so no consumer record literal breaks. New `Action` case + new `IFuaranRuntime` member follow the established pre-1.0 minor-add precedent.

## What changes

### 1. `Action<'Msg>.ReadFileBody` (additive DU case)

`Fuaran.UI/Types.fs`:

```fsharp
type Action<'Msg> =
    | ...
    | ReadFileBody of file: FileRef * encoding: FileReadEncoding * onRead: (string -> 'Msg)   // NEW
```

Mirrors `Call`'s return-channel shape: the renderer pre-wraps `onRead: string -> 'Msg` with `dispatch`, so the runtime stays 'Msg-generic and the read can be async at the host level while the typed surface stays dispatch-shaped. Smart-ctor: `Action.readFileBody file encoding onRead`.

### 2. `FileReadEncoding` + `FileRef` (additive top-level types)

```fsharp
type FileReadEncoding =
    | Text     // readAsText
    | Base64   // bytes, base64, no data-URL prefix
    | DataUrl  // full data:<mime>;base64,… string

type FileRef =
    { Id: string         // the ONLY part that serialises
      Handle: obj option } // the boxed browser File; None off-browser / on decoded trees
```

`FileSelection` gains `Ref: FileRef`. The blob stays browser-held on `Ref.Handle`; only `Ref.Id` ever serialises. Boxing the host blob behind `obj option` keeps `Fuaran.UI` standalone (FGP 2 – no `Browser.*` dependency in the typed-tree package).

### 3. `IFuaranRuntime.ReadFileBody` (additive substrate member)

```fsharp
type IFuaranRuntime =
    ...
    abstract ReadFileBody: file: FileRef * encoding: FileReadEncoding * onRead: (string -> unit) -> unit
```

- `DiagnosticRuntime` (.NET default): `eprintfn`, never calls back (no blob).
- `BrowserRuntime` (Fable-only): unboxes `file.Handle` to the browser `File` and reads via `FileReader` – `readAsText` for `Text`, `readAsDataURL` for `DataUrl`, and `readAsDataURL` with the `…;base64,` prefix stripped for `Base64`. Fires `onRead` from the load callback; failures route through `console.warn`.
- `MutableRuntime` + the `#else` .NET fallback delegate to `diagnostic`.

### 4. Default-deny by shape (FGP 3)

`ActionDescriptor` gains `ReadFileBody of fileId: string`; `runAction` consults `applyDispatchGate` before the host read, so a deny-by-shape host (e.g. the BYOK browser playground) can refuse file reads through the same seam it refuses `Call` / `Navigate` / `AiTool`.

### 5. Wire shape (forward-coupling)

The canonical-JSON wire shape for `Action.ReadFileBody({ Id = "workbook-upload:0"; Handle = … }, FileReadEncoding.Base64, …)`:

```json
{ "$type": "ReadFileBody", "encoding": "Base64", "fileRef": "workbook-upload:0", "onRead": "<closure>" }
```

`encoding` is a bare-string enum (`Text` / `Base64` / `DataUrl`). `fileRef` is the opaque id. The blob (`Handle`) never serialises; `onRead` is the closure sentinel (§4) and decodes to a no-op. The decoded `FileRef` carries `Handle = None`. Encoder + decoder + `SchemaGen` + corpus fixture (`nodes/btn-read-workbook.json`) + the TS encoder/decoder/renderer all moved in this change-set per `WIRE_FORMAT.md` §11; the cross-host conformance gate (`F# == corpus`, `TS == corpus`) is green.

## Migration recipe – file-ingesting consumer

Pre-Phase-136 shape (a document-conversion consumer's workbook ingest):

```fsharp
// Hand-rolled FileReader interop inside OnSelect / update
let reader = Browser.Dom.FileReader.Create()
reader.onload <- fun _ -> dispatch (WorkbookLoaded (string reader.result))
reader.readAsDataURL file
```

Phase 136 shape:

```fsharp
Fuaran.fileUpload
    "workbook-upload"
    { Defaults.fileUpload<Msg> with
        Accept = [ ".xlsx" ]
        OnSelect =
            fun selections ->
                match selections with
                | sel :: _ -> Action.readFileBody sel.Ref FileReadEncoding.Base64 WorkbookLoaded
                | [] -> Action.Chain [] }
```

Net: zero `FileReader` interop in the consumer; file-read intent is now AI-emittable through the typed surface, rides the same `IFuaranRuntime` substrate as clipboard / navigate, and a non-browser host can override `IFuaranRuntime.ReadFileBody` without touching call sites.

## Anti-patterns deliberately avoided

- **No `Async<unit>` on the typed dispatch.** File read IS async at the browser level but the typed surface stays callback-shaped (same posture `Call` takes), avoiding propagating async complexity across every `Action` consumer.
- **No blob on the wire.** Only the opaque token + encoding serialise; the blob is host-held. A decoded `ReadFileBody` cannot re-read (its `Handle` is `None`) – by design, since a decoded tree has no browser file context.
- **No second "read clipboard" pairing bundled in.** This phase is file-read only; clipboard-read has different security semantics and is tracked separately.
