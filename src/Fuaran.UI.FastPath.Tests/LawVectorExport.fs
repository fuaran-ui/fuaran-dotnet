namespace Fuaran.UI.FastPath.Tests

// ============================================================================
//  Phase 1478 — the host-neutral export of `Conformance.capabilityLaws`'
//  sample, so the other wire hosts can run the same family over the same
//  vectors (fuaran#1482).
//
//  `capabilityLaws` is SELF-CONTAINED: it takes only `(seed, iterations)` and
//  builds its own capabilities from the seed, so "its vectors" are the
//  `(input, expected verdict)` pairs it DRAWS. `Fuaran.Core.ConfRng` is public
//  and the law's draw order is fixed — `intBelow 50` (lo), `intBelow 50`
//  (span), `intBelow 1000` (the realized capture value), three draws per
//  iteration and nothing else — so the sample is reproducible here exactly,
//  rather than being approximated by rendering the law's own `LawResult`
//  evidence. That distinction matters: a `LawResult` says a law HELD, which is
//  not something another host can re-run.
//
//  Every `expected` below is computed by CALLING the pinned kit, never by
//  restating what the law says should happen; `CoreFunctionLawTests` then
//  asserts each computed verdict is the one `capabilityLaws` demands. So the
//  file is simultaneously the record and the check — a vector that disagreed
//  with the law would fail here before it could be published.
//
//  This exporter writes ONE file: `laws/capability-laws.json`. The
//  `laws/manifest.json` beside it is CURATED BY HAND and deliberately not
//  rendered here. It indexes every family in `laws/`, and those families come
//  from more than one exporter in more than one repository — `transformLaws`
//  is exported by `Fuaran.Core`'s own suite, which takes the same posture and
//  says so. A wholesale renderer would therefore silently drop whatever it did
//  not know about, and two of them would take turns reverting each other. What
//  this exporter is entitled to assert about the index is that its own row
//  still describes the file it writes, which is what `CoreFunctionLawTests`
//  checks — structurally, never as bytes.
// ============================================================================

