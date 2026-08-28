namespace Fuaran.UI

open Fuaran.Core

// ============================================================================
//  Fuaran — typed Custom-payload contracts (Phase 164).
//
//  The `NodeKind.Custom` escape hatch carries `Map<string, JVal>` props
//  plus an optional Phase-70 content hash. Without a façade, every consumer
//  hand-maintains FOUR things that must agree: the props encode, the client
//  decode, the server decode, and the content hash — drift between them is the
//  typed-surface erosion the bounded-escape posture warns about.
//
//  A `CustomContract<'Props>` is defined ONCE and from it you get: a typed node
//  constructor that can never emit a malformed prop bag (`Custom.node`), one-call
//  registration on BOTH the client and server registries (`RegisterContract`),
//  and a content hash DERIVED from the declared shape (not hand-typed). The
//  `Map<string, JVal>` wire shape is unchanged — this is a host-side façade,
//  not a tree-type change.
//
//  ── The payload-language declaration (Phase 1107) ──────────────────────────
//
//  A declared `PString` prop says "a string". For a whole class of registered
//  component that is the least interesting true thing about it: the string IS a
//  wire format, with its own decoder and its own gate, and a prop schema that
//  cannot say so passes a prose payload and leaves the failure to render.
//
//  ANNOTATION, NOT A `PropType` CASE — the decision, and why.
//
//   * The two facts are ORTHOGONAL. `PropType` answers "what JSON shape does
//     this hold"; the declaration answers "what does the content MEAN, and who
//     judges it". A payload need not be a string — a structured DSL is a
//     perfectly good `PObject` payload — so a `PWire` case would have to choose
//     one shape and foreclose the rest, or restate the shape question inside
//     itself.
//   * `PropType` is CLOSED, and its closedness is what it is for. Every
//     `matchesType` / `propTypeTag` arm is exhaustive; a new case breaks every
//     one of them in every consumer, for a fact none of them needs. With the
//     annotation, not one existing match moved.
//   * The set of payload languages is OPEN. Every domain names its own format
//     and its own gate, so a `PWire` case would carry free-form strings inside
//     a vocabulary whose entire value is that it is a fixed, checkable list —
//     the typed-surface erosion the bounded-escape posture warns about, done to
//     the one type meant to resist it.
//   * The cost is honest and it is paid here: widening `PropDecl` breaks
//     full-literal construction (FS0764). `Defaults.propDecl` is the Phase-1106
//     answer, and it is why the next annotation on a prop costs nothing.
//
//  THE HASH DOES NOT MOVE. `Hashing.customBodyShapeHash` folds the module and
//  component ids, the prop KEY SET and the exposed ids — never a prop's declared
//  detail. A contract that adopts the declaration therefore hashes exactly as it
//  did before, which is asserted rather than asserted-to-be-obvious: a moved
//  hash would invalidate every `StrictReplay` consumer of an existing component
//  for a change that altered nothing about what it emits.
// ============================================================================

open Fuaran.UI.Types

/// A decode failure for a typed Custom payload. Surfaced through the renderer
/// placeholder (naming the failing key) + the Phase-12.D diagnostics substrate
/// on both pipelines.
type CustomDecodeError =
    {
        /// The prop key that failed (or a synthetic marker for a whole-bag failure).
        Key: string
        /// Human-readable reason.
        Message: string
    }

[<RequireQualifiedAccess>]
module CustomDecodeError =
    /// A decode error naming the offending key.
    let forKey (key: string) (message: string) : CustomDecodeError = { Key = key; Message = message }

    /// A whole-payload decode error (no single key to blame).
    let payload (message: string) : CustomDecodeError =
        { Key = "<payload>"; Message = message }

