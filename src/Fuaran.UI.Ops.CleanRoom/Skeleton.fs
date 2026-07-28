module Fuaran.UI.Ops.CleanRoom.Skeleton

// ============================================================================
//  Structure-only clean room — content-free skeleton projection.
//
//  `project : Node<'Msg> -> Skeleton` walks the real tree and emits a
//  content-free *shadow* of it: per node, its `NodeId`, its structural
//  `Kind` discriminator, and a bounded `StructuralDescriptor` (a coarse
//  structural role + coarsened child-count / content-length buckets). Every
//  content field — every `TextSource.Literal`, every `Binding.Static`
//  value, every code / math / list payload — is dropped on projection. The
//  skeleton is what crosses a clean-room divide; the privileged prose never
//  serialises with it.
//
//  Why ABSTRACTION, not redaction (the load-bearing constraint). Structure
//  leaks content: a heading's *text* is often itself sensitive, and so are
//  child counts, ordering, and text *lengths*. So the skeleton replaces
//  specifics with bounded type tags (the `StructuralRole` role + coarsened
//  magnitude buckets) rather than blanking strings. This package ships the
//  MECHANISM — the projection seam, the bounded descriptor type, and the
//  count / length coarsening. The domain-specific tag vocabulary + classifier
//  that decides *which* descriptor a node gets (legal `indemnity-clause` /
//  `definition` / …) is the consuming domain's, layered on top via the
//  `classify` projection seam — see docs/STRUCTURE-ONLY-CLEAN-ROOM.md.
//
//  Purity / portability (GP 12 rule 3/4): every type is an immutable
//  record / DU and `project` is a pure function returning data. No
//  reflection, no `System.*` beyond `String.length` — the projection is
//  Fable-clean, walking the `Fuaran.UI.Types` DU surface through the shared
//  `Fuaran.UI.Ops.Introspect` traversal helpers.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect

// ─── Bounded coarsening buckets ─────────────────────────────────────────────

/// Coarsened child-count magnitude. Exact counts are a structural
/// side-channel (a 14-section contract vs a 3-section one leaks the kind of
/// document), so the projection emits only this bounded bucket, never the
/// raw integer.
[<RequireQualifiedAccess>]
type CountBucket =
    /// No children (a leaf).
    | None
    /// Exactly one child.
    | One
    /// 2–4 children.
    | Few
    /// 5–12 children.
    | Several
    /// 13 or more children.
    | Many

/// Coarsened content-length magnitude for a node's own (non-child) literal
/// content. Text length is a side-channel too (a 2-character heading vs a
/// 2000-character clause body leaks density), so the projection emits only
/// this bounded bucket, derived inside the perimeter and never the raw length.
[<RequireQualifiedAccess>]
type LengthBucket =
    /// No own literal content (a pure container, or a node whose content is
    /// entirely data-bound rather than literal).
    | Empty
    /// 1–16 characters.
    | Short
    /// 17–128 characters.
    | Medium
    /// 129–1024 characters.
    | Long
    /// More than 1024 characters.
    | VeryLong

module CountBucket =
    /// Coarsen a raw child count into its bounded bucket.
    let ofCount (n: int) : CountBucket =
        if n <= 0 then CountBucket.None
        elif n = 1 then CountBucket.One
        elif n <= 4 then CountBucket.Few
        elif n <= 12 then CountBucket.Several
        else CountBucket.Many

module LengthBucket =
    /// Coarsen a raw content-length into its bounded bucket.
    let ofLength (n: int) : LengthBucket =
        if n <= 0 then LengthBucket.Empty
        elif n <= 16 then LengthBucket.Short
        elif n <= 128 then LengthBucket.Medium
        elif n <= 1024 then LengthBucket.Long
        else LengthBucket.VeryLong

// ─── Structural role (the bounded, domain-neutral descriptor) ───────────────

