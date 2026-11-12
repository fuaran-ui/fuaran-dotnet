module Fuaran.UI.Ops.Benchmarks.EscapeRate

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  EscapeRate (Phase 2026) — the canonical encoder's string escape, isolated.
//
//  `opstream.encode` measures whole ops whose strings are a few characters
//  long, so the per-string escape is a small, unseparated share of it. These
//  three cases make the escape the dominant cost of one `CanonicalJson.encodeOp`
//  call, through the public entry a host calls, so a change to the escape is
//  priced without reaching a private function:
//
//   - clean — one 4,096-character string with nothing to escape: the common
//             case, where a clean-run writer copies the string whole.
//   - heavy — 4,096 characters, every fourth one escaped (`"`, `\`, a line
//             feed, U+0001 in turn): the worst case for a run-based writer.
//   - keys  — an object of 256 short keys and short values: many short strings,
//             each paying the escape's per-call overhead.
//
//  The module is Fable-clean on purpose: `escape-node/` compiles this same file
//  to JavaScript and runs it under node, so the Fable figure exists at all and
//  is taken by the same loop as the .NET one. Allocation is a .NET-only reading.
// ============================================================================

/// One escape case: a name and the op whose encode it times.
type Case = { Name: string; Op: TreeOp<unit> }

let private updateWith (value: JVal) : TreeOp<unit> =
    TreeOp.UpdateProp(NodeId "escape-target", "value", PropValue.Wire value)

let private cleanString: string = String.replicate 256 "abcdefghijklmnop"

let private heavyString: string =
    let escaped = [| "\""; "\\"; "\n"; "\u0001" |]

    String.concat "" [ for i in 0..1023 -> "abc" + escaped[i % escaped.Length] ]

let private keysObject: JVal = JObj [ for i in 0..255 -> $"k{i}", JStr $"v{i}" ]

let clean =
    { Name = "clean"
      Op = updateWith (JStr cleanString) }

let heavy =
    { Name = "heavy"
      Op = updateWith (JStr heavyString) }

let keys =
    { Name = "keys"
      Op = updateWith keysObject }

let all = [ clean; heavy; keys ]

#if FABLE_COMPILER
[<Fable.Core.Emit("performance.now()")>]
let private nowMs () : float = Fable.Core.Util.jsNative

let private allocatedBytes () : int64 = 0L
#else
let private nowMs () : float =
    float (System.Diagnostics.Stopwatch.GetTimestamp()) * 1000.0
    / float System.Diagnostics.Stopwatch.Frequency

let private allocatedBytes () : int64 =
    System.GC.GetAllocatedBytesForCurrentThread()
#endif

/// The encoded length of a case's op: printed beside every timing so a run
/// whose output moved is seen to have moved, and two runs are seen to have
/// encoded the same bytes.
let encodedLength (c: Case) : int = (CanonicalJson.encodeOp c.Op).Length

/// Mean wall-time (ns) and allocation (bytes, NaN under Fable) per encode of
/// `c`, over `iterations` calls after a warm-up of a tenth as many.
let measure (c: Case) (iterations: int) : float * float =
    let mutable sink = 0

    for _ in 1 .. max 1 (iterations / 10) do
        sink <- sink + (CanonicalJson.encodeOp c.Op).Length

    let allocBefore = allocatedBytes ()
    let started = nowMs ()

    for _ in 1..iterations do
        sink <- sink + (CanonicalJson.encodeOp c.Op).Length

    let elapsedMs = nowMs () - started
    let allocAfter = allocatedBytes ()

    // `sink` is read so the loop cannot be elided.
    if sink < 0 then
        failwith "unreachable"

    let meanNs = elapsedMs * 1_000_000.0 / float iterations

#if FABLE_COMPILER
    meanNs, nan
#else
    meanNs, float (allocAfter - allocBefore) / float iterations
#endif