/// A typed contract over the `NodeKind.Custom` escape hatch (Phase 164). Define
/// once with `CustomContract.create`; the content hash is derived from the
/// declared shape (module/component + the encoder's prop-key set + exposed ids),
/// reusing the Phase-134 SHA-256 derivation — never hand-typed.
type CustomContract<'Props> =
    {
        ModuleId: string
        ComponentId: string
        /// Encode typed props to the wire prop bag. Its KEY SET is the contract's
        /// declared prop schema (the hash is derived from it) — keep it stable.
        Encode: 'Props -> Map<string, JVal>
        /// Decode the wire prop bag back to typed props (or a labelled error).
        Decode: Map<string, JVal> -> Result<'Props, CustomDecodeError>
        /// Interior node ids the Custom body exposes (Phase 70) — folded into the
        /// derived hash AND stamped on every `Custom.node`.
        ExposedNodeIds: NodeId list
        /// The declared prop schema (name / `PropType` / required). This is what
        /// makes the component first-class: it projects into the AI's available-
        /// kinds prompt context (a model emits the widget's props correctly) and
        /// drives runtime prop validation. `create` derives a permissive all-`PJson`
        /// schema from the encoder's key set; `createWithSchema` takes an explicit
        /// typed schema. Its key set MUST equal the encoder's (checked in the typed
        /// constructor) so a schema and the content hash never disagree.
        Schema: PropSchema
        /// Content hash derived from the declared shape (Phase 134 / Phase 70).
        Hash: ContentHash
    }

