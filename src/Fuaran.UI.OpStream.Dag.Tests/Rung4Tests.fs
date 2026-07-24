module Fuaran.UI.OpStream.Dag.Tests.Rung4Tests

open System.Reflection
open Expecto

// ============================================================================
//  Rung-4 packaging contract (Phase 178 acceptance #5).
//
//  A consumer referencing only the LINEAR packages (Fuaran.UI / .Renderer /
//  .Ops / linear .OpStream.*) must pull NONE of the DAG binary or API. We
//  assert this as a reference-graph invariant: every linear assembly's
//  referenced-assembly list contains no `Fuaran.UI.OpStream.Dag.*` name, and
//  the DAG abstractions DO reference the linear abstractions (the dependency
//  direction is one-way). If a future edit makes any light-path package
//  reference a DAG package, this test turns red.
// ============================================================================

let private dagPrefix = "Fuaran.UI.OpStream.Dag"

/// Load a referenced assembly by simple name (it is in the test output dir
/// because this test project transitively references the whole graph).
let private load (name: string) : Assembly = Assembly.Load(AssemblyName(name))

let private refNames (asm: Assembly) : string list =
    asm.GetReferencedAssemblies()
    |> Array.choose (fun n -> Option.ofObj n.Name)
    |> Array.toList

let private referencesAnyDag (asm: Assembly) : string list =
    refNames asm |> List.filter (fun n -> n.StartsWith dagPrefix)

/// The linear ("light path") packages a simple tree app references.
let private linearAssemblies =
    [ "Fuaran.UI"
      "Fuaran.UI.Ops"
      "Fuaran.UI.OpStream.Abstractions"
      "Fuaran.UI.OpStream.InMemory"
      "Fuaran.UI.OpStream.Sqlite"
      "Fuaran.UI.OpStream.Replay" ]

[<Tests>]
let tests =
    testList
        "Dag.Rung4"
        [ test "no linear package references any DAG package" {
              for name in linearAssemblies do
                  let asm = load name
                  let dagRefs = referencesAnyDag asm

                  Expect.isEmpty
                      dagRefs
                      (sprintf
                          "%s must not reference any Fuaran.UI.OpStream.Dag.* assembly, but references: %A"
                          name
                          dagRefs)
          }

          test "the DAG abstractions reference the linear abstractions (one-way dependency)" {
              let refs = refNames (load "Fuaran.UI.OpStream.Dag.Abstractions")
              Expect.contains refs "Fuaran.UI.OpStream.Abstractions" "DAG abstractions build on the linear abstractions"
          }

          test "the merge engine requires the DAG abstractions (not the linear sink)" {
              let refs = refNames (load "Fuaran.UI.OpStream.Dag.Merge")
              Expect.contains refs "Fuaran.UI.OpStream.Dag.Abstractions" "merge engine references the DAG abstractions"
          } ]
