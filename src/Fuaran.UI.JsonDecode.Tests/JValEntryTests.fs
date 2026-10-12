module Fuaran.UI.JsonDecode.Tests.JValEntry

// Phase 2059 — the `JVal` entry points (`decodeNodeJVal`, `decodeNodeObjJVal`,
// `decodeOpJVal`) against the route they replace.
//
// A secondary reader that holds its payload as a Core `JVal` used to
// `Canon.render` it and decode the text. The `JVal` entry lowers the value into
// the decoder's AST instead, and its contract is EQUIVALENCE with that route:
// for every value, the same tree or the same refusal — code, path and message.
// So every assertion here compares the two routes over the same value, and the
// fixed-answer checks beside them are there so an equivalence cannot be won by
// both routes refusing everything.
//
// Three populations:
//
//  1. every `node` and `op` fixture in the corpus whose bytes the substrate
//     parser reads (the ones it refuses can never reach a `JVal` entry);
//  2. the number edges — the corpus's float divergence zone and the `Int32` /
//     2^53 / sentinel / negative-zero boundaries, at a typed float slot and at
//     a rule-12 payload slot, where the number policies differ;
//  3. the structural edges `lowerJVal` must hand back to the text path: a
//     repeated member, ill-formed UTF-16, and each §21 bound at and past it.

open Expecto
open Fuaran.Core
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Testing

type private Route =
    | Node
    | Op

/// One outcome, as text that is equal exactly when the two outcomes are: the
/// canonical encoding of an accepted value, or a refusal's code, path and
/// message.
let private outcome (r: Result<'a, JsonDecode.DecodeError>) (encode: 'a -> string) : string =
    match r with
    | Ok v -> "ok " + encode v
    | Error e -> sprintf "error %s at %s: %s" e.Code e.Path e.Message

let private viaText (route: Route) (jv: JVal) : string =
    let text = Canon.render jv

    match route with
    | Node -> outcome (JsonDecode.decodeNodeObj text) CanonicalJson.encodeNode
    | Op -> outcome (JsonDecode.decodeOp text) CanonicalJson.encodeOp

let private viaJVal (route: Route) (jv: JVal) : string =
    match route with
    | Node -> outcome (JsonDecode.decodeNodeObjJVal jv) CanonicalJson.encodeNode
    | Op -> outcome (JsonDecode.decodeOpJVal jv) CanonicalJson.encodeOp

/// Assert the two routes agree, and return the shared outcome.
let private agree (label: string) (route: Route) (jv: JVal) : string =
    let expected = viaText route jv
    let actual = viaJVal route jv
    Expect.equal actual expected (sprintf "%s: the JVal entry decided differently from render-then-decode" label)
    expected

let private expectCode (label: string) (code: string) (shared: string) =
    Expect.isTrue
        (shared.StartsWith("error " + code + " "))
        (sprintf "%s: expected %s, got %s" label code (shared.Substring(0, min 160 shared.Length)))

let private expectAccepted (label: string) (shared: string) =
    Expect.isTrue
        (shared.StartsWith "ok ")
        (sprintf "%s: expected the value to decode, got %s" label (shared.Substring(0, min 160 shared.Length)))

let private parsed (text: string) : JVal =
    match Json.parse text with
    | Ok jv -> jv
    | Error e -> failtestf "the test input does not parse: %s" e

/// A Custom node carrying `v` as its one prop — a rule-12 payload slot.
let private customWith (v: JVal) : JVal =
    JObj
        [ "id", JStr "c1"
          "kind",
          JObj
              [ "$type", JStr "Custom"
                "componentId", JStr "trend-card"
                "moduleId", JStr "analytics"
                "props", JObj [ "k", v ] ] ]

/// A Metric whose static value is `v` — a typed float slot.
let private metricWith (v: JVal) : JVal =
    JObj
        [ "id", JStr "m"
          "kind",
          JObj
              [ "$type", JStr "Metric"
                "label", JStr "Revenue"
                "value", JObj [ "$type", JStr "Static"; "value", v ] ] ]

/// The bits of a decoded Metric's static value.
let private metricBits (r: Result<Fuaran.UI.Types.Node<obj>, JsonDecode.DecodeError>) : int64 =
    match r with
    | Ok node ->
        match node.Kind with
        | Fuaran.UI.Types.NodeKind.Metric spec ->
            match spec.Value with
            | Fuaran.UI.Types.Binding.Static(Some v) -> System.BitConverter.DoubleToInt64Bits v
            | other -> failtestf "expected a Static value binding, got %A" other
        | other -> failtestf "expected a Metric, got %A" other
    | Error e -> failtestf "the Metric did not decode: %s" (JsonDecode.DecodeError.render e)

