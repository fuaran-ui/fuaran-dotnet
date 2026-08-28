namespace Fuaran.UI

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  CustomRegistry — the first-class extension registry.
//
//  A host registers a `CustomContract` once; the registry then makes that
//  third-party component behave like a built-in NodeKind in the two ways a
//  built-in gets for free:
//
//   1. AI DISCOVERY — `describeForAi` projects each registered contract's prop
//      schema into a `CustomKindCard`, the shape an orchestrator folds into the
//      model's available-kinds prompt context so it can emit the widget's props
//      correctly (name + type + required), instead of guessing an opaque bag.
//
//   2. VALIDATION — `validateProps` checks a decoded `NodeKind.Custom` prop bag
//      against the declared `PropSchema`: a missing required prop or a JVal whose
//      shape doesn't match the declared `PropType` is a defect, surfaced with the
//      `FUARAN068` code (its own allocation — FUARAN064 is the button
//      disabled-binding advisory). This closes the "a Custom node opts out of all
//      validation" gap — a registered kind is checked like a built-in.
//
//   3. PAYLOAD OBLIGATIONS (Phase 1107) — `ValidatePropsDetailed` reports, apart
//      from the defects, what the registry CANNOT judge: a prop whose schema
//      declares an inner wire format carries an outstanding gate run, and a
//      declaration naming no gate carries something worse. Reported separately
//      from the defects on purpose. An obligation is not an error — the payload
//      may well be fine — so folding it into the `FUARAN068` stream would make a
//      contract fail validation for the act of describing itself more honestly,
//      which is the surest way to get the declaration left off.
//
//   4. CARD EXPORT (Phase 1108) — the same `CustomKindCard` that feeds (1) is
//      now the specified, transportable CARD artefact (WIRE_FORMAT.md §25), so a
//      deployment can hand a FOREIGN host enough to prop-validate and honestly
//      label a `Custom` node it has no renderer for. Nothing new is assembled
//      for it: everything a foreign host needs was already gathered here for the
//      orchestrator's benefit and existed only in-process. See `CustomCard`.
//
//  The registry stores only the ERASED contract facts (module/component id +
//  schema + hash + summary), not the generic `'Props`, so contracts over
//  different prop types coexist in one registry. Pure data + FSharp.Core only (Fable-clean); a
//  host owns the registry's lifetime and threads it into its validate / prompt
//  paths.
// ============================================================================

/// One prop row of a `CustomKindCard` — the declared `PropSchema` entry with its
/// `PropType` rendered as a stable tag string an orchestrator can put in a
/// prompt. A NAMED record (not anonymous) deliberately: this is shipped public
/// API — named records are binary-stable across compilers and speakable from
/// C#/VB, anonymous records are neither.
type CustomPropCard =
    {
        Name: string
        Type: string
        Required: bool
        /// The inner wire format this prop's value is written in (Phase 1107), or
        /// `None` for an ordinary prop. A teaching surface or an eval harness
        /// reads this to know an inner language EXISTS — which `Type = "string"`
        /// could never tell it.
        PayloadLanguage: string option
        /// The stamp (`gate:version`) of the gate that judges that language.
        /// `None` alongside a `Some` language is the declared-but-ungated state,
        /// and it is carried as its own absence rather than being folded into the
        /// language string, so a card consumer never has to parse one out of the
        /// other.
        PayloadGate: string option
    }

/// The content-identity half of a card (Phase 1108) — the algorithm and the
/// digest, and deliberately NOT the `Strictness`.
///
/// `ContentHash` carries a third field because a NODE declares what should happen
/// on mismatch. A card is a description of a component, not a policy about one
/// tree's replay: the strictness belongs to whoever emitted the node, and a card
/// that carried one would be a foreign deployment's policy arriving as data.
type CardContentHash = { Algorithm: string; Hash: string }

