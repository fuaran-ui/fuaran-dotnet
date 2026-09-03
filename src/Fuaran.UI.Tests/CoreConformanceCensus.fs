module Fuaran.UI.Tests.CoreConformanceCensus

// Phase 1475 — the Core-conformance census in the UI tier.
//
// The 2026-09-03 sweep of the conformance kit found that most of its law families run only in
// Core's own suite: the UI tier, Core's largest consumer, reaches a handful of them and all from
// one file (`CoreAdoptionTests.fs`). That gap was unmeasured until a sweep happened to look at it,
// which is the defect this file exists to close — not the gap itself, but its INVISIBILITY.
//
// So this is a census, in the same shape as Core's own (`SampleAdequacyTests.censusTests`, from
// fuaran-core#121) and deliberately its mirror image. It enumerates every public law family the
// PINNED `Fuaran.Core.Conformance` ships — by reflection, never by a list, because a list is
// exactly what cannot notice a family nobody added to it — and resolves each to either an adopter
// in this repo or a stated reason for not adopting it. A family that is neither fails the suite.
//
// Both directions are checked, and the second one is the load-bearing half:
//
//   * roster -> census: a family the pinned kit ships with no row is red. This is what makes a
//     family Core ADDS arrive here as a failing row rather than silently.
//   * census -> roster: a row naming a family the kit no longer ships is red, because a row for a
//     renamed or removed family reads as coverage while covering nothing.
//   * census -> tests: an `Adopted` row naming a test that no longer exists is red.
//   * tests -> census: a test that runs a Core law family with no `Adopted` row is red. Enrolment
//     by NAME alone is the failure mode Core's own narrowing tripped on 2026-09-03 — a string
//     declaration and the code it describes drift apart silently — so enrolment here is by name
//     AND by reflection, and the two are cross-checked against each other.
//
// The rendered table is committed at `docs/core-conformance.md` so adoption is greppable without
// running anything. The suite regenerates that file and asserts the committed copy already
// matched, so a stale doc is red and a fresh checkout regenerates it.

open System
open System.IO
open System.Reflection
open System.Text
open System.Text.RegularExpressions
open Expecto

// ---------------------------------------------------------------------------
//  the closed classification
// ---------------------------------------------------------------------------

/// How this repo answers for one law family of the pinned kit. The set is CLOSED on purpose: a
/// free-text "unassigned" would let a family be filed away without anyone deciding anything, which
/// is the state this census exists to make impossible.
type Adoption =
    /// A test in this repo runs the family. `test` is the Expecto test-case name (which must still
    /// exist); `port` is the kit entry point through which it is reached — several families are
    /// reached through an aggregate (`Conformance.certify`) rather than by their own name.
    | Adopted of test: string * port: string
    /// The tier does not use the mechanism the family certifies. The reason names the mechanism, so
    /// the next reader can CHECK the classification rather than trust it.
    | NotUsed of mechanism: string
    /// The family belongs to a sibling host of the wire format, not to this one.
    | SiblingHost of host: string
    /// A named roadmap phase carries the enrolment; it flips to `Adopted` when that phase ships.
    | CarriedBy of phase: string

// ---------------------------------------------------------------------------
//  the census
// ---------------------------------------------------------------------------

/// The three test cases in `CoreAdoptionTests.fs` that reach a law family today. Named once so a
/// row and the test it enrols cannot drift apart by a typo.
let private certifyTest =
    "the Fuaran.UI witness certifies end-to-end via the unified Conformance.certify"

let private hashFnTest =
    "the portable SHA-256 certifies under Core's hashFnLaws (the supply-your-own-crypto contract)"

/// The persistence families (fuaran#1477) are enrolled from a DIFFERENT test project —
/// `Fuaran.UI.OpStream.Tests`, where the durable ports they are about live. The census already
/// discovers every `*.Tests` project whose `.fsproj` references the kit, and checks each `Adopted`
/// row against the project that actually runs the family, so no convention needed extending for
/// this; the names are declared here beside the others so all enrolment reads from one place.
let private casTest = "casLaws certifies over the Fuaran.UI op-stream witness"

