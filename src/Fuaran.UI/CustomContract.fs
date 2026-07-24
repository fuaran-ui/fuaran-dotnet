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
                { Name = k
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
