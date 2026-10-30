module Fuaran.UI.Tests.CoreAdoptionTests

// Validates `Fuaran.UI` against the `Fuaran.Core` conformance kit and re-expresses its
// op-stream over `Fuaran.Core.OpStream`. It builds the Core witnesses over Fuaran.UI's
// `Node` / `NodeId` / `TreeOp` / canonical-JSON codec, certifies the tree witness + op-stream
// end-to-end with the conformance kit, and proves the op-stream re-expresses over
// `Fuaran.Core.OpStream` with state parity to Fuaran.UI's own apply/replay (plus a portable
// JSONL round-trip that replays identically).
//
// Phase 379 — certification became CONSUMPTION for the structural five. The runtime apply
// engine (`Fuaran.UI.Ops.Apply`) no longer re-implements InsertChild / RemoveNode / MoveNode /
// ReorderChildren: it delegates them to `Fuaran.Core.Ops.applyContained` over exactly the
// witnesses certified here. The per-kind vertical (UpdateProp / ReplaceBinding / EditNode /
// UpdateStyle / UpdateState) and the whole-tree op (ReplaceRoot) remain domain-owned. The
// two closing tests below pin that consumption: the runtime structural apply agrees with a
// direct Core skeleton apply, and the Core-rejection → §4d ApplyError mapping stays at least
// as hint-rich as the retired hand-rolled walker.
//
// `Fuaran.UI`'s tree is HOMOGENEOUS at the protocol level: every node is a uniform
// `Node<'Msg> = { Id; Kind; State; Style; ... }` answering the same navigation
// (Id / kind tag / children / withChildren via `Ops.Introspect`), although `NodeKind` has many
// structurally-distinct cases (Layout containers vs Display / Input / Visualisation leaves). So
// it adopts the FULL Core tree witness through the unified `Conformance.certify`.
//
// One Fuaran.UI-specific wrinkle the kit surfaces: `Node<'Msg>` is NOT an F# equality type — it
// embeds message-handler closures in its spec records (`TabsSpec.OnSelect : int -> Action<'Msg>`,
// `StepperSpec.OnSelect`, `DisclosureSpec.OnToggle`, `LocalBinding.OnCommit`, ...), so the tree
// carries function-typed fields and cannot satisfy `: equality`. The conformance laws compare
// reconstructed trees with `=` (apply ∘ invert round-trips, replay parity), which needs node
// equality. We bridge entirely on the Fuaran.UI side with `EqNode`, a wrapper that compares two
// trees through their canonical wire encoding — two trees are equal iff they serialise
// identically, the same notion the wire-format round-trip corpus already uses. `Fuaran.Core`
// stays equality-agnostic; the comparison seam is a domain concern.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

module UiApply = Fuaran.UI.Ops.Apply
module Introspect = Fuaran.UI.Ops.Introspect
module JsonDecode = Fuaran.UI.Ops.JsonDecode
module CanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson
// Phase 409: the StreamEntry provenance envelope + its shipped encode / hashFn.
open Fuaran.UI.OpStream.Abstractions
module CoreStream = Fuaran.Core.OpStream
module CoreConf = Fuaran.Core.Conformance
module ComputeConf = Fuaran.Compute.Conformance
module CoreRng = Fuaran.Core.ConfRng
// Phase 1878 — the Cases-column registry; the runs below register into it at module init.
module LawCases = Fuaran.UI.Tests.LawCases

// ---- canonical-encoding equality wrapper (Fuaran.UI side) ----
// `Node<'Msg>` carries handler closures, so it is not an F# equality type; the conformance kit
// compares trees with `=`. We compare through the canonical wire encoding instead — the wrapper
// is the only place equality is defined, and it lives wholly on the Fuaran.UI side.

[<CustomEquality; NoComparison>]
type EqNode =
    { Node: Node<obj> }

    override this.Equals(o: obj) =
        match o with
        | :? EqNode as other -> CanonicalJson.encodeNode this.Node = CanonicalJson.encodeNode other.Node
        | _ -> false

    override this.GetHashCode() =
        (CanonicalJson.encodeNode this.Node).GetHashCode()

let private wrap (n: Node<obj>) : EqNode = { Node = n }
let private unwrap (e: EqNode) : Node<obj> = e.Node

// ---- the witnesses (Fuaran.UI types → Core contracts) ----

let private idw: Fuaran.Core.IdWitness<NodeId> =
    { ToString = fun (NodeId s) -> s
      OfString = NodeId
      Equals = (=) }

/// A coarse, top-level kind tag — stable under `ReplaceChildren` (a Stack stays a Layout), which
/// is all the conformance envelopes / addressing need.
let private kindTag (k: NodeKind<obj>) : string =
    // Phase 692 — the category is derived now, not a wrapper case; the six
    // structural kinds keep their case names, exactly as before.
    match k with
    | NodeKind.Custom _ -> "Custom"
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Mount _ -> "Mount"
    | k ->
        match Kind.category k with
        | NodeCategory.Layout -> "Layout"
        | NodeCategory.Display -> "Display"
        | NodeCategory.Input -> "Input"
        | NodeCategory.Visualisation -> "Visualisation"
        | NodeCategory.Structural -> "Structural"

let private nodew: Fuaran.Core.NodeWitness<EqNode, NodeId> =
    { Id = fun e -> NodeId e.Node.Id
      KindTag = fun e -> kindTag e.Node.Kind
      Children = fun e -> Introspect.getChildren e.Node.Kind |> Option.defaultValue [] |> List.map wrap
      ReplaceChildren =
        fun e cs ->
            match Introspect.withChildren e.Node.Kind (cs |> List.map unwrap) with
            | Some k -> wrap { e.Node with Kind = k }
            | None -> e }

/// A Layout (Stack / Card / ...) holds children; a Display leaf (Spacer / Heading / ...) does not.
/// `withChildren` is a no-op on a leaf, so `CanHold` routes the kit through `applyContained` —
/// an insert under a leaf is a typed `NotAContainer`, not a silent drop.
let private canHold (e: EqNode) =
    Introspect.getChildren e.Node.Kind |> Option.isSome

// ---- node builders (data-only Nodes so the canonical encoding is faithful) ----
// Stack containers + Spacer leaves carry no bindings / handlers, so they serialise cleanly and
// the witness navigation (getChildren / withChildren) round-trips them.

let private mkStack (id: string) (kids: EqNode list) : EqNode =
    wrap
        { Id = id
          Kind =
            NodeKind.Box(
                { Defaults.box with
                    Children = kids |> List.map unwrap }
            )
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          Fallback = None
          ExtraAttributes = Option.None
          Tooltip = None
          Visible = None }

let private mkSpacer (id: string) : EqNode =
    wrap
        { Id = id
          // Phase 459 — Spacer retired; a childless Markdown leaf serves the
          // same "leaf with no bindings/handlers" role this op-stream test needs.
          Kind = NodeKind.Markdown({ Text = TextSource.Literal "" })
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          Fallback = None
          ExtraAttributes = Option.None
          Tooltip = None
          Visible = None }

// ---- a mixed (leaf-bearing) tree generator + the container capability ----

let private genTree (rng: CoreRng.T) : EqNode * CoreRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let s = sprintf "n%d" counter
        counter <- counter + 1
        s

    let rec build depth =
        let id = freshId ()
        let leafRoll, r1 = CoreRng.intBelow 2 r
        r <- r1

        if depth <= 0 || leafRoll = 0 then
            mkSpacer id
        else
            let nKids, r2 = CoreRng.intBelow 3 r
            r <- r2
            mkStack id [ for _ in 1..nKids -> build (depth - 1) ]

    let rootId = freshId ()
    let nKids, r2 = CoreRng.intBelow 3 r
    r <- r2
    mkStack rootId [ for _ in 1..nKids -> build 1 ], r

let private genFresh (existing: Set<string>) (rng: CoreRng.T) : EqNode * CoreRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = CoreRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    mkSpacer (pick ()), r

let private opGen: Fuaran.Core.OpGen<EqNode, NodeId> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = Some canHold }

// ---- the op-stream re-expression ----

/// The two-seam StreamWitness: Fuaran.UI's `Ops.Apply.apply` reducer + its canonical-JSON op
/// codec. `Ops.JsonDecode.decodeOp` already returns `Result` (decoding to the obj-erased
/// `TreeOp<obj>` storage shape); its typed `DecodeError` maps onto Core's `string` error channel.
let private coreSw: Fuaran.Core.StreamWitness<TreeOp<obj>, EqNode, ApplyError> =
    { Apply = fun op e -> UiApply.apply op e.Node |> Result.map wrap
      Encode = CanonicalJson.encodeOp
      Decode = fun s -> JsonDecode.decodeOp s |> Result.mapError (sprintf "%A") }

// A base tree + an op generator mixing accepted ops with typed rejections (RemoveNode of an
// unknown id, duplicate-id InsertChild), to exercise reducer totality + the op-stream laws.
let private baseTree = mkStack "root" [ mkSpacer "s0"; mkSpacer "s1" ]

let private genStreamOp (rng: CoreRng.T) : TreeOp<obj> * CoreRng.T =
    let pick, r1 = CoreRng.intBelow 3 rng

    match pick with
    | 0 ->
        let v, r2 = CoreRng.next r1
        TreeOp.InsertChild(NodeId "root", unwrap (mkSpacer (sprintf "g%d" (v % 50)))), r2
    | 1 -> TreeOp.RemoveNode(NodeId "s0"), r1
    | _ -> TreeOp.RemoveNode(NodeId "ghost"), r1

let private streamGen: Fuaran.Core.StreamGen<TreeOp<obj>, EqNode> =
    { State0 = baseTree; Op = genStreamOp }

// Fuaran.UI's op-stream actor is now the typed `OpRecord.Actor` (Human/Agent), folded into the hash
// (Phase 320). These string tags are just the conformance-fixture labels, not the op-record actor.
let private uiOps: (string * TreeOp<obj>) list =
    [ "human:ajw", TreeOp.InsertChild(NodeId "root", unwrap (mkSpacer "s2"))
      "agent:claude", TreeOp.RemoveNode(NodeId "s0") ]

// ---------------------------------------------------------------------------
//  Phase 1878 — every adopting run below, registered through LawCases at module
//  initialisation (a top-level `let`, never inside a testCase closure — see LawCases.fs's
//  header for why order matters). The testCase bodies below assert on these SAME values
//  rather than recomputing them, so the census counts the very run each test asserted on.
// ---------------------------------------------------------------------------

let private certifyReport =
    CoreConf.certify nodew idw opGen coreSw streamGen CoreStream.defaultHash 20260619 200

let private certifyResults =
    LawCases.adopt
        [ "Conformance.witnessLaws"
          "Conformance.diffLaws"
          "Conformance.streamLaws"
          "Conformance.opAlgebra" ]
        200
        (fun _ -> certifyReport.Results)

