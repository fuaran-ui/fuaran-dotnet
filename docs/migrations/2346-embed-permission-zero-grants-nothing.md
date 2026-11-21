# Migration — the C# `EmbedPermission` zero value grants nothing (Phase 2346)

## What changed

`Fuaran.UI.CSharp.EmbedPermission` now has explicit values, and its zero value is a new member, `None`, which
grants nothing:

| Member | Before | After |
|---|---|---|
| `None` | — | `0` (new) |
| `AllowScripts` | `0` | `1` |
| `AllowSameOrigin` | `1` | `2` |
| `AllowForms` | `2` | `3` |
| `AllowFullscreen` | `3` | `4` |

Before this release `AllowScripts` was the first member, so `default(EmbedPermission)`, an uninitialised field or
array element, and `(EmbedPermission)0` were all a script grant, and nothing on the authoring path could tell them
apart from a grant someone wrote. Now each of them is `None`, and `Fuaran.Embed` drops `None` from the permission
list: the wire's no-grant form is absence, so an all-`None` list emits exactly what an empty list emits. The wire
format, the F# `EmbedPermission` union and the Visual Basic dialect's spelling of the four relaxations do not change.

## Who has to act

**Source that names the members (`EmbedPermission.AllowForms`, …) needs no edit — recompile it.** The constants are
baked into your assembly at compile time, so a recompile against this release picks up the new values.

**Act if any of these apply:**

1. **A binary built against an older `Fuaran.UI.CSharp` and run against this one without a recompile** (a plug-in
   loaded beside a newer host, say). The old constants are now off by one: an old `AllowScripts` (`0`) reads as
   `None` and grants nothing, an old `AllowSameOrigin` (`1`) reads as `AllowScripts`, an old `AllowForms` (`2`) as
   `AllowSameOrigin`, and an old `AllowFullscreen` (`3`) as `AllowForms`. Two of those GRANT something the author did
   not write. Recompile every assembly that references the enum, together, against this release; do not run a mixed
   set.
2. **Persisted integers.** If you stored `(int)permission` (a database column, a config file, a cache), the stored
   numbers now mean different members. Store the name (`permission.ToString()`) and parse it with
   `Enum.Parse<EmbedPermission>`, or re-map stored values once with the table above.
3. **Code that relied on the zero value being `AllowScripts`** — `new EmbedPermission[n]` filled and then partly
   overwritten, or `default` used as "scripts". That code granted script by accident before; it now grants nothing.
   Write `EmbedPermission.AllowScripts` where script is meant.
4. **Exhaustive `switch` statements over `EmbedPermission`** gain a `None` case to handle (or a compiler warning about
   it, where you enable that analysis).

## Verifying

`Fuaran.Embed(new EmbedOptions { …, Permissions = new EmbedPermission[3] })` encodes with no `permissions` member,
exactly as `Permissions = []` does. The C# conformance suite pins `default(EmbedPermission)` and
`(EmbedPermission)0` to no grant.

## Rollback

Pin the previous `Fuaran.UI.CSharp` release and recompile; nothing on the wire depends on this change.
