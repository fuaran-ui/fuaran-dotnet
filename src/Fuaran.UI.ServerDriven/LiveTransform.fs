namespace Fuaran.UI.ServerDriven

open Fuaran.Core

open Fuaran.Compute
open Fuaran.UI.FragmentMemo

// ============================================================================
//  LiveTransform — the incremental evaluation of a LIVE Transform source
//  (Phase 1179).
//
//  A `TransformSource.Live` binding runs a pipeline over a state-bound table
//  and is read again whenever that state is written. Evaluating it in full on
//  every write is correct and pays for every unchanged row; the columnar
//  substrate ships a seam that avoids exactly that — prime once over the source,
//  then advance the primed state against a delta describing what the edit
//  changed — and until this module nothing in the estate outside that
//  substrate's own tests called it.
//
//  ── WHY THIS TIER ─────────────────────────────────────────────────────────
//  The seam needs somewhere to keep the primed state BETWEEN evaluations, and
//  the render path has nowhere: a resolver call is a pure function of the
//  sources it is handed, by design and worth keeping. The server-driven tier is
//  the one that already holds a connection's state across edits — an inbound
//  event is a state edit, and the loop that folds it is the loop that would ask
//  for the table again — so the store lives here and is owned by whatever holds
//  the session.
//
//  ── WHAT IT PROMISES, AND WHAT IT DOES NOT ────────────────────────────────
//  It promises ONE thing: the table it returns is the table a full evaluation
//  over the current source produces. That is the substrate's own certified
//  property of the seam, not an assertion added here, and this module's tests
//  re-check it over the conformance corpus's own edit streams because a
//  consumer that trusted the property without measuring it would not notice the
//  day it stopped holding.
//
//  It does NOT promise to have done less work. A pipeline the seam declines —
//  one carrying a step whose output for a row is a function of rows the delta
//  does not name — falls back to the reference evaluator INSIDE the seam, which
//  reports the fall-back and its typed reason in the footprint. That is the
//  honest shape: a decline is a measured outcome with a reason, never a gap, and
//  a caller that wants to know before evaluating asks `Incremental.plan`.
//
//  ── THE STORE IS BOUNDED, AND SINGLE-THREADED BY CONTRACT ─────────────────
//  It reuses `FragmentMemo.BoundedLru` rather than minting a second cache: the
//  bound, the recency rule and the hit/miss counters are the same requirement
//  the fragment memo already met, and a long-lived connection cycling through
//  many grids must not grow a map without limit. The LRU's threading contract
//  travels with it — a host sharing one store across threads serialises access.
//  Evicting a site's state is never a correctness question: the next evaluation
//  re-primes and produces the same table, having paid for it.
// ============================================================================

/// The bound and the row-identity declaration a store takes when its
/// constructor is given neither. Named rather than repeated: the same two
/// values seed the parameterless `LiveTransformStore()` below and
/// [[LiveTransformOptions.defaults]], and a second literal is a second place
/// for them to disagree.
[<RequireQualifiedAccess>]
module LiveTransformDefaults =

    /// The default bound, in SITES. Generous relative to the number of live
    /// grids one connection renders, and small enough that a session cycling
    /// through many of them cannot grow without limit.
    [<Literal>]
    let Capacity = 64

    /// "no row identity declared" — no column is named, so no row has an
    /// identity and every evaluation runs through the seam's reference path.
    /// Correct on every site, restricting on none.
    [<Literal>]
    let IdentityColumn = ""

    /// How many edits a [[GridEditLog]] keeps: the depth of its undo stack and the
    /// length of the hash-chained record it replays from. Bounded for the reason
    /// the site capacity is: a long-lived session editing one grid all day must
    /// not grow without limit. Past the bound the oldest edit folds into the
    /// log's base, which stays replayable from its anchor.
    [<Literal>]
    let EditHistory = 100

