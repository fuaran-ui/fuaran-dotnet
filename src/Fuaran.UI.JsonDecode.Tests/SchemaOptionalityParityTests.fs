module Fuaran.UI.JsonDecode.Tests.SchemaOptionalityParity

// ============================================================================
//  The published schema's `required` lists, measured against the IDL's own
//  optionality declaration.
//
//  `SchemaGen` is a hand-walk (deliberately — Fable has no reflection), and its
//  `required` lists are hand-typed string lists sitting beside `props` lists
//  that are also hand-typed. Nothing measured them against `idl.json`, which is
//  the artefact that DECLARES each field's optionality and from which the
//  structural layer is generated. So a slot could be `opt` in the IDL, an
//  `option` in `Types.fs`, read with `tryField` by the decoder, and REQUIRED by
//  the published schema — all at once, with every existing gate green, because
//  no fixture happened to omit it. `ButtonGroupItem.onClick` was exactly that
//  and had been since the case landed — pinned as a two-surface divergence by
//  Phase 1567's teaching-parity suite, which could see the disagreement and not
//  which surface was wrong.
//
//  The two directions are not the same claim, and this suite states them
//  separately because they fail for different reasons:
//
//    1. **An IDL-omittable field must NOT be in the schema's `required`.**
//       Hard, no exemptions. A field the wire may legally omit, required by the
//       schema, makes the schema REFUSE a document every conformant decoder
//       ACCEPTS — the schema then says something DIFFERENT from the decoder
//       rather than something LESS, which is the one property the whole schema
//       leg exists to preserve (`SchemaConformanceTests.fs` argues it at the
//       `ChartAnnotation` date slot, for the same reason). Omittable is
//       `optional`, `omitDefault` (the Phase 460 discipline) and `hostOnly`
//       (never on the wire at all).
//
//    2. **An IDL-required field SHOULD be in the schema's `required`** — with a
//       NAMED residue, pinned inversely. This direction leaves the schema
//       WEAKER than the contract, which is admissible (it says less), so a
//       divergence here is a finding rather than a defect, and two of them are
//       deliberate: see `decoderTolerantOfAbsence` below.
//
//  Resolution is structural, driven from the IDL's own type graph rather than
//  from a name convention, so an INLINE object schema is reached at its use
//  site: `ButtonGroup.buttons` is `TList (TRecord "ButtonGroupItem")` in the
//  IDL and `arrayOf (record …)` in the schema, with no `$def` between them.
//  Eight of the IDL's records have no `$def` at all and are reachable only this
//  way.
//
//  Read against `SchemaGen.wireFormatSchema` — the GENERATOR — not the
//  committed `schema.json`. The stale-schema guard already ties the file to the
//  generator, so pointing here fails in the session that edits `SchemaGen`
//  rather than one regeneration later.
// ============================================================================

open System.Collections.Generic
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI.Ops

// ─── Inputs ─────────────────────────────────────────────────────────────────

let private schemaDoc = JsonDocument.Parse SchemaGen.wireFormatSchema
let private schemaRoot = schemaDoc.RootElement
let private schemaDefs = schemaRoot.GetProperty "$defs"

let private idlDoc =
    JsonDocument.Parse(File.ReadAllText(Path.Combine(Corpus.findRoot (), "idl.json")))

let private idlRoot = idlDoc.RootElement

/// A required string property of an artefact. `GetString()` is nullable under
/// F# 10, and a null here means the artefact is malformed rather than that a
/// default applies — so it fails by name (the `OpVocabularyTests` treatment).
let private reqStr (name: string) (el: JsonElement) : string =
    match el.GetProperty(name).GetString() with
    | null -> failwithf "idl.json: '%s' is null" name
    | s -> s

let private tryProp (name: string) (el: JsonElement) : JsonElement option =
    match el.ValueKind with
    | JsonValueKind.Object ->
        match el.TryGetProperty name with
        | true, v -> Some v
        | _ -> None
    | _ -> None

// ─── The IDL side ───────────────────────────────────────────────────────────

