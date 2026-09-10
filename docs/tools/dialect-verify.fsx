// dialect-verify.fsx — the loss-free proof leg of the Phase 840 dialect emission.
//
// Invoked by `authoring-pack.fsx --dialect lenient` (never directly) with one
// argument: a JSONL file of `{"label":…,"canonical":…,"dialect":…}` pairs, where
// `canonical` is a corpus-derived wire document and `dialect` is the mechanically
// re-emitted §16 shorthand form of the same document. For every pair this script
// runs BOTH texts through the real canonical decoder and byte-compares the
// re-encodes:
//
//     encodeNode (decode dialect) == encodeNode (decode canonical)
//
// which is exactly §16's normalisation law (`encode(decode(x)) == encode(verbose(x))`)
// applied to the emitted artefact rather than asserted about it. A dialect block
// is admitted into the pack variant only when this equality holds, so "the
// shorthand is loss-free" is a property the generator PROVES per block, not a
// claim the appendix makes about the decoder.
//
// Requires the Release build outputs of `src/Fuaran.UI.JsonDecode.Tests` (the
// project whose bin closure carries the decoder + encoder + their Fuaran.Core
// dependencies): `dotnet build src/Fuaran.UI.JsonDecode.Tests -c Release` first.
// This is why the dialect drift gate is a separate Build-dependent FAKE target
// (`AuthoringPackDialect`) rather than part of the build-free `AuthoringPack` one.

#I "../../src/Fuaran.UI.JsonDecode.Tests/bin/Release/net10.0"
#r "Fuaran.Core.Column.dll"
#r "Fuaran.Core.DataFrame.dll"
#r "Fuaran.Core.Function.dll"
#r "Fuaran.Core.Ops.dll"
#r "Fuaran.Core.Tree.dll"
#r "Fuaran.Core.Wire.dll"
#r "Fuaran.UI.dll"
#r "Fuaran.UI.Ops.Abstractions.dll"
#r "Fuaran.UI.Ops.dll"
#r "Fuaran.UI.OpStream.Abstractions.dll"

open System.IO
open System.Text.Json
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

let pairsPath =
    match fsi.CommandLineArgs |> Array.skip 1 with
    | [| p |] when File.Exists p -> p
    | _ ->
        eprintfn "usage: dotnet fsi dialect-verify.fsx <pairs.jsonl>"
        exit 2

let mutable failures = 0
let mutable checked' = 0
// Phase 1646 — counted apart from `failures`, because a canonical side that does
// not decode is NOT a loss-free failure and reporting it as one sent two readings
// hunting a lossy normalisation that does not exist. See the summary below.
let mutable canonicalFails = 0
let verdicts = System.Collections.Generic.List<string>()

for line in File.ReadAllLines pairsPath do
    if not (System.String.IsNullOrWhiteSpace line) then
        use doc = JsonDocument.Parse line
        let root = doc.RootElement
        let str (name: string) = root.GetProperty(name).GetString()
        let label = str "label"
        let canonical = str "canonical"
        let dialect = str "dialect"
        checked' <- checked' + 1

        let verdict =
            match JsonDecode.decodeNodeObj canonical, JsonDecode.decodeNodeObj dialect with
            | Ok c, Ok d ->
                let ec = CanonicalJson.encodeNode c
                let ed = CanonicalJson.encodeNode d

                if ec <> ed then
                    eprintfn "LOSSY: %s — the dialect form decodes to a DIFFERENT value" label
                    eprintfn "  canonical re-encode: %s" (ec.Substring(0, min 200 ec.Length))
                    eprintfn "  dialect   re-encode: %s" (ed.Substring(0, min 200 ed.Length))
                    "lossy"
                else
                    "ok"
            | Error e, _ ->
                // NOT a loss-free failure, and the distinction is the whole
                // point of counting it separately (Phase 1646). The canonical
                // side did not decode, so there is no value for the dialect
                // side to differ FROM and no normalisation to accuse. On a
                // hand-tier pair the overwhelmingly likely cause is a
                // deliberately-WRONG teaching example — the pack shows several
                // emissions beside the words "this exact emission fails to
                // decode" — and the caller's advisory tier is built to fall
                // back for exactly those.
                eprintfn
                    "CANONICAL DECODE FAILED (not a loss-free failure — the canonical side does not decode; \
                     on a hand pair this is usually a deliberately-wrong teaching example): %s — %A"
                    label
                    e

                "canonical-fail"
            | _, Error e ->
                eprintfn "DIALECT DECODE REFUSED: %s — %A (not in the decoder's lenient profile)" label e
                "dialect-fail"

        if verdict = "canonical-fail" then
            canonicalFails <- canonicalFails + 1
        elif verdict <> "ok" then
            failures <- failures + 1

        verdicts.Add(label + "\t" + verdict)

// The POLICY (which failures are fatal, which fall back to the canonical form)
// belongs to the caller — authoring-pack.fsx reads this verdicts file and decides;
// see its runDialectProof. Exit 3 signals "some pair failed" without pre-empting
// that decision; a human running this directly still sees every failure printed.
File.WriteAllLines(pairsPath + ".verdicts", verdicts)

// Phase 1646 — the summary distinguishes the two classes it always recorded but
// used to add together. `1 of 40 pair(s) failed the loss-free proof` said a
// normalisation was lossy when the truth was that a teaching counter-example does
// not decode ON PURPOSE, and it was read as a defect twice, the second time by a
// Tidy-Up bundle that asked for the losing shorthand to be found or dropped.
// There was no shorthand and nothing to drop. The exit code is unchanged — the
// CALLER owns the policy, and a `canonical-fail` still has to reach it.
let unproved = failures + canonicalFails

if unproved > 0 then
    let detail =
        match failures, canonicalFails with
        | 0, n -> sprintf "%d did not decode on the CANONICAL side (see above — not a loss-free failure)" n
        | n, 0 -> sprintf "%d failed the LOSS-FREE proof" n
        | n, m ->
            sprintf
                "%d failed the LOSS-FREE proof, %d did not decode on the CANONICAL side (not a loss-free failure)"
                n
                m

    eprintfn "dialect-verify: %d of %d pair(s) unproved — %s (verdicts written)" unproved checked' detail
    exit 3
else
    printfn "dialect-verify: %d pair(s) proved loss-free through the canonical decoder" checked'
    exit 0
