module Fuaran.UI.Tests.HostTypeDecoder

// ============================================================================
//  Phase 1789 — the decoder has no entry point for a type the IDL gives no
//  wire position.
//
//  `CellValue` is a HOST type. A grid column's value is the closure
//  `Row -> CellValue` (`Vocabulary.fs`, the `value` slot on the column
//  record), so what crosses the wire is the `"<closure>"` sentinel and nothing
//  else: the IDL gives `CellValue` no wire position, `manifest.json`
//  enumerates no such discriminator family, and no corpus vector carries one.
//  Yet `JsonDecode.fs` carried a private `decodeCellValue` — forty lines
//  parsing a `$type`-dispatched shape that no position accepts and that
//  nothing called. It was residue of an earlier design in which a column's
//  value was data, and it read to an auditor as evidence of a wire shape that
//  does not exist.
//
//  Deleting it is a one-commit act. Keeping it deleted is not: the same
//  reasoning that produced it once ("every DU in the vocabulary needs a
//  decoder arm") will produce it again, and neither a compile nor a
//  conformance run would notice, because an UNCALLED private decoder is
//  invisible to both. So the guard is this source read, which is the only
//  instrument that can see a function nobody invokes.
//
//  WHAT IT ASSERTS. It extracts the declared RESULT TYPE of every decoder in
//  `JsonDecode.fs` — the file's one declaration shape,
//  `let private decodeX (path: string) (j: Json) : Result<T, DecodeError> =` —
//  and requires that none of them is a host-prelude type the IDL gives no wire
//  position. The set is the one `Fuaran.UI.Idl.Vocabulary` names where it
//  explains why a function-typed slot's argument falls back to `obj`:
//  `BindingContext`, `ErrorPayload`, `CellValue`, `FileSelection`.
//
//  VACUITY — and it is the whole reason the probe has a positive control. A
//  pattern that matches nothing passes every assertion it makes, and the edit
//  most likely to blind this one is a harmless-looking change to the decoder's
//  declaration style. So the test also requires that the extracted set is
//  large (a floor of 100, against the 143 standing when this phase shipped)
//  and that it CONTAINS `CellFormat` and `ColumnWidth` — the grid column's two
//  wire-carried companions, which take exactly the declaration shape a
//  restored `decodeCellValue` would take. If those two stop being found, the
//  probe has gone blind and says so, rather than reporting a clean sweep of
//  nothing.
//
//  THE GO-RED. Restore `decodeCellValue` under any name — it is the RESULT
//  TYPE that is read, not the identifier — and this fails, naming the binding
//  and the type. Move every decoder out of the matched shape and the
//  positive-control arm fails instead.
// ============================================================================

open System.IO
open System.Text.RegularExpressions
open Expecto

/// The host-prelude DUs the IDL gives no wire position. `Vocabulary.fs` names
/// this set where it declares the function-typed slots: where an argument's
/// host type is not IDL-declared, the slot takes `obj` in that position. A type
/// in this set has no `$type` dispatch, no corpus vector and no decoder.
let private hostOnlyTypes =
    [ "BindingContext"; "CellValue"; "ErrorPayload"; "FileSelection" ]

/// Two types that ARE wire-carried and DO have decoders, in the same file and
/// the same declaration shape. They are the probe's positive control.
let private wireCarriedControls = [ "CellFormat"; "ColumnWidth" ]

/// 143 decoders stood when this phase shipped. The floor sits deliberately
/// below that: the assertion is "the probe still sees the file", not "the file
/// has not changed".
[<Literal>]
let private DecoderFloor = 100

let private decoderDecl =
    Regex(
        @"^let\s+private\s+(?<name>\w+)\s*(?:\([^)]*\)\s*)+:\s*Result<(?<result>.+?),\s*DecodeError>\s*=",
        RegexOptions.Multiline
    )

[<Tests>]
let tests =
    testList
        "Phase 1789 - host-only types have no decoder"
        [ test "JsonDecode declares no decoder returning a type the IDL gives no wire position" {
              let root =
                  match Fuaran.Tests.CorpusRoot.tryRepoRoot () with
                  | Some r -> r
                  | None -> failtest "this assembly is not inside the repo, so the decoder's source cannot be read"

              let source = Path.Combine(root, "src", "Fuaran.UI.Ops", "JsonDecode.fs")
              Expect.isTrue (File.Exists source) (sprintf "the decoder source is not at %s" source)

              let declared =
                  decoderDecl.Matches(File.ReadAllText source)
                  |> Seq.map (fun m -> m.Groups["name"].Value, m.Groups["result"].Value.Trim())
                  |> Seq.toList

              // (1) the probe sees the file at all.
              Expect.isGreaterThanOrEqual
                  (List.length declared)
                  DecoderFloor
                  (sprintf
                      "only %d decoder declarations matched in %s - the declaration shape has moved and this probe is blind"
                      (List.length declared)
                      source)

              // (2) the probe sees the exact shape a restored host-type decoder
              //     would take.
              let results = declared |> List.map snd |> Set.ofList

              for control in wireCarriedControls do
                  Expect.isTrue
                      (Set.contains control results)
                      (sprintf
                          "the positive control '%s' was not found among the matched decoders - the probe no longer recognises the declaration shape it hunts for, so its silence about host-only types means nothing"
                          control)

              // (3) the assertion itself.
              let offenders =
                  declared |> List.filter (fun (_, result) -> List.contains result hostOnlyTypes)

              Expect.isEmpty
                  offenders
                  (sprintf
                      "%s declares a decoder for a host-only type: %s. The IDL gives these types no wire position - no `$type` dispatch, no corpus vector, no position that accepts one - so the decoder is unreachable and reads as evidence of a wire shape that does not exist (Phase 1789)."
                      source
                      (offenders
                       |> List.map (fun (name, result) -> sprintf "%s : Result<%s, _>" name result)
                       |> String.concat ", "))
          } ]

