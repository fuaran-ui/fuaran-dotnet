module Fuaran.UI.Tests.FloatLayoutTests

// Phase 2318 — every float layout in the tier is Core's. The shared cases run here on .NET and in
// the Fable law harness under Node (`FloatLayoutCases.fs`); the memo key's float token is pinned
// here, because a memo key is stored and so must not move.

open Expecto

[<Tests>]
let tests =
    testList
        "Phase 2318 — the tier's float layouts are Core's"
        [ test "the AI wire and the SVG coordinate write Core's finite layout" {
              Expect.isEmpty (FloatLayoutCases.refutations ()) "every case writes its pinned bytes"
          }

          test "a memo value key lays a float out exactly as before (no stored key moves)" {
              // The bytes `ToString("R")` wrote, which every stored key was minted under; `-0` stays
              // apart from `0` and the non-finite tokens stay bare.
              let key =
                  Fuaran.UI.Memo.FragmentKey.value (
                      Map.ofList
                          [ "a", (1.5 :> obj)
                            "b", (-0.0 :> obj)
                            "c", (nan :> obj)
                            "d", (1e-7 :> obj)
                            "e", (infinity :> obj)
                            "f", (1.5e15 :> obj)
                            "g", (0.0 :> obj) ]
                  )

              Expect.equal
                  key
                  "a=f:1.5|b=f:-0|c=f:NaN|d=f:1E-07|e=f:Infinity|f=f:1500000000000000|g=f:0"
                  "the float tokens of a value key"
          } ]