let private idempotencyTest =
    "idempotencyLaws certifies over the Fuaran.UI op-stream witness"

let private snapshotTest =
    "snapshotLaws and snapshotLawsWith certify over the Fuaran.UI op-stream witness"

/// One row per public law family of the pinned kit. Order here is authoring order (grouped by
/// classification); the report sorts by family key, so this list may be reordered freely.
let census: (string * Adoption) list =
    [
      // ---- adopted today: CoreAdoptionTests.fs ----
      // `Conformance.certify` runs witnessLaws, then (on a well-formed witness) opAlgebra, diffLaws
      // and streamLaws. Only three of those four are roster families — `opAlgebra` is not
      // `*Laws`-suffixed and so is not a family under the shared predicate.
      "Conformance.witnessLaws", Adopted(certifyTest, "Conformance.certify")
      "Conformance.diffLaws", Adopted(certifyTest, "Conformance.certify")
      "Conformance.streamLaws", Adopted(certifyTest, "Conformance.certify (also Conformance.certifyStream)")
      "Conformance.hashFnLaws", Adopted(hashFnTest, "Conformance.hashFnLaws")
      "Conformance.hashFnAdversarialLaws", Adopted(hashFnTest, "Conformance.hashFnAdversarialLaws")

      // ---- fuaran#1476 — multi-writer DAG laws over the UI op-stream ----
      "Conformance.dagLaws", CarriedBy "fuaran#1476"
      "FoldConfluence.laneFoldLaws", CarriedBy "fuaran#1476"
      "FoldConfluence.laneFoldLawsWith", CarriedBy "fuaran#1476"
      "Conformance.mergeConflictLaws", CarriedBy "fuaran#1476"
      "Conformance.reconcileLaws", CarriedBy "fuaran#1476"
      "Conformance.concurrencyLaws", CarriedBy "fuaran#1476"
      "Conformance.concurrencyLawsWith", CarriedBy "fuaran#1476"
      "Conformance.arbitrationLaws", CarriedBy "fuaran#1476"

      // ---- fuaran#1477 — persistence laws over the tier's op-stream witness ----
      // All four run in `Fuaran.UI.OpStream.Tests`, beside the durable ports they are about. Note
      // what the ROW claims and what it does not: these families are parameterised over a
      // `StreamWitness` (Apply / Encode / Decode) and Core owns the append, so the adoption is over
      // the tier's reducer, op codec, chain digest and node encoder — NOT over `IOpStreamSink`,
      // which offers neither a compare-and-append nor an idempotency key. That gap is asserted
      // directly by the store tests in the same file.
      "Conformance.casLaws", Adopted(casTest, "Conformance.casLaws")
      "Conformance.idempotencyLaws", Adopted(idempotencyTest, "Conformance.idempotencyLaws")
      "Conformance.snapshotLaws", Adopted(snapshotTest, "Conformance.snapshotLaws")
      "Conformance.snapshotLawsWith", Adopted(snapshotTest, "Conformance.snapshotLawsWith")

      // ---- fuaran#1478 — function-registry and capability laws over the FastPath seam ----
      "Conformance.registryLaws", CarriedBy "fuaran#1478"
      "Conformance.capabilityLaws", CarriedBy "fuaran#1478"
      "Conformance.memoLaws", CarriedBy "fuaran#1478"
      "Conformance.memoSoundnessLaws", CarriedBy "fuaran#1478"
      "Conformance.functionVerifyLaws", CarriedBy "fuaran#1478"
      "Conformance.verifyHonestyLaws", CarriedBy "fuaran#1478"
      "Conformance.compositionLaws", CarriedBy "fuaran#1478"
      "Conformance.packLoadingLaws", CarriedBy "fuaran#1478"
      "Conformance.paramLaws", CarriedBy "fuaran#1478"
      "Conformance.deferredLaws", CarriedBy "fuaran#1478"
      // Not named by 1478's task list, placed there because it is the precondition of the memo
      // families that phase carries: `applyMemo`'s content-addressed key is a tree encoding, and a
      // non-injective encoder makes the cache serve the WRONG tree. The tier consumes that cache
      // model (`Fuaran.UI.Memo`, Phase 360), so the family has a real subject here.
      "Conformance.encoderInjectivityLaws", CarriedBy "fuaran#1478"

      // ---- fuaran#1479 — footprint and delta laws over the live-transform seam ----
      "Conformance.footprintLaws", CarriedBy "fuaran#1479"
      "IncrementalDelta.laws", CarriedBy "fuaran#1479"
      "IncrementalDelta.lawsWith", CarriedBy "fuaran#1479"
      "Conformance.dirtyPropagationLaws", CarriedBy "fuaran#1479"
      // Not named by 1479's task list. The tier does incremental dataframe evaluation through
      // `Incremental.primeOn` / `refreshOn` (`ServerDriven/LiveTransform.fs`), which is the seam
      // 1479 certifies; this family is the equivalence claim over that evaluation, so it belongs
      // with the phase that owns the seam rather than being called unused.
      "Conformance.incrementalLaws", CarriedBy "fuaran#1479"

      // ---- fuaran#1480 — attributed and attestation laws over the attributed op-stream ----
      "Conformance.attributedLaws", CarriedBy "fuaran#1480"
      "Conformance.attestationLaws", CarriedBy "fuaran#1480"
      "Conformance.noAttestationVacuityLaws", CarriedBy "fuaran#1480"

      // ---- fuaran#1481 — columnar laws over the tier's Column usage ----
      "Conformance.columnarOpLaws", CarriedBy "fuaran#1481"
      "Conformance.columnarValidatorLaws", CarriedBy "fuaran#1481"
      "Conformance.aggregateParityLaws", CarriedBy "fuaran#1481"
      "Conformance.schemaWalkLaws", CarriedBy "fuaran#1481"

      // ---- a sibling host's family ----
      "Conformance.captureReplayLaws", SiblingHost "fuaran-ts / fuaran-go (fuaran#1482)"

      // ---- mechanisms this tier does not use ----
      "Conformance.propagationEvalLaws",
      NotUsed
          "Fuaran.Core.Propagation's evaluator — the tier's reactivity runs on its own store and subscription channels (StateStore / FilterStore / SelectionStore / QueryStore) and never evaluates a Core propagation graph"
      "Conformance.leaseLaws",
      NotUsed
          "Fuaran.Core.Lease — leases are a coordination-plane mechanism for concurrent writers; the UI tier takes none"
      "Conformance.aiSurfaceLaws",
      NotUsed
          "Fuaran.Core.AiSurface — the tier ships its own runtime introspection surface (Fuaran.UI.AiTools) and consumes no Core AI surface"
      "Conformance.projectionLaws",
      NotUsed "Fuaran.Core.Projection — the tier renders a tree; it maintains no Core projection over an op stream"
      "Conformance.queryLaws",
      NotUsed
          "Fuaran.Core.Query's registry seam — QuerySource.fs is a deliberately thinner UI-facing sibling built on the Column / DataFrame types and explicitly NOT on the Core query registry, which no project here references"
      "Conformance.capabilityPipelineLaws",
      NotUsed "Fuaran.Core.Function's CapabilityPipeline — the tier composes no capability pipeline"
      "Conformance.capabilityPipelineIncrementalLaws",
      NotUsed "Fuaran.Core.Function's CapabilityPipeline — the tier composes no capability pipeline"
      "Conformance.normalizeLaws",
      NotUsed
          "Fuaran.Core.Ops.normalize — Apply.fs delegates only the structural-five APPLY to Core (applyContained); the tier ships no op-script normaliser, so the family's subject has no call site here"
      "Conformance.transformLaws",
      NotUsed
          "a host dataframe evaluator — QueryRefine consumes Fuaran.Core.DataFrame.evalPipeline as the pinned reference rather than shipping a second evaluator, so the parity laws have no host implementation to compare against the reference"
      "Conformance.canonicalFloatLaws",
      NotUsed
          "Wire.Canon.canonicalFloat — the tier's canonical-JSON encoder carries its own Fable-safe float formatter (CanonicalJson.formatFiniteDouble), and this family is self-contained over Core's encoder rather than taking a host one; cross-host float parity here is gated by the wire-format conformance corpus, a multi-host oracle it cannot replace" ]

