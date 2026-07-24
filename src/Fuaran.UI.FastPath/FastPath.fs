namespace Fuaran.UI

open Fuaran.Core

/// Fuaran.UI.FastPath — a public, signature-searchable pattern bank over the
/// Fuaran.Core artifact-function registry.
///
/// Composition-by-lookup, not composition-by-generation: given the holes you can
/// fill (the available context) and/or the kind of node you want to produce, ask
/// the bank which known patterns you can run — a deterministic, total, in-memory
/// structural search (`Fuaran.Core.FunctionRegistry.findBySignature`), no model
/// call and no server. Then instantiate the chosen pattern into a real Fuaran
/// tree. FSharp.Core + Fuaran.Core.Function only; Fable-clean, so the same lookup
/// runs identically in the browser and on the server, byte-for-byte.
module FastPath =

    /// One pattern in the bank: its identity + human labels, its signature-search
    /// metadata (the node kind it produces + its declared holes), and a builder
    /// that instantiates it into a real Fuaran tree given hole values (addr → value).
    type Pattern =
        {
            Id: string
            Title: string
            Summary: string
            /// The node-kind tag the pattern produces (the registry's result key).
            ResultType: string
            /// The declared holes (authoring form); projected to a Core `Signature`.
            Holes: HoleDecl list
            /// Instantiate into a tree. Unbound holes fall back to a sensible default,
            /// so a partial binding still renders.
            Build: Map<string, string> -> Types.Node<unit>
        }

    /// A bank = the Core signature registry (for search) + the id→pattern map (for
    /// retrieval + instantiation). Built once from a pattern list.
    type Bank =
        { Registry: FunctionRegistry
          Patterns: Map<string, Pattern> }

    /// A UI-ergonomic query: the holes the caller can fill (the available context),
    /// and optionally the node kind they want to produce.
    type Query =
        { Provide: HoleDecl list
          Produce: string option }

    // ── hole + query constructors (the ergonomic surface) ────────────────────

    /// A value hole: a scalar the caller can bind, constrained to a value space.
    let valueHole (addr: string) (name: string) (space: ValueSpace) : HoleDecl =
        { Addr = addr
          Name = name
          Kind = ValueHole space }

    /// A free-text value hole (any string).
    let textHole (addr: string) (name: string) : HoleDecl = valueHole addr name AnyString

    /// A numeric value hole over a bounded range.
    let numberHole (addr: string) (name: string) (lo: int) (hi: int) : HoleDecl = valueHole addr name (IntRange(lo, hi))

    /// Build a query from the context you can provide and (optionally) the kind you want.
    let query (provide: HoleDecl list) (produce: string option) : Query =
        { Provide = provide; Produce = produce }

    // ── projection: HoleDecl → SigEntry (mirrors Fuaran.Core.Function.signature) ──

    let private sigEntryOf (h: HoleDecl) : SigEntry =
        let kindStr, space, slot, action, required =
            match h.Kind with
            | ValueHole s -> "value", Some s, None, None, true
            | SlotHole c -> "slot", None, c, None, true
            | RepeatHole s -> "repeat", Some s, None, None, false
            | ActionHole e -> "action", None, None, Some e, false

        { Addr = h.Addr
          Name = h.Name
          Kind = kindStr
          Space = space
          Slot = slot
          Action = action
          Required = required }

    let private signatureOf (name: string) (holes: HoleDecl list) : Signature =
        { Name = name
          Holes = holes |> List.map sigEntryOf
          Effect = Effect.pureDeterministic }

    // ── build + search ───────────────────────────────────────────────────────

    /// Build a searchable bank from a pattern list. Each pattern becomes a
    /// pure/deterministic, client-declarative artifact-function keyed by its result
    /// type + required holes; a duplicate id is skipped so the registry stays
    /// consistent (the same default-deny posture as the Core registry).
    let bank (patterns: Pattern list) : Bank =
        let registry =
            (FunctionRegistry.empty, patterns)
            ||> List.fold (fun r p ->
                let cap =
                    Capability.create p.Id (signatureOf p.Title p.Holes) Placement.ClientDeclarative

                match FunctionRegistry.register (FunctionRegistry.entry p.ResultType cap) r with
                | Ok next -> next
                | Error _ -> r)

        { Registry = registry
          Patterns = patterns |> List.map (fun p -> p.Id, p) |> Map.ofList }

    /// Find every pattern whose signature matches the query under `mode` — the real
    /// Core `findBySignature`, mapped back to the instantiable patterns (id-stable).
    let find (mode: MatchMode) (q: Query) (b: Bank) : Pattern list =
        let sq: SignatureQuery =
            { ResultType = q.Produce
              Available = q.Provide |> List.map sigEntryOf }

        FunctionRegistry.findBySignature mode sq b.Registry
        |> List.choose (fun (e: FunctionEntry) -> Map.tryFind e.Capability.Id b.Patterns)

    /// "Everything I can run with the context I have" — structural subsumption
    /// (the entry's required holes are all satisfiable from the query context).
    let findRunnable (q: Query) (b: Bank) : Pattern list = find Subsumes q b

    /// "The pattern with precisely these holes" — exact signature match.
    let findExact (q: Query) (b: Bank) : Pattern list = find Exact q b

    /// Look a pattern up by id.
    let tryPattern (id: string) (b: Bank) : Pattern option = Map.tryFind id b.Patterns

    /// Instantiate a chosen pattern into a real Fuaran tree from hole values.
    let instantiate (p: Pattern) (values: Map<string, string>) : Types.Node<unit> = p.Build values

    /// Instantiate, then re-cross the language-tier pre-emit gate before the tree
    /// leaves the bank (FGP 7 egress — only valid Fuaran enters *or* leaves the
    /// pattern bank; Phase 562). Returns the built tree only if
    /// `PreEmitValidate.validate` passes; a pattern whose `Build` produces a
    /// semantically-invalid tree (empty / duplicate ids, dangling refs, tab
    /// mismatch, …) — or one whose emission drifted from the `Fuaran.UI` it was
    /// authored against — is rejected with its defect list rather than served. The
    /// bank is meant to hold only valid patterns, so this is defence-in-depth:
    /// served emissions here are typed `Node` values (never Fuaran-shaped JSON), so
    /// the guard catches a buggy `Build` or a drift-induced invalid tree, not a
    /// decode failure. `instantiate` stays the unchecked builder for callers that
    /// have already validated upstream.
    let tryInstantiate
        (p: Pattern)
        (values: Map<string, string>)
        : Result<Types.Node<unit>, PreEmitValidate.PreEmitDefect list> =
        let node = p.Build values

        match PreEmitValidate.validate node with
        | Ok() -> Ok node
        | Error defects -> Error defects
