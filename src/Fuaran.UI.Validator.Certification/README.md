# Fuaran.UI.Validator.Certification

Recipe / fragment certification for Fuaran UI. A parameterised fragment is a function of its
declared holes; `RecipeCertification.certifyFragment` proves it emits a validator-conformant
`Node` tree for every sampled or symbolic binding of those holes, by driving
`Fuaran.Core.Conformance`'s `verifyFunction` with the runtime pre-emit rule family
(`PreEmitValidate`) as the validity oracle. A certified fragment is a correct-by-construction
template; the verdict also carries the fragment's declared effect class, because certification is
structural and never a claim about output quality or determinism.

The module path is `Fuaran.UI.Validator.RecipeCertification`. It lived inside the build-time
validator executable (`Fuaran.UI.Validator`) until 0.95.0 (Phase 2058) and now lives in this
library, which that executable references; using it no longer pulls in FSharp.Compiler.Service.

Dependencies: `FSharp.Core`, `Fuaran.UI`, `Fuaran.UI.Core.Function`, `Fuaran.Core.Conformance`.
.NET-only (ships no Fable sources).

Apache-2.0 licensed.
