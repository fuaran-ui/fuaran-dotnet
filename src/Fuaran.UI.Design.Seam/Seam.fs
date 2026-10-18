namespace Fuaran.UI.Design

// ============================================================================
//  Phase 2295 — the design-provider seam's records.
//
//  A design tool sits behind a seam (`IDesignProvider`). The provider never sees
//  a Fuaran UI tree, and the lowering that turns a design into a tree never sees
//  the provider: the two meet only in the four records below, written down as
//  data with a conformance family (`wire-format-fixtures/design/`,
//  `DESIGN_SEAM.md`), so a second provider is certified by round-tripping the
//  fixtures rather than by resembling the first.
//
//  FSharp.Core and `Fuaran.Core.Wire` only, and Fable-portable: the records ship
//  their sources under `fable/` and the Fable stage compiles them. Computing a
//  hash is the caller's act (SHA-256 is not a portable primitive); the records
//  carry hashes and the codec checks their spelling.
// ============================================================================

/// A content hash as the seam spells it: `sha256:` and 64 lowercase hexadecimal digits — the
/// spelling `Bundle.ContentHash` already uses.
module DesignHash =

    [<Literal>]
    let prefix = "sha256:"

    /// Whether a string is a well-formed seam hash.
    let isWellFormed (s: string) : bool =
        s.Length = prefix.Length + 64
        && s.StartsWith(prefix, System.StringComparison.Ordinal)
        && s.Substring(prefix.Length)
           |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

/// The identity of a design-system bundle a provider has received: which provider holds it,
/// under which project, which bundle (by its content hash) and which IDL version its cards were
/// rendered from.
type DesignSystemRef =
    {
        /// The provider's own identifier — an opaque token the provider chooses.
        ProviderId: string
        /// The provider-side project holding the bundle, for a provider that has projects.
        ProjectId: string option
        /// The bundle's content hash (`Bundle.ContentHash`).
        BundleHash: string
        /// The IDL version the bundle's cards were rendered from (`Bundle.IdlVersion`).
        IdlVersion: int
    }

/// How many values a data shape holds.
[<RequireQualifiedAccess>]
type Cardinality =
    /// Exactly one value.
    | One
    /// Zero or one value.
    | Optional
    /// A collection of values.
    | Many

/// A shape of data a module shows, as a composition's data flow already knows it: a name, a type
/// name and a cardinality.
type DataShape =
    { Name: string
      Type: string
      Cardinality: Cardinality }

/// The closed vocabulary of interactions a brief may ask for. Each lowers to a construct the
/// language already has; an interaction outside this set is refused at decode, because a design
/// that asks for one could not be lowered to a tree.
[<RequireQualifiedAccess>]
type Interaction =
    /// Narrow a view by a named criterion. Lowers to a `Filters` node writing a
    /// `Binding.Filter` that the filtered view reads.
    | Filter
    /// Choose one item of a collection. Lowers to `Binding.Selection` over the node that shows
    /// the collection.
    | Select
    /// Open the detail of a chosen item. Lowers to `Action.Navigate` with a route carrying the
    /// key read through `Binding.Selection`.
    | Drill
    /// Set chosen items side by side. Lowers to `Binding.Selection` over several items, read by
    /// a second view (a `Chart` or `DataGrid`) bound to the selection.
    | Compare
    /// Take the data shown out of the module. Lowers to `Action.Invoke` of a host capability.
    | Export
    /// Move to another screen of the application. Lowers to `Action.Navigate`.
    | Navigate

/// A viewport a design is made for: a name and a width in CSS pixels.
type Viewport = { Name: string; Width: int }

/// How dense a design is. Lowers to the language's weight axis (`Compact`, `Standard`,
/// `Spacious`), which the theme maps onto its spacing scale.
[<RequireQualifiedAccess>]
type Density =
    | Compact
    | Standard
    | Spacious

/// What a module is for and what it must show, written for a design tool.
type DesignBrief =
    {
        /// The module's name.
        Module: string
        /// What the module is for, in a sentence.
        Purpose: string option
        /// The data the module shows.
        DataShapes: DataShape list
        /// The tasks a user performs in the module.
        Tasks: string list
        /// The interactions the module offers, from the closed vocabulary.
        Interactions: Interaction list
        /// The viewports the design is made for.
        Viewports: Viewport list
        /// The density the design is made at.
        Density: Density option
        /// The stdlib fragments the design may use, by name.
        Fragments: string list
        /// The design system the design is composed from.
        DesignSystem: DesignSystemRef
    }

/// Where a design request stands.
[<RequireQualifiedAccess>]
type RequestState =
    /// The design is made interactively in the tool, and retrieved by its declared export.
    | Manual
    /// The provider has accepted the request and the design is not ready.
    | Pending
    /// The design is ready to fetch.
    | Ready

/// A brief handed to a provider: the brief by its hash, the design system it was made against,
/// where the request stands, and the provider's handle for it.
type DesignRequest =
    {
        /// `sha256:` over the brief's canonical bytes (`SeamCodec.encodeBrief`).
        BriefHash: string
        DesignSystem: DesignSystemRef
        State: RequestState
        /// The provider's handle for the request, when it issues one.
        Handle: string option
    }

/// One file of a screen: its path within the result, its media type and its content hash.
type DesignFile =
    { Path: string
      MediaType: string
      Hash: string }

/// One screen of a design, as named files (HTML, CSS, images).
type DesignScreen =
    { Name: string; Files: DesignFile list }

/// The structured annotation of one screen: the components it uses, named as the bundle names
/// its Components and Fragments cards.
type ScreenAnnotation =
    { Screen: string
      Components: string list }

/// What a provider returns: the screens, the optional annotations, the design system the design
/// was made against and the result's content hash.
type DesignResult =
    {
        Screens: DesignScreen list
        /// Per-screen component inventories, when the provider supplies them.
        Annotations: ScreenAnnotation list option
        DesignSystem: DesignSystemRef
        /// `sha256:` over the result's file set, one line per file in ordinal path order of
        /// `<path> <hash>\n` — the rule `Bundle.ContentHash` uses for a bundle.
        ContentHash: string
    }

module DesignResult =

    /// The components the result's annotations name that are not in `known` (for a bundle, its
    /// Components and Fragments card names), in first-seen order without repeats. Empty when
    /// every name is known, or when the result carries no annotations.
    let unknownComponents (known: Set<string>) (result: DesignResult) : string list =
        result.Annotations
        |> Option.defaultValue []
        |> List.collect _.Components
        |> List.filter (fun c -> not (known.Contains c))
        |> List.distinct

/// A design tool behind the seam. The provider never sees a Fuaran UI tree; the lowering never
/// sees the provider. A provider is certified by round-tripping the `design/` corpus family.
type IDesignProvider =
    /// The provider's identifier, as it appears in every `DesignSystemRef` it issues.
    abstract ProviderId: string

    /// Hand the provider a written design-system bundle (its directory and its content hash);
    /// the provider answers with the reference designs are made against.
    abstract PublishDesignSystem: bundleDirectory: string * bundleHash: string -> Async<Result<DesignSystemRef, string>>

    /// Hand the provider a brief; the provider answers with the request it now holds.
    abstract RequestDesign: brief: DesignBrief -> Async<Result<DesignRequest, string>>

    /// Ask for the design a request produced: `None` while it is not ready.
    abstract FetchDesign: request: DesignRequest -> Async<Result<DesignResult option, string>>