/// `inner` wrapped in `n` arrays, built by a loop rather than by recursion.
let private nested (n: int) (inner: JVal) : JVal =
    let mutable v = inner

    for _ in 1..n do
        v <- JArr [ v ]

    v

let private corpusEquivalence =
    test "every node and op corpus fixture decides the same through the JVal entry" {
        let root, entries = Corpus.load ()
        let mutable compared = 0
        let mutable accepted = 0

        for e in entries do
            let route =
                match e.Decoder with
                | "node" -> Some Node
                | "op" -> Some Op
                | _ -> None

            match route with
            | None -> ()
            | Some route ->
                match Json.parse (Corpus.readPayload root e.InputFile) with
                | Error _ -> ()
                | Ok jv ->
                    let shared = agree e.Id route jv
                    compared <- compared + 1

                    if shared.StartsWith "ok " then
                        accepted <- accepted + 1

        // Both counts are asserted: a corpus that resolved to nothing, or one
        // the two routes both refused wholesale, would otherwise read as green.
        Expect.isGreaterThan compared 300 (sprintf "only %d corpus fixtures were compared" compared)
        Expect.isGreaterThan accepted 250 (sprintf "only %d compared fixtures decoded" accepted)
    }

let private floatEdges: (string * JVal) list =
    [ "0.1", JFloat 0.1
      "0.30000000000000004", JFloat 0.30000000000000004
      "1e21", JFloat 1e21
      "1e-7", JFloat 1e-7
      "an integer past 2^53", JFloat 123456789012345680.0
      "2^53 + 2", JFloat 9007199254740994.0
      "Int32.MaxValue + 1", JFloat 2147483648.0
      "the largest double", JFloat 1.7976931348623157e308
      "the smallest subnormal", JFloat 5e-324
      "negative zero", JFloat -0.0
      "a whole float", JFloat 2.0
      "NaN", JFloat nan
      "+Infinity", JFloat infinity
      "-Infinity", JFloat -infinity
      "0", JInt 0
      "-1", JInt -1
      "Int32.MaxValue", JInt System.Int32.MaxValue
      "Int32.MinValue", JInt System.Int32.MinValue ]

let private numberEdges =
    testList
        "number edges"
        [ for name, v in floatEdges do
              test $"{name} at a typed float slot" {
                  let shared = agree name Node (metricWith v)

                  // Every edge is a value the float slot takes (the sentinels as
                  // §7 strings), so a refusal here is a regression on its own.
                  expectAccepted name shared

                  // The canonical encoding collapses a negative zero, so it
                  // cannot see the one number the two routes could differ on
                  // without a byte changing: compare the decoded doubles' BITS.
                  let text = Canon.render (metricWith v)

                  Expect.equal
                      (metricBits (JsonDecode.decodeNodeObjJVal (metricWith v)))
                      (metricBits (JsonDecode.decodeNodeObj text))
                      (sprintf "%s: the decoded double differs in its bits" name)
              }

              test $"{name} at a rule-12 payload slot" { agree name Node (customWith v) |> ignore } ]

