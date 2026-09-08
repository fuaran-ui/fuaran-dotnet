module Fuaran.UI.Tests.Program

open Expecto
open Fuaran.UI.Testing

[<EntryPoint>]
let main argv =
    // Phase 1553 — the ONE place this suite reads the gate lane. `runTestsInAssemblyWithCLIArgs`
    // discovers and runs in one call, so the lane filter needs the two steps separated: discover the
    // assembly's tests, keep what the lane admits (`Lanes.applyTo`), then run. The full lane (the
    // default, and the only lane a ship may cite) returns the discovered tree untouched, so the ship
    // gate is byte-identical to the pre-phase run.
    match Impl.testFromThisAssembly () with
    | None ->
        eprintfn "no tests discovered in the assembly"
        1
    | Some tests -> runTestsWithCLIArgs [] argv (Lanes.applyTo Lanes.current tests)