// ============================================================================
//  Phase 1891 — grid edits as `ColumnOp`s.
//
//  An editable grid used to commit an edit as the WHOLE updated rows value, and
//  the store then learned what the edit changed by diffing the table it held
//  against the one it was handed (`Delta.diff`, a pass over both). The edit
//  already knew: it was one cell, one appended row, one column. So an edit is
//  now recorded as the columnar op it is (`Fuaran.Compute.ColumnOps`), and that
//  gives three things from shipped substrate rather than from code here:
//
//    * the refresh is fed the delta the OP induces (`ColumnOps.deltaOfPrepared`),
//      read off the one row it names rather than off the table;
//    * undo is `ColumnOps.invertPrepared`, read from the pre-edit version, which
//      an edit through `applyPrepared` leaves untouched;
//    * the history is an `OpStream` chain over the columnar witness, so it is
//      hash-chained and replays, from its base, to the live table.
//
//  ── ROW IDENTITY IS THE GRID'S `rowKeyField` ─────────────────────────────
//  A delta names rows by key, and the grid already declares which column is the
//  key. No second identity vocabulary is minted. A site that declares none still
//  edits correctly; it refreshes through the reference path and says so
//  (`ReferencePath NoRowIdentityDeclared`) rather than leaving a reader to infer
//  it from a footprint.
//
//  ── HOW THE RENDERER REACHES THE OP DELTA ────────────────────────────────
//  The host writes the edited VERSION (`GridEditLog.current`, a `Prepared`) as
//  the store value under the edit's key, and records the step with the session's
//  store (`LiveTransform.applyEdit` does both halves). The resolver reads a
//  `Prepared` store value as is (Phase 1890's prepared input, with no
//  conversion), and the store recognises the table it is handed as the version a
//  recorded step produced, so the site refreshes from the step's own delta. An
//  edit the store has no record of is diffed exactly as before: correct, at the
//  old price.
// ============================================================================

/// One applied edit: the version it was applied to, the op that was applied
/// (an undo applies the inverse op, a redo the original), and the version it
/// produced. `After` shares every chunk of `Before` the op did not write.
type EditStep =
    { Before: Prepared
      Op: ColumnOp
      After: Prepared }

/// Why an edit, an undo or a redo did not apply.
type EditRefusal =
    /// The op does not apply to the current table — the substrate's own verdict.
    | Refused of ColumnRejection
    /// There is no edit to undo: none was made, the bound evicted it, or the last
    /// edit was not invertible (`AppendRows`, `ApplyTransform`), which ends the
    /// undo history at that edit.
    | NothingToUndo
    /// There is no undone edit to redo, or a new edit has superseded it.
    | NothingToRedo

/// Why an evaluation's change was handed to the seam as the TOP delta — every
/// row may have moved — so the seam answers through its reference path.
type ReferencePathReason =
    /// The site declares no row-identity column, so no row can be named by key
    /// and no delta narrower than the top is true.
    | NoRowIdentityDeclared
    /// The held table and the new one differ and the declared column could not
    /// key them (a missing or repeated key, a moved schema). Carries the
    /// substrate's own account.
    | Unkeyable of detail: string

/// Where the delta an evaluation was refreshed against came from. A first
/// evaluation primes and has none; every later one names its source, so a reader
/// can tell an O(delta) refresh from a diffed one from a full one without
/// decoding the footprint.
type DeltaSource =
    /// No primed state for the site: the evaluation primed over the source.
    | NoPrior
    /// The source is the table the state already holds; nothing was refreshed.
    | SameSource
    /// The composed delta of the recorded edits that lead from the held table to
    /// this one, each read off its op (`ColumnOps.deltaOfPrepared`).
    | OpDerived of edits: int * delta: TableDelta
    /// The delta found by comparing the held table with this one row by row
    /// (`Delta.diff`) — the path for a change no recorded edit describes.
    | Diffed of delta: TableDelta
    /// The top delta, for the reason named.
    | ReferencePath of reason: ReferencePathReason

/// What one evaluation of a live Transform site produced, and the account of the
/// work that produced it. `Primed` says which of the two paths ran, so a caller
/// can tell a first render from an advance without decoding the footprint;
/// `Delta` says where an advance's delta came from (Phase 1891).
type LiveTransformEvaluation =
    { Result: Table
      Footprint: RecomputeFootprint
      Primed: bool
      Delta: DeltaSource }

/// The wire payload of one grid edit (Phase 1891): the State key it commits to
/// (the grid's edit destination), the grid's `rowKeyField` when it declares one,
/// and the edit as a columnar op. An undo is not a separate payload: it is the
/// edit whose op is the inverse.
type GridEdit =
    { Key: string
      RowKeyField: string option
      Op: ColumnOp }

