module Fuaran.UI.BindingWalk

// ============================================================================
//  Cross-tree binding collection for validation (Phase 427; the shared walk
//  Phases 421 / 424 / 425 deferred).
//
//  `PreEmitValidate` needs tree-wide facts the per-node checks can't see: which
//  filters / state keys / selections / queries the tree READS (wherever a
//  `Binding<'T>` or `TextSource` appears, in any spec slot), and which filters
//  the tree DECLARES (Filters-node chips). This module is that single walk —
//  a validation-oriented projection of every binding usage, tagged with the
//  reading node's id.
//
//  FORWARD-COUPLING (the same discipline as the renderer's reactive
//  key-collection walk in `Fuaran.UI.Renderer/Render.fs`, which serves
//  render-time subscriptions and mirrors this slot coverage): a new
//  binding-bearing field on any spec — or a new `NodeKind` / `Binding` /
//  `TextSource` case — must extend BOTH walks, else its readers silently
//  escape validation here and reactive subscription there.
//
//  Fable-compatible — no reflection, no server-only API; pattern-matching
//  only (the walk reads key/name strings off each binding, staying generic
//  over `'T` with no type-erasure hazard).
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

/// One observed binding usage — the validation-oriented projection of a
/// `Binding<'T>` read. `Query` carries its `dependsOn` filter names so the
/// consumption-union checks (decorative filter / dangling dependency) can be
/// derived without a second walk. A `Transform` param whose source is a
/// `Binding.Filter` surfaces as `TransformParamFilter` (a declared edge the
/// dangling check must verify against the declared chips), distinct from a
/// plain `Filter` value read (a display read a host may legitimately feed
/// without a chip). `TransformParam` records each declared param name with
/// whether the pipeline's `paramsOf` derivation actually references it.
[<RequireQualifiedAccess>]
type BindingUse =
    | State of key: string
    | Filter of name: string
    | Selection of targetNodeId: string
    | Query of name: string * dependsOn: string list
    | TransformParamFilter of name: string
    | TransformParam of name: string * referenced: bool
    /// A `Binding.Computed` read (Phase 932). The closure is handed the WHOLE
    /// `BindingContext.State` bag, so WHICH keys it reads is unknowable
    /// statically. Recorded as a usage rather than dropped, so a rule reasoning
    /// from the ABSENCE of a read can tell "nothing reads this key" apart from
    /// "this tree cannot be analysed" — the difference between a finding and a
    /// false accusation.
    | Computed
    /// Phase 865 — a `Binding.Transform` whose SOURCE slot
    /// (`TransformSource.Live`) is a `Binding.State`, carrying the key and
    /// whether THAT slot declares its own `defaultValue`. FUARAN105's subject.
    ///
    /// **It IS a `State` read, and it reaches both projections.** Phase 865
    /// recorded it as its own case and deliberately withheld it from
    /// `StateKeyFacts.Reads` and `TreeBindingFacts.Uses`, on the ground that
    /// folding it in would NARROW FUARAN098 (a shipped Warning) on trees that
    /// fire it today — a behaviour change outside the remit of adding a
    /// Warning. That deferral is now discharged, because the narrowing was
    /// MEASURED rather than assumed, and because the walk was contradicting the
    /// renderer beside it:
    ///
    ///  - `Render.keysOfBinding`'s `TransformSource.Live` arm has SUBSCRIBED
    ///    this slot since Phase 818 — a `SetState` on the source key
    ///    re-evaluates the pipeline and re-renders every reader — so a rule
    ///    saying "nothing in the tree reads this key" was denying an edge the
    ///    renderer honours. That is precisely the asymmetry the Phase 936
    ///    walk-conformance census exists to catch, and it went undetected only
    ///    because no census row carried a Transform. One does now.
    ///  - The narrowing measured empty. Over all 157 corpus node fixtures the
    ///    fold changed ONE fixture's read set (`badge-transform-live`, which
    ///    fires nothing) and TWO fixtures' usage lists, and left every defect —
    ///    of every code — byte-identical.
    ///
    /// The distinct case is KEPT rather than collapsed into `State`, because
    /// FUARAN105 needs the `hasDefault` bit and needs to know the read came
    /// from a Transform's source slot specifically.
    ///
    /// Reaching `Uses` moves no verdict either: every consumer of `Uses`
    /// (FUARAN070–076, `AffordanceInertness.bindingFindings`) matches specific
    /// cases and drops the rest, so the consumption-union rules cannot see this
    /// one. What it does reach is `StructuralQuery`'s `BoundTo` index, where a
    /// Transform's source reader is now findable on the State channel — the
    /// answer that arm always said was the honest one.
    | TransformStateSource of key: string * hasDefault: bool
    /// Phase 1075 — a `Binding.State` carrying a PRESENT `defaultValue`: the
    /// declaring reader, and under the seeding rule the thing that puts a value
    /// under `$state.<key>` for every other reader in the tree.
    ///
    /// Carries the value TWICE, on purpose. `value` is the boxed default
    /// exactly as the declaring reader would have resolved it — the renderers
    /// seed the store with THAT, so a grid's `Row seq` stays a `Row seq` and no
    /// reader's `unbox` changes meaning. `fingerprint` is a COMPARISON form:
    /// two readers may spell one table row-major and columnar and mean the same
    /// thing, so FUARAN106 must not read a re-encoding as a disagreement. See
    /// `seedFingerprint`.
    ///
    /// Emitted BESIDE `State key`, never instead of it: a declaring reader is
    /// still a reader, and nothing about the read projection moves.
    /// Filtered OUT of `TreeBindingFacts.Uses` — unlike
    /// `TransformStateSource`, which now reaches it, and for a reason that
    /// does not carry here: a seed declaration rides beside the `State` read
    /// it belongs to, so admitting it would record the same reader against the
    /// same name twice.
    | StateSeed of key: string * value: objnull * fingerprint: objnull
    /// Phase 1075 — an INLINE table carried in the tree, normalised to the
    /// canonical columnar `Table` whatever slot spelled it (a grid/chart
    /// `source` row array, a Transform's `Data` embedded table, a Transform's
    /// live `State` source's carried default). `seedKey` names the state key
    /// that carries it when the slot is a `Binding.State` — the discriminator
    /// FUARAN107 uses to tell "two copies" from "one shared name".
    ///
    /// Also filtered out of `Uses`.
    | InlineTable of table: Fuaran.Core.Table * seedKey: string option

/// One observed usage tagged with the id of the node whose spec reads it.
type NodeBindingUse = { Reader: string; Use: BindingUse }

/// Phase 1075 — one `Binding.State` declaration that seeds its slot.
type StateSeedDecl =
    {
        /// The id of the node whose spec carries the declaring binding.
        Reader: string
        /// The state key seeded.
        Key: string
        /// The boxed default, as the declaring reader resolves it — what the
        /// renderers put in the store.
        Value: objnull
        /// The comparison form (see `BindingUse.StateSeed`).
        Fingerprint: objnull
    }

/// Phase 1075 — one inline table carried in the tree, normalised.
type InlineTableDecl =
    { Reader: string
      SeedKey: string option
      Table: Fuaran.Core.Table }

/// One observed wire-survivable `Action.Call` (Phase 428), collected from the
/// wire-survivable Action slots (`Button.OnClick` / `Form.OnSubmit` /
/// `Modal.OnDismiss`, recursing `Chain`). Closure-held actions are invisible
/// by construction — the walk sees what the wire sees.
type CallUse =
    { Reader: string
      Endpoint: string
      HasOnResult: bool
      Into: CallResultTarget option }

