module Fuaran.UI.Tests.GeneratedLayer

// ============================================================================
//  Phase 671 step 2 — the tier-side byte-diff.
//
//  Core already proves the generated encoder equals the *corpus bytes*
//  (`IdlUiGenTests`), and the corpus is the hand-written host's own gate, so
//  generated == hand-written holds transitively. The migration recipe asks for
//  the comparison to be made **direct** on the tier side, which is what this is:
//  for one fixture, build BOTH the generated structural value and the equivalent
//  hand-written `Node<'Msg>`, encode each with its own encoder, and assert all
//  three byte-strings agree.
//
//  What this pins beyond Core's own gate:
//   - the generated module COMPILES and RUNS inside the tier, against the
//     tier's pinned `Fuaran.Core.*` packages (not Core's own project refs);
//   - `Generated.encodeNode` renders through the shared `Fuaran.Core.Canon`, so
//     it inherits the tier's key ordering / escaping / float rules rather than
//     re-implementing them;
//   - the `'Msg`-erasure boundary is real: the generated `Node` carries no
//     message type at all, and still reproduces the wire exactly.
//
//  Scaling this harness to the full 84-fixture corpus is the remainder of step 2.
// ============================================================================

open System.IO
open Expecto

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

/// Locate the workspace-root shared corpus by climbing from the test binary —
/// the same idiom `MarkdownCorpusTests` uses.
let private corpusDir () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", "nodes")

            if Directory.Exists candidate then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

let private fixture (name: string) : string option =
    corpusDir ()
    |> Option.map (fun d -> Path.Combine(d, name + ".json"))
    |> Option.filter File.Exists
    |> Option.map (File.ReadAllText >> fun s -> s.Trim())

[<Tests>]
let generatedLayerTests =
    testList
        "Phase 671 — the IDL-generated structural layer, tier-side"
        [ test "the generated encoder reproduces a corpus fixture byte-for-byte" {
              match fixture "heading-1" with
              | None -> skiptest "wire-format-fixtures/nodes/heading-1.json not found"
              | Some expected ->
                  let generated: Generated.Node =
                      Generated.mkHeading "heading-1" 2 (Generated.TextSource.Literal "Channel performance") Generated.HeadingVariant.Standard

                  Expect.equal (Generated.encodeNode generated) expected "generated encoder == corpus bytes"
          }

          test "generated and hand-written encoders agree DIRECTLY on the same fixture" {
              // The migration recipe's step 2, made direct rather than transitive.
              match fixture "heading-1" with
              | None -> skiptest "wire-format-fixtures/nodes/heading-1.json not found"
              | Some expected ->
                  let generated: Generated.Node =
                      Generated.mkHeading
                          "heading-1"
                          2
                          (Generated.TextSource.Literal "Channel performance")
                          Generated.HeadingVariant.Standard

                  let handWritten: Node<unit> =
                      Fuaran.heading
                          "heading-1"
                          { Defaults.heading with
                              Text = TextSource.Literal "Channel performance" }

                  let fromGenerated = Generated.encodeNode generated
                  let fromHandWritten = CanonicalJson.encodeNode handWritten

                  Expect.equal fromGenerated expected "generated == corpus"
                  Expect.equal fromHandWritten expected "hand-written == corpus"
                  Expect.equal fromGenerated fromHandWritten "generated == hand-written (the direct diff)"
          }

          test "the generated structural value is 'Msg-free by construction" {
              // The erasure boundary, stated as a compile-time fact: `Generated.Node`
              // takes no type parameter, so there is no message type to lose. The
              // closure slots the hand-written tier carries are erased to `unit`
              // (e.g. `Binding.Query of accessor: unit * name: string`), and the
              // encoder emits the sentinel unconditionally.
              let value: Generated.Node =
                  Generated.mkHeading "b" 2 (Generated.TextSource.Literal "x") Generated.HeadingVariant.Standard

              Expect.equal value.Id "b" "a plain structural record — no 'Msg anywhere in the type"
          } ]