let private containerResults = CoreConf.containerLaws nodew idw opGen 20260619 200

let private containerRun =
    LawCases.adopt [ "Conformance.containerLaws" ] 200 (fun _ -> containerResults)

// Phase 2304 — `TreeOpDiff.diff` lowers `Diff.toOpsContainedWith` under this container
// predicate, so the contained diff's family runs over this witness.
let private diffContainedResults =
    CoreConf.diffContainedLaws nodew idw opGen 20260619 200

// Phase 2306 — `Placement`'s verbs are Core's `TreePlacement.…Contained` under this container
// predicate, and its clone verbs rename through `FreshIds.repairDuplicates` under
// `FreshIds.derived`, so both families run over this witness.
let private placementResults = CoreConf.placementLaws nodew idw opGen 20260619 200

let private freshIdResults =
    CoreConf.freshIdLaws
        nodew
        idw
        opGen
        (fun (NodeId fresh) e -> wrap { e.Node with Id = fresh })
        (Fuaran.Core.FreshIds.derived idw)
        20260619
        200

let private chainBreakReasonResults = CoreConf.chainBreakReasonLaws 20260619 200

let private chainBreakReasonRun =
    LawCases.adopt [ "Conformance.chainBreakReasonLaws" ] 200 (fun _ -> chainBreakReasonResults)

let private dagBreakReasonResults = CoreConf.dagBreakReasonLaws 20260619 200

let private dagBreakReasonRun =
    LawCases.adopt [ "Conformance.dagBreakReasonLaws" ] 200 (fun _ -> dagBreakReasonResults)

/// The portable SHA-256, hoisted so both hashFn* runs below and the testCase asserting on them
/// share one function value.
let private sha256HashFn: Fuaran.Core.HashFn =
    fun prev payload -> Fuaran.Core.Hash.sha256Hex (prev + "|" + payload)

