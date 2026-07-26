namespace Fuaran.UI.FastPath.Tests

open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.UI

/// ============================================================================
///  Cross-host function-registry goldens — the F# (fifth) leg of Phase 558.
///
///  Phase 558 shipped a shared golden set at
///  `wire-format-fixtures/function-registry/goldens.json` — a canonical registry
///  + `findBySignature` (EXACT / SUBSUMES) queries + compose-path queries,
///  certified against the SHIPPED Python reference by py / ts / go / rs. **F# was
///  certified only by source-equivalence, never RUN against the goldens.** This
///  harness closes that gap: it loads the goldens, builds the reference registry,
///  and asserts `Fuaran.UI.FastPath.find Exact` / `find Subsumes` reproduce the
///  goldens' `findBySignature` expectations exactly — making the mechanical
///  agreement literally five-way on the subset F# supports.
///
///  SCOPE — findBySignature only. F# ships `findBySignature` (via FastPath over
///  `Fuaran.Core.FunctionRegistry`) but NO compose path, so the 6 `compose`
///  goldens are out of F# scope and stay **py-authoritative** (asserted by
///  py / ts / go / rs, not here). We assert only the `findBySignature` subset.
///
///  ADAPTER (goldens' host-neutral shape → F#). FastPath's own authoring surface
///  (`Pattern` / `HoleDecl`) cannot express an OPTIONAL value hole — its `bank`
///  projects every `ValueHole` to `Required = true` — yet the goldens'
///  `optional-note` carries an optional `subtitle`, and the
///  `subsumes-optional-hole-title-only` query turns on exactly that optionality.
///  So we build the `Fuaran.Core.FunctionRegistry` DIRECTLY from the golden
///  `SigEntry` shape (honouring each hole's `required`), then wrap it in a
///  `FastPath.Bank` so the assertion still runs through `FastPath.find` — which
///  delegates verbatim to `Fuaran.Core.FunctionRegistry.findBySignature`, the
///  engine the goldens twin. `FastPath.fs`'s public surface is untouched.
/// ============================================================================
module GoldensTests =

    /// The goldens live at the workspace-shared corpus root, a sibling of the
    /// `fuaran-dotnet/` repo — absent in a single-repo checkout (skip cleanly then, same
    /// as the DAG / merge conformance suites).
    let private goldensPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "..",
            "..",
            "..",
            "wire-format-fixtures",
            "function-registry",
            "goldens.json"
        )

    // ── JSON readers (Nullable is on; GetString() is string | null) ───────────

    /// A JSON string element → its non-null value (fails loudly on `null`).
    let private asStr (el: JsonElement) : string =
        match el.GetString() with
        | null -> failwith "goldens: expected a non-null string"
        | s -> s

    let private reqStr (el: JsonElement) (prop: string) : string =
        match el.GetProperty(prop).GetString() with
        | null -> failwithf "goldens: property '%s' is null" prop
        | s -> s

    let private reqInt (el: JsonElement) (prop: string) : int = el.GetProperty(prop).GetInt32()

    /// A property that may be absent OR JSON `null` (the goldens use `null` for a
    /// value hole's `slot` and a slot hole's `space`, and for a wildcard `resultType`).
    let private tryEl (el: JsonElement) (prop: string) : JsonElement option =
        match el.TryGetProperty prop with
        | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
        | _ -> None

    /// A host-neutral `space` object → the Core `ValueSpace`.
    let private spaceOf (el: JsonElement) : ValueSpace =
        match reqStr el "kind" with
        | "anyString" -> AnyString
        | "intRange" -> IntRange(reqInt el "min", reqInt el "max")
        | "stringLen" -> StringLen(reqInt el "min", reqInt el "max")
        | "enum" ->
            el.GetProperty("choices").EnumerateArray()
            |> Seq.map asStr
            |> List.ofSeq
            |> Enum
        | other -> failwithf "goldens: unknown value-space kind '%s'" other

    /// A host-neutral hole object → the Core `SigEntry` — HONOURING `required`
    /// (the whole reason this bypasses `FastPath.bank`).
    let private sigEntryOf (h: JsonElement) : SigEntry =
        { Addr = reqStr h "addr"
          Name = reqStr h "name"
          Kind = reqStr h "kind"
          Space = tryEl h "space" |> Option.map spaceOf
          Slot = tryEl h "slot" |> Option.map asStr
          Action = None
          Required = h.GetProperty("required").GetBoolean() }

    /// A host-neutral query hole → the FastPath `HoleDecl` used to build a `Query`.
    /// (The query side never consults `Required` — `matchesQuery` gates only on the
    /// ENTRY's required holes — so a query hole maps cleanly onto the closed
    /// `HoleKind` DU with no fidelity loss.)
    let private holeDeclOf (h: JsonElement) : HoleDecl =
        let kind = reqStr h "kind"

        let hk =
            match kind with
            | "value" ->
                match tryEl h "space" with
                | Some s -> ValueHole(spaceOf s)
                | None -> failwithf "goldens: value query-hole '%s' has no space" (reqStr h "addr")
            | "slot" -> tryEl h "slot" |> Option.map asStr |> SlotHole
            | other -> failwithf "goldens: query-hole kind '%s' unsupported by F# findBySignature" other

        { Addr = reqStr h "addr"
          Name = reqStr h "name"
          Kind = hk }

    let private modeOf (s: string) : MatchMode =
        match s with
        | "Exact" -> Exact
        | "Subsumes" -> Subsumes
        | other -> failwithf "goldens: unknown match mode '%s'" other

    // ── the materialised golden (parsed off the live JsonDocument) ────────────

    type private FindGolden =
        { Name: string
          Mode: MatchMode
          Produce: string option
          Provide: HoleDecl list
          Expected: string list }

    /// A dummy pattern per registry id — `find` maps a matched entry back to its
    /// pattern by `Capability.Id`, so every registry id MUST appear in the bank's
    /// pattern map or a real match would be silently dropped. `Build` is never
    /// invoked by `find`.
    let private dummyPattern (id: string) (resultType: string) : FastPath.Pattern =
        { Id = id
          Title = id
          Summary = ""
          ResultType = resultType
          Holes = []
          Build = fun _ -> failwith "goldens harness: Pattern.Build is never invoked by find" }

    [<Tests>]
    let tests =
        testList
            "Fuaran.UI.FastPath.Goldens"
            [ test "findBySignature goldens agree with FastPath.find (5-way: F# leg of Phase 558)" {
                  if not (File.Exists goldensPath) then
                      // Single-repo checkout — the workspace-shared corpus is absent.
                      skiptest "wire-format-fixtures/function-registry/goldens.json absent (single-repo checkout)"
                  else
                      use doc = JsonDocument.Parse(File.ReadAllText goldensPath)
                      let root = doc.RootElement

                      // 1. Build the reference Fuaran.Core registry from the goldens'
                      //    `registry` — SigEntry-faithful, `required` honoured.
                      let registry =
                          (FunctionRegistry.empty, root.GetProperty("registry").EnumerateArray())
                          ||> Seq.fold (fun r entry ->
                              let id = reqStr entry "id"
                              let resultType = reqStr entry "resultType"

                              let holes =
                                  entry.GetProperty("holes").EnumerateArray() |> Seq.map sigEntryOf |> List.ofSeq

                              let sg: Signature =
                                  { Name = id
                                    Holes = holes
                                    Effect = Effect.pureDeterministic }

                              let cap = Capability.create id sg Placement.ClientDeclarative

                              match FunctionRegistry.register (FunctionRegistry.entry resultType cap) r with
                              | Ok next -> next
                              | Error e -> failwithf "goldens registry: registration error for '%s': %A" id e)

                      // Wrap it in a FastPath.Bank so the assertion runs through the
                      // named F# reference (FastPath.find), one pattern per registry id.
                      let patterns =
                          root.GetProperty("registry").EnumerateArray()
                          |> Seq.map (fun e -> reqStr e "id", dummyPattern (reqStr e "id") (reqStr e "resultType"))
                          |> Map.ofSeq

                      let bank: FastPath.Bank =
                          { Registry = registry
                            Patterns = patterns }

                      // 2. Materialise the findBySignature goldens.
                      let goldens =
                          root.GetProperty("findBySignature").EnumerateArray()
                          |> Seq.map (fun g ->
                              let q = g.GetProperty("query")

                              { Name = reqStr g "name"
                                Mode = modeOf (reqStr g "mode")
                                Produce = tryEl q "resultType" |> Option.map asStr
                                Provide =
                                  q.GetProperty("available").EnumerateArray() |> Seq.map holeDeclOf |> List.ofSeq
                                Expected = g.GetProperty("expectedIds").EnumerateArray() |> Seq.map asStr |> List.ofSeq })
                          |> List.ofSeq

                      Expect.isGreaterThan goldens.Length 0 "the goldens carry at least one findBySignature query"

                      // 3. Each golden: FastPath.find under its mode == its expectedIds
                      //    (order-insensitive — both sorted).
                      for gold in goldens do
                          let q = FastPath.query gold.Provide gold.Produce

                          let actual = FastPath.find gold.Mode q bank |> List.map (fun p -> p.Id) |> List.sort

                          Expect.equal
                              actual
                              (List.sort gold.Expected)
                              (sprintf "findBySignature golden '%s' (mode %A) agrees" gold.Name gold.Mode)

                      // NOTE: the goldens' 6 `compose` queries are deliberately NOT
                      // asserted here — F# ships no compose path, so compose stays
                      // py-authoritative (certified by py / ts / go / rs).
                      ()
              } ]
