module FableLaws.Laws

// ============================================================================
//  Phase 1488 — the laws the Fable harness certifies.
//
//  THREE, and they are different kinds of claim.
//
//  1. THE BROWSER MERGE. Since Phase 2645 the DAG's merge is the reconciliation
//     policy over Core's fold, minted as a Core node and checked to replay
//     (`DagMerge.mergeDag`), and it ships in a package that transpiles: it runs in
//     a browser. Its order-independence is the whole promise — two replicas that
//     merge the same pair of branches in the opposite order must reach the same
//     tree AND mint the same node, or they have diverged while both believing they
//     converged. Here it is a law over GENERATED branch pairs, run on both
//     pipelines (it replaced the tree merge's `merge3Way` law at the cut), beside
//     law 2c, the policy's own symmetry.
//
//  2. THE PINNED KIT, UNDER FABLE. `FoldConfluence.laneFoldLawsAt` is run over this
//     tier's own reducer, op codec and footprint projection — the same
//     instantiation Phase 1476 certified on .NET (`CoreDagLawTests.fs`), through
//     the same witnesses (`TestSupport.fs` copies them verbatim). What the second
//     port adds is not a second opinion about Core: the kit is written to be
//     value-identical under Fable (see `ConfRng.intBelow`'s comment on why it uses
//     no 32-bit multiply), and this is the first thing in this repo that CHECKS
//     that claim over a real consumer witness rather than trusting it.
//
//  3. THE BROWSER'S INFLATER, ON A FOREIGN BLOCK TYPE. `Deflate.inflate` decodes
//     a raw-DEFLATE stream a standard deflate library emitted — a DYNAMIC-Huffman
//     block, which is the block type this tier's own deflater never produces and
//     every other producer always does. Its .NET conformance check runs through
//     `System.IO.Compression`, which does not transpile, so the one pipeline that
//     actually receives foreign bundles was the one pipeline the claim excluded.
//
//  Everything printed is COUNTS and fixed vocabulary. The runner beside this file
//  executes the same program on .NET and under Node and compares the two outputs
//  line by line, so a divergence that leaves both pipelines internally lawful
//  still shows up. A law-only probe would report two disagreeing pipelines as two
//  green runs — the lesson `tests/pure-tier-laws` records, applying unchanged.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.Renderer
open FableLaws.TestSupport

module FoldConfluence = Fuaran.Core.FoldConfluence
module UiApply = Fuaran.UI.Ops.Apply

// ---------------------------------------------------------------------------
//  the branch generator
// ---------------------------------------------------------------------------
//
//  Both adequacy classes must be reachable or the law is a statement about a constant:
//
//    * DISJOINT edits (different children, or two inserts of DIFFERENT fresh ids under the same
//      parent) must auto-merge — and the second of those is the case the NodeId-byte tie-break
//      decides, so it is the case a perturbed tie-break reddens;
//    * COLLIDING edits (the same child restyled to two different tones, a remove against a
//      restyle) must refuse — identically under both orders.
//
//  The fresh-id pool is deliberately tiny (six ids), so two independently drawn branches insert
//  the same id often enough for that case to be sampled too.

let private freshIds = [| "i0"; "i1"; "i2"; "i3"; "i4"; "i5" |]

/// The CONTENT an insert carries. Drawn independently of the id, so two branches that pick the same
/// fresh id agree about it sometimes and disagree about it sometimes — the two branches of the
/// shared-children guard (Phase 1497). Before 1497 every generated insert carried the same empty
/// text, so a same-id collision could only ever be an agreement and the refusal path was unsampled.
let private freshTexts = [| ""; "alpha"; "beta" |]

let private genEdit () : TreeOp<obj> list =
    let shape = PortableRng.below 4
    let k = PortableRng.below 3
    let t = PortableRng.below 3
    let f = PortableRng.below 6
    let x = PortableRng.below 3

    match shape with
    | 0 -> [ TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText freshIds[f] freshTexts[x])) ]
    | 1 -> [ styleOp childIds[k] tones[t] ]
    | 2 -> [ TreeOp.RemoveNode childIds[k] ]
    | _ ->
        [ styleOp childIds[k] tones[t]
          TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText freshIds[f] freshTexts[x])) ]

/// Apply an edit script to the base. `None` when the script does not apply at all — counted and
/// skipped rather than silently treated as "no edit", which would inflate the fast-forward cases.
let private applyEdit (ops: TreeOp<obj> list) (start: Node<obj>) : Node<obj> option =
    ops
    |> List.fold
        (fun acc op ->
            match acc with
            | None -> None
            | Some node ->
                match UiApply.apply op node with
                | Ok next -> Some next
                | Error _ -> None)
        (Some start)

// ---------------------------------------------------------------------------
//  law 1 — the merge node replays, whichever head is named first (Phase 2645)
// ---------------------------------------------------------------------------
//
//  Restated over the fold at the cut. A merge is `DagMerge.mergeDag` over Core's
//  DAG: the policy derives the tree, Core mints a node over the script, and the
//  node is handed back only when Core's closure fold of it reaches that tree. The
//  laws, over generated branch pairs off one genesis:
//
//    * ORDER — `merge(A, B)` and `merge(B, A)` agree on the verdict (both merge or
//      both refuse), on the tree, and on the merge node's ADDRESS (Core sorts the
//      parents; the script is a function of the pair, not of the naming);
//    * REPLAY — Core's fold of a minted node reaches the derived tree (the merge
//      never reports `DeltaNotReplayable` for a pair the policy derives);
//    * SELF — a delta reconciled against itself is its own tree (every pair agrees);
//    * FAST-FORWARD — a delta reconciled against the empty delta is its own tree;
//    * SHARED INSERT — two same-id inserts that agree land once, and two that differ
//      refuse at their parent.
//
//  The envelope's mirror law over a swapped naming is law 2c's. What this law adds
//  under Fable is the whole minting path: SHA-256 node ids, Core's drain order and
//  the replay witness, all on the transpiled code.

