module Fuaran.UI.ServerDriven.Tests.GridEditTests

// ============================================================================
//  Phase 1891 — grid edits as `ColumnOp`s.
//
//  Five claims, each stated over the real tier code rather than a model of it:
//
//    (a) THE WIRE. `GridEdit` round-trips every op kind byte for byte and refuses
//        what it cannot apply faithfully. The corpus's `grid-edit/` vectors are
//        what this host emits, and this host agrees with every one of them.
//    (b) THE HISTORY. Undo is the inverse, redo is the op again, the bound folds
//        the oldest edit into the base, and — the law — replaying the chained
//        record from the base reproduces the live table byte for byte, over
//        generated edit / undo / redo scripts.
//    (c) THE REFRESH. A site over an edited table refreshes from the delta the
//        edit's op induces (`OpDerived`), answers exactly what a full evaluation
//        answers, and a site with no row identity declared says it took the
//        reference path.
//    (d) THE COST. One edit to a 10,000-row grid, measured against a full
//        evaluation and against the diffed refresh it replaces, on Core's
//        deterministic rows-evaluated scale (asserted) and wall-clock (printed).
//    (e) THE KIT. The two columnar families run at this tier's own grid-edit
//        generator, which this tier did not have until this phase.
// ============================================================================

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven

module ComputeConf = Fuaran.Compute.Conformance

let private assertAllPassed (context: string) (results: LawResult list) =
    let failures = results |> List.filter (fun r -> not r.Passed)

    if not (List.isEmpty failures) then
        failures
        |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
        |> String.concat "\n"
        |> failtestf "%s failed:\n%s" context

