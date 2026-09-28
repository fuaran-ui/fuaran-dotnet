module Fuaran.UI.Tests.HostLimits

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

// ============================================================================
//  Phase 1817 — host emission limits (FUARAN158–FUARAN162).
//
//  Each limit refuses at N+1 and accepts at N; every refusal names the limit,
//  the measured value and the first offending node; the measurement is ONE
//  walk (one meter visit per node, however many limits are declared); and a
//  host that declares nothing validates byte-for-byte as before.
// ============================================================================

type private Msg = NoOp

let private markdown id : Node<Msg> = Fuaran.markdown id "text"

let private dashboard id (children: Node<Msg> list) : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

/// A root dashboard over `n - 1` markdown leaves — `n` nodes in all, the
/// leaves named `m1` … `m(n-1)` in walk order.
let private treeOfNodes (n: int) : Node<Msg> =
    dashboard "root" [ for i in 1 .. n - 1 -> markdown (sprintf "m%d" i) ]

/// A chain `d1 > d2 > … > dD` of single-child dashboards ending in a leaf at
/// level `depth` (the root is level 1), so the tree is exactly `depth` deep.
let private chainOfDepth (depth: int) : Node<Msg> =
    let rec build level =
        if level = depth then
            markdown (sprintf "d%d" level)
        else
            dashboard (sprintf "d%d" level) [ build (level + 1) ]

    build 1

/// A grid carrying `rows` inline static rows.
let private gridWithStaticRows (id: string) (rows: int) : Node<Msg> =
    let node = Generated.mkDataGrid id [] (Binding.Static None)

    match node.Kind with
    | NodeKind.DataGrid spec ->
        { node with
            Kind =
                NodeKind.DataGrid
                    { spec with
                        StaticRows =
                            Some
                                { DefaultSort = None
                                  Headers = [ TextSource.Literal "n" ]
                                  Rows = [ for i in 1..rows -> [ TextSource.Literal(string i) ] ]
                                  Sortable = None } } }
    | _ -> failwith "mkDataGrid built something other than a DataGrid"

/// A grid whose source is a host-inlined `Binding.Static` table of `rows` rows.
let private gridWithStaticSource (id: string) (rows: int) : Node<Msg> =
    let table: Fuaran.Core.Row seq =
        [ for i in 1..rows -> Map.ofList [ "n", box i |> Unchecked.nonNull ] ]
        |> Seq.ofList

    Generated.mkDataGrid id [] (Binding.Static(Some table))

/// A grid bound to a `$state` key — filled at render time, never judged.
let private gridWithBoundSource (id: string) : Node<Msg> =
    Generated.mkDataGrid id [] (Binding.State("rows", None))

let private limitsWith (f: HostLimits -> HostLimits) = f (HostLimits.named "test")

let private isHostLimitDefect (d: PreEmitDefect) =
    match d with
    | PreEmitDefect.HostNodeCountExceeded _
    | PreEmitDefect.HostDepthExceeded _
    | PreEmitDefect.HostChildrenExceeded _
    | PreEmitDefect.HostGridRowsExceeded _
    | PreEmitDefect.HostPayloadBytesExceeded _ -> true
    | _ -> false

/// The host-limit findings alone. Several fixtures carry unrelated defects (a
/// column-less grid has no row identity); this suite judges only its own.
let private hostDefects (limits: HostLimits) (node: Node<Msg>) : PreEmitDefect list =
    match validateWithLimits limits node with
    | Ok() -> []
    | Error ds -> ds |> List.filter isHostLimitDefect

let private messageOf (d: PreEmitDefect) =
    let _, _, message = describe d
    message