type MergeVerdict =
    { Trials: int
      Applied: int
      Merged: int
      Refused: int
      ClassDivergences: int
      TreeDivergences: int
      AddressDivergences: int
      ReplayFailures: int
      SelfMergeFailures: int
      FastForwardFailures: int
      SharedInsertFailures: int
      SelfMergeStructural: int
      First: string option }

/// A fixed actor and instant, so every node id is a pure function of the ops and both
/// pipelines mint the same DAG.
let private lawActor = Actor.Human "fable-laws"

let private lawStamp =
    System.DateTimeOffset(2026, 9, 12, 0, 0, 0, System.TimeSpan.Zero)

let private lawRecord (parents: string list) (op: TreeOp<obj>) : DagOpRecord<obj> =
    DagOpRecord.create "s" parents op None lawActor lawStamp OpResultEnvelope.Success

/// Genesis (an empty Batch) and one branch node per op of each delta: the DAG and the two heads.
let private forkOf (opsA: TreeOp<obj> list) (opsB: TreeOp<obj> list) =
    let genesis = lawRecord [] (TreeOp.Batch [])

    let chain (ops: TreeOp<obj> list) =
        ops
        |> List.fold
            (fun (acc: DagOpRecord<obj> list) op ->
                let parent = (List.head acc).Hash
                lawRecord [ parent ] op :: acc)
            [ genesis ]

    let a = chain opsA
    let b = chain opsB
    DagOpRecord.toDag (a @ b), (List.head a).Hash, (List.head b).Hash

let private canon (t: Node<obj>) : string = CanonicalJson.encodeNode t

/// One naming's verdict: `Some (tree, node id)` for a merge, `None` for a refusal, and the
/// replay law's own failure named.
let private mergeOf b dag x y : Result<(string * string) option, string> =
    match (DagMerge.mergeDag MergeContext.defaults "s" b dag x y lawStamp).Result with
    | MergeResult.Merged(record, tree) -> Ok(Some(canon tree, record.Hash))
    // One delta a prefix of the other: the node ids coincide and the merge fast-forwards.
    | MergeResult.FastForward h -> Ok(Some("fast-forward", h))
    | MergeResult.Refused _ -> Ok None
    | MergeResult.DeltaNotReplayable _ -> Error "the minted node does not replay to the derived tree"
    | other -> Error("an unexpected merge outcome: " + sprintf "%A" other)

let sharedInsertFailures () : string list =
    let b = unwrap baseTree

    let ins (text: string) =
        TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText "shared" text))

    [ (match DagReconcile.reconcile b [ ins "alpha" ] [ ins "alpha" ] with
       | Error _ -> [ "SHAREDINSERT agreeing same-id inserts refused instead of taking the shared value" ]
       | Ok r ->
           let copies =
               Fuaran.UI.Ops.Introspect.getChildren r.Tree.Kind
               |> Option.defaultValue []
               |> List.filter (fun n -> n.Id = "shared")
               |> List.length

           if copies = 1 then
               []
           else
               [ "SHAREDINSERT agreeing same-id inserts produced " + string copies + " copies" ])
      (match DagReconcile.reconcile b [ ins "alpha" ] [ ins "beta" ] with
       | Error(ReconcileFault.Refused rs) when rs |> List.exists (fun r -> r.Conflict.Address = "root") -> []
       | Error _ -> [ "SHAREDINSERT differing same-id inserts refused without naming their parent" ]
       | Ok _ -> [ "SHAREDINSERT differing same-id inserts merged instead of refusing" ]) ]
    |> List.concat

let mergeOrderLaws (seed: int) (iterations: int) : MergeVerdict =
    let b = unwrap baseTree
    PortableRng.reseed seed
    let mutable applied = 0
    let mutable merged = 0
    let mutable refused = 0
    let mutable classDiv = 0
    let mutable treeDiv = 0
    let mutable addressDiv = 0
    let mutable replayFail = 0
    let mutable selfFail = 0
    let mutable ffFail = 0
    let mutable selfStructural = 0
    let first: string option ref = ref None

    let note (why: string) =
        if first.Value.IsNone then
            first.Value <- Some why

    let sharedInsert = sharedInsertFailures ()

    match sharedInsert with
    | msg :: _ -> note msg
    | [] -> ()

    let rootChildIds (n: Node<obj>) =
        Fuaran.UI.Ops.Introspect.getChildren n.Kind
        |> Option.defaultValue []
        |> List.map (fun c -> c.Id)

    for i in 0 .. iterations - 1 do
        let opsA = genEdit ()
        let opsB = genEdit ()

        match applyEdit opsA b, applyEdit opsB b with
        | Some a, Some _ ->
            applied <- applied + 1
            let dag, headA, headB = forkOf opsA opsB

            if headA <> headB then
                match mergeOf b dag headA headB, mergeOf b dag headB headA with
                | Error why, _
                | _, Error why ->
                    replayFail <- replayFail + 1
                    note ("trial " + string i + ": " + why)
                | Ok(Some(f, fid)), Ok(Some(r, rid)) ->
                    merged <- merged + 1

                    if f <> r then
                        treeDiv <- treeDiv + 1
                        note ("trial " + string i + ": merged trees differ by naming order")

                    if fid <> rid then
                        addressDiv <- addressDiv + 1
                        note ("trial " + string i + ": the merge node's address differs by naming order")
                | Ok None, Ok None -> refused <- refused + 1
                | _ ->
                    classDiv <- classDiv + 1
                    note ("trial " + string i + ": one naming merged and the other refused")

            if rootChildIds a <> rootChildIds b then
                selfStructural <- selfStructural + 1

            match DagReconcile.reconcile b opsA opsA with
            | Ok r when canon r.Tree = canon a -> ()
            | _ ->
                selfFail <- selfFail + 1
                note ("trial " + string i + ": a delta reconciled against itself is not its own tree")

            match DagReconcile.reconcile b [] opsA, DagReconcile.reconcile b opsA [] with
            | Ok l, Ok r when canon l.Tree = canon a && canon r.Tree = canon a -> ()
            | _ ->
                ffFail <- ffFail + 1
                note ("trial " + string i + ": a delta against the empty delta is not its own tree")
        | _ -> ()

    { Trials = iterations
      Applied = applied
      Merged = merged
      Refused = refused
      ClassDivergences = classDiv
      TreeDivergences = treeDiv
      AddressDivergences = addressDiv
      ReplayFailures = replayFail
      SelfMergeFailures = selfFail
      FastForwardFailures = ffFail
      SharedInsertFailures = List.length sharedInsert
      SelfMergeStructural = selfStructural
      First = first.Value }

