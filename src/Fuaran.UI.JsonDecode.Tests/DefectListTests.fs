module Fuaran.UI.JsonDecode.Tests.DefectListTests

// ============================================================================
//  Phase 1935 — a refusal reports every independent defect (WIRE_FORMAT §29).
//
//  Three things are pinned here:
//
//   1. The CORPUS: every node reject fixture's defect list is exactly its
//      manifest `expectedDefects` (or, absent that, exactly its one expected
//      error), and `decodeNode` names the list's head. The multi-defect fixtures
//      are what a second host certifies against; this is the reference host
//      certifying them.
//   2. The corpus's own consistency: each list is in canonical order, its head
//      satisfies the fixture's single-error fields, and — for the §29 fixtures —
//      so does every other entry, which is what lets a floor-tier host pass the
//      ordinary reject leg by reporting any one of them (§29.5).
//   3. The canonical order itself (§29.3), on paths chosen to separate it from
//      the orders a host might reach for instead: string order, document order,
//      decode order.
// ============================================================================

open Expecto
open Fuaran.UI.Ops.JsonDecode

let private admitAll = Fuaran.UI.KindPolicy.DecodePolicy.admitAll

let private corpus = Corpus.load ()
let private corpusRoot = fst corpus

let private nodeRejects =
    snd corpus |> List.filter (fun e -> e.Kind = "reject" && e.Decoder = "node")

let private manifestDefects = Corpus.expectedDefects ()

/// The pre-§29 fixtures whose second defect is latent: their single-error
/// fields name the first defect only, so the "every entry satisfies them"
/// property of a §29 fixture does not hold for them, by construction.
let private latentSecondDefect =
    set
        [ "reject-color-value-not-hex"
          "reject-combobox-allowfreetext-nonbool"
          "reject-rating-max-zero"
          "reject-tokens-closed-without-suggestions"
          "reject-tokens-value-not-list" ]

let private listOf (json: string) : (string * string) list option =
    match decodeNodeWithDefects admitAll json with
    | Ok _ -> None
    | Error ds -> Some(ds |> List.map (fun d -> d.Code, d.Path))

let private orderPairs (pairs: (string * string) list) =
    pairs
    |> List.map (fun (c, p) ->
        DecodeError.create DecodeErrorCode.WRONG_TYPE p "" None
        |> fun e -> { e with Code = c })
    |> orderDefects
    |> List.map (fun e -> e.Code, e.Path)

let private corpusTest (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "defect list — %s" e.Id) (fun () ->
        let input = Corpus.readPayload corpusRoot e.InputFile

        let got =
            match listOf input with
            | Some l -> l
            | None -> failtestf "reject fixture %s decoded clean through decodeNodeWithDefects" e.Id

        match Map.tryFind e.Id manifestDefects with
        | Some expected -> Expect.equal got expected "the FULL defect list, in canonical order (§29)"
        | None ->
            Expect.equal
                got.Length
                1
                (sprintf "a fixture without expectedDefects holds exactly one defect; got %A" got)

        match decodeNode input with
        | Ok _ -> failtestf "reject fixture %s decoded clean through decodeNode" e.Id
        | Error err -> Expect.equal (err.Code, err.Path) (List.head got) "decodeNode names the list's head")

[<Tests>]
let corpusLists =
    testList
        "Fuaran.UI.Ops.JsonDecode — defect lists (WIRE_FORMAT §29) over the reject corpus"
        [ yield! nodeRejects |> List.map corpusTest

          test "every expectedDefects list is canonical, and its fixture's single-error fields hold for it" {
              Expect.isGreaterThanOrEqual manifestDefects.Count 10 "the §29 fixtures are in the corpus"

              for KeyValue(id, defects) in manifestDefects do
                  let e =
                      match nodeRejects |> List.tryFind (fun e -> e.Id = id) with
                      | Some e -> e
                      | None -> failtestf "expectedDefects on %s, which is not a node reject fixture" id

                  Expect.equal defects (orderPairs defects) (sprintf "%s: the list is in §29.3 order" id)
                  Expect.isNonEmpty defects (sprintf "%s: a list is never empty" id)

                  let legacyCode = Option.get e.ExpectedErrorCode
                  let legacyPath = Option.get e.ExpectedPath

                  let holds (c: string, p: string) =
                      c = legacyCode && p.StartsWith legacyPath

                  Expect.isTrue
                      (holds (List.head defects))
                      (sprintf "%s: the head satisfies the single-error fields" id)

                  if latentSecondDefect.Contains id then
                      Expect.isFalse
                          (List.forall holds defects)
                          (sprintf "%s: named as latent, so some entry escapes the single-error fields" id)
                  else
                      for d in defects do
                          Expect.isTrue
                              (holds d)
                              (sprintf
                                  "%s: %A escapes the single-error fields, so a floor host naming it would fail the reject leg (§29.5)"
                                  id
                                  d)
          } ]

// ─── The canonical order and the entry point (§29.1 / §29.3) ─────────────────

let private md (id: string) (text: string) =
    sprintf """{"id":"%s","kind":{"$type":"Markdown","text":%s}}""" id text

