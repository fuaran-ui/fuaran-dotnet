module Fuaran.UI.Tests.FilterEdgeCorpus

// ============================================================================
//  Phase 1784 — the corpus's negative wiring pair, bound to the rule it exists
//  to make a host prove.
//
//  `nodes/filters-param-source-declared.json` and its negative
//  `-undeclared.json` differ in exactly one thing: whether the document's
//  `Filters` node declares the second chip the grid's `Transform` params read.
//  Both are legal wire and both round-trip byte-identically, so the round-trip
//  family certifies the codec and says nothing at all about this. These are the
//  assertions that make the pair load-bearing.
//
//  THREE CLAIMS, and the third is the one that explains why the first two are
//  worth the fixtures:
//
//   1. The control raises no dangling-reference finding. Every declared edge in
//      it names a chip the tree declares.
//
//   2. The negative raises FUARAN075 for `genre` and for nothing else. One
//      defect, one name, in a document that is otherwise the control.
//
//   3. RESOLUTION CANNOT TELL THEM APART. Resolved against the same sources,
//      the two documents' grids return the same rows — because an undeclared
//      chip resolves to nothing exactly as an unset chip does, and the lenient
//      "unset filter ⇒ no constraint" prune then drops the dependent step in
//      both. So the resolver on its own silently returns the UNFILTERED set for
//      a document whose author asked for a filter, and the pre-emit rule is the
//      entire guard against it. That is the measurement, run in both
//      directions: the scoped read is shown to be genuinely narrower first, so
//      "both return every row" cannot pass vacuously on a feed that has no rows
//      to lose.
//
//  Claim 3 is also the answer to "which leg of which host exercises this". The
//  corpus carries no validator FIXTURE family — `validator/` is a coverage
//  declaration and message-parity mechanism over the defect vocabulary, not a
//  family of documents — so a host's answer to this pair is visible only where
//  its own pre-emit validator runs. Here, that is this suite.
// ============================================================================

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.PreEmitValidate

let private Control = "filters-param-source-declared.json"
let private Negative = "filters-param-source-undeclared.json"

/// The grid both documents carry; the id is fixed by the fixtures.
let private ConsumerId = "scoped-grid"

/// The chip the control declares and the negative does not.
let private UndeclaredName = "genre"

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

/// Decode a fixture, FAILING rather than skipping when the decode itself is
/// broken — an assertion suite that silently drops the document it is about
/// certifies nothing.
let private decode (dir: string) (name: string) : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText(Path.Combine(dir, name))) with
    | Ok node -> node
    | Error e -> failtestf "%s failed to decode: %s at %s" name e.Code e.Path

let private danglingNames (tree: Node<obj>) : string list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error defects ->
        defects
        |> List.choose (fun d ->
            match d with
            | PreEmitDefect.DanglingFilterReference(reader, name) when reader = ConsumerId -> Some name
            | _ -> None)
        |> List.sort

let private allDefects (tree: Node<obj>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error defects -> defects

/// The consumer's row feed, straight out of the decoded document.
let private consumerSource (name: string) (tree: Node<obj>) : Binding<Row seq> =
    let rec find (n: Node<obj>) : Binding<Row seq> option =
        match n.Kind with
        | NodeKind.DataGrid spec when n.Id = ConsumerId -> Some spec.Source
        | NodeKind.Box box -> box.Children |> List.tryPick find
        | _ -> None

    match find tree with
    | Some b -> b
    | None -> failtestf "%s carries no '%s' DataGrid — the fixture's shape has moved" name ConsumerId

let private rowCount (name: string) (sources: BindingResolver.BindingSources) (binding: Binding<Row seq>) : int =
    match BindingResolver.resolve sources binding with
    | BindingResolver.Resolved rows -> Seq.length rows
    | other -> failtestf "%s: expected the feed to resolve, got %A" name other

let private withRegion (value: string) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        Filters = Map.ofList [ "region", (box value |> Unchecked.nonNull) ] }

[<Tests>]
let tests =
    testList
        "Phase 1784 — the corpus's negative wiring pair"
        [ test "the control's declared edges all name a chip the document declares" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  let tree = decode d Control

                  Expect.isEmpty (danglingNames tree) "the control is fully wired, so no edge is grounded in nothing"

                  // And the control is clean OUTRIGHT, not merely free of this
                  // one code: a control carrying some other defect would make
                  // the negative's single finding hard to attribute.
                  Expect.isEmpty (allDefects tree) "the control raises no pre-emit defect at all"
          }

          test "the negative raises FUARAN075 for the undeclared chip, and for nothing else" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  let tree = decode d Negative

                  Expect.equal
                      (danglingNames tree)
                      [ UndeclaredName ]
                      "the declared edge on the undeclared chip is the finding"

                  // Exactly one defect, so the fixture cannot pass by raising
                  // something else that happens to contain the right case.
                  Expect.equal
                      (allDefects tree)
                      [ PreEmitDefect.DanglingFilterReference(ConsumerId, UndeclaredName) ]
                      "one document, one defect — the pair isolates a single variable"
          }

          test "the negative's code and severity are the vocabulary's" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  let code, severity, _ =
                      PreEmitValidate.describe (PreEmitDefect.DanglingFilterReference(ConsumerId, UndeclaredName))

                  Expect.equal code "FUARAN075" "the rule that names this defect"
                  Expect.equal severity DefectSeverity.Error "an unreachable edge is an error, never a warning"

                  // Tie the projection to the document rather than to a
                  // hand-built tree: the fixture really does produce this code.
                  let emitted =
                      decode d Negative
                      |> allDefects
                      |> List.map (fun x ->
                          let c, s, _ = PreEmitValidate.describe x
                          c, s)

                  Expect.equal
                      emitted
                      [ "FUARAN075", DefectSeverity.Error ]
                      "from the fixture, not from a fixture-shaped tree"
          }

          test "RESOLUTION cannot tell the two documents apart — the silent no-op" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  let control = consumerSource Control (decode d Control)
                  let negative = consumerSource Negative (decode d Negative)

                  // THE VACUITY CHECK, first. If a bound chip does not actually
                  // narrow the feed, the equality below says nothing: two feeds
                  // that can never be scoped are trivially equal.
                  let unscoped = rowCount Control BindingResolver.empty control
                  let scoped = rowCount Control (withRegion "emea") control

                  Expect.isGreaterThan unscoped scoped "a bound chip must genuinely narrow the feed"

                  // Now the finding. `genre` is DECLARED in the control and
                  // ABSENT from the negative, and neither is bound in these
                  // sources — so if resolution distinguished the two, these
                  // counts would differ.
                  Expect.equal
                      (rowCount Negative BindingResolver.empty negative)
                      unscoped
                      "an undeclared chip prunes its step exactly as an unset one does — the unfiltered set"

                  Expect.equal
                      (rowCount Negative (withRegion "emea") negative)
                      scoped
                      "and it does so under a bound sibling chip too"
          } ]