let mergeVerdictLines (v: MergeVerdict) : string list =
    let counts =
        "MERGE trials="
        + string v.Trials
        + " applied="
        + string v.Applied
        + " merged="
        + string v.Merged
        + " refused="
        + string v.Refused

    let laws =
        "MERGELAW class="
        + string v.ClassDivergences
        + " tree="
        + string v.TreeDivergences
        + " address="
        + string v.AddressDivergences
        + " replay="
        + string v.ReplayFailures
        + " self="
        + string v.SelfMergeFailures
        + " fastforward="
        + string v.FastForwardFailures
        + " sharedinsert="
        + string v.SharedInsertFailures

    match v.First with
    | Some why -> [ counts; laws; "MERGEFAIL " + sanitise why ]
    | None -> [ counts; laws ]

let mergeViolations (v: MergeVerdict) : int =
    v.ClassDivergences
    + v.TreeDivergences
    + v.AddressDivergences
    + v.ReplayFailures
    + v.SelfMergeFailures
    + v.FastForwardFailures
    + v.SharedInsertFailures

let mergeAdequacyFailures (v: MergeVerdict) : string list =
    [ if v.Merged = 0 then
          "ADEQUACY merge: no trial merged — the minting path was never sampled"
      if v.Refused = 0 then
          "ADEQUACY merge: no trial refused — the refusal path was never sampled"
      if v.SelfMergeStructural = 0 then
          "ADEQUACY merge: no trial changed any children — the structural agreement case was never sampled" ]

// ---------------------------------------------------------------------------
//  law 2 — the kit's lane-fold family, under this pipeline
// ---------------------------------------------------------------------------

/// The census enrolment name for this family's FABLE port — `CoreConformanceCensus.fs` carries it
/// in an `AdoptedAcross` row beside the .NET one, and enrolment there is by NAME, checked by a
/// source scan over this project. So the name lives beside the code it describes: a census string
/// that matched nothing here would be exactly the silent drift that census exists to catch.
///
/// It is also the label `Program.fs` prints, on BOTH legs, because the two legs' output is
/// byte-compared and a label that differed between them would be a divergence in the reporting
/// rather than in the algebra. The claim it names is the harness's, not one pipeline's.
let laneFoldFablePort =
    "laneFoldLawsAt certifies through the Fable law harness over the tier's reducer and codec"

/// Three lanes off one base, folded under all 3! = 6 arrival orders per trial — the same
/// instantiation `CoreDagLawTests.fs` runs on .NET, over the same witnesses.
let laneFoldResults (seed: int) (iterations: int) : Fuaran.Core.LawResult list =
    // The tier's `LaneGen` draws from `PortableRng`, so the seed is set here rather than passed
    // through the kit's rng — see the block above `PortableRng` in `TestSupport.fs`. The kit's own
    // `seed` argument still names the run in its verdicts, so a refutation stays quotable.
    PortableRng.reseed seed
    FoldConfluence.laneFoldLawsAt coreSw footprintOfEqOp hashState laneGen 3 seed iterations

/// The census enrolment name for the FABLE port of `Conformance.footprintInLawsAt` (Phase 2327),
/// printed on both legs as `laneFoldFablePort` is.
let footprintInFablePort =
    "footprintInLawsAt certifies through the Fable law harness over the tier's base-aware footprint"

/// The base-aware footprint laws over the transpiled projection: the same instantiation
/// `CoreDagLawTests.fs` runs on .NET, with the generator drawn from `PortableRng`.
let footprintInResults (seed: int) (iterations: int) : Fuaran.Core.LawResult list =
    PortableRng.reseed seed

    Fuaran.Core.Conformance.footprintInLawsAt
        coreSw
        footprintOfEqOp
        footprintInOfEqOp
        hashState
        footprintGen
        seed
        iterations