/// One observed CLOSURE-CARRYING action slot (Phase 577) — an `Action` case
/// holding host code that does not survive a serialisation round trip.
///
/// **What actually happens to it, stated precisely, because it is worse than
/// "it becomes a sentinel".** The canonical encoder emits the case's
/// discriminator and DROPS the payload entirely — a `Dispatch` encodes as
/// `{"$type":"Dispatch"}`, with no field where the message was. `"<closure>"`
/// is the DECODER's reconstruction (WIRE_FORMAT §4). So the emitted bytes carry
/// no trace of the loss: nothing downstream of the encoder can tell a
/// `Dispatch` that lost a message from one that never had a payload.
///
/// **Three slots, and the enumeration is the point.** `Action.Dispatch.msg`
/// carries the host's typed message; `Action.Call.onResult` and
/// `Action.ReadFileBody.onRead` carry continuations. Those are the whole of the
/// `Action` DU's closure surface, and every one of them decodes to an inert
/// placeholder (`SlotCapability.decoderPlaceholderSlots`), so a tree carrying
/// one loses the behaviour rather than degrading it.
///
/// **What this deliberately does NOT record.** The other decoder-placeholder
/// slots — a `FormFieldKind.onChange`, a `TabsSpec.onSelect`, a
/// `DisclosureSpec.onToggle` — erase too, but the renderers' write-back default
/// reconstructs the behaviour from the control's own writable binding, so their
/// loss is recoverable and flagging them would report a shape the language
/// sanctions. `Binding.Computed.fn` is likewise out of scope: it is
/// FUARAN084's subject already. This is the residue neither covers.
///
/// `Slot` is spelled with the same `Type.slot` names `SlotCapability` uses, so
/// one vocabulary names these slots across the estate.
type ClosureUse = { Reader: string; Slot: string }

/// The State-channel projection **FUARAN098** runs on (Phase 932) — which keys
/// the tree WRITES with `Action.SetState`, and which it can be shown to READ.
///
/// **What counts as a READ, enumerated rather than assumed.** The rule reasons
/// from the ABSENCE of a read, so an under-broad definition does not merely miss
/// findings — it manufactures false ones, which is how a Warning-severity rule
/// gets suppressed and stops protecting anything. The nine surfaces:
///
///  1. `Binding.State(k, _)` in ANY binding-bearing slot, recursing exactly as
///     `usesOfBinding` does — through `Local.initialFrom`, `Format.source`,
///     `I18n` args, and a `Transform`'s `params[].From`. This is the obvious
///     case and by volume the overwhelming majority.
///  2. A `Switch`'s branch SELECTOR, `SwitchSpec.On` — a `Binding` since
///     Phase 768, not the `StateKey: string` several stale comments still
///     describe. A button writing the key a `Switch` selects on is the canonical
///     honest affordance, so missing this surface alone would false-warn on the
///     single most idiomatic shape in the language.
///  3. A `DataGrid`'s `SortStateKey` and `PageStateKey` — plain STRINGS, not
///     bindings, and genuinely read (`BindingResolver.readSortSlot` /
///     `readPageDescriptor`). `EditStateKey` is deliberately NOT here: it is a
///     write DESTINATION with no reader anywhere in the renderer, so counting it
///     would mask real defects.
///  4. A `FormField` whose `FormFieldKind` value slot is `None` — the Phase 694
///     auto-bind reads `Binding.State(field.Id, _)` with nothing in the tree to
///     see. Already covered by `usesOfFormFieldKind`'s implicit-use argument.
///  5. An `Action.SetState`'s own `valueFrom` binding: a write whose value
///     derives from another key READS that key.
///  6. A `StateBehaviour` subtree — `OnEmpty` / `OnLoading` are wire-encoded
///     child nodes that render in place of the body and may read anything.
///  7. A `FragmentArg.SlotArg` subtree passed through `FragmentRef.Args` or
///     `Mount.Inputs`.
///  8. `Accessibility.Label` / `Hidden`, `Drawing`'s `DrawStyle` colour
///     bindings and `Shape.Label` text — ordinary binding slots, listed because
///     the two tree walks in this estate have each historically missed one of
///     them while covering the other.
///  9. A `Binding.Transform`'s live SOURCE slot,
///     `TransformSource.Live(Binding.State(k, _), _)` — recorded as
///     `BindingUse.TransformStateSource` and withheld from this set by Phase
///     865, folded in once the narrowing was measured. The renderer has
///     subscribed the slot since Phase 818, so withholding it let FUARAN098
///     report a write as invisible that the reactive walk beside it honours.
///     Both source shapes count: whether the slot carries its own default
///     decides FUARAN105's verdict, never whether the key is read.
///
/// Where a surface could not be decided, it is counted AS A READ: over-counting
/// costs a missed finding, under-counting costs a false accusation, and only one
/// of those kills the rule. `Mount.Inputs` is the live instance — a guest renders
/// in an isolated `StateStore.forScope`, so its INTERIOR reads no host key, but a
/// `SlotArg` handed to it is host-authored and its scope at render time is not
/// settled by the type. It is counted.
type StateKeyFacts =
    {
        /// Every `Action.SetState` reachable from a wire-survivable action slot
        /// (`Button.OnClick` / `Form.OnSubmit` / `Modal.OnDismiss`, recursing
        /// `Chain`), as (writing node id, key). Closure-held handlers are
        /// invisible by construction — the walk sees what the wire sees.
        Writes: (string * string) list
        /// Every state key the tree can be SHOWN to read, per the eight surfaces
        /// above.
        Reads: Set<string>
        /// True when the tree holds a reader whose state access cannot be seen —
        /// a `Binding.Computed` closure (handed the whole state bag) or a
        /// `NodeKind.Custom` node (whose registered host renderer may read
        /// anything). Under either, the absence of a read PROVES nothing, so
        /// FUARAN098 stands down for the whole tree rather than guessing.
        OpaqueReader: bool
        /// Every state key the tree can be shown to WRITE, from EVERY write
        /// surface — not only `Action.SetState`. Held BESIDE `Writes` rather
        /// than replacing it: `Writes` is the (writer, key) list FUARAN098
        /// iterates, and widening it would newly fire a shipped Warning on
        /// trees that pass today. The surfaces counted here:
        ///
        ///  1. `Action.SetState`'s key, reachable from a wire-survivable slot.
        ///  2. `Action.Call`'s `into: State <key>` result target.
        ///  3. A control's WRITE-BACK slot bound to `Binding.State(k, _)` —
        ///     `Select.value` / `.values`, `Tabs.activeIndex` / `.activeTag`,
        ///     `Stepper.activeStep`, `Disclosure.open`, `Modal.open`,
        ///     `Toast.open`, and every `FormField` value slot. The renderer
        ///     writes these back when no handler is supplied.
        ///  4. A `FormField` whose value slot is `None` — the Phase 694
        ///     auto-bind writes back to `State(field.Id)`.
        ///  5. A `DataGrid`'s `sortStateKey` / `pageStateKey` / `editStateKey`
        ///     — plain STRINGS the renderer writes with no `Binding` to see,
        ///     and an editable grid whose `source` is a direct `Binding.State`
        ///     (the Phase 663 commit destination).
        WriteKeys: Set<string>
        /// True when the tree holds a writer whose DESTINATION cannot be seen —
        /// the write-side twin of `OpaqueReader`, and the reason FUARAN103 can
        /// afford to be a finding at all. A closure produces an arbitrary
        /// `Action` at dispatch time, so it may write any key: a control's
        /// `onChange` / `onToggle` / `onSelect` / `onDismiss` handler, a
        /// grid's closure-bearing cell kinds, a `Binding.Local`'s `onCommit`,
        /// an `Action.Call`'s `onResult`, and the host-crossing actions
        /// (`Dispatch` / `Invoke` / `AiTool` / `CommitLocal` / `ReadFileBody`).
        /// So are a `NodeKind.Custom` node's registered renderer and a `Mount`
        /// guest. Under any of them the absence of a write proves nothing.
        OpaqueWriter: bool
        /// Every `NodeKind.Switch` whose branch SELECTOR (`SwitchSpec.On`, a
        /// `Binding` since Phase 768) reads a state key, as (switch node id,
        /// key). Collected explicitly rather than recovered from `Uses`: a
        /// Switch node's accessibility slots are State-bindable too, so a
        /// reader-tagged `State` use on a Switch does not identify the
        /// selector, and the rule that reasons about it must not guess.
        SwitchSelectors: (string * string) list
        /// Phase 865 — every `Binding.Transform` in the tree whose source slot
        /// is a **default-less** `Binding.State`, as (reading node id, key).
        /// FUARAN105's subjects.
        ///
        /// A source that DOES carry a `defaultValue` is not recorded: the
        /// decoder derives the Transform's initial snapshot table from that
        /// carried default, so the pipeline runs over real rows rather than
        /// `TransformLive.emptySource`, and the silent zero cannot arise. A
        /// SIBLING reader's default is deliberately NOT a rescuer — under the
        /// shipped resolver semantics `Binding.State`'s `defaultValue` is a
        /// per-reader fallback, not a slot seed, so it never reaches this
        /// Transform. That reading is the one the deferral of the seeding rule
        /// requires; see `PreEmitDefect.TransformSourceInert`.
        TransformInertSources: (string * string) list
        /// Phase 1075 — every `Binding.State` in the tree carrying a present
        /// `defaultValue`, in walk order. Under the seeding rule the FIRST
        /// declaration of a key seeds the slot; the rest are either agreements
        /// (identical fingerprint — harmless) or conflicts (FUARAN106).
        ///
        /// Host-reserved keys are NOT filtered here: the walk reports what the
        /// tree says, and refusing a tree-originated write to a host slot
        /// (Phase 782) is the SEEDING pass's job, where the refusal belongs
        /// beside every other one. A rule that wants the filtered set applies
        /// `StateKeyPolicy.isHostReserved` itself, as `stateSeeds` does.
        Seeds: StateSeedDecl list
        /// Phase 1075 — every inline table the tree carries, normalised to the
        /// canonical columnar shape. FUARAN107's subjects.
        InlineTables: InlineTableDecl list
    }