let private hashLawGen: Fuaran.Core.StreamGen<TreeOp<obj>, EqNode> =
    { State0 = baseTree
      Op =
        fun rng ->
            let v, r' = CoreRng.next rng
            TreeOp.InsertChild(NodeId "root", unwrap (mkSpacer (sprintf "h%d" v))), r' }

let private hashFnResults =
    CoreConf.hashFnLaws coreSw hashLawGen sha256HashFn 20260705 100

let private hashFnRun =
    LawCases.adopt [ "Conformance.hashFnLaws" ] 100 (fun _ -> hashFnResults)

let private hashFnAdversarialResults =
    CoreConf.hashFnAdversarialLaws sha256HashFn 500_000 4242

let private hashFnAdversarialRun =
    // The adversarial law's second argument is a search budget, not a case count in the
    // `SampleAdequacy` sense above — recorded at 1, so its cell reads a real, non-zero run rather
    // than a budget the `caseState` grammar was never built to describe.
    LawCases.adopt [ "Conformance.hashFnAdversarialLaws" ] 1 (fun _ -> hashFnAdversarialResults)

let private entryOf (op: TreeOp<obj>) : StreamEntry<obj> =
    { Op = op
      Timestamp = System.DateTimeOffset.FromUnixTimeSeconds 1_700_000_000L
      PromptId = None
      ResultEnvelope = OpResultEnvelope.Success }

let private entrySw: Fuaran.Core.StreamWitness<StreamEntry<obj>, EqNode, ApplyError> =
    { Apply = fun entry e -> UiApply.apply entry.Op e.Node |> Result.map wrap
      Encode = StreamEntry.encode
      // Core 0.34.0's `certifyStream` round-trips the chain through JSONL (fuaran-core Phase 301),
      // so the witness carries the tier's shipped decoder rather than a refusing stub.
      Decode = StreamEntry.decode (fun raw -> Fuaran.UI.Ops.JsonDecode.decodeOp raw |> Result.mapError (sprintf "%A")) }

let private entryStreamGen: Fuaran.Core.StreamGen<StreamEntry<obj>, EqNode> =
    { State0 = baseTree
      Op =
        fun rng ->
            let op, r' = genStreamOp rng
            entryOf op, r' }

let private certifyStreamReport =
    CoreConf.certifyStream entrySw entryStreamGen StreamEntry.hashFn 20260705 200

let private certifyStreamResults =
    LawCases.adopt [ "Conformance.reducer" ] 200 (fun _ -> certifyStreamReport.Results)

[<Tests>]
let tests =
    testList
        "Core adoption (Fuaran.UI)"
        [ testCase "the Fuaran.UI witness certifies end-to-end via the unified Conformance.certify"
          <| fun _ ->
              // Homogeneous-at-the-protocol-level, so the full bundle applies: witnessLaws +
              // opAlgebra (via CanHold) + streamLaws, through one entry point. Equality is supplied
              // by the canonical-encoding wrapper.
              if not certifyReport.AllPassed then
                  let msg =
                      certifyReport.Results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "Fuaran.UI witness failed Core certify:\n%s" msg

          testCase "the Fuaran.UI container predicate certifies under Core's containerLaws"
          <| fun _ ->
              // `canHold` is declared on this generator and is what `Ops.Apply.apply` routes
              // structural ops through (`applyContained`); Core 0.24.0 makes child-blindness the
              // DOMAIN's obligation, so it is certified here alongside the base run.
              //
              // The family's ADEQUACY guard is legitimately red for this witness, and that is
              // asserted rather than stepped around. Its third arm builds the offender it wants
              // refused by giving a fresh leaf a child through `ReplaceChildren`; this witness's
              // `withChildren` is a no-op on a leaf BY DESIGN (the comment beside `canHold` says so,
              // and `witnessLaws` admits it), so the malformed graft can never be constructed and
              // the arm is unreachable — by the witness's own shape, which is the stronger
              // guarantee. The two substantive laws are asserted by name; the adequacy law is
              // asserted red FOR THAT ARM and no other.
              let results = containerResults

              let byPrefix (prefix: string) =
                  match
                      results
                      |> List.filter (fun r -> r.Law.StartsWith(prefix, System.StringComparison.Ordinal))
                  with
                  | [ one ] -> one
                  | other -> failtestf "expected exactly one law starting '%s', found %d" prefix (List.length other)

              let childBlind = byPrefix "canHold is child-blind"
              let invariant = byPrefix "the contained apply preserves the container invariant"
              let adequacy = byPrefix "sample adequacy"

              Expect.isTrue childBlind.Passed (sprintf "child-blindness: %A" childBlind.Counterexample)
              Expect.isTrue invariant.Passed (sprintf "container invariant: %A" invariant.Counterexample)

              Expect.isFalse
                  adequacy.Passed
                  "the interior-graft arm has become reachable — the witness now lets a leaf carry children, so the third law is live and this test should assert it green instead"

              match adequacy.Counterexample with
              | Some text ->
                  Expect.stringContains
                      text
                      "never reached interior graft"
                      "the ONLY arm the guard may report unreached is the interior graft; any other unreached arm is a generator regression"
              | None -> failtest "a red adequacy law must say which arm it never reached"

          testCase "the Fuaran.UI witness certifies under Core's diffContainedLaws (TreeOpDiff's script)"
          <| fun _ ->
              // Phase 2304: `TreeOpDiff.diff` lowers `Diff.toOpsContainedWith` under this
              // container predicate, so the contained diff's reconstruction, applyability and
              // refusal laws are the tier's script's. The family's ADEQUACY guard is red for one
              // arm, by the witness's shape and for the reason `containerLaws`' is: its refused
              // pair needs an `after` that nests children under a leaf, and this witness's
              // `withChildren` cannot build one. The substantive laws are asserted green; the
              // guard is asserted red FOR THAT ARM and no other.
              let isAdequacy (r: Fuaran.Core.LawResult) =
                  r.Law.StartsWith("sample adequacy", System.StringComparison.Ordinal)

              let substantive = diffContainedResults |> List.filter (isAdequacy >> not)
              Expect.isGreaterThan (List.length substantive) 2 "the family's substantive laws ran"

              for r in substantive do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

              // Every other adequacy arm of the family is reached and green.
              match diffContainedResults |> List.filter (fun r -> isAdequacy r && not r.Passed) with
              | [] ->
                  failtest
                      "the refused-pair arm has become reachable — the witness now lets a leaf carry children, so assert the refusal law green instead"
              | [ adequacy ] ->
                  match adequacy.Counterexample with
                  | Some text ->
                      Expect.stringContains
                          text
                          "never reached refused"
                          "the ONLY arm the guard may report unreached is the refused pair"
                  | None -> failtest "a red adequacy law must say which arm it never reached"
              | other ->
                  failtestf
                      "expected one red adequacy arm, found %d: %A"
                      (List.length other)
                      (other |> List.map _.Counterexample)

          testCase "the Fuaran.UI witness certifies under Core's placementLaws (Placement's verbs)"
          <| fun _ ->
              // Phase 2306: `Placement.placeOp` / `moveOp` / `nudgeOp` lower through
              // `TreePlacement.placeContained` / `moveContained` under this container predicate, so
              // the landing, bare-op and refusal-by-name laws are the tier's verbs' laws.
              Expect.isGreaterThan (List.length placementResults) 2 "the family's laws ran"

              for r in placementResults do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "the Fuaran.UI witness certifies under Core's freshIdLaws (the clone verbs' renaming)"
          <| fun _ ->
              // Phase 2306: `Placement.duplicateOp` / `pasteOp` rename through
              // `FreshIds.repairDuplicates` under `FreshIds.derived`, the strategy certified here.
              Expect.isGreaterThan (List.length freshIdResults) 2 "the family's laws ran"

              for r in freshIdResults do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "the kit's classified chain walkers never mint Unrecognised (chainBreakReasonLaws)"
          <| fun _ ->
              // Self-contained in the pinned kit. Enrolled because this tier is the CONSUMER of the
              // classification: `HashChain.ofChainBreak` projects `ChainBreak.Reason` onto
              // `VerificationError`, and its `Unrecognised` arm is only honest if the walkers this
              // tier runs (`firstChainBreakWith` / `firstCaptureBreak`) mint the three named cases
              // for every break they can produce — which is exactly what the family certifies.
              chainBreakReasonResults
              |> List.filter (fun r -> not r.Passed)
              |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
              |> function
                  | [] -> ()
                  | failures -> failtestf "chainBreakReasonLaws at this pin:\n%s" (String.concat "\n" failures)

          testCase "the kit's classified DAG walker never mints Unrecognised (dagBreakReasonLaws)"
          <| fun _ ->
              // The DAG twin, for the same reason: `Fuaran.UI.OpStream.Dag.*` verifies through
              // `Dag.firstBreak` and reports its `DagBreakReason`.
              dagBreakReasonResults
              |> List.filter (fun r -> not r.Passed)
              |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
              |> function
                  | [] -> ()
                  | failures -> failtestf "dagBreakReasonLaws at this pin:\n%s" (String.concat "\n" failures)

          testCase "Core skeleton ops + invert operate over a concrete Fuaran.UI tree"
          <| fun _ ->
              let tree = mkStack "root" [ mkStack "a" [] ]
              let op = Fuaran.Core.SkeletonOp.InsertChild(NodeId "root", mkStack "b" [])

              match Fuaran.Core.Ops.apply Fuaran.Core.Policy.none (Fuaran.Core.OpScope.plain nodew idw) op tree with
              | Ok post ->
                  Expect.equal
                      (nodew.Children post |> List.map (fun e -> e.Node.Id))
                      [ "a"; "b" ]
                      "child inserted via Core op"

                  match Fuaran.Core.Ops.invert nodew idw op tree with
                  | Ok inv ->
                      match
                          Fuaran.Core.Ops.apply Fuaran.Core.Policy.none (Fuaran.Core.OpScope.plain nodew idw) inv post
                      with
                      | Ok restored -> Expect.equal restored tree "Core.invert undoes the insert on the Fuaran.UI tree"
                      | Error e -> failtestf "invert-apply failed: %A" e
                  | Error e -> failtestf "invert failed: %A" e
              | Error e -> failtestf "apply failed: %A" e

          testCase "the op-stream re-expresses over Core.OpStream with state parity to Fuaran.UI apply/replay"
          <| fun _ ->
              // Fuaran.UI's replay semantics = a fold of its `Ops.Apply.apply` over the ops.
              let uiReplay =
                  (Ok baseTree, uiOps)
                  ||> List.fold (fun acc (_, op) ->
                      acc |> Result.bind (fun e -> UiApply.apply op e.Node |> Result.map wrap))

              // the same ops over Core.OpStream (Fuaran.UI's actor is natively a string)
              let coreResult =
                  (Ok(baseTree, CoreStream.empty), uiOps)
                  ||> List.fold (fun acc (actor, op) ->
                      acc
                      |> Result.bind (fun (st, recs) ->
                          CoreStream.append
                              (CoreStream.canonical CoreStream.defaultHash coreSw)
                              (Fuaran.Core.Actor.ofLegacyString actor)
                              op
                              st
                              recs))

              match coreResult, uiReplay with
              | Ok(coreState, coreRecs), Ok uiState ->
                  Expect.equal coreState uiState "Core.OpStream state == Fuaran.UI apply/replay state"

                  Expect.isTrue
                      (CoreStream.verifyChain (CoreStream.canonical CoreStream.defaultHash coreSw) coreRecs)
                      "Core chain verifies"

                  // the portable win: Core's fromJsonl is FSharp.Core-only (runs in-browser) and
                  // Result-typed; a round-trip replays identically.
                  match Fuaran.Core.Jsonl.write coreSw coreRecs |> Fuaran.Core.Jsonl.read coreSw with
                  | Ok restored ->
                      match CoreStream.replay coreSw baseTree restored with
                      | Ok rs -> Expect.equal rs coreState "portable JSONL round-trip replays identically"
                      | Error e -> failtestf "round-trip replay failed: %A" e
                  | Error e -> failtestf "fromJsonl failed: %s" (Fuaran.Core.StreamLoadFault.toString e)
              | Ok _, Error e -> failtestf "Fuaran.UI replay failed: %A" e
              | Error e, _ -> failtestf "Core append failed: %A" e

          testCase "the runtime structural apply consumes Core.Ops (agrees with a direct skeleton apply)"
          <| fun _ ->
              // Phase 379: `Apply.apply` delegates the structural five to Core, so a runtime
              // InsertChild must produce exactly the tree a direct `Fuaran.Core.Ops.apply` over
              // the certified witness produces (compared through the canonical encoding, the
              // domain's equality notion).
              let viaRuntime =
                  UiApply.apply (TreeOp.InsertChild(NodeId "root", unwrap (mkSpacer "s2"))) (unwrap baseTree)

              let viaCore =
                  Fuaran.Core.Ops.apply
                      Fuaran.Core.Policy.none
                      (Fuaran.Core.OpScope.plain nodew idw)
                      (Fuaran.Core.SkeletonOp.InsertChild(NodeId "root", mkSpacer "s2"))
                      baseTree

              match viaRuntime, viaCore with
              | Ok u, Ok c ->
                  Expect.equal
                      (CanonicalJson.encodeNode u)
                      (CanonicalJson.encodeNode c.Node)
                      "runtime structural apply == direct Core skeleton apply"
              | _ -> failtestf "structural apply disagreed: runtime=%A core=%A" viaRuntime viaCore

          testCase "the Core-rejection mapping stays hint-rich (no poorer than the retired walker)"
          <| fun _ ->
              // The §4d recovery hints the hand-rolled walker produced must survive the
              // Rejection → ApplyError translation: a childless-parent insert still names the
              // kind, a bad reorder still enumerates the expected child ids.
              match UiApply.apply (TreeOp.InsertChild(NodeId "s0", unwrap (mkSpacer "x"))) (unwrap baseTree) with
              | Error e ->
                  Expect.equal e.Code ApplyErrorCode.ChildlessKind "leaf insert → ChildlessKind"
                  Expect.isSome e.Hint.NodeKind "ChildlessKind hint still names the target kind"
              | Ok _ -> failtest "expected ChildlessKind for an insert under a leaf"

              match UiApply.apply (TreeOp.ReorderChildren(NodeId "root", [ NodeId "s0" ])) (unwrap baseTree) with
              | Error e ->
                  Expect.equal e.Code ApplyErrorCode.OrderingMismatch "non-permutation reorder → OrderingMismatch"

                  Expect.isNonEmpty
                      e.Hint.AvailableFields
                      "OrderingMismatch hint still enumerates the expected child ids"
              | Ok _ -> failtest "expected OrderingMismatch for a non-permutation reorder"

          testCase "the portable SHA-256 certifies under Core's hashFnLaws (the supply-your-own-crypto contract)"
          <| fun _ ->
              // Phase 405: UI's hash-chain digest is the pure Fable-safe SHA-256, supplied
              // host-side per Core GP3 (fuaran-core#65). Run Core's certified HashFn law kit
              // over it — determinism, pre-image parity, tamper-detection — plus the
              // adversarial branch (a collision-resistant fn yields no in-budget forgery).
              let assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
                  let failures = results |> List.filter (fun r -> not r.Passed)

                  if not (List.isEmpty failures) then
                      failures
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"
                      |> failtestf "%s failed:\n%s" context

              hashFnResults |> assertAllPassed "hashFnLaws over the portable SHA-256"

              // budget + seed match Core's own invocation (500_000, 4242): the "FNV-1a admits a
              // forgery" branch needs the 32-bit birthday bound to realise a collision, AND the
              // law's forge pre-images are seed-shaped — sequential fixed-length 8-digit strings
              // (e.g. seed 20260705) happen to be FNV-collision-free in-budget, while 4242 spans
              // 4→6-digit lengths where collisions realise. The FNV branch is crypto-fn-independent,
              // so reusing Core's proven seed keeps this a test of OUR SHA-256's resist branch.
              hashFnAdversarialResults
              |> assertAllPassed "hashFnAdversarialLaws over the portable SHA-256"

          testCase "the Phase-406 StreamEntry witness certifies via Conformance.certifyStream (consumption)"
          <| fun _ ->
              // Phase 409: the op-stream re-expression is CERTIFIED, not just re-hashed. The
              // domain provenance envelope (`StreamEntry`) is the opaque `'Op` a Core StreamWitness
              // carries — Apply unwraps it to `Fuaran.UI.Ops.Apply.apply`, Encode is the shipped
              // `StreamEntry.encode`, and the chain HashFn is the shipped host-side SHA-256. Running
              // `certifyStream` (reducer laws + op-stream laws: append/verify/replay over the hashFn)
              // proves UI's op-stream runs on the certified Core spine, upgrading the stream layer
              // from pilot re-expression to certified consumption.
              if not certifyStreamReport.AllPassed then
                  certifyStreamReport.Results
                  |> List.filter (fun r -> not r.Passed)
                  |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                  |> String.concat "\n"
                  |> failtestf "StreamEntry witness failed certifyStream:\n%s" ]

// ============================================================================
//  Phase 1481 — the columnar law families, run in this tier's suite.
//
//  All FOUR are SELF-CONTAINED `(seed, iterations)` entry points in the pinned
//  0.18.0 kit: `columnarOpLaws`, `columnarValidatorLaws`, `aggregateParityLaws`
//  and `schemaWalkLaws` each build their own Core tables and pipelines and take
//  no consumer witness, so none of them can be instantiated "over the tier's
//  own X" in the literal sense. Running them here is real evidence — that the
//  PINNED kit's contract holds on this machine at this pin — and it is evidence
//  about the pin, not about the tier. That is what the census rows record, and
//  the port names each family itself so the row cannot overstate its reach.
//
//  Beside each, where the tier has a genuine surface the family's property
//  should hold over, a SEPARATE tier-shaped test asserts that property
//  DIRECTLY, using Core's public `Column` / `DataFrame` / `SchemaWalk` surfaces
//  rather than a wrapper pretending the law took the tier's input:
//
//   * `columnarOpLaws` — the tier's one production column authoring (typed builders)
//     surface is `RetrievalSource.Hit.toTable`. The op algebra the family
//     certifies (`Fuaran.Core.Column.Ops`) has NO call site in this tier at
//     all — nothing here edits a table through a table-edit op — so what is
//     asserted is the precondition that algebra rests on and this tier can
//     actually break: the columns that surface builds are well-formed against
//     the schema it declares.
//   * `aggregateParityLaws` — Core's aggregate entry point likewise has no
//     direct call site here, but the tier DOES reach Core's aggregation through
//     `QueryRefine.refineLocally` -> `DataFrame.evalPipeline`. So the parity
//     property is asserted over a column the TIER authored rather than one Core
//     generated. Only the parity half has a tier instance: `Hit.toTable` is
//     total and emits no `Null`, so the law's null-skip half has no shape here.
//   * `columnarValidatorLaws` — `Fuaran.UI.Validator` ships no columnar rule
//     (its grid checks are CSS `grid-template-columns` and AST row types), and
//     nothing in this repo registers a Core columnar validator. The tier's real
//     columnar-validation rule is FUARAN114 (Phase 1149, widened by Phase 1486)
//     in `PreEmitValidate.fs`: it grounds a grid's column `field` /
//     `rowKeyField` against the schema the grid's source PRODUCES. The same two properties
//     the law certifies for Core's validator — determinism, and a defect count
//     equal to the injected-fault count — are asserted over that rule.
//   * `schemaWalkLaws` — the tier calls the schema walk since Phase 1486: the
//     rule above derived its column set by hand from an EMPTY pipeline until the
//     pin carried the walk, and now folds the whole pipeline through
//     `SchemaWalk`. What is asserted here is the AGREEMENT that widening rests
//     on, which no law about Core alone can state — the walk against the rule's
//     own window, and the walk against the schema the tier's OWN refinement
//     evaluator (`QueryRefine.refineLocally`) produces for pipelines the rule now
//     judges. Written before the widening landed, so two of its assertions were
//     go-red proofs for it and read as the widened claim now.
// ============================================================================

module Columnar =

    open Fuaran.Core

    open Fuaran.Compute

    /// One seed for every family and every tier-shaped test below, so a failure
    /// anywhere reproduces from a single number.
    let private lawSeed = 20260904

    let private assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
        let failures = results |> List.filter (fun r -> not r.Passed)

        if not (List.isEmpty failures) then
            failures
            |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
            |> String.concat "\n"
            |> failtestf "%s failed:\n%s" context

    // -----------------------------------------------------------------------
    //  the tier's own columnar surfaces
    // -----------------------------------------------------------------------

    /// A seed-replayable sample of retrieval hits — the input to the tier's one
    /// production column authoring surface (typed builders).
    let private genHits (n: int) (rng: CoreRng.T) : Fuaran.UI.RetrievalSource.Hit list * CoreRng.T =
        let mutable r = rng

        let mkHit (i: int) (score: float) : Fuaran.UI.RetrievalSource.Hit =
            { Score = score
              Title = sprintf "t%d" i
              Snippet = sprintf "s%d" i
              SourceId = sprintf "doc-%d" (i % 3)
              Date = sprintf "2026-01-%02d" ((i % 28) + 1) }

        let hits =
            [ for i in 1..n ->
                  let s, r1 = CoreRng.intBelow 1000 r
                  r <- r1
                  mkHit i (float s * 0.001) ]

        hits, r

    /// The three-column table the grid rule and the schema walk are exercised
    /// over. Deliberately wider than the one-column fixture the shipped
    /// FUARAN114 tests use, so a pipeline can drop columns as well as add them.
    let private deptTable: Table =
        { Schema =
            [ Field.create "dept" StringType
              Field.create "headcount" IntType
              Field.create "spend" FloatType ]
          Columns =
            [ Column.ofStrs "dept" (Vector.ofList [ "eng"; "ops"; "eng" ]) AllValid
              Column.ofInts "headcount" (Vector.ofList [ 12; 3; 7 ]) AllValid
              Column.ofFloats "spend" (Vector.ofList [ 100.0; 25.5; 60.25 ]) AllValid ] }

    let private deptNames = deptTable.Schema |> List.map _.Name

    /// A `DataGrid` naming `columnFields` as its column fields and `rowKeyField`
    /// as its row key, reading `source`. The same shape the shipped FUARAN114
    /// tests build; repeated here rather than shared because those fixtures are
    /// private to their own file and a test project cannot reference another.
    let private gridNamingFields
        (columnFields: string list)
        (rowKeyField: string option)
        (source: Fuaran.UI.Types.Binding<Fuaran.UI.Types.Row seq>)
        : Fuaran.UI.Types.Node<obj> =
        { Id = "grid"
          Kind =
            Fuaran.UI.Types.NodeKind.DataGrid(
                { Defaults.dataGrid with
                    Source = source
                    RowKeyField = rowKeyField
                    Columns =
                        columnFields
                        |> List.map (fun f ->
                            { Defaults.columnErased with
                                Label = f
                                Field = Some f
                                Format = Fuaran.UI.Types.CellFormat.None
                                Kind = Fuaran.UI.Types.CellKindErased.Text
                                Width = Fuaran.UI.Types.ColumnWidth.Auto }) }
            )
          State = None
          Style = None
          Accessibility = None
          Motion = Fuaran.UI.Defaults.Motion.none
          Fallback = None
          ExtraAttributes = None
          Tooltip = None
          Visible = None }

    let private dashboardOf (children: Fuaran.UI.Types.Node<obj> list) =
        Fuaran.UI.Fuaran.dashboard
            "root"
            { Fuaran.UI.Defaults.dashboard<obj> with
                Children = children }

    /// The grid-field defects a validation of `node` reports, as
    /// `(nodeId, field)` pairs. Every other defect class is dropped: this is a
    /// test about ONE rule, and a second rule firing must not read as a
    /// grounding failure.
    let private ungroundedOf (node: Fuaran.UI.Types.Node<obj>) =
        match Fuaran.UI.PreEmitValidate.validate node with
        | Ok() -> []
        | Error defects ->
            defects
            |> List.choose (fun d ->
                match d with
                | Fuaran.UI.PreEmitValidate.PreEmitDefect.GridFieldUngrounded(id, field, _) -> Some(id, field)
                | _ -> None)

    /// A grid reading `pipeline` over `deptTable`, embedded.
    let private gridOver (pipeline: Transform list) (columnFields: string list) (rowKeyField: string option) =
        gridNamingFields
            columnFields
            rowKeyField
            (Fuaran.UI.Types.Binding.Transform(Fuaran.UI.Types.TransformSource.Data(Embedded deptTable), pipeline, None))

    /// Pipelines the tier's own refinement evaluator accepts over `deptTable`.
    /// Chosen to reach the three schema-shaping directions the walk must model:
    /// unchanged, appending, and closing.
    let private refinementMenu: (string * Transform list) list =
        [ "sort", [ Sort [ Fuaran.Compute.Slot.Lit "headcount", Asc ] ]
          "filter", [ Filter(Binary(Gt, Col "headcount", Lit(Int 4))) ]
          "derive", [ Derive("total", Binary(Add, Col "headcount", Lit(Int 1))) ]
          "project", [ Project [ "dept", "dept"; "spend", "cost" ] ]
          "groupBy",
          [ GroupBy(
                [ "dept" ],
                [ { Name = "heads"
                    Fn = Sum
                    Of = "headcount" } ]
            ) ]
          "derive+project",
          [ Derive("total", Binary(Add, Col "headcount", Lit(Int 1)))
            Project [ "dept", "dept"; "total", "total" ] ] ]

    // -----------------------------------------------------------------------
    //  Phase 1878 — the six self-contained runs below, registered through LawCases at module
    //  initialisation for the same reason the outer module's block is (LawCases.fs's header).
    // -----------------------------------------------------------------------

    let private columnarOpResults = ComputeConf.columnarOpLaws lawSeed 100

    let private columnarOpRun =
        LawCases.adopt [ "Conformance.columnarOpLaws" ] 100 (fun _ -> columnarOpResults)

    let private columnarOpWithResults =
        ComputeConf.columnarOpLawsWith ColumnOps.invert ComputeConf.columnarOpStreamGen lawSeed 100

    let private columnarOpWithRun =
        LawCases.adopt [ "Conformance.columnarOpLawsWith" ] 100 (fun _ -> columnarOpWithResults)

    let private columnarValidatorResults = CoreConf.columnarValidatorLaws lawSeed 100

    let private columnarValidatorRun =
        LawCases.adopt [ "Conformance.columnarValidatorLaws" ] 100 (fun _ -> columnarValidatorResults)

    let private aggregateParityResults = ComputeConf.aggregateParityLaws lawSeed 100

    let private aggregateParityRun =
        LawCases.adopt [ "Conformance.aggregateParityLaws" ] 100 (fun _ -> aggregateParityResults)

    let private aggregateNullSkipResults = CoreConf.aggregateNullSkipLaws lawSeed 100

    let private aggregateNullSkipRun =
        LawCases.adopt [ "Conformance.aggregateNullSkipLaws" ] 100 (fun _ -> aggregateNullSkipResults)

    let private schemaWalkResults = ComputeConf.schemaWalkLaws lawSeed 100

    let private schemaWalkRun =
        LawCases.adopt [ "Conformance.schemaWalkLaws" ] 100 (fun _ -> schemaWalkResults)

    // -----------------------------------------------------------------------
    //  the tests
    // -----------------------------------------------------------------------

    [<Tests>]
    let tests =
        testList
            "Core columnar laws (Fuaran.UI)"
            [

              // ---- self-contained: the pinned kit's own contract -----------

              testCase "the columnar op algebra certifies under Core's columnarOpLaws"
              <| fun _ ->
                  // The tier edits no table through a table-edit op — that
                  // algebra has no call site in this repo — so this is evidence
                  // about the PIN, recorded as such in the census row's port.
                  columnarOpResults
                  |> assertAllPassed "columnarOpLaws over the pinned columnar op algebra"

              testCase
                  "the columnar op algebra certifies under Core's columnarOpLawsWith at the kit's reference generator"
              <| fun _ ->
                  // Core 0.32.0 retyped the `With` form to take the domain's
                  // `StreamGen<ColumnOp, Table>`, and `columnarOpLaws` no longer
                  // delegates to it (the two share a runner), so the family is run
                  // by its own name here. The tier has no table-edit op to draw a
                  // generator of its own from, so this passes Core's shipped
                  // `invert` and the kit's reference generator — Core's own
                  // migration line — and is evidence about the PIN, as the census
                  // row's port says.
                  columnarOpWithResults
                  |> assertAllPassed "columnarOpLawsWith over the kit's reference columnar generator"

              testCase "the columnar validator certifies under Core's columnarValidatorLaws"
              <| fun _ ->
                  // Likewise: nothing here registers a Core columnar validator.
                  // The tier's own columnar-grounding rule is asserted below.
                  columnarValidatorResults
                  |> assertAllPassed "columnarValidatorLaws over the pinned columnar validator"

              testCase "aggregate parity certifies under Core's aggregateParityLaws"
              <| fun _ ->
                  aggregateParityResults
                  |> assertAllPassed "aggregateParityLaws over the pinned aggregate surface"

              testCase "aggregate null-skipping certifies under Core's aggregateNullSkipLaws"
              <| fun _ ->
                  // Core 0.32.0 split `aggregateParityLaws` in two: the GroupBy
                  // parity half moved with the dataframe families, and the
                  // `Column.aggregate` null-skip half stayed in the kit under this
                  // name. Adopted beside the parity half so the pair still runs
                  // both laws the one family ran at 0.31.0.
                  aggregateNullSkipResults
                  |> assertAllPassed "aggregateNullSkipLaws over the pinned aggregate surface"

              testCase "static output-schema derivation certifies under Core's schemaWalkLaws"
              <| fun _ ->
                  schemaWalkResults
                  |> assertAllPassed "schemaWalkLaws over the pinned schema walk"

              // ---- tier-shaped: the same properties, over the tier ---------

              testCase "the tier's Column authoring surface builds columns well-formed against its own schema"
              <| fun _ ->
                  // The precondition every columnar op and every aggregate
                  // rests on, and the one this tier can actually break:
                  // `Hit.toTable` writes five columns by hand, so a reordered
                  // or mistyped cell list is a silent corruption of everything
                  // downstream. `Table.validate` is Core's own well-formedness
                  // check (equal lengths, schema/column agreement); the cell
                  // identity below is what it cannot see.
                  let mutable rng = CoreRng.ofSeed lawSeed

                  for i in 0..49 do
                      let n, r1 = CoreRng.intBelow 6 rng
                      let hits, r2 = genHits (n + 1) r1
                      rng <- r2

                      let table =
                          match Fuaran.UI.RetrievalSource.Hit.toTable hits with
                          | Ok t -> t
                          | Error e -> failtestf "iteration %d: the generated hits were refused: %A" i e

                      Expect.equal
                          (Table.validate table)
                          (Ok())
                          (sprintf "iteration %d: the authored table is well-formed" i)

                      Expect.equal
                          table.Schema
                          Fuaran.UI.RetrievalSource.hitSchema
                          (sprintf "iteration %d: the authored table declares the canonical hit schema" i)

                      for j in 0 .. List.length hits - 1 do
                          let hit = List.item j hits

                          let cellAt name =
                              match Table.tryColumn name table with
                              | Some c -> Column.cell j c
                              | None -> failtestf "iteration %d: the authored table has no column '%s'" i name

                          Expect.equal
                              (cellAt "score")
                              (Float hit.Score)
                              (sprintf "iteration %d row %d: the score cell addresses its own hit" i j)

                          Expect.equal
                              (cellAt "title")
                              (Str hit.Title)
                              (sprintf "iteration %d row %d: the title cell addresses its own hit" i j)

                          Expect.equal
                              (cellAt "sourceId")
                              (Str hit.SourceId)
                              (sprintf "iteration %d row %d: the sourceId cell addresses its own hit" i j)

                          Expect.equal
                              (cellAt "date")
                              (Date hit.Date)
                              (sprintf "iteration %d row %d: the date cell addresses its own hit" i j)

              testCase "aggregating a tier-authored column equals the single-group GroupBy"
              <| fun _ ->
                  // `aggregateParityLaws`' parity half, over a column the TIER
                  // built rather than one Core generated. The law's other half
                  // (null-skip) has no tier instance: `Hit.toTable` is total and
                  // emits no `Null`, so there is no fault to skip.
                  let mutable rng = CoreRng.ofSeed lawSeed

                  let fns = [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ]

                  for i in 0..19 do
                      let n, r1 = CoreRng.intBelow 6 rng
                      let hits, r2 = genHits (n + 1) r1
                      rng <- r2

                      let table =
                          match Fuaran.UI.RetrievalSource.Hit.toTable hits with
                          | Ok t -> t
                          | Error e -> failtestf "iteration %d: the generated hits were refused: %A" i e

                      let scoreCol =
                          match Table.tryColumn "score" table with
                          | Some c -> c
                          | None -> failtest "the authored table has no score column"

                      for fn in fns do
                          let direct = Column.aggregate fn scoreCol |> Result.mapError (fun _ -> "aggErr")

                          let viaGroup =
                              match
                                  DataFrame.evalPipeline
                                      [ GroupBy([], [ { Name = "a"; Fn = fn; Of = "score" } ]) ]
                                      table
                              with
                              | Ok t ->
                                  match Table.tryColumn "a" t with
                                  | Some ac -> Ok(Column.cell 0 ac)
                                  | None -> Error "no agg column"
                              | Error _ -> Error "aggErr"

                          Expect.equal
                              direct
                              viaGroup
                              (sprintf
                                  "iteration %d fn=%A: aggregating the tier's own column = single-group GroupBy"
                                  i
                                  fn)

              testCase "the tier's grid-field rule is deterministic and reports exactly the ungrounded names"
              <| fun _ ->
                  // The two properties `columnarValidatorLaws` certifies for
                  // Core's columnar validator, asserted over the tier's own
                  // columnar-validation rule (FUARAN114, Phase 1149): the
                  // verdict is stable on a re-run of the same tree, and the
                  // defect count equals the injected-fault count — here, the
                  // number of named fields the source's schema cannot produce.
                  let mutable rng = CoreRng.ofSeed lawSeed
                  let ghosts = [ "headcnt"; "id"; "cost"; "region" ]
                  let mutable groundedSeen = 0
                  let mutable ungroundedSeen = 0

                  for i in 0..49 do
                      let nCols, r1 = CoreRng.intBelow 4 rng
                      let mutable r = r1

                      let draw () =
                          let useGhost, ra = CoreRng.intBelow 3 r
                          r <- ra

                          if useGhost = 0 then
                              let k, rb = CoreRng.intBelow (List.length ghosts) r
                              r <- rb
                              List.item k ghosts
                          else
                              let k, rb = CoreRng.intBelow (List.length deptNames) r
                              r <- rb
                              List.item k deptNames

                      let fields = [ for _ in 0..nCols -> draw () ]
                      let rowKey = draw ()
                      rng <- r

                      let node = gridOver [] fields (Some rowKey)
                      let first = ungroundedOf node
                      let second = ungroundedOf node

                      Expect.equal first second (sprintf "iteration %d: the grid-field rule is deterministic" i)

                      // Soundness: one defect per NAMED field the schema cannot
                      // produce — per column entry rather than per distinct
                      // name, because the author repairs each of them, and the
                      // rowKeyField counted alongside.
                      let expected =
                          (fields @ [ rowKey ])
                          |> List.filter (fun f -> not (List.contains f deptNames))
                          |> List.length

                      Expect.equal
                          (List.length first)
                          expected
                          (sprintf "iteration %d: one defect per ungrounded name (fields=%A key=%s)" i fields rowKey)

                      if expected = 0 then
                          groundedSeen <- groundedSeen + 1
                      else
                          ungroundedSeen <- ungroundedSeen + 1

                  // A parity law whose sample never produced one of the two
                  // verdicts is a green that proves half of what it says — the
                  // guard `schemaWalkLaws` carries, applied to this sample.
                  Expect.isGreaterThan groundedSeen 0 "the sample reached a fully-grounded grid"
                  Expect.isGreaterThan ungroundedSeen 0 "the sample reached an ungrounded grid"

              testCase "the schema walk agrees with the grid rule's window and with the tier's refinement evaluator"
              <| fun _ ->
                  // What the widening recorded in FUARAN114's own doc comment
                  // would rest on, asserted rather than assumed. Two halves:
                  //
                  //  1. Over the window the rule DOES cover — an `Embedded`
                  //     table with an EMPTY pipeline — the walk is closed over
                  //     exactly the schema the rule reads by hand, and its
                  //     membership test agrees name-for-name with the rule's
                  //     accept / refuse.
                  //  2. Over the pipelines the rule stands DOWN on, the walk's
                  //     answer agrees with the schema the tier's own refinement
                  //     evaluator (`QueryRefine.refineLocally`) actually
                  //     produces — the schema that path re-types a dashboard
                  //     against, so a walk that disagreed would mistype it.
                  let emptyWalk = SchemaWalk.ofPipeline deptTable.Schema []

                  Expect.isTrue (SchemaWalk.isClosed emptyWalk) "an empty pipeline closes the schema"

                  Expect.equal
                      (SchemaWalk.names emptyWalk)
                      deptNames
                      "the walk's column set is the one the grid rule reads by hand"

                  for field in deptNames @ [ "headcnt"; "id" ] do
                      let refused =
                          gridOver [] [ field ] (Some "dept")
                          |> ungroundedOf
                          |> List.exists (fun (_, f) -> f = field)

                      Expect.equal
                          (not refused)
                          (SchemaWalk.has field emptyWalk)
                          (sprintf "the grid rule and the walk's membership test agree about '%s'" field)

                  let mutable closedNonEmpty = 0

                  for name, pipeline in refinementMenu do
                      match Fuaran.UI.QueryRefine.refineLocally deptTable pipeline (dashboardOf []) with
                      | Error e -> failtestf "%s: the tier's refinement evaluator rejected the pipeline (%A)" name e
                      | Ok(refined, _) ->
                          let derived = SchemaWalk.ofPipeline deptTable.Schema pipeline
                          let actual = refined.Schema |> List.map _.Name

                          if SchemaWalk.isClosed derived then
                              closedNonEmpty <- closedNonEmpty + 1

                              Expect.equal
                                  (SchemaWalk.names derived)
                                  actual
                                  (sprintf "%s: a closed walk names the evaluated schema exactly, in order" name)
                          else
                              for n in SchemaWalk.names derived do
                                  Expect.isTrue
                                      (List.contains n actual)
                                      (sprintf "%s: an open walk claims no column the result lacks ('%s')" name n)

                          // And the widening itself, as a live fact rather than a
                          // comment (Phase 1486). These two assertions were
                          // written to go RED when the rule took the walk, and
                          // they did: where the walk CLOSES, FUARAN114 now
                          // refuses a field the pipeline cannot produce; where it
                          // stays open the rule still stands down, because an
                          // ignorance is not an absence.
                          let ungrounded =
                              gridOver pipeline [ "no-such-column" ] (Some "dept") |> ungroundedOf

                          if SchemaWalk.isClosed derived then
                              Expect.equal
                                  ungrounded
                                  [ "grid", "no-such-column" ]
                                  (sprintf "%s: FUARAN114 refuses the unproducible field under a closed walk" name)
                          else
                              Expect.isEmpty ungrounded (sprintf "%s: FUARAN114 stands down under an open walk" name)

                  Expect.isGreaterThan
                      closedNonEmpty
                      0
                      "at least one non-empty pipeline has a closed walk — the widening's whole subject" ]

// ─── Phase 1857 — id uniqueness across the named node slots ─────────────────
//
// `Introspect.nodeWitness.Children` is the ordered child list (`getChildren`), and it answers
// nothing for a node's `state` alternatives or an ErrorBoundary. So the surface uniqueness walk
// `certify` runs above never reaches a node the tier holds in a NAMED slot — `state.onEmpty`,
// `state.onLoading`, the `fallback`s (the node's own and the error boundary's) — and two nodes
// sharing an id across that boundary read clean to it. Core's `keyedChildrenLawsAt` certifies
// the tier's whole-tree id check over exactly those positions, run here at the PRODUCTION
// witnesses (`Introspect.nodeWitness` / `keyedWitness` / `idWitness`), not a test-side copy.
//
// Three things make the adoption certify something, and each has its own case: the run is
// MEASURED (a witness that declares no keyed position passes the family vacuously, BY
// DECLARATION, and would read as a green count in the census), the domain check refuses the
// state.onEmpty collision the surface walk is blind to, and the family itself goes red over a
// check that compares only the surface.

module KeyedChildren =

    open Fuaran.UI.Ops

    let private family = "Conformance.keyedChildrenLawsAt"

    let private md (id: string) : Node<obj> = Fuaran.markdown id "x"

    let private box (id: string) (kids: Node<obj> list) : Node<obj> =
        Fuaran.dashboard
            id
            { Defaults.dashboard<obj> with
                Children = kids }

    let private canHold (n: Node<obj>) =
        Introspect.getChildren n.Kind |> Option.isSome

    /// A tree whose nodes carry every named-slot shape the family is about: ordered containers,
    /// nodes with a `state.onEmpty` and / or `state.onLoading` alternative or its own
    /// `fallback`, and ErrorBoundaries (`child` and `fallback`, both named). Every id is fresh
    /// across the whole tree, slots included, so a drawn tree is lawful and the built arms are
    /// the only collisions the run sees.
    let private genTree (rng: CoreRng.T) : Node<obj> * CoreRng.T =
        let mutable counter = 0
        let mutable r = rng

        let fresh () =
            counter <- counter + 1
            sprintf "k%d" counter

        let roll n =
            let v, r' = CoreRng.intBelow n r
            r <- r'
            v

        let dress (node: Node<obj>) =
            match roll 5 with
            | 0 -> node
            | 1 -> node |> Node.onEmpty (md (fresh ()))
            | 2 -> node |> Node.onLoading (md (fresh ()))
            | 3 -> node |> Node.onLoading (md (fresh ())) |> Node.onEmpty (md (fresh ()))
            | _ ->
                { node with
                    Fallback = Some(md (fresh ())) }

        let rec build depth =
            let nid = fresh ()

            match roll (if depth <= 0 then 2 else 3) with
            | 0 -> dress (md nid)
            | 1 ->
                dress (
                    Fuaran.errorBoundary
                        nid
                        { Child = md (fresh ())
                          Fallback = md (fresh ()) }
                )
            | _ -> dress (box nid [ for _ in 1 .. roll 3 -> build (depth - 1) ])

        let rootId = fresh ()
        let root = box rootId [ for _ in 1 .. 1 + roll 3 -> build 2 ]
        root, r

    let private genFresh (existing: Set<string>) (rng: CoreRng.T) : Node<obj> * CoreRng.T =
        let mutable r = rng

        let rec pick () =
            let v, r' = CoreRng.next r
            r <- r'
            let id = sprintf "f%d" (v % 100000)
            if existing.Contains id then pick () else id

        md (pick ()), r

    let private opGen: Fuaran.Core.OpGen<Node<obj>, NodeId> =
        { Tree = genTree
          FreshNode = genFresh
          CanHold = Some canHold }

    let private seed = 20261017
    let private iterations = 200

    /// The family at the tier's own node and id witnesses, over `keyw` and `gen`.
    let private run
        (keyw: Fuaran.Core.KeyedWitness<Node<obj>, NodeId>)
        (gen: Fuaran.Core.OpGen<Node<obj>, NodeId>)
        (n: int)
        =
        CoreConf.keyedChildrenLawsAt keyw Introspect.nodeWitness Introspect.idWitness gen seed n

    // Registered at module initialisation (LawCases.fs's header says why). The bare name, Core's
    // obsolete forward of this body, left Core at 0.37.0 (Phase 2226), so the run answers for one.
    let private keyedChildrenRun =
        LawCases.adopt [ family ] iterations (run Introspect.keyedWitness opGen)

    let private guardPrefix = "sample adequacy (" + family + "): "

    let private isGuard (r: Fuaran.Core.LawResult) =
        r.Law.StartsWith(guardPrefix, System.StringComparison.Ordinal)

    /// The adequacy rule, stated as the family states it: the run is evidence only when its ONE
    /// guard is the built-arm `reached` law and it passed. The vacuous-by-declaration line is a
    /// PASSING guard law, so `List.forall _.Passed` alone would accept it — that is the reading
    /// this predicate exists to refuse.
    let private measured (results: Fuaran.Core.LawResult list) =
        match results |> List.filter isGuard with
        | [ guard ] ->
            guard.Passed
            && guard.Law.Contains("the sample reached every built arm", System.StringComparison.Ordinal)
            && not (guard.Law.Contains("BY DECLARATION", System.StringComparison.Ordinal))
        | _ -> false

    /// The check `certify`'s surface walk stands on: id uniqueness over `Children` alone.
    let private surfaceOnly: Fuaran.Core.KeyedWitness<Node<obj>, NodeId> =
        { Introspect.keyedWitness with
            Surface = "the surface walk alone (Tree.wellFormed over nodeWitness)"
            IdsUnique =
                fun n ->
                    Fuaran.Core.Tree.wellFormed Introspect.nodeWitness Introspect.idWitness n = Fuaran.Core.Tree.Structural }

    /// A host whose only named slots are two `state.onEmpty` alternatives, so every collision the
    /// family builds over it is an id in `state.onEmpty` and in the ordinary children (or in two
    /// onEmpty slots) — the go-red shape, exercised by the family itself.
    let private onEmptyHost: Node<obj> =
        box
            "root"
            [ md "a"
              md "list" |> Node.onEmpty (md "e1")
              md "grid" |> Node.onEmpty (md "e2") ]

    let private onEmptyGen: Fuaran.Core.OpGen<Node<obj>, NodeId> =
        { opGen with
            Tree = fun r -> onEmptyHost, r }

    let private lawNamed (prefix: string) (results: Fuaran.Core.LawResult list) =
        match
            results
            |> List.filter (fun r -> r.Law.StartsWith(prefix, System.StringComparison.Ordinal))
        with
        | [ one ] -> one
        | other -> failtestf "expected exactly one law starting '%s', found %d" prefix (List.length other)

    [<Tests>]
    let tests =
        testList
            "Phase 1857 — keyed children"
            [ testCase
                  "the tier's whole-tree id check certifies under Core's keyedChildrenLawsAt over the named node slots"
              <| fun _ ->
                  let results = keyedChildrenRun.Force()
                  let subject = results |> List.filter (isGuard >> not)
                  Expect.equal (List.length subject) 3 "the family's three laws ran"

                  for r in results do
                      Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

              testCase "the keyedChildrenLawsAt run is MEASURED, never vacuous by declaration"
              <| fun _ ->
                  let results = keyedChildrenRun.Force()

                  Expect.isTrue
                      (measured results)
                      (sprintf
                          "the run is not evidence: %A"
                          (results |> List.filter isGuard |> List.map (fun r -> r.Law, r.Counterexample)))

                  let cell = LawCases.cell family

                  Expect.isTrue
                      (fst (System.Int32.TryParse cell) && int cell > 0)
                      (sprintf "the census Cases cell reads %s, not a count" cell)

                  // The predicate bites, in both of the family's non-evidence states: a witness that
                  // declares no keyed position (the family PASSES it, by declaration), and one that
                  // declares positions but gives the kit no way to build into them (the guard reds).
                  let declaresNone =
                      { Introspect.keyedWitness with
                          KeyedChildren = fun _ -> []
                          PlaceKeyedChild = fun _ _ -> None }

                  let byDeclaration = run declaresNone opGen iterations

                  Expect.isTrue
                      (byDeclaration |> List.forall _.Passed)
                      "the family passes a witness that declares nothing — which is why passing is not the test"

                  Expect.isFalse (measured byDeclaration) "vacuous by declaration is refused as evidence"

                  let cannotBuild =
                      { Introspect.keyedWitness with
                          PlaceKeyedChild = fun _ _ -> None }

                  Expect.isFalse
                      (measured (run cannotBuild opGen iterations))
                      "declared but unbuildable is refused as evidence"

              testCase "an id held in state.onEmpty and in the ordinary children list is refused over the keyed walk"
              <| fun _ ->
                  let holder = md "list" |> Node.onEmpty (md "dup")
                  let fixture = box "root" [ md "dup"; holder ]

                  Expect.equal
                      (Fuaran.Core.Tree.wellFormed Introspect.nodeWitness Introspect.idWitness fixture)
                      Fuaran.Core.Tree.Structural
                      "the surface walk is blind to the onEmpty slot — the gap this phase closes"

                  Expect.equal
                      (Fuaran.Core.Tree.keyedIds Introspect.nodeWitness Introspect.keyedWitness holder)
                      [ NodeId "dup" ]
                      "the KeyedWitness declares state.onEmpty"

                  Expect.equal
                      (Fuaran.Core.Tree.wellFormedKeyed
                          Introspect.nodeWitness
                          Introspect.keyedWitness
                          Introspect.idWitness
                          fixture)
                      (Fuaran.Core.Tree.RepeatedId(NodeId "dup"))
                      "Core's keyed walk, through the tier's witness, names the repeated id"

                  Expect.isFalse (Introspect.keyedWitness.IdsUnique fixture) "the tier's id check refuses it"

              testCase "the family refuses a surface-only id check across the state.onEmpty boundary"
              <| fun _ ->
                  let real = run Introspect.keyedWitness onEmptyGen iterations

                  for r in real do
                      Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

                  Expect.isTrue (measured real) "every built arm was reached over the onEmpty host"

                  let blind = run surfaceOnly onEmptyGen iterations

                  let clash =
                      lawNamed
                          "the domain's id check refuses an id held in a keyed position and in the witness surface"
                          blind

                  Expect.isFalse
                      clash.Passed
                      "the family let a surface-only check through with an id in state.onEmpty and in the children"

                  let twice =
                      lawNamed "the domain's id check refuses an id held in two keyed positions" blind

                  Expect.isFalse
                      twice.Passed
                      "the family let a surface-only check through with one id in two onEmpty slots" ]

// ─── Phase 2038 — one answer to what a node's children are ──────────────────
//
// The tier supplies its `KeyedWitness` in production (`Introspect.keyedWitness`,
// derived from the `NodeChildren` lens rather than a second list) and the apply
// path calls Core's keyed forms. The go-red fixtures below failed on the private
// child walks they replace; the census keeps those walks from coming back.

module KeyedEnumeration =

    open System
    open System.IO
    open System.Text.RegularExpressions
    open Fuaran.UI.Ops

    let private md (id: string) : Node<obj> = Fuaran.markdown id "x"

    /// A dashboard holding `existing`.
    let private host: Node<obj> =
        Fuaran.dashboard
            "root"
            { Defaults.dashboard<obj> with
                Children = [ md "existing" ] }

    /// A Switch holding `caseChild` in its one case and `defaultChild` as its
    /// default — both KEYED positions, invisible to the structural walk.
    let private switchOf (caseChild: Node<obj>) (defaultChild: Node<obj>) : Node<obj> =
        Fuaran.switch
            "incoming"
            { Defaults.switch<obj> with
                Cases =
                    [ { Child = caseChild
                        Match = Some "a"
                        When = None } ]
                Default = defaultChild }

    /// The source files outside the enumeration's home that still carry a
    /// child-enumerating `NodeKind` match. FROZEN: none of them is in Phase
    /// 2038's cross-section, so adopting the enumeration there is its own change
    /// (and removes the file from this list, which the census then requires). A
    /// file may leave this list; none may join it.
    let private grandfathered =
        set
            [ "Fuaran.UI/BindingWalk.fs"
              "Fuaran.UI/Generated.fs"
              "Fuaran.UI/HostLimits.fs"
              "Fuaran.UI/PreEmitValidate.fs"
              "Fuaran.UI.Renderer/Render.fs"
              "Fuaran.UI.Renderer.Server/Email.fs"
              "Fuaran.UI.Renderer.Server/Speech.fs"
              "Fuaran.UI.Validator.Certification/RecipeCertification.fs" ]

    /// The enumeration's home — the one module allowed to take a `NodeKind`
    /// apart to find its children.
    let private home = "Fuaran.UI/NodeChildren.fs"

    /// The shapes a hand-written child enumeration takes, one line each: a
    /// container arm yielding its `.Children`, an error-boundary arm yielding an
    /// arm, a fragment declaration yielding `[ body ]`, a switch's cases mapped
    /// to their children and joined to the default.
    let private enumerating =
        Regex(
            @"\|\s*NodeKind\.(Box|SplitPanel|Tabs|Stepper|SummaryList|Disclosure|Modal|ScrollArea)\b.*->.*\.Children\b"
            + @"|\|\s*NodeKind\.ErrorBoundary\b.*->.*\.(Child|Fallback)\b"
            + @"|\|\s*NodeKind\.FragmentDecl\b.*->\s*\[\s*\w+\.Body\s*\]"
            + @"|_\.Child\)\s*@\s*\[",
            RegexOptions.Compiled
        )

    let private srcRoot =
        lazy
            (let rec climb (dir: DirectoryInfo option) =
                match dir with
                | None -> failwith "Phase 2038 census: no checkout root (run.ps1 + Fuaran.sln) above the test binary"
                | Some d ->
                    if
                        File.Exists(Path.Combine(d.FullName, "run.ps1"))
                        && File.Exists(Path.Combine(d.FullName, "Fuaran.sln"))
                    then
                        Path.Combine(d.FullName, "src")
                    else
                        climb (Option.ofObj d.Parent)

             climb (Some(DirectoryInfo(AppContext.BaseDirectory))))

    /// Every shipped source file (test projects, `bin` and `obj` excluded) that
    /// carries an enumerating line, as a `/`-separated path under `src/`.
    let private enumeratingFiles () : Set<string> =
        let root = srcRoot.Value

        Directory.GetFiles(root, "*.fs", SearchOption.AllDirectories)
        |> Array.map (fun p -> Path.GetRelativePath(root, p).Replace('\\', '/'))
        |> Array.filter (fun rel ->
            let project = rel.Split('/').[0]

            not (project.EndsWith(".Tests", StringComparison.Ordinal))
            && not (rel.Contains("/obj/", StringComparison.Ordinal))
            && not (rel.Contains("/bin/", StringComparison.Ordinal)))
        |> Array.filter (fun rel -> File.ReadLines(Path.Combine(root, rel)) |> Seq.exists enumerating.IsMatch)
        |> Set.ofArray

    let private insert (child: Node<obj>) =
        UiApply.apply (TreeOp.InsertChild(NodeId "root", child)) host

    [<Tests>]
    let tests =
        testList
            "Phase 2038 — the keyed enumeration"
            [ test "InsertChild of a Switch whose case child duplicates an existing id is refused" {
                  match insert (switchOf (md "existing") (md "d")) with
                  | Error e -> Expect.equal e.Code ApplyErrorCode.DuplicateNodeId "the case child's id is already held"
                  | Ok _ -> failtest "a Switch carrying a duplicate id in a case was grafted"
              }

              test "InsertChild of a Switch whose default duplicates an existing id is refused" {
                  match insert (switchOf (md "c") (md "existing")) with
                  | Error e -> Expect.equal e.Code ApplyErrorCode.DuplicateNodeId "the default's id is already held"
                  | Ok _ -> failtest "a Switch carrying a duplicate id in its default was grafted"
              }

              test "a clean Switch still inserts, and the result is well formed over the keyed walk" {
                  match insert (switchOf (md "c") (md "d")) with
                  | Ok updated ->
                      Expect.equal
                          (Fuaran.Core.Tree.wellFormedKeyed
                              Introspect.nodeWitness
                              Introspect.keyedWitness
                              Introspect.idWitness
                              updated)
                          Fuaran.Core.Tree.Structural
                          "every id unique over the keyed walk"
                  | Error e -> failtestf "a clean Switch was refused: %A" e.Code
              }

              test "placeOp refuses the graft the apply path refuses" {
                  match
                      Placement.placeOp
                          host
                          (switchOf (md "existing") (md "d"))
                          { ParentId = NodeId "root"
                            Placement = Fuaran.Core.Anchor.Last }
                  with
                  | Error(PlaceError.Refused(Fuaran.Core.Rejection.DuplicateId(NodeId "existing"))) -> ()
                  | other -> failtestf "expected DuplicateId existing, got %A" other
              }

              test "the KeyedWitness is the lens descendantNodes reads, not a second list" {
                  let eb =
                      Fuaran.errorBoundary
                          "eb"
                          { Child = md "child"
                            Fallback = md "fb" }

                  Expect.equal
                      (Introspect.nodeWitness.Children eb @ Introspect.keyedWitness.KeyedChildren eb
                       |> List.map _.Id)
                      (Introspect.descendantNodes eb |> List.map _.Id)
                      "structural children then keyed children are exactly the traversal surface"

                  let rebuilt =
                      Introspect.keyedWitness.ReplaceKeyedChildren eb [ md "child2"; md "fb2" ]

                  Expect.equal
                      (Introspect.keyedWitness.KeyedChildren rebuilt |> List.map _.Id)
                      [ "child2"; "fb2" ]
                      "ReplaceKeyedChildren writes back position for position"
              }

              test "findParentWithin differs from the structural spine only below a keyed position" {
                  let inner =
                      Fuaran.dashboard
                          "inner"
                          { Defaults.dashboard<obj> with
                              Children = [ md "leaf" ] }

                  let tree =
                      Fuaran.dashboard
                          "top"
                          { Defaults.dashboard<obj> with
                              Children = [ switchOf inner (md "d") ] }

                  let ids (found: (Node<obj> * int) option) =
                      found |> Option.map (fun (p, i) -> p.Id, i)

                  Expect.isNone (Introspect.findParent (NodeId "leaf") tree) "the structural spine stops at the Switch"

                  Expect.equal
                      (ids (Introspect.findParentWithin NodeChildren.Reach.keyed (NodeId "leaf") tree))
                      (Some("inner", 0))
                      "the keyed reach finds the Box held in the case"

                  Expect.equal
                      (ids (Introspect.findParentWithin NodeChildren.Reach.keyed (NodeId "incoming") tree))
                      (ids (Introspect.findParent (NodeId "incoming") tree))
                      "on the spine the two reaches answer alike"
              }

              test "the client fragment registry walks ErrorBoundary.fallback (the server's go-red pins the same)" {
                  let tree: Node<obj> =
                      Fuaran.errorBoundary
                          "eb"
                          { Child = Fuaran.fragmentRef "ref" "inFallback"
                            Fallback =
                              Fuaran.fragmentDecl
                                  "decl"
                                  { Defaults.fragmentDecl<obj> with
                                      Name = "inFallback"
                                      Body = md "frag-body" } }

                  let registry = Fuaran.UI.Renderer.Render.collectFragments Map.empty tree
                  Expect.isTrue (Map.containsKey (FragmentId "inFallback") registry) "the client registers the decl"
              }

              test "census: no child-enumerating NodeKind match outside the enumeration's home" {
                  let found = enumeratingFiles ()

                  // The probe must see SOMETHING, or an empty answer below is vacuous.
                  Expect.isTrue (Set.contains home found) "the census pattern recognises the home's own match"

                  let strays = found - grandfathered - set [ home ]

                  Expect.isEmpty
                      strays
                      "a new hand-written child enumeration — read `Fuaran.UI.NodeChildren` at the reach you mean instead"

                  let adopted = grandfathered - found

                  Expect.isEmpty
                      adopted
                      "a grandfathered file no longer enumerates children by hand — remove it from the frozen list"
              } ]

// ============================================================================
//  Phase 2308 — the memo's admission gate is Core's `applyMemo`.
//
//  `Fuaran.UI.Memo`'s engine gates an application on `Fuaran.Core.Memo.isMemoisable`
//  over the effect observed over the fragment's subtree and its slot arguments, over
//  `MemoGate.witness` — the gate `Fuaran.Core.Function.applyMemo` applies. Three
//  claims, each checked here rather than stated:
//
//   1. Core's memo laws hold over that witness and the encoder the engine's key is
//      built on (`CanonicalJson.encodeNode`). The encoder's PRECONDITION is stated
//      and asserted per draw: `applyMemo` needs an injective encoder (Core's Phase
//      56), and the canonical encoding is injective only where it carries no
//      unrepresentable-value sentinel — which is exactly the encoding the engine's
//      Phase 1525 refusal declines to key. So the sentinel refusal IS the law's
//      precondition, and no drawn node carries a sentinel.
//   2. Core's memo-soundness law holds over the go-red fixture: a fragment declared
//      pure that nests a network sub-fragment.
//   3. The engine, over its portable `MemoCacheStore`, answers every application of
//      a matrix of fragments and slot arguments exactly as `applyMemo` does over
//      the witness: the same hit / miss / bypass, the same tree, the same counters.
// ============================================================================

module MemoGateConformance =

    open Fuaran.UI.Memo
    open Fuaran.UI.Telemetry.Abstractions

    /// `MemoGate.witness` over the canonical-encoding equality wrapper the kit
    /// needs (`Node<obj>` carries closures and is not an equality type).
    let private eqWitness: Fuaran.Core.ArtifactWitness<EqNode, NodeId> =
        let w = MemoGate.witness<obj>

        let lowerArg (a: Fuaran.Core.Arg<EqNode>) : Fuaran.Core.Arg<Node<obj>> =
            match a with
            | Fuaran.Core.ValueArg s -> Fuaran.Core.ValueArg s
            | Fuaran.Core.SlotArg e -> Fuaran.Core.SlotArg(unwrap e)

        { Tree =
            { Id = unwrap >> w.Tree.Id
              KindTag = unwrap >> w.Tree.KindTag
              Children = unwrap >> w.Tree.Children >> List.map wrap
              ReplaceChildren = fun e cs -> wrap (w.Tree.ReplaceChildren (unwrap e) (List.map unwrap cs)) }
          IdW = w.IdW
          Holes = unwrap >> w.Holes
          Effect = unwrap >> w.Effect
          Bind = fun addr a e -> w.Bind addr (lowerArg a) (unwrap e) |> Result.map wrap }

    /// The encoder the engine's structural key is built on.
    let private encode (e: EqNode) : string = CanonicalJson.encodeNode e.Node

    /// The ref site, which is also the decl's id: `MemoGate.witness` namespaces a
    /// slot by the decl's id, the engine by the ref site's.
    let private refId = "card"

    let private md (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

    /// A network-reading sub-fragment, honestly declared.
    let private ticker: Node<obj> =
        Fuaran.fragmentDecl
            "network-ticker"
            { Defaults.fragmentDecl<obj> with
                Name = "network-ticker"
                Effect =
                    { HostEffect = HostEffect.ReadsHost
                      Determinism = DeterminismSource.Network }
                Body = md "ticker-text" "price" }

    /// A card: a value hole `alpha` (first in address order, so the kit's
    /// separator arm runs) and a `content` slot, with `extra` appended to its body.
    let private card (name: string) (effect: EffectClass) (extra: Node<obj> list) : ParamFragment<obj> =
        { Defaults.fragmentDecl<obj> with
            Name = name
            Effect = effect
            Holes =
                [ HoleDecl.Value("alpha", HoleValueSpace.StringLen(1, 40), None)
                  HoleDecl.Slot("content", None) ]
            Body =
                Fuaran.dashboard
                    "card-root"
                    { Defaults.dashboard<obj> with
                        // fuaran-validator: disable-next-line FUARAN057 — the reference names the card's own `content` slot, not a declared fragment
                        Children = [ md "card-title" "Title"; Fuaran.fragmentRef "content" "content" ] @ extra } }

    let private pureCard = card "card" EffectClass.pureDeterministic []

    let private effectingCard =
        card
            "card"
            { HostEffect = HostEffect.ReadsHost
              Determinism = DeterminismSource.Clock }
            []

    /// The go-red fixture: declared pure, nesting the network ticker.
    let private underDeclaredCard = card "card" EffectClass.pureDeterministic [ ticker ]

    /// The same lie told one level down, in a Switch case — a keyed position.
    let private underDeclaredInSwitch =
        card
            "card"
            EffectClass.pureDeterministic
            [ Fuaran.switch
                  "mode"
                  { Defaults.switch<obj> with
                      Cases =
                          [ { Child = ticker
                              Match = Some "live"
                              When = None } ]
                      Default = md "static" "closed" } ]

    let private fnNode (pf: ParamFragment<obj>) : EqNode = wrap (MemoGate.declNode refId pf)

    let private args (alpha: string) (content: Node<obj>) : Map<string, Fuaran.Core.Arg<EqNode>> =
        Map.ofList
            [ refId + ".alpha", Fuaran.Core.ValueArg alpha
              refId + ".content", Fuaran.Core.SlotArg(wrap content) ]

    let private drawMemo (rng: CoreRng.T) : Fuaran.Core.MemoSample<EqNode> * CoreRng.T =
        let n, r1 = CoreRng.intBelow 1000 rng
        let pick, r2 = CoreRng.intBelow 4 r1
        let text = sprintf "body %d" n
        let alt = sprintf "body %d" (n + 1)

        // The effecting function, four ways: declared, under-declared in an ordered
        // child, under-declared in a keyed position, and pure with an effecting
        // slot argument (the join over the arguments, Core's Phase 307).
        let effectingFn, effectingContent =
            match pick with
            | 0 -> effectingCard, md "body" text
            | 1 -> underDeclaredCard, md "body" text
            | 2 -> underDeclaredInSwitch, md "body" text
            | _ -> pureCard, ticker

        { PureFn = fnNode pureCard
          Args = args (sprintf "v%d" n) (md "body" text)
          ArgsAlt = args (sprintf "v%d" n) (md "body" alt)
          EffectingFn = fnNode effectingFn
          EffectingArgs = args (sprintf "v%d" n) effectingContent },
        r2

    /// The precondition of the memo laws over this encoder, asserted rather than
    /// assumed: no node a draw hands the kit carries an unrepresentable-value
    /// sentinel, so the canonical encoding is injective over everything keyed.
    let private sentinelFree (s: Fuaran.Core.MemoSample<EqNode>) : bool =
        let slots (m: Map<string, Fuaran.Core.Arg<EqNode>>) =
            m
            |> Map.toList
            |> List.choose (fun (_, a) ->
                match a with
                | Fuaran.Core.SlotArg e -> Some e
                | Fuaran.Core.ValueArg _ -> None)

        [ s.PureFn; s.EffectingFn ]
        @ slots s.Args
        @ slots s.ArgsAlt
        @ slots s.EffectingArgs
        |> List.forall (fun e -> List.isEmpty (FragmentKey.unrepresentableIn "sample" (encode e)))

    /// Every sample the kit drew, and those that broke the precondition — read by
    /// the test after the run, so the precondition is checked over the kit's own
    /// draws rather than over a second stream that might differ from them.
    let private drawn = ResizeArray<Fuaran.Core.MemoSample<EqNode>>()

    let private checkedDraw (rng: CoreRng.T) : Fuaran.Core.MemoSample<EqNode> * CoreRng.T =
        let s, r = drawMemo rng
        drawn.Add s
        s, r

    let private memoSeed = 20261017
    let private memoIterations = 200

    let private memoResults =
        LawCases.adopt [ "Conformance.memoLawsAt" ] memoIterations (fun iterations ->
            CoreConf.memoLawsAt eqWitness encode checkedDraw Fuaran.Core.OpStream.sha256Hash memoSeed iterations)

    let private memoSoundnessResults =
        LawCases.adopt [ "Conformance.memoSoundnessLawsAt" ] 20 (fun iterations ->
            CoreConf.memoSoundnessLawsAt
                eqWitness
                encode
                (fnNode underDeclaredCard)
                (args "v" (md "body" "x"))
                memoSeed
                iterations)

    let private assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
        Expect.isNonEmpty results (context + ": the family reported no law")

        let failures = results |> List.filter (fun r -> not r.Passed)

        if not (List.isEmpty failures) then
            failures
            |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
            |> String.concat "\n"
            |> failtestf "%s failed:\n%s" context

    /// A sink that records the engine's outcome per call.
    type private Outcomes() =
        let outcomes = ResizeArray<CacheOutcome>()
        member _.All = List.ofSeq outcomes

        interface IFuaranTelemetrySink with
            member _.RecordOpApply _ = ()
            member _.RecordDeny _ = ()
            member _.RecordRenderFailure _ = ()
            member _.RecordProviderCall _ = ()
            member _.RecordCacheStat t = outcomes.Add t.Outcome
            member _.RecordValidateOutcome _ = ()

    /// The body of a decl node `applyMemo` returned — what the engine stores.
    let private bodyOf (e: EqNode) : Node<obj> =
        match e.Node.Kind with
        | NodeKind.FragmentDecl spec -> spec.Body
        | other -> failwithf "applyMemo returned a %A, not the applied decl" other

    [<Tests>]
    let tests =
        testList
            "Phase 2308 — the memo gate is Core's applyMemo"
            [ test "the memo engine's witness and key encoder certify under Core's memoLawsAt" {
                  memoResults.Value |> assertAllPassed "memoLawsAt over MemoGate.witness"

                  // The precondition, over exactly the samples the kit drew.
                  Expect.isNonEmpty drawn "the kit drew no sample, so the precondition was checked over nothing"

                  let broken = drawn |> Seq.filter (sentinelFree >> not) |> Seq.length
                  Expect.equal broken 0 "every drawn node is sentinel-free, so the encoder is injective over it"

                  // Every effecting variant was drawn, so the bypass arm met each of them.
                  let effecting =
                      drawn
                      |> Seq.map (fun s -> encode s.EffectingFn, encode s.PureFn = encode s.EffectingFn)
                      |> Seq.distinct
                      |> Seq.length

                  Expect.equal effecting 4 "all four effecting variants were drawn"
              }

              test "an under-declared fragment is never cached under Core's memoSoundnessLawsAt" {
                  memoSoundnessResults.Value
                  |> assertAllPassed "memoSoundnessLawsAt over the pure-declared card nesting a network ticker"
              }

              test "the memo engine admits, serves and stores exactly what Core's applyMemo does" {
                  let fragments =
                      [ "pure", pureCard
                        "declared effecting", effectingCard
                        "pure nesting a network ticker", underDeclaredCard
                        "pure nesting a network ticker in a Switch case", underDeclaredInSwitch ]

                  let slotSets =
                      [ "pure slots", md "body" "x", md "body" "y"; "a network slot", ticker, ticker ]

                  for fname, pf in fragments do
                      for sname, first, second in slotSets do
                          let context = fname + ", " + sname
                          let session = [ first; first; second; first ]
                          let sink = Outcomes()
                          let store = FragmentStore.portable<obj> ()
                          let engine = Engine<obj>(store :> IFragmentStore<obj>, sink)

                          let engineTrees =
                              session
                              |> List.map (fun content ->
                                  match
                                      engine.Apply(
                                          pf,
                                          refId,
                                          Map.ofList [ "alpha", Unchecked.nonNull (box "v") ],
                                          Map.ofList [ "content", content ]
                                      )
                                  with
                                  | Ok d -> d.Result.Tree
                                  | Error e -> failtestf "%s: the engine refused: %s" context e)

                          let coreSteps, coreCache =
                              ((Fuaran.Core.Memo.empty, []), session)
                              ||> List.fold (fun (cache: Fuaran.Core.MemoCache<EqNode>, acc) content ->
                                  match
                                      Fuaran.Core.Function.applyMemo
                                          eqWitness
                                          encode
                                          (args "v" content)
                                          (fnNode pf)
                                          cache
                                  with
                                  | Ok(tree, cache') ->
                                      let step =
                                          if cache'.Hits > cache.Hits then CacheOutcome.Hit
                                          elif cache'.Misses > cache.Misses then CacheOutcome.Miss
                                          else CacheOutcome.Bypass

                                      cache', acc @ [ step, bodyOf tree ]
                                  | Error e -> failtestf "%s: applyMemo refused: %A" context e)
                              |> fun (cache, steps) -> steps, cache

                          Expect.equal sink.All (coreSteps |> List.map fst) (context + ": hit / miss / bypass per call")

                          List.zip engineTrees (coreSteps |> List.map snd)
                          |> List.iteri (fun i (mine, core) ->
                              Expect.equal
                                  (CanonicalJson.encodeNode mine)
                                  (CanonicalJson.encodeNode core)
                                  (sprintf "%s: call %d derives the tree applyMemo derives" context i))

                          let snapshot = store.Snapshot

                          Expect.equal
                              (snapshot.Hits, snapshot.Misses, snapshot.Bypasses)
                              (coreCache.Hits, coreCache.Misses, coreCache.Bypasses)
                              (context + ": the portable store's counters are applyMemo's")

                          Expect.equal
                              (Map.count snapshot.Entries)
                              (Map.count coreCache.Entries)
                              (context + ": the same number of entries is stored")
              } ]