// ---------------------------------------------------------------------------
//  the roster — by reflection over the PINNED kit
// ---------------------------------------------------------------------------

let private conformanceAssembly = typeof<Fuaran.Core.LawResult>.Assembly

/// The modules the kit publishes law entry points from. Mirrors Core's own census exactly, so the
/// two agree about what a "family" is; a module the kit stops shipping fails rather than silently
/// contributing nothing.
let private lawModules =
    [ "Conformance"; "FoldConfluence"; "IncrementalDelta"; "WireNullTolerance" ]

/// Core's roster predicate, character for character. The names are deliberately NOT all
/// `*Laws`-suffixed — `IncrementalDelta.laws`, `FoldConfluence.laneFoldLawsWith` — so a narrower
/// predicate here would silently disagree with Core's about the size of the shipped set.
let private isLawEntry (m: MethodInfo) =
    let n = m.Name

    n.EndsWith("Laws", StringComparison.Ordinal)
    || n.EndsWith("LawsWith", StringComparison.Ordinal)
    || n = "laws"
    || n = "lawsWith"

let private shippedFamilies () : string list =
    [ for moduleName in lawModules do
          match conformanceAssembly.GetType("Fuaran.Core." + moduleName) with
          | null -> failtestf "the pinned kit no longer ships a module named %s" moduleName
          | t ->
              for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
                  if isLawEntry m then
                      yield moduleName + "." + m.Name ]
    |> List.distinct
    |> List.sort