/// A registered custom component, projected for the AI's available-kinds context:
/// what to emit (`ModuleId` / `ComponentId`) and the prop contract to emit it
/// against.
///
/// Since Phase 1108 this is ALSO the wire-adjacent CARD artefact (WIRE_FORMAT.md
/// §25) — the same facts, transportable. That is the whole economy of the phase:
/// everything a foreign host needs to be honest about a `Custom` node it cannot
/// render was already assembled here for the orchestrator's benefit and existed
/// only in-process. A card is not a renderer and does not pretend to be one; it
/// is what makes "opaque elsewhere" into "legible-but-unrendered elsewhere".
type CustomKindCard =
    {
        ModuleId: string
        ComponentId: string
        Props: CustomPropCard list
        /// The registered contract's derived content hash, so a consumer can ask
        /// whether this card describes the node in front of it rather than merely
        /// a component of the same name.
        Hash: CardContentHash
        /// One line saying what the component IS. `None` where the contract
        /// declares none — a placeholder built from such a card shows identity
        /// alone. Never synthesised from the ids: an invented sentence is exactly
        /// the guess the degradation obligation forbids.
        Summary: string option
    }

/// A single prop-validation defect against a declared schema. `Code` is always
/// `CustomRegistry.propDefectCode` (`FUARAN068`) — carried per-defect so a host
/// folding these into a mixed defect stream keeps the code with the finding.
type CustomPropDefect =
    { Code: string
      Key: string
      Message: string }

/// Why a declared-wire payload prop carries an outstanding obligation (Phase
/// 1107). This is deliberately NOT a defect vocabulary: neither case says the
/// payload is wrong, only that this registry cannot say it is right.
[<RequireQualifiedAccess>]
type PayloadObligationKind =
    /// The prop declares an inner language AND names the gate that judges it,
    /// and that gate did not run here. It cannot: the registry holds no decoder
    /// for any domain's format, and FGP 7 puts the one definition of the gate in
    /// the domain that owns the language. A gate run is OWED before the payload
    /// can be called valid.
    | GateOwed
    /// The prop declares an inner language but names NO gate. Nothing can judge
    /// this payload at all — a claim with no falsifier. Distinct from `GateOwed`
    /// because the remedies are different: one is "run it", the other is
    /// "there is nothing to run".
    | Ungated

/// One outstanding payload obligation on a prop bag. Reported ALONGSIDE the
/// defect list rather than inside it: a `CustomPropDefect` is an error the
/// pre-emit walk escalates, and an obligation is not an error — a contract that
/// adopts a payload declaration must not thereby start failing validation.
type CustomPayloadObligation =
    { Key: string
      Language: string
      Gate: PayloadGate option
      Kind: PayloadObligationKind
      Message: string }

/// Both answers the registry gives about one prop bag: what is WRONG with it
/// (schema defects, error-grade) and what is still OWED on it (payload gate runs
/// this tier cannot perform). Keeping them in one record is the point — the
/// caller sees that a prop bag passing `Defects = []` is not thereby judged.
type CustomValidation =
    { Defects: CustomPropDefect list
      Obligations: CustomPayloadObligation list }

