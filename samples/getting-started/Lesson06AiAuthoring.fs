module Samples.GettingStarted.Lesson06AiAuthoring

// ============================================================================
//  LESSON 6 — Bring your own key: prompt, decode, render.
//
//  The whole loop, and it is smaller than you expect:
//
//      prompt -> the model emits wire JSON -> DECODE STRICTLY -> render
//
//  The middle step is the one that matters. The model's output is untrusted
//  text; the decoder turns it into a typed tree or refuses it by name (Lesson
//  4). Nothing between those two points inspects the string for danger, because
//  by the time you hold a tree there is nothing dangerous left to find.
//
//  THIS LESSON NEEDS YOUR OWN KEY, and it is the only one that does. Set
//  `ANTHROPIC_API_KEY` (or pass `--key <k>`) and it makes one HTTPS call.
//  Without a key it says so and moves on — the other five lessons are complete
//  offline, which is deliberate: nothing here should be unrunnable because a
//  reader has not signed up for anything.
//
//  There is no SDK involved: one `HttpClient`, one JSON body. A different
//  provider is a different URL and a different field name.
// ============================================================================

open System
open System.Net.Http
open System.Text

module Decode = Fuaran.UI.Ops.JsonDecode

/// The teaching in one paragraph. A real host sends a fuller prompt pack — the
/// vocabulary, the field-by-field rules, worked examples — but this is enough
/// for a capable model to emit something the strict decoder accepts.
let private systemPrompt =
    "You emit user interfaces as canonical Fuaran wire-format JSON and nothing else. \
     A node is {\"id\":\"…\",\"kind\":{\"$type\":\"…\",…}}. Useful kinds: Box (role Dashboard or Card, \
     with children), Heading (level, text), Metric (label, value {\"$type\":\"Static\",\"value\":n}, \
     format {\"$type\":\"Currency\",\"code\":\"GBP\"} or {\"$type\":\"Number\",\"decimals\":0}), \
     Markdown (text), Callout (tone Info|Success|Warning|Critical, body). Text fields are plain JSON \
     strings. Reply with ONE JSON object and no prose, no explanation and no code fence."

let private keyFrom (argv: string list) =
    let fromArgs =
        argv
        |> List.pairwise
        |> List.tryPick (fun (a, b) -> if a = "--key" then Some b else None)

    match fromArgs with
    | Some k -> Some k
    | None ->
        match Environment.GetEnvironmentVariable "ANTHROPIC_API_KEY" with
        | null -> None
        | "" -> None
        | k -> Some k

/// Pull the first balanced JSON object out of a reply, so a model that adds a
/// sentence or a code fence around its answer still works. This is presentation
/// tolerance, NOT safety tolerance — whatever comes out still faces the strict
/// decoder unchanged.
let private firstJsonObject (s: string) : string option =
    let start = s.IndexOf '{'

    if start < 0 then
        None
    else
        let mutable depth = 0
        let mutable inString = false
        let mutable escaped = false
        let mutable stop = -1
        let mutable i = start

        while stop < 0 && i < s.Length do
            let c = s[i]

            if escaped then
                escaped <- false
            elif c = '\\' && inString then
                escaped <- true
            elif c = '"' then
                inString <- not inString
            elif not inString then
                if c = '{' then
                    depth <- depth + 1
                elif c = '}' then
                    depth <- depth - 1

                    if depth = 0 then
                        stop <- i

            i <- i + 1

        if stop < 0 then
            None
        else
            Some(s.Substring(start, stop - start + 1))

let private ask (key: string) (prompt: string) : Async<string> =
    async {
        use client = new HttpClient()
        client.Timeout <- TimeSpan.FromSeconds 60.0
        client.DefaultRequestHeaders.Add("x-api-key", key)
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01")

        // Hand-built so the sample carries no provider SDK. The escaping is the
        // canonical encoder's, so a quote or a newline in the prompt is safe.
        let body =
            let q (s: string) =
                Fuaran.Core.Canon.render (Fuaran.Core.JStr s)

            sprintf
                """{"model":"claude-sonnet-4-5","max_tokens":2000,"system":%s,"messages":[{"role":"user","content":%s}]}"""
                (q systemPrompt)
                (q prompt)

        use content = new StringContent(body, Encoding.UTF8, "application/json")

        let! response =
            client.PostAsync("https://api.anthropic.com/v1/messages", content)
            |> Async.AwaitTask

        let! raw = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        if not response.IsSuccessStatusCode then
            return failwithf "the provider returned %d: %s" (int response.StatusCode) raw

        // The reply envelope is the provider's shape, not Fuaran's; the tree we
        // care about is inside the assistant's text.
        let doc = System.Text.Json.JsonDocument.Parse raw

        return
            doc.RootElement.GetProperty("content").EnumerateArray()
            |> Seq.filter (fun b -> b.GetProperty("type").GetString() = "text")
            |> Seq.map (fun b -> b.GetProperty("text").GetString())
            |> String.concat ""
    }

let run (argv: string list) =
    let prompt =
        "A dashboard for a small bookshop: this month's revenue in pounds, books sold, \
         and a short note welcoming the reader."

    match keyFrom argv with
    | None ->
        printfn "No key, so no call was made."
        printfn ""
        printfn "  Set ANTHROPIC_API_KEY (or pass --key <k>) and re-run to see the whole loop:"
        printfn "  prompt -> emitted wire JSON -> strict decode -> rendered HTML."
        printfn ""
        printfn "  The key is read from this process's environment, sent to the provider you"
        printfn "  chose, and nothing else. This sample stores nothing and logs nothing."
        printfn ""
        printfn "  The prompt it would send:"
        printfn "    %s" prompt
    | Some key ->
        printfn "Prompt: %s" prompt
        printfn ""

        try
            let reply = ask key prompt |> Async.RunSynchronously

            match firstJsonObject reply with
            | None ->
                printfn "The model replied with no JSON object at all:"
                printfn "  %s" (reply.Substring(0, min 300 reply.Length))
            | Some wire ->
                printfn "Emitted %d bytes of wire JSON." wire.Length
                printfn ""

                // THE GATE. Everything before this is untrusted text.
                match Decode.decodeNodeObj wire with
                | Error e ->
                    printfn "Refused by the strict decoder — and this is the system working:"
                    printfn "  %A" e
                    printfn ""
                    printfn "  A real orchestrator hands that error back to the model and asks again."
                    printfn "  The error names the path and the expectation, so the second attempt"
                    printfn "  usually lands. Nothing was rendered, and nothing had to be sanitised."
                | Ok tree ->
                    printfn "Decoded. Rendering it server-side, with no browser:"
                    printfn ""
                    let html = Fuaran.UI.Renderer.Server.Render.renderStatic tree
                    printfn "%s" html
        with ex ->
            printfn "The call failed: %s" ex.Message