module LawVectorExport =

    open System.IO
    open System.Reflection
    open System.Text
    open Fuaran.Core

    /// The family directory inside the shared corpus, the file this exporter
    /// writes, and the hand-curated index beside it (read by the suite, never
    /// written here). The directory name is the interface — hosts resolve
    /// `laws/` — so it is named once here.
    let familyDirName = "laws"
    let capabilityFileName = "capability-laws.json"
    let manifestFileName = "manifest.json"

    /// The seed and sample size the exported vectors are drawn from. Declared
    /// (rather than reused from the test's own law invocation) because a host
    /// re-running the family must be able to reproduce the sample from the file
    /// alone; `CoreFunctionLawTests` pins that the F# law run over this same
    /// seed is green, so the exported sample is a sample of a PASSING run.
    let seed = 20260904

    /// Far fewer iterations than the law run (100): the vectors are a shared
    /// corpus artefact in another repo — each one carries a whole capability
    /// declaration — and a host does not need a hundred draws of the same six
    /// shapes to disagree with the reference.
    let iterations = 12

    // -----------------------------------------------------------------------
    //  reproducing the law's own draw
    // -----------------------------------------------------------------------

    /// One iteration of `capabilityLaws`' sample: the drawn value space, the
    /// capture value, and the two capabilities the law builds from them.
    type Draw =
        { Iteration: int
          Lo: int
          Hi: int
          Realized: int
          Cap: Capability
          CapB: Capability }

    let draws () : Draw list =
        let mutable rng = ConfRng.ofSeed seed

        [ for i in 0 .. iterations - 1 do
              let lo, r1 = ConfRng.intBelow 50 rng
              let span, r2 = ConfRng.intBelow 50 r1
              let hi = lo + span + 1
              let realized, r3 = ConfRng.intBelow 1000 r2
              rng <- r3

              let hole: SigEntry =
                  { Addr = "h0"
                    Name = "x"
                    Kind = "value"
                    Space = Some(IntRange(lo, hi))
                    Slot = None
                    Action = None
                    Required = true }

              let sg: Signature =
                  { Name = "cap" + string i
                    Holes = [ hole ]
                    Effect =
                      { Host = ReadsHost
                        Determinism = Random } }

              yield
                  { Iteration = i
                    Lo = lo
                    Hi = hi
                    Realized = realized
                    Cap = Capability.create ("cap-" + string i) sg (ClientIsland Pyodide)
                    CapB = Capability.create ("cap-a" + string i) sg BuildTime } ]

    // -----------------------------------------------------------------------
    //  a small deterministic JSON renderer
    // -----------------------------------------------------------------------
    //  Hand-rolled rather than `Utf8JsonWriter`, for two reasons, both about
    //  the artefact being an ORACLE rather than merely valid JSON. The
    //  writer's indented mode emits `Environment.NewLine`, so the same run
    //  would produce different bytes on Windows and Linux. And its default
    //  string encoder escapes every character outside a conservative
    //  HTML-safe set — backticks, `+`, apostrophes and em-dashes all become
    //  `\uXXXX` — which is a framework-version-dependent choice this corpus
    //  should not inherit. The escaper below is the JSON minimum and nothing
    //  more: the two structural characters, the named short escapes, and the
    //  control range. Everything else is written as itself, in UTF-8.

    let private jstr (s: string) : string =
        let sb = StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\f' -> sb.Append("\\f") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    let private jint (n: int) : string = string n

    let private jobj (members: (string * string) list) : string =
        "{ "
        + (members |> List.map (fun (k, v) -> jstr k + ": " + v) |> String.concat ", ")
        + " }"

    let private jarr (items: string list) : string = "[" + String.concat ", " items + "]"

    let private argsJson (args: (string * string) list) : string =
        args
        |> List.map (fun (addr, value) -> jobj [ "addr", jstr addr; "value", jstr value ])
        |> jarr

    /// The pinned kit's version, read from the assembly rather than a literal:
    /// the version decides what the laws do, so a file naming it from a literal
    /// could describe a kit that is not the one that produced the vectors. The
    /// `+<sha>` build metadata is dropped — it moves with every Core build, and
    /// the committed artefact must be stable across rebuilds of the same pin.
    let kitVersion () : string =
        let asm = typeof<LawResult>.Assembly

        match asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (asm.GetName().Version)
        | attr -> attr.InformationalVersion.Split('+')[0]

    // -----------------------------------------------------------------------
    //  the vectors
    // -----------------------------------------------------------------------

    /// A single exported vector: what a host is given, and what the pinned kit
    /// actually answered.
    type Vector =
        { Id: string
          Case: string
          Input: (string * string) list
          Expected: (string * string) list }

    let private renderVector (v: Vector) : string =
        jobj
            [ "id", jstr v.Id
              "case", jstr v.Case
              "input", jobj v.Input
              "expected", jobj v.Expected ]

    /// The verdict the kit gave, in host-neutral words. A refusal outside the
    /// two the law distinguishes is rendered as `unexpected` rather than as F#
    /// pretty-printing: `CoreFunctionLawTests` fails on it, so it can never
    /// reach the corpus.
    let private verdictOf (r: Result<unit, InvokeError>) : (string * string) list =
        match r with
        | Ok() -> [ "verdict", jstr "accept" ]
        | Error(ArgOutOfSpace(addr, _, _)) ->
            [ "verdict", jstr "reject"; "error", jstr "argOutOfSpace"; "addr", jstr addr ]
        | Error(UnknownArg(addr, _)) -> [ "verdict", jstr "reject"; "error", jstr "unknownArg"; "addr", jstr addr ]
        | Error _ -> [ "verdict", jstr "reject"; "error", jstr "unexpected" ]

    /// Every vector for one drawn iteration — the four properties
    /// `capabilityLaws` certifies, each as an independently runnable case.
    let vectorsFor (d: Draw) : Vector list =
        let inSpace = string d.Lo
        let outOfSpace = string (d.Hi + 1)
        let declaration = CapabilityCodec.encode d.Cap
        let acceptArgs = [ "h0", inSpace ]

        let registryIds =
            match
                Registry.empty
                |> Registry.register d.Cap
                |> Result.bind (Registry.register d.CapB)
            with
            | Ok r -> Registry.enumerate r |> List.map (fun c -> c.Id)
            | Error _ -> []

        [ { Id = sprintf "capability-%d-accept" d.Iteration
            Case = "validateArgs"
            Input = [ "capability", jstr declaration; "args", argsJson acceptArgs ]
            Expected = verdictOf (Capability.validateArgs d.Cap acceptArgs) }

          { Id = sprintf "capability-%d-out-of-space" d.Iteration
            Case = "validateArgs"
            Input = [ "capability", jstr declaration; "args", argsJson [ "h0", outOfSpace ] ]
            Expected = verdictOf (Capability.validateArgs d.Cap [ "h0", outOfSpace ]) }

          { Id = sprintf "capability-%d-unknown-arg" d.Iteration
            Case = "validateArgs"
            Input = [ "capability", jstr declaration; "args", argsJson [ "nope", inSpace ] ]
            Expected = verdictOf (Capability.validateArgs d.Cap [ "nope", inSpace ]) }

          { Id = sprintf "capability-%d-invocation-key" d.Iteration
            Case = "invocationKey"
            Input = [ "capability", jstr declaration; "args", argsJson acceptArgs ]
            Expected =
              [ "key", jstr (Capability.invocationKey d.Cap acceptArgs)
                "determinismTag", jstr (Capability.determinismTag d.Cap)
                "capturedValue", jint d.Realized ] }

          { Id = sprintf "capability-%d-declaration-round-trip" d.Iteration
            Case = "declarationRoundTrip"
            Input = [ "declaration", jstr declaration ]
            Expected =
              [ "declaration",
                jstr (
                    match CapabilityCodec.decode declaration with
                    | Ok c -> CapabilityCodec.encode c
                    | Error m -> "DECODE FAILED: " + m
                ) ] }

          { Id = sprintf "capability-%d-registry-enumerate" d.Iteration
            Case = "registryEnumerate"
            Input = [ "declarations", jarr [ jstr declaration; jstr (CapabilityCodec.encode d.CapB) ] ]
            Expected = [ "ids", jarr (registryIds |> List.map jstr) ] } ]

    let allVectors () : Vector list = draws () |> List.collect vectorsFor

    // -----------------------------------------------------------------------
    //  the rendered artefacts
    // -----------------------------------------------------------------------

    let private description =
        "The (input, expected) pairs Fuaran.Core.Conformance.capabilityLaws draws from `seed` over "
        + "`iterations` iterations, computed by calling the pinned kit. A host reproduces the sample with its own "
        + "ConfRng: per iteration draw intBelow(50) = lo, intBelow(50) = span (hi = lo + span + 1), intBelow(1000) = "
        + "the captured value, and build one capability per iteration over a single required value hole `h0` in "
        + "IntRange(lo, hi) with effect readsHost/random. `capability` and `declaration` members carry a canonical "
        + "capability declaration as a JSON STRING — decode it with the host's capability codec. `validateArgs` "
        + "vectors expect accept, or reject with a named error class and the offending address. `invocationKey` "
        + "vectors expect the effect-identity key a non-deterministic invocation is journalled under, its "
        + "determinism tag, and the value the replay must return byte-identically. `declarationRoundTrip` expects "
        + "decode-then-encode to return the input bytes. `registryEnumerate` expects id-sorted enumeration "
        + "regardless of insertion order (the declarations are given in insertion order)."

    let renderCapabilityVectors () : string =
        let sb = StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "{"
        line ("  \"family\": " + jstr "capabilityLaws" + ",")
        line ("  \"kitVersion\": " + jstr (kitVersion ()) + ",")
        line ("  \"seed\": " + jint seed + ",")
        line ("  \"iterations\": " + jint iterations + ",")
        line ("  \"description\": " + jstr description + ",")
        line "  \"vectors\": ["

        let rendered = allVectors () |> List.map renderVector
        let last = List.length rendered - 1

        rendered
        |> List.iteri (fun i v -> line ("    " + v + (if i = last then "" else ",")))

        line "  ]"
        line "}"
        sb.ToString()

    // -----------------------------------------------------------------------
    //  writing
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let capabilityPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, capabilityFileName)

    let manifestPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, manifestFileName)

    /// Write the vectors with LF endings, whatever the host platform — the
    /// corpus is byte-compared by five hosts on three operating systems.
    ///
    /// `manifest.json` is NOT written: it is the hand-curated index over every
    /// family in `laws/`, several of which come from other exporters, so no
    /// single exporter may own it. See the header.
    let write (corpusDir: string) : unit =
        Directory.CreateDirectory(familyDir corpusDir) |> ignore
        File.WriteAllText(capabilityPath corpusDir, renderCapabilityVectors ())
