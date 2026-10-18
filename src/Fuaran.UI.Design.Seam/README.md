# Fuaran.UI.Design.Seam

The design-provider seam of Fuaran UI: the records a design tool and the language exchange when
a module is designed in the tool and lowered to a Fuaran UI tree. The tool sits behind the seam;
it never sees a tree, and the lowering never sees the tool.

- `DesignSystemRef` — the design-system bundle a provider holds: provider id, optional project id,
  the bundle's content hash and the IDL version its cards came from.
- `DesignBrief` — the module's name and purpose, the data shapes it shows, the user tasks, the
  interactions it offers (a closed vocabulary: `Filter`, `Select`, `Drill`, `Compare`, `Export`,
  `Navigate`, each lowering to an existing action or binding), the viewports and density, the
  fragments it may use, and the `DesignSystemRef`.
- `DesignRequest` — the brief's hash, the reference, and a state (`Manual`, `Pending`, `Ready`).
- `DesignResult` — the screens as named files with content hashes, optional per-screen component
  inventories, the reference, and the result's content hash.

`IDesignProvider` (`PublishDesignSystem`, `RequestDesign`, `FetchDesign`) is the interface a
provider implements over them.

```fsharp
open Fuaran.UI.Design

match SeamCodec.decodeResult text with
| Ok result -> DesignResult.unknownComponents knownCardNames result // [] when every name is a card
| Error e -> failwithf "%s at %s: %s" (SeamCodec.SeamErrorCode.name e.Code) e.Path e.Message
```

`SeamCodec` writes canonical bytes (ordinal-sorted keys, absent options omitted, lists always
present) and reads strictly on the records and leniently on annotations: an unknown member of a
record is refused, an unknown member of an annotation is ignored. A provider is certified by the
conformance corpus's `design/` family, specified by `DESIGN_SEAM.md` in the Fuaran UI
specification repository: every round-trip vector re-encodes byte-identically through
`SeamCodec.reencode`, and every reject vector refuses with its named code at its named path.

FSharp.Core and `Fuaran.Core.Wire` only; Fable-portable. Apache-2.0 licensed.