/// What `idl.json` says about a field's presence on the wire. Only `Required`
/// obliges a document to carry it; the other three are all omittable, for three
/// different reasons that make no difference to a `required` list.
type private Presence =
    | Required
    /// An F# `option` — absent means None.
    | Optional
    /// Emitted only when it differs from its declared default (Phase 460).
    | OmitDefault
    /// Not on the wire at all — a closure / accessor the host supplies.
    | HostOnly

let private presenceOf (field: JsonElement) : string * Presence =
    let name = reqStr "name" field

    let presence =
        match reqStr "$type" (field.GetProperty "optionality") with
        | "required" -> Required
        | "optional" -> Optional
        | "omitDefault" -> OmitDefault
        | "hostOnly" -> HostOnly
        | other -> failwithf "idl.json: unknown optionality '%s' on field '%s'" other name

    name, presence

let private fieldsOf (owner: JsonElement) : (string * Presence) list =
    match tryProp "fields" owner with
    | None -> []
    | Some fs -> fs.EnumerateArray() |> Seq.map presenceOf |> List.ofSeq

let private idlRecords =
    idlRoot.GetProperty("records").EnumerateArray()
    |> Seq.map (fun r -> reqStr "name" r, r)
    |> dict

let private idlUnions =
    idlRoot.GetProperty("unions").EnumerateArray()
    |> Seq.map (fun u -> reqStr "name" u, u)
    |> List.ofSeq

// ─── The schema side ────────────────────────────────────────────────────────

/// Follow a bare `{"$ref": "#/$defs/X"}` to its definition. Only a SOLE `$ref`
/// is followed: a `$ref` sitting beside sibling keywords is a narrowing, and
/// replacing it with its target would discard the narrowing.
let private deref (node: JsonElement) : JsonElement =
    let mutable current = node
    let mutable hops = 0

    while hops < 32
          && current.ValueKind = JsonValueKind.Object
          && current.EnumerateObject() |> Seq.length = 1
          && (current.TryGetProperty "$ref" |> fst) do
        match current.GetProperty("$ref").GetString() with
        | null -> hops <- 32
        | r ->
            let name = r.Substring(r.LastIndexOf '/' + 1)

            match schemaDefs.TryGetProperty name with
            | true, target ->
                current <- target
                hops <- hops + 1
            | _ -> hops <- 32

    current

/// An object schema's effective `(properties, required)`, merging `allOf` —
/// which is how `duCaseHoisted` splits a kind branch into a `$type` pin plus a
/// `$ref` to its spec, and how `forbidding` hangs its near-miss refusals.
let rec private collect (node: JsonElement) : Dictionary<string, JsonElement> * Set<string> =
    let node = deref node
    let props = Dictionary<string, JsonElement>()
    let mutable required = Set.empty

    match tryProp "allOf" node with
    | Some subs when subs.ValueKind = JsonValueKind.Array ->
        for sub in subs.EnumerateArray() do
            let p, r = collect sub

            for kv in p do
                props[kv.Key] <- kv.Value

            required <- Set.union required r
    | _ -> ()

    match tryProp "properties" node with
    | Some ps when ps.ValueKind = JsonValueKind.Object ->
        for p in ps.EnumerateObject() do
            props[p.Name] <- p.Value
    | _ -> ()

    match tryProp "required" node with
    | Some rs when rs.ValueKind = JsonValueKind.Array ->
        for r in rs.EnumerateArray() do
            match r.GetString() with
            | null -> ()
            | s -> required <- Set.add s required
    | _ -> ()

    props, required

/// The `oneOf` branch of a DU position whose `$type` is pinned to `tag`.
/// Recurses through a nested `oneOf` — `NodeKind` is a union OF the four
/// category unions, so a kind's branch is two levels down.
let rec private branchFor (node: JsonElement) (tag: string) : JsonElement option =
    let node = deref node

    match tryProp "oneOf" node with
    | Some branches when branches.ValueKind = JsonValueKind.Array ->
        branches.EnumerateArray()
        |> Seq.tryPick (fun b ->
            let props, _ = collect b

            let pinned =
                match props.TryGetValue "$type" with
                | true, t -> tryProp "const" t |> Option.bind (fun c -> c.GetString() |> Option.ofObj)
                | _ -> None

            match pinned with
            | Some c when c = tag -> Some b
            | _ -> branchFor b tag)
    | _ -> None

// ─── Findings ───────────────────────────────────────────────────────────────

