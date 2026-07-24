# Fuaran.UI.FastPath

A **signature-searchable pattern bank** for Fuaran UI — composition by *lookup*, not by generation.

Given the holes you can fill (the context you have) and/or the kind of node you want to produce, ask
the bank which known patterns you can run:

```fsharp
open Fuaran.UI

let bank = SeedCatalogue.defaultBank

// "What can I build that produces a Box, given I can supply two labelled metrics?"
let q =
    FastPath.query
        [ FastPath.textHole "m0.label" "first label"
          FastPath.numberHole "m0.value" "first value" 0 1_000_000
          FastPath.textHole "m1.label" "second label"
          FastPath.numberHole "m1.value" "second value" 0 1_000_000 ]
        (Some "Box")

let matches = FastPath.findRunnable q bank      // structural subsumption

// Instantiate a chosen pattern into a real Fuaran tree
match FastPath.tryPattern "dashboard-shell" bank with
| Some p ->
    let tree =
        FastPath.instantiate
            p
            (Map [ "title", "Q3"; "m0.label", "Revenue"; "m0.value", "128000" ])
    // render `tree` with any conformant host
| None -> ()
```

The search is the real `Fuaran.Core.FunctionRegistry.findBySignature` — deterministic, total, and
**in-memory**: no model call and no server. The whole surface is `FSharp.Core` +
`Fuaran.Core.Function` only, so the same lookup runs identically in the browser (via Fable) and on the
server, byte-for-byte.

## The seed catalogue

A curated, domain-neutral standard library of primitives (metric strips, KPI cards, dashboards,
heroes, callouts, sections, feature lists, empty/error states, CTA banners). A few are
**ComputeLayer-bound** — their value is a real `Fuaran.Core.DataFrame` transform pipeline over embedded
data, so a pattern's figure is computed client-side with no server.

The seed is the canonical "how to author a pattern" reference — extend the bank by registering your
own patterns into the same registry type (`FastPath.bank`).

Apache-2.0.