/// The pinned kit's version, read from the assembly rather than hard-coded: the version decides
/// which families exist, so a report that named it from a literal could describe a kit that is not
/// the one the suite ran against.
let private kitVersion () =
    match conformanceAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
    | null -> string (conformanceAssembly.GetName().Version)
    | attr ->
        // `0.18.0+<sha>` — the build metadata moves with every Core build, so it is dropped: the
        // committed report must be stable across rebuilds of the same pinned version.
        attr.InformationalVersion.Split('+')[0]

// ---------------------------------------------------------------------------
//  locating the checkout + reading the test sources
// ---------------------------------------------------------------------------

/// The repo root, found by climbing from the test binary. Identified by two files rather than one,
/// so a directory that merely happens to hold a `run.ps1` cannot be mistaken for it.
let private repoRoot =
    lazy
        (let rec climb (dir: DirectoryInfo option) =
            match dir with
            | None -> None
            | Some d ->
                if
                    File.Exists(Path.Combine(d.FullName, "run.ps1"))
                    && File.Exists(Path.Combine(d.FullName, "Fuaran.sln"))
                then
                    Some d.FullName
                else
                    climb (Option.ofObj d.Parent)

         match climb (Some(DirectoryInfo(AppContext.BaseDirectory))) with
         | Some root -> root
         | None ->
             // Never skip. A census that cannot read the sources it quantifies over would report a
             // fully-enrolled tier, which is the one answer it must never give by accident.
             failwith
                 "CoreConformanceCensus: could not locate the repo root above the test binary — the test sources could not be read, so the enrolment check has proved nothing.")

/// This file declares every family name as a string, so scanning it would find a "reference" to
/// each one. It is the declaration, not an adopter; excluded by name.
let private censusFileName = "CoreConformanceCensus.fs"

/// `//` line comments are stripped before scanning. Prose routinely names a law family
/// (`Conformance.hashFnLaws` in a header comment) and a mention is not a run; matching on comments
/// would make a doc edit fail the suite.
let private stripLineComments (text: string) =
    text.Split('\n')
    |> Array.map (fun line ->
        match line.IndexOf("//", StringComparison.Ordinal) with
        | -1 -> line
        | i -> line.Substring(0, i))
    |> String.concat "\n"