// ---------------------------------------------------------------------------
//  law 2c — the reconciliation policy is symmetric (Phase 2293)
// ---------------------------------------------------------------------------
//
//  `DagReconcile.reconcile` over two generated deltas from the base, named both
//  ways round. The policy admits pairs Core's independence theorem does not
//  cover (agreements, interleaves), so its order-independence is the POLICY's
//  rule, stated here: the two namings derive one tree, or refuse with envelopes
//  that are each other's mirror (every entry's left and right swapped, re-sorted),
//  or fail on the same fault class. The tallies print on both pipelines and are
//  byte-compared, which also certifies that the policy reaches the same verdicts
//  in a browser as on .NET.

/// One reconcile verdict, rendered so the two namings can be compared: the derived tree's
/// canonical bytes, the envelope with its sides swapped back when `mirror` is set, or the
/// fault's case name.
let private reconcileText (mirror: bool) (r: Result<Reconciled<obj>, ReconcileFault<obj>>) : string =
    match r with
    | Ok d -> "derived " + CanonicalJson.encodeNode d.Tree
    | Error(ReconcileFault.Refused rs) ->
        let rs =
            if mirror then
                rs
                |> List.map (fun x ->
                    { x with
                        Conflict =
                            { x.Conflict with
                                Left = x.Conflict.Right
                                Right = x.Conflict.Left } })
            else
                rs

        "refused " + DagReconcile.encodeEnvelope rs
    | Error(ReconcileFault.DeltaDoesNotApply _) -> "delta-does-not-apply"
    | Error(ReconcileFault.OrderSensitive _) -> "order-sensitive"
    | Error(ReconcileFault.FoldFaulted _) -> "fold-faulted"
    | Error(ReconcileFault.IntroducedDefects _) -> "introduced-defects"

/// The policy's symmetry over `iterations` generated delta pairs: the tally lines and the number
/// of pairs whose two namings disagree.
let reconcileSymmetry (seed: int) (iterations: int) : string list * int =
    let b = unwrap baseTree
    PortableRng.reseed seed
    let mutable derived = 0
    let mutable refused = 0
    let mutable other = 0
    let mutable asymmetric = 0

    for _ in 1..iterations do
        let a = genEdit ()
        let c = genEdit ()

        match applyEdit a b, applyEdit c b with
        | Some _, Some _ ->
            let ac = DagReconcile.reconcile b a c
            let ca = DagReconcile.reconcile b c a

            if reconcileText false ac <> reconcileText true ca then
                asymmetric <- asymmetric + 1

            match ac with
            | Ok _ -> derived <- derived + 1
            | Error(ReconcileFault.Refused _) -> refused <- refused + 1
            | Error _ -> other <- other + 1
        | _ -> ()

    // One `MERGE ` line: the stage compares only lines of its declared shapes, so a line of
    // any other prefix would print on both pipelines and be compared on neither.
    [ sprintf "MERGE reconcile derived=%d refused=%d other=%d asymmetric=%d" derived refused other asymmetric ],
    asymmetric

// ---------------------------------------------------------------------------
//  law 3 — the raw-DEFLATE inflater, over a FOREIGN dynamic-Huffman stream
// ---------------------------------------------------------------------------
//
//  `Deflate.inflate` is the receiving half of the teleport bundle codec, and it
//  runs in a browser: a shared link is decoded by the JavaScript this tier is
//  compiled to. Its .NET leg has had a conformance cross-check since Phase 437
//  — `CompressionTests.fs` inflates what the BCL's `DeflateStream` emits, which
//  is where the DYNAMIC-Huffman block type gets exercised — but that test sits
//  behind `#if !FABLE_COMPILER`, because `System.IO.Compression` does not
//  transpile. So the one block type no host of ours ever EMITS, and every
//  standard deflater always does, was certified on the canonical pipeline only.
//
//  The gap was not theoretical. A bundle built by a standard deflate library
//  (Python's `zlib`, and every other host's) inflated on .NET and failed in the
//  browser, which made every such share link unresumable while the .NET suite
//  stayed green.
//
//  The fixture below closes it by carrying a foreign stream as DATA — the one
//  form that needs no compression library on either pipeline.

