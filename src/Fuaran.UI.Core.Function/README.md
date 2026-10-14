# Fuaran.UI.Core.Function

The projection of a Fuaran UI fragment's typed holes and declared effect class onto the
`Fuaran.Core` function model:

- `Projection.toCoreSpace : HoleValueSpace -> Fuaran.Core.ValueSpace`
- `Projection.toCoreEffect : EffectClass -> Fuaran.Core.EffectClass`

The UI vocabulary and the Core one are structural mirrors. This package is the one place the
translation is written, so the AI-tool surface (`Fuaran.UI.AiTools`) and the build-time validator
(`Fuaran.UI.Validator`) cannot drift apart on it. It is deliberately small, and is kept small so
that neither of those packages widens the other's dependency closure.

Dependencies: `FSharp.Core`, `Fuaran.UI`, `Fuaran.Core.Function`. Fable-clean; sources ship under
`fable/` for Fable consumers.

Apache-2.0 licensed.
