module Fuaran.UI.JsonDecode.Tests.FuzzSamples

// ============================================================================
//  Phase 101 — cross-host fuzz-sample exchange (F# half).
//
//  Two CLI modes wired through Program.fs, driving the cross-host parity
//  harness at wire-format-fixtures/conformance/property-cross-host.mjs:
//
//   --emit-fuzz-samples <dir> <count>
//       Generate <count> Node + <count> TreeOp trees from the FsCheck
//       generators, encode each to canonical JSON, write to <dir>/fsharp/.
//       These are F#-canonical bytes; the Node runner feeds them through the
//       TS codec and asserts the TS canonical form is byte-identical (the
//       F# → TS leg of the cross-host property).
//
//   --check-fuzz-samples <dir> [host]
//       Read the <host>-canonical samples written to <dir>/<host>/ (that host's
//       encoder output for the same trees), decode + re-encode each with the F#
//       codec, and assert byte-identity. This is the converse <host> → F# leg:
//       F# consumes the other host's encoder output and reproduces it
//       byte-for-byte. `host` defaults to `typescript` (the Phase 101 leg), so
//       every existing invocation is unchanged.
//
//  Together the two legs prove F# canonical == <host> canonical over the
//  generated tree-space (not just the fixed Phase 98 corpus). The generators
//  emit only the cross-host-safe value subspace (plain-decimal int53 floats
//  etc. — see Generators.fs), so a divergence here is a genuine encoder/decoder
//  asymmetry, not a documented int53-scoping limitation.
//
//  Phase 236 — the F#↔Python exchange (the Legs F/G analogue). The check leg is
//  host-parameterised rather than TS-specific because the F# side of the
//  exchange is IDENTICAL for any host: decode the sample, re-encode, compare
//  bytes. Nothing about it was ever TS-specific except the hardcoded directory
//  name, so `--check-fuzz-samples <dir> python` completes the F# half against a
//  `<dir>/python/` set the moment the Python side emits one.
//
//  THE PYTHON SIDE IS THE OUTSTANDING HALF. Python already ships a WITHIN-HOST
//  generative floor (`fuaran-py/tests/test_generative_parity.py`, >=1000 cases),
//  which proves Python's own encode∘decode is stable — not that it agrees with
//  F#. Closing the exchange needs Python to do two things this file cannot:
//    (F) consume <dir>/fsharp/ (F#-canonical, emitted below), decode + re-encode
//        through the Python codec, assert byte-identity, and WRITE its own
//        canonical output to <dir>/python/ — the mirror of what
//        conformance/property-cross-host.mjs already does for TS; and
//    (G) nothing further on the F# side — `--check-fuzz-samples <dir> python`
//        below is leg G, and it is complete.
//  Note the corpus's conformance/fuzz-samples/ directory does not exist yet: it
//  is created by the emit below, so the exchange bootstraps from a clean tree.
// ============================================================================

#nowarn "3261"

open System.IO
open FsCheck
open FsCheck.FSharp
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

/// Evaluate a list generator exactly once (MaxTest = 1) via the FsCheck
/// runner, capturing the generated batch. Reuses the same proven Check.One /
/// Prop.forAll path the coverage tests use rather than a version-fragile
/// Gen.sample overload.
let private sampleOnce (count: int) (g: Gen<'a>) : 'a list =
    let mutable captured: 'a list = []

    let prop (xs: 'a list) =
        captured <- xs
        true

    Check.One(Config.QuickThrowOnFailure.WithMaxTest(1), Prop.forAll (Arb.fromGen (Gen.listOfLength count g)) prop)
    captured

let private firstDiff (a: string) (b: string) : string =
    let n = min a.Length b.Length

    let rec loop i =
        if i >= n then
            sprintf "lengths differ: F#=%d other=%d" a.Length b.Length
        elif a[i] <> b[i] then
            sprintf "first diff at byte %d" i
        else
            loop (i + 1)

    loop 0

/// `--emit-fuzz-samples <dir> <count>` — write F#-canonical fuzz samples.
let emit (dir: string) (count: int) : int =
    let fsharpDir = Path.Combine(dir, "fsharp")

    if Directory.Exists fsharpDir then
        Directory.Delete(fsharpDir, true)

    Directory.CreateDirectory fsharpDir |> ignore

    let nodes = sampleOnce count Generators.genNode

    nodes
    |> List.iteri (fun i n ->
        File.WriteAllText(Path.Combine(fsharpDir, sprintf "node-%04d.json" i), CanonicalJson.encodeNode n))

    let ops = sampleOnce count Generators.genOp

    ops
    |> List.iteri (fun i op ->
        File.WriteAllText(Path.Combine(fsharpDir, sprintf "op-%04d.json" i), CanonicalJson.encodeOp op))

    printfn "Emitted %d node + %d op fuzz samples to %s" (List.length nodes) (List.length ops) fsharpDir
    0

/// The producing host whose samples this leg validates, keyed by the
/// `<dir>/<host>/` sub-directory name. `typescript` is the Phase 101 leg;
/// `python` is the Phase 236 exchange.
let private hostHint (host: string) : string =
    match host with
    | "typescript" ->
        "run conformance/property-cross-host.mjs first (it writes the TS-canonical samples this leg validates)"
    | "python" ->
        "the Python emitter must run first and write its canonical samples there (fuaran-py owes this half of the Phase 236 exchange — see the header of this file)"
    | _ -> "no emitter is known for this host — check the directory name"

/// `--check-fuzz-samples <dir> [host]` — the converse <host> → F# leg.
/// `host` names the `<dir>/<host>/` sub-directory and defaults to `typescript`.
let check (dir: string) (host: string) : int =
    let hostDir = Path.Combine(dir, host)

    if not (Directory.Exists hostDir) then
        eprintfn "%s sample dir not found: %s — %s" host hostDir (hostHint host)

        2
    else
        let files = Directory.GetFiles(hostDir, "*.json") |> Array.sort
        let mutable failures = 0

        for f in files do
            let name = Path.GetFileName f
            let wire = File.ReadAllText f
            let isOp = name.StartsWith "op-"

            let reencoded =
                if isOp then
                    match JsonDecode.decodeOp wire with
                    | Ok d -> Some(CanonicalJson.encodeOp d)
                    | Error e ->
                        eprintfn "DECODE FAILED %s: %A" name e
                        None
                else
                    match JsonDecode.decodeNodeObj wire with
                    | Ok d -> Some(CanonicalJson.encodeNode d)
                    | Error e ->
                        eprintfn "DECODE FAILED %s: %A" name e
                        None

            match reencoded with
            | Some r when r = wire -> ()
            | Some r ->
                failures <- failures + 1
                eprintfn "MISMATCH %s (%s→F#): %s" name host (firstDiff wire r)
            | None -> failures <- failures + 1

        if failures = 0 then
            printfn "Cross-host %s→F# leg: %d samples re-encoded byte-identically" host files.Length
            0
        else
            eprintfn "Cross-host %s→F# leg: %d/%d samples diverged" host failures files.Length
            1
