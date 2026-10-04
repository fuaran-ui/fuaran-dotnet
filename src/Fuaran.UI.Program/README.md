# Fuaran.UI.Program

The **UI adapter** for the domain-generic bounded program core. Since 0.6.0 the core packages
(`Fuaran.Program.Bounded`, `.Runtime`, `.Server`) are written over a domain's witness
(`ProgramWitness`) and reference no UI-tier package. This package is the UI tier's instantiation of
that core. Since fuaran#2012 it is released from the UI tier's repository, in that tier's version
line (0.91.0 onward; 0.7.1 was its last release beside the core, under the program family's id), and
it depends on the core's released packages rather than building beside them. Since fuaran#2022 it
carries a UI-family id and namespace; `STABILITY.md` names the id it replaces.

## What it carries

- **The UI witness** (`UiWitness`) — the UI tier's action union, node tree, binding store, tree-ops
  and client effects, seen through the core's witness contract.
- **The pre-0.6.0 names as aliases** (`Aliases.fs`) — `BoundedStore`,
  `BoundedActions.runBoundedAction`, `Resolve.resolveTree` over a UI tree, and the rest, as closed
  aliases and partially applied modules over the UI witness.
- **The UI transport loop** — the bounded driver (`BoundedDriver`), its channel glue
  (`BoundedConnection`) and the client runtime (`Program`), plus the client effects' destination map
  against the core's egress seam (`ClientEffects`).

It is Fable-clean: its sources ship under `fable/` in the package for a browser placement. The
server placement's half is `Fuaran.UI.Program.Server`.

## Using it

Reference `Fuaran.UI.Program` beside the core packages, and open its namespace AFTER the
`Fuaran.Program.*` namespaces:

```fsharp
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime
open Fuaran.UI.Program // last: its modules shadow the generic ones of the same name
```

A module here and a core module of the same name are searched latest-opened first, and the names
here are the core's own names applied to the UI witness, so the order is what makes an existing call
site resolve to the UI instantiation.

A state binding with no declared default, at a slot nothing has written, is UNRESOLVED (the UI
tier's resolver, which this adapter follows): a clipboard write of it is refused, and a `SetState`
taking its `valueFrom` from it writes nothing. Give such a binding a default. `STABILITY.md` at the
repository root records the version history.

Apache-2.0 licensed.