/// Every test project that references the conformance kit, as `(projectDir, [ file, source ])`.
/// Discovered from the `.fsproj` files rather than named here, so a project that adopts a law
/// family in a later phase enters this scan with no edit.
let private conformanceTestProjects () =
    let src = Path.Combine(repoRoot.Value, "src")

    // `Path.GetFileName` is `string | null` under F# 10 nullness; a path from `GetDirectories`
    // always has one, so falling back to the whole path is unreachable rather than lenient.
    let leaf (p: string) =
        Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

    Directory.GetDirectories(src)
    |> Array.filter (fun dir ->
        (leaf dir).EndsWith(".Tests", StringComparison.Ordinal)
        && Directory.GetFiles(dir, "*.fsproj")
           |> Array.exists (fun p -> File.ReadAllText(p).Contains "Fuaran.Core.Conformance"))
    |> Array.map (fun dir ->
        let sources =
            Directory.GetFiles(dir, "*.fs")
            |> Array.filter (fun p -> leaf p <> censusFileName)
            |> Array.map (fun p -> leaf p, stripLineComments (File.ReadAllText p))

        leaf dir, sources)

/// The module aliases a source binds to the kit's law modules — `module CoreConf =
/// Fuaran.Core.Conformance` — plus the plain and fully-qualified spellings. Resolving aliases is
/// what keeps the scan from depending on any one file's naming habit.
let private aliasesFor (source: string) =
    let map = System.Collections.Generic.Dictionary<string, string>()

    for m in lawModules do
        map[m] <- m
        map["Fuaran.Core." + m] <- m

    for m in Regex.Matches(source, @"module\s+([A-Za-z_][\w']*)\s*=\s*Fuaran\.Core\.([A-Za-z_][\w']*)") do
        let alias = m.Groups[1].Value
        let target = m.Groups[2].Value

        if List.contains target lawModules then
            map[alias] <- target

    map

/// Every `Module.fn` token in a source that resolves, through that source's aliases, to a public
/// entry point of the kit. Returns the canonical `Module.fn` spelling.
let private kitReferences (source: string) : Set<string> =
    let aliases = aliasesFor source

    Regex.Matches(source, @"\b([A-Za-z_][\w']*(?:\.[A-Za-z_][\w']*)*)\.([a-z][\w']*)\b")
    |> Seq.choose (fun m ->
        let qualifier = m.Groups[1].Value
        let fn = m.Groups[2].Value

        match aliases.TryGetValue qualifier with
        | true, target -> Some(target + "." + fn)
        | _ -> None)
    |> Set.ofSeq

// ---------------------------------------------------------------------------
//  the report
// ---------------------------------------------------------------------------

let private reportPath () =
    Path.Combine(repoRoot.Value, "docs", "core-conformance.md")

let private statusOf =
    function
    | Adopted _ -> "Adopted"
    | NotUsed _ -> "Not used"
    | SiblingHost _ -> "Sibling host"
    | CarriedBy _ -> "Carried by phase"

let private detailOf =
    function
    | Adopted(test, port) -> sprintf "`%s` — via `%s`" test port
    | NotUsed mechanism -> mechanism
    | SiblingHost host -> host
    | CarriedBy phase -> phase

