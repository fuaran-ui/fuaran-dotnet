module Fuaran.UI.JsonDecode.Tests.DecoderFuzz

// ============================================================================
//  Phase 779 — decoder robustness fuzz.
//
//  The threat model's load-bearing claim is that decoding is TOTAL: a malformed
//  or hostile input yields a structured, typed error, never an exception and
//  never a hang. Until this harness that claim rested on a CURATED reject
//  corpus — inputs an author chose, which is evidence about the author's
//  imagination rather than about the decoder.
//
//  This module throws hostile bytes at the decode path instead. It is the
//  demand-side complement to the Phase 101 generative fuzzer: that one generates
//  VALID trees and asserts the encode round-trip; this one generates inputs a
//  conformant emitter would never produce and asserts the REFUSAL contract.
//
//  ── The four invariants, per input ────────────────────────────────────────
//
//   1. Totality      — `decode` returns `Ok tree` or a typed `DecodeError`.
//                      An escaping exception is a counterexample.
//   2. Termination   — it returns inside a time budget. A soft breach is a
//                      counterexample; a hard breach is a genuine hang and the
//                      watchdog kills the process rather than letting a CI job
//                      time out with nothing to show for it.
//   3. Bounded work  — allocation stays inside a budget proportional to the
//                      input. This is the guard against adversarial nesting or
//                      width amplifying a small payload into a large heap; it
//                      measures SUPER-LINEAR blow-up, not tight accounting.
//   4. Fixed point   — an accepted input's canonical form re-decodes, and
//                      re-encodes to itself: `encode(decode(encode x)) =
//                      encode x` fuzzed over the reachable accept-space rather
//                      than pinned by fixtures.
//
//  ── Why the subject is a parameter ────────────────────────────────────────
//
//  `Subject` abstracts "decode, canonically re-encode, re-decode, re-encode" so
//  the SAME invariant machinery can be pointed at a deliberately-broken stand-in
//  (`DecoderFuzzTests.fs`). A fuzz harness nobody has ever seen fail is decoration:
//  the go-red property is asserted in the suite on every run, not demonstrated
//  once by hand at authoring time and then trusted forever.
//
//  ── Determinism ──────────────────────────────────────────────────────────
//
//  Generation is driven by a self-contained SplitMix64 PRNG, not `System.Random`
//  (whose algorithm is explicitly unspecified across runtimes). Given the same
//  seed and the same `Config`, iteration N is byte-identical on every machine,
//  so `--fuzz-replay` reproduces a find without needing the payload — which
//  matters most for the one failure mode that cannot persist anything on its way
//  out: a `StackOverflowException` taking the process with it.
// ============================================================================

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

// ─── Deterministic PRNG ─────────────────────────────────────────────────────

