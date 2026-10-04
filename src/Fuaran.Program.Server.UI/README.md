# Fuaran.Program.Server.UI

The **UI adapter for the server placement** of the domain-generic bounded program core. The core
server package (`Fuaran.Program.Server`) is written over a domain's witness and references no
UI-tier package; this package is the UI tier's instantiation of it. Since fuaran#2012 it is released
from the UI tier's repository, in that tier's version line (0.91.0 onward; 0.7.1 was its last release
beside the core), against the core's released packages.

## What it carries

- **The pre-0.6.0 server names as aliases** (`ServerAliases.fs`) over the UI witness from
  `Fuaran.Program.UI`.
- **The UI event step** (`ServerSteps.fs`) — the tier's validation gate in front of the core's
  `ServerSession.dispatchWith`, for the server session, the durable interpreter and the operator
  controls. The core session starts at an action already chosen; this is what turns a UI event into
  one.

It is .NET only, like the server placement itself; the Fable-clean half is `Fuaran.Program.UI`.

## Using it

Reference it beside `Fuaran.Program.Server` and `Fuaran.Program.UI`, and open its namespace AFTER the
core's:

```fsharp
open Fuaran.Program.Bounded
open Fuaran.Program.Server
open Fuaran.Program.UI
open Fuaran.Program.Server.UI // last: its modules shadow the generic ones of the same name
```

Apache-2.0 licensed.
