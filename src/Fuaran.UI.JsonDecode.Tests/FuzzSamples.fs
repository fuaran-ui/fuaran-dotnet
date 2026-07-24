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
//   --check-fuzz-samples <dir>
//       Read the TS-canonical samples the runner wrote to <dir>/typescript/
//       (TS-encoder output for the same trees), decode + re-encode each with
//       the F# codec, and assert byte-identity. This is the converse TS → F#
//       leg: F# consumes TS-encoder output and reproduces it byte-for-byte.
//
//  Together the two legs prove F# canonical == TS canonical over the generated
//  tree-space (not just the fixed Phase 98 corpus). The generators emit only
//  the cross-host-safe value subspace (plain-decimal int53 floats etc. — see
//  Generators.fs), so a divergence here is a genuine encoder/decoder asymmetry,
//  not a documented int53-scoping limitation.
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
            sprintf "lengths differ: F#=%d TS=%d" a.Length b.Length
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

/// `--check-fuzz-samples <dir>` — converse TS → F# leg.
let check (dir: string) : int =
    let tsDir = Path.Combine(dir, "typescript")

    if not (Directory.Exists tsDir) then
        eprintfn
            "typescript sample dir not found: %s — run property-cross-host.mjs first (it writes the TS-canonical samples this leg validates)"
            tsDir

        2
    else
        let files = Directory.GetFiles(tsDir, "*.json") |> Array.sort
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
                eprintfn "MISMATCH %s (TS→F#): %s" name (firstDiff wire r)
            | None -> failures <- failures + 1

        if failures = 0 then
            printfn "Cross-host TS→F# leg: %d samples re-encoded byte-identically" files.Length
            0
        else
            eprintfn "Cross-host TS→F# leg: %d/%d samples diverged" failures files.Length
            1
