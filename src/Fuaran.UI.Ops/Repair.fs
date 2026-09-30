module Fuaran.UI.Ops.Repair

// ============================================================================
//  Deliberate repair of malformed canonical JSON (WIRE_FORMAT.md §28).
//
//  The two recoveries fuaran#850 and fuaran#855 built INSIDE the node decoder,
//  lifted out of it (Phase 1923). A decoder is strict: it parses, or it refuses
//  with `INVALID_JSON`. Repair is a separate, pure, text-to-text act a caller
//  invokes deliberately, and every repair it performs is named by a stable id
//  from a closed, versioned catalogue:
//
//    implied-node-close   fuaran#850 — insert the node-wrapper closers a
//                         `children[]` / `cases[]` element (or the root) owes
//    over-close-unique    fuaran#855 — delete one or two surplus closers, iff
//                         exactly one deletion decodes clean
//
//  Repair touches STRUCTURE only: it inserts or deletes closing brackets and
//  never invents or edits a key or a value. Its output is not trusted — the
//  caller still decodes it strictly and validates the tree; `repair` bypasses
//  neither.
//
//  Why this file does not hold the complete entry point. The uniqueness gate of
//  `over-close-unique` asks the SCHEMA whether a candidate decodes clean, so a
//  complete `repair` needs the decoder, and the decoder's opt-in
//  `Recovery.Lenient` needs `repair`. This file therefore holds the catalogue
//  and the text-level machinery, compiled BEFORE the decoder and parameterised
//  by it (`repairWith`); the decoder supplies itself and publishes the complete
//  entry point as `Fuaran.UI.Ops.JsonDecode.repair`. One implementation,
//  entered from two places — `Recovery.Lenient` is `repair` then strict decode,
//  not a second copy of either recovery.
//
//  Pure: no counters are written here. The decoder's `Recovery.Lenient` path
//  records the `Reliance` counters, as it always has; a caller of `repair`
//  reads what was applied from the result itself.
//
//  Fable-compatible: strings, `ResizeArray` and `StringBuilder` only.
// ============================================================================

/// The stable repair ids (WIRE_FORMAT.md §28.2). The same strings the
/// decoder's `Reliance` counters and `DecodeOutcome.Recovered` use, so the
/// reliance accounting, the per-decode record and the eval provenance speak
/// one vocabulary.
module RepairId =
    /// fuaran#850 — a node wrapper's dropped closing brace at a
    /// `children[]` / `cases[]` / root boundary, re-inserted.
    [<Literal>]
    let ImpliedNodeClose = "implied-node-close"

    /// fuaran#855 — a surplus closer, deleted when exactly one deletion
    /// decodes clean.
    [<Literal>]
    let OverCloseUnique = "over-close-unique"

    /// The closed catalogue, in the order `repair` tries its entries.
    let catalogue = [ ImpliedNodeClose; OverCloseUnique ]

/// The catalogue version (WIRE_FORMAT.md §28.2). A new repair, or a change to
/// an existing one's admissibility or output, is a specification change with
/// fixtures, and moves this number.
[<Literal>]
let CatalogueVersion = 1

/// Why `repair` declined (WIRE_FORMAT.md §28.4) — stable tokens, asserted
/// byte-for-byte by the `repair/` corpus family.
module Refusal =
    /// The input breaches a §21 resource limit. That is not malformed JSON, and
    /// no repair is attempted on it.
    [<Literal>]
    let LimitExceeded = "limit-exceeded"

    /// No catalogue entry applies to the document.
    [<Literal>]
    let NotInCatalogue = "not-in-catalogue"

    /// The document is in the over-close profile and two or more distinct
    /// candidates decode clean — ambiguous field ownership, refused.
    [<Literal>]
    let OverCloseAmbiguous = "over-close-ambiguous"

    /// The document is in the over-close profile and no candidate decodes clean.
    [<Literal>]
    let OverCloseNoCleanCandidate = "over-close-no-clean-candidate"

    /// The document is in the over-close profile and the enumeration is past a
    /// stated bound (length, closer positions, deletion sets, distinct
    /// candidates).
    [<Literal>]
    let OverCloseBounds = "over-close-bounds"