module CustomRegistry =

    /// The `CustomPropDefect` defect code — a registered custom component's prop
    /// bag violates its declared `PropSchema` (missing required prop / mistyped
    /// value). Its own allocation: `FUARAN064` is the button disabled-binding
    /// advisory.
    [<Literal>]
    let propDefectCode = "FUARAN068"

    /// The stable prompt-facing tag of a `PropType` (what an AI reads).
    let propTypeTag (t: PropType) : string =
        match t with
        | PropType.PString -> "string"
        | PropType.PInt -> "int"
        | PropType.PFloat -> "float"
        | PropType.PBool -> "bool"
        | PropType.PEnum choices -> "enum(" + String.concat "|" choices + ")"
        | PropType.PObject -> "object"
        | PropType.PArray -> "array"
        | PropType.PJson -> "json"

    /// The inverse of `propTypeTag` (Phase 1108) — recover a `PropType` from the
    /// tag a card carries, so a host holding only the card can prop-validate a
    /// node exactly as a host holding the contract does.
    ///
    /// `None` is the honest answer for a tag this package does not know: a card
    /// emitted by a NEWER producer can name a `PropType` case that did not exist
    /// when this consumer was built, and guessing (`PJson`, say — "accepts
    /// anything") would silently downgrade a check into a pass. The decoder turns
    /// this `None` into `UNKNOWN_DU_CASE`; the validator turns it into a stated
    /// non-answer.
    ///
    /// `enum(a|b|c)` round-trips through the same spelling `propTypeTag` emits.
    /// An empty choice list is unrepresentable in that spelling — `enum()` would
    /// read as one empty choice — so it is refused rather than parsed to a set
    /// that accepts nothing while claiming to be an enum.
    let tryParsePropTypeTag (tag: string) : PropType option =
        match tag with
        | "string" -> Some PropType.PString
        | "int" -> Some PropType.PInt
        | "float" -> Some PropType.PFloat
        | "bool" -> Some PropType.PBool
        | "object" -> Some PropType.PObject
        | "array" -> Some PropType.PArray
        | "json" -> Some PropType.PJson
        | _ when tag.StartsWith("enum(", System.StringComparison.Ordinal) && tag.EndsWith(")") ->
            let inner = tag.Substring(5, tag.Length - 6)

            if inner = "" then
                Option.None
            else
                Some(PropType.PEnum(inner.Split('|') |> Array.toList))
        | _ -> Option.None

    /// Whether a wire `JVal` satisfies a declared `PropType`. `PInt` accepts a
    /// `JInt`; `PFloat` accepts either (an int is an exact float); `PJson` accepts
    /// anything. Mirrors the decoder's number policy.
    let matchesType (t: PropType) (v: JVal) : bool =
        match t, v with
        | PropType.PString, JStr _ -> true
        | PropType.PInt, JInt _ -> true
        | PropType.PFloat, (JFloat _ | JInt _) -> true
        | PropType.PBool, JBool _ -> true
        | PropType.PEnum choices, JStr s -> List.contains s choices
        | PropType.PObject, JObj _ -> true
        | PropType.PArray, JArr _ -> true
        | PropType.PJson, _ -> true
        | _ -> false

    /// The prompt-facing rendering of a declared payload language (Phase 1107) —
    /// the one line a teaching surface prints beside the prop's type tag, so
    /// every such surface prints the same thing. `None` for an ordinary prop.
    /// The ungated case says so loudly: a reader must not have to notice a
    /// missing parenthetical to learn that nothing judges the payload.
    let payloadTag (p: PayloadLanguage option) : string option =
        p
        |> Option.map (fun pl ->
            match pl.Gate with
            | Some g -> sprintf "%s (gate %s)" pl.Language g.AsStamp
            | Option.None -> sprintf "%s (NO GATE)" pl.Language)

    /// Check a prop bag against a `PropSchema` — the whole of the registry's
    /// judgement, lifted out of the registry (Phase 1108).
    ///
    /// Lifted rather than duplicated because a CARD carries the same schema
    /// without the registry around it, and a foreign host validating from a card
    /// must reach exactly the same verdict a host holding the contract reaches.
    /// Two implementations of "does this bag satisfy this schema" would be two
    /// answers the day one of them was edited, and the whole claim a card makes
    /// is that it says what the contract says.
    let validateSchema (schema: PropSchema) (props: Map<string, JVal>) : CustomValidation =
        let missing =
            schema
            |> List.filter (fun p -> p.Required && not (Map.containsKey p.Name props))
            |> List.map (fun p ->
                { Code = propDefectCode
                  Key = p.Name
                  Message = sprintf "required prop '%s' (%s) is missing" p.Name (propTypeTag p.Type) })

        let mistyped =
            schema
            |> List.choose (fun p ->
                match Map.tryFind p.Name props with
                | Some v when not (matchesType p.Type v) ->
                    Some
                        { Code = propDefectCode
                          Key = p.Name
                          Message = sprintf "prop '%s' is not a %s" p.Name (propTypeTag p.Type) }
                | _ -> None)

        let obligations =
            schema
            |> List.choose (fun p ->
                match p.PayloadLanguage, Map.tryFind p.Name props with
                | Some pl, Some v when matchesType p.Type v ->
                    let kind, message =
                        match pl.Gate with
                        | Some g ->
                            PayloadObligationKind.GateOwed,
                            sprintf
                                "prop '%s' carries a '%s' payload; the '%s' gate judges it and has NOT run here"
                                p.Name
                                pl.Language
                                g.AsStamp
                        | Option.None ->
                            PayloadObligationKind.Ungated,
                            sprintf
                                "prop '%s' declares a '%s' payload but names no gate — nothing can judge it"
                                p.Name
                                pl.Language

                    Some
                        { Key = p.Name
                          Language = pl.Language
                          Gate = pl.Gate
                          Kind = kind
                          Message = message }
                | _ -> None)

        { Defects = missing @ mistyped
          Obligations = obligations }

/// The registered-contract facts, erased of the generic `'Props`.
type private RegisteredCustom =
    { ModuleId: string
      ComponentId: string
      Schema: PropSchema
      Hash: ContentHash
      Summary: string option }