/// The canonical codec for [[GridEdit]] (WIRE_FORMAT, "Edits as column ops").
[<RequireQualifiedAccess>]
module GridEdit =

    /// The payload as a `JVal`: `{"$type":"GridEdit","key","op","rowKeyField"?}`,
    /// the op in the columnar op codec's own form.
    let encodeJson (edit: GridEdit) : JVal =
        Canonical.typed
            "GridEdit"
            [ yield "key", JStr edit.Key
              yield "op", ColumnOps.encodeJson edit.Op
              match edit.RowKeyField with
              | Some field -> yield "rowKeyField", JStr field
              | None -> () ]

    /// The canonical wire string — sorted keys and the cross-host number layout,
    /// so two hosts encode one edit to the same bytes.
    let encode (edit: GridEdit) : string =
        Canonical.write Rendering.CrossHost (encodeJson edit)

    let private members = set [ "$type"; "key"; "op"; "rowKeyField" ]

    /// Decode a payload. Strict: a member outside the four is refused rather than
    /// ignored, because an edit that carried something this host would drop is an
    /// edit this host would apply differently from the one that was sent. An empty
    /// `key` or `rowKeyField` is refused — the empty string names no State key and
    /// no column; absence is how "no identity declared" is spelled.
    let decodeJson (el: JVal) : Result<GridEdit, string> =
        match el with
        | JObj fields ->
            let find name =
                fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

            match fields |> List.tryFind (fun (k, _) -> not (members.Contains k)) with
            | Some(k, _) -> Error("GridEdit: unknown member '" + k + "'")
            | None ->
                match find "$type", find "key", find "op", find "rowKeyField" with
                | Some(JStr "GridEdit"), Some(JStr key), Some op, field when key <> "" ->
                    let field =
                        match field with
                        | None -> Ok None
                        | Some(JStr f) when f <> "" -> Ok(Some f)
                        | Some _ -> Error "GridEdit: rowKeyField must be a non-empty string"

                    field
                    |> Result.bind (fun field ->
                        ColumnOps.decodeJson op
                        |> Result.mapError (fun m -> "GridEdit: op: " + m)
                        |> Result.map (fun op ->
                            { Key = key
                              RowKeyField = field
                              Op = op }))
                | Some(JStr "GridEdit"), Some(JStr _), Some _, _ -> Error "GridEdit: key must be a non-empty string"
                | Some(JStr "GridEdit"), _, None, _ -> Error "GridEdit: missing op"
                | Some(JStr "GridEdit"), _, _, _ -> Error "GridEdit: key must be a non-empty string"
                | _ -> Error "GridEdit: $type must be \"GridEdit\""
        | _ -> Error "GridEdit: expected an object"

    /// Decode a payload from its wire string.
    let decode (text: string) : Result<GridEdit, string> =
        match Json.parse text with
        | Error m -> Error("GridEdit: not json: " + m)
        | Ok el -> decodeJson el

/// The edit history of one edited table (Phase 1891): the live version, a
/// bounded undo / redo pair of stacks, and the hash-chained record of every op
/// applied, which replays from the log's base to the live table.
///
/// Immutable: every gesture returns a new log, so a host holding the log in its
/// model gets the same transactional behaviour its model already has.
type GridEditLog =
    private
        { Actor: Actor
          Capacity: int
          Base: Table
          Anchor: Anchor
          Records: OpRecord<ColumnOp> list
          Versions: Prepared list
          Current: Prepared
          Undo: (ColumnOp * ColumnOp) list
          Redo: (ColumnOp * ColumnOp) list }

