module Fuaran.UI.Ops.Tests.ApplyParity

// ============================================================================
//  Cross-pipeline apply-parity conformance (Phase 192) — the .NET half.
//
//  Applies every `wire-format-fixtures/ops/*` op to the shared
//  `ApplyParity.baseTree` and asserts the canonical-JSON projection of
//  each outcome against the committed golden
//  (`apply-parity.golden.json`, sibling of this file). The golden is the oracle
//  BOTH pipelines assert against:
//    - .NET (here): recompute from the corpus + assert == golden. A divergence
//      (or a corpus change not reflected in the golden) fails this test → CI.
//    - Fable (browser): `samples/apply-demo` exposes `window.__fuaranParity`;
//      the Phase-192 parity session asserts each op's Fable result == the SAME
//      golden `expected`. If the Fable float formatter / coercion ever diverged
//      from .NET, the browser assertion would fail against this golden.
//
//  The golden embeds each op's source JSON alongside its expected result, so a
//  corpus edit that changes an op's bytes fails the `op` drift-guard here until
//  the golden is regenerated (delete the file + re-run to regenerate, then
//  review + commit).
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto

/// Walk up from the test binary to the workspace-root `wire-format-fixtures/`
/// corpus (same climb the JsonDecode suite uses).
let private findCorpusRoot () : string =
    let rec climb (dir: DirectoryInfo | null) : string option =
        match dir with
        | null -> None
        | d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", "manifest.json")

            if File.Exists candidate then
                Some(Path.Combine(d.FullName, "wire-format-fixtures"))
            else
                climb d.Parent

    match climb (DirectoryInfo(AppContext.BaseDirectory)) with
    | Some root -> root
    | None ->
        failwithf
            "wire-format-fixtures/manifest.json not found walking up from %s. The apply-parity suite requires the Fuaran workspace checkout."
            AppContext.BaseDirectory

/// (fixtureName, opJson) for every `ops/*.json`, ordered by name (deterministic).
let private corpusOps () : (string * string) list =
    let opsDir = Path.Combine(findCorpusRoot (), "ops")

    Directory.GetFiles(opsDir, "*.json")
    |> Array.map (fun path ->
        let name =
            match Path.GetFileNameWithoutExtension path with
            | null -> path
            | n -> n

        name, File.ReadAllText(path).Trim())
    |> Array.sortBy fst
    |> Array.toList

let private goldenPath =
    Path.Combine(__SOURCE_DIRECTORY__, "apply-parity.golden.json")

let private serializerOptions =
    let o = JsonSerializerOptions(WriteIndented = true)
    o

/// Serialize the computed (name, op, expected) triples to the golden's exact
/// shape — a JSON array, indented, in fixture-name order.
let private serializeGolden (rows: (string * string * string) list) : string =
    let arr =
        rows
        |> List.map (fun (name, op, expected) ->
            {| name = name
               op = op
               expected = expected |})

    JsonSerializer.Serialize(arr, serializerOptions)

[<Tests>]
let tests =
    testList
        "Phase 192 — cross-pipeline apply parity"
        [ test "every corpus op's .NET apply matches the committed golden" {
              let ops = corpusOps ()

              Expect.isGreaterThan (List.length ops) 0 "no op fixtures found under wire-format-fixtures/ops"

              let computed = ops |> List.map (fun (name, op) -> name, op, ApplyParity.evalOp op)

              let computedJson = serializeGolden computed

              if not (File.Exists goldenPath) then
                  File.WriteAllText(goldenPath, computedJson)

                  failtestf
                      "apply-parity golden did not exist — generated it at %s. Review the outcomes and commit it; re-run to assert against it."
                      goldenPath
              else
                  let golden = File.ReadAllText(goldenPath).Replace("\r\n", "\n")
                  let computedNorm = computedJson.Replace("\r\n", "\n")

                  Expect.equal
                      computedNorm
                      golden
                      "The .NET apply pipeline diverged from the committed apply-parity golden. If the corpus changed intentionally, delete apply-parity.golden.json and re-run to regenerate, then review + commit."
          }

          // Per-op assertions for legible failure messages + the corpus drift
          // guard (golden's stored `op` must still match the corpus op bytes).
          test "golden op bytes still match the corpus (drift guard)" {
              if File.Exists goldenPath then
                  let ops = corpusOps () |> Map.ofList
                  use doc = JsonDocument.Parse(File.ReadAllText goldenPath)

                  for el in doc.RootElement.EnumerateArray() do
                      let name =
                          match el.GetProperty("name").GetString() with
                          | null -> ""
                          | n -> n

                      let goldenOp =
                          match el.GetProperty("op").GetString() with
                          | null -> ""
                          | o -> o

                      match Map.tryFind name ops with
                      | Some corpusOp ->
                          Expect.equal
                              goldenOp
                              corpusOp
                              (sprintf "golden op '%s' drifted from the corpus fixture — regenerate the golden" name)
                      | None -> failtestf "golden references fixture '%s' not present in the corpus" name
          } ]
