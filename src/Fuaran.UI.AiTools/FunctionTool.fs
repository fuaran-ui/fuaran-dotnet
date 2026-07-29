module Fuaran.UI.AiTools.FunctionTool

// ============================================================================
//  Cross-domain Slot composition — adopting `Fuaran.Core.Function.composeAcross`
//  (the cross-witness slot binding) into the UI tier (Phase 358).
//
//  A `Fuaran.UI` parameterised fragment (`FragmentDecl`) is an artifact-function
//  of its declared holes. `Fragment.fs` owns the WITHIN-witness laws (signature /
//  currying / value-space validation / the effect join); the renderer's
//  `FragmentApply` owns the WITHIN-witness hygienic slot substitution. This
//  module lifts that surface onto the GENERIC cross-witness operator so a UI
//  fragment's typed `Slot` hole can bind to the output tree of a NON-UI
//  artifact-function (a `Fuaran.Core.Query` / `DataFrame` function, a Calc model,
//  …). UI becomes the typed TERMINUS of a cross-domain pipeline.
//
//  What the adoption supplies:
//    (1) `uiWitness` — the UI outer `ArtifactWitness<Node<'Msg>, NodeId>` over a
//        `FragmentDecl` node. Its `Holes` project the decl's declared holes at
//        their absolute lexical address (`<declId>.<holeName>`), `Effect` reads
//        the decl's declared `EffectClass`, and `Bind` splices a slot argument
//        into the `FragmentRef`-marked slot inside the decl body, HYGIENICALLY
//        namespaced by the slot address — the same `<refId>.<innerId>` id-rewrite
//        the renderer's `FragmentApply` performs, re-expressed here over the
//        `Fuaran.UI.Ops.Introspect` container lens (FSharp.Core + Ops only, so it
//        stays Fable-clean and takes no renderer dependency).
//    (2) `composeIntoSlot` — the thin wrapper over `Function.composeAcross` that
//        fixes the outer witness to `uiWitness`: given any inner witness `wb`, a
//        host-supplied `embed : 'B -> Node<'Msg>` (the one genuinely cross-domain
//        step — how a foreign output subtree becomes a UI node), a slot address,
//        the inner function's output, and the outer fragment, it yields ONE
//        composed `Fuaran.UI` artifact-function of the residual (unbound) UI
//        holes. The three Forks carry across the boundary unchanged: totality
//        (either part's unbounded repeat is rejected, never run), hygiene (the
//        embedded inner's holes re-root under the slot address), and the effect
//        JOIN — a `network` inner (a live query) correctly flips the composed
//        dashboard's class to `network` (surfaced by `composedEffect`).
//
//  FGP 2 / FGP 4: `FSharp.Core` + `Fuaran.UI` + `Fuaran.UI.Ops` + the
//  FSharp.Core-only `Fuaran.Core.Function` contract — no renderer, no `System.*`,
//  no reflection; the composition runs byte-identically under Fable and .NET.
//  Additive (GP 11): the existing fragment / slot / `FragmentApply` surface is
//  untouched.
// ============================================================================

open Fuaran.UI.Types

module Introspect = Fuaran.UI.Ops.Introspect

// ─── UI ↔ Core hole/effect projection ────────────────────────────────────────
//
// The UI hole + effect vocabulary (`Fuaran.UI.Types`) and the Core one
// (`Fuaran.Core`) are structurally identical mirrors — this is the byte-for-byte
// translation the witness reads at the seam.

let private toCoreSpace (s: HoleValueSpace) : Fuaran.Core.ValueSpace =
    match s with
    | HoleValueSpace.IntRange(lo, hi) -> Fuaran.Core.IntRange(lo, hi)
    | HoleValueSpace.FloatRange(lo, hi) -> Fuaran.Core.FloatRange(lo, hi)
    | HoleValueSpace.StringLen(lo, hi) -> Fuaran.Core.StringLen(lo, hi)
    | HoleValueSpace.Enum choices -> Fuaran.Core.Enum choices
    | HoleValueSpace.AnyString -> Fuaran.Core.AnyString