type private Finding =
    {
        Site: string
        /// Omittable in the IDL, in the schema's `required`. Direction 1.
        OverRequired: string list
        /// Required in the IDL, absent from the schema's `required`. Direction 2.
        UnderRequired: string list
    }

let private findings = ResizeArray<Finding>()
let private reachedRecords = HashSet<string>()
let private recordStack = HashSet<string>()

/// `Binding.Static.value` is IDL-`optional` and schema-REQUIRED at the four
/// scalar instantiations, deliberately: Phase 1140 split the presence of the
/// `Static` payload per element type, because `requireFloat` / `requireInt` /
/// `requireBool` / `requireString` refuse the `JNull` an absent key routes to
/// while the collection and choice slots read absence as meaningful. It is
/// pinned INVERSELY in `BindingStaticPresenceTests.fs` in both directions, so
/// it is measured — just not here, where the IDL's single declaration cannot
/// express a per-instantiation answer.
let private bindingStaticPayloadExemption = set [ "Binding.Static", "value" ]

let private compare (site: string) (idlFields: (string * Presence) list) (node: JsonElement) =
    let props, schemaRequired = collect node

    let exempt =
        bindingStaticPayloadExemption
        |> Set.filter (fun (s, _) -> site.StartsWith(s + " ") || site = s)
        |> Set.map snd

    let known = idlFields |> List.map fst |> Set.ofList

    let idlRequired =
        idlFields |> List.filter (snd >> (=) Required) |> List.map fst |> Set.ofList

    // `$type` is the schema's own discriminator, not an IDL field.
    let stated = Set.difference schemaRequired (Set.singleton "$type")

    let over =
        Set.difference (Set.intersect stated (Set.difference known exempt)) idlRequired

    let under =
        Set.difference (Set.difference (Set.intersect idlRequired known) exempt) stated

    if not (Set.isEmpty over && Set.isEmpty under) then
        findings.Add
            { Site = site
              OverRequired = List.ofSeq over
              UnderRequired = List.ofSeq under }

    props

/// Descend an IDL field type into the schema node standing at that slot, so an
/// inline record is checked where it is used.
let rec private walkType (idlType: JsonElement) (node: JsonElement) =
    match tryProp "$type" idlType |> Option.bind (fun t -> t.GetString() |> Option.ofObj) with
    | Some "record" -> checkRecord (reqStr "name" idlType) node
    | Some "list" ->
        match tryProp "items" (deref node) with
        | Some items -> walkType (idlType.GetProperty "of") items
        | None -> ()
    | Some "map" ->
        match tryProp "additionalProperties" (deref node) with
        | Some values ->
            match tryProp "of" idlType with
            | Some of_ -> walkType of_ values
            | None -> ()
        | None -> ()
    | Some "option" ->
        match tryProp "of" idlType with
        | Some of_ -> walkType of_ node
        | None -> ()
    | _ -> ()

and private recurse
    (idlFields: (string * Presence) list)
    (owner: JsonElement)
    (props: Dictionary<string, JsonElement>)
    =
    let byName =
        match tryProp "fields" owner with
        | None -> Map.empty
        | Some fs -> fs.EnumerateArray() |> Seq.map (fun f -> reqStr "name" f, f) |> Map.ofSeq

    for (name, _) in idlFields do
        match props.TryGetValue name, Map.tryFind name byName with
        | (true, slot), Some field -> walkType (field.GetProperty "type") slot
        | _ -> ()

and private checkRecord (name: string) (node: JsonElement) =
    match idlRecords.TryGetValue name with
    | true, rec_ when not (recordStack.Contains name) ->
        recordStack.Add name |> ignore
        reachedRecords.Add name |> ignore
        let fields = fieldsOf rec_
        let props = compare ("record " + name) fields node
        recurse fields rec_ props
        recordStack.Remove name |> ignore
    | _ -> ()

// ─── The walk ───────────────────────────────────────────────────────────────

