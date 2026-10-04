# Fuaran.UI.Program.Server

The **UI adapter for the server placement** of the domain-generic bounded program core. The core
server package (`Fuaran.Program.Server`) is written over a domain's witness and references no
UI-tier package; this package is the UI tier's instantiation of it. Since fuaran#2012 it is released
from the UI tier's repository, in that tier's version line (0.91.0 onward; 0.7.1 was its last release
beside the core, under the program family's id), against the core's released packages. Since
fuaran#2022 it carries a UI-family id and namespace; `STABILITY.md` names the id it replaces.

## What it carries

- **The pre-0.6.0 server names as aliases** (`ServerAliases.fs`) over the UI witness from
  `Fuaran.UI.Program`.
- **The UI event step** (`ServerSteps.fs`) — the tier's validation gate in front of the core's
  `ServerSession.dispatchWith`, for the server session, the durable interpreter and the operator
  controls. The core session starts at an action already chosen; this is what turns a UI event into
  one.

It is .NET only, like the server placement itself; the Fable-clean half is `Fuaran.UI.Program`.

## Using it

Reference it beside `Fuaran.Program.Server` and `Fuaran.UI.Program`, and open its namespace AFTER the
core's:

```fsharp
open Fuaran.Program.Bounded
open Fuaran.Program.Server
open Fuaran.UI.Program
open Fuaran.UI.Program.Server // last: its modules shadow the generic ones of the same name
```

Apache-2.0 licensed.