/// A bounded, domain-neutral classification of a node's structural purpose,
/// derived from its `NodeKind` alone. This is the *mechanism's* descriptor —
/// it exposes a node's structural KIND-of-thing without its identity. A
/// consuming domain layers a richer (still bounded) tag vocabulary on top via
/// the `project`/`classifyWith` seam; this role is the always-present floor.
[<RequireQualifiedAccess>]
type StructuralRole =
    /// A layout container that holds children (Dashboard / Stack / Card / …).
    | Container
    /// A heading / section title.
    | Heading
    /// A block of prose / text content (markdown, callout, label-value, …).
    | TextBlock
    /// A data visualisation (grid / chart / table / map / metric / sparkline).
    | DataView
    /// An interactive input (form / button / select / file-upload / filters).
    | Interactive
    /// Embedded media (image).
    | Media
    /// A visual separator (spacer / divider).
    | Separator
    /// A reusable-subtree primitive (fragment decl / ref) or error boundary.
    | Structural
    /// An opaque escape (`NodeKind.Custom`) — structure visible, body opaque.
    | Opaque

/// The bounded per-node descriptor. `Role` is the always-present structural
/// floor; `ChildCount` / `ContentLength` are coarsened magnitude buckets. No
/// field carries node content — by construction, not by redaction.
type StructuralDescriptor =
    { Role: StructuralRole
      ChildCount: CountBucket
      ContentLength: LengthBucket }

// ─── The skeleton ───────────────────────────────────────────────────────────

/// One node of a content-free skeleton: a stable `NodeId`, the structural
/// `Kind` discriminator (e.g. `"Heading"`, `"Dashboard"` — a fixed-vocabulary
/// type name, never the node's text), its bounded `Descriptor`, and its
/// projected children. There is deliberately NO content field on this type:
/// the projection cannot leak prose because there is nowhere for prose to go.
type SkeletonNode =
    { Id: NodeId
      Kind: string
      Descriptor: StructuralDescriptor
      Children: SkeletonNode list }

/// The content-free shadow of a `Node<'Msg>` tree — the artefact that crosses
/// the clean-room divide. The untrusted side reads ids + kinds + bounded
/// structural metadata and emits id-referenced structural ops; the privileged
/// content never serialises into it.
type Skeleton = { Root: SkeletonNode }

// ─── Content-length extraction (inside the perimeter; only the bucket leaves) ─

/// Length of a `TextSource`'s literal content, in characters. Data-bound text
/// (`Bound` of a non-`Static` binding) contributes nothing — there is no
/// literal to measure. A `Static`-bound literal IS literal content and is
/// measured; an i18n key is a catalog handle (not prose) but its length is
/// counted as a conservative side-channel contribution.
let private textSourceLength (t: TextSource) : int =
    match t with
    | TextSource.Literal s -> String.length s
    | TextSource.Bound(Binding.Static s) -> String.length s
    | TextSource.Bound _ -> 0
    | TextSource.I18n(key, _) -> String.length key

let private textSourceOptLength (t: TextSource option) : int =
    match t with
    | Some t -> textSourceLength t
    | None -> 0

