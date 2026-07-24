module Fuaran.UI.OpStream.Dag.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match argv with
    | [| "--emit-dag-corpus"; root |] ->
        DagCorpus.emit root
        printfn "DAG corpus emitted to %s/dag" root
        0
    | [| "--emit-merge-corpus"; root |] ->
        MergeCorpus.emit root
        printfn "merge-conformance corpus emitted to %s/merge-conformance" root
        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