/// Hex → bytes. Deliberately NOT `Base64Url.decode`, though this repo ships one:
/// the fixture is the INPUT to the unit under test, and decoding it through
/// another codec from the same module would let a defect there present as a
/// deflate failure.
let private ofHex (hex: string) : byte[] =
    let digit (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'A' && c <= 'F' then 10 + int c - int 'A'
        else 10 + int c - int 'a'

    Array.init (hex.Length / 2) (fun i -> byte ((digit hex[i * 2] <<< 4) ||| digit hex[i * 2 + 1]))

/// The bytes the fixture stream decodes to — a teleport-shaped envelope, chosen
/// because it is what a real share link carries.
let private foreignPayload =
    """{"v":1,"kind":"teleport","tree":{"id":"root","kind":"Stack","""
    + """"children":[{"id":"c0","kind":"Text","text":"hello teleport"},"""
    + """{"id":"c1","kind":"Text","text":"dynamic huffman"},"""
    + """{"id":"c2","kind":"Button","label":"resume"}]},"""
    + """"state":{"count":42,"tone":"Brand","open":true}}"""

/// A raw-DEFLATE stream (RFC 1951, no zlib/gzip wrapper) of `foreignPayload`,
/// emitted by a foreign deflater as ONE final DYNAMIC-Huffman block. First byte
/// `0x75` = `0111 0101`b: LSB-first that reads BFINAL=1, BTYPE=10b=2 (dynamic).
///
/// Regenerate with (Python 3, standard library only):
///
///     import zlib
///     c = zlib.compressobj(9, zlib.DEFLATED, -15)          # -15 = raw, no wrapper
///     raw = c.compress(payload) + c.flush()
///     assert (raw[0] >> 1) & 3 == 2                        # dynamic block
///     print(raw.hex().upper())
///
/// The same literal is carried by `Fuaran.UI.Tests/CompressionTests.fs` so both
/// pipelines certify the identical bytes; test projects cannot reference each
/// other, so it is copied rather than shared (the posture `TestSupport.fs`
/// records for the merge witnesses).
let private foreignDynamicHex =
    "758FB10EC2300C447FA5F29C81564C19F905D81043485D356A6A57A9834055FE9DA42A9485C9BAD3BDD3798107E85AC1"
    + "E0A8050D821E270E020A2420825EC0153F30176F4B9DC5D8214BDB3BDF0624D0D72D670F7BEA82CFB5A71C0D3D7ACFD5"
    + "B73FA90F51FF23DA1799D1D9AA8F5D371AFA419A1D394511A6ACBDB9A32F4B718E2342BA2505B318597FB01C29571E9B"
    + "5CCE84850B26F30A782AF325444CE90D"

/// Spelled out rather than `%A`-formatted, for the reason `classString` above
/// gives: a runtime's rendering of a DU case is not a contract either pipeline
/// owes the other, and this string is compared between them.
let private inflateErrString (e: Deflate.InflateError) : string =
    match e with
    | Deflate.InflateError.OutputLimit limit -> "OutputLimit " + string limit
    | Deflate.InflateError.Malformed message -> "Malformed " + message

type DeflateCase =
    { Name: string
      Passed: bool
      Detail: string }

let private inflateCase (name: string) (expected: byte[]) (stream: byte[]) : DeflateCase =
    match Deflate.inflate (expected.Length + 64) stream with
    | Error e ->
        { Name = name
          Passed = false
          Detail = "inflate refused the stream: " + inflateErrString e }
    | Ok back when back.Length <> expected.Length ->
        { Name = name
          Passed = false
          Detail = "length " + string back.Length + " expected " + string expected.Length }
    | Ok back ->
        let mismatch =
            let mutable at = -1

            for i in 0 .. expected.Length - 1 do
                if at < 0 && back[i] <> expected[i] then
                    at <- i

            at

        if mismatch < 0 then
            { Name = name
              Passed = true
              Detail = "" }
        else
            { Name = name
              Passed = false
              Detail = "byte " + string mismatch + " differs" }

/// Three cases, and the pair is the point rather than either half.
///
/// `own-fixed` is the CONTROL: this tier's own deflater emits fixed-Huffman
/// blocks, so the round trip through it exercises everything the foreign case
/// does EXCEPT the dynamic table decode. It passed on both pipelines while
/// `foreign-dynamic` failed on one, which is what localises a refutation to the
/// dynamic path instead of to the inflater at large.
///
/// `foreign-dynamic-tight` runs the same foreign stream with `maxOutput` set to
/// the exact decoded length: the bomb guard's boundary, on the block type that
/// reaches it through the length/distance copy rather than through a literal.
let deflateCases () : DeflateCase list =
    let expected = Utf8.encode foreignPayload
    let foreign = ofHex foreignDynamicHex

    [ inflateCase "foreign-dynamic" expected foreign
      inflateCase "own-fixed" expected (Deflate.compress expected)
      (match Deflate.inflate expected.Length foreign with
       | Ok back when back.Length = expected.Length ->
           { Name = "foreign-dynamic-tight"
             Passed = true
             Detail = "" }
       | Ok back ->
           { Name = "foreign-dynamic-tight"
             Passed = false
             Detail = "length " + string back.Length + " expected " + string expected.Length }
       | Error e ->
           { Name = "foreign-dynamic-tight"
             Passed = false
             Detail = "inflate refused the stream: " + inflateErrString e }) ]

let deflateLines (cases: DeflateCase list) : string list =
    let failures = cases |> List.filter (fun c -> not c.Passed)

    ("DEFLATE cases="
     + string (List.length cases)
     + " failed="
     + string (List.length failures))
    :: (failures
        |> List.map (fun c -> "DEFLATEFAIL " + c.Name + " " + sanitise c.Detail))

let deflateViolations (cases: DeflateCase list) : int =
    cases |> List.filter (fun c -> not c.Passed) |> List.length

// ---------------------------------------------------------------------------
//  Law 4 — the selection-field projection answers the same on both pipelines.
// ---------------------------------------------------------------------------
//
//  `Binding.projectSelectionField` reads a named cell off the clicked row and
//  hands it to a slot of a declared type. The two legs got there differently:
//  .NET type-tests and, where the boxed representation differs from the slot's
//  type, coerces through an invariant `Convert.ChangeType`; Fable's `unbox` is a
//  NO-OP, so it used to hand whatever was in the cell straight through. A text
//  cell in a `Binding<float>` therefore raised on .NET — the resolver's loud
//  `Errored` — and rendered `NaN` in the browser. Same tree, same data, one host
//  refusing and the other showing a nonsense number.
//
//  This is the only place that claim can be certified, because it is a claim
//  about TWO RUNTIMES and neither pipeline's own suite can see the other. Each
//  case declares its expected outcome, so a leg that drifts fails locally; and
//  the lines are byte-compared across the two runs, so a leg that drifts in a
//  way both would accept fails the diff.
//
//  The rows are `Map<string, obj>` — the Transform-produced shape a real click
//  puts in the SelectionStore.

type SelectionCase =
    { Name: string
      Passed: bool
      Outcome: string }

let private row () : Map<string, obj> =
    Map.ofList
        [ "amount", (box 42.0 |> Unchecked.nonNull)
          "amountText", (box "42" |> Unchecked.nonNull)
          "label", (box "abc" |> Unchecked.nonNull)
          "flagText", (box "true" |> Unchecked.nonNull) ]

/// Run one projection and reduce it to a two-valued outcome. Deliberately
/// `ok` / `refused` and nothing else: the exception TEXT is a host's own and
/// the two runtimes do not word it alike, so comparing it would report a
/// message difference as an algebra divergence.
let private outcomeOf (project: unit -> unit) : string =
    try
        project ()
        "ok"
    with _ ->
        "refused"

let private selectionCase (name: string) (expected: string) (project: unit -> unit) : SelectionCase =
    let actual = outcomeOf project

    { Name = name
      Passed = actual = expected
      Outcome = actual }

let selectionFieldCases () : SelectionCase list =
    let r: obj = box (row ()) |> Unchecked.nonNull

    [ // A number in a numeric slot: the ordinary case, and the one that must
      // stay untouched by any coercion added for the others.
      selectionCase "float-from-number" "ok" (fun () -> Binding.projectSelectionField<float> "amount" r |> ignore)

      // The finding, in one line: a TEXT cell in a numeric slot. .NET coerces
      // it; Fable used to pass the string through and render NaN.
      selectionCase "float-from-numeric-text" "ok" (fun () ->
          Binding.projectSelectionField<float> "amountText" r |> ignore)

      // And its refusal twin. `Number("abc")` is NaN, which is a value, not an
      // error — so a leg that merely converts and does not CHECK passes this
      // string through as a number and renders NaN.
      selectionCase "float-from-non-numeric-text" "refused" (fun () ->
          Binding.projectSelectionField<float> "label" r |> ignore)

      // The other direction: a number in a text slot renders as its own
      // decimal spelling rather than as an unconverted number object.
      selectionCase "string-from-number" "ok" (fun () -> Binding.projectSelectionField<string> "amount" r |> ignore)

      selectionCase "bool-from-text" "ok" (fun () -> Binding.projectSelectionField<bool> "flagText" r |> ignore)

      // `obj` is what every DECODED path asks for, and it must coerce nothing
      // at all — the wire path's behaviour is unchanged by any of the above.
      selectionCase "obj-passthrough" "ok" (fun () -> Binding.projectSelectionField<obj> "label" r |> ignore)

      // The two structural refusals, which both legs already agreed on and
      // which are here so a change to the coercion cannot quietly swallow them.
      selectionCase "missing-field" "refused" (fun () -> Binding.projectSelectionField<obj> "nope" r |> ignore)

      // `Unchecked.defaultof<obj>` rather than a `null` literal: the F# 10
      // nullness checker refuses `null` at a non-nullable `obj`, and
      // `Unchecked.unbox` is outside Fable's supported subset.
      selectionCase "null-row" "refused" (fun () ->
          Binding.projectSelectionField<obj> "amount" Unchecked.defaultof<obj> |> ignore) ]

let selectionFieldLines (cases: SelectionCase list) : string list =
    let failures = cases |> List.filter (fun c -> not c.Passed)

    // Every case's OUTCOME is printed, not just the failures: the two runs are
    // byte-compared, so a divergence both legs individually accept is caught
    // only if the per-case answer is in the output.
    ("SELECTIONFIELD cases="
     + string (List.length cases)
     + " failed="
     + string (List.length failures))
    :: (cases |> List.map (fun c -> "SELECTIONFIELD " + c.Name + " " + c.Outcome))

let selectionFieldViolations (cases: SelectionCase list) : int =
    cases |> List.filter (fun c -> not c.Passed) |> List.length


// ---------------------------------------------------------------------------
//  Law 5 -- a `Format.DateTime` slot never throws, on either pipeline.
// ---------------------------------------------------------------------------
//
//  The wire admits every double a JSON number can spell, `NaN` and the
//  infinities included, and a `Format.DateTime` slot then receives one. The two
//  hosts failed differently and both failed badly: `FromUnixTimeSeconds` THREW
//  out of an SSR render pass, so one bad cell took the whole page down with a
//  500, and `Intl.DateTimeFormat` raised on an invalid time value in the
//  browser. Neither is a rendering.
//
//  Certified here rather than in either tier's own suite for the same reason
//  law 4 is: the claim is that TWO RUNTIMES answer alike, and each pipeline's
//  suite can only see its own. The bounds are the narrower, .NET pair, so a
//  value JavaScript could have drawn and .NET could not is refused on both --
//  which is the divergence, not a limitation.

type DateSentinelCase =
    { Name: string
      Passed: bool
      Outcome: string }

/// Format one value and reduce it to a three-valued outcome. `threw` is a
/// failure however it is spelt: the point of the guard is that neither runtime
/// is ever asked a question it answers with an exception.
let private dateOutcome (value: float) : string =
    try
        let rendered =
            Formatting.format "en-GB" (Format.DateTime(Some DateStyle.Short, None)) value

        if rendered = Formatting.unrepresentableInstant then
            "refused"
        else
            "rendered"
    with _ ->
        "threw"

let private dateCase (name: string) (expected: string) (value: float) : DateSentinelCase =
    let actual = dateOutcome value

    { Name = name
      Passed = actual = expected
      Outcome = actual }

let dateSentinelCases () : DateSentinelCase list =
    [ // The three float sentinels the wire admits and no calendar can hold.
      dateCase "nan" "refused" nan
      dateCase "positive-infinity" "refused" infinity
      dateCase "negative-infinity" "refused" (-infinity)

      // Outside `DateTimeOffset`'s span in both directions. The second is the
      // value from the finding: a large positive double that reads as a
      // plausible timestamp and is not one.
      dateCase "far-future" "refused" 1e15
      dateCase "far-past" "refused" -1e15

      // The bounds themselves, which must be INSIDE. A guard that is off by one
      // second at either end silently refuses a date that is representable.
      dateCase "min-bound" "rendered" Formatting.minInstantSeconds
      dateCase "max-bound" "rendered" Formatting.maxInstantSeconds
      dateCase "just-past-max" "refused" (Formatting.maxInstantSeconds + 1.0)
      dateCase "just-before-min" "refused" (Formatting.minInstantSeconds - 1.0)

      // And the ordinary case, which the guard must leave completely alone --
      // 2026-09-06T00:00:00Z.
      dateCase "an-ordinary-instant" "rendered" 1788652800.0
      dateCase "the-epoch" "rendered" 0.0 ]

let dateSentinelLines (cases: DateSentinelCase list) : string list =
    let failures = cases |> List.filter (fun c -> not c.Passed)

    ("DATESENTINEL cases="
     + string (List.length cases)
     + " failed="
     + string (List.length failures))
    :: (cases |> List.map (fun c -> "DATESENTINEL " + c.Name + " " + c.Outcome))

let dateSentinelViolations (cases: DateSentinelCase list) : int =
    cases |> List.filter (fun c -> not c.Passed) |> List.length

// ── Law 6: the DAG checkpoint's two refusals, on both pipelines ──────────────
//
//  Phase 1674. The de-fenced DAG stack was proved Fable-clean once, in 2026-07,
//  by transpiling it and running a harness under Node — 12/12, including both
//  checkpoint refusals. That harness lived in a scratchpad and persisted
//  nothing, so the estate's recorded false-green Fable trap stayed live for
//  this stack.
//
//  It is closed HERE rather than by the `samples/dag-fable-smoke` entry project
//  the finding suggested, because the marginal coverage of a second entry
//  project was MEASURED first and is zero on the compile axis: Phase 1606's
//  derived portability set already enters `Fuaran.UI.OpStream.Dag.Abstractions`
//  (graph root) and reaches every gated project (17 of 17 covered), and
//  `Dag.Merge` transpiles as a reference of THIS project. What was genuinely
//  uncovered is the RUN axis — no assertion about checkpoint behaviour has ever
//  executed under Node, because the .NET assertions are Expecto, which does not
//  transpile.
//
//  So the two refusals join the harness that already runs on both pipelines and
//  byte-compares them. That is strictly more assurance than the scratchpad run
//  had: a divergence between the pipelines in WHICH refusal is reached, or in
//  whether one is reached at all, differs line-for-line rather than passing as
//  two independently-green runs.
//
//  The three cases, in the order a reader should meet them:
//   - `honest`    — a Core checkpoint (`DagFold.checkpointAt`, Phase 2645) verifies,
//                   and the bounded replay (`DagFold.replayFrom`) equals the full
//                   replay from genesis. Without this the two refusals below could
//                   both be produced by a function that refuses everything.
//   - `tampered`  — the State is swapped for a different tree while the stored
//                   seal stays honest; `DagFold.verifyCheckpoint` refuses it.
//   - `off-spine` — state and seal are BOTH genuine; only the node moved. A
//                   state-only seal cannot see this, which is why Core's seal
//                   binds the node.

type CheckpointCase =
    { Name: string
      Outcome: string
      Passed: bool }

/// Three ops that extend the shared base tree. Each inserts a DISTINCT leaf, so
/// the three intermediate states are pairwise distinct by construction and the
/// forged-snapshot case below has a genuinely different tree to substitute.
let private ckOps: TreeOp<obj> list =
    [ TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText "k0" "one"))
      TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText "k1" "two"))
      TreeOp.InsertChild(NodeId "root", unwrap (mkLeafText "k2" "three")) ]

