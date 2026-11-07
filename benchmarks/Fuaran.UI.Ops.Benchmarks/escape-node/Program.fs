module Fuaran.UI.Ops.Benchmarks.EscapeNode.Program

open Fuaran.UI.Ops.Benchmarks

// Prints the `escape-rate` lines under node (and on .NET, where the same program runs as a check
// that the linked module compiles there too). The iteration count is the first argument.

#if FABLE_COMPILER
[<Fable.Core.Emit("(process.argv[2] ?? '')")>]
let private firstArgument () : string = Fable.Core.Util.jsNative
#endif

[<EntryPoint>]
let main argv =
#if FABLE_COMPILER
    let raw = firstArgument ()
#else
    let raw = if argv.Length > 0 then argv[0] else ""
#endif
    let iterations =
        match System.Int32.TryParse raw with
        | true, n when n > 0 -> n
        | _ -> 2000

    for c in EscapeRate.all do
        let meanNs, _ = EscapeRate.measure c iterations
        printfn "opstream.escape.%s.bytes = %d" c.Name (EscapeRate.encodedLength c)
        printfn "opstream.escape.%s.node.mean_ns = %.1f" c.Name meanNs

    0