/// The committed table. Deterministic by construction: rows sorted by family key, LF endings, and
/// no timestamp — a report that changed on every run could never be a drift guard.
let render (rows: (string * Adoption) list) : string =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore

    line "# Core-conformance census — the UI tier"
    line ""
    line "<!-- GENERATED by src/Fuaran.UI.Tests/CoreConformanceCensus.fs. Do not hand-edit: the"
    line "     suite regenerates this file and fails when the committed copy has drifted. -->"
    line ""

    line (
        sprintf
            "Every public law family the pinned `Fuaran.Core.Conformance` **%s** ships, and how this repo answers for it."
            (kitVersion ())
    )

    line ""
    line "A family with no row fails the suite, and so does a row naming a family the pinned kit no"
    line "longer ships — enrolment is by name AND by reflection, and the two are checked against"
    line "each other. `Carried by phase` is an enrolment a named roadmap phase will flip to"
    line "`Adopted` when it ships."
    line ""
    line "| Family | Status | Detail |"
    line "|---|---|---|"

    for family, adoption in rows |> List.sortBy fst do
        line (sprintf "| `%s` | %s | %s |" family (statusOf adoption) (detailOf adoption))

    line ""
    line "## Summary"
    line ""
    line "| Status | Families |"
    line "|---|---|"

    for status in [ "Adopted"; "Carried by phase"; "Not used"; "Sibling host" ] do
        let n = rows |> List.filter (fun (_, a) -> statusOf a = status) |> List.length
        line (sprintf "| %s | %d |" status n)

    line (sprintf "| **Total** | **%d** |" (List.length rows))

    sb.ToString()

// ---------------------------------------------------------------------------
//  the census tests
// ---------------------------------------------------------------------------

let private normaliseEol (s: string) = s.Replace("\r\n", "\n")