// ============================================================================
//  Phase 1843 — the published schema declares no definition nothing reaches.
//
//  1789 corrected the prose and deleted the dead decoder, and left a third
//  mention standing on purpose: `SchemaGen.fs` emitted a `CellValue` `$defs`
//  entry that no `$ref` named. The same dead shape, in `schema.json` — the
//  artefact a new host implementer is most likely to generate code from. The
//  emission is gone; this pins that it stays gone, and pins the general form
//  rather than the one name, because an unreachable definition of ANY type
//  tells a code generator the same untruth.
//
//  WHAT IT ASSERTS. Reachability, not mere reference: starting from every
//  `$ref` outside `$defs` (the root `oneOf` over `Node` / `TreeOp`), follow
//  `$ref`s through the definitions they name. A definition referenced only by
//  another unreachable one is exactly as dead, and a plain "is it named
//  somewhere" count would pass it.
//
//  It reads the GENERATOR (`SchemaGen.wireFormatSchema`), not the committed
//  corpus file. The committed file is bound to the generator byte-for-byte by
//  the stale-schema guard in `Fuaran.UI.JsonDecode.Tests`; the two together
//  pin the artefact.
//
//  VACUITY. A walker that finds no root `$ref` reaches nothing and would then
//  report every definition unreachable — loud, not silent. The silent failure
//  is a walker that stops following `$ref`s one level down and still reaches
//  a handful; so the reached set must clear a floor and must contain
//  `CellFormat` and `ColumnWidth`, which no root `$ref` names — each is reached
//  only through nested specs (`ColumnWidth` solely through the erased grid
//  column), several hops from the root.
//
//  THE GO-RED. Restore the `"CellValue", union [...]` entry in SchemaGen's
//  `defs` and this fails twice, naming `CellValue` in both arms.
// ============================================================================

open System.Text.Json

let private defsPrefix = "#/$defs/"

/// Every `#/$defs/<name>` target named anywhere under `element`.
let rec private defRefs (element: JsonElement) : string list =
    match element.ValueKind with
    | JsonValueKind.Object ->
        element.EnumerateObject()
        |> Seq.collect (fun p ->
            match p.Name, p.Value.ValueKind with
            | "$ref", JsonValueKind.String ->
                match p.Value.GetString() with
                | null -> []
                | target when target.StartsWith defsPrefix -> [ target.Substring defsPrefix.Length ]
                | _ -> []
            | _ -> defRefs p.Value)
        |> Seq.toList
    | JsonValueKind.Array -> element.EnumerateArray() |> Seq.collect defRefs |> Seq.toList
    | _ -> []

/// 148 definitions were reachable when this phase shipped. The floor is "the
/// walker still follows `$ref`s through the tree", not "the schema is frozen".
[<Literal>]
let private ReachableFloor = 100

[<Tests>]
let schemaReachability =
    testList
        "Phase 1843 - schema.json declares no unreachable definition"
        [ test "every $defs entry the generator emits is reachable from the schema root" {
              use doc = JsonDocument.Parse Fuaran.UI.Ops.SchemaGen.wireFormatSchema
              let root = doc.RootElement
              let defs = root.GetProperty "$defs"

              let defined = defs.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Set.ofSeq

              let rootRefs =
                  root.EnumerateObject()
                  |> Seq.filter (fun p -> p.Name <> "$defs")
                  |> Seq.collect (fun p -> defRefs p.Value)
                  |> Set.ofSeq

              let rec walk (reached: Set<string>) (frontier: string list) =
                  match frontier with
                  | [] -> reached
                  | name :: rest when reached.Contains name -> walk reached rest
                  | name :: rest ->
                      let next =
                          match defs.TryGetProperty name with
                          | true, def -> defRefs def
                          | false, _ -> []

                      walk (reached.Add name) (next @ rest)

              let reached = walk Set.empty (Set.toList rootRefs)

              // (1) the walker follows `$ref`s through the tree at all.
              Expect.isGreaterThanOrEqual
                  reached.Count
                  ReachableFloor
                  (sprintf
                      "only %d definitions reached from the root %A - the walker no longer follows $refs, so its silence about unreachable definitions means nothing"
                      reached.Count
                      rootRefs)

              for control in wireCarriedControls do
                  Expect.isTrue
                      (reached.Contains control)
                      (sprintf
                          "the positive control '%s' (reachable only through nested specs, several hops down) was not reached - the walk is shallow, so its verdict is not evidence"
                          control)

              // (2) the assertion itself.
              let unreachable = Set.difference defined reached

              Expect.isEmpty
                  unreachable
                  (sprintf
                      "SchemaGen emits $defs no $ref reaches from the schema root: %s. A dead definition in schema.json reads to a host implementer (and to every schema-driven code generator) as a wire shape that does not exist (Phase 1843)."
                      (String.concat ", " unreachable))
          }

          test "no host-only type is emitted as a schema definition" {
              use doc = JsonDocument.Parse Fuaran.UI.Ops.SchemaGen.wireFormatSchema
              let defs = doc.RootElement.GetProperty "$defs"

              let offenders = hostOnlyTypes |> List.filter (fun t -> fst (defs.TryGetProperty t))

              Expect.isEmpty
                  offenders
                  (sprintf
                      "schema.json declares a definition for host-only type(s) %A. The IDL gives these no wire position (Phase 1789), so a definition for one is dead by construction (Phase 1843)."
                      offenders)
          } ]