/// The result of `repair` (WIRE_FORMAT.md §28.3).
[<RequireQualifiedAccess>]
type RepairOutcome =
    /// The repaired text, and the catalogue ids applied to produce it. A
    /// document that already parses is returned unchanged with `Applied = []`:
    /// `repair` is the identity on well-formed input, which is what makes
    /// "repair, then strictly decode" a total composition.
    | Repaired of Text: string * Applied: string list
    /// No admissible repair; `Reason` is one of the `Refusal` tokens.
    | NotRepairable of Reason: string

/// The document-length ceiling for the over-close enumeration, in UTF-16 code
/// units (WIRE_FORMAT.md §28.2) — the number the decoder's recovery policy has
/// carried since Phase 1532.
[<Literal>]
let MaxOverCloseLength = Fuaran.UI.KindPolicy.DecodePolicy.MaxRecoverableLength

// ─── implied-node-close (fuaran#850; §28.2) ───────────────────────────────
//
// The one malformed-emission class the wire grammar cannot teach away: a node
// wrapper's closing brace dropped at the end of a `children[]` / `cases[]`
// element (or the root node left open at EOF), typically after a run of ≥2
// closing braces — the model closes the nested spec value and `kind` and stops
// one brace short of the node's own `}`. Measured on stored model emissions
// (2026-08-15): the emission is canonical in intent and correct in vocabulary,
// and fails on a brace count in a run; teaching the brace rule in the prompt
// has zero measured effect (failures re-emit the exact fragment the teaching
// quotes as its own wrong half), so the class is closed at the decode boundary
// instead.
//
// The remedy is measured, not assumed. Auto-close at EOF — the obvious fix —
// recovers NONE of the mid-document cells: the missing brace is owed
// mid-document, so the `]` that follows it is already mis-parsed and appending
// closers at the end fixes nothing (pinned as a test so the wrong fix cannot
// return). What recovers the whole measured set is auto-close on an
// ANCESTOR-LEGAL TOKEN: when `]` (or the array-level `,` that separates two
// element objects) arrives while node wrappers opened inside that array are
// still open, close the owed wrappers implicitly; when EOF arrives with the
// document prefix-valid and every open wrapper closable, close what is owed.
//
// CONTRACT — bounded, profile-gated, fails closed:
//   - Attempted ONLY on a document the strict parser refuses with
//     `INVALID_JSON` (never on `LIMIT_EXCEEDED`, and never on a document that
//     parses — `repair` returns that unchanged).
//   - INSERT-ONLY OWED CLOSERS: the recovery inserts `}` for wrappers that are
//     demonstrably open at a boundary where their close is the only insert-only
//     reading (plus the matching `]`/`}` closers at a clean EOF). It never
//     invents content, keys, values, or brackets that open anything.
//   - PROFILE-GATED: mid-document closes fire only into an array keyed
//     `children` / `cases` (the node-wrapper positions of the measured class).
//     The same defect inside any other array does NOT recover — it stays a
//     visible error, so the demand signal for other classes is not eaten.
//   - FAILS CLOSED: any input outside the profile — genuinely-ambiguous
//     nesting (a wrapper that is mid-key, awaiting a value, or after a `,`),
//     an over-closed document, an unterminated string, a truncated tail, or a
//     repaired text that still does not parse — declines, and `repair` moves on
//     to the next catalogue entry.
//   - NAMED: a repair is returned with the id `implied-node-close`, and the
//     decoder's opt-in lenient path counts it under the `Reliance` counter of
//     the same id. This is error REPAIR, not §16 shorthand normalisation — a
//     silently-repaired class stops generating demand signal, and the name is
//     what keeps it measurable while ceasing to be a loss (see
//     `docs/migrations/850-implied-node-close-recovery.md`).

