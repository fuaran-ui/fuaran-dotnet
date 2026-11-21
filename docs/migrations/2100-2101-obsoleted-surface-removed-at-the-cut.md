# Migration — the surface obsoleted by Phases 2100 and 2101 is removed in 0.105.0

Phases 2100 and 2101 marked six public names `[<Obsolete>]` and stated that they are removed in the announced
breaking cut, which is `0.105.0`. This note says what replaces each one. The cut's index is
[`0.105.0-breaking-cut.md`](0.105.0-breaking-cut.md).

## What goes

| Package | Removed | Use instead |
|---|---|---|
| `Fuaran.UI` | `Fuaran.UI.Bounded` (`NonEmptyString`, `BoundedString`, `BoundedInt`, `Fraction`) | Nothing in the vocabulary carries these types, and nothing in the tier reads them. Validate the scalar where it enters your own code. |
| `Fuaran.UI.AiTools` | `Fidelity.all` | `Fuaran.UI.RenderFidelity.allIntrinsics` |
| `Fuaran.UI.AiTools` | `Fidelity.kinds` | `Fuaran.UI.RenderFidelity.announcedKinds` |
| `Fuaran.UI.AiTools` | `Fidelity.isAnnounced` | `Fuaran.UI.RenderFidelity.announcesIntrinsically` |
| `Fuaran.UI.Renderer` | `Render.emitRenderFailure sink nodeId kindName errorMessage source` | `Render.emitRenderFailureWithContext sink Map.empty nodeId kindName errorMessage source` |
| `Fuaran.UI.Renderer` | `Render.accessibilityAttributes` | `Fuaran.UI.Renderer.Accessibility.accessibilityAttributes`, the projection it re-exported. The arguments are the same. |

The remaining `Fidelity` members (`forKind`, `ofNode`, `rolesEmitted`, `liveRegionsEmitted`, `describe`) stay.

## Who has to act

Anyone whose code names one of the six. The obsoletions and the removals both ship in `0.105.0`, so no released
version shows the `[<Obsolete>]` messages to a consumer. After the raise, each call site is an FS0039 error (the name
is not defined), and the table above gives the replacement. A build from source between the two steps shows FS0044
warnings that name each replacement.

Every replacement except `Fuaran.UI.Bounded` returns the same value as the name it replaces. Nothing replaces
`Bounded`: it was an opt-in kit that no record, wire field or renderer adopted.

## What does not change

No wire byte moves and no other signature moves. The two behaviour corrections that Phase 2100 shipped
(`Fragment.curry`'s refusal text for a repeat hole or an unknown name, and the membership of
`WireSurvivability.steerable`) shipped with that phase, not with the cut. `STABILITY.md` records them under Phase
2100's block in the `0.105.0` section.

## Verifying

Build against `0.105.0` with warnings as errors. A clean build means no removed name is referenced.

## Rollback

Pin the previous release. Every replacement except the absent `Bounded` exists there too, so code that has moved builds against both.