/// The tree-wide facts `PreEmitValidate`'s cross-tree checks run on.
type TreeBindingFacts =
    {
        /// Every binding usage in the tree, reader-tagged.
        Uses: NodeBindingUse list
        /// Every declared filter chip: (owning Filters-node id, `FilterSpec.Name`).
        DeclaredFilters: (string * string) list
        /// Every wire-survivable `Action.Call` in the tree, reader-tagged (Phase 428).
        Calls: CallUse list
        /// Every closure-carrying action slot in the tree, reader-tagged
        /// (Phase 577). The subject of **FUARAN112** and of
        /// `CanonicalJson.encodeNodeForTransport`'s refusal — see `ClosureUse`
        /// for what the three slots are and what is deliberately not in them.
        Closures: ClosureUse list
        /// Every node id in the tree, mapped to whether the node is a
        /// selection PRODUCER — a `Visualisation` kind (the grid's Phase 427
        /// default row-click write; charts/tables/maps via host closures).
        Nodes: Map<string, bool>
        /// The State-channel read/write projection FUARAN098 runs on (Phase 932).
        /// Held BESIDE `Uses` rather than folded into it: `Uses` records one
        /// entry per binding usage (a consumption edge, reader-tagged), while
        /// this is a per-KEY projection over surfaces that are not bindings at
        /// all — a grid's `sortStateKey` string, an absent form-field value
        /// slot, a subtree walked for state facts alone. Neither is derivable
        /// from the other.
        ///
        /// The two sets are no longer allowed to drift SILENTLY, which is the
        /// part that changed. `TransformStateSource` sat in neither, on the
        /// ground that admitting it would move shipped rules' verdicts; the
        /// blast radius was measured (no defect of any code changed, on any of
        /// the 157 corpus node fixtures) and it now reaches both. A future
        /// surface that belongs in one and not the other says so in its own
        /// case's doc comment, with the measurement.
        StateKeys: StateKeyFacts
    }

/// Binding usages read by a single binding, recursing into a `Local` binding's
/// re-sync source, `I18n` `{arg}` sub-bindings, a `Format` binding's numeric
/// source, and a parameterised `Transform`'s param sources — the same
/// recursion contract as the renderer's reactive walk.
/// Phase 1075 — normalise a boxed seed value to the form FUARAN106 compares.
///
/// Two readers may spell ONE table two ways — a grid's `source` is a row-major
/// array of row objects, a Transform's live `State` source carries the same
/// data as canonical columnar — and a conflict rule that read a re-encoding as
/// a disagreement would raise an Error on the most idiomatic shape the pack
/// teaches. So a tabular seed normalises through the SAME transpose + columnar
/// decode the decode-time snapshot uses (`TransformLive.initialSource`), and
/// everything else is compared as it stands.
///
/// A non-tabular value falls back to the value itself: a scalar seed
/// (`"Ada"`, `3.0`, `false`) compares structurally in both hosts, and a `JVal`
/// that is not a table compares as the `JVal` DU it is.
let private seedFingerprint (v: objnull) : objnull =
    match v with
    | :? JVal as jv ->
        match HostPrelude.TransformLive.initialSource jv with
        | Ok(Fuaran.Core.Embedded t) -> box t
        | _ -> box jv
    | other -> other

/// Phase 1075 — the EMPTY table, which is what a seed must not be.
///
/// `defaultValue: []` is the identity of the seeding lattice, not a claim about
/// content: an unseeded slot already resolves to `TransformLive.emptySource`,
/// so an empty declaration adds nothing an absent one does not already say.
/// Two consequences, and BOTH are load-bearing rather than tidy.
///
/// It must not WIN the first-declaration race, or a badge spelling
/// `{"$type":"State","key":"members","defaultValue":[]}` — today the only way
/// to say "I read this slot and carry nothing" in a Transform's source slot —
/// would seed the slot empty whenever it appeared before the grid that carries
/// the rows, and the charter's §5 order-independence would be false.
///
/// And it must not CONFLICT, or that same pair would raise FUARAN106 against
/// the grid beside it — an Error on the document the seeding rule exists to
/// make work.
let private emptyTable: Fuaran.Core.Table = { Schema = []; Columns = [] }

let isEmptySeed (fingerprint: objnull) : bool =
    match fingerprint with
    | :? Fuaran.Core.Table as t -> t = emptyTable
    | _ -> false

/// Phase 1075 — normalise a ROW-MAJOR feed (a grid / chart `source`) to the
/// canonical columnar `Table`, through the same path a live Transform source
/// takes. `None` when the rows do not decode as a table (a ragged row set —
/// Core's schema inference is deliberately loud rather than patching).
let private tableOfRows (rows: Fuaran.Core.Row seq) : Fuaran.Core.Table option =
    match HostPrelude.TransformLive.initialSource (Fuaran.Core.RowCodec.encodeRows rows) with
    | Ok(Fuaran.Core.Embedded t) -> Some t
    | Ok(Fuaran.Core.Ref _)
    | Error _ -> None