let checkpointCases () : CheckpointCase list =
    let initial = unwrap baseTree

    // A linear DAG: genesis -> a -> b.
    let g = lawRecord [] ckOps[0]
    let a = lawRecord [ g.Hash ] ckOps[1]
    let b = lawRecord [ a.Hash ] ckOps[2]
    let dag = DagOpRecord.toDag [ g; a; b ]

    let replayTo (head: string) : Node<obj> option =
        match DagFold.replayTo initial dag head with
        | Ok t -> Some t
        | Error _ -> None

    match replayTo g.Hash, replayTo a.Hash, replayTo b.Hash, DagFold.checkpointAt initial dag a.Hash with
    | Some atG, Some atA, Some full, Ok honest ->
        let honestOutcome =
            match DagFold.verifyCheckpoint honest dag, DagFold.replayFrom honest dag b.Hash with
            | Ok(), Ok bounded when canon bounded = canon full -> "replayed-equal-to-full"
            | Ok(), Ok _ -> "replayed-but-diverged"
            | _ -> "refused"

        // The forged state must really be a different tree, or the case
        // certifies nothing — asserted rather than assumed, as its own line.
        let forgedIsDistinct = canon atG <> canon atA

        let verdict (cp: Fuaran.Core.Dag.Checkpoint<Node<obj>>) =
            match DagFold.verifyCheckpoint cp dag with
            | Ok() -> "TRUSTED"
            | Error(Fuaran.Core.Dag.CheckpointBreak.Seal _) -> "refused-seal-mismatch"
            | Error _ -> "refused-wrong-class"

        let tamperedOutcome = verdict { honest with State = atG }

        // Genuine state, genuine seal, WRONG node: the checkpoint for `g` presented
        // as if it stood at `a`. Nothing in the state is forged.
        let offSpineOutcome =
            verdict
                { DagFold.sealAt g.Hash atG with
                    Node = a.Hash }

        [ { Name = "forged-snapshot-is-a-distinct-tree"
            Outcome = (if forgedIsDistinct then "distinct" else "IDENTICAL")
            Passed = forgedIsDistinct }
          { Name = "honest"
            Outcome = honestOutcome
            Passed = honestOutcome = "replayed-equal-to-full" }
          { Name = "tampered-snapshot"
            Outcome = tamperedOutcome
            Passed = tamperedOutcome = "refused-seal-mismatch" }
          { Name = "genuine-snapshot-moved-position"
            Outcome = offSpineOutcome
            Passed = offSpineOutcome = "refused-seal-mismatch" } ]
    | _ ->
        [ { Name = "fixture"
            Outcome = "REPLAY-FAILED"
            Passed = false } ]

