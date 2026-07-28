module Fuaran.UI.Tests.HintHonesty

// ============================================================================
//  The standing guard: every field the UpdateProp hint advertises must be
//  reachable by some op.
//
//  WHY THIS EXISTS. `Introspect.availableFields` is documented as the UpdateProp
//  surface and is what the §4d error hint shows an author after a refused edit.
//  When it names a field no op can reach, the author believes it, retries the
//  path, and reads the second refusal as a fault in their own emission. Two
//  independent model families did exactly that against `Button.Label` — the hint
//  said `Label` was available and the whole `NodeKind.Input` family returned
//  `NotSupportedYet` for every path.
//
//  WHY IT IS A GUARD AND NOT A ONE-OFF FIX. The gap propagated by citation. Each
//  newly-added kind quoted the previous omission as precedent — the source
//  comments said so in as many words — and the chain traced back to `Sparkline`,
//  whose omission was CORRECT (its only field is a Binding slot). A valid
//  special case became a general licence and ran for roughly 250 phase numbers,
//  because every individual instance looked reasonable beside the one before it.
//  A checklist would have been quoted the same way. This test cannot be quoted:
//  it re-derives each answer from the tree.
//
//  THE THREE ROUTES. A name may appear in `availableFields` only if:
//    1. `UpdateProp` sets it (`Ok`, or `TypeMismatch` on our deliberately
//       ill-typed probe value — both prove the path resolves);
//    2. it is a `Binding` slot, so `ReplaceBinding` sets it, and it therefore
//       also appears in `availableBindingSlots`;
//    3. it is reached structurally — the literal `Children` (InsertChild /
//       RemoveNode / MoveNode / ReorderChildren), or a prefix of an
//       `availableNestedPaths` entry (`Columns` ⇒ `Columns[i].Label`).
//  Anything else is advertised and unreachable, and fails.
//
//  SAMPLES COME FROM THE CANONICAL CORPUS, not from hand-written fixtures, so
//  the guard inherits the corpus's own completeness guarantee: WIRE_FORMAT.md
//  §11's forward-coupling rule already requires a fixture for every new
//  NodeKind. A kind cannot be added to the language without arriving here.
// ============================================================================

open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

/// Walk up from the test assembly looking for the shared corpus. The corpus
/// lives beside the language repo rather than inside it, so the depth differs
/// between a local build and CI.
let private corpusNodesDir () : string option =
    let rec probe (dir: DirectoryInfo option) (depth: int) =
        match dir with
        | None -> None
        | Some d when depth > 8 ->
            ignore d
            None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", "nodes")

            if Directory.Exists candidate then
                Some candidate
            else
                probe (Option.ofObj d.Parent) (depth + 1)

    let assemblyDir =
        System.Reflection.Assembly.GetExecutingAssembly().Location
        |> Path.GetDirectoryName
        |> Option.ofObj
        |> Option.map DirectoryInfo

    probe assemblyDir 0

/// Every node of every decodable corpus fixture, grouped by kind name. One
/// sample per kind is enough — `availableFields` dispatches on the kind, and for
/// `Box` it also dispatches on the layout mode, so all encountered variants are
/// kept and each is checked.
let private samplesByKind () : (string * Node<obj> * Node<obj>) list =
    match corpusNodesDir () with
    | None -> []
    | Some dir ->
        Directory.GetFiles(dir, "*.json")
        |> Array.toList
        |> List.collect (fun path ->
            match JsonDecode.decodeNodeObj (File.ReadAllText path) with
            // A fixture that does not decode is a real problem, but it is the
            // decoder suite's problem, not this one's. Skipped, and the
            // non-vacuity test below catches a wholesale failure.
            | Error _ -> []
            | Ok root ->
                Introspect.allNodeIds root
                |> List.choose (fun id -> Introspect.findNode id root)
                // Carry the root so the probe can be applied in situ.
                |> List.map (fun node -> Introspect.kindName node.Kind, node, root))
        |> List.distinctBy (fun (kindName, node, _) ->
            // Keep one sample per (kind, advertised-field-set): Box's surface
            // varies by layout mode, so a single Box sample would under-test it.
            kindName, Introspect.availableFields node.Kind)

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