/// SplitMix64. Chosen over `System.Random` because replayability is the whole
/// point of the seed: `Random`'s sequence is documented as implementation-
/// defined, so a repro captured on one runtime need not reproduce on another.
type Rng(seed: uint64) =
    let mutable s = if seed = 0UL then 0x9E3779B97F4A7C15UL else seed

    member _.NextU64() : uint64 =
        s <- s + 0x9E3779B97F4A7C15UL
        let mutable z = s
        z <- (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        z <- (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        z ^^^ (z >>> 31)

    /// Uniform in `[0, n)`; `0` for a non-positive `n` so no caller has to guard.
    member this.Next(n: int) : int =
        if n <= 1 then 0 else int (this.NextU64() % uint64 n)

    /// Uniform in `[lo, hi]`, inclusive.
    member this.Range(lo: int, hi: int) : int =
        if hi <= lo then lo else lo + this.Next(hi - lo + 1)

    member this.Bool() : bool = this.NextU64() % 2UL = 1UL

    member this.Pick(xs: 'a[]) : 'a = xs[this.Next xs.Length]

// ─── Subject: the invariant chain, as a swappable function ──────────────────

/// What one decode entry point did with one input. Deliberately string-typed:
/// the harness compares canonical FORMS, so it needs no access to the tree type
/// and the node and op entry points share one machinery.
type SubjectResult =
    /// The decoder refused with a typed error carrying this `Code`.
    | Refused of code: string
    /// The decoder accepted. `canonical` is the re-encoded form; `reDecoded` is
    /// what a second decode-then-encode of that form produced (`Error code` if
    /// the canonical form is itself refused — a real defect, since a decoder's
    /// own output must be re-readable).
    | Accepted of canonical: string * reDecoded: Result<string, string>

/// One decode entry point, or a deliberately-broken stand-in. `Run` is allowed —
/// required, in the self-test's case — to throw: catching is the harness's job.
type Subject =
    { Name: string
      Run: string -> SubjectResult }

let nodeSubject: Subject =
    { Name = "decodeNodeObj"
      Run =
        fun input ->
            match JsonDecode.decodeNodeObj input with
            | Error e -> Refused e.Code
            | Ok tree ->
                let canonical = CanonicalJson.encodeNode tree

                let reDecoded =
                    match JsonDecode.decodeNodeObj canonical with
                    | Error e -> Error e.Code
                    | Ok again -> Ok(CanonicalJson.encodeNode again)

                Accepted(canonical, reDecoded) }

let opSubject: Subject =
    { Name = "decodeOp"
      Run =
        fun input ->
            match JsonDecode.decodeOp input with
            | Error e -> Refused e.Code
            | Ok op ->
                let canonical = CanonicalJson.encodeOp op

                let reDecoded =
                    match JsonDecode.decodeOp canonical with
                    | Error e -> Error e.Code
                    | Ok again -> Ok(CanonicalJson.encodeOp again)

                Accepted(canonical, reDecoded) }

/// The real decode surface — BOTH public entry points, since the totality claim
/// is made about the decoder, not about one of its two doors.
let realSubjects = [ nodeSubject; opSubject ]

// ─── Verdicts ───────────────────────────────────────────────────────────────

type Verdict =
    /// The contract held: a typed refusal carrying this code.
    | Rejected of code: string
    /// The contract held: accepted, and its canonical form is a fixed point.
    | Clean
    /// Invariant 1 broken — an exception escaped the decode path.
    | Escaped of kind: string * message: string
    /// Invariant 2 broken — the decode returned, but past the soft time budget.
    | TimedOut of ms: float
    /// Invariant 3 broken — allocation past the budget for an input this size.
    | OverAllocated of bytes: int64 * budget: int64
    /// Invariant 4 broken — the decoder's own canonical output is refused.
    | CanonicalRefused of code: string
    /// Invariant 4 broken — the canonical form is not a fixed point.
    | FixedPointBroken of first: string * second: string

/// Did this verdict violate the refusal contract? `Rejected` and `Clean` are
/// both PASSES — a fuzz harness that treated refusal as failure would be
/// asserting the opposite of the claim under test.
let isCounterexample (v: Verdict) : bool =
    match v with
    | Rejected _
    | Clean -> false
    | _ -> true

/// A coarse class, used to hold a failure steady while minimising. Deliberately
/// drops the payload-specific detail (the exact message, the canonical strings):
/// a smaller input that fails the same WAY is the reduction we want, and
/// demanding byte-identical detail would refuse almost every candidate.
let verdictClass (v: Verdict) : string =
    match v with
    | Rejected _
    | Clean -> "held"
    | Escaped(kind, _) -> "escaped-" + kind
    | TimedOut _ -> "timeout"
    | OverAllocated _ -> "overallocated"
    | CanonicalRefused _ -> "canonical-refused"
    | FixedPointBroken _ -> "fixed-point-broken"

let describeVerdict (v: Verdict) : string =
    match v with
    | Rejected code -> "rejected " + code
    | Clean -> "accepted; canonical form is a fixed point"
    | Escaped(kind, message) -> sprintf "EXCEPTION ESCAPED: %s — %s" kind message
    | TimedOut ms -> sprintf "TIME BUDGET EXCEEDED: decode returned only after %.0f ms" ms
    | OverAllocated(bytes, budget) -> sprintf "ALLOCATION BUDGET EXCEEDED: %d bytes allocated, budget %d" bytes budget
    | CanonicalRefused code -> sprintf "CANONICAL FORM REFUSED: the decoder's own output re-decodes as %s" code
    | FixedPointBroken(a, b) ->
        sprintf "FIXED POINT BROKEN: first canonical form (%d chars) <> second (%d chars)" a.Length b.Length

// ─── Budgets ────────────────────────────────────────────────────────────────

type Budgets =
    {
        /// Past this, a decode that DID return is reported as a counterexample.
        SoftTimeMs: float
        /// Past this the decode has not returned at all, and the watchdog kills
        /// the process — the only way to turn a genuine hang into a report.
        HardTimeMs: float
        /// Allocation floor for an ORDINARY input: below this, no input is
        /// judged over-budget however small it was. Covers the fixed per-decode
        /// cost.
        AllocFloorBytes: int64
        /// Allowed allocation per input character, above the floor.
        AllocPerChar: int64
        /// The separate, much higher ceiling for the ONE documented
        /// super-linear path — see `isOverClosed` and the finding recorded
        /// against it. Held apart deliberately: folding it into the ordinary
        /// budget would raise the bar for every other input and quietly retire
        /// the invariant, which is the failure mode this whole harness exists
        /// to avoid.
        OverCloseFloorBytes: int64
    }

/// The shipped budgets, set from measurement rather than from taste.
///
/// The ORDINARY tier is a floor plus a per-character rate, and the two bind in
/// different places, which is worth stating because it is easy to misread the
/// pair as one number. Below about 32 KB the FLOOR is what binds: the fixed cost
/// of a decode dominates, and per-character ratios there are meaningless (a
/// 100-character input legitimately allocates a thousand times its own length).
/// Above 32 KB the RATE binds, and it is the one that catches super-linear work
/// on large inputs. The heaviest node fixture in the whole conformance corpus
/// decodes at 57 bytes per character, and the heaviest large shape found by
/// fuzzing outside the over-close class stays in the same order; 512 leaves an
/// order of magnitude, so a breach means the decoder's cost changed, not that
/// the machine was busy.
///
/// The OVER-CLOSE ceiling is not a measurement of good behaviour — it is a
/// ceiling over a known-bad one, deliberately loose so it catches an explosion
/// without pretending the current cost is acceptable. The cost itself is pinned
/// at much finer resolution by the dedicated profile test, and both tiers'
/// observed maxima are reported in every run, so raising this number could never
/// be a way to make the finding go away.
let defaultBudgets =
    { SoftTimeMs = 3000.0
      HardTimeMs = 60000.0
      AllocFloorBytes = 16L * 1024L * 1024L
      AllocPerChar = 512L
      OverCloseFloorBytes = 512L * 1024L * 1024L }

/// Is this document OVER-CLOSED — does some prefix carry more structural
/// closers than openers, ignoring brackets inside strings?
///
/// A plain, public property of a JSON document, computed here rather than read
/// out of the decoder: the harness must not depend on decoder internals to
/// decide how to judge the decoder. It is used for exactly one thing — routing
/// an input to the higher allocation ceiling, because an over-closed document
/// engages a recovery gate whose enumeration is QUADRATIC in document length.
///
/// Measured (Release, this machine), a node list with one surplus closer mid
/// document, decoding successfully in every row:
///
/// | input chars | allocated | ms |
/// |---|---|---|
/// |   899 |   0.5 MiB |  0.5 |
/// | 2 129 |   2.3 MiB |  2.8 |
/// | 4 179 |   8.1 MiB |  8.6 |
/// | 8 280 |  30.1 MiB | 22.3 |
/// |16 580 | 115.9 MiB | 81.9 |
///
/// Doubling the input roughly quadruples both. It stops there only because the
/// gate refuses outright past its closer-count bound — a cliff, not a taper. So
/// the work IS bounded and the totality claim holds; what an untrusted producer
/// controls is a ~7 000x allocation amplification inside the admitted window,
/// on the SUCCESS path, for documents a curated corpus never reaches (the whole
/// corpus tops out at 63 bytes per character).
///
/// Left unfixed deliberately. The remedy is either a lower enumeration bound —
/// which changes WHICH documents recover, against a labelled acceptance oracle
/// — or a restructured enumeration that shares parse work. Both are decisions
/// about the recovery feature's reach, not something a fuzz harness should make
/// as a side effect of finding it.
let isOverClosed (text: string) : bool =
    let mutable depth = 0
    let mutable over = false
    let mutable inString = false
    let mutable i = 0

    while i < text.Length do
        let c = text[i]

        if inString then
            if c = '\\' then
                i <- i + 1
            elif c = '"' then
                inString <- false
        elif c = '"' then
            inString <- true
        elif c = '{' || c = '[' then
            depth <- depth + 1
        elif c = '}' || c = ']' then
            depth <- depth - 1

            if depth < 0 then
                over <- true

        i <- i + 1

    over

let allocBudgetFor (b: Budgets) (input: string) : int64 =
    if isOverClosed input then
        max b.OverCloseFloorBytes (b.AllocPerChar * int64 input.Length)
    else
        max b.AllocFloorBytes (b.AllocPerChar * int64 input.Length)

// ─── The measured check ─────────────────────────────────────────────────────

/// Run one input through one subject and judge it against all four invariants.
/// Every exception is caught HERE and nowhere else, which is what makes "no
/// exception escapes" a measured property rather than a hope.
///
/// `StackOverflowException` is the one escape this cannot catch — .NET tears the
/// process down without unwinding. That is a stated boundary rather than a gap:
/// the process dying IS the red gate, and the deterministic seed plus the
/// iteration counter reproduce the input that did it.
let check (subject: Subject) (budgets: Budgets) (input: string) : Verdict =
    let before = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    let outcome =
        try
            Ok(subject.Run input)
        with ex ->
            Error(ex.GetType().Name, ex.Message)

    sw.Stop()
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
    let budget = allocBudgetFor budgets input

    match outcome with
    | Error(kind, message) -> Escaped(kind, message)
    | Ok result ->
        // Order matters: an input that both ran long AND over-allocated is
        // reported as the time breach, because that is the one an operator has
        // to act on first.
        if sw.Elapsed.TotalMilliseconds > budgets.SoftTimeMs then
            TimedOut sw.Elapsed.TotalMilliseconds
        elif allocated > budget then
            OverAllocated(allocated, budget)
        else
            match result with
            | Refused code -> Rejected code
            | Accepted(_, Error code) -> CanonicalRefused code
            | Accepted(first, Ok second) ->
                if String.Equals(first, second, StringComparison.Ordinal) then
                    Clean
                else
                    FixedPointBroken(first, second)

// ─── Corpus seeds + vocabulary ──────────────────────────────────────────────

/// Built-in seeds, so the harness is self-sufficient: the go-red self-test must
/// not depend on the conformance corpus being checked out alongside this repo in
/// order to prove that the harness can fail.
let private builtinSeeds: string[] =
    [| """{"id":"a","kind":{"$type":"Heading","level":1,"text":"x","variant":"Standard"}}"""
       """{"id":"b","kind":{"$type":"Box","children":[],"layout":{"$type":"Auto"},"role":"Group"}}"""
       """{"id":"c","kind":{"$type":"Markdown","source":"# hi"}}"""
       """{"$type":"RemoveNode","path":["a"]}"""
       """{"$type":"Batch","ops":[]}"""
       "{}"
       "[]"
       "null"
       "" |]

/// Every corpus payload the harness can find, as raw text.
///
/// READ-ONLY by construction: the fuzz never writes into the corpus. Seeds are
/// drawn from the round-trip, op, reject and lenient families alike — a REJECT
/// fixture is the most productive seed there is, since it already sits one edit
/// away from the refusal boundary the fuzz is probing.
let loadSeeds () : string[] =
    let fromCorpus =
        try
            let root = Corpus.findRoot ()

            [ "nodes"; "ops"; "reject"; "lenient" ]
            |> List.collect (fun family ->
                let dir = Path.Combine(root, family)

                if Directory.Exists dir then
                    Directory.GetFiles(dir, "*.json")
                    |> Array.filter (fun f -> not (f.EndsWith(".expected.json", StringComparison.Ordinal)))
                    |> Array.toList
                else
                    [])
            |> List.map File.ReadAllText
            |> List.toArray
        with _ ->
            // A corpus-less checkout still gets a working harness, just a
            // narrower seed pool. Silence is right here: the suite hosting this
            // is already gated on the corpus being present, so an absent corpus
            // means "self-test only", not "something went wrong".
            [||]

    Array.append builtinSeeds fromCorpus

/// The wire vocabulary the near-miss generators aim just beside. Read from the
/// corpus manifest when available, so a newly-admitted kind is fuzzed the day it
/// lands rather than whenever someone remembers to extend a literal list here.
let loadVocabulary () : string[] =
    let fallback =
        [| "Box"
           "Heading"
           "Markdown"
           "Metric"
           "Badge"
           "Form"
           "Button"
           "DataGrid"
           "Chart"
           "Custom" |]

    try
        match Corpus.loadKinds () with
        | [] -> fallback
        | kinds -> List.toArray kinds
    with _ ->
        fallback

/// One-shot cache: the vocabulary is read off disk and the generators reach for
/// it per input.
let private vocabCache: string[] option ref = ref None

let private vocabulary () : string[] =
    match vocabCache.Value with
    | Some v -> v
    | None ->
        let v = loadVocabulary ()
        vocabCache.Value <- Some v
        v

// ─── Generation ─────────────────────────────────────────────────────────────

type Config =
    {
        /// Names the stream, so a persisted repro's replay line can reconstruct
        /// the exact configuration as well as the exact seed. Without it the
        /// replay command is only approximately right, which is worse than
        /// obviously wrong.
        Name: string
        /// Cap on a generated payload's length. The bounded gate run keeps this
        /// small so the suite stays a few seconds; the long run raises it past
        /// `WireLimits.MaxStringLength` so the string bound is actually crossed.
        MaxPayloadChars: int
        /// One in this many inputs is a deliberately pathological (large) payload.
        HeavyEveryN: int
    }

let boundedConfig =
    { Name = "bounded"
      MaxPayloadChars = 48 * 1024
      HeavyEveryN = 120 }

let longConfig =
    { Name = "long"
      MaxPayloadChars = 2 * 1024 * 1024
      HeavyEveryN = 25 }

let private hostileChars: char[] =
    [| '{'
       '}'
       '['
       ']'
       '"'
       ':'
       ','
       '\\'
       '/'
       '-'
       '+'
       '.'
       'e'
       'E'
       '0'
       '9'
       'n'
       't'
       'f'
       ' '
       '\t'
       '\n'
       '\r'
       char 0x00
       char 0x7F
       char 0xFEFF
       char 0xD800 // a LONE high surrogate: valid in a .NET string, not valid UTF-16 text
       char 0xDFFF // a lone LOW surrogate — the mirror case
       char 0x2028
       'é'
       '中' |]

let private hostileTokens: string[] =
    [| "null"
       "true"
       "false"
       "{}"
       "[]"
       "\"\""
       "-0"
       "1e999"
       "-1e999"
       "1E-999"
       "NaN"
       "Infinity"
       "-Infinity"
       "0x10"
       "00"
       "01"
       "1.2.3"
       "+1"
       ".5"
       "5."
       "\\u0000"
       "\\uD800"
       "\\uFFFF"
       "\\x41"
       "\\"
       "\\\""
       "\"$type\":\"\""
       "\"$type\":null"
       "\"id\":\"\""
       "\"id\":null"
       "\"id\":[]"
       "\"kind\":\"Heading\""
       "\"children\":\"x\""
       ","
       ":"
       "["
       "]"
       "{"
       "}"
       "\""
       "'"
       "/*"
       "*/"
       "//"
       string (char 0x00)
       string (char 0xFEFF)
       string (char 0xD800)
       "\r\n" |]

/// The JSON key vocabulary a plausible-but-wrong document is assembled from —
/// REAL wire keys, so a generated near-miss reaches deep into the typed decoders
/// instead of bouncing off the first `MISSING_FIELD`. `__proto__` /
/// `constructor` are in the list because a JSON decoder is a prototype-pollution
/// surface in every host language that has one, and the corpus is shared.
let private wireKeys: string[] =
    [| "id"
       "kind"
       "$type"
       "children"
       "layout"
       "role"
       "text"
       "level"
       "variant"
       "source"
       "value"
       "label"
       "fields"
       "items"
       "columns"
       "rows"
       "onSubmit"
       "onClick"
       "required"
       "binding"
       "style"
       "props"
       "state"
       "ops"
       "path"
       "node"
       "index"
       "target"
       "name"
       "format"
       "unit"
       "min"
       "max"
       "options"
       "spec"
       "__proto__"
       "constructor"
       ""
       " " |]

let private scalarLiterals: string[] =
    [| "0"
       "-1"
       "1e308"
       "-1e308"
       "1e999"
       "3.141592653589793"
       "true"
       "false"
       "null"
       "\"\""
       "\"x\""
       "\"Standard\""
       "\"Group\""
       "9007199254740993"
       "-0.0" |]

/// A near-miss of a real vocabulary word: the class of input a model emitter
/// actually produces, and the class a curated reject corpus is worst at
/// covering, because a human writing fixtures reaches for obvious garbage.
let private nearMiss (rng: Rng) (word: string) : string =
    if word.Length = 0 then
        "x"
    else
        match rng.Next 8 with
        | 0 -> word.ToLowerInvariant()
        | 1 -> word.ToUpperInvariant()
        | 2 -> word + "s"
        | 3 -> word.Substring(0, word.Length - 1)
        | 4 -> word + " "
        | 5 -> " " + word
        | 6 -> word.Remove(rng.Next word.Length, 1)
        | _ -> word.Insert(rng.Next word.Length, string (rng.Pick hostileChars))

// ── Mutators ────────────────────────────────────────────────────────────────
//
// Each corrupts a seed payload. Named individually so a persisted repro records
// WHICH transformation produced it: a counterexample whose provenance is only
// "the fuzzer did something" is markedly harder to act on.

let private mutatorNames =
    [| "flip-char"
       "delete-span"
       "insert-token"
       "duplicate-span"
       "truncate"
       "transpose"
       "repeat-structural"
       "retype-value"
       "near-miss-type"
       "delete-key"
       "duplicate-key"
       "escape-injection"
       "prefix-junk"
       "suffix-junk" |]

/// Replace the value of a randomly-chosen `"$type":"…"` with a near-miss.
let private nearMissType (rng: Rng) (vocab: string[]) (s: string) : string =
    let marker = "\"$type\":\""
    let positions = ResizeArray<int>()
    let mutable i = s.IndexOf(marker, StringComparison.Ordinal)

    while i >= 0 do
        positions.Add i
        i <- s.IndexOf(marker, i + marker.Length, StringComparison.Ordinal)

    if positions.Count = 0 then
        // No discriminator to corrupt — append one rather than returning the
        // input untouched. A silently no-op mutator quietly shrinks the
        // effective iteration count and nothing reports that it did.
        s + "{\"$type\":\"" + nearMiss rng (rng.Pick vocab) + "\"}"
    else
        let start = positions[rng.Next positions.Count] + marker.Length
        let close = s.IndexOf('"', start)

        if close < 0 then
            s
        else
            let replacement =
                if rng.Bool() then
                    nearMiss rng (s.Substring(start, close - start))
                else
                    nearMiss rng (rng.Pick vocab)

            s.Substring(0, start) + replacement + s.Substring(close)

/// Delete a whole `"key":value` pair, approximated by cutting from the key's
/// opening quote to just past the next comma.
let private deleteKey (rng: Rng) (s: string) : string =
    let positions = ResizeArray<int>()
    let mutable i = s.IndexOf("\":", StringComparison.Ordinal)

    while i >= 0 do
        positions.Add i
        i <- s.IndexOf("\":", i + 2, StringComparison.Ordinal)

    if positions.Count = 0 then
        s
    else
        let colon = positions[rng.Next positions.Count]
        let mutable closeQuote = colon

        while closeQuote > 0 && s[closeQuote] <> '"' do
            closeQuote <- closeQuote - 1

        let mutable openQuote = closeQuote - 1

        while openQuote > 0 && s[openQuote] <> '"' do
            openQuote <- openQuote - 1

        let cutFrom = max 0 openQuote
        let comma = s.IndexOf(',', colon)
        let cutTo = if comma < 0 then min s.Length (colon + 8) else comma + 1
        s.Remove(cutFrom, min (cutTo - cutFrom) (s.Length - cutFrom))

let private mutateOnce (rng: Rng) (vocab: string[]) (cfg: Config) (s: string) : string * string =
    let name = rng.Pick mutatorNames
    let len = s.Length

    let result =
        match name with
        | "flip-char" when len > 0 ->
            let i = rng.Next len
            s.Remove(i, 1).Insert(i, string (rng.Pick hostileChars))
        | "delete-span" when len > 1 ->
            let i = rng.Next len
            s.Remove(i, min (len - i) (rng.Range(1, 8)))
        | "insert-token" -> s.Insert(rng.Next(len + 1), rng.Pick hostileTokens)
        | "duplicate-span" when len > 1 ->
            let i = rng.Next len
            let n = min (len - i) (rng.Range(1, 64))
            s.Insert(rng.Next(len + 1), s.Substring(i, n))
        | "truncate" when len > 1 -> s.Substring(0, rng.Next len)
        | "transpose" when len > 2 ->
            let i = rng.Next(len - 1)
            s.Remove(i, 2).Insert(i, String([| s[i + 1]; s[i] |]))
        | "repeat-structural" ->
            let ch = rng.Pick [| "["; "{"; "\""; "]"; "}"; "," |]
            let n = min (rng.Range(2, 4096)) (max 2 (cfg.MaxPayloadChars / 4))
            s.Insert(rng.Next(len + 1), String.replicate n ch)
        | "retype-value" when len > 0 ->
            let i = rng.Next len
            s.Remove(i, min (len - i) (rng.Range(1, 12))).Insert(i, rng.Pick scalarLiterals)
        | "near-miss-type" -> nearMissType rng vocab s
        | "delete-key" -> deleteKey rng s
        | "duplicate-key" when len > 4 ->
            // A duplicated key is a real emitter defect and a classic
            // cross-host parser divergence (first-wins vs last-wins vs refuse).
            let i = s.IndexOf('"')
            let j = if i < 0 then -1 else s.IndexOf(',', i)

            if j < 0 then
                s
            else
                s.Insert(j + 1, s.Substring(i, j - i) + ",")
        | "escape-injection" when len > 0 ->
            s.Insert(rng.Next len, rng.Pick [| "\\u"; "\\uD800"; "\\u00"; "\\"; "\\/"; "\\b\\f" |])
        | "prefix-junk" -> String(Array.init (rng.Range(1, 16)) (fun _ -> rng.Pick hostileChars)) + s
        | "suffix-junk" -> s + String(Array.init (rng.Range(1, 16)) (fun _ -> rng.Pick hostileChars))
        | _ -> s + string (rng.Pick hostileChars)

    let capped =
        if result.Length > cfg.MaxPayloadChars then
            result.Substring(0, cfg.MaxPayloadChars)
        else
            result

    name, capped

// ── Structure-aware generation ──────────────────────────────────────────────

let rec private genValue (rng: Rng) (depth: int) (sb: StringBuilder) (cfg: Config) : unit =
    if sb.Length > cfg.MaxPayloadChars then
        sb.Append("0") |> ignore
    elif depth <= 0 then
        sb.Append(rng.Pick scalarLiterals) |> ignore
    else
        match rng.Next 12 with
        | 0
        | 1
        | 2
        | 3 -> sb.Append(rng.Pick scalarLiterals) |> ignore
        | 4
        | 5
        | 6
        | 7 ->
            sb.Append('{') |> ignore
            let n = rng.Range(0, 5)

            for i in 0 .. n - 1 do
                if i > 0 then
                    sb.Append(',') |> ignore

                sb.Append('"').Append(rng.Pick wireKeys).Append("\":") |> ignore
                genValue rng (depth - 1) sb cfg

            sb.Append('}') |> ignore
        | 8
        | 9
        | 10 ->
            sb.Append('[') |> ignore
            let n = rng.Range(0, 5)

            for i in 0 .. n - 1 do
                if i > 0 then
                    sb.Append(',') |> ignore

                genValue rng (depth - 1) sb cfg

            sb.Append(']') |> ignore
        | _ ->
            // A plausible node shell around a wrong interior: the shape that
            // gets furthest into the typed decoders before it fails, and so the
            // one most likely to reach code a shallow syntax reject never does.
            sb.Append("{\"id\":\"g\",\"kind\":{\"$type\":\"") |> ignore
            sb.Append(nearMiss rng (rng.Pick(vocabulary ()))) |> ignore
            sb.Append("\",\"") |> ignore
            sb.Append(rng.Pick wireKeys).Append("\":") |> ignore
            genValue rng (depth - 1) sb cfg
            sb.Append("}}") |> ignore

/// The deliberately pathological family — depth, width and string length taken
/// past `WireLimits`. Every payload is assembled as TEXT: building one as a
/// nested F# value would overflow while CONSTRUCTING the input, which proves
/// nothing about the decoder (the lesson `LimitTests.fs` records).
let private genPathological (rng: Rng) (cfg: Config) : string =
    let cap = cfg.MaxPayloadChars

    match rng.Next 9 with
    | 0 ->
        let n = min (cap / 2) (rng.Range(64, 200000))
        String.replicate n "[" + String.replicate n "]"
    | 1 ->
        let n = min (cap / 6) (rng.Range(64, 100000))
        String.replicate n "{\"a\":" + "1" + String.replicate n "}"
    | 2 ->
        // Unterminated as well as over-deep: the depth guard must fire on the
        // way DOWN, before truncation is ever reached.
        let n = min (cap / 2) (rng.Range(64, 200000))
        String.replicate n "["
    | 3 ->
        // Deep NODE nesting rather than deep JSON — crosses `MaxDepth` while
        // staying far inside `MaxJsonDepth`, isolating the tree bound.
        let depth = rng.Range(2, 400)

        let mutable acc =
            """{"id":"leaf","kind":{"$type":"Heading","level":1,"text":"x","variant":"Standard"}}"""

        for i in 1..depth do
            if acc.Length < cap then
                acc <-
                    "{\"id\":\"n"
                    + string i
                    + "\",\"kind\":{\"$type\":\"Box\",\"children\":["
                    + acc
                    + "],\"layout\":{\"$type\":\"Auto\"},\"role\":\"Group\"}}"

        acc
    | 4 ->
        let n = min (cap / 2) (rng.Range(1000, 200000))
        "{\"id\":\"a\",\"kind\":[" + String.Join(",", Array.create n "1") + "]}"
    | 5 ->
        let n = min cap (rng.Range(1000, 1200000))

        "{\"id\":\"a\",\"kind\":{\"$type\":\"Heading\",\"level\":1,\"text\":\""
        + String.replicate n "x"
        + "\",\"variant\":\"Standard\"}}"
    | 6 ->
        let depth = rng.Range(2, 300)
        let mutable acc = """{"$type":"Batch","ops":[]}"""

        for _ in 1..depth do
            if acc.Length < cap then
                acc <- "{\"$type\":\"Batch\",\"ops\":[" + acc + "]}"

        acc
    | 7 ->
        // Escape-heavy: nearly every character an escape, so the unescape path
        // does the work rather than the structural walk.
        let n = min (cap / 6) (rng.Range(500, 100000))

        "{\"id\":\"a\",\"kind\":{\"$type\":\"Markdown\",\"source\":\""
        + String.replicate n "\\u0041"
        + "\"}}"
    | _ ->
        let n = min (cap / 4) (rng.Range(500, 50000))
        "{" + String.Join(",", Array.init n (fun i -> sprintf "\"k%d\":1" i)) + "}"

/// One generated input plus the provenance a repro needs to be actionable.
type Generated = { Payload: string; Origin: string }

/// Deterministic in `(seed, iteration, cfg)` — the replay contract. Every branch
/// draws from the same `Rng`, so ADDING a generator family renumbers the stream;
/// that is why a persisted repro carries its payload too and the replay path is
/// the backstop rather than the primary record.
let generate (rng: Rng) (seeds: string[]) (vocab: string[]) (cfg: Config) (iteration: int) : Generated =
    if iteration % cfg.HeavyEveryN = 0 then
        { Payload = genPathological rng cfg
          Origin = "pathological" }
    else
        match rng.Next 10 with
        | 0
        | 1 ->
            let sb = StringBuilder()
            genValue rng (rng.Range(1, 6)) sb cfg

            { Payload = sb.ToString()
              Origin = "structured-generation" }
        | 2 ->
            let n = rng.Range(0, 200)

            { Payload = String(Array.init n (fun _ -> rng.Pick hostileChars))
              Origin = "raw-junk" }
        | 3 ->
            // Crossover: prefix of one seed, suffix of another. Produces
            // half-valid documents no single-seed mutation reaches.
            let a = rng.Pick seeds
            let b = rng.Pick seeds
            let i = if a.Length = 0 then 0 else rng.Next a.Length
            let j = if b.Length = 0 then 0 else rng.Next b.Length

            { Payload = a.Substring(0, i) + b.Substring(j)
              Origin = "crossover" }
        | _ ->
            let steps = rng.Range(1, 4)
            let mutable acc = rng.Pick seeds
            let names = ResizeArray<string>()

            for _ in 1..steps do
                let name, next = mutateOnce rng vocab cfg acc
                acc <- next
                names.Add name

            { Payload = acc
              Origin = "mutation:" + String.Join("+", names) }

// ─── Minimisation ───────────────────────────────────────────────────────────

/// Delta-debugging by span deletion: repeatedly cut a chunk and keep the cut if
/// the input still fails the same WAY. Bounded by a candidate count AND a wall
/// clock, because the class most worth minimising (a time-budget breach) is
/// exactly the one where each probe is expensive.
let minimise (classify: string -> string) (target: string) (input: string) : string =
    let clock = Stopwatch.StartNew()
    let mutable best = input
    let mutable granularity = 2
    let mutable budget = 400
    let mutable go = true

    while go && budget > 0 && clock.Elapsed.TotalSeconds < 25.0 do
        let chunk = max 1 (best.Length / granularity)
        let mutable reduced = false
        let mutable i = 0

        while i < best.Length && budget > 0 && clock.Elapsed.TotalSeconds < 25.0 do
            let take = min chunk (best.Length - i)
            let candidate = best.Remove(i, take)
            budget <- budget - 1

            if candidate.Length > 0 && classify candidate = target then
                best <- candidate
                reduced <- true
            else
                i <- i + take

        if reduced then granularity <- max 2 (granularity / 2)
        elif chunk > 1 then granularity <- granularity * 2
        else go <- false

    best

// ─── Counterexamples + persistence ──────────────────────────────────────────

type Counterexample =
    { Subject: string
      Iteration: int
      Seed: uint64
      ConfigName: string
      Origin: string
      Verdict: Verdict
      Original: string
      Minimised: string }

/// Walk up from the test binary to the harness's own source directory, so a
/// persisted repro lands where `git status` shows it rather than inside `bin/`,
/// where the next clean build deletes it.
let reproDir () : string =
    let rec climb (dir: DirectoryInfo | null) : string option =
        match dir with
        | null -> None
        | d ->
            if File.Exists(Path.Combine(d.FullName, "Fuaran.UI.JsonDecode.Tests.fsproj")) then
                Some d.FullName
            else
                climb d.Parent

    let baseDir =
        match climb (DirectoryInfo(AppContext.BaseDirectory)) with
        | Some d -> d
        | None -> AppContext.BaseDirectory

    let dir = Path.Combine(baseDir, "fuzz-repros")
    Directory.CreateDirectory dir |> ignore
    dir

/// Persist a minimised repro plus the metadata needed to act on it, into a
/// caller-supplied directory. Returns the payload path so the failing test can
/// name it.
///
/// Split from `persist` so the writing path is unit-testable against a temp
/// directory. A repro writer that only ever runs when something has ALREADY gone
/// wrong is the worst possible place to discover a typo.
let persistTo (dir: string) (c: Counterexample) : string =
    Directory.CreateDirectory dir |> ignore

    let stem =
        sprintf "%s-%s-seed%d-iter%d" (verdictClass c.Verdict) c.Subject c.Seed c.Iteration

    let safe =
        String(
            stem.ToCharArray()
            |> Array.map (fun ch -> if Char.IsLetterOrDigit ch then ch else '-')
        )

    let payloadPath = Path.Combine(dir, safe + ".input.txt")
    File.WriteAllText(payloadPath, c.Minimised)

    let replay =
        sprintf
            "    dotnet run --project src/Fuaran.UI.JsonDecode.Tests -c Release -- --fuzz-replay %d %d %d %s"
            c.Seed
            (max 1 (c.Iteration - 1))
            (c.Iteration + 1)
            c.ConfigName

    let notes =
        String.Join(
            "\n",
            [ "# Decoder fuzz counterexample"
              ""
              sprintf "- Subject: `%s`" c.Subject
              sprintf "- Seed: `%d`" c.Seed
              sprintf "- Iteration: `%d`" c.Iteration
              sprintf "- Config: `%s`" c.ConfigName
              sprintf "- Origin: `%s`" c.Origin
              sprintf "- Verdict: %s" (describeVerdict c.Verdict)
              sprintf "- Length: %d chars original, %d minimised" c.Original.Length c.Minimised.Length
              ""
              "The minimised input is beside this file as `.input.txt`. Replay the"
              "generating stream with:"
              ""
              replay
              ""
              "Counterexample policy: fix the decoder, then land the minimised"
              "input as a permanent reject fixture, so every conformant host"
              "inherits the case rather than only this one." ]
        )

    File.WriteAllText(Path.Combine(dir, safe + ".md"), notes)
    payloadPath

/// Persist into the harness's own `fuzz-repros/` directory.
let persist (c: Counterexample) : string = persistTo (reproDir ()) c

// ─── Hang watchdog ──────────────────────────────────────────────────────────

/// Turns genuine non-termination into a report. A decode that never returns
/// cannot be interrupted (.NET has no safe thread abort), so the only honest
/// options are "kill the process with a diagnosis" and "let CI time out with
/// none". This picks the first.
type HangWatchdog(hardMs: float, describe: unit -> string) =
    let clock = Stopwatch.StartNew()
    let mutable startTicks = 0L
    let mutable stopFlag = 0

    let loop () =
        while Volatile.Read(&stopFlag) = 0 do
            Thread.Sleep 250
            let t = Interlocked.Read(&startTicks)

            if t <> 0L then
                let elapsedMs = float (clock.ElapsedTicks - t) / float Stopwatch.Frequency * 1000.0

                if elapsedMs > hardMs then
                    eprintfn "DECODER FUZZ HANG: no return after %.0f ms — %s" elapsedMs (describe ())

                    eprintfn
                        "The decode path is not interruptible; killing the process so the gate goes red WITH a diagnosis."

                    Console.Out.Flush()
                    Console.Error.Flush()
                    Environment.Exit 9

    let thread = Thread(loop)

    do thread.IsBackground <- true

    member _.Start() = thread.Start()

    /// Mark the start of a decode the watchdog should time.
    member _.Enter() =
        Interlocked.Exchange(&startTicks, clock.ElapsedTicks) |> ignore

    /// Mark the decode as returned.
    member _.Leave() =
        Interlocked.Exchange(&startTicks, 0L) |> ignore

    member _.Stop() = Volatile.Write(&stopFlag, 1)

// ─── The run ────────────────────────────────────────────────────────────────

type RunStats =
    {
        Iterations: int
        Inputs: int
        SeedCount: int
        RejectCodes: Map<string, int>
        Accepted: int
        MaxDecodeMs: float
        MaxAllocBytes: int64
        MaxAllocRatio: float
        /// The two tiers, reported separately and always. The ordinary figure is
        /// the one that says whether the decoder is well-behaved; the over-close
        /// figure is the size of the known exception. A single blended maximum
        /// would let the second hide inside the first.
        OrdinaryInputs: int
        MaxOrdinaryAllocBytes: int64
        MaxOrdinaryAllocRatio: float
        OverClosedInputs: int
        MaxOverCloseAllocBytes: int64
        MaxOverCloseAllocRatio: float
        ElapsedSeconds: float
        Seed: uint64
        Counterexamples: Counterexample list
    }

let private emptyStats (seed: uint64) =
    { Iterations = 0
      Inputs = 0
      SeedCount = 0
      RejectCodes = Map.empty
      Accepted = 0
      MaxDecodeMs = 0.0
      MaxAllocBytes = 0L
      MaxAllocRatio = 0.0
      OrdinaryInputs = 0
      MaxOrdinaryAllocBytes = 0L
      MaxOrdinaryAllocRatio = 0.0
      OverClosedInputs = 0
      MaxOverCloseAllocBytes = 0L
      MaxOverCloseAllocRatio = 0.0
      ElapsedSeconds = 0.0
      Seed = seed
      Counterexamples = [] }

/// Run `iterations` generated inputs through every subject, judging each against
/// all four invariants. `subjects` is a parameter precisely so the go-red
/// self-test drives the IDENTICAL machinery with a broken stand-in.
let run
    (subjects: Subject list)
    (budgets: Budgets)
    (cfg: Config)
    (seed: uint64)
    (iterations: int)
    (minimiseFinds: bool)
    : RunStats =
    let rng = Rng(seed)
    let seeds = loadSeeds ()
    let vocab = loadVocabulary ()
    let clock = Stopwatch.StartNew()

    let mutable stats =
        { emptyStats seed with
            SeedCount = seeds.Length }

    // Held in ref cells rather than as mutable locals because the watchdog's
    // describe callback closes over them, and F# will not capture a mutable
    // local in a closure.
    let currentLength = ref 0
    let currentIter = ref 0

    // JIT warm-up. Without it the very first decode carries the cost of jitting
    // the whole decode stack and can breach a time budget the decoder had
    // nothing to do with — a false counterexample, and the most confusing kind.
    //
    // It warms the REAL entry points, never `subjects`. Warming through the
    // caller's subjects spends a self-test mutant's firing budget on inputs
    // nobody is measuring, so the go-red proof reports "found nothing" and the
    // harness looks broken when it is the warm-up that ate the evidence. That
    // is not hypothetical: it is what the first run of this file did.
    for s in Array.truncate 8 seeds do
        for subject in realSubjects do
            try
                subject.Run s |> ignore
            with _ ->
                ()

    let watchdog =
        HangWatchdog(
            budgets.HardTimeMs,
            fun () -> sprintf "seed %d, iteration %d, input length %d" seed currentIter.Value currentLength.Value
        )

    watchdog.Start()

    try
        for i in 1..iterations do
            currentIter.Value <- i
            let g = generate rng seeds vocab cfg i
            currentLength.Value <- g.Payload.Length

            for subject in subjects do
                let before = GC.GetAllocatedBytesForCurrentThread()
                let stepClock = Stopwatch.StartNew()
                watchdog.Enter()
                let verdict = check subject budgets g.Payload
                watchdog.Leave()
                stepClock.Stop()
                let allocated = GC.GetAllocatedBytesForCurrentThread() - before

                let ratio =
                    if g.Payload.Length = 0 then
                        0.0
                    else
                        float allocated / float g.Payload.Length

                stats <-
                    { stats with
                        Inputs = stats.Inputs + 1
                        MaxDecodeMs = max stats.MaxDecodeMs stepClock.Elapsed.TotalMilliseconds
                        MaxAllocBytes = max stats.MaxAllocBytes allocated
                        MaxAllocRatio = max stats.MaxAllocRatio ratio }

                stats <-
                    if isOverClosed g.Payload then
                        { stats with
                            OverClosedInputs = stats.OverClosedInputs + 1
                            MaxOverCloseAllocBytes = max stats.MaxOverCloseAllocBytes allocated
                            MaxOverCloseAllocRatio = max stats.MaxOverCloseAllocRatio ratio }
                    else
                        { stats with
                            OrdinaryInputs = stats.OrdinaryInputs + 1
                            MaxOrdinaryAllocBytes = max stats.MaxOrdinaryAllocBytes allocated
                            MaxOrdinaryAllocRatio = max stats.MaxOrdinaryAllocRatio ratio }

                match verdict with
                | Rejected code ->
                    let n = stats.RejectCodes |> Map.tryFind code |> Option.defaultValue 0

                    stats <-
                        { stats with
                            RejectCodes = Map.add code (n + 1) stats.RejectCodes }
                | Clean ->
                    stats <-
                        { stats with
                            Accepted = stats.Accepted + 1 }
                | _ ->
                    let target = verdictClass verdict

                    let minimised =
                        if minimiseFinds then
                            minimise (fun candidate -> verdictClass (check subject budgets candidate)) target g.Payload
                        else
                            g.Payload

                    let counterexample =
                        { Subject = subject.Name
                          Iteration = i
                          Seed = seed
                          ConfigName = cfg.Name
                          Origin = g.Origin
                          Verdict = verdict
                          Original = g.Payload
                          Minimised = minimised }

                    stats <-
                        { stats with
                            Counterexamples = counterexample :: stats.Counterexamples }

            stats <- { stats with Iterations = i }
    finally
        watchdog.Stop()

    clock.Stop()

    { stats with
        ElapsedSeconds = clock.Elapsed.TotalSeconds
        Counterexamples = List.rev stats.Counterexamples }

/// A one-line human summary, shared by the gate test and the long-run CLI.
let summarise (stats: RunStats) : string =
    let codes =
        stats.RejectCodes
        |> Map.toList
        |> List.sortByDescending snd
        |> List.map (fun (code, n) -> sprintf "%s=%d" code n)
        |> String.concat " "

    sprintf
        "%d inputs (%d iterations x %d entry points) in %.1f s — accepted %d, refused [%s], %d counterexamples; max decode %.0f ms; ordinary alloc peak %d bytes (%.0f x) over %d inputs; over-closed alloc peak %d bytes (%.0f x) over %d inputs"
        stats.Inputs
        stats.Iterations
        (if stats.Iterations = 0 then
             0
         else
             stats.Inputs / stats.Iterations)
        stats.ElapsedSeconds
        stats.Accepted
        codes
        (List.length stats.Counterexamples)
        stats.MaxDecodeMs
        stats.MaxOrdinaryAllocBytes
        stats.MaxOrdinaryAllocRatio
        stats.OrdinaryInputs
        stats.MaxOverCloseAllocBytes
        stats.MaxOverCloseAllocRatio
        stats.OverClosedInputs

/// Replay a generated stream over `[fromIter, toIter]`, printing what each input
/// was and what each entry point did with it. The investigative counterpart to
/// `run`: no watchdog, so a debugger can be attached to the very hang under
/// investigation rather than racing it.
///
/// Iterations before `fromIter` are still GENERATED (and discarded) — the stream
/// is a single PRNG sequence, so skipping ahead would produce different inputs.
let replay
    (subjects: Subject list)
    (budgets: Budgets)
    (cfg: Config)
    (seed: uint64)
    (fromIter: int)
    (toIter: int)
    : int =
    let rng = Rng(seed)
    let seeds = loadSeeds ()
    let vocab = loadVocabulary ()
    let mutable found = 0

    printfn
        "Replaying seed %d, config '%s', iterations %d..%d (%d corpus seeds)"
        seed
        cfg.Name
        fromIter
        toIter
        seeds.Length

    for i in 1..toIter do
        let g = generate rng seeds vocab cfg i

        if i >= fromIter then
            printfn ""
            printfn "iteration %d — origin %s, %d chars" i g.Origin g.Payload.Length

            let preview =
                if g.Payload.Length > 300 then
                    g.Payload.Substring(0, 300) + " ...(truncated)"
                else
                    g.Payload

            printfn "  payload: %s" preview

            for subject in subjects do
                let verdict = check subject budgets g.Payload
                printfn "  %-16s %s" subject.Name (describeVerdict verdict)

                if isCounterexample verdict then
                    found <- found + 1

    found