let private structuralEdges =
    let maxString = Fuaran.UI.WireLimits.MaxStringLength
    let maxArray = Fuaran.UI.WireLimits.MaxArrayLength
    // `customWith` puts its value inside three composites, so `depthBudget` arrays
    // around it reach exactly `MaxJsonDepth` levels.
    let depthBudget = Fuaran.UI.WireLimits.MaxJsonDepth - 3

    testList
        "structural edges"
        [ test "a repeated member at the root is INVALID_JSON, as the text route says" {
              let jv =
                  match customWith (JStr "x") with
                  | JObj fields -> JObj(fields @ [ "id", JStr "c2" ])
                  | other -> other

              agree "root duplicate" Node jv |> expectCode "root duplicate" "INVALID_JSON"
          }

          test "a repeated member in a payload is INVALID_JSON" {
              agree "nested duplicate" Node (customWith (JObj [ "a", JInt 1; "a", JInt 2 ]))
              |> expectCode "nested duplicate" "INVALID_JSON"
          }

          test "a repeated member in an op is INVALID_JSON" {
              let root, entries = Corpus.load ()

              let op =
                  entries
                  |> List.find (fun e -> e.Decoder = "op" && e.Kind = "op-round-trip")
                  |> fun e -> parsed (Corpus.readPayload root e.InputFile)

              let duplicated =
                  match op with
                  | JObj((k, v) :: rest) -> JObj((k, v) :: rest @ [ k, v ])
                  | other -> failtestf "the op fixture is not an object: %A" other

              agree "op duplicate" Op duplicated |> expectCode "op duplicate" "INVALID_JSON"
          }

          test "a lone high surrogate in a value is INVALID_JSON" {
              agree "lone high" Node (customWith (JStr(string (char 0xD800) + "a")))
              |> expectCode "lone high" "INVALID_JSON"
          }

          test "a lone low surrogate in a key is INVALID_JSON" {
              agree "lone low key" Node (customWith (JObj [ string (char 0xDC00), JInt 1 ]))
              |> expectCode "lone low key" "INVALID_JSON"
          }

          test "a surrogate pair is one code point and decodes" {
              agree "pair" Node (customWith (JStr "\U0001F600")) |> expectAccepted "pair"
          }

          test "a raw control character decodes: the render escapes it" {
              agree "C0" Node (customWith (JStr "a\u0001b\nc")) |> expectAccepted "C0"
          }

          test "a string of MaxStringLength code points decodes, and one more is LIMIT_EXCEEDED" {
              agree "at the string bound" Node (customWith (JStr(String.replicate maxString "a")))
              |> expectAccepted "at the string bound"

              agree "past the string bound" Node (customWith (JStr(String.replicate (maxString + 1) "a")))
              |> expectCode "past the string bound" "LIMIT_EXCEEDED"
          }

          test "an astral string counts code points, not units, against the bound" {
              // MaxStringLength astral characters are 2 x MaxStringLength units:
              // within the bound in code points, which is the unit §21.6 names.
              agree "astral at the bound" Node (customWith (JStr(String.replicate maxString "\U0001F600")))
              |> expectAccepted "astral at the bound"
          }

          test "an array of MaxArrayLength items decodes, and one more is LIMIT_EXCEEDED" {
              agree "at the array bound" Node (customWith (JArr(List.replicate maxArray (JInt 1))))
              |> expectAccepted "at the array bound"

              agree "past the array bound" Node (customWith (JArr(List.replicate (maxArray + 1) (JInt 1))))
              |> expectCode "past the array bound" "LIMIT_EXCEEDED"
          }

          test "an object of MaxArrayLength + 1 members is LIMIT_EXCEEDED" {
              let members = [ for i in 0..maxArray -> $"m{i}", JInt i ]

              agree "past the member bound" Node (customWith (JObj members))
              |> expectCode "past the member bound" "LIMIT_EXCEEDED"
          }

          test "nesting to MaxJsonDepth decides as the text route does, and one level more is LIMIT_EXCEEDED" {
              agree "at the depth bound" Node (customWith (nested depthBudget (JInt 1)))
              |> ignore

              agree "past the depth bound" Node (customWith (nested (depthBudget + 1) (JInt 1)))
              |> expectCode "past the depth bound" "LIMIT_EXCEEDED"
          }

          test "a value nested far past the bound is refused without a stack overflow" {
              agree "100k deep" Node (customWith (nested 100_000 (JInt 1)))
              |> expectCode "100k deep" "LIMIT_EXCEEDED"
          }

          Lanes.slow (
              test "a value whose rendering passes a third of MaxDocumentBytes takes the text route and decodes" {
                  // Twelve maximal strings: about 12.6 million units, past the
                  // third under which the lowering may decide the size alone.
                  let big = JArr(List.replicate 12 (JStr(String.replicate maxString "a")))
                  agree "past a third" Node (customWith big) |> expectAccepted "past a third"
              }
          )

          Lanes.slow (
              test "a value whose rendering passes MaxDocumentBytes is LIMIT_EXCEEDED" {
                  // Seventeen maximal strings of a two-byte character: about 35.7
                  // million UTF-8 bytes, past the ceiling of 33,554,432.
                  let big = JArr(List.replicate 17 (JStr(String.replicate maxString "é")))

                  agree "past the document ceiling" Node (customWith big)
                  |> expectCode "past the document ceiling" "LIMIT_EXCEEDED"
              }
          ) ]

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops.JsonDecode — the JVal entry points (Phase 2059)"
        [ corpusEquivalence
          numberEdges
          structuralEdges
          test "decodeNodeJVal is decodeNodeObjJVal marked as wire-originated" {
              let jv = metricWith (JFloat 1.5)

              match JsonDecode.decodeNodeJVal jv, JsonDecode.decodeNodeObjJVal jv with
              | Ok wire, Ok obj ->
                  Expect.equal
                      (CanonicalJson.encodeNode (Fuaran.UI.Ops.Types.WireTree.reify wire))
                      (CanonicalJson.encodeNode obj)
                      "the two node entries disagree"
              | a, b -> failtestf "expected both to decode: %A / %A" a b
          } ]
