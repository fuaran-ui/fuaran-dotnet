module Samples.GettingStarted.Lesson05DeclaredOperations

// ============================================================================
//  LESSON 5 — Declare the operations, and most prompts stop needing a model.
//
//  THE CONTRAST IS THE LESSON, so run this one and read the two halves side by
//  side.
//
//  A control can PUBLISH the operations it supports: each one a name, a typed
//  signature of holes, and a declared effect. That declaration is data, so it
//  can be searched. Ask "what can I run with the context I have" and you get an
//  answer computed by structural matching over the registry — deterministic,
//  total, in memory, in microseconds, offline, and identical on every host and
//  every run. No model call. No network. Nothing to be non-deterministic about.
//
//  The model is then reserved for what genuinely needs judgement: turning "make
//  the revenue number stand out" into a choice among the declared operations,
//  or authoring something the bank has never seen. That is a much smaller job,
//  and — because the operations are typed — its output is checkable before it
//  runs.
//
//  WHAT THIS SAMPLE DELIBERATELY DOES NOT USE. The patterns below are declared
//  HERE, in this file, by this sample. There is a curated public seed catalogue
//  in the package too, and either is fine to build on. What no sample can reach
//  is a bank LEARNED from a corpus of real sessions — the resolver that gets
//  better the more it is used. That is not part of the open language tier, and
//  its absence is deliberate rather than an omission.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Core

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the declarations ────────────────────────────────────────────────────────
//
// Each pattern says what it PRODUCES and what holes it needs filled. `Build` is
// ordinary code — the declaration is what is searchable, and the build is what
// runs once a pattern is chosen.

let private kpiTile: FastPath.Pattern =
    { Id = "sample.kpi-tile"
      Title = "KPI tile"
      Summary = "A single labelled figure, formatted as currency."
      ResultType = "Metric"
      Holes = [ FastPath.textHole "kpi.label" "label"; FastPath.textHole "kpi.value" "value" ]
      Build =
        fun holes ->
            let label = holes |> Map.tryFind "kpi.label" |> Option.defaultValue "Value"

            let value =
                holes
                |> Map.tryFind "kpi.value"
                |> Option.bind (fun v ->
                    match System.Double.TryParse v with
                    | true, d -> Some d
                    | _ -> None)
                |> Option.defaultValue 0.0

            Fuaran.metric
                "kpi"
                { Defaults.metric with
                    Label = TextSource.Literal label
                    Value = Binding.Static(Some value)
                    Format = CellFormat.Currency "GBP"
                    Tone = ToneVariant.Brand } }

let private noticeBanner: FastPath.Pattern =
    { Id = "sample.notice"
      Title = "Notice banner"
      Summary = "A tone-tagged callout with a heading and a body."
      ResultType = "Callout"
      Holes =
        [ FastPath.textHole "notice.heading" "heading"
          FastPath.textHole "notice.body" "body" ]
      Build =
        fun holes ->
            Fuaran.callout
                "notice"
                { Defaults.callout with
                    Tone = ToneVariant.Warning
                    Heading = holes |> Map.tryFind "notice.heading" |> Option.map TextSource.Literal
                    Body =
                        holes
                        |> Map.tryFind "notice.body"
                        |> Option.defaultValue ""
                        |> TextSource.Literal } }

let private progressBar: FastPath.Pattern =
    { Id = "sample.progress"
      Title = "Progress bar"
      Summary = "A labelled completion bar over a percentage."
      ResultType = "Progress"
      Holes =
        [ FastPath.textHole "progress.label" "label"
          FastPath.numberHole "progress.percent" "percent" 0 100 ]
      Build =
        fun holes ->
            let percent =
                holes
                |> Map.tryFind "progress.percent"
                |> Option.bind (fun v ->
                    match System.Int32.TryParse v with
                    | true, n -> Some n
                    | _ -> None)
                |> Option.defaultValue 0

            Fuaran.progress
                "progress"
                { Defaults.progress with
                    Fraction = Binding.Static(Some(float percent / 100.0))
                    Label = holes |> Map.tryFind "progress.label" |> Option.map TextSource.Literal } }

let private bank = FastPath.bank [ kpiTile; noticeBanner; progressBar ]

let run () =
    // ── the deterministic half ───────────────────────────────────────────────
    printfn "WITHOUT a model — a structural search over what is declared."
    printfn ""

    // "I have a label and a figure. What can I build?"
    let context =
        FastPath.query [ FastPath.textHole "kpi.label" "label"; FastPath.textHole "kpi.value" "value" ] None

    let runnable = FastPath.findRunnable context bank

    printfn "  Context: a label and a value."
    printfn "  Runnable right now: %s" (runnable |> List.map _.Title |> String.concat ", ")

    // Narrowing by what you want PRODUCED, not only by what you can supply.
    let wantCallout =
        FastPath.query
            [ FastPath.textHole "notice.heading" "heading"
              FastPath.textHole "notice.body" "body" ]
            (Some "Callout")

    printfn
        "  Asking specifically for a Callout: %s"
        (FastPath.findRunnable wantCallout bank |> List.map _.Title |> String.concat ", ")

    // Dispatch. This is the whole "execution" step: pick the match, fill the
    // holes, get a real tree. It is a function call.
    match runnable with
    | [] -> printfn "  (nothing matched — check the hole addresses)"
    | pattern :: _ ->
        let tree =
            pattern.Build(Map.ofList [ "kpi.label", "Net revenue"; "kpi.value", "142500" ])

        printfn ""
        printfn "  Dispatched %s -> %s" pattern.Id (Canon.encodeNode tree)

    // The declarations are introspectable, which is what lets a model choose
    // among them without being told what they are in a prompt.
    printfn ""
    printfn "  The declared signatures, as JSON Schema:"

    for entry in FunctionRegistry.enumerate bank.Registry do
        printfn
            "    %-18s -> %s"
            entry.Capability.Id
            (Fuaran.Core.Canon.render (Function.toJsonSchema entry.Capability.Signature))

    // ── the half that genuinely needs one ────────────────────────────────────
    printfn ""
    printfn "WITH a model — for the request no declaration covers."
    printfn ""
    printfn "  \"Show me last quarter's revenue as a KPI\""
    printfn "     -> the search above answers this. Deterministic, offline, no key."
    printfn ""
    printfn "  \"Rework this page so a colour-blind reader can still tell the"
    printfn "   at-risk workstreams from the healthy ones, and explain why\""
    printfn "     -> no declaration covers that. It needs judgement, and it is"
    printfn "        exactly the kind of request worth paying a model for."
    printfn ""
    printfn "  The point is not that models are unnecessary. It is that most"
    printfn "  requests in a real application are the first kind, and answering"
    printfn "  those by search rather than by generation makes them instant,"
    printfn "  free, offline and repeatable."