let private runWalk () =
    // Unions with a `$def` of their own.
    for (name, union) in idlUnions do
        if name = "Binding" then
            // `Binding<'T>` has no single `$def` — Phase 1068 emits one per
            // instantiated element type. Every instantiation carries the same
            // case set, so each is measured against the one IDL declaration.
            let instantiations =
                schemaDefs.EnumerateObject()
                |> Seq.map _.Name
                |> Seq.filter _.StartsWith("Binding_")
                |> List.ofSeq

            for inst in instantiations do
                for case in union.GetProperty("cases").EnumerateArray() do
                    let tag = reqStr "tag" case

                    match branchFor (schemaDefs.GetProperty inst) tag with
                    | Some branch ->
                        let fields = fieldsOf case
                        let props = compare (sprintf "Binding.%s (%s)" tag inst) fields branch
                        recurse fields case props
                    | None ->
                        findings.Add
                            { Site = sprintf "Binding.%s (%s) — no schema branch" tag inst
                              OverRequired = []
                              UnderRequired = [] }
        else
            match schemaDefs.TryGetProperty name with
            | true, def ->
                for case in union.GetProperty("cases").EnumerateArray() do
                    let tag = reqStr "tag" case

                    match branchFor def tag with
                    | Some branch ->
                        let fields = fieldsOf case
                        let props = compare (sprintf "union %s.%s" name tag) fields branch
                        recurse fields case props
                    | None ->
                        findings.Add
                            { Site = sprintf "union %s.%s — no schema branch" name tag
                              OverRequired = []
                              UnderRequired = [] }
            | _ ->
                findings.Add
                    { Site = sprintf "union %s — no $def" name
                      OverRequired = []
                      UnderRequired = [] }

    // Node kinds, reached through `NodeKind`'s nested category unions.
    let nodeKind = schemaDefs.GetProperty "NodeKind"

    for kind in idlRoot.GetProperty("kinds").EnumerateArray() do
        let tag = reqStr "tag" kind

        match branchFor nodeKind tag with
        | Some branch ->
            let fields = fieldsOf kind
            let props = compare ("kind " + tag) fields branch
            recurse fields kind props
        | None ->
            findings.Add
                { Site = sprintf "kind %s — no schema branch" tag
                  OverRequired = []
                  UnderRequired = [] }

    // Tree ops.
    let treeOp = schemaDefs.GetProperty "TreeOp"

    for op in idlRoot.GetProperty("ops").EnumerateArray() do
        let tag = reqStr "tag" op

        match branchFor treeOp tag with
        | Some branch ->
            let fields = fieldsOf op
            let props = compare ("op " + tag) fields branch
            recurse fields op props
        | None ->
            findings.Add
                { Site = sprintf "op %s — no schema branch" tag
                  OverRequired = []
                  UnderRequired = [] }

    // The node envelope's own fields.
    let nodeFieldsOwner = idlRoot

    let nodeFields =
        idlRoot.GetProperty("nodeFields").EnumerateArray()
        |> Seq.map presenceOf
        |> List.ofSeq

    let nodeProps = compare "Node" nodeFields (schemaDefs.GetProperty "Node")

    let byName =
        idlRoot.GetProperty("nodeFields").EnumerateArray()
        |> Seq.map (fun f -> reqStr "name" f, f)
        |> Map.ofSeq

    ignore nodeFieldsOwner

    for (name, _) in nodeFields do
        match nodeProps.TryGetValue name, Map.tryFind name byName with
        | (true, slot), Some field -> walkType (field.GetProperty "type") slot
        | _ -> ()

    // Records with a `$def` of their own — the ones no field type reached.
    for name in idlRecords.Keys |> List.ofSeq do
        match schemaDefs.TryGetProperty name with
        | true, def -> checkRecord name def
        | _ -> ()

runWalk ()

let private overFindings =
    findings |> Seq.filter (fun f -> not f.OverRequired.IsEmpty) |> List.ofSeq

let private underFindings =
    findings |> Seq.filter (fun f -> not f.UnderRequired.IsEmpty) |> List.ofSeq

let private structuralFindings =
    findings
    |> Seq.filter (fun f -> f.OverRequired.IsEmpty && f.UnderRequired.IsEmpty)
    |> List.ofSeq

let private render (fs: Finding list) (pick: Finding -> string list) =
    fs
    |> List.map (fun f -> sprintf "  %s: %s" f.Site (pick f |> List.sort |> String.concat ", "))
    |> String.concat "\n"