[<Tests>]
let tests =
    testList
        "Phase 1817 — host emission limits"
        [ testList
              "each limit accepts at N and refuses at N+1"
              [ test "maxNodes (FUARAN158)" {
                    let limits = limitsWith (fun l -> { l with MaxNodes = Some 5 })

                    Expect.isEmpty (hostDefects limits (treeOfNodes 5)) "5 nodes under maxNodes = 5 is accepted"

                    Expect.equal
                        (hostDefects limits (treeOfNodes 6))
                        [ PreEmitDefect.HostNodeCountExceeded("m5", 5, 6, "test") ]
                        "the sixth node (m5, pre-order) is the first past the budget; the tree measures 6"
                }

                test "maxDepth (FUARAN159)" {
                    let limits = limitsWith (fun l -> { l with MaxDepth = Some 4 })

                    Expect.isEmpty (hostDefects limits (chainOfDepth 4)) "a tree 4 deep under maxDepth = 4 is accepted"

                    Expect.equal
                        (hostDefects limits (chainOfDepth 5))
                        [ PreEmitDefect.HostDepthExceeded("d5", 4, 5, "test") ]
                        "d5 is the first node below level 4; the tree reaches level 5"
                }

                test "maxChildren (FUARAN160), one finding per offending container" {
                    let limits = limitsWith (fun l -> { l with MaxChildren = Some 3 })

                    let wide id n =
                        dashboard id [ for i in 1..n -> markdown (sprintf "%s-%d" id i) ]

                    Expect.isEmpty (hostDefects limits (wide "root" 3)) "3 children under maxChildren = 3 is accepted"

                    Expect.equal
                        (hostDefects limits (dashboard "root" [ wide "a" 4; wide "b" 3; wide "c" 5 ]))
                        [ PreEmitDefect.HostChildrenExceeded("a", 3, 4, "test")
                          PreEmitDefect.HostChildrenExceeded("c", 3, 5, "test") ]
                        "every over-wide container is named with its own count, in walk order"
                }

                test "maxGridRows (FUARAN161) over staticRows" {
                    let limits = limitsWith (fun l -> { l with MaxGridRows = Some 10 })

                    Expect.isEmpty (hostDefects limits (gridWithStaticRows "g" 10)) "10 rows under maxGridRows = 10"

                    Expect.equal
                        (hostDefects limits (gridWithStaticRows "g" 11))
                        [ PreEmitDefect.HostGridRowsExceeded("g", 10, 11, "test") ]
                        "11 inline rows are refused"
                }

                test "maxGridRows (FUARAN161) over a Binding.Static source" {
                    let limits = limitsWith (fun l -> { l with MaxGridRows = Some 10 })

                    Expect.isEmpty (hostDefects limits (gridWithStaticSource "g" 10)) "10 rows under maxGridRows = 10"

                    Expect.equal
                        (hostDefects limits (gridWithStaticSource "g" 11))
                        [ PreEmitDefect.HostGridRowsExceeded("g", 10, 11, "test") ]
                        "an inlined static table is inline rows too"
                }

                test "maxGridRows does not judge a bound source" {
                    let limits = limitsWith (fun l -> { l with MaxGridRows = Some 0 })

                    Expect.isEmpty
                        (hostDefects limits (gridWithBoundSource "g"))
                        "a $state source is the host's to page — there are no inline rows to count"
                }

                test "maxSerializedBytes (FUARAN162)" {
                    let tree = treeOfNodes 4
                    let size = HostLimits.serializedBytes tree

                    let atSize =
                        limitsWith (fun l ->
                            { l with
                                MaxSerializedBytes = Some size })

                    let underSize =
                        limitsWith (fun l ->
                            { l with
                                MaxSerializedBytes = Some(size - 1) })

                    Expect.isEmpty (hostDefects atSize tree) "a payload exactly at the limit is accepted"

                    Expect.equal
                        (hostDefects underSize tree)
                        [ PreEmitDefect.HostPayloadBytesExceeded("root", size - 1, size, "test") ]
                        "one byte over is refused, naming the root and the encoded size"
                } ]

          test "every refusal message names the limit, the measured value and the offending node" {
              let limits =
                  { HostLimits.named "tight" with
                      MaxNodes = Some 3
                      MaxDepth = Some 1
                      MaxChildren = Some 2
                      MaxGridRows = Some 1
                      MaxSerializedBytes = Some 10 }

              let tree = dashboard "root" [ markdown "a"; markdown "b"; gridWithStaticRows "g" 7 ]

              let found = hostDefects limits tree

              Expect.equal
                  (found |> List.map (fun d -> let code, _, _ = describe d in code))
                  [ "FUARAN158"; "FUARAN159"; "FUARAN160"; "FUARAN161"; "FUARAN162" ]
                  "all five limits are reported, in limit order, each under its own code"

              let expectations =
                  [ "maxNodes", "4", "'g'"
                    "maxDepth", "level 2", "'a'"
                    "maxChildren", "3", "'root'"
                    "maxGridRows", "7", "'g'"
                    "maxSerializedBytes", string (HostLimits.serializedBytes tree), "'root'" ]

              for d, (limitName, measured, nodeId) in List.zip found expectations do
                  let message = messageOf d
                  Expect.stringContains message limitName "the message names the limit"
                  Expect.stringContains message measured "the message names the measured value"
                  Expect.stringContains message nodeId "the message names the first offending node"
                  Expect.stringContains message "'tight'" "the message names the declaration that refused"
          }

          test "the measurement is ONE walk — one meter visit per node, whatever is declared" {
              let tree =
                  dashboard "root" [ chainOfDepth 3; gridWithStaticRows "g" 4; markdown "x"; markdown "y" ]

              let nodeCount = 1 + 3 + 1 + 1 + 1
              let meter = HostLimitMeter HostLimits.email
              validateWithMeter meter tree |> ignore

              Expect.equal meter.Visits nodeCount "five limits declared, and each node visited exactly once"

              let m = meter.Measurement
              Expect.equal m.Nodes nodeCount "the node figure is the same walk's count"
              Expect.equal m.Depth 4 "root > d1 > d2 > d3 is four levels"
              Expect.equal m.WidestContainer 4 "the root holds four children"
              Expect.equal m.LargestGrid 4 "the grid carries four rows"
              Expect.equal m.SerializedBytes (Some(HostLimits.serializedBytes tree)) "the payload is encoded once"

              let undeclared = HostLimitMeter(HostLimits.named "none")
              validateWithMeter undeclared tree |> ignore

              Expect.equal
                  undeclared.Measurement.SerializedBytes
                  None
                  "no encode is paid for a size limit nobody declared"
          }

          test "a tree past the wire depth is not encoded, and the wire refusal still stands" {
              let tree = chainOfDepth (WireLimits.MaxDepth + 3)
              let meter = HostLimitMeter HostLimits.mobile

              match validateWithMeter meter tree with
              | Ok() -> failwith "a tree past WireLimits.MaxDepth must be refused"
              | Error ds ->
                  Expect.exists
                      ds
                      (function
                      | PreEmitDefect.MaxDepthExceeded _ -> true
                      | _ -> false)
                      "FUARAN091 — the decode-side wire limit — still fires; an emission limit never loosens it"

                  Expect.contains
                      ds
                      (PreEmitDefect.HostDepthExceeded("d17", 16, WireLimits.MaxDepth, "mobile"))
                      "the host limit reports its own breach, measured to the deepest level walked"

              Expect.isNone meter.Measurement.SerializedBytes "an abandoned walk is never encoded"
          }

          testList
              "the unbounded default is byte-for-byte the prior behaviour"
              [ let trees: (string * Node<Msg>) list =
                    [ "a clean tree", treeOfNodes 12
                      "a deep chain", chainOfDepth 10
                      "past the wire depth", chainOfDepth (WireLimits.MaxDepth + 2)
                      "a grid with defects of its own", gridWithStaticRows "g" 500
                      "duplicate ids", dashboard "root" [ markdown "same"; markdown "same" ] ]

                for name, tree in trees do
                    test name {
                        Expect.equal
                            (validateWithLimits HostLimits.unbounded tree)
                            (validate tree)
                            "validateWithLimits HostLimits.unbounded = validate"

                        Expect.equal
                            (validateWithLimits (HostLimits.named "declares-nothing") tree)
                            (validate tree)
                            "a named declaration with no limits is equally inert"
                    } ]

          test "the presets are data: named, fully declared, and editable by record update" {
              for preset, identity in
                  [ HostLimits.email, "email"
                    HostLimits.mobile, "mobile"
                    HostLimits.card, "card" ] do
                  Expect.equal preset.Identity identity "a preset names itself"

                  Expect.equal
                      (HostLimits.declared preset |> List.map fst)
                      HostLimits.kinds
                      (sprintf "%s declares every limit" identity)

              let kiosk =
                  { HostLimits.card with
                      Identity = "kiosk"
                      MaxNodes = Some 900 }

              Expect.equal kiosk.MaxNodes (Some 900) "a host starts from a preset and edits it"
              Expect.isFalse (HostLimits.declaresAny HostLimits.unbounded) "unbounded declares nothing"
          }

          test "each limit owns one code, projected through describe" {
              Expect.equal
                  (HostLimits.kinds |> List.map hostLimitCode)
                  [ "FUARAN158"; "FUARAN159"; "FUARAN160"; "FUARAN161"; "FUARAN162" ]
                  "maxNodes → 158 … maxSerializedBytes → 162"

              for kind in HostLimits.kinds do
                  let _, severity, _ =
                      describe (
                          hostLimitDefect
                              "x"
                              { Kind = kind
                                NodeId = "n"
                                Limit = 1
                                Measured = 2 }
                      )

                  Expect.equal severity DefectSeverity.Error "an emission limit is a refusal, not advice"
          }

          test "utf8Length agrees with the platform encoder" {
              for s in [ ""; "ascii"; "café"; "€100"; "\U0001F600 smile"; "lone \uD800 surrogate" ] do
                  Expect.equal
                      (HostLimits.utf8Length s)
                      (System.Text.Encoding.UTF8.GetByteCount s)
                      (sprintf "UTF-8 length of %A" s)
          } ]
