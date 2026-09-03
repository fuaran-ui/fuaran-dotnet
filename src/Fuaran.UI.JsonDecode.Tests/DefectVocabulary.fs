module Fuaran.UI.JsonDecode.Tests.DefectVocabulary

// ============================================================================
//  The canonical pre-emit DEFECT VOCABULARY, generated from the F# reference.
//
//  Phase 664 made `PreEmitValidate.describe` the single exhaustive projection
//  from a defect to its (code, severity, message). This walks the defect DU by
//  reflection, builds one representative instance per case, and asks `describe`
//  — so the vocabulary is DERIVED from the implementation rather than restated
//  beside it. A new defect case appears in the emitted artefact with no edit
//  here, which is the whole point: the previous state of the world was five
//  hosts each implementing whichever rules they happened to port, with nothing
//  measuring the gap.
//
//  Why reflection rather than a hand-listed table: a hand-listed table is a
//  second source of truth that drifts silently, and drifting silently is the
//  exact defect this phase exists to close. Reflection is confined to this test
//  project on purpose — it never enters the shipped `Fuaran.UI` package, where
//  it would be a Fable portability hazard.
// ============================================================================

// 3261/3264 — reflection's `obj`-returning surface (`PropertyInfo.GetValue`, `MakeUnion`,
// `MakeTuple`) is nullable to F# 10's nullness checker, while `sentinel` is
// declared to return a plain `obj`. Every value it actually produces is non-null
// — `list.Empty` and `option.None` are real boxed values, and the `failwithf`
// arm is how an unhandled field type exits rather than a null slipping through —
// so the suppression states a posture rather than hiding one. Same shape as the
// file-scoped suppression in `Fuaran.UI.Renderer.Core/Sanitize.fs`.
#nowarn "3261"
#nowarn "3264"

open System
open System.Text
open FSharp.Reflection

open Fuaran.UI

/// One entry of the vocabulary: a code, its severity, and the defect cases that
/// raise it. Several DU cases may legitimately share a code.
type VocabularyEntry =
    {
        Code: string
        Severity: string
        Cases: string list
        /// `describe`'s message rendered from sentinel arguments, so the SHAPE of
        /// the message is visible without pretending a real node id was involved.
        MessageShape: string
    }

/// A representative value for a defect field. The values are deliberately
/// recognisable rather than realistic — a manifest that carried plausible node
/// ids would read as though it recorded real defects.
let rec private sentinel (fieldName: string) (t: Type) : obj =
    if t = typeof<string> then
        box ("<" + fieldName + ">")
    elif t = typeof<int> then
        box 0
    elif t = typeof<bool> then
        box false
    elif t = typeof<float> then
        // Phase 1130 — `RatingValueOutOfScale` carries the offending value.
        // Zero renders as a bare `0` through `%g`, which is stable across
        // cultures and across the two pipelines; a non-zero sentinel would
        // print differently under a comma-decimal locale and make the emitted
        // vocabulary machine-dependent.
        box 0.0
    elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<_ list> then
        // An empty list: every message that consumes one does so through
        // `List.length`, so an empty list renders a stable `0`.
        let empty = typedefof<_ list>.MakeGenericType(t.GetGenericArguments())
        empty.GetProperty("Empty").GetValue(null)
    elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<_ option> then
        // `None` — the omitted shape, which is what a defect message renders
        // when the optional detail is absent.
        let opt = typedefof<_ option>.MakeGenericType(t.GetGenericArguments())
        opt.GetProperty("None").GetValue(null)
    elif FSharpType.IsTuple t then
        let parts =
            FSharpType.GetTupleElements t |> Array.map (fun e -> sentinel fieldName e)

        FSharpValue.MakeTuple(parts, t)
    elif FSharpType.IsUnion(t, true) then
        // A nested defect DU (`EditDefect`, `SortDefect`): the FIRST case is
        // enough, because the outer case's CODE does not vary with it — only
        // the message tail does, and the manifest tracks codes.
        let case = (FSharpType.GetUnionCases(t, true))[0]

        let args = case.GetFields() |> Array.map (fun f -> sentinel f.Name f.PropertyType)

        FSharpValue.MakeUnion(case, args, true)
    else
        failwithf
            "DefectVocabulary: no sentinel for field '%s' of type %s — add one rather than letting the vocabulary silently omit its case"
            fieldName
            t.FullName