[<RequireQualifiedAccess>]
module GridEditLog =

    let private stream = OpStream.canonical OpStream.defaultHash ColumnOps.streamWitness

    let private shape = ChainShape.records stream.Config ColumnOps.encode

    /// A log over `table`, keeping at most `capacity` edits, its records chained
    /// under `actor`.
    let create (actor: Actor) (capacity: int) (table: Table) : GridEditLog =
        { Actor = actor
          Capacity = max 1 capacity
          Base = table
          Anchor = Anchor.genesis stream.Config
          Records = []
          Versions = []
          Current = DataFrame.prepare table
          Undo = []
          Redo = [] }

    /// The live version — what a host writes as the store value under the edit's key.
    let current (log: GridEditLog) : Prepared = log.Current

    /// The live table.
    let table (log: GridEditLog) : Table = DataFrame.toTable log.Current

    /// The table the chain replays from: the one the log was created over, or, once
    /// the bound has folded older edits away, the version they left.
    let baseTable (log: GridEditLog) : Table = log.Base

    /// The boundary the chained records hang from — genesis until the bound folds
    /// the first edit away, then the folded record's sequence and hash.
    let anchor (log: GridEditLog) : Anchor = log.Anchor

    /// Every op applied since the base, in order, hash-chained.
    let records (log: GridEditLog) : OpRecord<ColumnOp> list = log.Records

    /// The chain's head: the hash the next record will link to.
    let head (log: GridEditLog) : string =
        (ChainShape.tip shape log.Anchor log.Records).PrevHash

    let canUndo (log: GridEditLog) : bool = not (List.isEmpty log.Undo)
    let canRedo (log: GridEditLog) : bool = not (List.isEmpty log.Redo)

    /// The first integrity fault in the record, or `None` for an intact chain.
    let verify (log: GridEditLog) : ChainBreak option =
        ChainShape.walk shape stream.HashFn log.Anchor log.Records

    /// The record replayed from the base through the columnar witness's own
    /// `apply` — the law is that this is the live table.
    let replay (log: GridEditLog) : Result<Table, int * ColumnRejection> =
        OpStream.replay ColumnOps.streamWitness log.Base log.Records

    // Chain `op` (already applied, producing `after`) and fold the oldest record
    // into the base while the record is longer than the bound. The fold is a
    // re-anchor, not a re-hash: every surviving record keeps its hash.
    let private chain (op: ColumnOp) (after: Prepared) (log: GridEditLog) : GridEditLog =
        let records =
            ChainShape.append
                shape
                stream.HashFn
                log.Anchor
                (fun seq ->
                    { Seq = seq
                      Actor = log.Actor
                      Op = op
                      PrevHash = ""
                      Hash = "" })
                log.Records

        let rec fold (log: GridEditLog) =
            match log.Records, log.Versions with
            | r :: records, v :: versions when List.length log.Records > log.Capacity ->
                fold
                    { log with
                        Base = DataFrame.toTable v
                        Anchor = { Seq = r.Seq + 1; PrevHash = r.Hash }
                        Records = records
                        Versions = versions }
            | _ -> log

        fold
            { log with
                Records = records
                Versions = log.Versions @ [ after ]
                Current = after }

    let private step (log: GridEditLog) (op: ColumnOp) (after: Prepared) : EditStep =
        { Before = log.Current
          Op = op
          After = after }

    /// Apply an edit. Its inverse is read from the version it is applied to; an op
    /// with none (`AppendRows`, `ApplyTransform`) applies, and ends the undo
    /// history there. Any new edit clears the redo stack.
    let apply (op: ColumnOp) (log: GridEditLog) : Result<GridEditLog * EditStep, EditRefusal> =
        match ColumnOps.applyPrepared op log.Current with
        | Error r -> Error(Refused r)
        | Ok after ->
            let undo =
                match ColumnOps.invertPrepared op log.Current with
                | Ok inverse -> (op, inverse) :: log.Undo |> List.truncate log.Capacity
                | Error _ -> []

            Ok(chain op after { log with Undo = undo; Redo = [] }, step log op after)

    /// Undo the most recent undoable edit by applying its inverse. The inverse is
    /// chained like any edit, so the record still replays to the live table.
    let undo (log: GridEditLog) : Result<GridEditLog * EditStep, EditRefusal> =
        match log.Undo with
        | [] -> Error NothingToUndo
        | (op, inverse) :: rest ->
            match ColumnOps.applyPrepared inverse log.Current with
            | Error r -> Error(Refused r)
            | Ok after ->
                Ok(
                    chain
                        inverse
                        after
                        { log with
                            Undo = rest
                            Redo = (op, inverse) :: log.Redo },
                    step log inverse after
                )

    /// Redo the most recently undone edit. Its inverse still holds: undoing it
    /// restored exactly the version it was read from.
    let redo (log: GridEditLog) : Result<GridEditLog * EditStep, EditRefusal> =
        match log.Redo with
        | [] -> Error NothingToRedo
        | (op, inverse) :: rest ->
            match ColumnOps.applyPrepared op log.Current with
            | Error r -> Error(Refused r)
            | Ok after ->
                Ok(
                    chain
                        op
                        after
                        { log with
                            Undo = (op, inverse) :: log.Undo |> List.truncate log.Capacity
                            Redo = rest },
                    step log op after
                )

/// One site's primed state, with the table it was last evaluated against and,
/// where the evaluation was handed one, the prepared version that table is — the
/// identity a recorded edit's `Before` is matched against without materialising
/// anything.
type internal LiveSiteState =
    { Eval: IncrementalEval
      Source: Table
      Version: Prepared option }

