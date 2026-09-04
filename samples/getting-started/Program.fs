module Samples.GettingStarted.Program

// The tour, in order. Each lesson stands alone — run one with
// `dotnet run --project samples/getting-started -- <name>` — but they build on
// each other, so the first pass is worth taking in sequence.

let private lessons: (string * string * (string list -> unit)) list =
    [ "authoring", "A user interface is a value", (fun _ -> Lesson01Authoring.run ())
      "ops", "Edit the tree, don't regenerate it", (fun _ -> Lesson02EditByOps.run ())
      "replay", "A session replays exactly", (fun _ -> Lesson03Replay.run ())
      "safety", "Safety is a property of the shape", (fun _ -> Lesson04Safety.run ())
      "operations", "Declared operations need no model", (fun _ -> Lesson05DeclaredOperations.run ())
      "ai", "Bring your own key: prompt, decode, render", Lesson06AiAuthoring.run ]

let private rule (title: string) =
    printfn ""
    printfn "══ %s %s" title (String.replicate (max 1 (66 - title.Length)) "═")
    printfn ""

[<EntryPoint>]
let main argv =
    let args = List.ofArray argv

    let requested =
        args
        |> List.filter (fun a -> not (a.StartsWith "--"))
        |> List.filter (fun a -> lessons |> List.exists (fun (name, _, _) -> name = a))

    let selected =
        match requested with
        | [] -> lessons
        | names -> lessons |> List.filter (fun (name, _, _) -> List.contains name names)

    if List.isEmpty selected then
        printfn "No such lesson. Available:"

        for (name, title, _) in lessons do
            printfn "  %-12s %s" name title

        1
    else
        for (name, title, run) in selected do
            rule (sprintf "%s — %s" name title)
            run args

        printfn ""
        printfn "══ done %s" (String.replicate 61 "═")
        printfn ""
        printfn "Next: samples/catalog for the whole component vocabulary rendered in a browser,"
        printfn "and docs/AI_AUTHORING_GUIDE.md for what to put in a system prompt."
        0