[<Tests>]
let canonicalOrder =
    testList
        "Fuaran.UI.Ops.JsonDecode — the §29.3 canonical defect order"
        [ test "an index sorts numerically, not as text" {
              Expect.equal
                  (orderPairs [ "WRONG_TYPE", "$.a[10]"; "WRONG_TYPE", "$.a[9]"; "WRONG_TYPE", "$.a[2]" ])
                  [ "WRONG_TYPE", "$.a[2]"; "WRONG_TYPE", "$.a[9]"; "WRONG_TYPE", "$.a[10]" ]
                  "2 < 9 < 10"
          }

          test "a member name sorts Ordinally — `$type` first, upper case before lower" {
              Expect.equal
                  (orderPairs [ "WRONG_TYPE", "$.k.role"; "WRONG_TYPE", "$.k.Z"; "WRONG_TYPE", "$.k.$type" ])
                  [ "WRONG_TYPE", "$.k.$type"; "WRONG_TYPE", "$.k.Z"; "WRONG_TYPE", "$.k.role" ]
                  "'$' < 'Z' < 'r' by code unit"
          }

          test "segments compare one at a time, so a member name is never compared with its sibling's tail" {
              Expect.equal
                  (orderPairs [ "WRONG_TYPE", "$.kind.layout.wrap"; "WRONG_TYPE", "$.kind.layoutX" ])
                  [ "WRONG_TYPE", "$.kind.layout.wrap"; "WRONG_TYPE", "$.kind.layoutX" ]
                  "`layout` < `layoutX` as names, whatever follows `layout`"
          }

          test "an ancestor sorts before its descendants, and a tie on path is broken by code" {
              Expect.equal
                  (orderPairs [ "WRONG_TYPE", "$.a.b"; "WRONG_TYPE", "$.a"; "MISSING_FIELD", "$.a.b" ])
                  [ "WRONG_TYPE", "$.a"; "MISSING_FIELD", "$.a.b"; "WRONG_TYPE", "$.a.b" ]
                  "path first, then code"
          }

          test "one entry per (code, path): the last constructed is kept" {
              let first = DecodeError.create DecodeErrorCode.WRONG_TYPE "$.x" "first" None
              let second = DecodeError.create DecodeErrorCode.WRONG_TYPE "$.x" "second" None
              Expect.equal (orderDefects [ first; second ]) [ second ] "the rewrite replaces what it rewrote"
          } ]

[<Tests>]
let entryPoint =
    testList
        "Fuaran.UI.Ops.JsonDecode — decodeNodeWithDefects"
        [ test "a clean document decodes exactly as through decodeNode" {
              let json = md "a" "\"hi\""

              match decodeNodeWithDefects admitAll json, decodeNode json with
              | Ok _, Ok _ -> ()
              | a, b -> failtestf "expected both to accept, got %A / %A" (Result.map ignore a) (Result.map ignore b)
          }

          test "siblings in an array are all decoded: every child's defect is reported, in index order" {
              let json =
                  sprintf
                      """{"id":"r","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[%s,%s,%s]}}"""
                      (md "a" "5")
                      (md "b" "\"ok\"")
                      (md "c" "true")

              Expect.equal
                  (listOf json)
                  (Some
                      [ "WRONG_TYPE", "$.kind.children[0].kind.text"
                        "WRONG_TYPE", "$.kind.children[2].kind.text" ])
                  "children[0] and children[2]; children[1] is clean"
          }

          test "INVALID_JSON is a one-entry list" {
              Expect.equal (listOf "{\"id\":") (Some [ "INVALID_JSON", "$" ]) "no tree to walk"
          }

          test "a refused document's first defect is what every single-error entry point returns" {
              let json = """{"id":"","kind":{"$type":"Box","children":[]}}"""

              let head =
                  match decodeNodeWithDefects admitAll json with
                  | Error(d :: _) -> d
                  | other -> failtestf "expected a refusal, got %A" other

              Expect.equal (decodeNode json |> Result.map ignore) (Error head) "decodeNode"
              Expect.equal (decodeNodeObj json |> Result.map ignore) (Error head) "decodeNodeObj"
              Expect.equal (decodeNodeWithPolicy admitAll json |> Result.map ignore) (Error head) "decodeNodeWithPolicy"

              Expect.equal
                  ((decodeNodeWithOutcome admitAll json).Result |> Result.map ignore)
                  (Error head)
                  "decodeNodeWithOutcome"
          }

          test "an abandoned attempt leaves nothing behind: a swallowed default is not a defect" {
              // A Filter binding's `defaultValue` that its slot cannot parse is
              // dropped, not refused — the attempt's error must not surface in
              // the list of the refusal the OTHER member causes.
              let json =
                  """{"id":"s","kind":{"$type":"Metric","label":"L","value":{"$type":"Filter","name":"f","defaultValue":{"x":1}},"tone":"Nope"}}"""

              Expect.equal (listOf json) (Some [ "UNKNOWN_DU_CASE", "$.kind.tone" ]) "the tone alone"
          } ]