/// An immutable registry of custom-component contracts keyed on
/// `(moduleId, componentId)`. Build by folding `register` over your contracts.
type CustomRegistry private (entries: Map<string * string, RegisteredCustom>) =

    static member Empty = CustomRegistry(Map.empty)

    /// Register a typed contract (its `'Props` is erased — only the schema + hash
    /// are retained). Re-registering the same key replaces it.
    member _.Register(contract: CustomContract<'Props>) : CustomRegistry =
        let key = (contract.ModuleId, contract.ComponentId)

        let entry =
            { ModuleId = contract.ModuleId
              ComponentId = contract.ComponentId
              Schema = contract.Schema
              Hash = contract.Hash
              Summary = contract.Summary }

        CustomRegistry(Map.add key entry entries)

    /// The AI-facing cards for every registered component — fold these into the
    /// model's available-kinds prompt context so a `Custom` node can be emitted
    /// with correct props.
    member _.DescribeForAi() : CustomKindCard list =
        entries
        |> Map.toList
        |> List.map (fun (_, e) ->
            { ModuleId = e.ModuleId
              ComponentId = e.ComponentId
              Hash =
                { Algorithm = e.Hash.Algorithm
                  Hash = e.Hash.Hash }
              Summary = e.Summary
              Props =
                e.Schema
                |> List.map (fun p ->
                    { Name = p.Name
                      Type = CustomRegistry.propTypeTag p.Type
                      Required = p.Required
                      PayloadLanguage = p.PayloadLanguage |> Option.map _.Language
                      PayloadGate = p.PayloadLanguage |> Option.bind _.Gate |> Option.map _.AsStamp }) })

    /// Validate a decoded `NodeKind.Custom` prop bag against the registered
    /// schema for `(moduleId, componentId)`. An UNregistered component is `Ok`
    /// (the registry only speaks for what it knows — an unknown custom kind is a
    /// host trust-boundary concern, not a schema violation). A registered one is
    /// checked: every required prop present, every present prop the right
    /// `PropType`. Returns the defect list (empty = NO SCHEMA DEFECT — which
    /// since Phase 1107 is deliberately narrower than "valid": a declared-wire
    /// payload prop can be defect-free and still be unjudged. See
    /// `ValidatePropsDetailed`.)
    member this.ValidateProps(moduleId: string, componentId: string, props: Map<string, JVal>) : CustomPropDefect list =
        this.ValidatePropsDetailed(moduleId, componentId, props).Defects

    /// The PAYLOAD obligations on a prop bag (Phase 1107) — what this registry
    /// cannot judge and says so. One entry per declared-wire prop that is
    /// PRESENT and well-shaped: `GateOwed` when the contract names a gate (the
    /// host composes and runs it), `Ungated` when it names none.
    ///
    /// A declared-wire prop that is absent, or present with the wrong JSON
    /// shape, raises NO obligation — the first is nothing to judge, and the
    /// second already has a `CustomPropDefect` saying the shape is wrong.
    /// Reporting both would double-count one fault and make the obligation list
    /// a noisier restatement of the defect list rather than the different
    /// question it is.
    member this.ValidatePayloads
        (moduleId: string, componentId: string, props: Map<string, JVal>)
        : CustomPayloadObligation list =
        this.ValidatePropsDetailed(moduleId, componentId, props).Obligations

    /// The distinguishing call: a prop bag's schema DEFECTS and its outstanding
    /// payload OBLIGATIONS, separately. `ValidateProps` and `ValidatePayloads`
    /// are the two projections of it.
    ///
    /// This is what closes the gap a `PString` declaration left open. Before the
    /// payload-language annotation, a prop holding a whole inner wire format and
    /// a prop holding a label were the same declaration, so a payload that was
    /// prose rather than its declared format PASSED prop validation and failed
    /// only at render. It still passes `Defects` — the shape genuinely is a
    /// string — but it now leaves an obligation behind, so the two are no longer
    /// the same answer.
    member _.ValidatePropsDetailed(moduleId: string, componentId: string, props: Map<string, JVal>) : CustomValidation =
        match Map.tryFind (moduleId, componentId) entries with
        | None -> { Defects = []; Obligations = [] }
        | Some e -> CustomRegistry.validateSchema e.Schema props
