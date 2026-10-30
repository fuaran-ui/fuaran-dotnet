module Fuaran.UI.JsonDecode.Tests.ApplyLimitProperties

// ============================================================================
//  Phase 1925 — no op is exempt from the decoder's bounds by assertion.
//
//  The property: for every op the generator draws, AIMED at a tree that sits
//  one level under MaxDepth, an `apply` that succeeds yields a tree the wire
//  decoder accepts with no `LIMIT_EXCEEDED`. The decoder is the oracle, so the
//  property covers every bound it applies (MaxDepth, MaxNodes, the Skeleton
//  row ceiling) without restating any of them, and an op the apply engine
//  treats as "cannot grow the tree" is held to the same check as one it does
//  not.
//
//  Aiming is what makes it non-vacuous: a generated op addresses random ids
//  and would almost always answer NodeNotFound. Every op is re-pointed at
//  nodes of the base tree — the deepest leaf, a three-deep branch, a Skeleton,
//  a Heading — and `UpdateProp` additionally draws integer-slot values on both
//  sides of the 32-bit and the row bounds. The run then asserts it both
//  applied ops and saw `LimitExceeded` refusals, so a generator that drifted
//  into reaching nothing fails rather than reading green.
// ============================================================================

open Expecto
open Fuaran.UI.Testing
open FsCheck
open FsCheck.FSharp
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

/// Root `r`, a chain `c1`..`c22` (the leaf `c22` sits at depth 23 against
/// MaxDepth 24), a branch `b` > `b1` > `b2`, and three leaves whose fields
/// carry the integer slots and a declared default.
let private baseTree: Node<obj> =
    let box (id: string) (children: string list) =
        sprintf
            """{"id":"%s","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Group"}}"""
            id
            (String.concat "," children)

    let chain =
        [ 22..-1..1 ]
        |> List.fold (fun inner i -> box (sprintf "c%d" i) (inner |> Option.toList) |> Some) None
        |> Option.get

    let branch = box "b" [ box "b1" [ box "b2" [] ] ]

    let leaves =
        [ """{"id":"sk","kind":{"$type":"Skeleton","rows":3}}"""
          """{"id":"h","kind":{"$type":"Heading","level":2,"text":"Title"}}"""
          """{"id":"co","kind":{"$type":"Callout","body":"Body","tone":"Warning"}}""" ]

    match JsonDecode.decodeNodeObj (box "r" ([ chain; branch ] @ leaves)) with
    | Ok tree -> tree
    | Error e -> failwithf "the base tree does not decode: %s" e.Message

let private nodeTargets = [ "c22"; "c20"; "c11"; "b"; "b2"; "r"; "sk"; "h"; "co" ]
let private movable = [ "b"; "b1"; "b2"; "c11"; "c21" ]
let private parents = [ "c22"; "c20"; "b2"; "r" ]

let private pick (xs: string list) : Gen<NodeId> = Gen.elements xs |> Gen.map NodeId

/// Integer-slot values either side of every bound the field rules apply.
let private propValue: Gen<PropValue> =
    Gen.elements
        [ JInt 1
          JInt 3
          JInt 10000
          JInt 10001
          JInt 2147483647
          JFloat 1e15
          JFloat 2.5
          JFloat 3.0
          JStr "Default"
          JStr "Success" ]
    |> Gen.map PropValue.Wire