let checkpointLines (cases: CheckpointCase list) : string list =
    let failures = cases |> List.filter (fun c -> not c.Passed)

    ("DAGCHECKPOINT cases="
     + string (List.length cases)
     + " failed="
     + string (List.length failures))
    :: (cases |> List.map (fun c -> "DAGCHECKPOINT " + c.Name + " " + c.Outcome))

let checkpointViolations (cases: CheckpointCase list) : int =
    cases |> List.filter (fun c -> not c.Passed) |> List.length

// ── Law 7: the CellFormat projection, on both pipelines ──────────────────────
//
//  Phase 1941. `Formatting.formatNumber` is the text a server-rendered figure
//  carries AND the text the browser re-renders it as, so the two pipelines must
//  agree byte for byte. The Fable arm IS JavaScript (`String`, `toFixed`,
//  `toPrecision`, emitted directly); the .NET arm is an exact-rational
//  re-implementation of them. This law prints every formatted text, so the
//  runner's line-by-line comparison of the two outputs is the parity check, and
//  it also holds the awkward values to their known JavaScript answers.
//
//  The sweep's values are PARSED from decimal text rather than computed, because
//  a correctly-rounded parse gives the same double on both runtimes while a
//  power-of-ten multiply need not.

type NumberFormatCase =
    { Name: string
      Passed: bool
      Outcome: string }

