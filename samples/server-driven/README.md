# samples/server-driven

A worked example of the **server-driven** client tier (Phase 152) — the third
way to make a Fuaran tree interactive (alongside the Fable client renderer and
the `hydrateRoot` hydration mount). The Elmish `update` loop runs on the
**server**; the browser runs one generic shim and patches the DOM in place.

Two pages, both server-driven, no client bundle:

- **`/` — a tabbed counter** (`App.fs`): `+1` / `-1` / `Reset` inside a Tabs node,
  plus an "Advanced" Disclosure. Clicking a button, a tab header, or the
  disclosure summary round-trips through the server `update` and the changed
  region patches in with no full re-render and no flash. Tabs + Disclosure
  exercise the shim's click payload bridges (`payload.index` from `data-tab-index`;
  `payload.open` from the `<details>` state).
- **`/form` — a buffered form** (`Form.fs`, the Phase 152 **form policy** worked
  example): a `Binding.Local` display name + a 0..120 ranged age. Each field is
  **client-buffered** — the shim does **not** round-trip keystrokes; the live DOM
  value is the buffer (policy (b)). The server sees the values only on a flush:
  - **Save profile** (submit) — the shim harvests every buffered field; the
    server validates (Phase 156 declared `Required` / range enforcement), then
    flushes each field's `OnCommit` folded with the form's `OnSubmit` in one diff.
    Only the "Saved profile" card patches — the form keeps its typed values,
    focus, and scroll.
  - **Apply name** (an `Action.CommitLocal "name"` button) — commits just the
    name buffer without a full submit (the `OnCommitAction` flush).

**No client bundle, no Fable compile** — `App.fs` / `Form.fs` are plain F# that
run on the server.

## Run

```powershell
dotnet run --project samples/server-driven/ServerDriven.Sample.fsproj
```

Open <http://localhost:14050> (counter) and <http://localhost:14050/form> (form).

## How it fits together

- **`App.fs`** — the app: `Model = { Count; Tab; Advanced }`, `update`,
  `view : Model -> Node<obj>`. Msg is boxed at the dispatch site so the tree
  renders directly through the server renderer. The whole closure space runs
  server-side (the server-closure win).
- **`Program.fs`** — the ASP.NET host:
  - `GET /` server-renders `view initial` to HTML
    (`Renderer.Server.Render.render`) — the SSR first paint the shim patches
    onto. Every node carries `data-fuaran-node-id`.
  - `GET /fuaran-live-patch.js` + `GET /fuaran-reference.css` — the generic shim
    and the reference stylesheet, copied to `wwwroot` and served statically.
  - `mapFuaranLive` (from `Fuaran.UI.ServerDriven.AspNetCore`) wires
    `GET /live/stream` (SSE push) + `POST /live/event` (client→server). Each
    connection gets a fresh `LiveSession` starting at the initial model —
    matching the SSR baseline so the first diff is exact.

## The loop, observed

Open the SSE stream, click `#inc`, and the server streams back exactly one
targeted patch:

```
id: 1
event: patch
data: {"seq":1,"patches":[{"kind":"ReplaceFragment","nodeId":"count",
       "html":"<div id=\"count\" ...><p>Count: <strong>1</strong></p>...</div>"}],
       "effects":[]}
```

The diff localised to the changed `count` node; the lowering re-rendered just
that node; the shim swaps it in place.

The `/form` submit is the same shape — a `Save profile` POST carrying the
harvested buffers (`{"name":"Ada","age":42}`) streams back one frame that clears
the field-error attrs and re-renders **only** the saved card (not the form):

```
data: {"seq":1,"patches":[
  {"kind":"RemoveAttr","nodeId":"name","name":"data-fuaran-field-error"},
  {"kind":"RemoveAttr","nodeId":"age","name":"data-fuaran-field-error"},
  {"kind":"ReplaceFragment","nodeId":"saved-body","html":"...Hello <strong>Ada</strong> — age <strong>42</strong>..."}],
  "effects":[]}
```

See `fuaran-dotnet/docs/SERVER_DRIVEN.md` (the "Form / input / local-state policy"
section) for the buffer-flush protocol + the round-trip-vs-buffer dividing line,
plus the full architecture + transport-choice analysis.
