module Fuaran.UI.Tests.AdapterOptionsIsolation

// ============================================================================
//  Phase 1594 — the `OnReady` raw-handle escape valve is AUTHORING-ONLY by
//  construction, and this file is the proof.
//
//  `AgAdapterOptions<'Msg>` carries hooks that hand a host the grid / chart
//  library's own instance. The whole safety story is that no decoded tree can
//  supply or trigger one — and the phase deliberately chose the placement that
//  makes that STRUCTURAL rather than behavioural. `GridSpec.OnRowClick` is
//  authoring-only because a closure cannot cross the wire (an argument about
//  what a decoder can DO); the options record is authoring-only because it is
//  not a member of any wire-decoded type at all (a fact about what the types
//  ARE). The second is checkable by inspection, so it is checked here.
//
//  Three claims, in increasing strength:
//    1. `GridSpec` / `ChartSpec` gained no ready-hook member.
//    2. No type in the wire-decoded closure of `Node<'Msg>` mentions the
//       options record anywhere in a field's type.
//    3. It could not, even in principle: the record is declared in the renderer
//       assembly, and the wire assembly does not reference the renderer.
//
//  The last two tests exist because a reflective walk that silently found
//  nothing would pass all three — so the closure is pinned as non-trivial and
//  the detector is shown to fire on a type that DOES mention the record.
// ============================================================================

open System
open System.Collections.Generic
open Expecto
open FSharp.Reflection
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// The open generic definition, so a closed `AgAdapterOptions<Msg>` at any
/// instantiation matches.
let private optionsDef = typedefof<AgAdapter.AgAdapterOptions<obj>>

/// The assembly holding the wire types — `Fuaran.UI`.
let private wireAssembly = typeof<Node<obj>>.Assembly

/// The assembly holding the options record — `Fuaran.UI.Renderer`.
let private rendererAssembly = optionsDef.Assembly

/// A type and every type mentioned inside it (generic arguments, array element
/// types), transitively. `Node list` mentions `Node`; `AgAdapterOptions<Msg>
/// option` mentions `AgAdapterOptions<Msg>`. Terminates because a closed type's
/// argument tree is finite.
let rec private mentions (t: Type) : Type seq =
    seq {
        yield t

        if t.IsGenericType then
            yield! t.GetGenericArguments() |> Seq.collect mentions

        if t.IsArray then
            // `Option.ofObj` rather than a `null` pattern: under the 10.0.3xx compiler the
            // identifier branch of `match … with | null -> … | element -> …` is not narrowed for
            // `Type | null`, so the recursive call is a nullness error (FS3261) under this repo's
            // `TreatWarningsAsErrors`. `global.json` rolls forward on the feature band, so the
            // stricter compiler is in scope for every machine that has one.
            match Option.ofObj (t.GetElementType()) with
            | None -> ()
            | Some element -> yield! mentions element
    }

/// The declared field types of a record or union case; empty for anything else.
let private fieldTypes (t: Type) : (string * Type)[] =
    if FSharpType.IsRecord(t, true) then
        FSharpType.GetRecordFields(t, true)
        |> Array.map (fun p -> p.Name, p.PropertyType)
    elif FSharpType.IsUnion(t, true) then
        FSharpType.GetUnionCases(t, true)
        |> Array.collect (fun uc -> uc.GetFields() |> Array.map (fun p -> uc.Name + "." + p.Name, p.PropertyType))
    else
        [||]

/// Every wire-assembly type reachable from `Node<'Msg>` through record fields
/// and union-case fields — the closure of what a decoded tree is made of.
let private wireClosure: Type list =
    let seen = HashSet<Type>()

    let rec visit (t: Type) =
        for candidate in mentions t do
            if candidate.Assembly = wireAssembly && seen.Add candidate then
                for _, fieldType in fieldTypes candidate do
                    visit fieldType

    visit typeof<Node<obj>>
    List.ofSeq seen

/// Does this type mention the options record anywhere?
let private mentionsOptions (t: Type) =
    mentions t
    |> Seq.exists (fun x -> x = optionsDef || (x.IsGenericType && x.GetGenericTypeDefinition() = optionsDef))

/// Names a wire spec must not carry — the valve lives on the adapter's
/// constructor, never on a decoded spec.
let private readyHookNames =
    set [ "OnReady"; "OnGridReady"; "OnChartReady"; "OnVisReady" ]

[<Tests>]
let tests =
    testList
        "AdapterOptionsIsolation"
        [ test "GridSpec and ChartSpec carry no ready-hook member" {
              let offenders =
                  [ "GridSpec", typeof<GridSpec<obj>>; "ChartSpec", typeof<ChartSpec<obj>> ]
                  |> List.collect (fun (label, t) ->
                      fieldTypes t
                      |> Array.toList
                      |> List.map fst
                      |> List.filter readyHookNames.Contains
                      |> List.map (fun f -> label + "." + f))

              Expect.isEmpty
                  offenders
                  "a ready hook reached a wire-decoded spec — the valve must stay on AgAdapterOptions, which the \
                   constructor supplies"
          }

          test "no wire-decoded type mentions AgAdapterOptions" {
              let offenders =
                  wireClosure
                  |> List.collect (fun t ->
                      fieldTypes t
                      |> Array.toList
                      |> List.filter (snd >> mentionsOptions)
                      |> List.map (fun (name, _) -> t.Name + "." + name))

              Expect.isEmpty
                  offenders
                  "AgAdapterOptions became reachable from a decoded tree — the escape valve is no longer \
                   authoring-only by construction"
          }

          test "the wire assembly cannot reference the renderer that declares the options" {
              Expect.notEqual rendererAssembly wireAssembly "AgAdapterOptions must not live in the wire assembly"

              let referenced =
                  wireAssembly.GetReferencedAssemblies() |> Array.map (fun a -> a.Name)

              Expect.isFalse
                  (referenced |> Array.contains (rendererAssembly.GetName().Name))
                  "the wire assembly gained a reference to the renderer — the structural argument for the valve \
                   being unreachable from a decoded tree rests on this edge not existing"
          }

          // ── The two probe checks. A walk that found nothing, or a detector
          //    that never fires, would pass every assertion above vacuously.
          test "the wire closure is non-trivial and contains the two specs it is about" {
              Expect.isGreaterThan (List.length wireClosure) 50 "the reflective walk collapsed — it proves nothing"

              Expect.contains wireClosure typeof<GridSpec<obj>> "GridSpec absent from the walk"
              Expect.contains wireClosure typeof<ChartSpec<obj>> "ChartSpec absent from the walk"
          }

          test "the detector fires on a type that does mention the options record" {
              Expect.isTrue
                  (mentionsOptions typeof<AgAdapter.AgAdapterOptions<obj> option>)
                  "the mention detector cannot see the options record through an option — every negative result \
                   above would be vacuous"

              Expect.isFalse (mentionsOptions typeof<GridSpec<obj>>) "GridSpec must not mention the options record"
          } ]