let private numberFormatCase (name: string) (format: CellFormat) (value: float) (expected: string) =
    let actual = Formatting.formatNumber format value

    { Name = name
      Passed = actual = expected
      Outcome = actual }

/// The formats the sweep runs each value through.
let private sweepFormats: (string * CellFormat) list =
    [ "none", CellFormat.None
      "number", CellFormat.Number(None, None)
      "fixed0", CellFormat.Number(Some 0, None)
      "fixed2", CellFormat.Number(Some 2, None)
      "fixed7", CellFormat.Number(Some 7, None)
      "percent", CellFormat.Percent(None, None)
      "sig1", CellFormat.SignificantDigits 1
      "sig3", CellFormat.SignificantDigits 3
      "sig17", CellFormat.SignificantDigits 17 ]

let numberFormatCases (seed: int) (values: int) : NumberFormatCase list =
    let known =
        [ numberFormatCase "sig3-1234" (CellFormat.SignificantDigits 3) 1234.0 "1230"
          numberFormatCase "number-1234567.5" (CellFormat.Number(None, None)) 1234567.5 "1234567.5"
          numberFormatCase "none-small" CellFormat.None 0.000001234 "0.000001234"
          numberFormatCase "none-negative-zero" CellFormat.None -0.0 "0"
          numberFormatCase "none-1e21" CellFormat.None 1e21 "1e+21"
          numberFormatCase "fixed2-tie" (CellFormat.Number(Some 2, None)) 0.125 "0.13"
          numberFormatCase "fixed2-negative-to-zero" (CellFormat.Number(Some 2, None)) -0.001 "-0.00"
          numberFormatCase "percent0-tie" (CellFormat.Percent(Some 0, None)) 0.005 "1%"
          numberFormatCase "none-nan" CellFormat.None nan "NaN"
          numberFormatCase "fixed2-infinity" (CellFormat.Number(Some 2, None)) -infinity "-Infinity" ]

    PortableRng.reseed seed

    // The sweep's texts are folded into one digest line rather than printed, so
    // the log stays readable; the runner compares the digest line like any
    // other, and a single differing character on either pipeline moves it.
    // `% 1000003` after a multiply by 31 stays far inside both runtimes' exact
    // integer range.
    let mutable digest = 0
    let mutable count = 0

    for _ in 1..values do
        let sign = if PortableRng.below 2 = 0 then "" else "-"
        let digits = string (PortableRng.below 1000000)
        let exponent = PortableRng.below 50 - 28
        let value = float (sign + digits + "e" + string exponent)

        for (_, format) in sweepFormats do
            for c in Formatting.formatNumber format value + "|" do
                digest <- (digest * 31 + int c) % 1000003

            count <- count + 1

    known
    @ [ { Name = "sweep-texts=" + string count + "-digest"
          Passed = true
          Outcome = string digest } ]

let numberFormatLines (cases: NumberFormatCase list) : string list =
    let failures = cases |> List.filter (fun c -> not c.Passed)

    ("NUMBERFORMAT cases="
     + string (List.length cases)
     + " failed="
     + string (List.length failures))
    :: (cases |> List.map (fun c -> "NUMBERFORMAT " + c.Name + " " + c.Outcome))

let numberFormatViolations (cases: NumberFormatCase list) : int =
    cases |> List.filter (fun c -> not c.Passed) |> List.length