let rec usesOfBinding<'T> (binding: Binding<'T>) : BindingUse list =
    match binding with
    // Phase 1075 — a PRESENT default is a seed declaration as well as a read.
    | Binding.State(key, Some d) ->
        let boxed = box d

        [ BindingUse.State key
          BindingUse.StateSeed(key, boxed, seedFingerprint boxed) ]
    | Binding.State(key, _) -> [ BindingUse.State key ]
    | Binding.Filter(name, _) -> [ BindingUse.Filter name ]
    | Binding.Selection(nodeId, _, _, _) -> [ BindingUse.Selection nodeId ]
    | Binding.Query(name, _, dependsOn) -> [ BindingUse.Query(name, defaultArg dependsOn []) ]
    // Phase 765 — `Now` reads no node, state key, filter or query: the host
    // furnishes it once per render pass. It participates in no reactive edge,
    // so it contributes no usage (the `Computed` posture below).
    | Binding.Now _ -> []
    | Binding.Local(_, _, initialFrom, _, _) -> usesOfBinding initialFrom
    | Binding.I18n(_, Some args) -> args |> Map.toList |> List.collect (fun (_, ab) -> usesOfBinding<JVal> ab)
    | Binding.I18n(_, None) -> []
    | Binding.Format(source, _, _) -> usesOfBinding source
    | Binding.Transform(source, pipeline, parameters) ->
        // The pure `Transform.paramsOf` derivation (fuaran-core#77) names every
        // param the pipeline actually references — a declared `params` entry
        // outside it is dead weight (FUARAN076).
        let referenced = Fuaran.Core.Transform.paramsOf pipeline |> Set.ofList

        // Phase 865 — the SOURCE slot, recorded as its own case rather than as a
        // `State` read (see `BindingUse.TransformStateSource`). A `Data` source
        // is columnar/`ref` and names no state key; a `Live` source over any
        // other binding shape is not FUARAN105's subject.
        // Phase 1075 — the same slot, read twice more. A live `State` source
        // carrying a default is a SEED declaration like any other
        // `Binding.State` (charter §4: the declaring reader is any
        // `Binding.State` with a present `defaultValue`), and the table it
        // carries — or the `Data` arm's embedded table — is an INLINE TABLE
        // FUARAN107 compares against every other copy in the tree.
        //
        // It still does NOT emit `BindingUse.State key`: 865's reasoning is
        // unchanged by seeding, because widening `Reads` narrows FUARAN098 on
        // trees that fire it today, which is a different phase's work.
        let sourceUse =
            match source with
            | TransformSource.Live(Binding.State(key, defaultValue), initial) ->
                let seed =
                    match defaultValue with
                    | Some dv ->
                        let boxed = box dv
                        [ BindingUse.StateSeed(key, boxed, seedFingerprint boxed) ]
                    | None -> []

                let inline_ =
                    match defaultValue, initial with
                    | Some _, Fuaran.Core.Embedded t -> [ BindingUse.InlineTable(t, Some key) ]
                    | _ -> []

                BindingUse.TransformStateSource(key, defaultValue.IsSome) :: (seed @ inline_)
            | TransformSource.Data(Fuaran.Core.Embedded t) -> [ BindingUse.InlineTable(t, None) ]
            | TransformSource.Live _
            | TransformSource.Data _ -> []

        sourceUse
        @ (defaultArg parameters []
           |> List.collect (fun (p: TransformParam) ->
               let sourceUses =
                   usesOfBinding p.From
                   |> List.map (function
                       // A param's Filter source is the DECLARED filter→consumer
                       // edge (the 424 construct) — distinct from a plain value
                       // read for the dangling / consumption checks.
                       | BindingUse.Filter filterName -> BindingUse.TransformParamFilter filterName
                       | other -> other)

               BindingUse.TransformParam(p.Name, Set.contains p.Name referenced) :: sourceUses))
    // Phase 932 — `Computed` reads the whole state bag through a closure, so it
    // is an OPAQUE read, not an absent one. See `BindingUse.Computed`.
    | Binding.Computed _ -> [ BindingUse.Computed ]
    | Binding.Invoke _
    | Binding.Static _ -> []

/// Binding usages read by a `TextSource`. `Literal` carries none; `Bound`
/// defers to its binding; `TextSource.I18n` args are `JVal` literals.
let usesOfText (text: TextSource) : BindingUse list =
    match text with
    | TextSource.Bound b -> usesOfBinding b
    | TextSource.Literal _
    | TextSource.I18n _ -> []

let private usesOfTextOpt (text: TextSource option) : BindingUse list =
    match text with
    | Some t -> usesOfText t
    | None -> []

/// Phase 1075 — binding usages of a ROW-FEED slot (a grid's / chart's
/// `source`), which the generic walk cannot fully normalise.
///
/// Two things happen here that `usesOfBinding` structurally cannot. The seed
/// FINGERPRINT is re-derived from the typed rows: the generic walk boxes a
/// `Row seq` and has no portable way to recognise it, so a grid seeding
/// `members` row-major and a Transform seeding it columnar would read as a
/// disagreement. And a row array — whether it rides a `State` default or a
/// `Static` value — IS an inline table, so it is recorded as one.
///
/// The seed VALUE is untouched: the store must receive the `Row seq` the grid
/// itself resolves, not a re-encoding of it.
let private rowFeedUses (binding: Binding<Fuaran.Core.Row seq>) : BindingUse list =
    let table =
        match binding with
        | Binding.State(_, Some rows)
        | Binding.Static(Some rows) -> tableOfRows rows
        | _ -> None

    let normalised =
        usesOfBinding binding
        |> List.map (fun u ->
            match u, table with
            | BindingUse.StateSeed(k, v, _), Some t -> BindingUse.StateSeed(k, v, box t)
            | _ -> u)

    let inline_ =
        match binding, table with
        | Binding.State(key, Some _), Some t -> [ BindingUse.InlineTable(t, Some key) ]
        | Binding.Static(Some _), Some t -> [ BindingUse.InlineTable(t, None) ]
        | _ -> []

    normalised @ inline_

let private usesOfBindingOpt (binding: Binding<'T> option) : BindingUse list =
    match binding with
    | Some b -> usesOfBinding b
    | None -> []

let private usesOfFormFieldKind<'Msg> (implicitUse: BindingUse option) (kind: FormFieldKind<'Msg>) : BindingUse list =
    // Value slots are `option` since the swap (Phase 596 auto-bind — absence
    // is legal wire); constraints ride flat (min/max/step) rather than as the
    // retired constraint records.
    //
    // Phase 694 — a `None` value slot IS a read: the renderer substitutes the
    // context's auto-binding at render time (decode no longer synthesises it
    // into the tree), so the walker contributes `implicitUse` for absence —
    // `State(field id)` in a form, `Filter(name)` on a chip — keeping the
    // wiring lint and resume analysis semantically identical to the old
    // decode-synthesised shape.
    let usesOfValueSlot (v: Binding<'x> option) : BindingUse list =
        match v with
        | Some b -> usesOfBinding b
        | None -> Option.toList implicitUse

    match kind with
    | FormFieldKind.Text(v, _) -> usesOfValueSlot v
    | FormFieldKind.Number(v, _) -> usesOfValueSlot v
    | FormFieldKind.Checkbox(v, _) -> usesOfValueSlot v
    | FormFieldKind.Toggle(v, _) -> usesOfValueSlot v
    | FormFieldKind.TextArea(v, _, _) -> usesOfValueSlot v
    | FormFieldKind.RangedNumber(v, _, _, _, _) -> usesOfValueSlot v
    | FormFieldKind.Range(v, _, _, _, _) -> usesOfValueSlot v
    | FormFieldKind.Choice(opts, value, _) -> usesOfBinding opts @ usesOfValueSlot value
    // Phase 1113 — the combobox's option source is an ordinary binding, so a
    // Query-bound suggestion source is a real read and is walked as one.
    | FormFieldKind.Combobox(_, _, opts, value) -> usesOfBinding opts @ usesOfValueSlot value
    | FormFieldKind.SegmentedChoice(opts, value, _, _) -> usesOfBinding opts @ usesOfValueSlot value
    | FormFieldKind.Date(v, _, _, _, _, _) -> usesOfValueSlot v
    | FormFieldKind.DateRange(v, _, _, _, _, _) -> usesOfValueSlot v

/// The `Action.Call`s reachable from a wire-survivable action value,
/// recursing `Chain` (Phase 428). Non-Call arms carry no fetch.
let rec callsOfAction<'Msg> (readerId: string) (action: Action<'Msg>) : CallUse list =
    match action with
    | Action.Call(endpoint, onResult, into) ->
        [ { Reader = readerId
            Endpoint = endpoint
            HasOnResult = onResult.IsSome
            Into = into } ]
    | Action.Chain actions -> actions |> List.collect (callsOfAction readerId)
    | Action.Dispatch _
    | Action.Notify _
    | Action.Navigate _
    | Action.SetState _
    | Action.AiTool _
    | Action.CommitLocal _
    | Action.WriteToClipboard _
    | Action.ReadFileBody _
    | Action.Invoke _ -> []

