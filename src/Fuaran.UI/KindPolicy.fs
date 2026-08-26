module Fuaran.UI.KindPolicy

// ============================================================================
//  Host-declared kind admission policy (WIRE_FORMAT.md §23).
//
//  An application closes the algebra BY DECLARATION rather than by omission. A
//  host that registers no `Custom` renderers and installs no guest seam is
//  *functionally* closed — but a tree carrying `Custom` / `Mount` still decodes,
//  costs the work, renders a placeholder, and silently stops being inert the day
//  an unrelated registration lands. A declared policy makes the closure
//  monotone and auditable: the refusal is a logged, attributable event carrying
//  the kind and the policy that refused it.
//
//  THE DEFAULT IS UNCHANGED, and that is the load-bearing property. With no
//  policy supplied, every valid document decodes exactly as the specification
//  says it must (§22: a tree carrying a hostile payload is a valid wire document
//  and a decoder MUST NOT reject it). A policy is a HOST-SIDE NARROWING the
//  specification permits a deployment to apply; it is not a wire narrowing, and
//  conformance is measured with no policy declared. A document refused under a
//  policy is still a valid wire document.
//
//  Placement. This lives in `Fuaran.UI` rather than beside the decoder in
//  `Fuaran.UI.Ops` because BOTH ends need it: the decoder enforces it, and
//  `PreEmitValidate` lints an authoring host's own tree against it before emit.
//  `Fuaran.UI.Ops` depends on `Fuaran.UI`, never the reverse, so the shared type
//  can only sit here — the same reason `WireLimits` does.
//
//  Fable-compatible: `Set<string>` and `List` only, no reflection, no `System.*`.
// ============================================================================

open Fuaran.UI.Types

// ─── The policy surface ───────────────────────────────────────────────────

/// What a policy admits.
///
/// Deliberately an ALLOW-LIST with no deny-list case, and the asymmetry is the
/// decision rather than an omission. A deny-list of today's hatch kinds silently
/// admits tomorrow's — which is the precise failure this mechanism exists to
/// refuse ("functionally closed until an unrelated change lands"). A host that
/// wants to think in exclusions builds the admitted set from a vocabulary it
/// names, at the moment it declares the policy, with `DecodePolicy.excludingFrom`.
[<RequireQualifiedAccess>]
type Admission =
    /// Every kind the decoder recognises. The shipped default; §22 unchanged.
    | AdmitAll
    /// Exactly these wire discriminators (`kind.$type`), and no others.
    | AdmitOnly of Set<string>

/// A host's declared decode-time kind admission policy.
///
/// `Identity` is a short, stable name for the policy — it is reported in the
/// refusal so a log line says WHICH declaration refused, not merely that
/// something did. Two deployments running different profiles produce
/// distinguishable evidence; a policy whose refusals are anonymous is one
/// nobody can audit.
type DecodePolicy =
    { Identity: string
      Admission: Admission }

module DecodePolicy =

    /// The shipped default: admit every recognised kind. Supplying this is
    /// byte-for-byte indistinguishable from supplying no policy at all.
    let admitAll: DecodePolicy =
        { Identity = "admit-all"
          Admission = Admission.AdmitAll }

    /// Admit exactly `kinds`, named by their WIRE discriminators (`kind.$type`).
    let admitting (identity: string) (kinds: string seq) : DecodePolicy =
        { Identity = identity
          Admission = Admission.AdmitOnly(Set.ofSeq kinds) }

    /// Admit everything in `vocabulary` except `excluded` — the exclusion form,
    /// resolved to an allow-list AT CONSTRUCTION against the vocabulary the
    /// caller names. So a kind added to the language later is NOT admitted by a
    /// policy declared today, which is the whole point of the allow-list shape.
    ///
    /// A name in `excluded` that is not in `vocabulary` is a no-op — the set
    /// difference cannot report it. A caller shipping a named profile should pin
    /// its exclusions against the vocabulary with a test rather than trusting the
    /// spelling; `Fuaran.UI.Ops.JsonDecode.Policy.closedProfile` does exactly
    /// that.
    let excludingFrom (identity: string) (vocabulary: string seq) (excluded: string seq) : DecodePolicy =
        { Identity = identity
          Admission = Admission.AdmitOnly(Set.difference (Set.ofSeq vocabulary) (Set.ofSeq excluded)) }

    /// Does `policy` admit the wire discriminator `kind`?
    let admits (policy: DecodePolicy) (kind: string) : bool =
        match policy.Admission with
        | Admission.AdmitAll -> true
        | Admission.AdmitOnly admitted -> Set.contains kind admitted

    /// Is this policy a narrowing at all? `false` for the shipped default, so a
    /// caller can skip the whole check rather than test admission per node.
    let narrows (policy: DecodePolicy) : bool =
        match policy.Admission with
        | Admission.AdmitAll -> false
        | Admission.AdmitOnly _ -> true

    /// The admitted vocabulary as a hint string, Ordinal-sorted and `|`-joined —
    /// the `ExpectedShape` a refusal carries. PROJECTED from the policy rather
    /// than written beside it, on the same discipline (and for the same reason)
    /// as the decoder's `wrongNodeKindHint`: a hint that names a set the gate
    /// does not enforce is worse than no hint.
    let hint (policy: DecodePolicy) : string =
        match policy.Admission with
        | Admission.AdmitAll -> "any recognised node kind (this policy admits all)"
        | Admission.AdmitOnly admitted -> admitted |> Set.toList |> String.concat " | "

// ─── The wire projection of a kind ────────────────────────────────────────

/// The WIRE discriminator of a kind — what appears as `kind.$type` on the wire
/// and what a policy's admitted set is written in.
///
/// `Kind.name` is the DISPLAY / kind-constraint tag, and the two vocabularies
/// coincide for every kind but one: `NodeKind.DataGrid` tags as `"Grid"` there
/// and is `"DataGrid"` on the wire. So this adapts that single case and defers
/// to `Kind.name` for the rest, rather than re-enumerating thirty-nine arms that
/// would then drift. `Fuaran.UI.Renderer.Relay.wireKindName` is the same
/// two-line adaptation made at the relay boundary for the same reason; both are
/// pinned against the corpus, which is the authority for the vocabulary.
let wireKindName (kind: NodeKind<'Msg>) : string =
    match kind with
    | NodeKind.DataGrid _ -> "DataGrid"
    | other -> Kind.name other
