module Samples.GettingStarted.Lesson04Safety

// ============================================================================
//  LESSON 4 — Safety is a property of the shape, not of a filter.
//
//  The usual way to make model output safe is to inspect it: scan for script
//  tags, strip attributes, sanitise. That is a losing position, because it asks
//  you to enumerate what is dangerous.
//
//  Here the argument runs the other way. The wire format can express a closed
//  set of node kinds with typed fields — and executable code is not one of them,
//  so there is nothing to strip. An emission is either a well-formed tree from
//  that closed vocabulary or it is REFUSED, and the refusal says which field, at
//  which path, and what was expected. Default-deny by shape.
//
//  Two gates, and they answer different questions:
//    * the DECODER asks "is this a tree at all" — a wrong type, an unknown kind
//      or a missing required field never becomes a value;
//    * the PRE-EMIT VALIDATOR asks "is this tree coherent" — a filter nothing
//      consumes, a switch nothing can write, a fragment reference that resolves
//      to nothing. Decodable, and still not something you want to render.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson
module Decode = Fuaran.UI.Ops.JsonDecode

/// What a well-behaved model emits.
let private good = Canon.encodeNode Lesson01Authoring.dashboard

/// Four refusals, each a different failure a model actually makes.
let private refusals: (string * string) list =
    [ "a node kind that does not exist", """{"id":"x","kind":{"$type":"ScriptBlock","code":"alert(1)"}}"""

      "a required field left out", """{"id":"x","kind":{"$type":"Metric","label":"Revenue"}}"""

      "a field of the wrong type", """{"id":"x","kind":{"$type":"Heading","level":"one","text":"Hi"}}"""

      "an attempt to smuggle markup through a text field",
      """{"id":"x","kind":{"$type":"Heading","level":1,"text":{"$type":"Html","raw":"<script>alert(1)</script>"}}}""" ]

let run () =
    match Decode.decodeNodeObj good with
    | Ok _ -> printfn "A well-formed emission decodes. (%d bytes)" good.Length
    | Error e -> printfn "unexpected: the good emission failed to decode: %A" e

    printfn ""
    printfn "And these do not:"
    printfn ""

    for (what, wire) in refusals do
        match Decode.decodeNodeObj wire with
        | Ok _ -> printfn "  %-46s ACCEPTED — this is a defect, please report it" what
        | Error e -> printfn "  %-46s refused: %A" what e

    // Note what did NOT happen: nothing was sanitised, no allow-list was
    // consulted, and no string was inspected for dangerous content. The last
    // case fails for the same structural reason as the others — `Html` is not
    // in the closed `TextSource` vocabulary — not because anything recognised
    // `<script>`.
    printfn ""
    printfn "Nothing above was sanitised. There is no code case in the vocabulary to strip."

    // The second gate. This tree decodes perfectly and is still incoherent: it
    // declares a filter chip that no reader consumes, so the control renders and
    // does nothing — the failure mode a user experiences as "the filter is
    // broken" and a developer never sees in a log.
    let decorative: Node<obj> =
        Fuaran.dashboard
            "decorative"
            { Defaults.dashboard with
                Children =
                    [ Fuaran.filters
                          "chips"
                          [ { Defaults.filter<obj> with
                                Name = "region"
                                Label = TextSource.Literal "Region"
                                Kind = FilterField.text "region" } ] ] }

    printfn ""

    match PreEmitValidate.validate decorative with
    | Ok() -> printfn "the incoherent tree unexpectedly validated"
    | Error defects ->
        printfn "A tree can decode and still be incoherent. The pre-emit validator:"

        for d in defects do
            printfn "  %A" d
