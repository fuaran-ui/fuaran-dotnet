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
    match k with
    | NodeKind.Layout _ -> "Layout"
    | NodeKind.Display _ -> "Display"
    | NodeKind.Input _ -> "Input"
    | NodeKind.Visualisation _ -> "Visualisation"
    | NodeKind.Custom _ -> "Custom"
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Mount _ -> "Mount"

let private nodew: Fuaran.Core.NodeWitness<EqNode, NodeId> =
    { Id = fun e -> e.Node.Id
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
        { Id = NodeId id
          Kind =
            NodeKind.Box(
                    { Layout =
                        BoxLayout.Flex
                            { Direction = Vertical
                              Wrap = false
                              Gap = None }
                      Role = BoxRole.Group
                      Heading = None
                      Children = kids |> List.map unwrap }
            )
          State = Defaults.stateBehaviour
          Style = Defaults.style
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None }

let private mkSpacer (id: string) : EqNode =
    wrap
        { Id = NodeId id
          // Phase 459 — Spacer retired; a childless Markdown leaf serves the
          // same "leaf with no bindings/handlers" role this op-stream test needs.
          Kind = NodeKind.Markdown( { Text = TextSource.Literal "" })
          State = Defaults.stateBehaviour
          Style = Defaults.style
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None }

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
                      (nodew.Children post
                       |> List.map (fun e ->
                           match e.Node.Id with
                           | NodeId s -> s))
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
