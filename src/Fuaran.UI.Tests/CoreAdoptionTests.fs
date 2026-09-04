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
module CoreRng = Fuaran.Core.ConfRng

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
                { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
                  Role = BoxRole.Group
                  Heading = None
                  Children = kids |> List.map unwrap
                  KeepTogether = false
                  BreakBefore = false }
            )
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None
          Tooltip = None }

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
          ExtraAttributes = Option.None
          Tooltip = None }

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

[<Tests>]
let tests =
    testList
        "Core adoption (Fuaran.UI)"
        [ testCase "the Fuaran.UI witness certifies end-to-end via the unified Conformance.certify"
          <| fun _ ->
              // Homogeneous-at-the-protocol-level, so the full bundle applies: witnessLaws +
              // opAlgebra (via CanHold) + streamLaws, through one entry point. Equality is supplied
              // by the canonical-encoding wrapper.
              let report =
                  CoreConf.certify nodew idw opGen coreSw streamGen CoreStream.defaultHash 20260619 200

              if not report.AllPassed then
                  let msg =
                      report.Results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "Fuaran.UI witness failed Core certify:\n%s" msg

          testCase "Core skeleton ops + invert operate over a concrete Fuaran.UI tree"
          <| fun _ ->
              let tree = mkStack "root" [ mkStack "a" [] ]
              let op = Fuaran.Core.SkeletonOp.InsertChild(NodeId "root", mkStack "b" [])

              match Fuaran.Core.Ops.apply nodew idw op tree with
              | Ok post ->
                  Expect.equal
                      (nodew.Children post |> List.map (fun e -> e.Node.Id))
                      [ "a"; "b" ]
                      "child inserted via Core op"

                  match Fuaran.Core.Ops.invert nodew idw op tree with
                  | Ok inv ->
                      match Fuaran.Core.Ops.apply nodew idw inv post with
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
                              CoreStream.defaultHash
                              coreSw
                              (Fuaran.Core.Actor.ofLegacyString actor)
                              op
                              st
                              recs))

              match coreResult, uiReplay with
              | Ok(coreState, coreRecs), Ok uiState ->
                  Expect.equal coreState uiState "Core.OpStream state == Fuaran.UI apply/replay state"
                  Expect.isTrue (CoreStream.verifyChain CoreStream.defaultHash coreSw coreRecs) "Core chain verifies"

                  // the portable win: Core's fromJsonl is FSharp.Core-only (runs in-browser) and
                  // Result-typed; a round-trip replays identically.
                  match CoreStream.toJsonl coreSw coreRecs |> CoreStream.fromJsonl coreSw with
                  | Ok restored ->
                      match CoreStream.replay coreSw baseTree restored with
                      | Ok rs -> Expect.equal rs coreState "portable JSONL round-trip replays identically"
                      | Error e -> failtestf "round-trip replay failed: %A" e
                  | Error e -> failtestf "fromJsonl failed: %s" e
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
                      nodew
                      idw
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
              let sha256HashFn: Fuaran.Core.HashFn =
                  fun prev payload -> Fuaran.UI.OpStream.Abstractions.HashChain.sha256Hex (prev + "|" + payload)

              // A high-acceptance generator: the hash laws exercise the CHAIN (determinism,
              // pre-image parity, tamper-detection), so every op should append — the
              // rejection-mixing `streamGen` above is the right input for the reducer laws,
              // not these (a rejection-heavy stream legitimately builds 2-record chains,
              // which starves the interior-drop tamper branch).
              let hashLawGen: Fuaran.Core.StreamGen<TreeOp<obj>, EqNode> =
                  { State0 = baseTree
                    Op =
                      fun rng ->
                          let v, r' = CoreRng.next rng
                          TreeOp.InsertChild(NodeId "root", unwrap (mkSpacer (sprintf "h%d" v))), r' }

              let assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
                  let failures = results |> List.filter (fun r -> not r.Passed)

                  if not (List.isEmpty failures) then
                      failures
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"
                      |> failtestf "%s failed:\n%s" context

              CoreConf.hashFnLaws coreSw hashLawGen sha256HashFn 20260705 100
              |> assertAllPassed "hashFnLaws over the portable SHA-256"

              // budget + seed match Core's own invocation (500_000, 4242): the "FNV-1a admits a
              // forgery" branch needs the 32-bit birthday bound to realise a collision, AND the
              // law's forge pre-images are seed-shaped — sequential fixed-length 8-digit strings
              // (e.g. seed 20260705) happen to be FNV-collision-free in-budget, while 4242 spans
              // 4→6-digit lengths where collisions realise. The FNV branch is crypto-fn-independent,
              // so reusing Core's proven seed keeps this a test of OUR SHA-256's resist branch.
              CoreConf.hashFnAdversarialLaws sha256HashFn 500_000 4242
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
              let entryOf (op: TreeOp<obj>) : StreamEntry<obj> =
                  { Op = op
                    Timestamp = System.DateTimeOffset.FromUnixTimeSeconds 1_700_000_000L
                    PromptId = None
                    ResultEnvelope = OpResultEnvelope.Success }

              let entrySw: Fuaran.Core.StreamWitness<StreamEntry<obj>, EqNode, ApplyError> =
                  { Apply = fun entry e -> UiApply.apply entry.Op e.Node |> Result.map wrap
                    Encode = StreamEntry.encode
                    Decode = fun _ -> Error "StreamEntry decode is not exercised by certifyStream" }

              let entryStreamGen: Fuaran.Core.StreamGen<StreamEntry<obj>, EqNode> =
                  { State0 = baseTree
                    Op =
                      fun rng ->
                          let op, r' = genStreamOp rng
                          entryOf op, r' }

              let report =
                  CoreConf.certifyStream entrySw entryStreamGen StreamEntry.hashFn 20260705 200

              if not report.AllPassed then
                  report.Results
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
//   * `columnarOpLaws` — the tier's one production `Column.create` authoring
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
//     columnar-validation rule is FUARAN114 (Phase 1149) in
//     `PreEmitValidate.fs`: it grounds a grid's column `field` / `rowKeyField`
//     against the schema of the `Table` the grid reads. The same two properties
//     the law certifies for Core's validator — determinism, and a defect count
//     equal to the injected-fault count — are asserted over that rule.
//   * `schemaWalkLaws` — nothing in this repo calls the schema walk yet. The
//     rule above derives its column set by hand from an EMPTY pipeline, and its
//     own doc comment records the widening as waiting on a pin that carries the
//     walk. That pin arrived: 0.18.0 ships it. So the agreement the widening
//     would rest on is asserted here — the walk against the rule's window, and
//     the walk against the schema the tier's OWN refinement evaluator produces
//     for the pipelines the rule currently stands down on. Widening the shipped
//     rule is a change to what FUARAN114 reports and is deliberately not made
//     here.
// ============================================================================

module Columnar =

    open Fuaran.Core

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
    /// production `Column.create` authoring surface.
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
        { Schema = [ "dept", StringType; "headcount", IntType; "spend", FloatType ]
          Columns =
            [ Column.create "dept" StringType [ Str "eng"; Str "ops"; Str "eng" ]
              Column.create "headcount" IntType [ Int 12; Int 3; Int 7 ]
              Column.create "spend" FloatType [ Float 100.0; Float 25.5; Float 60.25 ] ] }

    let private deptNames = deptTable.Schema |> List.map fst

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
                { SortStateKey = None
                  PageSize = None
                  PageStateKey = None
                  EditStateKey = None
                  DefaultSort = None
                  Source = source
                  RowKey = None
                  RowKeyField = rowKeyField
                  Columns =
                    columnFields
                    |> List.map (fun f ->
                        { Label = f
                          Value = None
                          Field = Some f
                          Sortable = None
                          Editable = None
                          Format = Fuaran.UI.Types.CellFormat.None
                          Kind = Fuaran.UI.Types.CellKindErased.Text
                          Width = Fuaran.UI.Types.ColumnWidth.Auto })
                  OnRowClick = None
                  Editable = false
                  Reorderable = false
                  TransferInKey = None
                  TransferOutKey = None
                  StaticRows = None
                  KeepRowsTogether = false
                  RepeatHeader = false
                  Exportable = false }
            )
          State = None
          Style = None
          Accessibility = None
          Motion = Fuaran.UI.Defaults.Motion.none
          ExtraAttributes = None
          Tooltip = None }

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
        [ "sort", [ Sort [ "headcount", Asc ] ]
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
                  CoreConf.columnarOpLaws lawSeed 100
                  |> assertAllPassed "columnarOpLaws over the pinned columnar op algebra"

              testCase "the columnar validator certifies under Core's columnarValidatorLaws"
              <| fun _ ->
                  // Likewise: nothing here registers a Core columnar validator.
                  // The tier's own columnar-grounding rule is asserted below.
                  CoreConf.columnarValidatorLaws lawSeed 100
                  |> assertAllPassed "columnarValidatorLaws over the pinned columnar validator"

              testCase "aggregate parity certifies under Core's aggregateParityLaws"
              <| fun _ ->
                  CoreConf.aggregateParityLaws lawSeed 100
                  |> assertAllPassed "aggregateParityLaws over the pinned aggregate surface"

              testCase "static output-schema derivation certifies under Core's schemaWalkLaws"
              <| fun _ ->
                  CoreConf.schemaWalkLaws lawSeed 100
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

                      let table = Fuaran.UI.RetrievalSource.Hit.toTable hits

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

                      let table = Fuaran.UI.RetrievalSource.Hit.toTable hits

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
                          let actual = refined.Schema |> List.map fst

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

                          // And the gap itself, as a live fact rather than a
                          // comment: the rule stands down here, so a field the
                          // pipeline cannot produce passes ungrounded.
                          Expect.isEmpty
                              (gridOver pipeline [ "no-such-column" ] (Some "dept") |> ungroundedOf)
                              (sprintf "%s: FUARAN114 stands down on a non-empty pipeline" name)

                  Expect.isGreaterThan
                      closedNonEmpty
                      0
                      "at least one non-empty pipeline the rule stands down on has a closed walk — the widening's whole subject" ]
