module Fuaran.UI.Program.Parity.Tests.LowersToTests

open System
open System.IO
open Microsoft.FSharp.Reflection
open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.Program.Bounded
open Fuaran.UI.Program

// ─── The `lowers-to/` family: the UI action arms on the bounded core ─────────
//
// WIRE_FORMAT.md §30 states, for every `Action` arm, the core arm it lowers to
// and the leaf declaration it carries; its `lowers-to/` vectors pin one reading
// per row. This host certifies them through `UiWitness.view` — the view the
// bounded core folds over, so the reading measured here IS this host's
// lowering rather than a second description of it — after decoding each action
// through `UiWitness.decodeAction`, the entry point the program wire uses.
//
// The expected readings are hand-authored in the corpus and emitted by no host:
// nothing here writes one, and the comparison is structural (member order
// ignored, §30.2), so no encoder of this repository stands between the vector
// and the verdict.
//
// The vectors live in the TREE wire corpus, not the program specification's:
// the table is the action vocabulary's own fact. That corpus is a sibling clone
// and a BUILD INPUT here (the roster marks this suite `requiresCorpus`); its
// absence FAILS rather than skips, for the reason `FixtureIo` gives.

/// The tree wire corpus: `FUARAN_WIRE_FIXTURES`, else the nearest
/// `wire-format-fixtures/` above this source file.
let wireCorpusRoot: string =
    match Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES" with
    | null
    | "" ->
        let rec walk (dir: DirectoryInfo) =
            match dir with
            | null -> None
            | d ->
                let candidate = Path.Combine(d.FullName, "wire-format-fixtures")

                if File.Exists(Path.Combine(candidate, "manifest.json")) then
                    Some candidate
                else
                    walk d.Parent

        match walk (DirectoryInfo __SOURCE_DIRECTORY__) with
        | Some root -> root
        | None ->
            failwith
                "the wire-format conformance corpus was not found above this source file. It is a sibling clone \
                 and a BUILD INPUT to this suite — clone it beside this repository, or point FUARAN_WIRE_FIXTURES \
                 at it. This suite fails rather than skipping: a conformance check that passes when its oracle \
                 is missing is worse than no check."
    | declared -> Path.GetFullPath declared

let private family: string = Path.Combine(wireCorpusRoot, "lowers-to")

let private readJson (path: string) : JVal =
    if not (File.Exists path) then
        failwithf "the lowers-to family names '%s', which is not present" path

    match Json.parse (File.ReadAllText path) with
    | Ok value -> value
    | Error e -> failwithf "'%s' is not JSON: %s" path e

let private memberOf (key: string) (value: JVal) : JVal =
    match value with
    | JObj members ->
        match members |> List.tryFind (fun (k, _) -> k = key) with
        | Some(_, v) -> v
        | None -> failwithf "no member '%s'" key
    | _ -> failwithf "not an object where '%s' was expected" key

let private str (value: JVal) : string =
    match value with
    | JStr s -> s
    | other -> failwithf "expected a string, found %A" other

let private items (value: JVal) : JVal list =
    match value with
    | JArr xs -> xs
    | other -> failwithf "expected an array, found %A" other

/// §30.2's equality: JSON values with object members compared order-free.
let rec private normalise (value: JVal) : JVal =
    match value with
    | JObj members ->
        members
        |> List.map (fun (k, v) -> k, normalise v)
        |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
        |> JObj
    | JArr xs -> JArr(List.map normalise xs)
    | other -> other