[<RequireQualifiedAccess>]
module CustomContract =
    /// Build a contract, deriving the content hash from the declared shape — the
    /// module/component ids, the encoder's prop-key set (read once from
    /// `schemaSample`), and `exposedNodeIds`. `schemaSample` is any representative
    /// `'Props`; the encoder must emit a STABLE key set (that key set IS the
    /// declared schema). `strictness` governs the mismatch behaviour (Phase 70).
    let create
        (moduleId: string)
        (componentId: string)
        (encode: 'Props -> Map<string, JVal>)
        (decode: Map<string, JVal> -> Result<'Props, CustomDecodeError>)
        (schemaSample: 'Props)
        (exposedNodeIds: NodeId list)
        (strictness: HashStrictness)
        : CustomContract<'Props> =
        let propKeys = encode schemaSample |> Map.toList |> List.map fst
        let exposedIds = exposedNodeIds |> List.map (fun (NodeId s) -> s)

        let hash = Hashing.customBodyShapeHash moduleId componentId propKeys exposedIds

        { ModuleId = moduleId
          ComponentId = componentId
          Encode = encode
          Decode = decode
          ExposedNodeIds = exposedNodeIds
          // No explicit schema supplied: derive a permissive one — every emitted
          // key is a required `PJson` (any structured value). Validation then only
          // checks presence, never type; `createWithSchema` tightens that.
          Schema =
            propKeys
            |> List.map (fun k ->
                { Defaults.propDecl with
                    Name = k
                    Type = PropType.PJson
                    Required = true })
          Hash =
            { Algorithm = "SHA256"
              Hash = hash
              Strictness = strictness } }

    /// Build a contract with an EXPLICIT typed prop schema — the first-class path:
    /// each prop's `PropType` + `Required` drives AI discovery and runtime
    /// validation. The schema's key set MUST equal the encoder's key set (else the
    /// schema and the content hash would describe different prop sets); a mismatch
    /// is an `Error`. The content hash is derived from the schema key set exactly
    /// as `create` derives it from the encoder, so a typed and an untyped contract
    /// over the same props hash identically.
    let createWithSchema
        (moduleId: string)
        (componentId: string)
        (schema: PropSchema)
        (encode: 'Props -> Map<string, JVal>)
        (decode: Map<string, JVal> -> Result<'Props, CustomDecodeError>)
        (schemaSample: 'Props)
        (exposedNodeIds: NodeId list)
        (strictness: HashStrictness)
        : Result<CustomContract<'Props>, string> =
        let encoderKeys = encode schemaSample |> Map.toList |> List.map fst |> List.sort
        let schemaKeys = schema |> List.map _.Name |> List.sort

        if encoderKeys <> schemaKeys then
            Error(
                sprintf
                    "prop schema key set %A does not match the encoder's key set %A — they must declare the same props"
                    schemaKeys
                    encoderKeys
            )
        else
            let exposedIds = exposedNodeIds |> List.map (fun (NodeId s) -> s)
            let hash = Hashing.customBodyShapeHash moduleId componentId encoderKeys exposedIds

            Ok
                { ModuleId = moduleId
                  ComponentId = componentId
                  Encode = encode
                  Decode = decode
                  ExposedNodeIds = exposedNodeIds
                  Schema = schema
                  Hash =
                    { Algorithm = "SHA256"
                      Hash = hash
                      Strictness = strictness } }

    /// The derived hash string (the Phase-134 body-shape SHA-256). Registries
    /// record this so a `Custom.node`'s declared hash verifies against it.
    let hash (contract: CustomContract<'Props>) : string = contract.Hash.Hash

    /// The payload language declared for `key`, or `None` when the contract
    /// declares no such prop or declares it as an ordinary one.
    let payloadLanguage (contract: CustomContract<'Props>) (key: string) : PayloadLanguage option =
        contract.Schema
        |> List.tryFind (fun p -> p.Name = key)
        |> Option.bind _.PayloadLanguage

    /// Every declared-wire prop of a contract, in schema order.
    let payloadProps (contract: CustomContract<'Props>) : (string * PayloadLanguage) list =
        contract.Schema
        |> List.choose (fun p -> p.PayloadLanguage |> Option.map (fun pl -> p.Name, pl))

// ============================================================================
//  Payload provenance (Phase 1107, task 3) — the shape an op-stream or telemetry
//  consumer records when a declared-wire payload prop is UPDATED.
//
//  The WIRING is per-host and deliberately so: this tier holds no op-stream
//  sink, and FGP 7 puts the one definition of a domain's gate in that domain, so
//  nothing here can run one. What this tier owns is the SHAPE both ends agree
//  on, and the one-line attribution a stream stores — so that two hosts writing
//  the same fact write the same bytes, and a reader can tell the three states
//  apart without parsing prose.
//
//  `NotRun` is a first-class verdict rather than an omission. A stream that
//  simply leaves the unjudged case out cannot distinguish "the gate ran and was
//  content" from "nobody looked", and that reading is precisely how an unjudged
//  payload becomes an assumed-good one.
// ============================================================================

/// What a domain gate concluded about a payload it was handed.
[<RequireQualifiedAccess>]
type PayloadGateVerdict =
    /// The named gate ran and accepted the payload.
    | Accepted
    /// The named gate ran and refused it, carrying the gate's own reason.
    | Refused of reason: string
    /// No gate ran — the payload was updated unjudged. Recorded, never omitted.
    | NotRun

/// The provenance record for one update to a declared-wire payload prop: which
/// component, which prop, which inner language, which gate (if any is named),
/// and what that gate concluded.
type PayloadUpdateProvenance =
    { ModuleId: string
      ComponentId: string
      Key: string
      Language: string
      Gate: PayloadGate option
      Verdict: PayloadGateVerdict }

[<RequireQualifiedAccess>]
module PayloadProvenance =
    /// The record for a payload update on `key` of `contract`, or `None` when
    /// that prop declares no inner language. `None` is the load-bearing half: a
    /// host cannot manufacture a "via `<language>` gate" attribution for a prop
    /// whose contract never claimed one, so the attribution is falsifiable
    /// against the schema rather than being whatever the writer typed.
    let forUpdate
        (contract: CustomContract<'Props>)
        (key: string)
        (verdict: PayloadGateVerdict)
        : PayloadUpdateProvenance option =
        CustomContract.payloadLanguage contract key
        |> Option.map (fun pl ->
            { ModuleId = contract.ModuleId
              ComponentId = contract.ComponentId
              Key = key
              Language = pl.Language
              Gate = pl.Gate
              Verdict = verdict })

    /// The stable one-line attribution a stream stores — "via `<language>` gate
    /// `<stamp>` — `<verdict>`". An ungated declaration renders `<ungated>` in
    /// the stamp slot rather than eliding it, so the line never reads as though
    /// a gate were named.
    let attribution (p: PayloadUpdateProvenance) : string =
        let stamp =
            match p.Gate with
            | Some g -> g.AsStamp
            | Option.None -> "<ungated>"

        let verdict =
            match p.Verdict with
            | PayloadGateVerdict.Accepted -> "accepted"
            | PayloadGateVerdict.Refused reason -> "refused: " + reason
            | PayloadGateVerdict.NotRun -> "NOT RUN"

        sprintf "via %s gate %s — %s" p.Language stamp verdict

[<RequireQualifiedAccess>]
module Custom =
    /// A typed Custom node from a contract + props (Phase 164): encodes via the
    /// contract (so the prop bag can never be malformed), stamps the contract's
    /// derived content hash and exposed ids. The raw `Fuaran.custom` constructor
    /// remains for hosts that need the untyped escape (bounded escape stays
    /// bounded, not widened).
    let node (id: string) (contract: CustomContract<'Props>) (props: 'Props) : Node<'Msg> =
        Fuaran.custom
            id
            contract.ModuleId
            contract.ComponentId
            (contract.Encode props)
            (Some contract.Hash)
            contract.ExposedNodeIds