module private ImpliedNodeClose =
    [<RequireQualifiedAccess>]
    type Kind =
        | Obj
        | Arr

    /// Between-token expectation of the innermost open container. A container
    /// with an open child sits in `Value` (object) / `Value` or `ValueOrClose`
    /// (array) until the child completes — which is what makes the owed-wrapper
    /// chain checkable: a frame BELOW the top is mid-value by construction, and
    /// its pending value IS the frame above it.
    [<RequireQualifiedAccess>]
    type State =
        /// Object, after `{` — a key or `}` may follow.
        | KeyOrClose
        /// Object, after `,` — a key must follow.
        | Key
        /// Object, after a key — `:` must follow.
        | Colon
        /// A value must follow (object: after `:`; array: after `,`).
        | Value
        /// After a complete member / element — `,` or the closer.
        | CommaOrClose
        /// Array, after `[` — a value or `]` may follow.
        | ValueOrClose

    type Frame =
        {
            Kind: Kind
            mutable State: State
            /// Arr frames only: the object key whose value this array is (None
            /// for an array nested directly in an array) — the profile gate.
            ArrKey: string option
            /// Obj frames only: the most recently read member key.
            mutable LastKey: string option
        }

    /// The array keys the mid-document recovery may close owed wrappers into —
    /// the node-list positions of the measured class. Deliberately NOT every
    /// array: a dropped brace in a data array stays a visible error.
    let private recoveryArrayKeys = [ "children"; "cases" ]

    /// Scan `text`, closing owed node wrappers at ancestor-legal tokens.
    /// `Some repaired` when the profile matched and a bounded repair exists;
    /// `None` otherwise (the caller falls back to the original error).
    let tryRecover (text: string) : string option =
        if isNull (box text) then
            None
        else
            let n = text.Length
            let mutable i = 0
            let stack = ResizeArray<Frame>()
            let inserts = ResizeArray<int>()
            let mutable rootDone = false
            let mutable failed = false
            let mutable finished = false

            let isWsChar c =
                c = ' ' || c = '\t' || c = '\n' || c = '\r'

            let skipWsLocal () =
                while i < n && isWsChar text[i] do
                    i <- i + 1

            // Skip a string literal (cursor on the opening quote); false on an
            // unterminated string — the truncation fingerprint, never recovered.
            let skipString () =
                i <- i + 1
                let mutable closed = false

                while not closed && i < n do
                    let c = text[i]

                    if c = '\\' then
                        i <- i + 2
                    elif c = '"' then
                        i <- i + 1
                        closed <- true
                    else
                        i <- i + 1

                closed

            let readKey () : string option =
                let start = i

                if skipString () then
                    Some(text.Substring(start + 1, i - start - 2))
                else
                    None

            let top () = stack[stack.Count - 1]

            // A completed value: advance the enclosing frame (or mark the root
            // value complete).
            let completeValue () =
                if stack.Count = 0 then
                    rootDone <- true
                else
                    (top ()).State <- State.CommaOrClose

            let isScalarStart c =
                c = '-' || (c >= '0' && c <= '9') || c = 't' || c = 'f' || c = 'n'

            let skipScalar () =
                let isScalarChar c =
                    c = '-'
                    || c = '+'
                    || c = '.'
                    || (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')

                while i < n && isScalarChar text[i] do
                    i <- i + 1

            // Consume the opening of one value at the cursor. Malformed scalars
            // are tolerated here — the repaired text re-parses through the real
            // parser, which is the validator of record.
            let openValue () : bool =
                let c = text[i]

                if c = '{' then
                    stack.Add
                        { Kind = Kind.Obj
                          State = State.KeyOrClose
                          ArrKey = None
                          LastKey = None }

                    i <- i + 1
                    true
                elif c = '[' then
                    let key =
                        if stack.Count > 0 && (top ()).Kind = Kind.Obj then
                            (top ()).LastKey
                        else
                            None

                    stack.Add
                        { Kind = Kind.Arr
                          State = State.ValueOrClose
                          ArrKey = key
                          LastKey = None }

                    i <- i + 1
                    true
                elif c = '"' then
                    if skipString () then
                        completeValue ()
                        true
                    else
                        false
                elif isScalarStart c then
                    skipScalar ()
                    completeValue ()
                    true
                else
                    false

            let isClosableObj (f: Frame) =
                f.Kind = Kind.Obj
                && (f.State = State.CommaOrClose || f.State = State.KeyOrClose)

            // Close the owed object-wrapper chain so the current token can be
            // consumed at the nearest enclosing array — the ancestor-legal-token
            // rule. The TOP frame must be a closable object (between members);
            // every frame beneath it in the chain is an object mid-value (its
            // pending value is the frame above, completed by the implied close);
            // the chain ends at the first enclosing array, which must be keyed
            // `children` / `cases`. Anything else fails closed.
            let closeOwedWrappers () : bool =
                if stack.Count = 0 || not (isClosableObj (top ())) then
                    false
                else
                    let mutable k = 1

                    while k < stack.Count
                          && stack[stack.Count - 1 - k].Kind = Kind.Obj
                          && stack[stack.Count - 1 - k].State = State.Value do
                        k <- k + 1

                    if k >= stack.Count then
                        false // the chain ran to the root — no enclosing array
                    else
                        let target = stack[stack.Count - 1 - k]

                        let profileOk =
                            target.Kind = Kind.Arr
                            && (match target.ArrKey with
                                | Some key -> List.contains key recoveryArrayKeys
                                | None -> false)

                        if not profileOk then
                            false
                        else
                            for _ in 1..k do
                                inserts.Add i
                                stack.RemoveAt(stack.Count - 1)

                            // The implied closes complete the array's pending
                            // element.
                            target.State <- State.CommaOrClose
                            true

            skipWsLocal ()

            if i >= n || text[i] <> '{' then
                None
            else
                openValue () |> ignore // pushes the root object frame

                while not failed && not finished do
                    skipWsLocal ()

                    if i >= n then
                        finished <- true
                    elif rootDone then
                        failed <- true // trailing content
                    else
                        let f = top ()
                        let c = text[i]

                        match f.Kind, f.State with
                        | Kind.Obj, State.KeyOrClose ->
                            if c = '}' then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            elif c = '"' then
                                match readKey () with
                                | Some key ->
                                    f.LastKey <- Some key
                                    f.State <- State.Colon
                                | None -> failed <- true
                            else
                                failed <- true
                        | Kind.Obj, State.Key ->
                            if c = '"' then
                                match readKey () with
                                | Some key ->
                                    f.LastKey <- Some key
                                    f.State <- State.Colon
                                | None -> failed <- true
                            else
                                failed <- true
                        | Kind.Obj, State.Colon ->
                            if c = ':' then
                                i <- i + 1
                                f.State <- State.Value
                            else
                                failed <- true
                        | Kind.Obj, State.Value -> failed <- not (openValue ())
                        | Kind.Obj, State.CommaOrClose ->
                            if c = ',' then
                                // Lookahead: an object continuation must be a
                                // key. `,` then `{` is only legal at an ancestor
                                // ARRAY — the second signature of the class (the
                                // wrapper owed its close before the separator).
                                let save = i
                                i <- i + 1
                                skipWsLocal ()

                                if i < n && text[i] = '"' then
                                    f.State <- State.Key
                                elif i < n && text[i] = '{' then
                                    i <- save

                                    if not (closeOwedWrappers ()) then
                                        failed <- true
                                else
                                    failed <- true
                            elif c = '}' then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            elif c = ']' then
                                // The first signature of the class: `]` while
                                // node wrappers inside the array are still open.
                                if not (closeOwedWrappers ()) then
                                    failed <- true
                            else
                                failed <- true
                        | Kind.Obj, State.ValueOrClose -> failed <- true // unreachable
                        | Kind.Arr, (State.ValueOrClose | State.Value) ->
                            if c = ']' && f.State = State.ValueOrClose then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            else
                                failed <- not (openValue ())
                        | Kind.Arr, State.CommaOrClose ->
                            if c = ',' then
                                i <- i + 1
                                f.State <- State.Value
                            elif c = ']' then
                                i <- i + 1
                                stack.RemoveAt(stack.Count - 1)
                                completeValue ()
                            else
                                failed <- true
                        | Kind.Arr, _ -> failed <- true // unreachable

                if failed then
                    None
                else
                    // EOF. The TOP frame's own state gates (between members /
                    // elements only — a mid-key, post-`:`, or post-`,` cut is
                    // genuine truncation, never recovered). Frames below it are
                    // mid-value by construction (their value IS the frame
                    // above), so the implied close of the child completes them.
                    let eofCloses = ResizeArray<char>()
                    let mutable eofOk = true

                    if not rootDone then
                        if stack.Count = 0 then
                            eofOk <- false
                        else
                            let t = top ()

                            let topClosable =
                                match t.Kind, t.State with
                                | Kind.Obj, (State.CommaOrClose | State.KeyOrClose) -> true
                                | Kind.Arr, (State.CommaOrClose | State.ValueOrClose) -> true
                                | _ -> false

                            if not topClosable then
                                eofOk <- false
                            else
                                for idx in stack.Count - 1 .. -1 .. 0 do
                                    eofCloses.Add(if stack[idx].Kind = Kind.Obj then '}' else ']')

                    if not eofOk then
                        None
                    elif inserts.Count = 0 && eofCloses.Count = 0 then
                        // The scanner consumed the document cleanly but the real
                        // parser rejected it — the failure is not this class.
                        None
                    elif inserts.Count + eofCloses.Count > Fuaran.UI.WireLimits.MaxJsonDepth then
                        None
                    else
                        // Insert positions are ascending by construction.
                        let sb = System.Text.StringBuilder(n + inserts.Count + eofCloses.Count)

                        let mutable prev = 0

                        for pos in inserts do
                            sb.Append(text.Substring(prev, pos - prev)).Append '}' |> ignore
                            prev <- pos

                        sb.Append(text.Substring prev) |> ignore

                        for ch in eofCloses do
                            sb.Append ch |> ignore

                        Some(sb.ToString())
// ─── over-close-unique (fuaran#855; §28.2) ────────────────────────────────
//
// The MIRROR of the fuaran#850 class, with the sign of the defect reversed and
// the default of the gate reversed with it. 850's emission owes a closer and
// drops it; this one emits a closer it does not owe — `…}}}` where `}}` was
// owed, one level past the node. Same boundary, opposite direction.
//
// The two are not symmetric problems, and the asymmetry is structural rather
// than incidental. An OWED closer has exactly one legal home: when `]` arrives
// with objects still open inside the array, the grammar admits precisely one
// repair, which is why 850 could measure its recovery as determined. A SURPLUS
// closer has as many candidate homes as there are enclosing levels, and every
// choice re-assigns the fields that follow it to a different owner. Measured
// over the stored instances of this class: on the grammar alone most admit two
// to five distinct minimal-deletion repairs, and after decoding every candidate
// through this very decoder, six of the first sixteen STILL admit two to five
// repairs that each decode clean.
//
// What that ambiguity costs is field ownership, not node placement — which is
// what makes it dangerous. On the worst measured cell the five clean repairs
// produce the IDENTICAL node skeleton and differ only in which object owns the
// five trailing fields; the LEFTMOST legal deletion — the obvious
// implementation — buries all five inside a `Static` binding and renders a bare
// unformatted number with no trend and no icon. That tree passes every gate.
// The reliance counter can report THAT a coercion happened; it cannot report
// that the coercion chose the right owner, and nothing downstream can either.
//
// CONTRACT — the same shape as 850, with the opposite default:
//   - Attempted ONLY on a document the strict parser refuses with
//     `INVALID_JSON`, and after `implied-node-close` has declined (850 fails
//     closed on over-closure, so the two never contend). Never on
//     `LIMIT_EXCEEDED`, never on a document that parses.
//   - PROFILE-GATED: a string-aware structural scan must show the document
//     NET over-closed by one or two closers, with the surplus uncompensated
//     (the running minimum depth equals the final depth — a surplus that is
//     later re-opened is a differently-shaped defect), and not cut inside a
//     string.
//   - DELETE-ONLY, BOUNDED: candidates are the document with one or two
//     structural closers removed, enumerated exhaustively within stated bounds.
//     Nothing is ever inserted, and no key, value or bracket is invented.
//   - ACCEPT IFF EXACTLY ONE CANDIDATE DECODES CLEAN. Uniqueness is evaluated
//     over the COMPLETE enumeration, de-duplicated by parsed value — two
//     deletions inside one whitespace-interrupted closer run yield different
//     strings and the identical document, and that is one repair, not two.
//   - REFUSES BY DEFAULT: zero clean candidates, two or more, or an enumeration
//     past the bounds are each a named `NotRepairable` refusal, and the strict
//     decode's `INVALID_JSON` stands. An `INVALID_JSON` the demand loop can
//     feed on is worth more than a wrong tree no counter can flag.
//   - COUNTED BOTH WAYS on the decoder's opt-in lenient path:
//     `Reliance.OverCloseUnique` on an acceptance, `Reliance.OverCloseRefused`
//     on a refusal (see
//     `docs/migrations/855-uniqueness-gated-overclose-recovery.md`).
//
// The failure-offset rule — "the repair lies in the contiguous closer run
// ending at the first mismatch" — was measured on the same set and selects a
// schema-clean repair in eleven of sixteen. It is used here for candidate
// ORDERING ONLY, and it is worth being explicit that ordering CANNOT change the
// verdict: uniqueness is a property of the whole enumeration, so a rule that
// merely reorders it can never override the count. Five of sixteen have their
// true repair outside that run, which is exactly why it is not the gate.

module private OverClose =
    /// The surplus this gate will consider. Every measured instance is one or
    /// two; three would be a differently-shaped defect, not a deeper one.
    [<Literal>]
    let MaxSurplus = 2

    /// Structural closers the enumeration will draw from. The largest measured
    /// instance is a 34 KB document with 286.
    [<Literal>]
    let MaxCloserPositions = 512

    /// Deletion sets enumerated. The largest measured instance needs 2,628
    /// (a surplus of two over 73 closers); past this the gate refuses rather
    /// than spending unbounded work on the failure path.
    [<Literal>]
    let MaxDeletionSets = 8192

    /// Distinct parseable candidates decoded. The largest measured instance
    /// yields five; a document yielding more than this is not the measured
    /// class and the gate refuses rather than widening.
    [<Literal>]
    let MaxDistinctCandidates = 32

    type Profile =
        {
            /// Surplus closers — 1 or 2 for every measured instance.
            Surplus: int
            /// Every structural closer position, ascending.
            Closers: int[]
            /// The contiguous closer run ending at the first mismatch
            /// (whitespace-tolerant), inclusive. ORDERING ONLY.
            RunLo: int
            RunHi: int
        }

    /// String-aware structural scan. `Some profile` when the document is
    /// over-closed by a net, uncompensated one or two closers and is not cut
    /// inside a string; `None` otherwise — and a `None` here is NOT a refusal
    /// of this class, it is a document that was never in it, so nothing is
    /// counted for it.
    ///
    /// The FIRST STRUCTURAL MISMATCH is where a closer either finds no open
    /// bracket at all or disagrees in kind with the innermost one — which for
    /// this class is the surplus closer itself, since it pops the enclosing
    /// `children[]` array with a `}`. It is the position the parser fails at,
    /// recomputed here rather than recovered from the error message.
    ///
    /// A note on what is deliberately NOT checked. "No crossed brackets before
    /// the mismatch" reads like a second gate and is in fact vacuous: the
    /// mismatch IS the first crossing, so nothing can cross before it. Stating
    /// it as an implemented condition would have been decoration, and an
    /// earlier draft of this scan did exactly that — it evaluated crossing over
    /// the whole document and so rejected every real instance of the class,
    /// because the surplus closer is itself the crossing.
    let profile (text: string) : Profile option =
        if isNull (box text) || text.Length = 0 then
            None
        else
            let n = text.Length
            let closers = ResizeArray<int>()
            let opens = ResizeArray<char>()
            let mutable i = 0
            let mutable depth = 0
            let mutable minDepth = 0
            let mutable firstMismatch = -1
            let mutable inString = false

            while i < n do
                let c = text[i]

                if inString then
                    if c = '\\' then
                        i <- i + 1
                    elif c = '"' then
                        inString <- false
                elif c = '"' then
                    inString <- true
                elif c = '{' || c = '[' then
                    depth <- depth + 1
                    opens.Add c
                elif c = '}' || c = ']' then
                    closers.Add i
                    depth <- depth - 1

                    if opens.Count = 0 then
                        if firstMismatch < 0 then
                            firstMismatch <- i
                    else
                        let opened = opens[opens.Count - 1]
                        opens.RemoveAt(opens.Count - 1)

                        if (opened = '{') <> (c = '}') && firstMismatch < 0 then
                            firstMismatch <- i

                    if depth < minDepth then
                        minDepth <- depth

                i <- i + 1

            let surplus = -depth

            if inString then
                None // cut inside a string — the truncation fingerprint
            elif depth >= 0 || minDepth <> depth then
                // Not net over-closed, or the surplus is compensated by a later
                // re-opening — a differently-shaped defect either way.
                None
            elif surplus > MaxSurplus then
                None
            elif firstMismatch < 0 then
                None
            else
                // Walk back from the mismatch over the contiguous run of
                // closers. Whitespace between them is part of the run — a
                // pretty-printed emission separates its closers by newlines.
                let mutable lo = firstMismatch
                let mutable j = firstMismatch - 1
                let mutable scanning = true

                while scanning && j >= 0 do
                    let c = text[j]

                    if c = ' ' || c = '\t' || c = '\n' || c = '\r' then
                        j <- j - 1
                    elif c = '}' || c = ']' then
                        lo <- j
                        j <- j - 1
                    else
                        scanning <- false

                Some
                    { Surplus = surplus
                      Closers = closers.ToArray()
                      RunLo = lo
                      RunHi = firstMismatch }

    /// The candidate repaired documents, failure-run-first. `None` when a bound
    /// is exceeded — which IS a refusal of a profile-matching document, so the
    /// caller counts it.
    ///
    /// **Lazy on purpose.** A surplus of two enumerates every unordered pair of
    /// deletions, so the candidate COUNT is quadratic in the closer positions —
    /// `m(m-1)/2`, up to `MaxDeletionSets` — and each candidate is a full copy
    /// of the document. Materialising them all at once therefore costs
    /// `sets x length`, which at the bounds is hundreds of megabytes of live
    /// string before a single one is parsed: 8,128 candidates of a 34 KB
    /// document is ~527 MiB, and the largest MEASURED instance (73 closers, a
    /// surplus of two) is 2,628 candidates.
    ///
    /// That cost lands on the SUCCESS path, not just a pathological one. The
    /// caller must see the whole enumeration to decide uniqueness — a second
    /// clean decode is what turns an acceptance into a refusal — so it cannot
    /// stop early, and a document that recovers pays in full.
    ///
    /// Yielding one at a time makes the peak `O(length)` instead. It does NOT
    /// change the verdict, and that is the point: same candidates, same order,
    /// same count, so every accept/refuse is bit-for-bit what the eager form
    /// gave. Only the work stays quadratic; the memory no longer is. The bound
    /// check below is still EAGER — it is arithmetic on the closer count, so a
    /// refusal past the bounds is decided before anything is generated.
    let candidates (text: string) (p: Profile) : string seq option =
        let m = p.Closers.Length

        let sets =
            if p.Surplus = 1 then
                int64 m
            else
                int64 m * int64 (m - 1) / 2L

        if m > MaxCloserPositions || sets > int64 MaxDeletionSets then
            None
        else
            let inRun pos = pos >= p.RunLo && pos <= p.RunHi

            // `b < 0` for a single deletion; otherwise `a < b`.
            let repaired (a: int) (b: int) =
                let sb = System.Text.StringBuilder(text.Length)

                if b < 0 then
                    sb.Append(text.Substring(0, a)).Append(text.Substring(a + 1)) |> ignore
                else
                    sb
                        .Append(text.Substring(0, a))
                        .Append(text.Substring(a + 1, b - a - 1))
                        .Append(text.Substring(b + 1))
                    |> ignore

                sb.ToString()

            // Two passes: the failure-run-touching sets first. Ordering only —
            // the verdict is a count over the union of both passes.
            Some(
                seq {
                    for pass in 0..1 do
                        if p.Surplus = 1 then
                            for x in 0 .. m - 1 do
                                let a = p.Closers[x]

                                if inRun a = (pass = 0) then
                                    yield repaired a -1
                        else
                            for x in 0 .. m - 2 do
                                for y in x + 1 .. m - 1 do
                                    let a = p.Closers[x]
                                    let b = p.Closers[y]

                                    if (inRun a || inRun b) = (pass = 0) then
                                        yield repaired a b
                }
            )

/// What the strict parser says about one text — the three answers `repair`
/// needs.
[<RequireQualifiedAccess>]
type internal Parse<'J> =
    | Parsed of 'J
    | Malformed
    | OverLimit

/// `repair`, parameterised by the host's strict parser and its strict node
/// decoder (WIRE_FORMAT.md §28.3). The decoder supplies both and publishes the
/// result as `JsonDecode.repair`.
///
/// The procedure, in the order the specification states it:
///   1. A text that parses is returned unchanged, applied `[]`.
///   2. A text refused for a §21 limit is `limit-exceeded`.
///   3. `implied-node-close`: if the insert-only scan yields a repair and the
///      repaired text parses, that is the result.
///   4. `over-close-unique`: if the text is in the over-close profile, the
///      candidates are enumerated, de-duplicated by parsed value, and each
///      distinct one is decoded; exactly one clean decode is the result — the
///      FIRST candidate text, in enumeration order, that parses to it.
///   5. Otherwise `not-in-catalogue`.
let internal repairWith<'J when 'J: equality>
    (parse: string -> Parse<'J>)
    (decodesClean: 'J -> bool)
    (text: string)
    : RepairOutcome =
    match parse text with
    | Parse.Parsed _ -> RepairOutcome.Repaired(text, [])
    | Parse.OverLimit -> RepairOutcome.NotRepairable Refusal.LimitExceeded
    | Parse.Malformed ->
        let implied =
            match ImpliedNodeClose.tryRecover text with
            | Some repaired ->
                match parse repaired with
                | Parse.Parsed _ -> Some repaired
                | Parse.Malformed
                | Parse.OverLimit -> None
            | None -> None

        match implied with
        | Some repaired -> RepairOutcome.Repaired(repaired, [ RepairId.ImpliedNodeClose ])
        | None ->
            match OverClose.profile text with
            | None -> RepairOutcome.NotRepairable Refusal.NotInCatalogue
            | Some p ->
                // The length ceiling is checked AFTER the profile scan, so only
                // documents genuinely in the class are refused under it. The
                // scan is one linear pass; the enumeration is the amplifier,
                // because every candidate is a full copy plus a full re-parse.
                if text.Length > MaxOverCloseLength then
                    RepairOutcome.NotRepairable Refusal.OverCloseBounds
                else
                    match OverClose.candidates text p with
                    | None -> RepairOutcome.NotRepairable Refusal.OverCloseBounds
                    | Some cands ->
                        // De-duplicate on the PARSED VALUE: deleting either of two
                        // closers separated only by whitespace yields two strings
                        // and one document, and that is one repair, not two.
                        let seen = ResizeArray<'J>()
                        let mutable clean = 0
                        let mutable accepted: string option = None
                        let mutable overflow = false

                        // `cands` is lazy, so each candidate is built, parsed and
                        // dropped before the next exists. The enumerator is
                        // stepped by hand because the overflow guard must stop
                        // GENERATION, not merely skip the remaining items.
                        use e = cands.GetEnumerator()

                        while not overflow && e.MoveNext() do
                            let candidate = e.Current

                            match parse candidate with
                            | Parse.Parsed j ->
                                let mutable known = false

                                for k in 0 .. seen.Count - 1 do
                                    if not known && seen[k] = j then
                                        known <- true

                                if not known then
                                    seen.Add j

                                    if seen.Count > OverClose.MaxDistinctCandidates then
                                        overflow <- true
                                    elif decodesClean j then
                                        clean <- clean + 1

                                        if clean = 1 then
                                            accepted <- Some candidate
                            | Parse.Malformed
                            | Parse.OverLimit -> ()

                        // The whole gate, in one line: exactly one, or nothing.
                        if overflow then
                            RepairOutcome.NotRepairable Refusal.OverCloseBounds
                        else
                            match clean, accepted with
                            | 1, Some repaired -> RepairOutcome.Repaired(repaired, [ RepairId.OverCloseUnique ])
                            | 0, _ -> RepairOutcome.NotRepairable Refusal.OverCloseNoCleanCandidate
                            | _ -> RepairOutcome.NotRepairable Refusal.OverCloseAmbiguous