[<Tests>]
let tests =
    testList
        "Core conformance census (Fuaran.UI)"
        [

          testCase "every law family the pinned kit ships has exactly one census row"
          <| fun _ ->
              // The half a declaration structurally cannot do for itself: a declaration quantifies
              // over what it names, so a family nobody enrolled produces no finding at any grade.
              // Reflection is what closes it — a family Core ADDS arrives here as a failing row.
              let declared = census |> List.map fst |> Set.ofList
              let shipped = shippedFamilies ()

              let unclassified = shipped |> List.filter (fun f -> not (Set.contains f declared))

              Expect.isEmpty
                  unclassified
                  (sprintf
                      "these law families of the pinned kit have no census row — classify each as Adopted / NotUsed / SiblingHost / CarriedBy in CoreConformanceCensus.fs: %A"
                      unclassified)

          testCase "the census names no family the pinned kit no longer ships"
          <| fun _ ->
              // The other direction, and it matters for the same reason: a row for a family that
              // was renamed or removed reads as coverage while covering nothing.
              let shipped = shippedFamilies () |> Set.ofList

              let stale =
                  census |> List.map fst |> List.filter (fun f -> not (Set.contains f shipped))

              Expect.isEmpty stale (sprintf "these census rows name no family the pinned kit ships: %A" stale)

          testCase "the census carries no duplicate row and no empty reason"
          <| fun _ ->
              let names = census |> List.map fst

              Expect.equal
                  (List.length (List.distinct names))
                  (List.length names)
                  "a family classified twice could be classified two ways"

              for family, adoption in census do
                  let reason =
                      match adoption with
                      | Adopted(test, port) -> test + port
                      | NotUsed mechanism -> mechanism
                      | SiblingHost host -> host
                      | CarriedBy phase -> phase

                  Expect.isTrue
                      (reason.Trim().Length > 6)
                      (sprintf
                          "%s carries no usable reason — the reason is what lets the next reader CHECK the classification rather than trust it"
                          family)

          testCase "the reflected roster agrees with the kit's own SampleAdequacy census"
          <| fun _ ->
              // Two independent enumerations of the same set: this file's reflection, and the kit's
              // own shipped `(family, adequacy)` declaration. They are built by different people for
              // different purposes, so a disagreement means one of them is describing a kit that is
              // not the pinned one — a finding, never something to paper over.
              let reflected = shippedFamilies () |> Set.ofList

              let kitDeclared = Fuaran.Core.SampleAdequacy.census |> List.map fst |> Set.ofList

              Expect.isEmpty
                  (Set.difference reflected kitDeclared |> Set.toList)
                  "families found by reflection that the kit's own SampleAdequacy census omits"

              Expect.isEmpty
                  (Set.difference kitDeclared reflected |> Set.toList)
                  "families the kit's own SampleAdequacy census names that reflection does not find"

          testCase "every Adopted row names a test that still exists, reached through a referenced entry point"
          <| fun _ ->
              // A row naming a retired test is the census failing quietly: it reads as coverage,
              // and nothing runs. Both halves are checked — the test name, and the kit entry point
              // the row claims reaches it (several families are reached through `certify` rather
              // than by their own name, and that claim is checkable too).
              let projects = conformanceTestProjects ()

              for family, adoption in census do
                  match adoption with
                  | Adopted(test, port) ->
                      let hosting =
                          projects
                          |> Array.filter (fun (_, sources) ->
                              sources |> Array.exists (fun (_, text) -> text.Contains test))

                      Expect.isNonEmpty
                          hosting
                          (sprintf
                              "%s is enrolled against a test named \"%s\", which no source in a conformance-referencing test project contains"
                              family
                              test)

                      let portEntries =
                          Regex.Matches(port, @"\b([A-Za-z_][\w']*)\.([a-z][\w']*)\b")
                          |> Seq.map (fun m -> m.Groups[1].Value + "." + m.Groups[2].Value)
                          |> Set.ofSeq

                      let reachedHere =
                          hosting
                          |> Array.exists (fun (_, sources) ->
                              sources
                              |> Array.exists (fun (_, text) ->
                                  Set.intersect (kitReferences text) portEntries |> Set.isEmpty |> not))

                      Expect.isTrue
                          reachedHere
                          (sprintf
                              "%s is enrolled as reached through %s, but no source in the project holding \"%s\" references that entry point"
                              family
                              port
                              test)
                  | _ -> ()

          testCase "a test that runs a Core law family has an Adopted row for it in the same project"
          <| fun _ ->
              // The second direction, done mechanically rather than by convention. Enrolling by
              // string alone is exactly the class Core's own narrowing tripped on 2026-09-03: the
              // declaration and the code it describes drift apart in silence. So the sources are
              // read, the kit aliases resolved, and every law family a test project actually
              // references must carry an `Adopted` row naming a test in THAT project.
              let familySet = shippedFamilies () |> Set.ofList

              let adoptedTests =
                  census
                  |> List.choose (fun (family, adoption) ->
                      match adoption with
                      | Adopted(test, _) -> Some(family, test)
                      | _ -> None)
                  |> Map.ofList

              for projectName, sources in conformanceTestProjects () do
                  let referenced =
                      sources
                      |> Array.map (fun (_, text) -> kitReferences text)
                      |> Array.fold Set.union Set.empty
                      |> Set.filter (fun r -> Set.contains r familySet)

                  for family in referenced do
                      match Map.tryFind family adoptedTests with
                      | None ->
                          failtestf
                              "%s runs the law family %s but the census does not classify it as Adopted — enrol it with the test name, or the census is describing a tier it has not read"
                              projectName
                              family
                      | Some test ->
                          Expect.isTrue
                              (sources |> Array.exists (fun (_, text) -> text.Contains test))
                              (sprintf
                                  "%s runs %s, but its Adopted row names the test \"%s\", which lives in a different project"
                                  projectName
                                  family
                                  test)

          testCase "the committed docs/core-conformance.md matches the rendered census"
          <| fun _ ->
              // The report is regenerated here and the committed copy asserted to have already
              // matched, so a stale doc is red and a fresh checkout that runs the suite recovers
              // it. Regenerating BEFORE asserting is deliberate: the repair for the red is already
              // on disk by the time the failure is read.
              let path = reportPath ()
              let rendered = render census

              let committed =
                  if File.Exists path then
                      Some(normaliseEol (File.ReadAllText path))
                  else
                      None

              Path.GetDirectoryName path
              |> Option.ofObj
              |> Option.iter (Directory.CreateDirectory >> ignore)

              File.WriteAllText(path, rendered)

              match committed with
              | None ->
                  failtestf "docs/core-conformance.md did not exist and has just been generated at %s — commit it" path
              | Some prior ->
                  Expect.equal
                      prior
                      (normaliseEol rendered)
                      "docs/core-conformance.md had drifted from the census; it has been regenerated in place — review and commit it" ]