/// Closure-carrying slots held by an ACTION value, recursing `Chain` — the
/// sibling of `callsOfAction` (Phase 577). Exhaustive by construction: no
/// wildcard, so a new `Action` case must be classified here rather than
/// silently escaping the transport refusal and FUARAN112.
let rec closuresOfAction<'Msg> (readerId: string) (action: Action<'Msg>) : ClosureUse list =
    match action with
    | Action.Dispatch _ ->
        [ { Reader = readerId
            Slot = "Action.Dispatch.msg" } ]
    | Action.Call(_, onResult, _) ->
        if onResult.IsSome then
            [ { Reader = readerId
                Slot = "Action.Call.onResult" } ]
        else
            []
    | Action.ReadFileBody(_, _, _, onRead) ->
        if onRead.IsSome then
            [ { Reader = readerId
                Slot = "Action.ReadFileBody.onRead" } ]
        else
            []
    | Action.Chain actions -> actions |> List.collect (closuresOfAction readerId)
    // The closure-free arms. `Invoke` reaches a host capability by ID with
    // wire-encoded args, and `AiTool` by tool name — neither holds host code.
    | Action.Notify _
    | Action.Navigate _
    | Action.SetState _
    | Action.AiTool _
    | Action.CommitLocal _
    | Action.WriteToClipboard _
    | Action.Invoke _ -> []

/// Binding usages carried by an ACTION value, recursing `Chain` — the sibling
/// of `callsOfAction`, and the arm of the walk that was missing.
///
/// `Action.SetState`'s `valueFrom` (Phase 818) is the only binding-bearing
/// action slot in the vocabulary: every other arm carries strings, a `JVal`
/// literal, an `InvokeArg` pair of strings, or a closure the wire cannot see.
/// So this reads as one arm and a long tail of empties — which is exactly why
/// it is written as an EXHAUSTIVE match over the DU rather than one case and a
/// wildcard. Its whole job is to be the place the compiler stops a new
/// binding-bearing action arm, the way `callsOfAction` beside it does for a new
/// fetch-bearing one.
///
/// A `valueFrom` read is a DISPATCH-TIME read: it resolves when the gesture
/// fires, not at render, which is why the reactive walk deliberately does not
/// subscribe it (the recorded asymmetry in the Phase 936 census). Analysis
/// counts it regardless — the tree does read that key/filter/query, and a
/// consumption rule reasoning from its absence would be reasoning from a
/// surface it simply never looked at.
let rec usesOfAction<'Msg> (action: Action<'Msg>) : BindingUse list =
    match action with
    | Action.SetState(_, _, Some valueFrom) -> usesOfBinding valueFrom
    | Action.SetState(_, _, None) -> []
    | Action.Chain actions -> actions |> List.collect usesOfAction
    | Action.Call _
    | Action.Dispatch _
    | Action.Notify _
    | Action.Navigate _
    | Action.AiTool _
    | Action.CommitLocal _
    | Action.WriteToClipboard _
    | Action.ReadFileBody _
    | Action.Invoke _ -> []

/// The State key a WRITE-BACK slot commits to, and whether committing also runs
/// host code that may write elsewhere. A slot holding any other binding shape
/// gives the renderer's write-back default nowhere to write — the FUARAN069
/// inert-control condition — so it contributes no write. A `Local` buffers and
/// commits to whatever it re-syncs FROM, so its destination is `initialFrom`'s;
/// its `onCommit` hook is host code layered on top of that.
let rec writeBackTargetOf<'T> (binding: Binding<'T>) : string option * bool =
    match binding with
    | Binding.State(key, _) -> Some key, false
    | Binding.Local(_, _, initialFrom, onCommit, _) ->
        let key, opaque = writeBackTargetOf initialFrom
        key, opaque || onCommit.IsSome
    | _ -> None, false

/// The write-side facts of one `FormFieldKind`'s value slot.
type FormFieldWrite =
    {
        /// The State key an explicit value binding commits to.
        Target: string option
        /// True when the value slot is ABSENT, so the Phase 694 auto-bind
        /// decides the destination from the field's own id (in a form) or the
        /// FilterStore (on a chip) — a distinction only the caller can make.
        SlotAbsent: bool
        /// True when a change handler, or a `Local`'s `onCommit`, may write
        /// somewhere this walk cannot see.
        Opaque: bool
    }

/// `writeBackTargetOf` over a `FormFieldKind`'s value slot, plus its handler.
/// One arm per case so a new field kind is a compile error here rather than a
/// silently-uncounted writer — the same forward-coupling posture the read walk
/// takes in `usesOfFormFieldKind`.
let formFieldWriteFacts<'Msg> (kind: FormFieldKind<'Msg>) : FormFieldWrite =
    let slot v hasHandler =
        match v with
        | Some b ->
            let target, opaque = writeBackTargetOf b

            { Target = target
              SlotAbsent = false
              Opaque = opaque || hasHandler }
        | None ->
            { Target = None
              SlotAbsent = true
              Opaque = hasHandler }

    match kind with
    | FormFieldKind.Text(v, h) -> slot v h.IsSome
    | FormFieldKind.Number(v, h) -> slot v h.IsSome
    | FormFieldKind.Checkbox(v, h) -> slot v h.IsSome
    | FormFieldKind.Toggle(v, h) -> slot v h.IsSome
    | FormFieldKind.TextArea(v, h, _) -> slot v h.IsSome
    | FormFieldKind.RangedNumber(v, h, _, _, _) -> slot v h.IsSome
    | FormFieldKind.Range(v, h, _, _, _) -> slot v h.IsSome
    | FormFieldKind.Choice(_, v, h) -> slot v h.IsSome
    | FormFieldKind.Combobox(_, h, _, v) -> slot v h.IsSome
    | FormFieldKind.SegmentedChoice(_, v, h, _) -> slot v h.IsSome
    | FormFieldKind.Date(v, h, _, _, _, _) -> slot v h.IsSome
    | FormFieldKind.DateRange(v, h, _, _, _, _) -> slot v h.IsSome