/// The per-connection store of primed live-Transform evaluations, one per SITE.
///
/// A site is whatever the caller says it is, and the caller should make it
/// identify a reader: two grids over one state key are two sites with two
/// pipelines, and one grid keeps its primed state across every edit to that key.
/// A site whose pipeline or whose source schema has moved is not a defect — the
/// seam notices and re-primes, recording why in the footprint.
///
/// Phase 1586 — it is also the estate's implementation of
/// `Fuaran.UI.ILiveTransformStore`, the seam the RENDERER's own
/// `TransformSource.Live` arm consults — and, since Phase 1761, its
/// `TransformSource.Data` arm too, keyed by the pipeline closed over its bound
/// params, so a static chart whose chips did not move is handed back its prior
/// table (`ReusedPrior`) rather than re-evaluated. That interface takes no identity
/// column, because nothing in a rendered tree declares one and the renderer
/// would have to guess; `identityColumn` is therefore the declaration whoever
/// CONSTRUCTS the store makes on its behalf, once, for every site it serves.
/// The default is the empty string — no column of that name exists, so no row
/// has an identity, and every evaluation runs through the seam's reference path
/// and answers correctly while restricting nothing. A host that wants the
/// saving names its key column; a host that names none loses only the saving.
type LiveTransformStore(capacity: int, identityColumn: string) =
    let states = BoundedLru<LiveSiteState>(capacity)
    let mutable recent: EditStep list = []

    /// A store at an explicit bound with no declared row identity — correct on
    /// every site, restricting on none. Preserved at its original arity: the
    /// Phase-1179 call sites construct through it and their behaviour is
    /// unchanged, because the 4-argument `Evaluate` below takes the identity
    /// column per call and never consults this one.
    new(capacity: int) = LiveTransformStore(capacity, "")

    /// The default bound. Generous relative to the number of live grids one
    /// connection renders, and small enough that a session cycling through many
    /// of them cannot grow without limit.
    new() = LiveTransformStore(LiveTransformDefaults.Capacity, LiveTransformDefaults.IdentityColumn)

    /// The row-identity declaration this store applies to the interface-driven
    /// calls that carry none of their own. Empty means "none declared".
    member _.IdentityColumn = identityColumn

    member _.Capacity = states.Capacity
    member _.Count = states.Count

    /// Sites primed and then advanced, rather than re-primed — the observability
    /// the LRU already keeps. A hit rate near zero on a stable set of grids means
    /// the site keys are not stable, which is a caller defect the counts surface.
    member _.Hits = states.Hits

    member _.Misses = states.Misses

    /// Forget every primed state. Correctness-neutral: the next evaluation of any
    /// site re-primes over its current source.
    member _.Clear() =
        states.Clear()
        recent <- []

    /// Phase 1891 — record an applied edit so that the next evaluation of every
    /// site over its `Before` version refreshes from the edit's own delta rather
    /// than by diffing. The record is the step, held by reference and bounded by
    /// the store's capacity (the newest first); recording is correctness-neutral,
    /// because a step is used only when it is the exact version a site holds and
    /// the exact version it is handed, both by reference.
    member _.Record(step: EditStep) =
        recent <- step :: recent |> List.truncate states.Capacity

    /// How many recorded edits the store currently holds.
    member _.RecordedEdits = List.length recent

    /// Evaluate `pipeline` over `source` for one site, priming on the first sight
    /// of the site and advancing the primed state on every later one.
    ///
    /// `identityColumn` is the column whose value identifies a row — the key the
    /// edit stream addresses rows by. It is the caller's declaration and not a
    /// guess: a source keyed by position has no identity, and the seam declines a
    /// positional delta rather than treating it as an identity one, because a
    /// cache keyed by position is invalidated wholesale by any insert. With none
    /// declared (the empty string) a change is handed to the seam as the top
    /// delta and the evaluation says so (`ReferencePath NoRowIdentityDeclared`).
    ///
    /// The delta is DERIVED here, never taken from the caller: from the edits
    /// recorded with [[Record]] when they lead, version by version, from the
    /// table the site holds to the one handed in (Phase 1891, `OpDerived`), and
    /// otherwise by diffing the two (`Diffed`). A caller-supplied delta would be a
    /// second description of a change the tables already carry, and the seam's
    /// whole guarantee is conditioned on that description being truthful.
    ///
    /// A pipeline reading a HOST-RESOLVED named source is refused by name rather
    /// than served: the everyday seam call resolves nothing, so there is no
    /// answer to give and inventing a footprint for one would be worse than the
    /// refusal. Evaluate such a pipeline through [[LiveTransform.reference]].
    member _.Evaluate
        (site: string, identityColumn: string, pipeline: Transform list, source: Table)
        : Result<LiveTransformEvaluation, string> =

        let idw = RowIdentity.byColumn identityColumn

        let toEvaluation (primed: bool) (delta: DeltaSource) (version: Prepared option) (state: IncrementalEval) =
            states.Set(
                site,
                { Eval = state
                  Source = source
                  Version = version }
            )

            { Result = Incremental.result state
              Footprint = Incremental.footprint state
              Primed = primed
              Delta = delta }

        // The recorded step whose `After` IS the table handed in — matched by
        // reference, newest first, so the common case (one edit since the last
        // render) is one comparison against a table already materialised.
        let stepTo (table: Table) =
            recent
            |> List.tryFind (fun s -> obj.ReferenceEquals(DataFrame.toTable s.After, table))

        // The recorded steps leading from the version `held` stands for to
        // `last`, oldest first: walked back by VERSION identity, which reads no
        // table, until a step's `Before` is the held one.
        let lineage (held: LiveSiteState) (last: EditStep) : EditStep list option =
            let isHeld (v: Prepared) =
                match held.Version with
                | Some h -> obj.ReferenceEquals(v, h)
                | None -> obj.ReferenceEquals(DataFrame.toTable v, held.Source)

            let rec back (s: EditStep) (acc: EditStep list) (budget: int) =
                if isHeld s.Before then
                    Some(s :: acc)
                elif budget = 0 then
                    None
                else
                    match recent |> List.tryFind (fun p -> obj.ReferenceEquals(p.After, s.Before)) with
                    | Some p -> back p (s :: acc) (budget - 1)
                    | None -> None

            back last [] (List.length recent)

        match states.TryGet site with
        // Phase 1761 — the SAME source object under the same pipeline: the
        // prior result is the answer, and it is handed back without a refresh.
        // The seam would reach the same verdict (`ReusedPrior`) — the result is
        // a function of the pipeline and the source, and neither moved — but
        // only after comparing the two tables STRUCTURALLY, which is linear in
        // the table and measured at a third of a full evaluation for a
        // 2,000-row source. A static `Data` site hands the store the table it
        // decoded, the same object every render, so this is its steady state.
        // Nothing is re-set: the state the store holds is still the one that
        // produced this result.
        | Some prior when
            obj.ReferenceEquals(prior.Source, source)
            && Incremental.pipelineOf prior.Eval = pipeline
            ->
            let held = Incremental.result prior.Eval

            Ok
                { Result = held
                  Footprint =
                    { SourceRows = Table.rowCount source
                      ResultRows = Table.rowCount held
                      Recompute = ReusedPrior }
                  Primed = false
                  Delta = SameSource }
        // Phase 1891 — a first sight over a recorded edit's version primes over
        // that prepared version (`primeOnPrepared`), paying no table boundary.
        | None ->
            let primed =
                match stepTo source with
                | Some s ->
                    Incremental.primeOnPrepared idw pipeline s.After
                    |> Result.map (toEvaluation true NoPrior (Some s.After))
                | None ->
                    Incremental.primeOn idw pipeline source
                    |> Result.map (toEvaluation true NoPrior None)

            primed |> Result.mapError DataFrame.errorString
        | Some prior ->
            // Phase 1761 — a source equal to the one the state holds is
            // truthfully described by the quiet delta under ANY identity scheme,
            // the undeclared one included, and the seam answers that with
            // `ReusedPrior`, evaluating nothing. Reference equality first,
            // because the common case is the same decoded table handed back;
            // structural equality otherwise, which is also the comparison the
            // seam itself makes before it reuses anything.
            let edited =
                if identityColumn = "" then
                    None
                else
                    stepTo source
                    |> Option.bind (fun last -> lineage prior last |> Option.map (fun steps -> steps, last))

            match edited with
            // Phase 1891 — the recorded edits from the held version to this one:
            // each step's delta is read off its op, the deltas compose in order,
            // and the refresh runs over the last step's prepared version, which
            // shares every chunk the edits did not write.
            | Some(steps, last) ->
                let delta =
                    steps
                    |> List.map (fun s -> ColumnOps.deltaOfPrepared idw s.Before s.Op)
                    |> List.reduce Delta.compose

                Incremental.refreshOnPrepared idw pipeline prior.Eval delta last.After
                |> Result.map (toEvaluation false (OpDerived(List.length steps, delta)) (Some last.After))
                |> Result.mapError DataFrame.errorString
            | None ->
                let unchanged = obj.ReferenceEquals(prior.Source, source) || prior.Source = source

                // A source the witness cannot key is not a source with no change;
                // it is a source whose change cannot be DESCRIBED. The honest delta
                // for that is the top element, which the seam answers by
                // evaluating in full and re-priming its caches — so the next edit
                // can restrict again — and the evaluation names the reason.
                let delta, account =
                    if unchanged then
                        Delta.empty idw.Scheme, SameSource
                    elif identityColumn = "" then
                        FullRefresh, ReferencePath NoRowIdentityDeclared
                    else
                        match Delta.diff idw prior.Source source with
                        | Ok d -> d, Diffed d
                        | Error e -> FullRefresh, ReferencePath(Unkeyable(Delta.defectString e))

                Incremental.refreshOn idw pipeline prior.Eval delta source
                |> Result.map (toEvaluation false account None)
                |> Result.mapError DataFrame.errorString

    // Phase 1586 — the renderer's seam, served by the same method above under
    // the store's own identity declaration. ONE key rule, not two: the `site`
    // string is the key on both paths, and the interface adds no second one.
    // The footprint and the primed/advanced bit are dropped rather than
    // widened onto the seam — a renderer has nowhere to put them, and a seam
    // that carried them would oblige every implementation to mint an account
    // of work it may not have done.
    //
    // A plain comment, not an XML doc: F# attaches no documentation to an
    // interface implementation, and `///` here is diagnostic FS3520 rather than
    // a doc anyone can read. The contract this member serves is documented
    // where it is DECLARED, on `Fuaran.UI.ILiveTransformStore`.
    interface Fuaran.UI.ILiveTransformStore with
        member this.Evaluate(site: string, pipeline: Transform list, source: Table) =
            this.Evaluate(site, identityColumn, pipeline, source) |> Result.map _.Result