let rec private aim (op: TreeOp<obj>) : Gen<TreeOp<obj>> =
    gen {
        match op with
        | TreeOp.EditNode(_, kind) ->
            let! t = pick nodeTargets
            return TreeOp.EditNode(t, kind)
        | TreeOp.UpdateProp(_, _, generated) ->
            let! t = pick [ "sk"; "h"; "co" ]
            let! path = Gen.elements [ "Rows"; "rows"; "Level"; "Tone" ]
            let! value = Gen.oneof [ propValue; Gen.constant generated ]
            return TreeOp.UpdateProp(t, path, value)
        | TreeOp.ReplaceBinding(_, slot, binding) ->
            let! t = pick nodeTargets
            return TreeOp.ReplaceBinding(t, slot, binding)
        | TreeOp.UpdateStyle(_, style) ->
            let! t = pick nodeTargets
            return TreeOp.UpdateStyle(t, style)
        | TreeOp.PatchStyle(_, delta) ->
            let! t = pick nodeTargets
            return TreeOp.PatchStyle(t, delta)
        | TreeOp.UpdateState(_, state) ->
            let! t = pick nodeTargets
            return TreeOp.UpdateState(t, state)
        | TreeOp.InsertChild(_, child) ->
            let! p = pick parents
            return TreeOp.InsertChild(p, child)
        | TreeOp.RemoveNode _ ->
            let! t = pick movable
            return TreeOp.RemoveNode t
        | TreeOp.MoveNode _ ->
            let! t = pick movable
            let! p = pick parents
            return TreeOp.MoveNode(t, p)
        | TreeOp.ReorderChildren(_, order) ->
            let! p = pick parents
            return TreeOp.ReorderChildren(p, order)
        | TreeOp.ReplaceRoot node -> return TreeOp.ReplaceRoot node
        | TreeOp.Batch inner ->
            let! aimed = inner |> List.map aim |> Gen.sequenceToList
            return TreeOp.Batch aimed
    }

let private limitCode =
    JsonDecode.DecodeErrorCode.toString JsonDecode.DecodeErrorCode.LIMIT_EXCEEDED

type private Tally =
    { Applied: int
      Limited: int
      Escaped: string list }

let private run (ops: TreeOp<obj> list) : Tally =
    ops
    |> List.fold
        (fun tally op ->
            match Apply.apply op baseTree with
            | Error err when err.Code = ApplyErrorCode.LimitExceeded ->
                { tally with
                    Limited = tally.Limited + 1 }
            | Error _ -> tally
            | Ok applied ->
                let bytes = CanonicalJson.encodeNode applied

                match JsonDecode.decodeNodeObj bytes with
                | Error e when e.Code = limitCode ->
                    { tally with
                        Applied = tally.Applied + 1
                        Escaped = sprintf "%s -> %s" (CanonicalJson.encodeOp op) e.Message :: tally.Escaped }
                | _ ->
                    { tally with
                        Applied = tally.Applied + 1 })
        { Applied = 0
          Limited = 0
          Escaped = [] }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops apply — a successful apply is within every decode bound (Phase 1925)"
        [ Lanes.slow
          <| testCase "no aimed op applies to a tree the decoder refuses with LIMIT_EXCEEDED" (fun () ->
              let sample = Gen.listOfLength 3000 (Generators.genOp |> Gen.bind aim)
              let cfg = Config.QuickThrowOnFailure.WithMaxTest(1)

              let prop (ops: TreeOp<obj> list) =
                  let tally = run ops
                  Expect.isEmpty tally.Escaped "ops that applied past a decode bound"
                  Expect.isGreaterThan tally.Applied 0 "the sample applied some ops"
                  Expect.isGreaterThan tally.Limited 0 "the sample reached the limits"
                  true

              Check.One(cfg, Prop.forAll (Arb.fromGen sample) prop))

          testCase "a Native payload is held to the row bound the fast path skips the decoder for" (fun () ->
              let op =
                  TreeOp.UpdateProp(NodeId "sk", "Rows", PropValue.Native(Unchecked.nonNull (box 10001)))

              match Apply.apply op baseTree with
              | Error err -> Expect.equal err.Code ApplyErrorCode.LimitExceeded "the refusal code"
              | Ok _ -> failtest "a native row count past the bound applied")

          testCase "a Native payload of a type no decoder reads is refused, not stringified" (fun () ->
              let op =
                  TreeOp.UpdateProp(NodeId "co", "Body", PropValue.Native(Unchecked.nonNull (box System.Guid.Empty)))

              match Apply.apply op baseTree with
              | Error err -> Expect.equal err.Code ApplyErrorCode.KindMismatch "the refusal code"
              | Ok applied -> failtestf "applied as %s" (CanonicalJson.encodeNode applied)) ]