/// Is `field` reachable by any of the three declared routes?
let private routeFor (root: Node<obj>) (node: Node<obj>) (field: string) : Result<string, string> =
    let bindingSlots = Introspect.availableBindingSlots node.Kind
    let nestedPaths = Introspect.availableNestedPaths node.Kind

    let op = TreeOp.UpdateProp(NodeId node.Id, field, PropValue.Native(nn "probe"))

    match Apply.apply op root with
    | Ok _ -> Ok "UpdateProp"
    | Error err ->
        match err.Code with
        // The path resolved; only the probe value was the wrong shape.
        | ApplyErrorCode.KindMismatch -> Ok "UpdateProp (type-checked)"
        | _ when List.contains field bindingSlots -> Ok "ReplaceBinding"
        | _ when field = "Children" -> Ok "structural ops"
        | _ when nestedPaths |> List.exists (fun p -> p.StartsWith(field + "[")) -> Ok "nested path"
        | code -> Error $"{code}: {err.Message}"

[<Tests>]
let hintHonestyTests =
    testList
        "UpdateProp hint honesty"
        [ test "the corpus is actually being read (guard against a vacuous pass)" {
              // Without this, a wrong corpus path turns every assertion below
              // into a loop over zero items, and the suite goes green whilst
              // testing nothing at all.
              let samples = samplesByKind ()

              Expect.isSome (corpusNodesDir ()) "wire-format-fixtures/nodes must be locatable from the test assembly"

              Expect.isGreaterThan
                  (List.length samples)
                  20
                  "expected samples from the corpus covering at least 20 distinct kind surfaces"
          }

          test "every advertised field is reachable by a declared route" {
              let failures =
                  samplesByKind ()
                  |> List.collect (fun (kindName, node, root) ->
                      Introspect.availableFields node.Kind
                      |> List.choose (fun field ->
                          match routeFor root node field with
                          | Ok _ -> None
                          | Error reason -> Some $"  {kindName}.{field} — advertised but unreachable ({reason})"))
                  |> List.distinct

              if not failures.IsEmpty then
                  let detail = String.concat "\n" failures

                  failtestf
                      "availableFields advertises %d field(s) no op can reach.\n\
                       Either wire the field, or remove it from the UpdateProp surface — an\n\
                       unreachable entry sends the author into a retry loop against a hint\n\
                       that cannot be satisfied:\n%s"
                      (List.length failures)
                      detail
          }

          test "every declared binding slot is a real slot" {
              // The mirror obligation. `availableBindingSlots` is the second
              // route the test above trusts, so an entry there that
              // `ReplaceBinding` refuses would launder an unreachable field
              // into a pass.
              let failures =
                  samplesByKind ()
                  |> List.collect (fun (kindName, node, root) ->
                      Introspect.availableBindingSlots node.Kind
                      |> List.choose (fun slot ->
                          let op =
                              TreeOp.ReplaceBinding(NodeId node.Id, slot, Binding.Static(Some(nn "probe")))

                          match Apply.apply op root with
                          | Ok _ -> None
                          | Error err ->
                              match err.Code with
                              // The slot exists; the probe binding is the wrong
                              // payload type, which is what we expect.
                              | ApplyErrorCode.KindMismatch -> None
                              | ApplyErrorCode.SlotNotFound ->
                                  Some $"  {kindName}.{slot} — declared slot does not exist"
                              | code -> Some $"  {kindName}.{slot} — {code}: {err.Message}"))
                  |> List.distinct

              if not failures.IsEmpty then
                  failtestf
                      "availableBindingSlots declares slot(s) ReplaceBinding cannot reach:\n%s"
                      (String.concat "\n" failures)
          }

          test "coverage report — which kinds the corpus exercises" {
              // Not an assertion, a visible inventory. The count is the thing a
              // reader should sanity-check when a new kind lands: if the corpus
              // gained a fixture and this number did not move, the fixture is
              // not reaching the decoder.
              let kinds =
                  samplesByKind () |> List.map (fun (k, _, _) -> k) |> List.distinct |> List.sort

              printfn "  kinds covered by the corpus (%d): %s" (List.length kinds) (String.concat ", " kinds)
              Expect.isGreaterThan (List.length kinds) 15 "corpus should exercise a broad kind set"
          } ]