/// Collect the tree-wide binding facts for `node` (see `TreeBindingFacts`),
/// descending through layout children, error-boundary subtrees, and
/// fragment-decl bodies (`FragmentRef` carries no body; a `Mount` guest is an
/// opaque isolation boundary — both contribute their own node id only).
let collect<'Msg> (root: Node<'Msg>) : TreeBindingFacts =
    let uses = ResizeArray<NodeBindingUse>()
    let declaredFilters = ResizeArray<string * string>()
    let calls = ResizeArray<CallUse>()
    let closures = ResizeArray<ClosureUse>()
    let nodes = System.Collections.Generic.Dictionary<string, bool>()

    // ── The Phase 932 State-channel projection (see `StateKeyFacts`) ──
    let stateReads = System.Collections.Generic.HashSet<string>()
    let stateWrites = ResizeArray<string * string>()
    let mutable opaqueReader = false

    // ── The write-side projection FUARAN103 runs on ──
    let stateWriteKeys = System.Collections.Generic.HashSet<string>()
    let switchSelectors = ResizeArray<string * string>()
    let mutable opaqueWriter = false

    // ── The Phase 865 read-side projection FUARAN105 runs on ──
    let transformInertSources = ResizeArray<string * string>()

    // ── The Phase 1075 seeding projection (the resolver's seed map, FUARAN106,
    //    FUARAN107) ──
    let seeds = ResizeArray<StateSeedDecl>()
    let inlineTables = ResizeArray<InlineTableDecl>()

    /// An EMPTY table is not a copy of anything. `TransformLive.emptySource` is
    /// what a live source with no data decodes to and what `[]` normalises to,
    /// so recording it would make every pair of empty sources in one tree read
    /// as duplicated data.
    let isNonEmptyTable (t: Fuaran.Core.Table) =
        not (List.isEmpty t.Columns) || not (List.isEmpty t.Schema)

    /// A closure produces an arbitrary `Action` at dispatch time, so it may
    /// write any key. Seeing one stands the write-side rule down for the whole
    /// tree — over-counting an opaque writer costs a missed finding, and the
    /// alternative costs a false accusation of a tree that is perfectly correct.
    let noteOpaqueIf (present: bool) =
        if present then
            opaqueWriter <- true

    /// Fold one `writeBackTargetOf` result into the write projection.
    let noteWriteBack (target: string option, opaque: bool) =
        target |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)
        noteOpaqueIf opaque

    /// A `FormFieldKind`'s value slot is a write-back DESTINATION.
    /// `implicitKey` is what the Phase 694 auto-bind writes when the slot is
    /// ABSENT: the field's own id inside a form, and NOTHING on a filter chip,
    /// whose channel is the FilterStore rather than the State store.
    let recordFormFieldWrites (implicitKey: string option) (kind: FormFieldKind<'Msg>) =
        let facts = formFieldWriteFacts kind
        noteWriteBack (facts.Target, facts.Opaque)

        if facts.SlotAbsent then
            implicitKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)

    /// Fold usages into the STATE projection only. Every read surface reaches
    /// this, including the ones deliberately kept out of `Uses`. `readerId` is
    /// the node whose spec holds the binding — carried because FUARAN105 names
    /// the reading node, and this is the one fold every read surface reaches.
    let recordStateOf (readerId: string) (found: BindingUse list) =
        for u in found do
            match u with
            | BindingUse.State k -> stateReads.Add k |> ignore
            | BindingUse.Computed -> opaqueReader <- true
            // A Transform's live `State` source IS a read of that key, and the
            // fold belongs here rather than in `Uses` — see
            // `BindingUse.TransformStateSource`.
            //
            // Phase 865 additionally records the DEFAULT-LESS source as
            // FUARAN105's subject; a source carrying its own default is what
            // makes the initial snapshot real, so it is a read and nothing
            // more.
            | BindingUse.TransformStateSource(key, hasDefault) ->
                stateReads.Add key |> ignore

                if not hasDefault then
                    transformInertSources.Add(readerId, key)
            // Phase 1075 — the seeding projection. Every read surface reaches
            // this fold, which is what makes the seed map slot-complete rather
            // than complete only where `Uses` happens to be.
            | BindingUse.StateSeed(key, value, fingerprint) ->
                seeds.Add
                    { Reader = readerId
                      Key = key
                      Value = value
                      Fingerprint = fingerprint }
            | BindingUse.InlineTable(table, seedKey) ->
                if isNonEmptyTable table then
                    inlineTables.Add
                        { Reader = readerId
                          SeedKey = seedKey
                          Table = table }
            | _ -> ()

    // `inUses` is false while walking a subtree that contributes STATE facts but
    // must not widen `Uses` / `Calls` / `Nodes` — a `StateBehaviour` branch or a
    // `SlotArg` argument tree. Those subtrees were never walked here before, so
    // feeding them into the consumption-union checks would change five shipped
    // rules' verdicts as a side-effect of adding a Warning.
    let record (inUses: bool) (readerId: string) (found: BindingUse list) =
        recordStateOf readerId found

        if inUses then
            for u in found do
                match u with
                // Phase 1075 — the same posture for the two seeding cases. A
                // declaring reader still contributes its `State key` read to
                // `Uses`; the declaration itself is a fact about the SLOT, not
                // a consumption edge, and widening `Uses` would move five
                // shipped Error-severity rules' verdicts as a side effect.
                | BindingUse.StateSeed _
                | BindingUse.InlineTable _ -> ()
                | _ -> uses.Add { Reader = readerId; Use = u }

    /// Every `SetState` reachable from a wire-survivable action slot: the WRITE
    /// side. The `valueFrom` READ is collected by `usesOfAction` in
    /// `recordCalls` — through `record`, so it reaches `Uses` as well as the
    /// state projection — and must NOT be folded a second time here: `seeds`,
    /// `inlineTables` and `transformInertSources` are lists, so a doubled fold
    /// would report FUARAN105/106/107 twice on one slot.
    let rec recordStateAction (readerId: string) (action: Action<'Msg>) =
        match action with
        | Action.SetState(key, _, _) ->
            stateWrites.Add(readerId, key)
            stateWriteKeys.Add key |> ignore
        | Action.Chain actions -> actions |> List.iter (recordStateAction readerId)
        // A declared result target names its destination; an `onResult` closure
        // does not, and may write anything at all.
        | Action.Call(_, onResult, into) ->
            match into with
            | Some(CallResultTarget.State key) -> stateWriteKeys.Add key |> ignore
            | _ -> ()

            noteOpaqueIf onResult.IsSome
        // The host-crossing arms. Each hands control to code the tree cannot
        // see, which may write the store directly.
        | Action.Dispatch _
        | Action.Invoke _
        | Action.AiTool _
        | Action.CommitLocal _
        | Action.ReadFileBody _ -> opaqueWriter <- true
        | Action.Navigate _
        | Action.Notify _
        | Action.WriteToClipboard _ -> ()

    let recordCalls (inUses: bool) (readerId: string) (action: Action<'Msg>) =
        recordStateAction readerId action
        record inUses readerId (usesOfAction action)

        if inUses then
            for c in callsOfAction readerId action do
                calls.Add c

            for c in closuresOfAction readerId action do
                closures.Add c

    let rec walk (inUses: bool) (n: Node<'Msg>) =
        let readerId = n.Id

        let isProducer =
            match Kind.category n.Kind with
            | NodeCategory.Visualisation -> true
            | _ -> false

        if inUses then
            nodes[readerId] <- isProducer

        match n.Accessibility with
        | Some a -> record inUses readerId (usesOfBindingOpt a.Label @ usesOfBindingOpt a.Hidden)
        | None -> ()

        // Phase 1112 — the node-level tooltip trait. A `TextSource.Bound` hint
        // is a real binding read: the renderer resolves it against the same
        // sources every other bound text is resolved against, so leaving it out
        // of the walk would make a state key that ONLY a tooltip reads look
        // unread — and the unwired-producer diagnostics quantify over exactly
        // that.
        record inUses readerId (usesOfTextOpt n.Tooltip)

        // A `StateBehaviour` branch is a wire-encoded child node rendered in
        // place of the body — a real reader the walk never descended into.
        match n.State with
        | Some sb ->
            sb.OnEmpty |> Option.iter (walk inUses)
            sb.OnLoading |> Option.iter (walk inUses)
        | None -> ()

        // Phase 692 — one exhaustive match over the flat vocabulary, where this
        // was four nested ones under the category envelope. Every arm yields
        // `(the bindings it reads, the children to walk)`; only the container
        // kinds have children, so the rest yield `[]`.
        let directUses, children =
            match n.Kind with
            // ── Layout ──
            | NodeKind.Box s -> usesOfTextOpt s.Heading, s.Children
            | NodeKind.SplitPanel s -> [], s.Children
            | NodeKind.SummaryList s -> usesOfTextOpt s.Heading, s.Children
            | NodeKind.Stepper s ->
                noteWriteBack (writeBackTargetOf s.ActiveStep)
                noteOpaqueIf s.OnSelect.IsSome
                usesOfBinding s.ActiveStep, s.Children
            | NodeKind.Disclosure s ->
                noteWriteBack (writeBackTargetOf s.Open)
                noteOpaqueIf s.OnToggle.IsSome
                (usesOfText s.Heading @ usesOfBinding s.Open), s.Children
            | NodeKind.Tabs s ->
                noteWriteBack (writeBackTargetOf s.ActiveIndex)
                s.ActiveTag |> Option.iter (writeBackTargetOf >> noteWriteBack)
                noteOpaqueIf (s.OnSelect.IsSome || s.OnSelectTag.IsSome)

                let headerUses =
                    match s.TabHeaders with
                    | Some headers ->
                        headers
                        |> List.collect (fun h -> usesOfText h.Label @ usesOfBindingOpt h.Disabled)
                    | None -> []

                (usesOfBinding s.ActiveIndex @ usesOfBindingOpt s.ActiveTag @ headerUses), s.Children
            | NodeKind.Modal s ->
                // Modal's OnDismiss is the wire-survivable Action slot (Phase 428).
                s.OnDismiss |> Option.iter (recordCalls inUses readerId)
                // A dismissable modal's close gesture writes its own `open` slot.
                noteWriteBack (writeBackTargetOf s.Open)
                (usesOfTextOpt s.Heading @ usesOfBinding s.Open), s.Children
            | NodeKind.ScrollArea s -> [], s.Children
            // ── Display ──
            | NodeKind.Heading h -> usesOfText h.Text, []
            | NodeKind.Markdown m -> usesOfText m.Text, []
            | NodeKind.Metric k ->
                let uses =
                    usesOfText k.Label
                    @ usesOfBinding k.Value
                    @ usesOfBindingOpt k.Trend
                    @ usesOfTextOpt k.Subtext

                uses, []
            | NodeKind.Badge b -> usesOfText b.Label, []
            | NodeKind.Sparkline s -> usesOfBinding s.Source, []
            | NodeKind.Callout c -> usesOfTextOpt c.Heading @ usesOfText c.Body, []
            | NodeKind.Progress p -> usesOfBinding p.Fraction @ usesOfTextOpt p.Label @ usesOfTextOpt p.Caveat, []
            | NodeKind.Skeleton _ -> [], []
            | NodeKind.Icon _ -> [], []
            | NodeKind.LabelValueRow r -> usesOfText r.Label @ usesOfBinding r.Value @ usesOfTextOpt r.Help, []
            | NodeKind.Fact fa -> usesOfText fa.Label @ usesOfText fa.Value @ usesOfTextOpt fa.Help, []
            | NodeKind.Link l -> usesOfBinding l.Href @ usesOfText l.Label, []
            // Phase 1079 — `Caption` (Phase 1078) and every `SrcSet` candidate's
            // `Src` (Phase 1080) join `Src`/`Alt` here, and the omission they
            // close is the one this walk exists to prevent. Both slots hold
            // ordinary bindings over the same sources `Src` and `Alt` use, so a
            // caption bound to a State key, or a candidate resolved from a
            // query, was invisible to every analysis that runs on this walk:
            // unknown-key detection, write-back inference, the dependency
            // report. Neither slot arm was written; each new field simply landed
            // green, which is exactly the residue `WalkConformanceTests`'s own
            // header predicted for a new FIELD on an existing spec. The census
            // rows added with this fix are what make reverting either line red.
            | NodeKind.Image i ->
                usesOfBinding i.Src
                @ usesOfText i.Alt
                @ usesOfTextOpt i.Caption
                @ (i.SrcSet |> List.collect (fun e -> usesOfBinding e.Src)),
                []
            // Phase 1076 — three reactive slots, and the third is the one a
            // walk written from the record's field list would miss: `Poster`
            // lives inside the `MediaKind.Video` case payload, not on the spec.
            // A poster resolved from a query is as much a dependency as the
            // primary `Src`, so the case is matched rather than the slot
            // ignored; `Audio` genuinely contributes nothing beyond the shared
            // pair.
            | NodeKind.Media m ->
                let kindUses =
                    match m.Kind with
                    | MediaKind.Video(_, poster) -> usesOfBindingOpt poster
                    | MediaKind.Audio -> []

                usesOfBinding m.Src @ usesOfText m.Label @ kindUses, []
            // Phase 1111 — two reactive slots, both on the spec: the document
            // URL and the frame's accessible name. `Permissions` and
            // `AspectRatio` are closed enums, never bindings.
            | NodeKind.Embed e -> usesOfBinding e.Src @ usesOfText e.Title, []
            | NodeKind.List l -> l.Items |> List.collect usesOfText, []
            // Phase 1120 — a tree's bindings are its rows' labels, and the
            // recursion is over `TreeItem` rather than over `Node`, so the
            // children slot stays empty: nothing below a `Tree` is a node.
            | NodeKind.Tree t ->
                noteOpaqueIf t.OnSelect.IsSome

                let rec labelUses (items: TreeItem list) =
                    items |> List.collect (fun i -> usesOfText i.Label @ labelUses i.Children)

                labelUses t.Items, []
            | NodeKind.Toast t ->
                noteWriteBack (writeBackTargetOf t.Open)
                usesOfText t.Message @ usesOfBinding t.Open, []
            | NodeKind.CodeBlock _ -> [], []
            | NodeKind.Math _ -> [], []
            | NodeKind.Drawing d ->
                let uses =
                    // Phase 524 — geometry is static; the reactive slots are the
                    // DrawStyle colour bindings + Label text, walked recursively
                    // through Group nesting.
                    let usesOfDrawStyle (st: DrawStyle) =
                        usesOfBindingOpt st.Fill
                        @ usesOfBindingOpt st.Stroke
                        @ usesOfBindingOpt st.StrokeWidth
                        @ usesOfBindingOpt st.Opacity

                    let rec usesOfShape (sh: Shape) =
                        match sh with
                        | Shape.Group(children, st) -> (children |> List.collect usesOfShape) @ usesOfDrawStyle st
                        | Shape.Rectangle(_, _, _, _, _, st) -> usesOfDrawStyle st
                        | Shape.Line(_, _, _, _, st) -> usesOfDrawStyle st
                        | Shape.Polyline(_, st) -> usesOfDrawStyle st
                        | Shape.Polygon(_, st) -> usesOfDrawStyle st
                        | Shape.Curve(_, st) -> usesOfDrawStyle st
                        | Shape.Circle(_, _, _, st) -> usesOfDrawStyle st
                        | Shape.Ellipse(_, _, _, _, st) -> usesOfDrawStyle st
                        | Shape.Label(_, _, text, st) -> usesOfText text @ usesOfDrawStyle st

                    usesOfDrawStyle d.Style
                    @ (d.Shapes |> List.collect usesOfShape)
                    @ usesOfTextOpt d.Title
                    @ usesOfTextOpt d.Description

                uses, []
            // ── Input ──
            | NodeKind.Button b ->
                let uses =
                    // OnClick is a wire-survivable Action slot (Phase 428).
                    recordCalls inUses readerId b.OnClick
                    usesOfText b.Label @ usesOfTextOpt b.Tooltip @ usesOfBindingOpt b.Disabled

                uses, []
            | NodeKind.FileUpload fu ->
                noteOpaqueIf fu.OnSelect.IsSome
                usesOfText fu.Label @ usesOfBindingOpt fu.Disabled, []
            | NodeKind.Select s ->
                noteWriteBack (writeBackTargetOf s.Value)
                s.Values |> Option.iter (writeBackTargetOf >> noteWriteBack)
                noteOpaqueIf (s.OnChange.IsSome || s.OnChangeMulti.IsSome)

                let uses =
                    usesOfText s.Label
                    @ usesOfBinding s.Source
                    @ usesOfBinding s.Value
                    @ usesOfBindingOpt s.Values
                    @ usesOfTextOpt s.Placeholder
                    @ usesOfBindingOpt s.Disabled

                uses, []
            | NodeKind.Form f ->
                let uses =
                    // OnSubmit is a wire-survivable Action slot (Phase 428).
                    recordCalls inUses readerId f.OnSubmit

                    let fieldUses =
                        f.Fields
                        |> List.collect (fun field ->
                            recordFormFieldWrites (Some field.Id) field.Kind

                            usesOfText field.Label
                            @ usesOfTextOpt field.Help
                            @ usesOfFormFieldKind (Some(BindingUse.State field.Id)) field.Kind)

                    usesOfText f.SubmitLabel @ usesOfBindingOpt f.Disabled @ fieldUses

                uses, []
            | NodeKind.Filters spec ->
                let uses =
                    if inUses then
                        for fs in spec.Items do
                            declaredFilters.Add(readerId, fs.Name)

                    spec.Items
                    |> List.collect (fun (fs: FilterSpec<_>) ->
                        // No implicit key: an absent value slot on a chip
                        // auto-binds to the FILTER store, not the State store.
                        recordFormFieldWrites None fs.Kind

                        usesOfText fs.Label
                        @ usesOfFormFieldKind (Some(BindingUse.Filter fs.Name)) fs.Kind)

                uses, []
            // ── Visualisation ──
            | NodeKind.DataGrid g ->
                // Phase 932 — `sortStateKey` / `pageStateKey` are plain STRINGS the
                // renderer READS (`readSortSlot` / `readPageDescriptor`), so they are
                // state reads with no `Binding` for the walk to see. `editStateKey` is
                // a write DESTINATION with no reader anywhere in the renderer, and
                // counting it would mask the very defect this rule looks for.
                g.SortStateKey |> Option.iter (fun k -> stateReads.Add k |> ignore)
                g.PageStateKey |> Option.iter (fun k -> stateReads.Add k |> ignore)

                // The write side of the same three slots: a header click writes
                // the sort descriptor, the pager writes the page descriptor, and
                // an edited cell commits to `editStateKey` — or, absent one, back
                // to a directly-State-bound `source` (Phase 663).
                g.SortStateKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)
                g.PageStateKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)
                g.EditStateKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)

                if g.Editable && g.EditStateKey.IsNone then
                    noteWriteBack (writeBackTargetOf g.Source)

                // A row-click handler is a closure over the row: an arbitrary
                // action per row, so an arbitrary write.
                noteOpaqueIf g.OnRowClick.IsSome

                // A closure-bearing cell produces an arbitrary `Action` per row,
                // so it may write any key. The value-only cell kinds cannot.
                for col in g.Columns do
                    match col.Kind with
                    | CellKindErased.Editable onEdit -> noteOpaqueIf onEdit.IsSome
                    | CellKindErased.Checkbox(_, onToggle) -> noteOpaqueIf onToggle.IsSome
                    | CellKindErased.Button(_, onClick) -> noteOpaqueIf onClick.IsSome
                    | CellKindErased.ButtonGroup _
                    | CellKindErased.Custom _ -> opaqueWriter <- true
                    | CellKindErased.Text
                    | CellKindErased.Numeric
                    | CellKindErased.Date
                    | CellKindErased.Link _
                    | CellKindErased.Pill _
                    | CellKindErased.TonedPill _
                    | CellKindErased.Progress _ -> ()

                let uses =
                    // Phase 393 — a static read-only grid carries its cells as `TextSource`
                    // in `StaticRows`; a data-bound grid carries a `Source` binding.
                    rowFeedUses g.Source
                    @ (match g.StaticRows with
                       | Some sr ->
                           (sr.Headers |> List.collect usesOfText)
                           @ (sr.Rows |> List.collect (List.collect usesOfText))
                       | None -> [])

                uses, []
            | NodeKind.Chart c -> rowFeedUses c.Source @ usesOfTextOpt c.Title, []
            | NodeKind.Map m -> usesOfBinding m.Source, []
            // ── Structural ──
            | NodeKind.ErrorBoundary spec -> [], [ spec.Child; spec.Fallback ]
            // Phase 932 — the branch SELECTOR is a BINDING since Phase 768; the comment
            // that stood here still described the `StateKey: string` field that change
            // retired. Its state reads are collected (a button writing the key a Switch
            // selects on is the canonical HONEST affordance, so missing this surface
            // alone would false-warn on the most idiomatic shape in the language).
            //
            // Tidy-Up follow-on — 932 routed this to `recordStateOf`, i.e. into the
            // state projection but deliberately NOT into `Uses`, because widening `Uses`
            // is a behaviour change outside an additive Warning rule's remit. It now
            // goes through `record` into BOTH: 768 made `On` any `Binding`, so a
            // `Binding.Selection` selector is a Selection READ and a dangling one is
            // FUARAN070's own case. Measured before landing — the full suite stays
            // green, and reverting this line reddens exactly the Switch-selector probe
            // in `PreEmitValidateTests`. The case children + default are walked so
            // their own bindings are captured.
            | NodeKind.Switch spec ->
                record inUses readerId (usesOfBinding spec.On)

                // The selector, recorded EXPLICITLY for FUARAN103: a Switch's
                // accessibility slots are State-bindable too, so a reader-tagged
                // State use on this node does not identify the branch selector.
                match spec.On with
                | Binding.State(key, _) -> switchSelectors.Add(readerId, key)
                | _ -> ()

                [], (spec.Cases |> List.map _.Child) @ [ spec.Default ]
            | NodeKind.FragmentDecl spec -> [], [ spec.Body ]
            // Custom props are JVal literals, not bindings; a FragmentRef carries
            // no body; a Mount guest owns its own scoped stores.
            | NodeKind.Custom _ ->
                // Phase 932 — a REGISTERED custom renderer is host code that may read
                // any state key, so the tree can no longer be shown to read nothing.
                // It may equally WRITE any key, which is the same argument on the
                // other channel.
                opaqueReader <- true
                opaqueWriter <- true
                [], []
            | NodeKind.FragmentRef spec ->
                spec.Args |> Option.iter walkSlotArgs
                [], []
            | NodeKind.Mount spec ->
                // A guest's INTERIOR reads no host key (it renders under an isolated
                // `StateStore.forScope`), but a `SlotArg` handed to it is host-authored
                // and its render-time scope is not settled by the type. Counted as a
                // read: over-counting costs a missed finding, under-counting costs a
                // false accusation. The same holds on the write channel — a guest
                // is another tree entirely, and this walk never sees its body.
                opaqueWriter <- true
                spec.Inputs |> Option.iter walkSlotArgs
                [], []

        record inUses readerId directUses
        children |> List.iter (walk inUses)

    and walkSlotArgs (args: Map<string, FragmentArg<'Msg>>) =
        for KeyValue(_, arg) in args do
            match arg with
            | FragmentArg.SlotArg tree -> walk true tree
            | _ -> ()

    walk true root

    { Uses = List.ofSeq uses
      DeclaredFilters = List.ofSeq declaredFilters
      Calls = List.ofSeq calls
      Closures = List.ofSeq closures
      Nodes = nodes |> Seq.fold (fun acc (KeyValue(k, v)) -> Map.add k v acc) Map.empty
      StateKeys =
        { Writes = List.ofSeq stateWrites
          Reads = Set.ofSeq stateReads
          OpaqueReader = opaqueReader
          WriteKeys = Set.ofSeq stateWriteKeys
          OpaqueWriter = opaqueWriter
          SwitchSelectors = List.ofSeq switchSelectors
          TransformInertSources = List.ofSeq transformInertSources
          Seeds = List.ofSeq seeds
          InlineTables = List.ofSeq inlineTables } }

/// Phase 1075 — the SEED MAP for a tree: the value each `$state.<key>` slot
/// carries before anything else has said anything.
///
/// The rules, all four from the charter's §4, are here and nowhere else so the
/// two reference renderers cannot drift on them:
///
///  - **The declaring reader is any `Binding.State` with a present
///    `defaultValue`.** There is no separate declaration site — that is the
///    whole economy of the rule.
///  - **First declaration in walk order wins.** Two declarations of one key are
///    a defect (FUARAN106, Error) but the renderer must still be deterministic
///    and must not depend on which host walked the tree; first-wins is the
///    charter's §3.4(1) alternative, taken BESIDE the refusal rather than
///    instead of it.
///  - **A host-reserved key (Phase 782) is never seeded.** A seed is a
///    tree-originated write, and the wire must not gain a way around a
///    deliberate floor.
///  - **The seed is the FLOOR, not an override.** The caller merges a
///    host-furnished value and any written value OVER this map — see the
///    renderers' `withStateSeeds`.
let stateSeeds<'Msg> (root: Node<'Msg>) : Map<string, obj> =
    (collect root).StateKeys.Seeds
    |> List.fold
        (fun acc (d: StateSeedDecl) ->
            match d.Value with
            // A null seed carries nothing — an absent value cannot be the value
            // before anything else has said anything.
            | null -> acc
            // Nor can an EMPTY table: it is the value an unseeded slot already
            // has, so declaring it says nothing, and letting it win the
            // first-declaration race would make document order matter (see
            // `isEmptySeed`).
            | _ when isEmptySeed d.Fingerprint -> acc
            | value ->
                if Fuaran.UI.StateKeyPolicy.isHostReserved d.Key || Map.containsKey d.Key acc then
                    acc
                else
                    Map.add d.Key value acc)
        Map.empty