let private toCoreEffect (e: EffectClass) : Fuaran.Core.EffectClass =
    { Host =
        match e.HostEffect with
        | HostEffect.Pure -> Fuaran.Core.Pure
        | HostEffect.ReadsHost -> Fuaran.Core.ReadsHost
        | HostEffect.WritesHost -> Fuaran.Core.WritesHost
      Determinism =
        match e.Determinism with
        | DeterminismSource.Deterministic -> Fuaran.Core.Deterministic
        | DeterminismSource.Clock -> Fuaran.Core.Clock
        | DeterminismSource.Random -> Fuaran.Core.Random
        | DeterminismSource.Network -> Fuaran.Core.Network }

let private rawId (NodeId s) : string = s

/// The absolute lexical address of a fragment-decl hole: `<declId>.<holeName>`
/// (hygiene — binding is by address, never a bare name).
let holeAddr (declId: string) (holeName: string) : string = declId + "." + holeName

// ─── The UI outer witness ────────────────────────────────────────────────────

/// The declared holes of a fragment-decl node, each at its absolute lexical
/// address. A non-decl node declares no holes (an empty list) — the witness is
/// total over any `Node<'Msg>`, but only a `FragmentDecl` carries holes to bind.
let private declHoles (node: Node<'Msg>) : Fuaran.Core.HoleDecl list =
    match node.Kind with
    | NodeKind.FragmentDecl spec ->
        let declId = node.Id

        spec.Holes
        |> Option.defaultValue []
        |> List.map (fun h ->
            let name = HoleDecl.name h

            let kind =
                match h with
                | HoleDecl.Value(_, space, _) -> Fuaran.Core.ValueHole(toCoreSpace space)
                | HoleDecl.Slot(_, constraintOpt) -> Fuaran.Core.SlotHole constraintOpt
                | HoleDecl.Repeat(_, space) -> Fuaran.Core.RepeatHole(toCoreSpace space)

            { Addr = holeAddr declId name
              Name = name
              Kind = kind })
    | _ -> []

/// The declared effect class of a fragment-decl node (pure-deterministic for a
/// non-decl node — it declares no effect surface).
let private declEffect (node: Node<'Msg>) : Fuaran.Core.EffectClass =
    match node.Kind with
    | NodeKind.FragmentDecl spec ->
        spec.Effect
        |> Option.map toCoreEffect
        |> Option.defaultValue Fuaran.Core.Effect.pureDeterministic
    | _ -> Fuaran.Core.Effect.pureDeterministic

// ── the hygienic slot substitution (the renderer's FragmentApply logic,
//    re-expressed over the Ops.Introspect container lens) ──

/// The slot name a node is an unbound marker for (a bare `FragmentRef`), when it
/// is one.
let private slotMarker (node: Node<'Msg>) : string option =
    match node.Kind with
    | NodeKind.FragmentRef spec -> Some spec.Name
    | _ -> None

/// Rewrite every interior NodeId by `prefix` — hygienic namespacing of an
/// inserted slot subtree (`<slotAddr>.<innerId>`), so two cross-witness
/// compositions into distinct slots cannot capture one another (Fork 2).
let rec private namespaceIds (prefix: string) (node: Node<'Msg>) : Node<'Msg> =
    let renamed = { node with Id = prefix + node.Id }

    match Introspect.getChildren renamed.Kind with
    | Some kids ->
        match Introspect.withChildren renamed.Kind (kids |> List.map (namespaceIds prefix)) with
        | Some k -> { renamed with Kind = k }
        | None -> renamed
    | None ->
        match renamed.Kind with
        | NodeKind.ErrorBoundary spec ->
            { renamed with
                Kind =
                    NodeKind.ErrorBoundary
                        { Child = namespaceIds prefix spec.Child
                          Fallback = namespaceIds prefix spec.Fallback } }
        | _ -> renamed

/// Substitute the `FragmentRef`-marked slot named `slotName` inside `body` with
/// `arg`, namespaced by `slotPrefix`. Recurses into containers + error
/// boundaries. Returns the rewritten body + whether a marker was found.
let rec private substituteSlot
    (slotPrefix: string)
    (slotName: string)
    (arg: Node<'Msg>)
    (body: Node<'Msg>)
    : Node<'Msg> * bool =
    match slotMarker body with
    | Some s when s = slotName -> namespaceIds (slotPrefix + ".") arg, true
    | _ ->
        match Introspect.getChildren body.Kind with
        | Some kids ->
            let mutable found = false

            let newKids =
                kids
                |> List.map (fun c ->
                    let c', f = substituteSlot slotPrefix slotName arg c
                    found <- found || f
                    c')

            match Introspect.withChildren body.Kind newKids with
            | Some k -> { body with Kind = k }, found
            | None -> body, found
        | None ->
            match body.Kind with
            | NodeKind.ErrorBoundary spec ->
                let child, f1 = substituteSlot slotPrefix slotName arg spec.Child
                let fallback, f2 = substituteSlot slotPrefix slotName arg spec.Fallback

                { body with
                    Kind = NodeKind.ErrorBoundary { Child = child; Fallback = fallback } },
                (f1 || f2)
            | _ -> body, false

/// Bind a hole@`addr` on a fragment-decl node. Only a slot binding
/// (`SlotArg`) is lowered by this witness — a UI slot hole binds a subtree; the
/// cross-domain composition `composeAcross` only ever emits a `SlotArg`
/// (`embed inner`). A `ValueArg` (scalar value-hole) is not a cross-witness bind
/// and is refused here (value holes are bound host-side via `FragmentApply`'s
/// value-binding map, not through this seam).
let private declBind (addr: string) (arg: Fuaran.Core.Arg<Node<'Msg>>) (node: Node<'Msg>) : Result<Node<'Msg>, string> =
    match node.Kind with
    | NodeKind.FragmentDecl spec ->
        let declId = node.Id

        // The addr is `<declId>.<slotName>`; recover the slot name.
        let prefix = declId + "."

        if not (addr.StartsWith prefix) then
            Error(sprintf "slot address '%s' is not addressed under fragment decl '%s'" addr declId)
        else
            let slotName = addr.Substring prefix.Length

            match arg with
            | Fuaran.Core.SlotArg subtree ->
                let body', found = substituteSlot addr slotName subtree spec.Body

                if not found then
                    Error(sprintf "no unbound slot marker named '%s' in fragment decl '%s' body" slotName declId)
                else
                    // The bound slot is no longer an open hole — drop it from the
                    // decl's declared holes so the composed function's signature
                    // reports only the residual (unbound) holes.
                    let residualHoles =
                        spec.Holes
                        |> Option.map (List.filter (fun h -> HoleDecl.name h <> slotName))
                        |> Option.bind (function
                            | [] -> None
                            | hs -> Some hs)

                    Ok
                        { node with
                            Kind =
                                NodeKind.FragmentDecl
                                    { spec with
                                        Body = body'
                                        Holes = residualHoles } }
            | Fuaran.Core.ValueArg _ ->
                Error(sprintf "hole '%s' bound with a value arg through the cross-witness slot seam" addr)
    | _ -> Error "cross-witness slot binding targets a FragmentDecl node"

/// The UI outer `ArtifactWitness<Node<'Msg>, NodeId>` — the typed terminus a
/// cross-domain pipeline composes INTO. Reuses the UI hole-extraction + effect +
/// hygienic-bind logic (no re-invention): `Holes` / `Effect` project the
/// `FragmentDecl` surface, `Bind` performs the renderer's slot substitution over
/// the `Ops.Introspect` container lens.
let uiWitness<'Msg> : Fuaran.Core.ArtifactWitness<Node<'Msg>, NodeId> =
    { Tree =
        { Id = fun n -> NodeId n.Id
          KindTag = fun n -> Introspect.kindName n.Kind
          Children = fun n -> Introspect.getChildren n.Kind |> Option.defaultValue []
          ReplaceChildren =
            fun n cs ->
                match Introspect.withChildren n.Kind cs with
                | Some k -> { n with Kind = k }
                | None -> n }
      IdW =
        { ToString = rawId
          OfString = NodeId
          Equals = (=) }
      Holes = declHoles
      Effect = declEffect
      Bind = declBind }

// ─── The cross-domain composition surface ────────────────────────────────────

/// Compose a foreign artifact-function's output INTO a UI fragment's typed slot —
/// the adoption of `Fuaran.Core.Function.composeAcross` with the outer witness
/// fixed to `uiWitness`. `wb` is the inner (foreign) witness; `embed` lifts the
/// inner's `'B` output into a `Fuaran.UI` `Node<'Msg>` the slot can hold (the one
/// genuinely cross-domain step — how a data subtree becomes a UI node);
/// `slotAddr` is the absolute lexical address (`<declId>.<slotName>`) of the UI
/// slot to bind into (`holeAddr` mints it); `inner` is the foreign function's
/// output; `outer` is the UI fragment decl. The result is ONE composed
/// `Fuaran.UI` artifact-function of the residual UI holes, with hygiene + the
/// effect join carried across the seam.
let composeIntoSlot<'Msg, 'B, 'IdB>
    (wb: Fuaran.Core.ArtifactWitness<'B, 'IdB>)
    (embed: 'B -> Node<'Msg>)
    (slotAddr: string)
    (inner: 'B)
    (outer: Node<'Msg>)
    : Result<Node<'Msg>, Fuaran.Core.ApplyError> =
    Fuaran.Core.Function.composeAcross uiWitness wb embed slotAddr inner outer

/// The composed effect class of binding a `'B`-witness inner across the seam into
/// the UI `outer` — the componentwise join (Fork 3), recomputable on demand. A
/// `network` inner (a live query) joins the UI's (typically pure-deterministic)
/// class up to `network`.
let composedEffect<'Msg, 'B, 'IdB>
    (wb: Fuaran.Core.ArtifactWitness<'B, 'IdB>)
    (inner: 'B)
    (outer: Node<'Msg>)
    : Fuaran.Core.EffectClass =
    Fuaran.Core.Function.composedEffectAcross uiWitness wb inner outer

// ─── Effect-soundness audit (task 16) ─────────────────────────────────────────

/// Inverse of `toCoreEffect` — lower a `Fuaran.Core.EffectClass` back to the UI
/// DU, so the audit reports its verdict in the UI's own vocabulary.
let private fromCoreEffect (e: Fuaran.Core.EffectClass) : EffectClass =
    { HostEffect =
        match e.Host with
        | Fuaran.Core.Pure -> HostEffect.Pure
        | Fuaran.Core.ReadsHost -> HostEffect.ReadsHost
        | Fuaran.Core.WritesHost -> HostEffect.WritesHost
      Determinism =
        match e.Determinism with
        | Fuaran.Core.Deterministic -> DeterminismSource.Deterministic
        | Fuaran.Core.Clock -> DeterminismSource.Clock
        | Fuaran.Core.Random -> DeterminismSource.Random
        | Fuaran.Core.Network -> DeterminismSource.Network }

/// Effect-soundness audit for a UI fragment (the runtime teeth on the mandatory
/// effect signature). A `FragmentDecl` MUST declare an `EffectClass` at least as
/// wide as everything its body actually composes — the `EffectClass.covers`
/// understatement rule (`Types.fs`). This wires `Fuaran.Core.Function.observedEffect`
/// (the componentwise join of every node's declared effect over the body's preorder
/// walk) to a UI fragment via `uiWitness`: `Ok ()` when the declaration is honest,
/// `Error (declared, observed)` — both in UI vocabulary — when a descendant leaks a
/// wider effect than the decl admits (e.g. a `pure`-declared fragment nesting a
/// `network` sub-fragment). A non-`FragmentDecl` node declares no effect surface, so
/// it audits as pure-deterministic (the check reduces to "the subtree composes
/// nothing wider than pure"). Closes the declared-vs-observed gap the build-time AST
/// validator cannot reach (it has no typed effect surface — see `FragmentCheck.fs`).
let auditFragmentEffect<'Msg> (node: Node<'Msg>) : Result<unit, EffectClass * EffectClass> =
    match Fuaran.Core.Function.auditEffect uiWitness node with
    | Ok() -> Ok()
    | Error(declared, observed) -> Error(fromCoreEffect declared, fromCoreEffect observed)