/// This host's reading of the arm an action lowers to (§30.2), taken from the
/// UI witness's view. Total over the view: the three arms §30.1 never targets
/// are a lowering defect, reported by name.
///
/// Phase 2106 — `Confirm` is the one round-trip arm: its gesture lowers to the
/// question's leaf, and its reading also carries `answer`, the reading of what
/// the ANSWER event lowers to — read through the same witness, off the answer
/// the loop folds (`UiWitness.answer`), so the vector certifies the code the
/// loop runs rather than a description of it.
let rec reading (action: Action<obj>) : JVal =
    let answer =
        match action with
        | Action.Confirm _ -> [ "answer", reading (UiWitness.answer "" true action) ]
        | _ -> []

    match UiWitness.view action with
    | ActionView.Sequence members -> JObj [ "arm", JStr "Sequence"; "members", JArr(List.map reading members) ]
    | ActionView.Assign(key, _, from) ->
        JObj [ "arm", JStr "Assign"; "from", JBool(Option.isSome from); "key", JStr key ]
    | ActionView.Call(endpoint, declaresTarget) ->
        JObj
            [ "arm", JStr "Call"
              "declaresTarget", JBool declaresTarget
              "endpoint", JStr endpoint ]
    | ActionView.Leaf declaration ->
        // Phase 2194 — `opaque` is present exactly when the leaf declares
        // itself an escape (§30.2), so a leaf that declares nothing and one
        // that cannot be analysed never read alike.
        let opaque =
            match declaration.Opaque with
            | Some o -> [ "opaque", JObj [ "name", JStr o.Name; "reason", JStr o.Reason ] ]
            | None -> []

        JObj(
            [ "arm", JStr "Leaf"
              "effectKinds", JArr(declaration.EffectKinds |> List.map JStr)
              "hostCalls",
              JArr(
                  declaration.HostCalls
                  |> List.map (fun call -> JObj [ "channel", JStr call.Channel; "name", JStr call.Name ])
              ) ]
            @ opaque
            @ answer
        )
    // Reached only through a confirm's answer (above): §30.1 targets `Choose`
    // there and nowhere else, which the vectors pin.
    | ActionView.Choose(_, whenTrue, whenFalse, _) ->
        JObj
            [ "arm", JStr "Choose"
              "whenFalse", reading whenFalse
              "whenTrue", reading whenTrue ]
    | ActionView.Require _ -> failwith "the UI witness lowered an action to Require, which §30.1 never targets"
    | ActionView.Repeat _ -> failwith "the UI witness lowered an action to Repeat, which §30.1 never targets"
    | ActionView.Each _ -> failwith "the UI witness lowered an action to Each, which §30.1 never targets"

type private Vector =
    {
        Name: string
        Arm: string
        Action: JVal
        /// Phase 2198 — the tree the action sits in, for the one arm whose
        /// lowering reads one (`CommitLocal`, §30.1): its key is found there.
        Tree: JVal option
        Expected: JVal
    }

let private load () : string list * Vector list =
    let manifest = readJson (Path.Combine(family, "manifest.json"))

    let arms = memberOf "arms" manifest |> items |> List.map str

    let vectors =
        memberOf "vectors" manifest
        |> items
        |> List.map (fun entry ->
            let file = readJson (Path.Combine(family, str (memberOf "file" entry)))

            { Name = str (memberOf "name" entry)
              Arm = str (memberOf "arm" entry)
              Action = memberOf "action" file
              Tree =
                match file with
                | JObj members -> members |> List.tryFind (fun (k, _) -> k = "tree") |> Option.map snd
                | _ -> None
              Expected = memberOf "lowersTo" file })

    arms, vectors

let private decodeOrFail (vector: Vector) : Action<obj> =
    match UiWitness.decodeAction vector.Action with
    | Ok action -> action
    | Error refusal -> failwithf "%s: the action does not decode: %A" vector.Name refusal

/// The action as the bounded path lowers it (Phase 2198): resolved against the
/// vector's tree, decoded by this host's own node decoder, when it carries one.
let private lowered (vector: Vector) : Action<obj> =
    let action = decodeOrFail vector

    match vector.Tree with
    | None -> action
    | Some tree ->
        match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (Canon.render tree) with
        | Ok root -> UiWitness.lowerCommits root action
        | Error err -> failwithf "%s: the tree does not decode: %A" vector.Name err

/// The case name of a decoded action, read off the closed union itself.
let private caseName (action: Action<obj>) : string =
    let case, _ = FSharpValue.GetUnionFields(box action, typeof<Action<obj>>)
    case.Name