/// A node's OWN literal content length (excluding children) — the raw input
/// to the `ContentLength` bucket. Covers the content-bearing display + layout
/// shapes; anything else contributes 0. Only the coarsened bucket ever leaves
/// the perimeter, so an unmatched kind under-reporting length is safe (it can
/// only widen the abstraction, never narrow it).
let private ownContentLength (kind: NodeKind<'Msg>) : int =
    match kind with
    | NodeKind.Heading s -> textSourceLength s.Text
    | NodeKind.Markdown s -> textSourceLength s.Text
    | NodeKind.Badge s -> textSourceLength s.Label
    | NodeKind.Metric s -> textSourceLength s.Label + textSourceOptLength s.Subtext
    | NodeKind.Callout s -> textSourceLength s.Body + textSourceOptLength s.Heading
    | NodeKind.LabelValueRow s -> textSourceLength s.Label
    | NodeKind.Link s -> textSourceLength s.Label
    | NodeKind.Image s -> textSourceLength s.Alt
    | NodeKind.List s -> s.Items |> List.sumBy textSourceLength
    | NodeKind.Toast s -> textSourceLength s.Message
    | NodeKind.CodeBlock s -> String.length s.Code
    | NodeKind.Math s -> String.length s.Source
    | NodeKind.Box s -> textSourceOptLength s.Heading
    | NodeKind.SummaryList s -> textSourceOptLength s.Heading
    | NodeKind.Disclosure s -> textSourceLength s.Heading
    | NodeKind.Modal s -> textSourceOptLength s.Heading
    | _ -> 0

// ─── Role classification ────────────────────────────────────────────────────

/// Map a `NodeKind` to its bounded, domain-neutral `StructuralRole`. Pure
/// dispatch on the kind discriminator — no content inspected.
let roleOf (kind: NodeKind<'Msg>) : StructuralRole =
    // Phase 692 — the category wrappers this dispatched on are gone; the derived
    // `Kind.category` carries the same classification. The Display kinds whose
    // role differs from their category's default are matched first.
    match kind with
    | NodeKind.Heading _ -> StructuralRole.Heading
    | NodeKind.Metric _
    | NodeKind.Sparkline _ -> StructuralRole.DataView
    | NodeKind.Image _ -> StructuralRole.Media
    | NodeKind.Custom _ -> StructuralRole.Opaque
    // Switch (Phase 392) is a structural control-flow primitive — it selects a
    // child subtree by state; the chosen child carries its own role. Mount (§4o)
    // is a structural isolation boundary — the guest's own content is classified
    // in its own scope, so at the host level it is Structural.
    | _ ->
        match Kind.category kind with
        | NodeCategory.Layout -> StructuralRole.Container
        | NodeCategory.Display -> StructuralRole.TextBlock
        | NodeCategory.Input -> StructuralRole.Interactive
        | NodeCategory.Visualisation -> StructuralRole.DataView
        | NodeCategory.Structural -> StructuralRole.Structural

// ─── Projection ─────────────────────────────────────────────────────────────

/// The descriptor-classification seam. The default (`defaultDescriptor`)
/// derives the bounded descriptor from a node's kind + coarsened magnitudes
/// alone. A consuming domain substitutes a richer classifier (still returning
/// the bounded `StructuralDescriptor`) to attach domain tags — without ever
/// being able to widen the type into a content-carrying one.
type Classify<'Msg> = Node<'Msg> -> StructuralDescriptor

/// The default, domain-neutral descriptor: structural role + coarsened
/// child-count + coarsened own-content-length. Used by `project`.
let defaultDescriptor (node: Node<'Msg>) : StructuralDescriptor =
    let childCount =
        match getChildren node.Kind with
        | Some children -> List.length children
        | None -> 0

    { Role = roleOf node.Kind
      ChildCount = CountBucket.ofCount childCount
      ContentLength = LengthBucket.ofLength (ownContentLength node.Kind) }

/// Project a real `Node<'Msg>` tree into a content-free `Skeleton` using a
/// caller-supplied descriptor classifier. The structure + ids are preserved
/// (the untrusted side needs them to author id-referenced ops); all content
/// is dropped.
let projectWith (classify: Classify<'Msg>) (root: Node<'Msg>) : Skeleton =
    let rec go (node: Node<'Msg>) : SkeletonNode =
        let children =
            match getChildren node.Kind with
            | Some kids -> kids |> List.map go
            | None -> []

        { Id = node.Id
          Kind = kindName node.Kind
          Descriptor = classify node
          Children = children }

    { Root = go root }

/// Project a real `Node<'Msg>` tree into a content-free `Skeleton` with the
/// default domain-neutral descriptor. This is the mechanism's headline entry
/// point: `project realTree` is what an in-perimeter caller hands across the
/// clean-room divide.
let project (root: Node<'Msg>) : Skeleton = projectWith defaultDescriptor root

// ─── Skeleton queries ───────────────────────────────────────────────────────

/// Every `NodeId` present in the skeleton, in DFS pre-order. The
/// `StructuralOpBroker` checks inbound op targets against this set — an op
/// that references an id not in the issued skeleton is withheld.
let nodeIds (skeleton: Skeleton) : NodeId list =
    let rec go (n: SkeletonNode) = n.Id :: (n.Children |> List.collect go)

    go skeleton.Root

/// The set of every `NodeId` present in the skeleton (the membership index the
/// broker probes per inbound op).
let knownIds (skeleton: Skeleton) : Set<NodeId> = nodeIds skeleton |> Set.ofList

/// The number of nodes in the skeleton (audit metadata — a count, never content).
let nodeCount (skeleton: Skeleton) : int = nodeIds skeleton |> List.length
