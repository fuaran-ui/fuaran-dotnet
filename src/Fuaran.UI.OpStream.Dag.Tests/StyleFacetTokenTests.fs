module Fuaran.UI.OpStream.Dag.Tests.StyleFacetTokenTests

// ============================================================================
//  The refusal envelope's style-facet values are CANONICAL WIRE TOKENS
//  (Phase 1521 / finding M-A9).
//
//  `MergeConflict.encodeEnvelope` is documented byte-stable across hosts — its
//  SHA-256 is the cross-host refusal hash, the determinism artefact for a
//  REFUSED structural merge. The style facets reached it through a RUNTIME
//  formatter, whose output is a property of the runtime rather than of the
//  format: .NET and Fable need not agree on it, and a TypeScript or Rust
//  replica has no equivalent to agree with at all. So the one artefact whose
//  whole job is cross-host identity was computed from a per-runtime rendering.
//
//  `TreeMerge` now renders each facet through a total token function. The
//  compiler catches a MISSING case; only a test can catch a WRONG STRING, and
//  this is that test: for every case of every style facet it drives a real
//  three-way merge that conflicts on that facet, and asserts the envelope
//  carries exactly the token the GENERATED canonical encoder emits.
//
//  The generated encoder omits a facet at its default value (the
//  omit-when-default discipline), so that one token per facet cannot be read
//  from it. Those five are named below with the same words the generator's
//  enum declaration uses, and they are the only assertions here that are not
//  differential — which is worth knowing, because they are the ones a future
//  IDL rename would leave silently wrong.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

let private styleOp (f: SemanticStyle -> SemanticStyle) (id: NodeId) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, f Defaults.style)

/// The token the GENERATED canonical encoder emits for one facet of a style —
/// or `None` when it omits the member, which is exactly when the value is that
/// facet's default.
let private generatedToken (key: string) (s: SemanticStyle) : string option =
    match Generated.encodeSemanticStyleJson s with
    | JObj members ->
        members
        |> List.tryPick (fun (k, v) ->
            match k, v with
            | k, JStr token when k = key -> Some token
            | _ -> None)
    | _ -> None

/// Drive a genuine conflict on one facet and read the two sides back out of the
/// envelope. Going through `merge3Way` rather than calling the token function
/// directly is the point: the claim under test is about what the ENVELOPE
/// carries, and a helper tested in isolation could be correct while the envelope
/// still rendered something else.
///
/// The BASE value is a parameter, and it has to be: `mergeStyleField` raises a
/// conflict only when both sides moved AWAY from the base, so a pair one of
/// whose values happens to equal the base auto-merges and asserts nothing. The
/// caller therefore supplies a third value.
let private conflictSides
    (facet: string)
    (baseValue: SemanticStyle -> SemanticStyle)
    (aValue: SemanticStyle -> SemanticStyle)
    (bValue: SemanticStyle -> SemanticStyle)
    : string * string =
    let baseTree = buildDashboard () |> applyOk (styleOp baseValue leftChildId)
    let a = baseTree |> applyOk (styleOp aValue leftChildId)
    let b = baseTree |> applyOk (styleOp bValue leftChildId)

    match TreeMerge.merge3Way baseTree a b with
    | Ok _ -> failtestf "the %s pair auto-merged; the fixture no longer conflicts" facet
    | Error conflicts ->
        match
            conflicts
            |> List.tryFind (fun c -> c.NodeId = (let (NodeId s) = leftChildId in s) && c.Facet = facet)
        with
        | None -> failtestf "no %s conflict was raised for the pair under test" facet
        | Some c ->
            match c.A, c.B with
            | Some sa, Some sb -> sa.Value, sb.Value
            | _ -> failtestf "the %s conflict carries no two-sided envelope" facet

