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
                eprintfn "CANONICAL DECODE FAILED: %s — %A" label e
                "canonical-fail"
            | _, Error e ->
                eprintfn "DIALECT DECODE REFUSED: %s — %A (not in the decoder's lenient profile)" label e
                "dialect-fail"

        if verdict <> "ok" then
            failures <- failures + 1

        verdicts.Add(label + "\t" + verdict)

// The POLICY (which failures are fatal, which fall back to the canonical form)
// belongs to the caller — authoring-pack.fsx reads this verdicts file and decides;
// see its runDialectProof. Exit 3 signals "some pair failed" without pre-empting
// that decision; a human running this directly still sees every failure printed.
File.WriteAllLines(pairsPath + ".verdicts", verdicts)

if failures > 0 then
    eprintfn "dialect-verify: %d of %d pair(s) failed the loss-free proof (verdicts written)" failures checked'
    exit 3
else
    printfn "dialect-verify: %d pair(s) proved loss-free through the canonical decoder" checked'
    exit 0