/// What a HOST declares, once, about the live-Transform store one of its
/// sessions will hold (Phase 1604).
///
/// Two values and no more, because two is what the store cannot derive for
/// itself. `Capacity` bounds how many SITES one session keeps primed at a time;
/// `IdentityColumn` names the column whose value identifies a row, which is the
/// declaration [[LiveTransformStore]] applies to every seam-driven call — see
/// its own documentation for why row identity is the store's and not the
/// renderer's.
///
/// It is a record rather than two arguments so that a later declaration is a
/// field and not a new arity on every composition site.
type LiveTransformOptions =
    {
        /// How many sites one session keeps primed. At the bound the least
        /// recently used site is evicted, which is correctness-neutral: the next
        /// evaluation of an evicted site re-primes and answers the same table,
        /// having paid for it.
        Capacity: int

        /// The column whose value identifies a row, or the empty string for "none
        /// declared". A store with none declared is CORRECT and simply restricts
        /// nothing, so a host that does not know its key column loses the saving
        /// and never the answer.
        IdentityColumn: string
    }

/// Companion values for [[LiveTransformOptions]].
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LiveTransformOptions =

    /// The bound the parameterless store takes, with no row identity declared —
    /// the shape a host gets by opting in and declaring nothing else.
    let defaults: LiveTransformOptions =
        { Capacity = LiveTransformDefaults.Capacity
          IdentityColumn = LiveTransformDefaults.IdentityColumn }

    /// [[defaults]] with the host's key column named — the one-line opt-in for a
    /// host that knows how its rows are identified.
    let identifiedBy (column: string) : LiveTransformOptions =
        { defaults with
            IdentityColumn = column }