/// Every entry, derived by asking `describe` about one instance of each case.
let entries () : VocabularyEntry list =
    FSharpType.GetUnionCases(typeof<PreEmitValidate.PreEmitDefect>, true)
    |> Array.toList
    |> List.map (fun case ->
        let args = case.GetFields() |> Array.map (fun f -> sentinel f.Name f.PropertyType)

        let instance =
            FSharpValue.MakeUnion(case, args, true) :?> PreEmitValidate.PreEmitDefect

        let code, severity, message = PreEmitValidate.describe instance
        case.Name, code, string severity, message)
    // Several cases may share a code; the entry carries all of them, so a
    // reader can see that `FUARAN082` is raised from more than one place.
    |> List.groupBy (fun (_, code, _, _) -> code)
    |> List.map (fun (code, group) ->
        let _, _, severity, message = List.head group

        { Code = code
          Severity = severity
          Cases = group |> List.map (fun (name, _, _, _) -> name) |> List.sort
          MessageShape = message })
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Code, b.Code))

let private escape (s: string) : string =
    let sb = StringBuilder()

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | '\t' -> sb.Append "\\t" |> ignore
        | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

/// The emitted artefact. Hand-written rather than routed through a JSON library
/// so the output shape is pinned here and cannot move under a dependency bump —
/// the same reasoning the canonical encoder is hand-written.
let toJson () : string =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore

    line "{"
    line "  \"version\": 1,"
    line "  \"artefact\": \"defect-vocabulary\","
    line "  \"family\": \"pre-emit\","

    line
        "  \"description\": \"The canonical pre-emit defect vocabulary: every code a conformant host's pre-emit validator may raise, with its severity and the message shape that states the fix. GENERATED from the reference host's `PreEmitValidate.describe` by reflecting over the defect DU — never hand-maintained, so a new defect case appears here without an edit. A host may implement a SUBSET (a headless codec legitimately carries fewer rules, and the native surfaces delegate entirely); what it may not do is diverge silently, which is what the per-host `validator-coverage.json` declarations and their gate exist to prevent.\","

    // The scope note is the honest half of this artefact. The FUARAN code space is
    // SHARED with a second validator family — the reference host's build-time
    // source-AST walker — whose codes are not enumerated here, and which a sibling
    // host can only approximate as tree-time checks because it has no F# AST to
    // walk. That sharing is not cosmetic: sibling hosts today raise codes from both
    // families without distinguishing them, so a host declaring a code absent from
    // `codes` below may be conforming to a reference this file does not describe.
    line
        "  \"scope\": \"PRE-EMIT family only — the tree-time validator a host runs before a tree goes on the wire. The FUARAN code space is shared with a second family (the reference host's build-time source-AST walker) that is NOT enumerated here; a declaration citing a code absent from `codes` must name that family explicitly rather than be read as drift. Enumerating the second family is open work.\","

    line "  \"codes\": ["
    let entries = entries ()

    entries
    |> List.iteri (fun i e ->
        let comma = if i = entries.Length - 1 then "" else ","

        let cases =
            e.Cases |> List.map (fun c -> "\"" + escape c + "\"") |> String.concat ", "

        line (
            sprintf
                "    { \"code\": \"%s\", \"severity\": \"%s\", \"cases\": [%s], \"messageShape\": \"%s\" }%s"
                (escape e.Code)
                (escape e.Severity)
                cases
                (escape e.MessageShape)
                comma
        ))

    line "  ]"
    sb.Append("}\n") |> ignore
    sb.ToString()