// ─── Direction 2's named residue ────────────────────────────────────────────

/// IDL-`required` fields the published schema deliberately does NOT require.
/// Named, never counted — a site MOVING onto or off this list is the
/// interesting event (the `schemaInexpressibleRejects` posture).
///
/// Both entries are the same case, and it is a case where the two artefacts
/// answer different questions rather than one of them being wrong. `required`
/// in the IDL says the ENCODER always emits the field and the F# slot is not an
/// `option`; `required` in the schema says a document LACKING it is invalid.
/// For these two the decoder deliberately reads absence — `Chart.stacked`
/// defaults to `false` for wire that predates Phase 126's field, and
/// `Tabs.activeIndex` to `Binding.Static (Some 0)` — so requiring them here
/// would make the published schema refuse documents every conformant host
/// accepts. Saying LESS than the decoder is admissible; saying something
/// DIFFERENT is the one thing this artefact must not do.
///
/// Pinned INVERSELY below: if either slot's decoder stops tolerating absence
/// (or `SchemaGen` starts requiring it), this test fails and the entry goes,
/// rather than the exemption outliving its reason.
let private decoderTolerantOfAbsence: Set<string * string> =
    set [ "kind Chart", "stacked"; "kind Tabs", "activeIndex" ]

/// Records `idl.json` declares but no IDL field type references, so the walk
/// cannot reach whatever the schema says about them. Both are reached in the
/// generated layer only through a `hosted` codec pair (`Binding<RangePair>` /
/// `Binding<DateRangePair>`), and the schema models both slots as `anyJson` —
/// the §5 abstention — so there is no `required` list to measure at either.
/// Named for the same reason as the residue above: an unreachable record is a
/// blind spot, and a blind spot that is not enumerated grows silently.
let private unreachableRecords = set [ "DateRangePair"; "RangePair" ]

// ─── Tests ──────────────────────────────────────────────────────────────────

[<Tests>]
let optionalityParityTests =
    testList
        "schema optionality parity (IDL)"
        [ testCase "no IDL-omittable field appears in the schema's required list" (fun () ->
              if not overFindings.IsEmpty then
                  failtestf
                      "The published schema REQUIRES fields the IDL declares omittable, so it refuses documents the decoder accepts:\n%s\n\nFix `SchemaGen.fs` — the IDL is the artefact."
                      (render overFindings _.OverRequired))

          testCase "every IDL-required field is schema-required, but for the named residue" (fun () ->
              let observed =
                  underFindings
                  |> List.collect (fun f -> f.UnderRequired |> List.map (fun n -> f.Site, n))
                  |> Set.ofList

              let unexpected = Set.difference observed decoderTolerantOfAbsence
              let stale = Set.difference decoderTolerantOfAbsence observed

              if not (Set.isEmpty unexpected) then
                  failtestf
                      "The published schema does NOT require fields the IDL declares required, and these are not on the named residue:\n%s\n\nEither require them in `SchemaGen.fs`, or add them to `decoderTolerantOfAbsence` with the reason."
                      (unexpected
                       |> Seq.map (fun (s, n) -> sprintf "  %s: %s" s n)
                       |> Seq.sort
                       |> String.concat "\n")

              if not (Set.isEmpty stale) then
                  failtestf
                      "These sites are on the `decoderTolerantOfAbsence` residue but no longer diverge — remove them:\n%s"
                      (stale
                       |> Seq.map (fun (s, n) -> sprintf "  %s: %s" s n)
                       |> Seq.sort
                       |> String.concat "\n"))

          testCase "every IDL entity resolves to a schema position" (fun () ->
              if not structuralFindings.IsEmpty then
                  failtestf
                      "The walk could not locate a schema position for:\n%s"
                      (structuralFindings
                       |> List.map (fun f -> "  " + f.Site)
                       |> List.sort
                       |> String.concat "\n"))

          testCase "the unreachable-record residue is exactly the named set" (fun () ->
              let declared = idlRecords.Keys |> Set.ofSeq
              let unreached = Set.difference declared (Set.ofSeq reachedRecords)

              Expect.equal
                  unreached
                  unreachableRecords
                  "an IDL record the type graph does not reach is a blind spot in this guard — enumerate it with its reason, or wire it up") ]