/// One facet: its envelope key, and the cases paired so each is exercised
/// against the next. `expected` is read from the generated encoder where it
/// emits, and named here only for the default case it omits.
let private facetCases
    (facet: string)
    (key: string)
    (defaultToken: string)
    (cases: (SemanticStyle -> SemanticStyle) list)
    : Test list =
    let tokenOf (set: SemanticStyle -> SemanticStyle) =
        generatedToken key (set Defaults.style) |> Option.defaultValue defaultToken

    let arr = List.toArray cases

    arr
    |> Array.mapi (fun i av -> i, av)
    |> Array.toList
    |> List.map (fun (i, av) ->
        // Each case is exercised as the A side against its successor, over a
        // base two along — a third value, so both sides genuinely moved. Every
        // case therefore appears on both sides of some pair and as some pair's
        // base, and no facet needs a special case for its default value.
        let bv = arr[(i + 1) % arr.Length]
        let baseV = arr[(i + 2) % arr.Length]
        let expectedA = tokenOf av
        let expectedB = tokenOf bv

        testCase (sprintf "%s — %s vs %s" facet expectedA expectedB) (fun () ->
            let actualA, actualB = conflictSides facet baseV av bv

            Expect.equal
                actualA
                expectedA
                (sprintf
                    "the refusal envelope's A side for %s must be the canonical wire token the generated encoder emits"
                    facet)

            Expect.equal
                actualB
                expectedB
                (sprintf
                    "the refusal envelope's B side for %s must be the canonical wire token the generated encoder emits"
                    facet)))

[<Tests>]
let tests =
    testList
        "TreeMerge — refusal-envelope style tokens are canonical (Phase 1521)"
        [ testList
              "tone"
              (facetCases
                  "style.tone"
                  "tone"
                  "Default"
                  [ (fun s -> { s with Tone = ToneVariant.Default })
                    (fun s -> { s with Tone = ToneVariant.Subdued })
                    (fun s -> { s with Tone = ToneVariant.Brand })
                    (fun s -> { s with Tone = ToneVariant.Success })
                    (fun s -> { s with Tone = ToneVariant.Warning })
                    (fun s -> { s with Tone = ToneVariant.Critical })
                    (fun s -> { s with Tone = ToneVariant.Info }) ])
          testList
              "weight"
              (facetCases
                  "style.weight"
                  "weight"
                  "Standard"
                  [ (fun s -> { s with Weight = StyleWeight.Compact })
                    (fun s -> { s with Weight = StyleWeight.Standard })
                    (fun s -> { s with Weight = StyleWeight.Spacious }) ])
          testList
              "emphasis"
              (facetCases
                  "style.emphasis"
                  "emphasis"
                  "Normal"
                  [ (fun s -> { s with Emphasis = Emphasis.Quiet })
                    (fun s -> { s with Emphasis = Emphasis.Normal })
                    (fun s -> { s with Emphasis = Emphasis.Loud }) ])
          testList
              "role"
              (facetCases
                  "style.role"
                  "role"
                  "None"
                  [ (fun s -> { s with Role = StyleRole.None })
                    (fun s -> { s with Role = StyleRole.Eyebrow })
                    (fun s -> { s with Role = StyleRole.Data })
                    (fun s -> { s with Role = StyleRole.Lede })
                    (fun s -> { s with Role = StyleRole.Caption }) ])
          testList
              "voice"
              (facetCases
                  "style.voice"
                  "voice"
                  "Default"
                  [ (fun s -> { s with Voice = FontVoice.Default })
                    (fun s -> { s with Voice = FontVoice.Display })
                    (fun s -> { s with Voice = FontVoice.Structural }) ])
          testList
              "direction"
              (facetCases
                  "style.direction"
                  "direction"
                  "auto"
                  [ (fun s ->
                        { s with
                            Direction = TextDirection.Auto })
                    (fun s -> { s with Direction = TextDirection.Ltr })
                    (fun s -> { s with Direction = TextDirection.Rtl }) ]) ]
