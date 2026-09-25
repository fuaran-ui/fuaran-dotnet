namespace Fuaran.UI.FastPath.Tests

// ============================================================================
//  The `capabilityLaws` vectors, READ — no longer written — by this tier.
//
//  Phase 1478 exported `Conformance.capabilityLaws`' sample from here, so the
//  other wire hosts could run the same family over the same vectors
//  (fuaran#1482). But the reference for Core behaviour was then produced one
//  repository and one pin away from Core: a Core change that moved a vector
//  could not re-emit it in the same change-set. Since fuaran-core Phase 235
//  Core emits `laws/capability-laws.json` itself (its `--emit-laws`, beside
//  `transform-laws.json`), the shared corpus carries a declared copy of Core's
//  file, and this tier emits NO law set.
//
//  What remains here is what every host does with that file: read it, and
//  certify the PINNED kit against it. Each vector is read back through the
//  public codecs — the declaration through `CapabilityCodec.decode`, the args as
//  plain `{addr, value}` pairs — and its recorded answer is recomputed by
//  calling the pinned kit, never trusted. The draw is still reproduced here
//  (`draws`), because `capturedValue` is the sample's own draw rather than
//  something the kit computes from the inputs, and because the file declares
//  the seed a host must be able to reproduce the sample from.
//
//  `laws/manifest.json` beside it is CURATED BY HAND: it indexes every family
//  in `laws/`, and no exporter owns it. What this tier is entitled to assert
//  about the index is that its capabilityLaws row describes the file it reads.
// ============================================================================

module LawVectorExport =

    open System.IO
    open System.Reflection
    open Fuaran.Core

    /// The family directory inside the shared corpus, the file this tier reads,
    /// and the hand-curated index beside it. The directory name is the
    /// interface — hosts resolve `laws/` — so it is named once here.
    let familyDirName = "laws"
    let capabilityFileName = "capability-laws.json"
    let manifestFileName = "manifest.json"

    /// The seed and sample size Core's file declares. Restated here only so the
    /// draw can be reproduced; the suite checks the file declares the same two.
    let seed = 20260904
    let iterations = 12

    /// The re-emit command, which is Core's now — named so a stale or missing
    /// copy's failure says where the remedy lives.
    let emitCommand =
        "in the fuaran-core repository: dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws <corpus dir>"

    // -----------------------------------------------------------------------
    //  reproducing the law's own draw
    // -----------------------------------------------------------------------

    /// One iteration of `capabilityLaws`' sample: the drawn value space, the
    /// capture value, and the two capabilities the law builds from them. The
    /// draw order is fixed — `intBelow 50` (lo), `intBelow 50` (span),
    /// `intBelow 1000` (the captured value) — and `ConfRng` is public.
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

    /// The pinned kit's version, read from the assembly: the version decides
    /// what the laws do, so it is what a copy's `kitVersion` is compared with.
    /// The `+<sha>` build metadata is dropped.
    let kitVersion () : string =
        let asm = typeof<LawResult>.Assembly

        match asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (asm.GetName().Version)
        | attr -> attr.InformationalVersion.Split('+')[0]

    // -----------------------------------------------------------------------
    //  reading Core's file
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let capabilityPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, capabilityFileName)

    let manifestPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, manifestFileName)

    let field (name: string) (v: JVal) : JVal option =
        match v with
        | JObj members -> members |> List.tryPick (fun (k, x) -> if k = name then Some x else None)
        | _ -> None

    let private str (name: string) (v: JVal) : string option =
        match field name v with
        | Some(JStr s) -> Some s
        | _ -> None

    let private argsOf (v: JVal option) : (string * string) list option =
        match v with
        | Some(JArr items) ->
            let pairs =
                items
                |> List.map (fun a ->
                    match str "addr" a, str "value" a with
                    | Some addr, Some value -> Some(addr, value)
                    | _ -> None)

            if List.forall Option.isSome pairs then
                Some(List.choose id pairs)
            else
                None
        | _ -> None

    /// The verdict members `validateArgs` answers with, in the file's words.
    let private verdictOf (r: Result<unit, InvokeError>) : (string * JVal) list =
        match r with
        | Ok() -> [ "verdict", JStr "accept" ]
        | Error(ArgOutOfSpace(addr, _, _)) ->
            [ "verdict", JStr "reject"; "error", JStr "argOutOfSpace"; "addr", JStr addr ]
        | Error(UnknownArg(addr, _)) -> [ "verdict", JStr "reject"; "error", JStr "unknownArg"; "addr", JStr addr ]
        | Error _ -> [ "verdict", JStr "reject"; "error", JStr "unexpected" ]

    /// Run one vector the way a host does, against the pinned kit, and say what
    /// disagreed with the recorded answer (or `None`).
    let checkVector (v: JVal) : string option =
        let id = str "id" v |> Option.defaultValue "<no id>"
        let input = field "input" v |> Option.defaultValue (JObj [])

        let expected =
            match field "expected" v with
            | Some(JObj ms) -> ms
            | _ -> []

        let decl (name: string) =
            match str name input with
            | None -> Error(sprintf "%s: input.%s missing" id name)
            | Some d ->
                match CapabilityCodec.decode d with
                | Ok c -> Ok(d, c)
                | Error m -> Error(sprintf "%s: input.%s did not decode (%s)" id name m)

        let differs (actual: (string * JVal) list) =
            if actual = expected then
                None
            else
                Some(sprintf "%s: the pinned kit answers %A but the vector records %A" id actual expected)

        match str "case" v, argsOf (field "args" input) with
        | Some "validateArgs", Some args ->
            match decl "capability" with
            | Error m -> Some m
            | Ok(_, c) -> differs (verdictOf (Capability.validateArgs c args))
        | Some "invocationKey", Some args ->
            match decl "capability" with
            | Error m -> Some m
            | Ok(_, c) ->
                let captured = expected |> List.filter (fun (k, _) -> k = "capturedValue")

                differs (
                    [ "key", JStr(Capability.invocationKey c args)
                      "determinismTag", JStr(Capability.determinismTag c) ]
                    @ captured
                )
        | Some "declarationRoundTrip", _ ->
            match decl "declaration" with
            | Error m -> Some m
            | Ok(d, c) ->
                let back = CapabilityCodec.encode c

                if back <> d then
                    Some(sprintf "%s: decode-then-encode did not return the input bytes" id)
                else
                    differs [ "declaration", JStr back ]
        | Some "registryEnumerate", _ ->
            match field "declarations" input with
            | Some(JArr ds) ->
                let registered =
                    ds
                    |> List.fold
                        (fun acc d ->
                            match acc, d with
                            | Error e, _ -> Error e
                            | Ok r, JStr s ->
                                match CapabilityCodec.decode s with
                                | Ok c -> Registry.register c r |> Result.mapError (sprintf "%A")
                                | Error m -> Error m
                            | Ok _, _ -> Error "a declaration is not a string")
                        (Ok Registry.empty)

                match registered with
                | Error m -> Some(sprintf "%s: the declarations did not register (%s)" id m)
                | Ok r -> differs [ "ids", JArr(Registry.enumerate r |> List.map (fun c -> JStr c.Id)) ]
            | _ -> Some(sprintf "%s: input.declarations missing" id)
        | Some other, _ -> Some(sprintf "%s: unknown case `%s`" id other)
        | None, _ -> Some(sprintf "%s: no case" id)