let private ok (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got %A" e

// ---------------------------------------------------------------------------
//  tables
// ---------------------------------------------------------------------------

let private column (name: string) (ty: ColumnType) (cells: Cell list) : Column = Column.ofCells name ty cells |> ok

let private grid (rows: (string * int * Cell) list) : Table =
    { Schema =
        [ Field.create "id" StringType
          Field.create "qty" IntType
          Field.create "note" StringType ]
      Columns =
        [ column "id" StringType (rows |> List.map (fun (k, _, _) -> Str k))
          column "qty" IntType (rows |> List.map (fun (_, q, _) -> Int q))
          column "note" StringType (rows |> List.map (fun (_, _, n) -> n)) ] }

let private small =
    grid
        [ "a", 3, Str "first"
          "b", 5, Null
          "c", 8, Str "third"
          "d", 13, Str "fourth" ]

let private bytesOf (t: Table) : string = ColumnCodec.encode (Embedded t)

let private tableJson (t: Table) : JVal = ColumnCodec.encodeJson (Embedded t)

let private tableOfJson (el: JVal) : Table =
    match ColumnCodec.decodeJson el with
    | Ok(Embedded t) -> t
    | other -> failtestf "not an embedded table: %A" other

let private actor = Actor.Human "grid-edit-tests"

// ---------------------------------------------------------------------------
//  (a) the wire
// ---------------------------------------------------------------------------

let private everyOp: ColumnOp list =
    [ SetCell("qty", 1, Int 21)
      SetCell("note", 0, Null)
      SetColumn(column "qty" IntType [ Int 1; Int 2; Int 3; Int 4 ])
      InsertColumn(1, column "flag" BoolType [ Bool true; Bool false; Bool true; Bool false ])
      RemoveColumn "note"
      AppendRows [ [ "id", Str "e"; "qty", Int 21 ] ]
      ApplyTransform [ Filter(Binary(Gt, Col "qty", Lit(Int 4))) ] ]

/// The refusal token a corpus vector names, one per `ColumnRejection` / `EditRefusal` case.
let private rejectionToken (r: ColumnRejection) : string =
    match r with
    | NoSuchColumn _ -> "noSuchColumn"
    | DuplicateColumn _ -> "duplicateColumn"
    | RowOutOfRange _ -> "rowOutOfRange"
    | CellTypeMismatch _ -> "cellTypeMismatch"
    | ColumnLengthMismatch _ -> "columnLengthMismatch"
    | RowShapeUnknownColumn _ -> "rowShapeUnknownColumn"
    | TransformRejected _ -> "transformRejected"
    | NotInvertible _ -> "notInvertible"

let private refusalToken (r: EditRefusal) : string =
    match r with
    | Refused r -> rejectionToken r
    | NothingToUndo -> "nothingToUndo"
    | NothingToRedo -> "nothingToRedo"

let private wireTests =
    testList
        "the GridEdit wire"
        [ testCase "every op kind round-trips byte for byte, with and without a rowKeyField"
          <| fun () ->
              for op in everyOp do
                  for field in [ Some "id"; None ] do
                      let edit =
                          { Key = "orders"
                            RowKeyField = field
                            Op = op }

                      let bytes = GridEdit.encode edit
                      let back = GridEdit.decode bytes |> ok
                      Expect.equal back edit (sprintf "decode inverts encode: %s" bytes)
                      Expect.equal (GridEdit.encode back) bytes "re-encoding is byte-identical"

          testCase "absence is how 'no identity declared' is spelled — the member is omitted"
          <| fun () ->
              let bytes =
                  GridEdit.encode
                      { Key = "orders"
                        RowKeyField = None
                        Op = RemoveColumn "note" }

              Expect.equal
                  bytes
                  """{"$type":"GridEdit","key":"orders","op":{"$type":"removeColumn","name":"note"}}"""
                  "canonical bytes"

          testCase "the decoder refuses what it could not apply faithfully"
          <| fun () ->
              let refused =
                  [ """{"$type":"GridEdit","key":"k","op":{"$type":"removeColumn","name":"x"},"extra":1}""",
                    "unknown member"
                    """{"$type":"GridEdit","key":"","op":{"$type":"removeColumn","name":"x"}}""", "empty key"
                    """{"$type":"GridEdit","key":"k","op":{"$type":"removeColumn","name":"x"},"rowKeyField":""}""",
                    "empty field"
                    """{"$type":"GridEdit","key":"k","op":{"$type":"removeColumn","name":"x"},"rowKeyField":3}""",
                    "non-string field"
                    """{"$type":"GridEdit","key":"k"}""", "missing op"
                    """{"$type":"GridEdit","key":"k","op":{"$type":"dropTable"}}""", "unknown op"
                    """{"$type":"Edit","key":"k","op":{"$type":"removeColumn","name":"x"}}""", "wrong tag"
                    """[1]""", "not an object"
                    """{""", "not json" ]

              for text, why in refused do
                  match GridEdit.decode text with
                  | Ok e -> failtestf "%s: decoded %A" why e
                  | Error _ -> () ]

// ---------------------------------------------------------------------------
//  the tier's grid-edit generator (also the kit families' witness, below)
// ---------------------------------------------------------------------------

/// Grid edits as an editable grid makes them: cell edits on the value columns and the key, a
/// whole-column replace, appended rows, a column inserted and one removed — with refusals drawn
/// on purpose (a row past the end, a wrong-typed cell, a missing column).
let private gridEditGen () : StreamGen<ColumnOp, Table> =
    { State0 = small
      Op =
        fun r ->
            let kind, r = ConfRng.intBelow 9 r
            let row, r = ConfRng.intBelow 6 r
            let v, r = ConfRng.intBelow 50 r

            match kind with
            | 0
            | 1
            | 2 -> SetCell("qty", row, Int v), r
            | 3 -> SetCell("note", row, (if v % 3 = 0 then Null else Str(sprintf "n%d" v))), r
            | 4 -> SetCell("id", row, Str(sprintf "k%d" v)), r
            | 5 -> AppendRows [ [ "id", Str(sprintf "new%d" v); "qty", Int v ] ], r
            | 6 -> SetCell("qty", row, Str "wrong"), r
            | 7 -> RemoveColumn(if v % 2 = 0 then "note" else "flag"), r
            | _ -> InsertColumn(v % 3, column "flag" BoolType (List.replicate (row % 5) (Bool true))), r }

// ---------------------------------------------------------------------------
//  (b) the history
// ---------------------------------------------------------------------------

let private applyOk op log = GridEditLog.apply op log |> ok |> fst

let private replayed (log: GridEditLog) = GridEditLog.replay log |> ok

/// The law, stated once: the record verifies, and replays from the base to the live table,
/// byte for byte under the canonical column codec.
let private assertReplays (context: string) (log: GridEditLog) =
    Expect.isNone (GridEditLog.verify log) (context + ": the chain verifies")
    Expect.equal (bytesOf (replayed log)) (bytesOf (GridEditLog.table log)) (context + ": replay is the live table")

let private historyTests =
    testList
        "the edit history"
        [ testCase "undo applies the inverse, redo the op, and both are chained"
          <| fun () ->
              let log0 = GridEditLog.create actor 10 small
              let log1 = applyOk (SetCell("qty", 2, Int 99)) log0
              let log2 = applyOk (RemoveColumn "note") log1
              let undone, step = GridEditLog.undo log2 |> ok
              Expect.equal (bytesOf (GridEditLog.table undone)) (bytesOf (GridEditLog.table log1)) "one undo"
              Expect.isTrue (step.Op.IsInsertColumn) "the undo applied the inverse op"
              let undone2 = GridEditLog.undo undone |> ok |> fst
              Expect.equal (bytesOf (GridEditLog.table undone2)) (bytesOf small) "two undos"
              Expect.isFalse (GridEditLog.canUndo undone2) "nothing left to undo"
              let redone = GridEditLog.redo undone2 |> ok |> fst
              Expect.equal (bytesOf (GridEditLog.table redone)) (bytesOf (GridEditLog.table log1)) "a redo"
              Expect.equal (List.length (GridEditLog.records redone)) 5 "every applied op is chained"
              assertReplays "after undo and redo" redone

          testCase "a non-invertible edit applies and ends the undo history there"
          <| fun () ->
              let log =
                  GridEditLog.create actor 10 small
                  |> applyOk (SetCell("qty", 0, Int 1))
                  |> applyOk (AppendRows [ [ "id", Str "z" ] ])

              Expect.equal (GridEditLog.undo log |> Result.map ignore) (Error NothingToUndo) "no undo past an append"
              assertReplays "after an append" log

          testCase "a refused edit leaves the log as it was, and says why"
          <| fun () ->
              let log = GridEditLog.create actor 10 small

              match GridEditLog.apply (SetCell("qty", 40, Int 1)) log with
              | Error(Refused(RowOutOfRange _)) -> ()
              | other -> failtestf "expected a row-out-of-range refusal, got %A" other

              Expect.isEmpty (GridEditLog.records log) "nothing chained"

          testCase "a new edit clears the redo stack"
          <| fun () ->
              let log =
                  GridEditLog.create actor 10 small
                  |> applyOk (SetCell("qty", 0, Int 1))
                  |> GridEditLog.undo
                  |> ok
                  |> fst
                  |> applyOk (SetCell("qty", 1, Int 2))

              Expect.equal (GridEditLog.redo log |> Result.map ignore) (Error NothingToRedo) "redo superseded"

          testCase "the bound folds the oldest edits into the base and the record still replays"
          <| fun () ->
              let mutable log = GridEditLog.create actor 3 small

              for i in 0..9 do
                  log <- applyOk (SetCell("qty", i % 4, Int(100 + i))) log

              Expect.equal (List.length (GridEditLog.records log)) 3 "the record is bounded"
              Expect.equal (GridEditLog.anchor log).Seq 7 "the anchor carries the folded count"
              Expect.notEqual (bytesOf (GridEditLog.baseTable log)) (bytesOf small) "the base moved"
              assertReplays "after folding" log
              // The surviving records kept their hashes: undo is still three deep.
              let u1 = GridEditLog.undo log |> ok |> fst
              let u2 = GridEditLog.undo u1 |> ok |> fst
              let u3 = GridEditLog.undo u2 |> ok |> fst
              Expect.equal (GridEditLog.undo u3 |> Result.map ignore) (Error NothingToUndo) "undo is bounded too"

          testCase "a tampered record is found"
          <| fun () ->
              let log =
                  GridEditLog.create actor 10 small
                  |> applyOk (SetCell("qty", 0, Int 1))
                  |> applyOk (SetCell("qty", 1, Int 2))

              let records = GridEditLog.records log

              let tampered =
                  records
                  |> List.mapi (fun i r ->
                      if i = 0 then
                          { r with Op = SetCell("qty", 0, Int 2) }
                      else
                          r)

              let shape = ChainShape.records StreamConfig.canonical ColumnOps.encode

              Expect.isSome
                  (ChainShape.walk shape OpStream.defaultHash (GridEditLog.anchor log) tampered)
                  "a rewritten op breaks the chain"

          // THE LAW. Generated scripts of edits (every kind, refused ones included), undos and
          // redos, at a small bound so the fold runs: after every gesture the record verifies and
          // replays from its base to the live table, and an undo returns the table the edit left.
          testCase "replay equals the live state over generated edit / undo / redo scripts"
          <| fun () ->
              let mutable folds = 0
              let mutable undos = 0
              let mutable redos = 0

              for seed in 1..60 do
                  let mutable r = ConfRng.ofSeed seed
                  let mutable log = GridEditLog.create actor 6 small
                  let mutable history: Table list = []

                  for _ in 1..25 do
                      let k, r1 = ConfRng.intBelow 10 r
                      r <- r1

                      match k with
                      | 0
                      | 1 ->
                          match GridEditLog.undo log with
                          | Ok(next, _) ->
                              undos <- undos + 1

                              match history with
                              | previous :: rest ->
                                  Expect.equal
                                      (bytesOf (GridEditLog.table next))
                                      (bytesOf previous)
                                      (sprintf "seed %d: undo returns the prior table" seed)

                                  history <- rest
                              | [] -> ()

                              log <- next
                          | Error _ -> ()
                      | 2 ->
                          match GridEditLog.redo log with
                          | Ok(next, _) ->
                              redos <- redos + 1
                              history <- GridEditLog.table log :: history
                              log <- next
                          | Error _ -> ()
                      | _ ->
                          let op, r2 = (gridEditGen ()).Op r
                          r <- r2
                          let before = GridEditLog.table log

                          match GridEditLog.apply op log with
                          | Ok(next, _) ->
                              history <- if GridEditLog.canUndo next then before :: history else []

                              log <- next
                          | Error _ -> ()

                      if (GridEditLog.anchor log).Seq > 0 then
                          folds <- folds + 1

                      assertReplays (sprintf "seed %d" seed) log

              Expect.isGreaterThan undos 20 "the scripts reached undo"
              Expect.isGreaterThan redos 5 "the scripts reached redo"
              Expect.isGreaterThan folds 20 "the scripts reached the bound" ]

// ---------------------------------------------------------------------------
//  (a, continued) the corpus's grid-edit vectors
// ---------------------------------------------------------------------------

let private keyed (op: ColumnOp) =
    { Key = "orders"
      RowKeyField = Some "id"
      Op = op }

let private unkeyed (op: ColumnOp) = { keyed op with RowKeyField = None }

/// One vector per op kind and per refusal class, each with its expected answer computed by this
/// host from the specification's rules as the columnar op substrate implements them.
let private vectorCases: (string * string * GridEdit) list =
    [ "set-cell",
      "a value edit: one row changed by key; the inverse restores the old cell",
      keyed (SetCell("qty", 1, Int 21))
      "set-cell-null", "a cell set to null; the inverse restores the text", keyed (SetCell("note", 0, Null))
      "set-cell-unchanged", "a cell set to the value it holds: the empty delta", keyed (SetCell("qty", 2, Int 8))
      "set-cell-key",
      "an edit to the key column: the old key removed, the new one added",
      keyed (SetCell("id", 3, Str "z"))
      "set-cell-unkeyed",
      "the same value edit on a grid declaring no rowKeyField: the top delta, the reference path named",
      unkeyed (SetCell("qty", 1, Int 21))
      "set-column",
      "a whole column replaced: every row whose cell moved, by key",
      keyed (SetColumn(column "qty" IntType [ Int 3; Int 6; Int 8; Int 0 ]))
      "insert-column",
      "a column inserted: the top delta (the schema moved); the inverse removes it",
      keyed (InsertColumn(1, column "flag" BoolType [ Bool true; Bool false; Bool true; Bool false ]))
      "remove-column", "a column removed: the inverse re-inserts it at its index", keyed (RemoveColumn "note")
      "append-rows",
      "a row appended: added by key; not invertible, so it ends an undo history",
      keyed (AppendRows [ [ "id", Str "e"; "qty", Int 21 ] ])
      "apply-transform",
      "a whole-table transform: the top delta; not invertible",
      keyed (ApplyTransform [ Filter(Binary(Gt, Col "qty", Lit(Int 4))) ])
      "refuse-row-out-of-range", "a cell past the last row is refused", keyed (SetCell("qty", 4, Int 1))
      "refuse-cell-type", "a cell of the wrong type is refused", keyed (SetCell("qty", 0, Str "many"))
      "refuse-no-such-column", "a column that does not exist is refused", keyed (RemoveColumn "price")
      "refuse-duplicate-column",
      "inserting a column that exists is refused",
      keyed (InsertColumn(0, column "qty" IntType [ Int 0; Int 0; Int 0; Int 0 ]))
      "refuse-unknown-row-column",
      "an appended row naming an unknown column is refused",
      keyed (AppendRows [ [ "id", Str "e"; "price", Int 1 ] ]) ]

let private expectedOf (table: Table) (edit: GridEdit) : JVal =
    let before = DataFrame.prepare table

    match ColumnOps.applyPrepared edit.Op before with
    | Error r -> JObj [ "refused", JStr(rejectionToken r) ]
    | Ok after ->
        let delta =
            match edit.RowKeyField with
            | Some field ->
                let idw = RowIdentity.byColumn field
                let d = ColumnOps.deltaOfPrepared idw before edit.Op

                // The prepared form must agree with the table form: the vector pins ONE delta.
                Expect.equal d (ColumnOps.deltaOf idw table edit.Op) "deltaOfPrepared agrees with deltaOf"
                [ "delta", DeltaCodec.encodeJson d ]
            | None ->
                [ "delta", DeltaCodec.encodeJson FullRefresh
                  "referencePath", JStr "noRowIdentityDeclared" ]

        let undo =
            match ColumnOps.invertPrepared edit.Op before with
            | Ok inverse ->
                let undone = ColumnOps.applyPrepared inverse after |> ok

                [ "inverse", ColumnOps.encodeJson inverse
                  "undone", tableJson (DataFrame.toTable undone) ]
            | Error _ -> [ "notInvertible", JBool true ]

        JObj([ "after", tableJson (DataFrame.toTable after) ] @ delta @ undo)

/// A scripted history: edits, undos and redos in order, with the table and both flags after each.
type private Gesture =
    | Edit of GridEdit
    | Undo
    | Redo

let private historyCases: (string * string * Gesture list) list =
    [ "undo-redo",
      "two edits, both undone, a third undo refused, one redone",
      [ Edit(keyed (SetCell("qty", 0, Int 1)))
        Edit(keyed (RemoveColumn "note"))
        Undo
        Undo
        Undo
        Redo ]
      "edit-clears-redo",
      "an undone edit is superseded by a new one",
      [ Edit(keyed (SetCell("qty", 0, Int 1)))
        Undo
        Edit(keyed (SetCell("qty", 1, Int 2)))
        Redo ]
      "append-ends-history",
      "a non-invertible edit applies and nothing before it can be undone",
      [ Edit(keyed (SetCell("qty", 0, Int 1)))
        Edit(keyed (AppendRows [ [ "id", Str "e" ] ]))
        Undo ] ]

let private gestureJson (g: Gesture) : JVal =
    match g with
    | Edit e -> JObj [ "edit", GridEdit.encodeJson e ]
    | Undo -> JObj [ "undo", JBool true ]
    | Redo -> JObj [ "redo", JBool true ]

let private gestureOfJson (el: JVal) : Gesture =
    match el with
    | JObj [ "edit", e ] -> Edit(GridEdit.decodeJson e |> ok)
    | JObj [ "undo", JBool true ] -> Undo
    | JObj [ "redo", JBool true ] -> Redo
    | other -> failtestf "not a gesture: %A" other

let private historyExpected (table: Table) (gestures: Gesture list) : JVal =
    let mutable log = GridEditLog.create actor LiveTransformDefaults.EditHistory table

    gestures
    |> List.map (fun g ->
        let outcome =
            match g with
            | Edit e -> GridEditLog.apply e.Op log
            | Undo -> GridEditLog.undo log
            | Redo -> GridEditLog.redo log

        match outcome with
        | Error r -> JObj [ "refused", JStr(refusalToken r) ]
        | Ok(next, _) ->
            log <- next

            JObj
                [ "canRedo", JBool(GridEditLog.canRedo next)
                  "canUndo", JBool(GridEditLog.canUndo next)
                  "table", tableJson (GridEditLog.table next) ])
    |> JArr

let private vectorsDescription =
    "Grid edits as columnar ops (WIRE_FORMAT.md, \"Edits as column ops - GridEdit\", Phase 1891). NOT YET AN ENUMERATED FAMILY: this directory carries no manifest.json, on the grid-window-writer/ precedent (27.7), so no host's corpusFamilies names it until its hosts adopt it. Emitted by the reference host (fuaran-dotnet: FUARAN_REGEN=1 over the GridEditTests of Fuaran.UI.ServerDriven.Tests), whose answers are the columnar op substrate's. BEHAVIOUR vectors, not byte-parity fixtures: tables are the canonical column codec's embedded form, ops the columnar op codec's, deltas the row-delta codec's, compared after decoding. vectors: apply input.edit.op to input.table. A refused op expects refused, the refusal token. Otherwise expected.after is the table it produced; expected.delta the row delta the op induces under the identity input.edit.rowKeyField declares (scheme column:<field>), or, where it declares none, the top delta with referencePath noRowIdentityDeclared; expected.inverse the op that undoes it and expected.undone the table that inverse restores (equal to input.table), or expected.notInvertible true. histories: from input.table, run input.gestures in order through one edit history - edit applies its op, undo applies the most recent undoable edit's inverse, redo re-applies the most recently undone op; a new edit clears what could be redone, and a non-invertible edit clears what could be undone - and compare each entry of expected.steps: the table and both flags, or refused with nothingToUndo, nothingToRedo or a refusal token."

let private vectorsJson () : JVal =
    JObj
        [ "description", JStr vectorsDescription
          "family", JStr "gridEdit"
          "histories",
          JArr
              [ for id, description, gestures in historyCases ->
                    JObj
                        [ "description", JStr description
                          "expected", JObj [ "steps", historyExpected small gestures ]
                          "id", JStr id
                          "input", JObj [ "gestures", JArr(gestures |> List.map gestureJson); "table", tableJson small ] ] ]
          "vectors",
          JArr
              [ for id, description, edit in vectorCases ->
                    JObj
                        [ "description", JStr description
                          "expected", expectedOf small edit
                          "id", JStr id
                          "input", JObj [ "edit", GridEdit.encodeJson edit; "table", tableJson small ] ] ]
          "version", JInt 1 ]

/// The vectors file as committed: the canonical bytes, indented two spaces, LF, one trailing newline.
let private vectorsText () : string =
    let canonical = Canonical.write Rendering.CrossHost (vectorsJson ())
    use doc = JsonDocument.Parse canonical
    use buffer = new MemoryStream()

    let options =
        JsonWriterOptions(Indented = true, Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    do
        use writer = new Utf8JsonWriter(buffer, options)
        doc.WriteTo writer

    Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n") + "\n"

let private vectorsPath (root: string) =
    Path.Combine(root, "grid-edit", "grid-edit-vectors.json")

let private field (name: string) (el: JVal) : JVal =
    match el with
    | JObj fields ->
        match fields |> List.tryFind (fun (k, _) -> k = name) with
        | Some(_, v) -> v
        | None -> failtestf "missing member %s" name
    | _ -> failtestf "not an object reading %s" name

let private canonical (v: JVal) = Canonical.write Rendering.CrossHost v

let private corpusTests =
    testList
        "the grid-edit corpus vectors"
        [ testCase "the committed vectors are what this host emits (FUARAN_REGEN=1 rewrites them)"
          <| fun () ->
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some root ->
                  let path = vectorsPath root
                  let text = vectorsText ()

                  if Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1" then
                      Directory.CreateDirectory(Path.GetDirectoryName path |> nonNull) |> ignore
                      File.WriteAllText(path, text, UTF8Encoding(false))

                  Expect.isTrue (File.Exists path) (sprintf "%s exists — emit it with FUARAN_REGEN=1" path)
                  Expect.equal (File.ReadAllText path) text "the committed vectors are current"

          testCase "this host agrees with every committed vector"
          <| fun () ->
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some root ->
                  let file = Json.parse (File.ReadAllText(vectorsPath root)) |> ok

                  let vectors =
                      match field "vectors" file with
                      | JArr xs -> xs
                      | _ -> failtest "vectors is not an array"

                  Expect.isGreaterThanOrEqual vectors.Length 15 "a family certified by reading nothing is no family"

                  for v in vectors do
                      let input = field "input" v
                      let table = tableOfJson (field "table" input)
                      let edit = GridEdit.decodeJson (field "edit" input) |> ok

                      Expect.equal
                          (canonical (expectedOf table edit))
                          (canonical (field "expected" v))
                          (sprintf "vector %A" (field "id" v))

                  let histories =
                      match field "histories" file with
                      | JArr xs -> xs
                      | _ -> failtest "histories is not an array"

                  Expect.isGreaterThanOrEqual histories.Length 3 "the undo vectors are read"

                  for h in histories do
                      let input = field "input" h

                      let gestures =
                          match field "gestures" input with
                          | JArr xs -> xs |> List.map gestureOfJson
                          | _ -> failtest "gestures is not an array"

                      Expect.equal
                          (canonical (historyExpected (tableOfJson (field "table" input)) gestures))
                          (canonical (field "steps" (field "expected" h)))
                          (sprintf "history %A" (field "id" h)) ]

// ---------------------------------------------------------------------------
//  (c) the refresh
// ---------------------------------------------------------------------------

let private pipeline =
    [ Derive("double", Binary(Mul, Col "qty", Lit(Int 2)))
      Filter(Binary(Gt, Col "qty", Lit(Int 4))) ]

let private reference (t: Table) =
    LiveTransform.reference pipeline t |> ok

let private evaluate (store: LiveTransformStore) (identity: string) (t: Table) =
    store.Evaluate("orders-grid", identity, pipeline, t) |> ok

let private edit (store: LiveTransformStore) (op: ColumnOp) (log: GridEditLog) =
    LiveTransform.applyEdit store (keyed op) log |> ok |> fst

let private refreshTests =
    testList
        "the op-derived refresh"
        [ testCase "an edit refreshes from its own delta and answers what a full evaluation answers"
          <| fun () ->
              let store = LiveTransformStore(16, "id")
              let log0 = GridEditLog.create actor 10 small
              Expect.isTrue (evaluate store "id" (GridEditLog.table log0)).Primed "the first sight primes"
              let log1 = edit store (SetCell("qty", 0, Int 30)) log0
              let e1 = evaluate store "id" (GridEditLog.table log1)

              match e1.Delta with
              | OpDerived(1, RowSet r) -> Expect.equal r.Rows.Length 1 "one row named"
              | other -> failtestf "expected the op's own delta, got %A" other

              Expect.equal e1.Result (reference (GridEditLog.table log1)) "the full evaluation's answer"

              match e1.Footprint.Recompute with
              | RowsRecomputed n -> Expect.isLessThan n 4 "fewer rows than the table"
              | other -> failtestf "expected a restricted refresh, got %A" other

          testCase "several edits between evaluations compose; undo and redo refresh the same way"
          <| fun () ->
              let store = LiveTransformStore(16, "id")
              let log0 = GridEditLog.create actor 10 small
              evaluate store "id" (GridEditLog.table log0) |> ignore

              let log2 =
                  log0
                  |> edit store (SetCell("qty", 0, Int 30))
                  |> edit store (SetCell("qty", 3, Int 1))

              let e2 = evaluate store "id" (GridEditLog.table log2)

              match e2.Delta with
              | OpDerived(n, _) -> Expect.equal n 2 "both edits composed"
              | other -> failtestf "two edits, one refresh: %A" other

              Expect.equal e2.Result (reference (GridEditLog.table log2)) "after two edits"
              let log3 = LiveTransform.undoEdit store log2 |> ok |> fst
              let e3 = evaluate store "id" (GridEditLog.table log3)
              Expect.isTrue e3.Delta.IsOpDerived "an undo is an edit"
              Expect.equal e3.Result (reference (GridEditLog.table log3)) "after undo"
              let log4 = LiveTransform.redoEdit store log3 |> ok |> fst
              let e4 = evaluate store "id" (GridEditLog.table log4)
              Expect.isTrue e4.Delta.IsOpDerived "a redo is an edit"
              Expect.equal e4.Result (reference (GridEditLog.table log4)) "after redo"

          testCase "a site declaring no row identity takes the reference path, and says so"
          <| fun () ->
              let store = LiveTransformStore(16, "")
              let log0 = GridEditLog.create actor 10 small
              evaluate store "" (GridEditLog.table log0) |> ignore
              let log1 = edit store (SetCell("qty", 0, Int 30)) log0
              let e = evaluate store "" (GridEditLog.table log1)
              Expect.equal e.Delta (ReferencePath NoRowIdentityDeclared) "the reason is named"
              Expect.equal e.Result (reference (GridEditLog.table log1)) "and the answer is still right"

          testCase "a change no recorded edit describes is diffed, as before"
          <| fun () ->
              let store = LiveTransformStore(16, "id")
              evaluate store "id" small |> ignore

              let changed =
                  grid
                      [ "a", 30, Str "first"
                        "b", 5, Null
                        "c", 8, Str "third"
                        "d", 13, Str "fourth" ]

              let e = evaluate store "id" changed
              Expect.isTrue e.Delta.IsDiffed (sprintf "diffed: %A" e.Delta)
              Expect.equal e.Result (reference changed) "the full evaluation's answer"

          testCase "the renderer reads an edited version from State: rows for the grid, the version for a Transform"
          <| fun () ->
              let store = LiveTransformStore(16, "id")
              let seen = ResizeArray<LiveTransformEvaluation>()

              let seam =
                  { new ILiveTransformStore with
                      member _.Evaluate(site, p, source) =
                          store.Evaluate(site, store.IdentityColumn, p, source)
                          |> Result.map (fun e ->
                              seen.Add e
                              e.Result) }

              let sourcesAt (log: GridEditLog) =
                  { BindingSources.empty with
                      State = Map.ofList [ "orders", nonNull (box (GridEditLog.current log)) ]
                      LiveTransforms = Some seam }

              let rows (binding: Binding<Row seq>) (sources: BindingSources) =
                  match BindingResolver.resolve<Row seq> sources binding with
                  | BindingResolver.Resolved rows -> List.ofSeq rows
                  | other -> failtestf "unresolved: %A" other

              let transform =
                  Binding.Transform(
                      TransformSource.Live(Binding.State("orders", None), HostPrelude.TransformLive.emptySource),
                      pipeline,
                      None
                  )

              let log0 = GridEditLog.create actor 10 small
              Expect.equal (rows (Binding.State("orders", None)) (sourcesAt log0)).Length 4 "the grid reads rows"
              let first = rows transform (sourcesAt log0)
              let log1 = edit store (SetCell("qty", 0, Int 30)) log0
              let second = rows transform (sourcesAt log1)
              Expect.equal seen.Count 2 "the store served both renders"
              Expect.isTrue seen[0].Primed "the first render primed"
              Expect.isTrue seen[1].Delta.IsOpDerived (sprintf "the second refreshed from the edit: %A" seen[1].Delta)
              Expect.equal second.Length (first.Length + 1) "the edited row now passes the filter" ]

// ---------------------------------------------------------------------------
//  (d) the cost: one edit to a 10,000-row grid
// ---------------------------------------------------------------------------

let private bigGrid (n: int) : Table =
    { Schema =
        [ Field.create "id" StringType
          Field.create "a" IntType
          Field.create "b" IntType ]
      Columns =
        [ Column.ofStrs "id" (Array.init n (sprintf "r%05d") |> Vector.ofArray) AllValid
          Column.ofInts "a" (Array.init n (fun i -> i % 97) |> Vector.ofArray) AllValid
          Column.ofInts "b" (Array.init n (fun i -> i % 13) |> Vector.ofArray) AllValid ] }

let private bigPipeline =
    [ Derive("t", Binary(Mul, Col "a", Col "b"))
      Filter(Binary(Gt, Col "t", Lit(Int 100))) ]

let private median (xs: float list) = (List.sort xs)[List.length xs / 2]

let private costTests =
    testList
        "the cost of one edit"
        [ // The two pipeline classes the seam answers differently: a filter takes the row-local walk,
          // a derive-only pipeline the chunked path, which recognises the chunks a prepared version
          // shares with the one before it. Each is timed three ways over the SAME edit, fresh
          // objects every run: the op-derived refresh (the store recorded the edit); the diffed
          // refresh it replaces — the edited table arriving as a NEW table, as a whole-rows write
          // decoded by the renderer always arrived; and a full evaluation. Rows evaluated (Core's
          // deterministic scale) and the answers are asserted; the clock is printed, never gated.
          testCase "one edit to a 10,000-row grid refreshes through the op-derived delta"
          <| fun () ->
              let n = 10_000
              let runs = 9

              let classes =
                  [ "filter (row-local walk)", bigPipeline, 2
                    // The chunked path counts the rows of the chunks it evaluated: one chunk.
                    "derive only (chunked path)", [ Derive("t", Binary(Mul, Col "a", Col "b")) ], 1024 ]

              for label, pipe, bound in classes do
                  let timesOp = ResizeArray<float>()
                  let timesDiff = ResizeArray<float>()
                  let timesFull = ResizeArray<float>()
                  let mutable opRows = -1
                  let mutable diffRows = -1
                  let mutable fullRows = -1

                  for k in 1..runs do
                      let op = SetCell("a", 4_000 + k, Int 96)

                      // The op-derived refresh: the store recorded the edit.
                      let store = LiveTransformStore(4, "id")
                      let log0 = GridEditLog.create actor 10 (bigGrid n)
                      store.Evaluate("s", "id", pipe, GridEditLog.table log0) |> ok |> ignore
                      // One edit before the timed one: the steady state of an editing session,
                      // where the site's state was itself built over the version the edit reads.
                      let warm =
                          LiveTransform.applyEdit store (keyed (SetCell("b", k, Int 3))) log0 |> ok |> fst

                      store.Evaluate("s", "id", pipe, GridEditLog.table warm) |> ok |> ignore
                      let log1 = LiveTransform.applyEdit store (keyed op) warm |> ok |> fst
                      let after = GridEditLog.table log1
                      let sw = Stopwatch.StartNew()
                      let e = store.Evaluate("s", "id", pipe, after) |> ok
                      sw.Stop()
                      timesOp.Add sw.Elapsed.TotalMilliseconds
                      Expect.isTrue e.Delta.IsOpDerived (sprintf "op-derived: %A" e.Delta)
                      opRows <- Incremental.rowsEvaluated e.Footprint

                      // The diffed refresh it replaces.
                      let diffStore = LiveTransformStore(4, "id")
                      let warmed = ColumnOps.apply (SetCell("b", k, Int 3)) (bigGrid n) |> ok
                      diffStore.Evaluate("s", "id", pipe, bigGrid n) |> ok |> ignore
                      diffStore.Evaluate("s", "id", pipe, warmed) |> ok |> ignore

                      let arrived =
                          ColumnOps.apply op (ColumnOps.apply (SetCell("b", k, Int 3)) (bigGrid n) |> ok)
                          |> ok

                      let sw = Stopwatch.StartNew()
                      let d = diffStore.Evaluate("s", "id", pipe, arrived) |> ok
                      sw.Stop()
                      timesDiff.Add sw.Elapsed.TotalMilliseconds
                      Expect.isTrue d.Delta.IsDiffed (sprintf "diffed: %A" d.Delta)
                      diffRows <- Incremental.rowsEvaluated d.Footprint

                      // A full evaluation of the edited table.
                      let fresh =
                          ColumnOps.apply op (ColumnOps.apply (SetCell("b", k, Int 3)) (bigGrid n) |> ok)
                          |> ok

                      let sw = Stopwatch.StartNew()
                      let full = Incremental.primeOn (RowIdentity.byColumn "id") pipe fresh |> ok
                      sw.Stop()
                      timesFull.Add sw.Elapsed.TotalMilliseconds
                      fullRows <- Incremental.rowsEvaluated (Incremental.footprint full)

                      Expect.equal
                          e.Result
                          (Incremental.result full)
                          (label + ": the op-derived refresh is the full answer")

                      Expect.equal
                          d.Result
                          (Incremental.result full)
                          (label + ": the diffed refresh is the full answer")

                  printfn
                      "[phase 1891] one SetCell on a %d-row grid, %s, median of %d: op-derived refresh %.3f ms / %d rows evaluated; diffed refresh %.3f ms / %d rows; full evaluation %.3f ms / %d rows"
                      n
                      label
                      runs
                      (median (List.ofSeq timesOp))
                      opRows
                      (median (List.ofSeq timesDiff))
                      diffRows
                      (median (List.ofSeq timesFull))
                      fullRows

                  Expect.isLessThan opRows fullRows (label + ": fewer rows evaluated than a full evaluation")
                  Expect.isLessThanOrEqual opRows bound (label + ": the edited row's share, not the table")
          testCase "replay of the edit stream reproduces the 10,000-row grid byte for byte"
          <| fun () ->
              let mutable log = GridEditLog.create actor 64 (bigGrid 10_000)
              let mutable r = ConfRng.ofSeed 1891

              for _ in 1..200 do
                  let row, r1 = ConfRng.intBelow 10_000 r
                  let v, r2 = ConfRng.intBelow 1000 r1
                  r <- r2

                  log <-
                      match GridEditLog.apply (SetCell((if v % 2 = 0 then "a" else "b"), row, Int v)) log with
                      | Ok(next, _) -> next
                      | Error e -> failtestf "refused: %A" e

                  if v % 7 = 0 then
                      log <- GridEditLog.undo log |> ok |> fst

              assertReplays "after 200 edits over 10,000 rows" log ]


// ---------------------------------------------------------------------------
//  (e) the kit's columnar families at this tier's own generator
// ---------------------------------------------------------------------------

let private kitTests =
    testList
        "the columnar kit families at the tier's grid edits"
        [ testCase
              "the columnar op algebra certifies under Core's columnarOpLawsWith at this tier's grid-edit generator"
          <| fun () ->
              for seed in [ 1; 7; 1891 ] do
                  ComputeConf.columnarOpLawsWith ColumnOps.invert (gridEditGen ()) seed 60
                  |> assertAllPassed (sprintf "columnarOpLawsWith, seed %d" seed)

          testCase "incrementalLawsWith certifies op-driven equivalence at this tier's grid edits and pipelines"
          <| fun () ->
              let pipelines =
                  [ pipeline
                    [ Filter(Binary(Gt, Col "qty", Lit(Int 4))) ]
                    [ GroupBy([ "note" ], [ { Name = "n"; Fn = Count; Of = "qty" } ]) ] ]

              for seed in [ 1; 7; 1891 ] do
                  ComputeConf.incrementalLawsWith pipelines (gridEditGen ()) seed 60
                  |> assertAllPassed (sprintf "incrementalLawsWith, seed %d" seed) ]

[<Tests>]
let tests =
    testList
        "Phase 1891 — grid edits as ColumnOps"
        [ wireTests; corpusTests; historyTests; refreshTests; costTests; kitTests ]
