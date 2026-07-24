# Fuaran.UI

Fuaran — Diametrical Ltd's AI-emittable F# UI language. Defines the §4b canonical type contract (`Types.fs`), the `Defaults.X` typed default records, and the smart-constructor surface (`Fuaran.X "id" { ... }`) authors use to build UI trees. Standalone: depends on `Fuaran.Core.{Wire,Column,DataFrame}` + `FSharp.Core` only — all Apache-2.0 — no platform-SDK dependency — the §4l down-shift portability story (Fuaran → Feliz codemod escape valve) requires `Fuaran.UI` be usable standalone.

Consumed by `Fuaran.UI.Renderer` (Fable + React + Feliz rendering) and by downstream AI-driving-the-UI runtime tiers shipped as separate package families. The canonical language design specification (the §4b type contract, the wire format, the introspection surface) is maintained separately.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