[<RequireQualifiedAccess>]
module LiveTransform =

    /// A fresh store at the default bound.
    let store () = LiveTransformStore()

    /// The reference answer for one evaluation — a full evaluation of the
    /// pipeline over the source, with no cache consulted and nothing primed.
    ///
    /// This is what the incremental path is measured AGAINST, and it is exposed
    /// so a caller can measure it: the seam's equivalence is certified upstream,
    /// and a consumer that never checks it is a consumer that would not notice
    /// the certification lapsing.
    let reference (pipeline: Transform list) (source: Table) : Result<Table, string> =
        DataFrame.evalPipelineInEnv Map.empty pipeline source
        |> Result.mapError DataFrame.errorString

    // ── Phase 1891 — an edit is one gesture, not two ─────────────────────────
    //
    //  Applying an edit to the log and recording its step with the session's
    //  store are two halves of one act: a host that did the first and forgot the
    //  second would get correct answers at the diffed price forever, and nothing
    //  would say so but a footprint. So the three gestures below do both, and a
    //  host writes `GridEditLog.current` of the returned log under the edit's key.
    //  The store's own identity declaration is what the refresh keys rows by; a
    //  host serving grids that declare `rowKeyField` declares the same column on
    //  the store (`LiveTransformOptions.identifiedBy`).

    let private recorded (store: LiveTransformStore) (outcome: Result<GridEditLog * EditStep, EditRefusal>) =
        outcome
        |> Result.map (fun (log, step) ->
            store.Record step
            log, step)

    /// Apply a decoded [[GridEdit]]'s op to `log` and record the step with `store`.
    let applyEdit
        (store: LiveTransformStore)
        (edit: GridEdit)
        (log: GridEditLog)
        : Result<GridEditLog * EditStep, EditRefusal> =
        GridEditLog.apply edit.Op log |> recorded store

    /// Undo the most recent undoable edit and record the step with `store`.
    let undoEdit (store: LiveTransformStore) (log: GridEditLog) : Result<GridEditLog * EditStep, EditRefusal> =
        GridEditLog.undo log |> recorded store

    /// Redo the most recently undone edit and record the step with `store`.
    let redoEdit (store: LiveTransformStore) (log: GridEditLog) : Result<GridEditLog * EditStep, EditRefusal> =
        GridEditLog.redo log |> recorded store

    // ── Phase 1604 — the session composition ────────────────────────────────
    //
    //  Phase 1179 built the store and Phase 1586 gave the RENDERER a seam that
    //  consults one, but nothing constructed a store and threaded it through a
    //  session — so `BindingSources.LiveTransforms` was `None` on every path
    //  this tier served, and a live source still paid full evaluation in
    //  practice. This is that wiring, and it is one function on purpose.
    //
    //  ── WHO OWNS THE STORE, AND WHEN IT GOES AWAY ─────────────────────────
    //  The SESSION owns it. `initSession` mints one store per call and it is
    //  reachable from exactly two places: the render closure inside the
    //  session's own `DriverServices`, and the handle returned to the caller.
    //  There is no registry, no static, no `Map<sessionId, store>` — and that
    //  absence is the design rather than an omission, because such a map is
    //  precisely how a per-session cache becomes a cross-tenant one and how it
    //  outlives the session it was minted for. Drop the session and the store
    //  is unreachable with it.
    //
    //  It holds no unmanaged resource, no handle and no thread, so it is not
    //  `IDisposable` and adding that would promise a teardown with nothing to
    //  tear down. What an explicit session-end has is `Clear()`, which releases
    //  the primed tables early and is correctness-neutral. The ONE way to leak
    //  a store is for a caller to keep the returned handle past its session's
    //  end, which is why the handle is returned for OBSERVATION — the bound,
    //  the counts, an early `Clear()` — and never as something to pass to a
    //  second session.
    //
    //  ── WHY IT BUILDS THE WHOLE SESSION ───────────────────────────────────
    //  The smaller function — hand back the store and the render, let the
    //  caller call `Driver.init` — is hoistable, and the sample host in this
    //  repo already hoists its `renderFragment` to a module-level value shared
    //  by every connection. Hoisted, that smaller function gives every session
    //  ONE store: a cross-tenant cache, silently, from a change that looks like
    //  tidying. Building the session is the gesture that cannot be hoisted,
    //  because a session cannot be.
    //
    //  ── WHY THE SOURCES ARE A THUNK ───────────────────────────────────────
    //  Read PER RENDER, never captured — the shape `DriverServices`'
    //  `CorrelationContext` already takes, and for the same reason. Services
    //  are built once per connection while a host's binding sources move with
    //  its state, so a `BindingSources` taken by value here would freeze the
    //  state a session renders against, which is a REGRESSION relative to what
    //  a host can do today by closing over its own cell.
    //
    //  ── WHY THIS TIER SETS THE SLOT, RATHER THAN THE HOST ─────────────────
    //  The host supplies its render as a function OF sources (`Render.render`
    //  already has that shape) and its own sources; the slot is set here, on
    //  every render this session serves. A seam that handed the host a store
    //  and trusted it to put it in the record would be satisfied by a host that
    //  quietly did not, and the failure would be invisible: a correct answer at
    //  full price, forever.

    /// Compose one server-driven session that holds its OWN live-`Transform`
    /// store, and return both.
    ///
    /// `sources` is read once per render; `render` is the host's renderer as a
    /// function of the sources it resolves against (server-side, `Render.render`);
    /// `servicesFrom` is how the host builds its `DriverServices` from a
    /// `renderFragment` — `DriverServices.create`, `DriverServices.createPermissive`,
    /// or its own lambda closing over a dispatch gate. The remaining three
    /// arguments are `Driver.init`'s own.
    ///
    /// The returned store is the session's, for as long as the session lives —
    /// see the note above on ownership. Call this once per session; passing one
    /// session's store to another is not possible through this surface, which is
    /// the point of it.
    let initSession
        (options: LiveTransformOptions)
        (sources: unit -> Fuaran.UI.BindingSources)
        (render: Fuaran.UI.BindingSources -> Fuaran.UI.Types.Node<'Msg> -> string)
        (servicesFrom: (Fuaran.UI.Types.Node<'Msg> -> string) -> Driver.DriverServices<'Msg>)
        (update: 'Msg -> 'Model -> 'Model)
        (view: 'Model -> Fuaran.UI.Types.Node<'Msg>)
        (model: 'Model)
        : Driver.LiveSession<'Model, 'Msg> * LiveTransformStore =

        let store = LiveTransformStore(options.Capacity, options.IdentityColumn)
        let seam = store :> Fuaran.UI.ILiveTransformStore

        let renderFragment (node: Fuaran.UI.Types.Node<'Msg>) : string =
            render
                { sources () with
                    LiveTransforms = Some seam }
                node

        Driver.init (servicesFrom renderFragment) update view model, store