[<Tests>]
let tests =
    testList
        "lowers-to (WIRE_FORMAT §30)"
        [ test "the manifest's arm set is this host's closed action union, and every arm has a vector" {
              let arms, vectors = load ()

              let union =
                  FSharpType.GetUnionCases(typeof<Action<obj>>)
                  |> Array.map _.Name
                  |> Array.sort
                  |> List.ofArray

              Expect.equal (List.sort arms) union "§30.1 is exhaustive over the union: no arm missing, none extra"

              for arm in arms do
                  Expect.isTrue
                      (vectors |> List.exists (fun v -> v.Arm = arm))
                      $"the arm '{arm}' has at least one lowers-to vector"
          }

          test "every vector decodes to the arm it is filed under" {
              let _, vectors = load ()
              Expect.isNonEmpty vectors "the family enumerates no vector"

              for vector in vectors do
                  Expect.equal (caseName (decodeOrFail vector)) vector.Arm $"{vector.Name}: the decoded arm"
          }

          test "every vector lowers through the UI witness to the reading the table states" {
              let _, vectors = load ()

              for vector in vectors do
                  let actual = reading (lowered vector)

                  Expect.equal
                      (normalise actual)
                      (normalise vector.Expected)
                      $"{vector.Name}: the reading of the core arm it lowers to"
          }

          test "a perturbed reading fails the comparison (the harness can go red)" {
              let _, vectors = load ()
              let vector = vectors |> List.find (fun v -> v.Name = "chain")
              let actual = normalise (reading (decodeOrFail vector))

              let perturbed =
                  match vector.Expected with
                  | JObj members ->
                      members
                      |> List.map (fun (k, v) -> if k = "arm" then k, JStr "Leaf" else k, v)
                      |> JObj
                  | other -> failwithf "the chain vector's reading is not an object: %A" other

              Expect.notEqual actual (normalise perturbed) "a reading naming the wrong core arm is refused"

              let reordered =
                  match vector.Expected with
                  | JObj members -> JObj(List.rev members)
                  | other -> other

              Expect.equal actual (normalise reordered) "member order alone never decides a comparison"
          }

          // Phase 2194 — the go-red half of the `Dispatch` row: the reading of
          // a `Dispatch` viewed as a PLAIN leaf (the view before this phase,
          // declaring nothing) fails the vector, so the row cannot be met by a
          // witness that hides the escape.
          test "a Dispatch viewed as a plain leaf fails its vector (the opaque mark is what the row certifies)" {
              let _, vectors = load ()
              let vector = vectors |> List.find (fun v -> v.Arm = "Dispatch")

              let actual = reading (decodeOrFail vector)

              let plain =
                  match actual with
                  | JObj members -> members |> List.filter (fun (k, _) -> k <> "opaque") |> JObj
                  | other -> failwithf "the Dispatch reading is not an object: %A" other

              Expect.equal (normalise actual) (normalise vector.Expected) "the witness meets the row"
              Expect.notEqual (normalise plain) (normalise vector.Expected) "the same leaf without its mark does not"
          }

          // Phase 2198 — the go-red half of the `CommitLocal` row: the same
          // commit lowered WITHOUT its tree (the view before this phase, a leaf
          // that demands nothing) fails the vector, so the row cannot be met by a
          // witness that leaves the key unnamed.
          test "a commit lowered without its tree fails its vector (the key is what the row certifies)" {
              let _, vectors = load ()
              let vector = vectors |> List.find (fun v -> v.Name = "commit-local")

              Expect.isSome vector.Tree "the commit vector carries the tree its key is found in"

              Expect.equal
                  (normalise (reading (lowered vector)))
                  (normalise vector.Expected)
                  "the witness meets the row"

              Expect.notEqual
                  (normalise (reading (decodeOrFail vector)))
                  (normalise vector.Expected)
                  "the tree-blind leaf does not"
          } ]
