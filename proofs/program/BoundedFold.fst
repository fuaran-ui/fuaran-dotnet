(*
   Copyright 2026 Diametrical Ltd

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*)

/// Phase 1715 — the shared bounded fold, modelled in F* and proved.
/// Phase 1898 — the same fold restated over the ACTION VIEW of the
/// generic tier (DECISIONS.md D18, `docs/generic-tier.md` §3), written
/// before the code that will meet it (D14).
///
/// # What this module is
///
/// Two layers, one file.
///
/// **The generic tier** — a model of the fold the generic core will run,
/// `BoundedActions.run witness` (Phase 1896 writes it): one `match` over
/// the shapes of D18's `ActionView` — four at the cut, FIVE since Phase
/// 1967 added the halting guard `Require` (the second witness's F1),
/// SEVEN since Phase 1976 added the selection `Choose` and the bounded
/// iteration `Repeat` that D1 and D2 charter (the second witness's
/// re-run found the core had sequence and abort but neither) —
/// parameterised by a WITNESS record that is the fold-read members of
/// `ProgramWitness`. Phase 1896 wrote the F# against the four-shape
/// model; Phase 1967 restated and re-proved this model over five shapes
/// BEFORE the port that followed (D14 again), and the one law the fifth
/// shape changes is the sequence homomorphism, which gains a halting
/// clause that is vacuous for every view without a guard. Phase 1976
/// restated it again over seven, before its port: the two new shapes
/// are COMPOSITION shapes, so the structural characterisation
/// (`fold_total`) excludes them as it excludes a sequence; the halting
/// clause now has three sources (`fold_no_halting_shape_no_halt`); a
/// repeat is its own unrolling (`repeat_is_unrolling`); and the fragment
/// of the view that can run BACKWARDS is named, decided from the tree,
/// and proved to undo itself (`reverse_run`, section 6 below). EIGHT
/// since Phase 1990 added `Each`, per-element iteration over a LITERAL
/// collection, seen here LOWERED: the view carries the body once per
/// element with that element substituted for the placeholder — the
/// substitution is the witness's (`ActionWitness.Substitute`), as `View`
/// and `Lower` are, because the fold cannot see inside an action — and
/// the fold runs the sequence of those elements. Every theorem below
/// extends over it as over a sequence, and `each_is_lowering` (section 9)
/// is the equation: an `Each` folds, traces, reverses and prices exactly
/// as the sequence of its elements.
/// Every definition is captioned with the D18 contract member it models
/// (§3.2 the action view, §3.3 expressions, §3.4 the store).
///
/// **The UI witness** — today's fourteen-arm `Action` union seen THROUGH
/// the view: `ui_view` is the adapter's total `View`, `ui_lower` its
/// `Lower`, and `run` is `run_action` at that witness, with the exact
/// signature Phase 1715 gave it. The differential host
/// (`tests/Fuaran.Program.Parity.Tests/ProofOracleTests.fs`) runs the
/// EXTRACTION of `run` beside `BoundedActions.runBoundedActionWith` over
/// the conformance corpus's driver-semantics family and an arm-complete
/// action corpus, and requires the store, the effect list and the
/// diagnostics to agree at every step — that host is the only thing that
/// says this model is about the code that ships. It runs unchanged on
/// this restatement, which is the claim that the fourteen arms seen
/// through the view ARE the fourteen arms.
///
/// # Every parameter of the model is an assumption
///
/// The generic fold takes one witness and one placement arm. What each
/// arrow is assumed to be, and which theorem leans on it:
///
///   * `w_view` (D18 `ActionWitness.View`) — a TOTAL arrow. Here the
///     view is taken to exhaustion (`action_view` is a tree), so the
///     obligation that the F# `View`, applied repeatedly, unfolds a finite
///     tree is carried by this field's type: a witness whose `View` put an
///     action inside its own `Sequence` could not be written here. The UI
///     witness discharges it by `ui_view` being accepted as `Tot`.
///     Every theorem below leans on it, since every theorem is about
///     `fold` over a finite view.
///   * `w_lower` (D18 `ActionWitness.Lower`) — a total arrow whose
///     RESULT TYPE is the kept assumption K3: at most one effect, or a
///     refusal, or a decline, and no store. A leaf that wrote the store
///     or emitted a list cannot be expressed. `fold_total` states what
///     that buys.
///   * `w_describe` (D18 `ActionWitness.Describe`) — a total arrow; the
///     diagnostics carry its answer verbatim.
///   * `w_resolve` (D18 `ExprWitness.Resolve`) — a total pure arrow, the
///     same answer for the same store. Nothing proved here depends on
///     what it answers, only on the fold consulting it where it does.
///   * `w_is_reserved` / `w_reserved_prefix` (D18 `StoreWitness`) — a
///     total predicate and a constant (K5). `fold_reserved_untouched` is
///     quantified over every such predicate.
///   * `answer` (the placement arm, `HandlerArm.Answer`) — what a call
///     MEANS at this placement, opaque. `fold_total` names the answered
///     case and excludes it; `fold_reserved_untouched` carries the seam's
///     obligation `arm_preserves_reserved` as a hypothesis, which
///     `inert_preserves_reserved` discharges for the arm that declines.
///
/// **No-closure-invocation is an obligation on the witness, stated as a
/// precondition (D18).** The core holds no closure: nothing in the view
/// is one, and the fold reads an action only through `w_describe`,
/// `w_lower` and the shape `w_view` gave it. `fold_blind` proves that
/// half UNCONDITIONALLY — two views of the same shape whose actions
/// describe and lower alike fold identically. `blind_to` states the
/// witness's half — a relation on actions under which `w_view` produces
/// same-shaped views — and `run_action_blind` is the theorem conditional
/// on it. `ui_blind_to_closures` discharges that obligation for the UI
/// witness under `same_but_closures`, so `run_no_closure` — Phase 1715's
/// statement, word for word — is again unconditional.
///
/// # The store is modelled concretely, on purpose
///
/// D18 §3.4 makes the store abstract behind `StoreWitness.Assign`. The
/// model keeps a concrete keyed channel (an association list, `write`),
/// because `fold_reserved_untouched` is a theorem ABOUT what is written,
/// and a theorem about writes to an abstract store would be a theorem
/// about an obligation nobody had stated. What the model claims of a
/// domain's `Assign` is therefore K4: one keyed state channel where an
/// assignment writes one key. A store whose `Assign` did something else
/// is outside this model, and the differential host's projection of the
/// domain store onto this channel is where that is checked.
///
/// # The theorems
///
///   Generic tier (over the view):
///   * `fold_total` — every view shape is named, with no wildcard; one
///     step that is neither `VSequence` nor an ANSWERED call leaves the
///     placement untouched, leaves the store identical or writes exactly
///     one key the reserved predicate rejects, and emits at most one
///     effect and at most one diagnostic (K3, as a theorem).
///   * `fold_blind` / `run_action_blind` — the core half and the
///     conditional whole of no-closure-invocation, above.
///   * `sequence_homomorphism` — `fold_many (app xs ys) s` is
///     `fold_many ys` applied to the store `fold_many xs s` left, effects
///     and diagnostics concatenated in order. DECISIONS.md D7's splice
///     property, as an equation, now over `VSequence`.
///   * `fold_reserved_untouched` — the output store agrees with the input
///     at every reserved key, for any arm that preserves them.
///
///   The UI witness (each a corollary of the generic theorem at
///   `ui_witness`, keeping its Phase-1715 name and statement):
///   * `run_total`, `run_no_closure`, `chain_homomorphism`,
///     `reserved_untouched`, `inert_preserves_reserved`.
///
/// Nothing about the browser placement, the durable journal, or the
/// per-connection session cells is modelled.

module BoundedFold

(* ───────────────────────────────────────────────────────────────────
   Small closed types the model owns — declared here rather than taken
   from `FStar.Pervasives.Native` or `FStar.List.Tot` so the extraction
   references `Prims` and nothing else. `oracle/Prims.fs` is that whole
   runtime; a model reaching for a further name would fail the byte-diff
   in `check.ps1` rather than silently compile against a widened shim.
   ─────────────────────────────────────────────────────────────────── *)

/// F#: `'a option`.
type opt (a: Type0) =
  | ONone : opt a
  | OSome : item: a -> opt a

/// F#: `List.append` / `xs @ ys`. Defined here for the reason above.
let rec app (#a: Type0) (xs: list a) (ys: list a) : Tot (list a) (decreases xs) =
  match xs with
  | [] -> ys
  | x :: rest -> x :: app rest ys

/// F#: `List.length`. Defined here for the reason above; the store-bound
/// `Each`'s ceiling check reads it (Phase 1991).
let rec length (#a: Type0) (xs: list a) : Tot nat (decreases xs) =
  match xs with
  | [] -> 0
  | _ :: rest -> 1 + length rest

(* ───────────────────────────────────────────────────────────────────
   The store — D18 §3.4 `StoreWitness`'s one state channel, today
   `BoundedActions.BoundedStore` (= `BindingSources`).`State`.

   Only the `State` map is written on this path, so only `State` is
   modelled: an association list over an abstract value type `v` (D18's
   `JVal`, which the fold never inspects). The other channels are host
   context the fold never writes and only ever reads THROUGH the
   witness's `Resolve`, so they reach the model as part of that arrow.
   ─────────────────────────────────────────────────────────────────── *)

type key = string

type store (v: Type0) = list (key & v)

/// F#: `Map.tryFind`.
let rec lookup (#v: Type0) (s: store v) (k: key) : Tot (opt v) (decreases s) =
  match s with
  | [] -> ONone
  | (k', x) :: rest -> if k' = k then OSome x else lookup rest k

/// D18 §3.4 `StoreWitness.Assign`, modelled concretely (see the header).
/// F#: `Map.add` — replace in place when the key is present, append
/// otherwise. Replacing rather than shadowing is what keeps `lookup` in
/// agreement with a map's semantics, and what lets the differential host
/// compare the two stores as key-ordered sequences.
let rec write (#v: Type0) (s: store v) (k: key) (x: v) : Tot (store v) (decreases s) =
  match s with
  | [] -> [(k, x)]
  | (k', y) :: rest -> if k' = k then (k, x) :: rest else (k', y) :: write rest k x

(* ═══════════════════════════════════════════════════════════════════
   THE GENERIC TIER — D18 §3.2 / §3.3 / §3.4, the members the fold reads.
   ═══════════════════════════════════════════════════════════════════ *)

/// D18 §3.3 `Resolution = Resolved of JVal | NotResolved | Errored of
/// string` — three cases. The UI's fourth (`I18nUnresolved`) never
/// reaches the core: the adapter folds it into `Errored` (see
/// `ui_resolve`), and the refusal text is unchanged by that.
type resolution (v: Type0) =
  | Resolved : value: v -> resolution v
  | NotResolved : resolution v
  | Errored : message: string -> resolution v

/// The bound of a repeat (Phase 1976; D2: a literal count, or a parameter
/// checked against a range). `BLiteral` is known from the tree alone,
/// which is what admits a repeat to the reversible fragment
/// (`reversible`). `BParameter` resolves at dispatch through `w_resolve`,
/// is read as a count through `w_as_count`, and halts outside `[lo, hi]`
/// BEFORE the first iteration — the over-bound refusal — so the budget
/// can price it at `hi` without consulting the store (`view_cost`).
/// F#: `Bound<'Expr>`.
type bound (e: Type0) =
  | BLiteral : count: nat -> bound e
  | BParameter : count: e -> lo: nat -> hi: nat -> bound e

/// D18 §3.2 `ActionView<'Action, 'Expr>`, taken TO EXHAUSTION. The F#
/// `View` is one level — `Sequence of 'Action list` — and the F# fold
/// re-views each child as it reaches it. This model views the whole tree
/// first, so that termination of the fold is structural on the view and
/// the obligation that `View` unfolds finitely sits on the witness's
/// `w_view` field (a `Tot` arrow) rather than on a fuel the code does not
/// have. `act` is the action the F# fold holds in hand when it views it,
/// carried here so `w_describe` and `w_lower` can be given it.
///
/// EIGHT shapes since Phase 1990: the five of Phase 1967; the selection
/// and the bounded iteration D1 and D2 charter, which the second
/// witness's re-run found missing (Phase 1976: it had to carry a two-arm
/// branch beside the core); and per-element iteration over a literal
/// collection (Phase 1990). Four are COMPOSITION shapes — `VSequence`,
/// `VChoose`, `VRepeat`, `VEach` — whose step is their members' steps;
/// the other four are one step each, and `fold_total` characterises them.
type action_view (a: Type0) (e: Type0) (v: Type0) =
  /// `Sequence of 'Action list` — the composition arm (today: `Chain`).
  | VSequence : act: a -> ops: list (action_view a e v) -> action_view a e v
  /// `Assign of key * value: JVal option * from: 'Expr option` (today:
  /// `SetState`).
  | VAssign : act: a -> state_key: key -> value: opt v -> value_from: opt e -> action_view a e v
  /// `Call of endpoint: string * declaresTarget: bool` (today: `Call`; a
  /// declared target is refused, D9).
  | VCall : act: a -> endpoint: string -> declares_target: bool -> action_view a e v
  /// `Require of condition: 'Expr` — the HALTING guard (Phase 1967, the
  /// second witness's F1). The condition resolves against the store at
  /// dispatch exactly as an `Assign`'s `from` does; a guard that holds
  /// changes nothing, and one that does not HALTS the fold: nothing after
  /// it in the enclosing sequence runs, and the outcome says so. It is
  /// the one shape that halts (`fold_total`), and the one a view with no
  /// guard never reaches (`fold_no_require_no_halt`).
  | VRequire : act: a -> condition: e -> action_view a e v
  /// `Choose of entry: 'Expr * whenTrue: 'Action * whenFalse: 'Action *
  /// exit: 'Expr option` — SELECTION (Phase 1976). The entry condition
  /// resolves exactly as a guard's does: the boolean true takes
  /// `when_true`, any other value takes `when_false`, and an unresolved or
  /// an errored condition HALTS as a guard's would (a branch that cannot
  /// decide is a defect, not a default). The EXIT assertion, when carried,
  /// is resolved against the store the arm left and must HOLD after the
  /// true arm and FAIL after the false arm (Janus): violated, unresolved
  /// or errored, it halts with the assertion named in the reason, after
  /// the arm's effects, which the handler rolls back (D8). Absent, the
  /// branch runs forwards exactly the same and is outside the reversible
  /// fragment (`reversible`), because nothing then says which arm to undo.
  | VChoose : act: a -> entry: e -> when_true: action_view a e v -> when_false: action_view a e v -> exit: opt e -> action_view a e v
  /// `Repeat of bound: Bound<'Expr> * body: 'Action` — BOUNDED ITERATION
  /// (Phase 1976, D2). The body runs `count` times in sequence, stopping at
  /// the first halt, and sees NO index: an index is state, the body's one
  /// channel to state is the store it writes, and an index the body could
  /// overwrite would not be a function of the bound alone, which is what
  /// running the inverse the same number of times rests on. A repeat IS
  /// its unrolling (`repeat_is_unrolling`), so every sequence law covers it.
  | VRepeat : act: a -> count: bound e -> body: action_view a e v -> action_view a e v
  /// `Each of collection: JVal list * placeholder: string * body:
  /// 'Action` — PER-ELEMENT ITERATION over a LITERAL collection (Phase
  /// 1990, D29), seen LOWERED. `elements` is the body with each element
  /// of the collection substituted for the placeholder — one view per
  /// element, in collection order — as the witness's `Substitute` lowers
  /// it when the F# fold meets the shape. The fold never substitutes: it
  /// cannot see inside an action, so substitution is the witness's exactly
  /// as `View` and `Lower` are, and the obligation that `Substitute`
  /// preserves the body's shape (a placeholder replaced in every operand,
  /// nothing else moved) sits on the witness beside the obligation that
  /// `View` unfolds finitely. What the fold runs is the SEQUENCE of the
  /// elements (`each_is_lowering`): the bound is the list's length, fixed
  /// by the tree (D2); the element is a value in the tree and not a cell
  /// in the store, so the case D21 refused an index for does not arise;
  /// and the trace, the inverse and the cost are a sequence's. An empty
  /// collection is the empty sequence.
  | VEach : act: a -> elements: list (action_view a e v) -> action_view a e v
  /// `Each of collection: Collection<'Expr> (Stored (source, ceiling)) *
  /// placeholder * body` — PER-ELEMENT ITERATION over a collection the
  /// STORE holds (Phase 1991, D36), seen LOWERED over the extent the store
  /// holds at entry: `extent` is what the host read through the same
  /// resolution the fold performs, `elements` the body once per element
  /// of it, substituted. The fold reads the source ONCE at entry and
  /// checks the ceiling before the first element; past the check it runs
  /// the elements exactly as `VEach` does (`each_of_is_each_over_extent`).
  | VEachOf : act: a -> source: e -> ceiling: nat -> extent: list v -> elements: list (action_view a e v) -> action_view a e v
  /// `Leaf of LeafDeclaration` — every other domain act. The declaration
  /// is the DEMANDED projection's business and the fold never reads it,
  /// so it is not carried; what the fold does with a leaf is `w_lower`.
  | VLeaf : act: a -> action_view a e v

/// D18 §3.2 `LeafOutcome<'Effect>`. Its shape IS the kept assumption
/// K3: a leaf emits at most ONE effect, or is refused, or is declined,
/// and there is no case that returns a store.
type leaf_outcome (eff: Type0) =
  | Emit : emitted: eff -> leaf_outcome eff
  | Refuse : reason: string -> leaf_outcome eff
  | Decline : leaf_outcome eff

/// The fold-read members of D18's `ProgramWitness`, as one record: the
/// three of `ActionWitness` (§3.2), `ExprWitness.Resolve` (§3.3), and
/// the two of `StoreWitness` the refusal needs (§3.4). `Assign` is
/// modelled concretely by `write` (header); `LandQuery` is the handler's
/// landing and never the fold's. Every field is a TOTAL arrow, and the
/// header says what each theorem assumes of it.
noeq type witness (a: Type0) (e: Type0) (v: Type0) (eff: Type0) = {
  /// `ActionWitness.View`, applied to exhaustion.
  w_view: a -> action_view a e v;
  /// `ActionWitness.Lower: nodeId -> 'Action -> 'Store -> LeafOutcome`.
  w_lower: string -> a -> store v -> leaf_outcome eff;
  /// `ActionWitness.Describe` (today: `Validation.describeAction`).
  w_describe: a -> string;
  /// `ExprWitness.Resolve`.
  w_resolve: store v -> e -> resolution v;
  /// `StoreWitness.IsReserved` (K5).
  w_is_reserved: key -> bool;
  /// `StoreWitness.ReservedPrefix`, for the refusal's text.
  w_reserved_prefix: string;
  /// The guard's truth test: whether a resolved value is the boolean
  /// `true`. NOT a witness member in F# — the core owns `JVal` and the
  /// fold tests `jv = JBool true` itself — but `v` is abstract in this
  /// model, exactly as the store is concrete there and abstract here, so
  /// the test arrives as an arrow the differential host wires to that
  /// comparison. Nothing proved here depends on what it answers.
  w_is_true: v -> bool;
  /// The bound's count test (Phase 1976): whether a resolved value is a
  /// non-negative integer, and which. The same kind of arrow as
  /// `w_is_true`, for the same reason — the core owns `JVal` and reads
  /// `JInt n` itself; here `v` is abstract. Nothing proved here depends
  /// on what it answers.
  w_as_count: v -> opt nat;
  /// The store-bound `Each`'s collection test (Phase 1991): whether a
  /// resolved value is a collection, and its elements. The same kind of
  /// arrow as `w_as_count` — the core reads `JArr xs` itself; here `v` is
  /// abstract. Nothing proved here depends on what it answers.
  w_as_elements: v -> opt (list v);
}

/// F#: `BoundedDiagnostic` — program-owned, so not generic. The action
/// is named by its log-safe description rather than carried, exactly as
/// production does — which is also what makes two runs that differ only
/// in carried closures produce EQUAL diagnostics.
type diagnostic =
  | DUnsupported : node_id: string -> action_name: string -> diagnostic
  | DRefused : node_id: string -> action_name: string -> reason: string -> diagnostic

/// F#: `BoundedOutcome`, generic in the effect the witness lowers to.
/// `o_halted` (Phase 1967) says a guard halted the fold: the store is
/// the store as of the halt — the fold does not roll back; a placement
/// that rolls back is the handler (D8) — and nothing after the guard in
/// the enclosing sequence ran.
type bounded_outcome (v: Type0) (eff: Type0) = {
  o_store: store v;
  o_effects: list eff;
  o_diagnostics: list diagnostic;
  o_halted: bool;
}

/// F#: `HandlerAnswer<'Placement>`.
type handler_answer (v: Type0) (eff: Type0) (p: Type0) = {
  h_store: store v;
  h_effects: list eff;
  h_diagnostics: list diagnostic;
  h_placement: p;
}

/// F#: `HandlerArm<'Placement>` — what a call action MEANS at this
/// placement. Opaque: `ONone` DECLINES, which is the documented no-op
/// every placement with no handler registry gives.
noeq type handler_arm (v: Type0) (eff: Type0) (p: Type0) = {
  answer: string -> string -> store v -> p -> opt (handler_answer v eff p);
}

/// F#: `HandlerArm.inert` — the arm that declines every call.
let inert_arm (#v: Type0) (#eff: Type0) (#p: Type0) : handler_arm v eff p =
  { answer = (fun _ _ _ _ -> ONone) }

(* ───────────────────────────────────────────────────────────────────
   The three outcome constructors — `BoundedActions.store` / `noOp` /
   `refused`. They take the DESCRIPTION rather than the action: the
   generic fold has no action vocabulary to describe, only the witness's
   answer.
   ─────────────────────────────────────────────────────────────────── *)

let store_only (#v: Type0) (#eff: Type0) (s: store v) : bounded_outcome v eff =
  { o_store = s; o_effects = []; o_diagnostics = []; o_halted = false }

let declined (#v: Type0) (#eff: Type0) (node_id: string) (description: string) (s: store v)
  : bounded_outcome v eff =
  { o_store = s; o_effects = []; o_diagnostics = [ DUnsupported node_id description ]; o_halted = false }

let refused (#v: Type0) (#eff: Type0)
            (node_id: string) (description: string) (reason: string) (s: store v)
  : bounded_outcome v eff =
  { o_store = s; o_effects = []; o_diagnostics = [ DRefused node_id description reason ]; o_halted = false }

/// F#: `BoundedActions.halted` (Phase 1967) — a refusal that HALTS: the
/// same diagnostic a non-halting refusal carries, and the flag.
let halted (#v: Type0) (#eff: Type0)
           (node_id: string) (description: string) (reason: string) (s: store v)
  : bounded_outcome v eff =
  { o_store = s; o_effects = []; o_diagnostics = [ DRefused node_id description reason ]; o_halted = true }

/// F#: `BoundedActions.haltedAfter` (Phase 1976) — a halt that follows an
/// ARM that ran: the arm's store, effects and diagnostics kept, the halt's
/// diagnostic appended, and the flag. A violated exit assertion is this:
/// the arm's writes stand as of the halt (the fold never rolls back; the
/// handler does, D8), and the outcome says which assertion failed.
let halted_after (#v: Type0) (#eff: Type0)
                 (o: bounded_outcome v eff) (node_id: string) (description: string) (reason: string)
  : bounded_outcome v eff =
  { o_store = o.o_store;
    o_effects = o.o_effects;
    o_diagnostics = app o.o_diagnostics [ DRefused node_id description reason ];
    o_halted = true }

/// F#: the `Result<JVal option, string>` the `Assign` arm computes.
type jval_payload (v: Type0) =
  | POk : value: opt v -> jval_payload v
  | PErr : message: string -> jval_payload v

(* ───────────────────────────────────────────────────────────────────
   THE FOLD — `BoundedActions.run witness` (Phase 1896), one `match`
   over the five view shapes. It owns sequencing, the one store write,
   the reserved-namespace refusal (K5), D9's refusal of a declared result
   target, D7's handler-effect arm, and — since Phase 1967 — the halting
   guard. It never recurses into a leaf.

   Termination is structural on the view. `fold`, `fold_many` and
   `fold_repeat` are mutually recursive with a three-place lexicographic
   measure: the list inside `VSequence` and the arms of `VChoose` are
   strict subterms of the view, each element is a strict subterm of the
   list, and a repeat descends into its body (a strict subterm) with the
   remaining count as the last place — so a bounded iteration terminates
   by its bound, as D2 says it must, with no fuel the code does not have.
   ─────────────────────────────────────────────────────────────────── *)

let rec fold (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
             (w: witness a e v eff) (ar: handler_arm v eff p)
             (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Tot (bounded_outcome v eff & p) (decreases %[x; 0; 0]) =
  match x with

  // The one store mutation: write the state channel. The reserved
  // namespace is closed on this path too — the loop's whole premise is
  // that the tree is untrusted.
  | VAssign act state_key value value_from ->
    (if w.w_is_reserved state_key then
       refused node_id (w.w_describe act)
         (strcat "State key '"
           (strcat state_key
             (strcat "' is under the host-reserved '" (strcat w.w_reserved_prefix "' namespace")))) s
     else
       let payload : jval_payload v =
         match value_from with
         | OSome expr ->
           (match w.w_resolve s expr with
            | Resolved jv -> POk (OSome jv)
            | NotResolved -> POk ONone
            | Errored m -> PErr m)
         | ONone -> POk value
       in
       match payload with
       | POk (OSome jv) -> store_only (write s state_key jv)
       | POk ONone ->
         refused node_id (w.w_describe act) "valueFrom did not resolve to a value — no write performed" s
       | PErr m ->
         refused node_id (w.w_describe act)
           (strcat "valueFrom errored: " (strcat m " — no write performed")) s),
    pl

  // A call that ALSO declares where its answer should land is REFUSED
  // rather than honoured or quietly ignored: result-target ownership
  // sits with the handler (D9). Otherwise the placement's arm decides
  // what the call MEANS here; declining is the documented no-op.
  | VCall act endpoint declares_target ->
    if declares_target then
      (refused node_id (w.w_describe act)
         "the call declares a result target; a handler declares where its own results land" s,
       pl)
    else
      (match ar.answer node_id endpoint s pl with
       | ONone -> (declined node_id (w.w_describe act) s, pl)
       | OSome ans ->
         ({ o_store = ans.h_store; o_effects = ans.h_effects; o_diagnostics = ans.h_diagnostics;
            o_halted = false },
          ans.h_placement))

  // The halting guard (Phase 1967). The condition resolves against the
  // store at dispatch — the SAME arrow an `Assign`'s `from` resolves
  // through — and the fold decides: the boolean true holds and changes
  // nothing; any other value, an unresolved condition, and an errored one
  // halt, the last carrying the domain's own text as the reason, which is
  // how a typed refusal reaches the diagnostic. A guard writes nothing
  // and emits nothing, whichever way it goes.
  | VRequire act condition ->
    (match w.w_resolve s condition with
     | Resolved jv ->
       if w.w_is_true jv then store_only s
       else halted node_id (w.w_describe act) "the guard did not hold" s
     | NotResolved ->
       halted node_id (w.w_describe act) "the guard did not resolve to a value" s
     | Errored m -> halted node_id (w.w_describe act) m s),
    pl

  // A leaf is the domain's: lowered to at most one effect, refused with
  // a reason, or declined. The fold does not look inside it.
  | VLeaf act ->
    (match w.w_lower node_id act s with
     | Emit emitted -> { o_store = s; o_effects = [ emitted ]; o_diagnostics = []; o_halted = false }
     | Refuse reason -> refused node_id (w.w_describe act) reason s
     | Decline -> declined node_id (w.w_describe act) s),
    pl

  // SELECTION (Phase 1976). The entry condition resolves against the
  // store exactly as a guard's does, and picks the arm: the boolean true
  // takes `when_true`, any other value `when_false`; unresolved or errored
  // halts, as a guard would, before either arm runs. The arm runs as a
  // member of a sequence would (its halt is the branch's halt). Then the
  // EXIT assertion, when carried, is resolved against the store the arm
  // left: it must hold after the true arm and fail after the false arm —
  // the assertion that lets the inverse branch pick the arm to undo
  // (`reverse`) — and violated, unresolved or errored it halts AFTER the
  // arm, keeping what the arm did, with the failure named.
  | VChoose act entry when_true when_false exit ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       let took_true = w.w_is_true jv in
       let (o1, p1) =
         if took_true then fold w ar node_id when_true s pl
         else fold w ar node_id when_false s pl
       in
       if o1.o_halted then (o1, p1)
       else
         (match exit with
          | ONone -> (o1, p1)
          | OSome assertion ->
            (match w.w_resolve o1.o_store assertion with
             | Resolved jv' ->
               if w.w_is_true jv' = took_true then (o1, p1)
               else
                 (halted_after o1 node_id (w.w_describe act)
                    (if took_true then "the exit assertion did not hold after the true arm"
                     else "the exit assertion held after the false arm"),
                  p1)
             | NotResolved ->
               (halted_after o1 node_id (w.w_describe act) "the exit assertion did not resolve to a value", p1)
             | Errored m ->
               (halted_after o1 node_id (w.w_describe act) (strcat "the exit assertion errored: " m), p1)))
     | NotResolved ->
       (halted node_id (w.w_describe act) "the branch condition did not resolve to a value" s, pl)
     | Errored m -> (halted node_id (w.w_describe act) m s, pl))

  // BOUNDED ITERATION (Phase 1976). A literal bound runs the body that
  // many times; a parameter bound is resolved against the store ONCE, at
  // entry, read as a count, and checked against its declared range — an
  // over-bound repeat halts here, before the first iteration, which is
  // what lets the budget price it at the range's top without the store.
  // The body sees no index.
  | VRepeat act count body ->
    (match count with
     | BLiteral n -> fold_repeat w ar node_id body n s pl
     | BParameter expr lo hi ->
       (match w.w_resolve s expr with
        | Resolved jv ->
          (match w.w_as_count jv with
           | OSome n ->
             if lo <= n && n <= hi then fold_repeat w ar node_id body n s pl
             else (halted node_id (w.w_describe act) "the repeat's bound is outside its declared range" s, pl)
           | ONone -> (halted node_id (w.w_describe act) "the repeat's bound did not resolve to a count" s, pl))
        | NotResolved ->
          (halted node_id (w.w_describe act) "the repeat's bound did not resolve to a value" s, pl)
        | Errored m -> (halted node_id (w.w_describe act) m s, pl)))

  // Compose: fold in order, threading the store AND the placement's
  // accumulation, concatenating effects and diagnostics — and stopping
  // at the first member that halts.
  | VSequence _ ops -> fold_many w ar node_id ops s pl

  // PER-ELEMENT ITERATION (Phase 1990). The elements are the body, lowered
  // once per element by the witness; what runs is their sequence, exactly
  // as `VSequence` runs its members — `each_is_lowering` is the equation.
  | VEach _ elements -> fold_many w ar node_id elements s pl

  // PER-ELEMENT ITERATION over a collection the STORE holds (Phase 1991).
  // The source is resolved against the store ONCE, at entry, read as a
  // collection, and its extent checked against the declared ceiling — an
  // extent over it halts here, before the first element, which is what
  // lets the budget price it at the ceiling without the store. Past the
  // check the elements run exactly as `VEach`'s do.
  | VEachOf act source ceiling _ elements ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs ->
          if length xs <= ceiling then fold_many w ar node_id elements s pl
          else (halted node_id (w.w_describe act) "the collection's extent is over its declared ceiling" s, pl)
        | ONone -> (halted node_id (w.w_describe act) "the collection did not resolve to a list" s, pl))
     | NotResolved -> (halted node_id (w.w_describe act) "the collection did not resolve to a value" s, pl)
     | Errored m -> (halted node_id (w.w_describe act) m s, pl))

and fold_many (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
              (w: witness a e v eff) (ar: handler_arm v eff p)
              (node_id: string) (ops: list (action_view a e v)) (s: store v) (pl: p)
  : Tot (bounded_outcome v eff & p) (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> store_only s, pl
  | x :: rest ->
    let (o1, p1) = fold w ar node_id x s pl in
    if o1.o_halted then (o1, p1)
    else
      let (o2, p2) = fold_many w ar node_id rest o1.o_store p1 in
      { o_store = o2.o_store;
        o_effects = app o1.o_effects o2.o_effects;
        o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
        o_halted = o2.o_halted },
      p2

/// The body of a repeat, `n` times, composed exactly as a sequence
/// composes its members (the store threaded, the lists concatenated, the
/// first halt the whole answer). `repeat_is_unrolling` says so as an
/// equation: this IS `fold_many` over `n` copies of the body, written
/// over the count so that termination is the bound.
and fold_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                (w: witness a e v eff) (ar: handler_arm v eff p)
                (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p)
  : Tot (bounded_outcome v eff & p) (decreases %[body; 2; n]) =
  if n = 0 then (store_only s, pl)
  else
    let (o1, p1) = fold w ar node_id body s pl in
    if o1.o_halted then (o1, p1)
    else
      let (o2, p2) = fold_repeat w ar node_id body (n - 1) o1.o_store p1 in
      { o_store = o2.o_store;
        o_effects = app o1.o_effects o2.o_effects;
        o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
        o_halted = o2.o_halted },
      p2

/// The action-level entry the generic core exposes: view, then fold.
/// F#: `BoundedActions.run witness arm nodeId action store placement`.
let run_action (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
               (w: witness a e v eff) (ar: handler_arm v eff p)
               (node_id: string) (act: a) (s: store v) (pl: p)
  : bounded_outcome v eff & p =
  fold w ar node_id (w.w_view act) s pl

(* ───────────────────────────────────────────────────────────────────
   THE GENERIC THEOREMS

   Everything from here to the UI witness is ghost: the predicates carry
   `noextract_to "FSharp"` and the lemmas are erased by the extractor,
   so the oracle the differential host runs is exactly the definitions
   above.
   ─────────────────────────────────────────────────────────────────── *)

// ─── 1. Totality over the view ───────────────────────────────────────

/// Every view shape, named once more with NO wildcard. Its value is
/// uninteresting; its shape is the point — a fifth shape added to
/// `action_view` fails to compile HERE exactly as it fails to compile in
/// `fold`. (K2: control structure is sequence + assign + call, and
/// everything else is a leaf.)
[@@ noextract_to "FSharp"]
let handled_view (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v) : bool =
  match x with
  | VSequence _ _ -> true
  | VAssign _ _ _ _ -> true
  | VCall _ _ _ -> true
  | VRequire _ _ -> true
  | VChoose _ _ _ _ _ -> true
  | VRepeat _ _ _ -> true
  | VEach _ _ -> true
  | VEachOf _ _ _ _ _ -> true
  | VLeaf _ -> true

/// The COMPOSITION shapes (Phase 1976; Phase 1990): the four whose step
/// is their members' steps, and which the structural characterisation
/// below therefore does not speak for — a sequence, a selection, a
/// repeat, a per-element iteration.
[@@ noextract_to "FSharp"]
let composition (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v) : bool =
  VSequence? x || VChoose? x || VRepeat? x || VEach? x || VEachOf? x

[@@ noextract_to "FSharp"]
let at_most_one (#a: Type0) (l: list a) : bool =
  match l with
  | [] -> true
  | [_] -> true
  | _ -> false

/// The placement ANSWERED this call — the one shape whose outcome is
/// the placement's rather than the fold's, and therefore the one the
/// structural characterisation below cannot speak for. Naming it is how
/// the seam stays visible instead of being quietly assumed away.
[@@ noextract_to "FSharp"]
let answered_view (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                  (ar: handler_arm v eff p) (node_id: string) (x: action_view a e v)
                  (s: store v) (pl: p) : bool =
  match x with
  | VCall _ endpoint false -> OSome? (ar.answer node_id endpoint s pl)
  | _ -> false

/// **`fold_total`.** The fold is defined on every shape of the view —
/// delivered by the `Tot` effect and the `decreases` clause on `fold`,
/// and by `handled_view` naming every constructor without a wildcard —
/// and one step that is neither a COMPOSITION shape (a sequence, a
/// selection, a repeat: Phase 1976 widened the exclusion from the one
/// shape to the three, since a branch's step is its arm's and a repeat's
/// is its body's) nor a call the placement answered is characterised
/// structurally: the placement is untouched, the store is either
/// unchanged or written at exactly one key the reserved predicate
/// rejects, at most one effect and at most one diagnostic are emitted,
/// and ONLY A GUARD HALTS among them. For a leaf that is K3 made a
/// theorem: whatever `w_lower` answers, this is the most it can do. For a
/// guard it is the Phase-1967 clause: it writes nothing, emits nothing,
/// and is the one non-composition shape whose step can halt. (A branch
/// and a repeat halt too — on a condition that cannot be decided, a
/// violated exit assertion, an over-bound count — and
/// `fold_no_halting_shape_no_halt` names all three.)
let fold_total (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
               (w: witness a e v eff) (ar: handler_arm v eff p)
               (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Lemma
      (requires (not (composition x)) /\ (not (answered_view ar node_id x s pl)))
      (ensures
        (handled_view x /\
         (let (o, pl') = fold w ar node_id x s pl in
          pl' == pl /\
          at_most_one o.o_effects /\
          at_most_one o.o_diagnostics /\
          (o.o_halted ==> VRequire? x) /\
          (VRequire? x ==> (o.o_store == s /\ o.o_effects == [])) /\
          (o.o_store == s \/
           (VAssign? x /\
            (exists (jv: v). o.o_store == write s (VAssign?.state_key x) jv) /\
            not (w.w_is_reserved (VAssign?.state_key x))))))) =
  match x with
  | VAssign _ state_key value value_from ->
    if w.w_is_reserved state_key then ()
    else
      (match value_from with
       | OSome expr ->
         (match w.w_resolve s expr with
          | Resolved _ -> ()
          | _ -> ())
       | ONone -> (match value with | OSome _ -> () | ONone -> ()))
  | VCall _ endpoint declares_target ->
    if declares_target then ()
    else
      (match ar.answer node_id endpoint s pl with
       | ONone -> ()
       | OSome _ -> ())
  | VRequire _ condition ->
    (match w.w_resolve s condition with
     | Resolved jv -> if w.w_is_true jv then () else ()
     | NotResolved -> ()
     | Errored _ -> ())
  | VLeaf act ->
    (match w.w_lower node_id act s with
     | Emit _ -> ()
     | Refuse _ -> ()
     | Decline -> ())
  | VSequence _ _ -> ()
  | VChoose _ _ _ _ _ -> ()
  | VRepeat _ _ _ -> ()
  | VEach _ _ -> ()
  | VEachOf _ _ _ _ _ -> ()

// ─── 2. The fold is blind to everything but the view ─────────────────

/// Two views have the SAME SHAPE under a witness: the same constructors
/// at every node, the same keys, values, expressions, endpoints and
/// target flags, the carried actions describing alike, and — at a leaf
/// — lowering alike at every node id and store. Nothing is required of
/// the carried actions beyond that: this is what "the fold reads an
/// action only through the witness" means, as a relation.
[@@ noextract_to "FSharp"]
let rec same_shape (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0)
                   (w: witness a e v eff) (x: action_view a e v) (y: action_view a e v)
  : Tot prop (decreases %[x; 0]) =
  match x, y with
  | VSequence _ xs, VSequence _ ys -> same_shape_list w xs ys
  | VAssign ax k1 v1 f1, VAssign ay k2 v2 f2 ->
    w.w_describe ax == w.w_describe ay /\ k1 == k2 /\ v1 == v2 /\ f1 == f2
  | VCall ax e1 t1, VCall ay e2 t2 ->
    w.w_describe ax == w.w_describe ay /\ e1 == e2 /\ t1 == t2
  | VRequire ax c1, VRequire ay c2 ->
    w.w_describe ax == w.w_describe ay /\ c1 == c2
  | VChoose ax e1 t1 f1 x1, VChoose ay e2 t2 f2 x2 ->
    w.w_describe ax == w.w_describe ay /\ e1 == e2 /\ x1 == x2 /\
    same_shape w t1 t2 /\ same_shape w f1 f2
  | VRepeat ax c1 b1, VRepeat ay c2 b2 ->
    w.w_describe ax == w.w_describe ay /\ c1 == c2 /\ same_shape w b1 b2
  | VEach ax e1, VEach ay e2 ->
    w.w_describe ax == w.w_describe ay /\ same_shape_list w e1 e2
  | VEachOf ax s1 c1 _ e1, VEachOf ay s2 c2 _ e2 ->
    w.w_describe ax == w.w_describe ay /\ s1 == s2 /\ c1 == c2 /\ same_shape_list w e1 e2
  | VLeaf ax, VLeaf ay ->
    w.w_describe ax == w.w_describe ay /\
    (forall (n: string) (st: store v). w.w_lower n ax st == w.w_lower n ay st)
  | _, _ -> False

and same_shape_list (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0)
                    (w: witness a e v eff)
                    (xs: list (action_view a e v)) (ys: list (action_view a e v))
  : Tot prop (decreases %[xs; 1]) =
  match xs, ys with
  | [], [] -> True
  | x1 :: r1, x2 :: r2 -> same_shape w x1 x2 /\ same_shape_list w r1 r2
  | _, _ -> False

/// **`fold_blind` — the core's half of no-closure-invocation, held
/// UNCONDITIONALLY.** Two same-shaped views fold to IDENTICAL outcomes
/// and identical placements. The fold cannot depend on anything in an
/// action the witness did not surface, so it cannot have applied a
/// closure the witness did not — and in this model there is no closure
/// to apply: the view carries none, and `a` has no elimination form the
/// fold could reach for.
let rec fold_blind (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                   (w: witness a e v eff) (ar: handler_arm v eff p)
                   (node_id: string) (x: action_view a e v) (y: action_view a e v)
                   (s: store v) (pl: p)
  : Lemma (requires same_shape w x y)
          (ensures fold w ar node_id x s pl == fold w ar node_id y s pl)
          (decreases %[x; 0; 0]) =
  match x, y with
  | VSequence _ xs, VSequence _ ys -> fold_blind_list w ar node_id xs ys s pl
  | VLeaf ax, VLeaf ay ->
    assert (w.w_lower node_id ax s == w.w_lower node_id ay s)
  // A branch: the same condition picks the same arm, the arms fold alike
  // by induction, and the exit assertion is then resolved against equal
  // stores. The carried actions describe alike, so the halt's diagnostic
  // is equal too.
  | VChoose _ entry t1 f1 _, VChoose _ _ t2 f2 _ ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       if w.w_is_true jv then fold_blind w ar node_id t1 t2 s pl
       else fold_blind w ar node_id f1 f2 s pl
     | _ -> ())
  // A repeat: the same bound resolves to the same count, and the bodies
  // fold alike that many times.
  | VRepeat _ count b1, VRepeat _ _ b2 ->
    (match count with
     | BLiteral n -> fold_blind_repeat w ar node_id b1 b2 n s pl
     | BParameter expr lo hi ->
       (match w.w_resolve s expr with
        | Resolved jv ->
          (match w.w_as_count jv with
           | OSome n -> if lo <= n && n <= hi then fold_blind_repeat w ar node_id b1 b2 n s pl else ()
           | ONone -> ())
        | _ -> ()))
  // Per-element iteration: same-shaped elements fold alike, as a
  // sequence's members do.
  | VEach _ e1, VEach _ e2 -> fold_blind_list w ar node_id e1 e2 s pl
  // A store-bound iteration: the same source resolves to the same extent,
  // checked against the same ceiling, and the elements fold alike.
  | VEachOf _ source ceiling _ e1, VEachOf _ _ _ _ e2 ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs -> if length xs <= ceiling then fold_blind_list w ar node_id e1 e2 s pl else ()
        | ONone -> ())
     | _ -> ())
  | _, _ -> ()

and fold_blind_list (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                    (w: witness a e v eff) (ar: handler_arm v eff p)
                    (node_id: string)
                    (xs: list (action_view a e v)) (ys: list (action_view a e v))
                    (s: store v) (pl: p)
  : Lemma (requires same_shape_list w xs ys)
          (ensures fold_many w ar node_id xs s pl == fold_many w ar node_id ys s pl)
          (decreases %[xs; 1; 0]) =
  match xs, ys with
  | [], [] -> ()
  | x1 :: r1, x2 :: r2 ->
    fold_blind w ar node_id x1 x2 s pl;
    let (o1, p1) = fold w ar node_id x1 s pl in
    if o1.o_halted then () else fold_blind_list w ar node_id r1 r2 o1.o_store p1
  | _, _ -> ()

and fold_blind_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                      (w: witness a e v eff) (ar: handler_arm v eff p)
                      (node_id: string)
                      (b1: action_view a e v) (b2: action_view a e v) (n: nat)
                      (s: store v) (pl: p)
  : Lemma (requires same_shape w b1 b2)
          (ensures fold_repeat w ar node_id b1 n s pl == fold_repeat w ar node_id b2 n s pl)
          (decreases %[b1; 2; n]) =
  if n = 0 then ()
  else begin
    fold_blind w ar node_id b1 b2 s pl;
    let (o1, p1) = fold w ar node_id b1 s pl in
    if o1.o_halted then () else fold_blind_repeat w ar node_id b1 b2 (n - 1) o1.o_store p1
  end

/// **The witness obligation.** A witness is BLIND TO a relation on
/// actions when its `w_view` sends related actions to same-shaped views.
/// This is the half of no-closure-invocation that D18 places on the
/// witness: only a `View` or a `Lower` could reach a closure, so only a
/// `View` or a `Lower` could tell two actions apart by one — and this
/// says they do not.
[@@ noextract_to "FSharp"]
let blind_to (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0)
             (rel: a -> a -> prop) (w: witness a e v eff) : prop =
  forall (x: a) (y: a). rel x y ==> same_shape w (w.w_view x) (w.w_view y)

/// **`run_action_blind` — no-closure-invocation for the generic core,
/// CONDITIONAL on the witness obligation.** Under any relation the
/// witness is blind to, related actions run to identical outcomes and
/// placements. Instantiated at the UI witness and `same_but_closures`
/// below, where the obligation is discharged, it is Phase 1715's
/// `run_no_closure` again.
let run_action_blind (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                     (rel: a -> a -> prop)
                     (w: witness a e v eff) (ar: handler_arm v eff p)
                     (node_id: string) (x: a) (y: a) (s: store v) (pl: p)
  : Lemma (requires blind_to rel w /\ rel x y)
          (ensures run_action w ar node_id x s pl == run_action w ar node_id y s pl) =
  fold_blind w ar node_id (w.w_view x) (w.w_view y) s pl

// ─── 3. `Sequence` is the fold's homomorphism ────────────────────────

let rec app_assoc (#a: Type0) (xs: list a) (ys: list a) (zs: list a)
  : Lemma (ensures app (app xs ys) zs == app xs (app ys zs)) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: rest -> app_assoc rest ys zs

let rec app_nil (#a: Type0) (xs: list a)
  : Lemma (ensures app xs [] == xs) (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: rest -> app_nil rest

/// The composition of two outcomes, as the `Sequence` arm composes them:
/// the second's store and halt, the effects and the diagnostics
/// concatenated in order. Named so the homomorphism can be stated once
/// and read at both levels.
[@@ noextract_to "FSharp"]
let composed (#v: Type0) (#eff: Type0) (o1: bounded_outcome v eff) (o2: bounded_outcome v eff)
  : bounded_outcome v eff =
  { o_store = o2.o_store;
    o_effects = app o1.o_effects o2.o_effects;
    o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
    o_halted = o2.o_halted }

/// **`sequence_homomorphism`.** Running the concatenation of two
/// operation lists is running the first and — IF IT DID NOT HALT — then
/// the second against the store the first left, with the effects and the
/// diagnostics concatenated in order and the placement threaded through;
/// a first half that halted is the whole answer, and the second half
/// never runs. This is DECISIONS.md D7's splice property — a nested call
/// sees the writes before it and is seen by the writes after it — stated
/// as an equation, and it is what makes `Sequence` a composition rather
/// than a special case. The halting clause is Phase 1967's: a guard is a
/// control structure, so it is the evaluator's, and this is the one law
/// it changes. At a witness with no guard the clause is vacuous
/// (`fold_no_require_no_halt`), which is how the UI tier's
/// `chain_homomorphism` keeps its unconditional form.
let rec sequence_homomorphism (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                              (w: witness a e v eff) (ar: handler_arm v eff p)
                              (node_id: string)
                              (xs: list (action_view a e v)) (ys: list (action_view a e v))
                              (s: store v) (pl: p)
  : Lemma
      (ensures
        (let (o1, p1) = fold_many w ar node_id xs s pl in
         if o1.o_halted then fold_many w ar node_id (app xs ys) s pl == (o1, p1)
         else
           (let (o2, p2) = fold_many w ar node_id ys o1.o_store p1 in
            fold_many w ar node_id (app xs ys) s pl == (composed o1 o2, p2))))
      (decreases xs) =
  match xs with
  | [] ->
    let (o2, _) = fold_many w ar node_id ys s pl in
    app_nil o2.o_effects;
    app_nil o2.o_diagnostics
  | x :: rest ->
    let (ox, px) = fold w ar node_id x s pl in
    if ox.o_halted then ()
    else begin
      sequence_homomorphism w ar node_id rest ys ox.o_store px;
      let (o1r, p1r) = fold_many w ar node_id rest ox.o_store px in
      if o1r.o_halted then ()
      else begin
        let (o2, _) = fold_many w ar node_id ys o1r.o_store p1r in
        app_assoc ox.o_effects o1r.o_effects o2.o_effects;
        app_assoc ox.o_diagnostics o1r.o_diagnostics o2.o_diagnostics
      end
    end

/// The same statement at the view level, which is the form the
/// `Sequence` arm is read in. The carried actions are irrelevant to it —
/// the fold reads none of them in this arm — so any three will do.
let sequence_action_homomorphism (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                 (w: witness a e v eff) (ar: handler_arm v eff p)
                                 (node_id: string) (act: a) (act1: a) (act2: a)
                                 (xs: list (action_view a e v)) (ys: list (action_view a e v))
                                 (s: store v) (pl: p)
  : Lemma
      (ensures
        (let (o1, p1) = fold w ar node_id (VSequence act1 xs) s pl in
         if o1.o_halted then fold w ar node_id (VSequence act (app xs ys)) s pl == (o1, p1)
         else
           (let (o2, p2) = fold w ar node_id (VSequence act2 ys) o1.o_store p1 in
            fold w ar node_id (VSequence act (app xs ys)) s pl == (composed o1 o2, p2)))) =
  sequence_homomorphism w ar node_id xs ys s pl

// ─── 4. Reserved keys are untouched ──────────────────────────────────

/// The placement's arm preserves reserved keys. This is an ASSUMPTION
/// about the seam, not a claim about it: the arm returns a store of its
/// own, so the fold cannot bound what the placement wrote.
/// `inert_preserves_reserved` below discharges it for the placements
/// that run no handlers, which is why the theorem is not vacuous.
[@@ noextract_to "FSharp"]
let arm_preserves_reserved (#v: Type0) (#eff: Type0) (#p: Type0)
                           (is_reserved: key -> bool) (ar: handler_arm v eff p) : prop =
  forall (node_id: string) (endpoint: string) (s: store v) (pl: p).
    (match ar.answer node_id endpoint s pl with
     | ONone -> True
     | OSome ans ->
       (forall (kk: key). is_reserved kk ==> lookup ans.h_store kk == lookup s kk))

let rec write_preserves_other (#v: Type0) (s: store v) (k1: key) (x: v) (k2: key)
  : Lemma (requires ~(k1 == k2))
          (ensures lookup (write s k1 x) k2 == lookup s k2)
          (decreases s) =
  match s with
  | [] -> ()
  | (k', _) :: rest -> if k' = k1 then () else write_preserves_other rest k1 x k2

/// **`fold_reserved_untouched`.** The store the fold returns agrees with
/// the store it was given at every reserved key. The only write the fold
/// performs is the `Assign` shape's, and that shape refuses a reserved
/// key before reaching it; a leaf has no store to return (K3) — so the
/// output differs from the input only at non-reserved keys, which is the
/// property multi-tenant hosting of an untrusted tree rests on.
let rec fold_reserved_untouched (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                (w: witness a e v eff) (ar: handler_arm v eff p)
                                (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
                                (kk: key)
  : Lemma (requires arm_preserves_reserved w.w_is_reserved ar /\ w.w_is_reserved kk)
          (ensures lookup (fst (fold w ar node_id x s pl)).o_store kk == lookup s kk)
          (decreases %[x; 0; 0]) =
  match x with
  | VAssign _ state_key value value_from ->
    if w.w_is_reserved state_key then ()
    else
      (match value_from with
       | OSome expr ->
         (match w.w_resolve s expr with
          | Resolved jv -> write_preserves_other s state_key jv kk
          | _ -> ())
       | ONone ->
         (match value with
          | OSome jv -> write_preserves_other s state_key jv kk
          | ONone -> ()))
  | VSequence _ ops -> fold_reserved_untouched_list w ar node_id ops s pl kk
  | VCall _ _ _ -> ()
  | VRequire _ condition ->
    (match w.w_resolve s condition with
     | Resolved jv -> if w.w_is_true jv then () else ()
     | NotResolved -> ()
     | Errored _ -> ())
  // A branch writes only through the arm it took; the exit assertion
  // reads the arm's store and never writes it (`halted_after` keeps it).
  | VChoose _ entry when_true when_false _ ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       if w.w_is_true jv then fold_reserved_untouched w ar node_id when_true s pl kk
       else fold_reserved_untouched w ar node_id when_false s pl kk
     | _ -> ())
  // A repeat writes only through its body, however many times.
  | VRepeat _ count body ->
    (match count with
     | BLiteral n -> fold_reserved_untouched_repeat w ar node_id body n s pl kk
     | BParameter expr lo hi ->
       (match w.w_resolve s expr with
        | Resolved jv ->
          (match w.w_as_count jv with
           | OSome n ->
             if lo <= n && n <= hi then fold_reserved_untouched_repeat w ar node_id body n s pl kk else ()
           | ONone -> ())
        | _ -> ()))
  // A per-element iteration writes only through its elements, in sequence.
  | VEach _ elements -> fold_reserved_untouched_list w ar node_id elements s pl kk
  // A store-bound iteration reads the store and writes only through its
  // elements, once the extent has passed the ceiling.
  | VEachOf _ source ceiling _ elements ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs -> if length xs <= ceiling then fold_reserved_untouched_list w ar node_id elements s pl kk else ()
        | ONone -> ())
     | _ -> ())
  | VLeaf act ->
    (match w.w_lower node_id act s with
     | Emit _ -> ()
     | Refuse _ -> ()
     | Decline -> ())

and fold_reserved_untouched_list (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                 (w: witness a e v eff) (ar: handler_arm v eff p)
                                 (node_id: string) (ops: list (action_view a e v))
                                 (s: store v) (pl: p) (kk: key)
  : Lemma (requires arm_preserves_reserved w.w_is_reserved ar /\ w.w_is_reserved kk)
          (ensures lookup (fst (fold_many w ar node_id ops s pl)).o_store kk == lookup s kk)
          (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> ()
  | x :: rest ->
    fold_reserved_untouched w ar node_id x s pl kk;
    let (o1, p1) = fold w ar node_id x s pl in
    if o1.o_halted then () else fold_reserved_untouched_list w ar node_id rest o1.o_store p1 kk

and fold_reserved_untouched_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                   (w: witness a e v eff) (ar: handler_arm v eff p)
                                   (node_id: string) (body: action_view a e v) (n: nat)
                                   (s: store v) (pl: p) (kk: key)
  : Lemma (requires arm_preserves_reserved w.w_is_reserved ar /\ w.w_is_reserved kk)
          (ensures lookup (fst (fold_repeat w ar node_id body n s pl)).o_store kk == lookup s kk)
          (decreases %[body; 2; n]) =
  if n = 0 then ()
  else begin
    fold_reserved_untouched w ar node_id body s pl kk;
    let (o1, p1) = fold w ar node_id body s pl in
    if o1.o_halted then () else fold_reserved_untouched_repeat w ar node_id body (n - 1) o1.o_store p1 kk
  end

/// The inert arm preserves reserved keys under EVERY predicate — it
/// declines every call, so there is no store for it to have written.
/// This is what makes `fold_reserved_untouched` a statement about the
/// placements that run no handlers rather than a conditional nobody has
/// discharged.
let inert_preserves_reserved (#v: Type0) (#eff: Type0) (#p: Type0) (is_reserved: key -> bool)
  : Lemma (arm_preserves_reserved is_reserved (inert_arm #v #eff #p)) = ()

// ─── 5. Which shapes halt (Phase 1967; widened by Phase 1976) ────────

/// A view holds a guard somewhere: at its root, or inside a sequence, an
/// arm or a body at any depth.
[@@ noextract_to "FSharp"]
let rec has_require (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v)
  : Tot bool (decreases %[x; 0]) =
  match x with
  | VSequence _ ops -> has_require_list ops
  | VRequire _ _ -> true
  | VChoose _ _ when_true when_false _ -> has_require when_true || has_require when_false
  | VRepeat _ _ body -> has_require body
  | VEach _ elements -> has_require_list elements
  | VEachOf _ _ _ _ elements -> has_require_list elements
  | VAssign _ _ _ _ -> false
  | VCall _ _ _ -> false
  | VLeaf _ -> false

and has_require_list (#a: Type0) (#e: Type0) (#v: Type0) (ops: list (action_view a e v))
  : Tot bool (decreases %[ops; 1]) =
  match ops with
  | [] -> false
  | x :: rest -> has_require x || has_require_list rest

/// A view holds one of Phase 1976's shapes somewhere — a selection or a
/// repeat, at any depth. The syntactic fact "a view without the new
/// shapes folds exactly as before" keys on: `ui_view_no_flow` discharges
/// it for the UI witness, which views nothing as either. A per-element
/// iteration (Phase 1990) is NOT itself a flow shape here: its collection
/// is literal and it has no condition, so it cannot halt of itself and
/// holds a flow shape only through its elements — which is what lets
/// `fold_no_halting_shape_no_halt` say an `Each` over a guard-free,
/// flow-free body never halts.
[@@ noextract_to "FSharp"]
let rec has_flow (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v)
  : Tot bool (decreases %[x; 0]) =
  match x with
  | VSequence _ ops -> has_flow_list ops
  | VChoose _ _ _ _ _ -> true
  | VRepeat _ _ _ -> true
  | VEach _ elements -> has_flow_list elements
  // A store-bound iteration (Phase 1991) IS a flow shape: its extent is
  // read from the store and checked against a ceiling, and it halts when
  // the read fails or the extent is over it.
  | VEachOf _ _ _ _ _ -> true
  | VRequire _ _ -> false
  | VAssign _ _ _ _ -> false
  | VCall _ _ _ -> false
  | VLeaf _ -> false

and has_flow_list (#a: Type0) (#e: Type0) (#v: Type0) (ops: list (action_view a e v))
  : Tot bool (decreases %[ops; 1]) =
  match ops with
  | [] -> false
  | x :: rest -> has_flow x || has_flow_list rest

/// The shapes whose step can halt: a guard that does not hold; a
/// selection whose condition cannot be decided or whose exit assertion is
/// violated; a repeat whose bound cannot be read or is over its range.
[@@ noextract_to "FSharp"]
let may_halt (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v) : bool =
  has_require x || has_flow x

[@@ noextract_to "FSharp"]
let may_halt_list (#a: Type0) (#e: Type0) (#v: Type0) (ops: list (action_view a e v)) : bool =
  has_require_list ops || has_flow_list ops

/// **`fold_no_halting_shape_no_halt`.** A view with no guard, no selection
/// and no repeat in it never halts, under every witness and every
/// placement arm — so the halting clause of `sequence_homomorphism` is
/// vacuous for it, and a domain whose `View` produces none of the three
/// (the UI tier: `ui_view_no_require`, `ui_view_no_flow`) inherits every
/// pre-1967 statement unchanged. An answered call never halts either:
/// `handler_answer` carries no halt, by its type, because a handler is its
/// own atomicity unit (D8) and a handler that failed rolled ITSELF back
/// and the fold carries on. Phase 1976 widened this from the guard alone
/// (`fold_no_require_no_halt`, kept below as the corollary it now is) to
/// the three halting shapes.
let rec fold_no_halting_shape_no_halt (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                      (w: witness a e v eff) (ar: handler_arm v eff p)
                                      (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Lemma (requires not (may_halt x))
          (ensures not (fst (fold w ar node_id x s pl)).o_halted)
          (decreases %[x; 0; 0]) =
  match x with
  | VAssign _ state_key value value_from ->
    if w.w_is_reserved state_key then ()
    else
      (match value_from with
       | OSome expr ->
         (match w.w_resolve s expr with
          | Resolved _ -> ()
          | _ -> ())
       | ONone -> (match value with | OSome _ -> () | ONone -> ()))
  | VCall _ endpoint declares_target ->
    if declares_target then ()
    else
      (match ar.answer node_id endpoint s pl with
       | ONone -> ()
       | OSome _ -> ())
  | VLeaf act ->
    (match w.w_lower node_id act s with
     | Emit _ -> ()
     | Refuse _ -> ()
     | Decline -> ())
  | VSequence _ ops -> fold_no_halting_shape_no_halt_list w ar node_id ops s pl
  | VEach _ elements -> fold_no_halting_shape_no_halt_list w ar node_id elements s pl
  | VRequire _ _ -> ()
  | VChoose _ _ _ _ _ -> ()
  | VRepeat _ _ _ -> ()
  | VEachOf _ _ _ _ _ -> ()

and fold_no_halting_shape_no_halt_list (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                       (w: witness a e v eff) (ar: handler_arm v eff p)
                                       (node_id: string) (ops: list (action_view a e v))
                                       (s: store v) (pl: p)
  : Lemma (requires not (may_halt_list ops))
          (ensures not (fst (fold_many w ar node_id ops s pl)).o_halted)
          (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> ()
  | x :: rest ->
    fold_no_halting_shape_no_halt w ar node_id x s pl;
    let (o1, p1) = fold w ar node_id x s pl in
    fold_no_halting_shape_no_halt_list w ar node_id rest o1.o_store p1

/// **`fold_no_require_no_halt`** — Phase 1967's statement, now a corollary
/// for a view WITHOUT the Phase-1976 shapes: with no guard it never halts.
/// The extra hypothesis is what Phase 1976 changed: over the widened view
/// a guard is no longer the only shape that halts, so the 1967 statement
/// as written is false there, and this is the form that stays true — at
/// any view without a selection or a repeat, which is every view the UI
/// witness produces (`ui_view_no_flow`), it is exactly the old theorem.
let fold_no_require_no_halt (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                            (w: witness a e v eff) (ar: handler_arm v eff p)
                            (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Lemma (requires not (has_require x) /\ not (has_flow x))
          (ensures not (fst (fold w ar node_id x s pl)).o_halted) =
  fold_no_halting_shape_no_halt w ar node_id x s pl

// ─── 6. The reversible fragment (Phase 1976) ─────────────────────────

(* ───────────────────────────────────────────────────────────────────
   Selection and iteration are where a reversible language needs
   structure a forward-only one does not, so the two shapes were designed
   for reversal from the start (the Janus model, Lutz and Yokoyama): a
   branch carries an EXIT assertion that picks the arm to undo, and a
   repeat's bound is a function of the tree alone. `Assign` is not
   reversible by construction — it destroys the old value — so it joins
   the fragment BY TRACE: a reversible run records, at each write, the
   value it overwrote (the Bennett embedding), and the inverse program
   restores it with an ordinary `Assign`. Nothing is recorded by a run
   that is not asked to trace (`fold` carries no trace, `traced_agrees`
   says the two runs agree), so a program that never reverses pays
   nothing.

   The fragment is SYNTACTIC — `reversible` reads the tree and nothing
   else — and it is: sequence, assign, the guard, a branch WITH an exit
   assertion, a repeat with a LITERAL bound. A call and a leaf are effects
   and are 1977's boundary; a branch without an exit assertion has nothing
   to say which arm to undo; a parameter bound is read from the store the
   body may have overwritten. The one thing the fragment cannot decide
   from the tree is whether every key a run assigned was PRESENT before
   it: a key that was absent cannot be restored by an assignment (the
   store has no delete, K4), so the trace says `TWrote ONone` and
   `restorable` refuses it. That is a property of the run, carried by
   the trace, and `reverse_run` is conditional on it — named as such in
   the ladder.
   ─────────────────────────────────────────────────────────────────── *)

/// The trace of one run, mirroring the view: one leaf entry per
/// non-composition step, `TWrote old` for an assignment that wrote (with
/// the value it overwrote, `ONone` if the key was absent), `TNothing` for
/// every other step and for an assignment that was refused; the members
/// that RAN of a sequence, a repeat or a per-element iteration (a halted
/// prefix is shorter); and which arm a branch took. F#: `Trace`.
type trace (v: Type0) =
  | TNothing : trace v
  | TWrote : old: opt v -> trace v
  | TSeq : steps: list (trace v) -> trace v
  | TChoose : took_true: bool -> arm: trace v -> trace v
  | TRepeat : iterations: list (trace v) -> trace v
  /// The elements that RAN of an `Each` (Phase 1990): the lowered form's
  /// steps, kept under their own constructor so a reader of the trace
  /// sees how many elements ran, and inverted as a sequence is.
  | TEach : elements: list (trace v) -> trace v
  /// A store-bound `Each`'s run (Phase 1991): the extent the read ANSWERED
  /// at entry, beside the steps of the elements that ran. The inverse reads
  /// this record and never the live store.
  | TEachOf : extent: list v -> elements: list (trace v) -> trace v

/// The fold, recording its trace. Arm for arm the same as `fold`
/// (`traced_agrees` proves the outcome and the placement equal), plus the
/// third result. F#: `BoundedActions.runTraced`'s fold.
let rec fold_traced (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                    (w: witness a e v eff) (ar: handler_arm v eff p)
                    (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Tot (bounded_outcome v eff & p & trace v) (decreases %[x; 0; 0]) =
  match x with
  | VAssign act state_key value value_from ->
    if w.w_is_reserved state_key then
      (refused node_id (w.w_describe act)
         (strcat "State key '"
           (strcat state_key
             (strcat "' is under the host-reserved '" (strcat w.w_reserved_prefix "' namespace")))) s,
       pl, TNothing)
    else
      let payload : jval_payload v =
        match value_from with
        | OSome expr ->
          (match w.w_resolve s expr with
           | Resolved jv -> POk (OSome jv)
           | NotResolved -> POk ONone
           | Errored m -> PErr m)
        | ONone -> POk value
      in
      (match payload with
       | POk (OSome jv) -> (store_only (write s state_key jv), pl, TWrote (lookup s state_key))
       | POk ONone ->
         (refused node_id (w.w_describe act) "valueFrom did not resolve to a value — no write performed" s,
          pl, TNothing)
       | PErr m ->
         (refused node_id (w.w_describe act)
            (strcat "valueFrom errored: " (strcat m " — no write performed")) s,
          pl, TNothing))

  | VCall act endpoint declares_target ->
    if declares_target then
      (refused node_id (w.w_describe act)
         "the call declares a result target; a handler declares where its own results land" s,
       pl, TNothing)
    else
      (match ar.answer node_id endpoint s pl with
       | ONone -> (declined node_id (w.w_describe act) s, pl, TNothing)
       | OSome ans ->
         ({ o_store = ans.h_store; o_effects = ans.h_effects; o_diagnostics = ans.h_diagnostics;
            o_halted = false },
          ans.h_placement, TNothing))

  | VRequire act condition ->
    ((match w.w_resolve s condition with
      | Resolved jv ->
        if w.w_is_true jv then store_only s
        else halted node_id (w.w_describe act) "the guard did not hold" s
      | NotResolved ->
        halted node_id (w.w_describe act) "the guard did not resolve to a value" s
      | Errored m -> halted node_id (w.w_describe act) m s),
     pl, TNothing)

  | VLeaf act ->
    ((match w.w_lower node_id act s with
      | Emit emitted -> { o_store = s; o_effects = [ emitted ]; o_diagnostics = []; o_halted = false }
      | Refuse reason -> refused node_id (w.w_describe act) reason s
      | Decline -> declined node_id (w.w_describe act) s),
     pl, TNothing)

  | VChoose act entry when_true when_false exit ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       let took_true = w.w_is_true jv in
       let (o1, p1, arm) =
         if took_true then fold_traced w ar node_id when_true s pl
         else fold_traced w ar node_id when_false s pl
       in
       let tr = TChoose took_true arm in
       if o1.o_halted then (o1, p1, tr)
       else
         (match exit with
          | ONone -> (o1, p1, tr)
          | OSome assertion ->
            (match w.w_resolve o1.o_store assertion with
             | Resolved jv' ->
               if w.w_is_true jv' = took_true then (o1, p1, tr)
               else
                 (halted_after o1 node_id (w.w_describe act)
                    (if took_true then "the exit assertion did not hold after the true arm"
                     else "the exit assertion held after the false arm"),
                  p1, tr)
             | NotResolved ->
               (halted_after o1 node_id (w.w_describe act) "the exit assertion did not resolve to a value", p1, tr)
             | Errored m ->
               (halted_after o1 node_id (w.w_describe act) (strcat "the exit assertion errored: " m), p1, tr)))
     | NotResolved ->
       (halted node_id (w.w_describe act) "the branch condition did not resolve to a value" s, pl, TNothing)
     | Errored m -> (halted node_id (w.w_describe act) m s, pl, TNothing))

  | VRepeat act count body ->
    (match count with
     | BLiteral n ->
       let (o, p', its) = fold_traced_repeat w ar node_id body n s pl in (o, p', TRepeat its)
     | BParameter expr lo hi ->
       (match w.w_resolve s expr with
        | Resolved jv ->
          (match w.w_as_count jv with
           | OSome n ->
             if lo <= n && n <= hi then
               let (o, p', its) = fold_traced_repeat w ar node_id body n s pl in (o, p', TRepeat its)
             else
               (halted node_id (w.w_describe act) "the repeat's bound is outside its declared range" s, pl, TNothing)
           | ONone ->
             (halted node_id (w.w_describe act) "the repeat's bound did not resolve to a count" s, pl, TNothing))
        | NotResolved ->
          (halted node_id (w.w_describe act) "the repeat's bound did not resolve to a value" s, pl, TNothing)
        | Errored m -> (halted node_id (w.w_describe act) m s, pl, TNothing)))

  | VSequence _ ops ->
    let (o, p', steps) = fold_traced_many w ar node_id ops s pl in (o, p', TSeq steps)

  | VEach _ elements ->
    let (o, p', steps) = fold_traced_many w ar node_id elements s pl in (o, p', TEach steps)

  | VEachOf act source ceiling _ elements ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs ->
          if length xs <= ceiling then
            let (o, p', steps) = fold_traced_many w ar node_id elements s pl in (o, p', TEachOf xs steps)
          else
            (halted node_id (w.w_describe act) "the collection's extent is over its declared ceiling" s, pl, TNothing)
        | ONone ->
          (halted node_id (w.w_describe act) "the collection did not resolve to a list" s, pl, TNothing))
     | NotResolved ->
       (halted node_id (w.w_describe act) "the collection did not resolve to a value" s, pl, TNothing)
     | Errored m -> (halted node_id (w.w_describe act) m s, pl, TNothing))

and fold_traced_many (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                     (w: witness a e v eff) (ar: handler_arm v eff p)
                     (node_id: string) (ops: list (action_view a e v)) (s: store v) (pl: p)
  : Tot (bounded_outcome v eff & p & list (trace v)) (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> (store_only s, pl, [])
  | x :: rest ->
    let (o1, p1, t1) = fold_traced w ar node_id x s pl in
    if o1.o_halted then (o1, p1, [ t1 ])
    else
      let (o2, p2, ts) = fold_traced_many w ar node_id rest o1.o_store p1 in
      ({ o_store = o2.o_store;
         o_effects = app o1.o_effects o2.o_effects;
         o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
         o_halted = o2.o_halted },
       p2, t1 :: ts)

and fold_traced_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                       (w: witness a e v eff) (ar: handler_arm v eff p)
                       (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p)
  : Tot (bounded_outcome v eff & p & list (trace v)) (decreases %[body; 2; n]) =
  if n = 0 then (store_only s, pl, [])
  else
    let (o1, p1, t1) = fold_traced w ar node_id body s pl in
    if o1.o_halted then (o1, p1, [ t1 ])
    else
      let (o2, p2, ts) = fold_traced_repeat w ar node_id body (n - 1) o1.o_store p1 in
      ({ o_store = o2.o_store;
         o_effects = app o1.o_effects o2.o_effects;
         o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
         o_halted = o2.o_halted },
       p2, t1 :: ts)

/// The action-level traced entry. F#: `BoundedActions.runTraced`.
let run_action_traced (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                      (w: witness a e v eff) (ar: handler_arm v eff p)
                      (node_id: string) (act: a) (s: store v) (pl: p)
  : bounded_outcome v eff & p & trace v =
  fold_traced w ar node_id (w.w_view act) s pl

/// **`traced_agrees`.** The traced fold IS the fold, with a trace beside
/// it: the outcome and the placement are equal at every view, store and
/// placement. This is what "the forward run without reversal records
/// nothing" rests on — `fold` is the production path and carries no
/// trace; `fold_traced` is the reversible run and carries one; and
/// nothing a program does differs between them.
let rec traced_agrees (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                      (w: witness a e v eff) (ar: handler_arm v eff p)
                      (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Lemma (ensures (let (o, pl', _) = fold_traced w ar node_id x s pl in (o, pl') == fold w ar node_id x s pl))
          (decreases %[x; 0; 0]) =
  match x with
  | VSequence _ ops -> traced_agrees_many w ar node_id ops s pl
  | VEach _ elements -> traced_agrees_many w ar node_id elements s pl
  | VEachOf _ source ceiling _ elements ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs -> if length xs <= ceiling then traced_agrees_many w ar node_id elements s pl else ()
        | ONone -> ())
     | _ -> ())
  | VChoose _ entry when_true when_false _ ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       if w.w_is_true jv then traced_agrees w ar node_id when_true s pl
       else traced_agrees w ar node_id when_false s pl
     | _ -> ())
  | VRepeat _ count body ->
    (match count with
     | BLiteral n -> traced_agrees_repeat w ar node_id body n s pl
     | BParameter expr lo hi ->
       (match w.w_resolve s expr with
        | Resolved jv ->
          (match w.w_as_count jv with
           | OSome n -> if lo <= n && n <= hi then traced_agrees_repeat w ar node_id body n s pl else ()
           | ONone -> ())
        | _ -> ()))
  | VAssign _ _ _ _ -> ()
  | VCall _ _ _ -> ()
  | VRequire _ _ -> ()
  | VLeaf _ -> ()

and traced_agrees_many (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                       (w: witness a e v eff) (ar: handler_arm v eff p)
                       (node_id: string) (ops: list (action_view a e v)) (s: store v) (pl: p)
  : Lemma (ensures (let (o, pl', _) = fold_traced_many w ar node_id ops s pl in
                    (o, pl') == fold_many w ar node_id ops s pl))
          (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> ()
  | x :: rest ->
    traced_agrees w ar node_id x s pl;
    let (o1, p1) = fold w ar node_id x s pl in
    if o1.o_halted then () else traced_agrees_many w ar node_id rest o1.o_store p1

and traced_agrees_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                         (w: witness a e v eff) (ar: handler_arm v eff p)
                         (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p)
  : Lemma (ensures (let (o, pl', _) = fold_traced_repeat w ar node_id body n s pl in
                    (o, pl') == fold_repeat w ar node_id body n s pl))
          (decreases %[body; 2; n]) =
  if n = 0 then ()
  else begin
    traced_agrees w ar node_id body s pl;
    let (o1, p1) = fold w ar node_id body s pl in
    if o1.o_halted then () else traced_agrees_repeat w ar node_id body (n - 1) o1.o_store p1
  end

/// **The reversible fragment, decided from the tree alone.** Sequence,
/// assign, the guard, a branch WITH an exit assertion, a repeat with a
/// LITERAL bound; never a call or a leaf (effects: 1977's boundary). A
/// `bool`, not a `prop`: this is the classification the code ships
/// (`BoundedActions.reversible`), so it extracts.
let rec reversible (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v)
  : Tot bool (decreases %[x; 0]) =
  match x with
  | VSequence _ ops -> reversible_list ops
  | VAssign _ _ _ _ -> true
  | VRequire _ _ -> true
  | VChoose _ _ when_true when_false exit -> OSome? exit && reversible when_true && reversible when_false
  | VRepeat _ count body -> BLiteral? count && reversible body
  | VEach _ elements -> reversible_list elements
  // IN the fragment (Phase 1991, D36 item 3): the extent is recorded in
  // the trace, so the inverse reads the record and never re-reads the
  // store the body may have overwritten.
  | VEachOf _ _ _ _ elements -> reversible_list elements
  | VCall _ _ _ -> false
  | VLeaf _ -> false

and reversible_list (#a: Type0) (#e: Type0) (#v: Type0) (ops: list (action_view a e v))
  : Tot bool (decreases %[ops; 1]) =
  match ops with
  | [] -> true
  | x :: rest -> reversible x && reversible_list rest

/// A trace every write of which overwrote a PRESENT key, so every write
/// can be undone by an assignment. The run-dependent half of
/// reversibility, decided from the trace. F#: `Trace.restorable`.
let rec restorable (#v: Type0) (tr: trace v) : Tot bool (decreases %[tr; 0]) =
  match tr with
  | TNothing -> true
  | TWrote ONone -> false
  | TWrote (OSome _) -> true
  | TSeq steps -> restorable_list steps
  | TChoose _ arm -> restorable arm
  | TRepeat iterations -> restorable_list iterations
  | TEach elements -> restorable_list elements
  | TEachOf _ elements -> restorable_list elements

and restorable_list (#v: Type0) (steps: list (trace v)) : Tot bool (decreases %[steps; 1]) =
  match steps with
  | [] -> true
  | t :: rest -> restorable t && restorable_list rest

/// `n` copies of a view: the unrolling of a literal repeat.
let rec replicate (#a: Type0) (n: nat) (x: a) : Tot (list a) (decreases n) =
  if n = 0 then [] else x :: replicate (n - 1) x

/// The action a view carries at its root.
let act_of (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v) : a =
  match x with
  | VSequence act _ -> act
  | VAssign act _ _ _ -> act
  | VCall act _ _ -> act
  | VRequire act _ -> act
  | VChoose act _ _ _ _ -> act
  | VRepeat act _ _ -> act
  | VEach act _ -> act
  | VEachOf act _ _ _ _ -> act
  | VLeaf act -> act

/// **The inverse of a RUN** — a program in the fragment, built from the
/// program and its trace, that undoes what the run did when folded from
/// the store the run left (`reverse_run`):
///
///   * a sequence inverts to its members' inverses in REVERSE order;
///   * an assignment that wrote inverts to the assignment of the value it
///     overwrote (the Bennett restore); one that was refused wrote nothing
///     and inverts to the empty sequence;
///   * a guard is its own inverse — it held at that store and holds again;
///   * a branch inverts to the branch whose ENTRY condition is the exit
///     assertion and whose EXIT assertion is the entry condition, with the
///     arm that ran inverted and the other arm EMPTY: nothing was recorded
///     for the arm that did not run, and the exit assertion is what
///     guarantees the inverse never takes it (Janus, with the trace
///     standing in for the untaken arm's self-inverse);
///   * a literal repeat inverts to the SEQUENCE of its iterations'
///     inverses in reverse order — not a repeat, because each iteration
///     overwrote different values and so has its own inverse body;
///   * a per-element iteration (Phase 1990) inverts to the SEQUENCE of
///     its elements' inverses in reverse order, for the same reason — it
///     IS the sequence of its elements (`each_is_lowering`).
///
/// Total: a program and a trace that do not match (a call, a leaf, a
/// trace from a different run) invert to the empty sequence, and
/// `reverse_run` says nothing of them — its hypotheses exclude them.
/// Termination is on the trace, which is finite by construction.
/// F#: `BoundedActions.reverse`.
let rec reverse (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v) (tr: trace v)
  : Tot (action_view a e v) (decreases %[tr; 0]) =
  match x, tr with
  | VSequence act ops, TSeq steps -> VSequence act (reverse_many ops steps)
  | VAssign act state_key _ _, TWrote (OSome old) -> VAssign act state_key (OSome old) ONone
  | VRequire act condition, _ -> VRequire act condition
  | VChoose act entry when_true _ (OSome exit), TChoose true arm ->
    VChoose act exit (reverse when_true arm) (VSequence act []) (OSome entry)
  | VChoose act entry _ when_false (OSome exit), TChoose false arm ->
    VChoose act exit (VSequence act []) (reverse when_false arm) (OSome entry)
  | VRepeat act (BLiteral n) body, TRepeat iterations -> VSequence act (reverse_many (replicate n body) iterations)
  | VEach act elements, TEach steps -> VSequence act (reverse_many elements steps)
  | VEachOf act _ _ _ elements, TEachOf _ steps -> VSequence act (reverse_many elements steps)
  | _, _ -> VSequence (act_of x) []

and reverse_many (#a: Type0) (#e: Type0) (#v: Type0)
                 (ops: list (action_view a e v)) (steps: list (trace v))
  : Tot (list (action_view a e v)) (decreases %[steps; 1]) =
  match ops, steps with
  | x :: rest, t :: ts -> app (reverse_many rest ts) [ reverse x t ]
  | _, _ -> []

/// The action-level inverse. F#: `BoundedActions.reverse`.
let reverse_action (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0)
                   (w: witness a e v eff) (act: a) (tr: trace v) : action_view a e v =
  reverse (w.w_view act) tr

/// Restoring an overwritten PRESENT key: writing the old value over the
/// new one gives back exactly the store before the write — structurally,
/// not only at `lookup`, because `write` replaces in place.
let rec write_restore (#v: Type0) (s: store v) (k: key) (x: v) (old: v)
  : Lemma (requires lookup s k == OSome old)
          (ensures write (write s k x) k old == s)
          (decreases s) =
  match s with
  | [] -> ()
  | (k', _) :: rest -> if k' = k then () else write_restore rest k x old

/// One step of `fold_many` and of the traced folds, as equations the
/// recursive lemmas below call rather than leave the solver to unfold
/// (the same discipline `proofs/Staging.fst` records for `plan_ops`).
let fold_many_nil (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                  (w: witness a e v eff) (ar: handler_arm v eff p) (node_id: string) (s: store v) (pl: p)
  : Lemma (fold_many w ar node_id [] s pl == (store_only s, pl)) = ()

let fold_many_single (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                     (w: witness a e v eff) (ar: handler_arm v eff p)
                     (node_id: string) (y: action_view a e v) (s: store v) (pl: p)
  : Lemma ((fst (fold_many w ar node_id [ y ] s pl)).o_store == (fst (fold w ar node_id y s pl)).o_store /\
           (fst (fold_many w ar node_id [ y ] s pl)).o_halted == (fst (fold w ar node_id y s pl)).o_halted /\
           snd (fold_many w ar node_id [ y ] s pl) == snd (fold w ar node_id y s pl)) = ()

let fold_traced_many_cons (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                          (w: witness a e v eff) (ar: handler_arm v eff p)
                          (node_id: string) (x: action_view a e v) (rest: list (action_view a e v))
                          (s: store v) (pl: p)
  : Lemma
      (fold_traced_many w ar node_id (x :: rest) s pl ==
       (let (o1, p1, t1) = fold_traced w ar node_id x s pl in
        if o1.o_halted then (o1, p1, [ t1 ])
        else
          let (o2, p2, ts) = fold_traced_many w ar node_id rest o1.o_store p1 in
          ({ o_store = o2.o_store;
             o_effects = app o1.o_effects o2.o_effects;
             o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
             o_halted = o2.o_halted },
           p2, t1 :: ts))) = ()

let fold_traced_repeat_step (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                            (w: witness a e v eff) (ar: handler_arm v eff p)
                            (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p)
  : Lemma
      (requires n > 0)
      (ensures
        fold_traced_repeat w ar node_id body n s pl ==
        (let (o1, p1, t1) = fold_traced w ar node_id body s pl in
         if o1.o_halted then (o1, p1, [ t1 ])
         else
           let (o2, p2, ts) = fold_traced_repeat w ar node_id body (n - 1) o1.o_store p1 in
           ({ o_store = o2.o_store;
              o_effects = app o1.o_effects o2.o_effects;
              o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
              o_halted = o2.o_halted },
            p2, t1 :: ts))) = ()

let reverse_many_cons (#a: Type0) (#e: Type0) (#v: Type0)
                      (x: action_view a e v) (rest: list (action_view a e v))
                      (t: trace v) (ts: list (trace v))
  : Lemma (reverse_many (x :: rest) (t :: ts) == app (reverse_many rest ts) [ reverse x t ]) = ()

/// **`reverse_run`.** For every program `x` in the reversible fragment and
/// every store `s` it runs on — the traced run from `s` does not halt and
/// its trace is restorable — folding the inverse of the run from the
/// store the run left gives back `s`, and does not halt. The forward run
/// is at placement `pl` and the inverse at any `pl'`: the fragment has no
/// call, so neither touches its placement. In one line:
/// `run (reverse p) (run p s) = s`, with `reverse` applied to the RUN
/// (the program and its trace), which is what the Bennett embedding of
/// `Assign` makes it.
let rec reverse_run (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                    (w: witness a e v eff) (ar: handler_arm v eff p)
                    (node_id: string) (x: action_view a e v) (s: store v) (pl: p) (pl': p)
  : Lemma
      (requires
        reversible x /\
        (let (o, _, tr) = fold_traced w ar node_id x s pl in not o.o_halted /\ restorable tr))
      (ensures
        (let (o, _, tr) = fold_traced w ar node_id x s pl in
         let (o', _) = fold w ar node_id (reverse x tr) o.o_store pl' in
         o'.o_store == s /\ not o'.o_halted))
      (decreases %[x; 0; 0]) =
  match x with
  | VAssign _ state_key value value_from ->
    if w.w_is_reserved state_key then ()
    else
      (match value_from with
       | OSome expr ->
         (match w.w_resolve s expr with
          | Resolved jv ->
            (match lookup s state_key with
             | OSome old -> write_restore s state_key jv old
             | ONone -> ())
          | _ -> ())
       | ONone ->
         (match value with
          | OSome jv ->
            (match lookup s state_key with
             | OSome old -> write_restore s state_key jv old
             | ONone -> ())
          | ONone -> ()))
  | VRequire _ _ -> ()
  | VSequence _ ops -> reverse_run_many w ar node_id ops s pl pl'
  | VChoose _ entry when_true when_false _ ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       if w.w_is_true jv then reverse_run w ar node_id when_true s pl pl'
       else reverse_run w ar node_id when_false s pl pl'
     | _ -> ())
  | VRepeat _ (BLiteral n) body -> reverse_run_repeat w ar node_id body n s pl pl'
  | VRepeat _ (BParameter _ _ _) _ -> ()
  // A per-element iteration is undone as the sequence of its elements
  // is: the forward trace is the elements' steps and the inverse is their
  // inverses in reverse order.
  | VEach _ elements -> reverse_run_many w ar node_id elements s pl pl'
  // A store-bound iteration is undone as the sequence of the elements the
  // recorded extent lowered to; a run that halted at the read is excluded
  // by the hypothesis.
  | VEachOf _ source ceiling _ elements ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs -> if length xs <= ceiling then reverse_run_many w ar node_id elements s pl pl' else ()
        | ONone -> ())
     | _ -> ())
  | VCall _ _ _ -> ()
  | VLeaf _ -> ()

and reverse_run_many (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                     (w: witness a e v eff) (ar: handler_arm v eff p)
                     (node_id: string) (ops: list (action_view a e v)) (s: store v) (pl: p) (pl': p)
  : Lemma
      (requires
        reversible_list ops /\
        (let (o, _, steps) = fold_traced_many w ar node_id ops s pl in not o.o_halted /\ restorable_list steps))
      (ensures
        (let (o, _, steps) = fold_traced_many w ar node_id ops s pl in
         let (o', _) = fold_many w ar node_id (reverse_many ops steps) o.o_store pl' in
         o'.o_store == s /\ not o'.o_halted))
      (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> fold_many_nil w ar node_id s pl'
  | x :: rest ->
    fold_traced_many_cons w ar node_id x rest s pl;
    let (o1, p1, t1) = fold_traced w ar node_id x s pl in
    let (o2, p2, ts) = fold_traced_many w ar node_id rest o1.o_store p1 in
    reverse_many_cons x rest t1 ts;
    // The inverse of the rest runs first, from where the whole run ended,
    // and lands on the store the first member left ...
    reverse_run_many w ar node_id rest o1.o_store p1 pl';
    let (oa, pa) = fold_many w ar node_id (reverse_many rest ts) o2.o_store pl' in
    // ... then the first member's inverse runs from there and lands on `s`.
    sequence_homomorphism w ar node_id (reverse_many rest ts) [ reverse x t1 ] o2.o_store pl';
    fold_many_single w ar node_id (reverse x t1) oa.o_store pa;
    reverse_run w ar node_id x s pl pa

and reverse_run_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                       (w: witness a e v eff) (ar: handler_arm v eff p)
                       (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p) (pl': p)
  : Lemma
      (requires
        reversible body /\
        (let (o, _, its) = fold_traced_repeat w ar node_id body n s pl in not o.o_halted /\ restorable_list its))
      (ensures
        (let (o, _, its) = fold_traced_repeat w ar node_id body n s pl in
         let (o', _) = fold_many w ar node_id (reverse_many (replicate n body) its) o.o_store pl' in
         o'.o_store == s /\ not o'.o_halted))
      (decreases %[body; 2; n]) =
  if n = 0 then fold_many_nil w ar node_id s pl'
  else begin
    fold_traced_repeat_step w ar node_id body n s pl;
    let (o1, p1, t1) = fold_traced w ar node_id body s pl in
    let (o2, p2, ts) = fold_traced_repeat w ar node_id body (n - 1) o1.o_store p1 in
    assert (replicate n body == body :: replicate (n - 1) body);
    reverse_many_cons body (replicate (n - 1) body) t1 ts;
    reverse_run_repeat w ar node_id body (n - 1) o1.o_store p1 pl';
    let (oa, pa) = fold_many w ar node_id (reverse_many (replicate (n - 1) body) ts) o2.o_store pl' in
    sequence_homomorphism w ar node_id (reverse_many (replicate (n - 1) body) ts) [ reverse body t1 ] o2.o_store pl';
    fold_many_single w ar node_id (reverse body t1) oa.o_store pa;
    reverse_run w ar node_id body s pl pa
  end

/// **`repeat_is_unrolling`.** A literal repeat folds exactly as the
/// sequence of `n` copies of its body — outcome and placement — so every
/// sequence law (`sequence_homomorphism`, `fold_reserved_untouched`'s
/// list form) is a law about repeats too, and the inverse of a repeat is
/// the inverse of that sequence.
let rec repeat_is_unrolling (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                            (w: witness a e v eff) (ar: handler_arm v eff p)
                            (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p)
  : Lemma (ensures fold_repeat w ar node_id body n s pl == fold_many w ar node_id (replicate n body) s pl)
          (decreases n) =
  if n = 0 then ()
  else begin
    let (o1, p1) = fold w ar node_id body s pl in
    if o1.o_halted then () else repeat_is_unrolling w ar node_id body (n - 1) o1.o_store p1
  end

/// **`reversible_run_undoes`** — `reverse_run` at the action level, which
/// is the form the code exposes: `run (reverse p) (run p s) = s`.
let reversible_run_undoes (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                          (w: witness a e v eff) (ar: handler_arm v eff p)
                          (node_id: string) (act: a) (s: store v) (pl: p) (pl': p)
  : Lemma
      (requires
        reversible (w.w_view act) /\
        (let (o, _, tr) = run_action_traced w ar node_id act s pl in not o.o_halted /\ restorable tr))
      (ensures
        (let (o, _, tr) = run_action_traced w ar node_id act s pl in
         let (o', _) = fold w ar node_id (reverse_action w act tr) o.o_store pl' in
         o'.o_store == s /\ not o'.o_halted)) =
  reverse_run w ar node_id (w.w_view act) s pl pl'

// ─── 7. The work of a run is bounded by the view's cost (Phase 1976) ─

/// `n` times `c`, written over the count so that every fact about it is
/// linear and structural — no non-linear arithmetic reaches the solver.
[@@ noextract_to "FSharp"]
let rec times (n: nat) (c: nat) : Tot nat (decreases n) =
  if n = 0 then 0 else c + times (n - 1) c

[@@ noextract_to "FSharp"]
let max (x: nat) (y: nat) : nat = if x >= y then x else y

/// `Budget.actionCascadeCost` over the view, exactly: a sequence sums its
/// members; a selection is one step (its condition) and the MORE
/// expensive arm; a repeat is one step (its bound) and its body times the
/// bound, a parameter bound priced at the TOP of its range so the price
/// needs no store; every other shape is one step. Ghost: `Budget.fs`
/// computes it in saturating arithmetic, and this is the exact figure it
/// saturates.
[@@ noextract_to "FSharp"]
let rec view_cost (#a: Type0) (#e: Type0) (#v: Type0) (x: action_view a e v)
  : Tot nat (decreases %[x; 0]) =
  match x with
  | VSequence _ ops -> view_cost_list ops
  | VChoose _ _ when_true when_false _ -> 1 + max (view_cost when_true) (view_cost when_false)
  | VRepeat _ (BLiteral n) body -> 1 + times n (view_cost body)
  | VRepeat _ (BParameter _ _ hi) body -> 1 + times hi (view_cost body)
  // An `Each` is priced as the lowered form it is: its elements' costs
  // summed — the body's cost once per element of a literal collection,
  // with no step for a bound, because a literal collection is not read.
  | VEach _ elements -> view_cost_list elements
  // A store-bound `Each` (Phase 1991) is one step for the read and its
  // lowered elements' costs summed.
  | VEachOf _ _ _ _ elements -> 1 + view_cost_list elements
  | VAssign _ _ _ _ -> 1
  | VCall _ _ _ -> 1
  | VRequire _ _ -> 1
  | VLeaf _ -> 1

and view_cost_list (#a: Type0) (#e: Type0) (#v: Type0) (ops: list (action_view a e v))
  : Tot nat (decreases %[ops; 1]) =
  match ops with
  | [] -> 0
  | x :: rest -> view_cost x + view_cost_list rest

/// The steps a run took, read off its trace: one per leaf entry.
[@@ noextract_to "FSharp"]
let rec trace_steps (#v: Type0) (tr: trace v) : Tot nat (decreases %[tr; 0]) =
  match tr with
  | TNothing -> 1
  | TWrote _ -> 1
  | TSeq steps -> steps_sum steps
  | TChoose _ arm -> trace_steps arm
  | TRepeat iterations -> steps_sum iterations
  | TEach elements -> steps_sum elements
  | TEachOf _ elements -> steps_sum elements

and steps_sum (#v: Type0) (steps: list (trace v)) : Tot nat (decreases %[steps; 1]) =
  match steps with
  | [] -> 0
  | t :: rest -> trace_steps t + steps_sum rest

let rec times_monotone (n: nat) (m: nat) (c: nat)
  : Lemma (requires n <= m) (ensures times n c <= times m c) (decreases m) =
  if m = 0 then () else if n = 0 then () else times_monotone (n - 1) (m - 1) c

/// **`fold_steps_within_cost`.** The steps a run takes never exceed the
/// view's cost: a selection's run is within its condition and the more
/// expensive arm, and a repeat's run is within its bound and the body's
/// cost that many times — a parameter bound within the top of its range.
/// So the budget's price, computed from the tree before the run, bounds
/// the work the run does, which is the second half of "safe to run
/// untrusted" extended to the two new shapes.
let rec fold_steps_within_cost (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                               (w: witness a e v eff) (ar: handler_arm v eff p)
                               (node_id: string) (x: action_view a e v) (s: store v) (pl: p)
  : Lemma (ensures (let (_, _, tr) = fold_traced w ar node_id x s pl in trace_steps tr <= view_cost x))
          (decreases %[x; 0; 0]) =
  match x with
  | VSequence _ ops -> fold_steps_within_cost_list w ar node_id ops s pl
  | VEach _ elements -> fold_steps_within_cost_list w ar node_id elements s pl
  | VEachOf _ source ceiling _ elements ->
    (match w.w_resolve s source with
     | Resolved jv ->
       (match w.w_as_elements jv with
        | OSome xs -> if length xs <= ceiling then fold_steps_within_cost_list w ar node_id elements s pl else ()
        | ONone -> ())
     | _ -> ())
  | VChoose _ entry when_true when_false _ ->
    (match w.w_resolve s entry with
     | Resolved jv ->
       if w.w_is_true jv then fold_steps_within_cost w ar node_id when_true s pl
       else fold_steps_within_cost w ar node_id when_false s pl
     | _ -> ())
  | VRepeat _ count body ->
    (match count with
     | BLiteral n -> fold_steps_within_cost_repeat w ar node_id body n s pl
     | BParameter expr lo hi ->
       (match w.w_resolve s expr with
        | Resolved jv ->
          (match w.w_as_count jv with
           | OSome n ->
             if lo <= n && n <= hi then begin
               fold_steps_within_cost_repeat w ar node_id body n s pl;
               times_monotone n hi (view_cost body)
             end
             else ()
           | ONone -> ())
        | _ -> ()))
  | VAssign _ _ _ _ -> ()
  | VCall _ _ _ -> ()
  | VRequire _ _ -> ()
  | VLeaf _ -> ()

and fold_steps_within_cost_list (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                (w: witness a e v eff) (ar: handler_arm v eff p)
                                (node_id: string) (ops: list (action_view a e v)) (s: store v) (pl: p)
  : Lemma (ensures (let (_, _, steps) = fold_traced_many w ar node_id ops s pl in
                    steps_sum steps <= view_cost_list ops))
          (decreases %[ops; 1; 0]) =
  match ops with
  | [] -> ()
  | x :: rest ->
    fold_traced_many_cons w ar node_id x rest s pl;
    fold_steps_within_cost w ar node_id x s pl;
    let (o1, p1, _) = fold_traced w ar node_id x s pl in
    if o1.o_halted then () else fold_steps_within_cost_list w ar node_id rest o1.o_store p1

and fold_steps_within_cost_repeat (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                  (w: witness a e v eff) (ar: handler_arm v eff p)
                                  (node_id: string) (body: action_view a e v) (n: nat) (s: store v) (pl: p)
  : Lemma (ensures (let (_, _, its) = fold_traced_repeat w ar node_id body n s pl in
                    steps_sum its <= times n (view_cost body)))
          (decreases %[body; 2; n]) =
  if n = 0 then ()
  else begin
    fold_traced_repeat_step w ar node_id body n s pl;
    fold_steps_within_cost w ar node_id body s pl;
    let (o1, p1, _) = fold_traced w ar node_id body s pl in
    if o1.o_halted then () else fold_steps_within_cost_repeat w ar node_id body (n - 1) o1.o_store p1
  end

// ─── 9. An `Each` is the sequence of its elements (Phase 1990) ────────

(* ───────────────────────────────────────────────────────────────────
   Per-element iteration over a literal collection is VOCABULARY that
   lowers to the core by substitution (D1): the witness substitutes each
   element of the collection for the placeholder in the body, and what
   the core runs is the sequence of the results. The view carries the
   lowered form (`VEach act elements`), so the theorem below is an
   EQUATION between two views over the same elements, and every sequence
   law — the homomorphism, the reserved-key invariant, the halting
   clause, the reversal, the cost bound — is a law about an `Each` with
   no further proof. What the model does NOT own is the substitution
   itself: `ActionWitness.Substitute` is a witness arrow, like `View`
   and `Lower`, and the obligation that it preserves the body's shape is
   stated in the ladder and tested at the toy witness, where the
   differential host runs production (which substitutes as it folds)
   beside this model (handed the elements already substituted).
   ─────────────────────────────────────────────────────────────────── *)

/// **`each_is_lowering`.** An `Each` over its lowered elements folds to
/// the same outcome and placement as the sequence of those elements;
/// traced, it answers the same outcome and placement with the elements'
/// steps under its own constructor; it is in the reversible fragment
/// exactly when the sequence is; and it is priced exactly as the
/// sequence is — the body's cost once per element. Unconditional: the
/// fold's `VEach` arm IS `fold_many` over the elements.
let each_is_lowering (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                     (w: witness a e v eff) (ar: handler_arm v eff p)
                     (node_id: string) (act: a) (elements: list (action_view a e v)) (s: store v) (pl: p)
  : Lemma
      (ensures
        fold w ar node_id (VEach act elements) s pl == fold w ar node_id (VSequence act elements) s pl /\
        (let (o, p', tr) = fold_traced w ar node_id (VEach act elements) s pl in
         let (o', p'', tr') = fold_traced w ar node_id (VSequence act elements) s pl in
         o == o' /\ p' == p'' /\ TEach? tr /\ TSeq? tr' /\ TEach?.elements tr == TSeq?.steps tr') /\
        reversible (VEach act elements) == reversible (VSequence act elements) /\
        view_cost (VEach act elements) == view_cost (VSequence act elements)) = ()

/// **`each_reverse_is_sequence_reverse`.** The inverse of an `Each` run is
/// the inverse of the run of the sequence of its elements: the elements'
/// inverses in reverse order. With `each_is_lowering` and `reverse_run`
/// this is the reversal statement carried through the lowering — an
/// `Each` in the fragment, run forwards with a restorable trace, is
/// undone to its starting store by the inverse of its lowered sequence.
let each_reverse_is_sequence_reverse (#a: Type0) (#e: Type0) (#v: Type0)
                                     (act: a) (elements: list (action_view a e v)) (steps: list (trace v))
  : Lemma (reverse (VEach act elements) (TEach steps) == reverse (VSequence act elements) (TSeq steps)) = ()

// ─── 10. A store-bound `Each` (Phase 1991) ───────────────────────────

(* ───────────────────────────────────────────────────────────────────
   Per-element iteration over a collection the STORE holds (D36). The
   view still carries the lowered form — the elements the host
   substituted over the extent it read — and what the model ADDS is the
   read: the source resolved once at entry through `w_resolve`, read as
   a collection through `w_as_elements`, and the ceiling checked against
   what it answered before the first element. So the equation with
   `VEach` is CONDITIONAL on the store holding the extent the view was
   lowered over — the differential host's obligation, stated — and the
   refusal over the ceiling runs nothing. The trace records the extent
   the arrow ANSWERED, which is how the model says "recorded".
   ─────────────────────────────────────────────────────────────────── *)

/// **`each_of_is_each_over_extent`.** When the source resolves to a
/// collection whose extent is within the ceiling, a store-bound `Each`
/// folds to the same outcome and placement as the literal `Each` over the
/// same lowered elements; traced, it answers the same outcome and
/// placement, recording the extent the read answered beside the same
/// element steps; and it is in the reversible fragment exactly when the
/// literal `Each` is.
let each_of_is_each_over_extent (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                (w: witness a e v eff) (ar: handler_arm v eff p)
                                (node_id: string) (act: a) (source: e) (ceiling: nat) (jv: v) (xs: list v)
                                (elements: list (action_view a e v)) (s: store v) (pl: p)
  : Lemma
      (requires w.w_resolve s source == Resolved jv /\ w.w_as_elements jv == OSome xs /\ length xs <= ceiling)
      (ensures
        fold w ar node_id (VEachOf act source ceiling xs elements) s pl == fold w ar node_id (VEach act elements) s pl /\
        (let (o, p', tr) = fold_traced w ar node_id (VEachOf act source ceiling xs elements) s pl in
         let (o', p'', tr') = fold_traced w ar node_id (VEach act elements) s pl in
         o == o' /\ p' == p'' /\ TEachOf? tr /\ TEach? tr' /\
         TEachOf?.extent tr == xs /\ TEachOf?.elements tr == TEach?.elements tr') /\
        reversible (VEachOf act source ceiling xs elements) == reversible (VEach act elements)) = ()

/// **`each_of_over_ceiling_runs_nothing`.** When the source resolves to a
/// collection whose extent is OVER the ceiling, the fold halts at entry
/// with the refusal named: the store untouched, no effect, one diagnostic,
/// and no element run. Traced, it answers the same outcome and placement
/// and records `TNothing`, as every halting read does.
let each_of_over_ceiling_runs_nothing (#a: Type0) (#e: Type0) (#v: Type0) (#eff: Type0) (#p: Type0)
                                      (w: witness a e v eff) (ar: handler_arm v eff p)
                                      (node_id: string) (act: a) (source: e) (ceiling: nat) (jv: v) (xs: list v)
                                      (extent: list v) (elements: list (action_view a e v)) (s: store v) (pl: p)
  : Lemma
      (requires w.w_resolve s source == Resolved jv /\ w.w_as_elements jv == OSome xs /\ length xs > ceiling)
      (ensures
        fold w ar node_id (VEachOf act source ceiling extent elements) s pl ==
        (halted node_id (w.w_describe act) "the collection's extent is over its declared ceiling" s, pl) /\
        (let (o, p') = fold w ar node_id (VEachOf act source ceiling extent elements) s pl in
         o.o_store == s /\ o.o_effects == [] /\ o.o_halted /\ p' == pl) /\
        fold_traced w ar node_id (VEachOf act source ceiling extent elements) s pl ==
        (halted node_id (w.w_describe act) "the collection's extent is over its declared ceiling" s, pl, TNothing)) = ()

/// **`each_of_reverse_is_sequence_reverse`.** The inverse of a store-bound
/// `Each` run is the inverse of the run of the sequence of its elements.
/// `reverse` takes a program and a trace and NO store, so the inverse is
/// built from the record — the recorded extent's lowering — and never
/// from the live store: D36's "never the live store" clause, by type.
let each_of_reverse_is_sequence_reverse (#a: Type0) (#e: Type0) (#v: Type0)
                                        (act: a) (source: e) (ceiling: nat) (extent: list v)
                                        (elements: list (action_view a e v))
                                        (recorded: list v) (steps: list (trace v))
  : Lemma (reverse (VEachOf act source ceiling extent elements) (TEachOf recorded steps) ==
           reverse (VSequence act elements) (TSeq steps)) = ()

/// **`each_of_priced_at_the_read_plus_elements`.** A store-bound `Each` is
/// priced as one step for the read plus its lowered sequence.
let each_of_priced_at_the_read_plus_elements (#a: Type0) (#e: Type0) (#v: Type0)
                                             (act: a) (source: e) (ceiling: nat) (extent: list v)
                                             (elements: list (action_view a e v))
  : Lemma (view_cost (VEachOf act source ceiling extent elements) == 1 + view_cost (VSequence act elements)) = ()

/// Every element costs at most `c`. Ghost.
[@@ noextract_to "FSharp"]
let rec all_cost_within (#a: Type0) (#e: Type0) (#v: Type0) (c: nat) (els: list (action_view a e v))
  : Tot bool (decreases els) =
  match els with
  | [] -> true
  | x :: rest -> view_cost x <= c && all_cost_within c rest

let rec view_cost_list_within (#a: Type0) (#e: Type0) (#v: Type0) (c: nat) (els: list (action_view a e v))
  : Lemma (requires all_cost_within c els) (ensures view_cost_list els <= times (length els) c) (decreases els) =
  match els with
  | [] -> ()
  | _ :: rest -> view_cost_list_within c rest

/// **`each_of_priced_within_ceiling`.** Lowered over an extent within the
/// ceiling, with every element costing at most `c`, a store-bound `Each`
/// costs at most one step for the read plus `c` per element of the
/// ceiling — the price the budget computes from the tree alone (D36 item
/// 5: the parameter-bound repeat's rule at the top of its range).
let each_of_priced_within_ceiling (#a: Type0) (#e: Type0) (#v: Type0)
                                  (act: a) (source: e) (ceiling: nat) (extent: list v)
                                  (elements: list (action_view a e v)) (c: nat)
  : Lemma (requires length elements <= ceiling /\ all_cost_within c elements)
          (ensures view_cost (VEachOf act source ceiling extent elements) <= 1 + times ceiling c) =
  view_cost_list_within c elements;
  times_monotone (length elements) ceiling c

(* ═══════════════════════════════════════════════════════════════════
   THE UI WITNESS — today's fourteen arms seen through the view.

   What follows is the adapter Phase 1897 writes (`Fuaran.Program.UI`),
   modelled: the closed `Action` union, the UI's resolution outcomes, its
   client effects, the eight host arrows it composes, and `ui_view` /
   `ui_lower` / `ui_describe` / `ui_resolve` — the four `ActionWitness` +
   `ExprWitness` members — over them. `run` is `run_action` at that
   witness with Phase 1715's exact signature, which is what keeps the
   existing differential host running unchanged: the fourteen arms seen
   through the view are the fourteen arms.
   ═══════════════════════════════════════════════════════════════════ *)

(* ───────────────────────────────────────────────────────────────────
   Resolution outcomes — `BindingResolver.Resolution`, the UI's four.
   ─────────────────────────────────────────────────────────────────── *)

/// F#: `resolveJVal`'s result, with `JValObj.toObj` already applied to
/// the resolved value (the lowering is the axiom's, not the fold's).
type res (v: Type0) =
  | JResolved : value: v -> res v
  | JNotResolved : res v
  | JErrored : message: string -> res v
  | JI18nUnresolved : i18n_key: string -> res v

/// F#: `resolveScalarText`'s result. `SResolved ONone` is the
/// resolved-but-NULL value the tier answers for the unwritten-`State`
/// steady state — the one distinction the two `TextSource` arms below
/// treat differently, so it is carried rather than collapsed.
type res_text =
  | SResolved : value: opt string -> res_text
  | SNotResolved : res_text
  | SErrored : message: string -> res_text
  | SI18nUnresolved : i18n_key: string -> res_text

(* ───────────────────────────────────────────────────────────────────
   The vocabulary the action union ranges over — `Fuaran.UI.Types`.
   ─────────────────────────────────────────────────────────────────── *)

/// F#: `TextSource`. `TBound`'s binding and `TI18n`'s argument map are
/// opaque: the adapter reads the i18n KEY and hands the rest to an axiom.
type text_source (b: Type0) =
  | TLiteral : text: string -> text_source b
  | TBound : binding: b -> text_source b
  | TI18n : i18n_key: string -> args: b -> text_source b

/// F#: `NavigateTarget`. Passed through untouched — it names the
/// browsing context, not a destination, so the URL floor has no opinion
/// on it.
type nav_target =
  | NSelf : nav_target
  | NBlank : nav_target

/// F#: `FileReadEncoding`.
type file_encoding =
  | FText : file_encoding
  | FBase64 : file_encoding
  | FDataUrl : file_encoding

/// F#: `CallResultTarget`. The view reads only its PRESENCE.
type call_target =
  | CTState : state_key: string -> call_target
  | CTQuery : query_name: string -> call_target

/// F#: `ClientEffect`, restricted to the arms this witness lowers to.
/// The host union is wider (`PushState` / `Download` / `Confirm`); their
/// absence here is the claim that the bounded fold cannot reach them,
/// and the differential host's total translation is where that claim is
/// checked.
type client_effect =
  | ENavigate : route: string -> target: nav_target -> client_effect
  | EClipboard : text: string -> client_effect
  | EPrint : client_effect
  | EFocus : node_id: string -> client_effect
  | EReadFileBody : node_id: string -> encoding: string -> client_effect

/// F#: `Action<'Msg>`, the closed union. Fourteen arms, named
/// one-for-one. `k` is a closure slot the wire decoder fills with an
/// inert sentinel; `b` is a host value handed to an axiom. Nothing in
/// this module can eliminate a `k`.
type action (v: Type0) (b: Type0) (k: Type0) =
  | AChain : ops: list (action v b k) -> action v b k
  | AWriteToClipboard : text: text_source b -> action v b k
  | ADispatch : msg: k -> action v b k
  | AInvoke : capability_id: string -> args: b -> action v b k
  | AReadFileBody : file_ref: string -> file_handle: k -> encoding: file_encoding -> on_read: k -> action v b k
  | ACall : endpoint: string -> on_result: k -> into: opt call_target -> action v b k
  | ANavigate : route: text_source b -> target: nav_target -> action v b k
  | ACommitLocal : node_id: string -> action v b k
  | ANotify : channel: string -> payload: b -> action v b k
  | ASetState : state_key: key -> value: opt v -> value_from: opt b -> action v b k
  | AAiTool : tool_name: string -> args: b -> action v b k
  | APrint : action v b k
  | AConfirm : prompt: text_source b -> on_confirm: action v b k -> on_cancel: opt (action v b k) -> action v b k
  | AFocus : node_id: string -> action v b k

/// The UI-typed outcome and arm — Phase 1715's names at Phase 1715's
/// arity, which is the surface the differential host is written to.
type outcome (v: Type0) = bounded_outcome v client_effect

type arm (v: Type0) (p: Type0) = handler_arm v client_effect p

/// The host-supplied pure functions the UI witness composes. Every one
/// is a TOTAL arrow and nothing below depends on what any of them
/// answers. Modelling any of them would introduce a second
/// implementation free to disagree with the host's; the theorems hold
/// for EVERY total arrow, which is the honest statement.
///
///   * `resolve_jval` / `resolve_scalar` / `i18n_has` / `resolve_text` —
///     `Fuaran.UI.Renderer.BindingResolver`'s resolution of a binding
///     against the store, with `JValObj.toObj`'s lowering folded into the
///     value the arrow returns.
///   * `sanitize_url` — the tree wire specification's renderer URL floor
///     (`Fuaran.UI.Renderer.Sanitize.sanitizeUrl`).
///   * `is_reserved` / `reserved_prefix` —
///     `Fuaran.UI.Renderer.StateKeys.isHostReserved` and the namespace it
///     names.
///   * `route_path` — the log-safe route projection the action
///     description uses (`ActionInvocation.routePath`).
noeq type axioms (v: Type0) (b: Type0) = {
  is_reserved: key -> bool;
  reserved_prefix: string;
  resolve_jval: store v -> b -> res v;
  resolve_scalar: store v -> b -> res_text;
  i18n_has: store v -> string -> bool;
  resolve_text: store v -> text_source b -> string;
  sanitize_url: string -> opt string;
  route_path: string -> string;
}

(* ───────────────────────────────────────────────────────────────────
   `ActionWitness.Describe` — `ActionInvocation.describe`, the log-safe
   projection `Validation.describeAction` forwards to. Modelled rather
   than axiomatised, because the diagnostics the fold emits carry it and
   the differential host compares them verbatim.
   ─────────────────────────────────────────────────────────────────── *)

let describe (#v: Type0) (#b: Type0) (#k: Type0) (ax: axioms v b) (a: action v b k) : string =
  match a with
  | ADispatch _ -> "Dispatch"
  | ACall endpoint _ _ -> strcat "Call(" (strcat endpoint ")")
  | ANotify channel _ -> strcat "Notify(" (strcat channel ")")
  | ANavigate route _ ->
    (match route with
     | TLiteral literal -> strcat "Navigate(" (strcat (ax.route_path literal) ")")
     | _ -> "Navigate(<bound>)")
  | ASetState k _ _ -> strcat "SetState(" (strcat k ")")
  | AAiTool tool_name _ -> strcat "AiTool(" (strcat tool_name ")")
  | AChain _ -> "Chain"
  | ACommitLocal node_id -> strcat "CommitLocal(" (strcat node_id ")")
  | AWriteToClipboard _ -> "WriteToClipboard"
  | APrint -> "Print"
  | AConfirm _ _ _ -> "Confirm"
  | AFocus node_id -> strcat "Focus(" (strcat node_id ")")
  | AReadFileBody _ _ _ _ -> "ReadFileBody"
  | AInvoke capability_id _ -> strcat "Invoke(" (strcat capability_id ")")

/// F#: the `Result<string, string>` the `Navigate` and clipboard leaves
/// compute.
type text_result =
  | ROk : value: string -> text_result
  | RErr : message: string -> text_result

let unresolved_i18n (k: string) : string = strcat "unresolved i18n key '" (strcat k "'")

(* ───────────────────────────────────────────────────────────────────
   `ActionWitness.View` — the adapter's total match over the closed
   fourteen-case union, taken to exhaustion. Four cases are control
   structure the fold owns; the other ten are leaves. Every constructor
   is named, with no wildcard: the exhaustiveness check D18 moves into
   the adapter is checked here by the same means.
   ─────────────────────────────────────────────────────────────────── *)

let rec ui_view (#v: Type0) (#b: Type0) (#k: Type0) (a: action v b k)
  : Tot (action_view (action v b k) b v) (decreases %[a; 0]) =
  match a with
  | AChain ops -> VSequence a (ui_view_list ops)
  | ASetState state_key value value_from -> VAssign a state_key value value_from
  | ACall endpoint _ into -> VCall a endpoint (OSome? into)
  | AWriteToClipboard _ -> VLeaf a
  | ADispatch _ -> VLeaf a
  | AInvoke _ _ -> VLeaf a
  | AReadFileBody _ _ _ _ -> VLeaf a
  | ANavigate _ _ -> VLeaf a
  | ACommitLocal _ -> VLeaf a
  | ANotify _ _ -> VLeaf a
  | AAiTool _ _ -> VLeaf a
  | APrint -> VLeaf a
  | AConfirm _ _ _ -> VLeaf a
  | AFocus _ -> VLeaf a

and ui_view_list (#v: Type0) (#b: Type0) (#k: Type0) (ops: list (action v b k))
  : Tot (list (action_view (action v b k) b v)) (decreases %[ops; 1]) =
  match ops with
  | [] -> []
  | x :: rest -> ui_view x :: ui_view_list rest

(* ───────────────────────────────────────────────────────────────────
   `ActionWitness.Lower` — what each LEAF does at this witness. The
   three control cases are never handed to it through the view
   (`ui_view` sends them elsewhere) and, being total, it declines them.
   ─────────────────────────────────────────────────────────────────── *)

let ui_lower (#v: Type0) (#b: Type0) (#k: Type0)
             (ax: axioms v b) (node_id: string) (a: action v b k) (s: store v)
  : leaf_outcome client_effect =
  match a with

  // An inherently-browser arm lowered to a closure-free effect. The
  // route resolves at DISPATCH time and the URL floor judges the
  // RESOLVED string; an unresolved route navigates NOWHERE rather than
  // degrading to the empty string, which is a real navigation.
  | ANavigate route target ->
    let resolved : text_result =
      match route with
      | TLiteral literal -> ROk literal
      | TBound binding ->
        (match ax.resolve_scalar s binding with
         | SResolved (OSome value) -> ROk value
         | SResolved ONone -> RErr "the route binding resolved to no value"
         | SNotResolved -> RErr "the route binding did not resolve to a value"
         | SErrored m -> RErr m
         | SI18nUnresolved kk -> RErr (unresolved_i18n kk))
      | TI18n kk _ ->
        if ax.i18n_has s kk then ROk (ax.resolve_text s route) else RErr (unresolved_i18n kk)
    in
    (match resolved with
     | RErr reason -> Refuse (strcat reason " — nothing was navigated to")
     | ROk r ->
       match ax.sanitize_url r with
       | OSome safe -> Emit (ENavigate safe target)
       | ONone -> Refuse "route is not a safe URL")

  // The clipboard payload resolves at DISPATCH time through the same
  // resolver. A resolved-but-null value is the unwritten-`State` steady
  // state and is legitimately copied as the empty string; a binding that
  // genuinely fails to resolve is REFUSED, because on a clipboard nobody
  // sees the gap.
  | AWriteToClipboard text ->
    let payload : text_result =
      match text with
      | TLiteral literal -> ROk literal
      | TBound binding ->
        (match ax.resolve_scalar s binding with
         | SResolved (OSome value) -> ROk value
         | SResolved ONone -> ROk ""
         | SNotResolved -> RErr "the payload binding did not resolve to a value"
         | SErrored m -> RErr m
         | SI18nUnresolved kk -> RErr (unresolved_i18n kk))
      | TI18n kk _ ->
        if ax.i18n_has s kk then ROk (ax.resolve_text s text) else RErr (unresolved_i18n kk)
    in
    (match payload with
     | ROk value -> Emit (EClipboard value)
     | RErr reason -> Refuse (strcat reason " — nothing was written to the clipboard"))

  // Payload-free, and lowered rather than refused: printing is an act of
  // the machine the document is READ on.
  | APrint -> Emit EPrint

  // The node id is a bare string the AUTHOR wrote, addressing a node in
  // this document: nothing to resolve and no floor to apply.
  | AFocus target_node_id -> Emit (EFocus target_node_id)

  // The `on_read` closure is the inert decode sentinel — NOT invoked
  // here (and, in this model, not invocable: `k` has no elimination
  // form). The emitted id is the node the EVENT came from.
  | AReadFileBody _ _ encoding _ ->
    let enc =
      match encoding with
      | FText -> "Text"
      | FBase64 -> "Base64"
      | FDataUrl -> "DataUrl"
    in
    Emit (EReadFileBody node_id enc)

  // Computational host arms with no store/DOM effect on the bounded
  // path, and the one the path has no return leg for. Documented
  // declines, each with a readable diagnostic so "this action is inert
  // here" is observable rather than silent.
  | AConfirm _ _ _ -> Decline
  | ANotify _ _ -> Decline
  | AAiTool _ _ -> Decline
  | AInvoke _ _ -> Decline
  | ADispatch _ -> Decline
  | ACommitLocal _ -> Decline

  // Control structure: never a leaf through `ui_view`. Total, so it
  // answers; it answers the no-op.
  | AChain _ -> Decline
  | ASetState _ _ _ -> Decline
  | ACall _ _ _ -> Decline

/// `ExprWitness.Resolve` at the UI witness: the four-case UI resolution
/// narrowed to D18's three. `I18nUnresolved` becomes an `Errored` whose
/// message is the one production already builds, so the refusal the
/// fold emits for it is byte-identical to Phase 1715's.
let ui_resolve (#v: Type0) (#b: Type0) (ax: axioms v b) (s: store v) (binding: b) : resolution v =
  match ax.resolve_jval s binding with
  | JResolved jv -> Resolved jv
  | JNotResolved -> NotResolved
  | JErrored m -> Errored m
  | JI18nUnresolved kk -> Errored (unresolved_i18n kk)

/// The UI witness, assembled: D18's `ProgramWitness` at the UI adapter,
/// restricted to the members the fold reads.
let ui_witness (#v: Type0) (#b: Type0) (#k: Type0) (ax: axioms v b)
  : witness (action v b k) b v client_effect =
  { w_view = ui_view;
    w_lower = ui_lower ax;
    w_describe = describe ax;
    w_resolve = ui_resolve ax;
    w_is_reserved = ax.is_reserved;
    w_reserved_prefix = ax.reserved_prefix;
    // Unreachable at this witness: no arm of the fourteen views as a
    // guard (`ui_view_no_require`), so the fold never asks. The constant
    // is the fail-closed answer — were it ever reached, it would halt.
    w_is_true = (fun _ -> false);
    // Unreachable too: no arm views as a repeat (`ui_view_no_flow`). The
    // same fail-closed constant — an unreadable count halts.
    w_as_count = (fun _ -> ONone);
    w_as_elements = (fun _ -> ONone) }

/// **The fold at the UI witness** — `BoundedActions.runBoundedActionWith`
/// today, `BoundedActions.run uiWitness` after Phase 1897. Phase 1715's
/// signature, so the differential host is unchanged.
let run (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
        (ax: axioms v b) (ar: arm v p)
        (node_id: string) (a: action v b k) (s: store v) (pl: p)
  : outcome v & p =
  run_action (ui_witness ax) ar node_id a s pl

(* ───────────────────────────────────────────────────────────────────
   THE UI THEOREMS — Phase 1715's five, each a corollary of the generic
   theorem at `ui_witness`. Ghost from here on.
   ─────────────────────────────────────────────────────────────────── *)

// ─── 1. Totality over the closed union ───────────────────────────────

/// Every constructor, named once more with NO wildcard. Its value is
/// uninteresting; its shape is the point — a fifteenth arm added to
/// `action` fails to compile HERE exactly as it fails to compile in
/// `ui_view`, which is what "no wildcard, no throw" buys, stated as a
/// thing the prover checks rather than a thing a reader must notice.
[@@ noextract_to "FSharp"]
let handled (#v: Type0) (#b: Type0) (#k: Type0) (a: action v b k) : bool =
  match a with
  | AChain _ -> true
  | AWriteToClipboard _ -> true
  | ADispatch _ -> true
  | AInvoke _ _ -> true
  | AReadFileBody _ _ _ _ -> true
  | ACall _ _ _ -> true
  | ANavigate _ _ -> true
  | ACommitLocal _ -> true
  | ANotify _ _ -> true
  | ASetState _ _ _ -> true
  | AAiTool _ _ -> true
  | APrint -> true
  | AConfirm _ _ _ -> true
  | AFocus _ -> true

/// The placement ANSWERED this call, at the action level.
[@@ noextract_to "FSharp"]
let answered (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
             (ar: arm v p) (node_id: string) (a: action v b k) (s: store v) (pl: p) : bool =
  match a with
  | ACall endpoint _ ONone -> OSome? (ar.answer node_id endpoint s pl)
  | _ -> false

/// **`run_total`.** Phase 1715's statement, now `fold_total` at the UI
/// witness: the fold is defined on every arm of the closed union, and
/// one step that is neither the composition arm nor a call the placement
/// answered leaves the placement untouched, leaves the store unchanged
/// or written at exactly one non-reserved key, and emits at most one
/// effect and at most one diagnostic.
let run_total (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
              (ax: axioms v b) (ar: arm v p)
              (node_id: string) (a: action v b k) (s: store v) (pl: p)
  : Lemma
      (requires (not (AChain? a)) /\ (not (answered ar node_id a s pl)))
      (ensures
        (handled a /\
         (let (o, pl') = run ax ar node_id a s pl in
          pl' == pl /\
          at_most_one o.o_effects /\
          at_most_one o.o_diagnostics /\
          (o.o_store == s \/
           (ASetState? a /\
            (exists (x: v). o.o_store == write s (ASetState?.state_key a) x) /\
            not (ax.is_reserved (ASetState?.state_key a))))))) =
  match a with
  | AChain _ -> ()
  | AWriteToClipboard _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | ADispatch _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | AInvoke _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | AReadFileBody _ _ _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | ACall _ _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | ANavigate _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | ACommitLocal _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | ANotify _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | ASetState _ _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | AAiTool _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | APrint -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | AConfirm _ _ _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl
  | AFocus _ -> fold_total (ui_witness ax) ar node_id (ui_view a) s pl

// ─── 2. No closure is ever invoked ───────────────────────────────────

/// Two actions differ ONLY in the closures they carry. Every other
/// position — the keys, the endpoints, the bindings, the text sources,
/// the nested actions — is required equal; the `k`-typed positions are
/// required nothing at all.
[@@ noextract_to "FSharp"]
let rec same_but_closures (#v: Type0) (#b: Type0) (#k: Type0)
                          (x: action v b k) (y: action v b k) : Tot prop (decreases %[x; 0]) =
  match x, y with
  | AChain xs, AChain ys -> same_but_closures_list xs ys
  | AWriteToClipboard t1, AWriteToClipboard t2 -> t1 == t2
  | ADispatch _, ADispatch _ -> True
  | AInvoke c1 a1, AInvoke c2 a2 -> c1 == c2 /\ a1 == a2
  | AReadFileBody f1 _ e1 _, AReadFileBody f2 _ e2 _ -> f1 == f2 /\ e1 == e2
  | ACall e1 _ i1, ACall e2 _ i2 -> e1 == e2 /\ i1 == i2
  | ANavigate r1 t1, ANavigate r2 t2 -> r1 == r2 /\ t1 == t2
  | ACommitLocal n1, ACommitLocal n2 -> n1 == n2
  | ANotify c1 p1, ANotify c2 p2 -> c1 == c2 /\ p1 == p2
  | ASetState k1 v1 f1, ASetState k2 v2 f2 -> k1 == k2 /\ v1 == v2 /\ f1 == f2
  | AAiTool t1 a1, AAiTool t2 a2 -> t1 == t2 /\ a1 == a2
  | APrint, APrint -> True
  | AConfirm p1 c1 x1, AConfirm p2 c2 x2 ->
    p1 == p2 /\ same_but_closures c1 c2 /\ same_but_closures_opt x1 x2
  | AFocus n1, AFocus n2 -> n1 == n2
  | _, _ -> False

and same_but_closures_opt (#v: Type0) (#b: Type0) (#k: Type0)
                          (x: opt (action v b k)) (y: opt (action v b k))
  : Tot prop (decreases %[x; 1]) =
  match x, y with
  | ONone, ONone -> True
  | OSome a1, OSome a2 -> same_but_closures a1 a2
  | _, _ -> False

and same_but_closures_list (#v: Type0) (#b: Type0) (#k: Type0)
                           (xs: list (action v b k)) (ys: list (action v b k))
  : Tot prop (decreases %[xs; 2]) =
  match xs, ys with
  | [], [] -> True
  | a1 :: r1, a2 :: r2 -> same_but_closures a1 a2 /\ same_but_closures_list r1 r2
  | _, _ -> False

/// Two actions related by `same_but_closures` have the same log-safe
/// description — the description reads the constructor and the
/// author-declared name, never a closure.
let describe_ignores_closures (#v: Type0) (#b: Type0) (#k: Type0)
                              (ax: axioms v b) (x: action v b k) (y: action v b k)
  : Lemma (requires same_but_closures x y)
          (ensures describe ax x == describe ax y) =
  ()

/// Two actions related by `same_but_closures` lower alike at every node
/// id and store — `ui_lower` reads the route, the payload, the encoding
/// and the node id, never a closure slot.
let ui_lower_ignores_closures (#v: Type0) (#b: Type0) (#k: Type0)
                              (ax: axioms v b) (x: action v b k) (y: action v b k)
                              (node_id: string) (s: store v)
  : Lemma (requires same_but_closures x y)
          (ensures ui_lower ax node_id x s == ui_lower ax node_id y s) =
  ()

/// `ui_view` sends `same_but_closures` actions to same-shaped views —
/// the UI witness meets the obligation `blind_to` states, one action
/// pair at a time. Composition needs the induction; every other arm is
/// a leaf or a one-level shape, discharged by the two lemmas above.
let rec ui_view_same_shape (#v: Type0) (#b: Type0) (#k: Type0)
                           (ax: axioms v b) (x: action v b k) (y: action v b k)
  : Lemma (requires same_but_closures x y)
          (ensures same_shape (ui_witness ax) (ui_view x) (ui_view y))
          (decreases %[x; 0]) =
  describe_ignores_closures ax x y;
  match x, y with
  | AChain xs, AChain ys -> ui_view_same_shape_list ax xs ys
  | _, _ ->
    let aux (n: string) (st: store v)
      : Lemma (ui_lower ax n x st == ui_lower ax n y st) =
      ui_lower_ignores_closures ax x y n st
    in
    FStar.Classical.forall_intro_2 aux

and ui_view_same_shape_list (#v: Type0) (#b: Type0) (#k: Type0)
                            (ax: axioms v b) (xs: list (action v b k)) (ys: list (action v b k))
  : Lemma (requires same_but_closures_list xs ys)
          (ensures same_shape_list (ui_witness ax) (ui_view_list xs) (ui_view_list ys))
          (decreases %[xs; 1]) =
  match xs, ys with
  | [], [] -> ()
  | a1 :: r1, a2 :: r2 ->
    ui_view_same_shape ax a1 a2;
    ui_view_same_shape_list ax r1 r2
  | _, _ -> ()

/// **`ui_blind_to_closures` — the witness obligation, DISCHARGED for the
/// UI witness.** This is the half of no-closure-invocation D18 places on
/// the adapter, proved here for the adapter's model.
let ui_blind_to_closures (#v: Type0) (#b: Type0) (#k: Type0) (ax: axioms v b)
  : Lemma (blind_to (same_but_closures #v #b #k) (ui_witness ax)) =
  let aux (x: action v b k) (y: action v b k)
    : Lemma (same_but_closures x y ==> same_shape (ui_witness ax) (ui_view x) (ui_view y)) =
    FStar.Classical.move_requires (ui_view_same_shape ax x) y
  in
  FStar.Classical.forall_intro_2 aux

/// **`run_no_closure`.** Phase 1715's statement, word for word: two
/// actions differing only in the closures they carry produce IDENTICAL
/// outcomes and identical placements. It is now `run_action_blind` — the
/// core's unconditional half — at a witness whose obligation
/// `ui_blind_to_closures` has discharged, so the theorem is again
/// unconditional. The differential host is what carries the claim back
/// to production, and its go-red case commits a fold that invokes a
/// carried closure to show the comparison catches it.
let run_no_closure (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
                   (ax: axioms v b) (ar: arm v p)
                   (node_id: string) (x: action v b k) (y: action v b k) (s: store v) (pl: p)
  : Lemma (requires same_but_closures x y)
          (ensures run ax ar node_id x s pl == run ax ar node_id y s pl) =
  ui_blind_to_closures #v #b #k ax;
  run_action_blind same_but_closures (ui_witness ax) ar node_id x y s pl

// ─── 3. `Chain` is the fold's homomorphism ───────────────────────────

/// Viewing a concatenation is concatenating the views.
let rec ui_view_list_app (#v: Type0) (#b: Type0) (#k: Type0)
                         (xs: list (action v b k)) (ys: list (action v b k))
  : Lemma (ensures ui_view_list (app xs ys) == app (ui_view_list xs) (ui_view_list ys))
          (decreases xs) =
  match xs with
  | [] -> ()
  | _ :: rest -> ui_view_list_app rest ys

/// No arm of the fourteen views as a guard: `ui_view` produces no
/// `VRequire` at any depth. The syntactic fact every pre-1967 UI
/// statement rests on since the halting clause arrived.
let rec ui_view_no_require (#v: Type0) (#b: Type0) (#k: Type0) (a: action v b k)
  : Lemma (ensures not (has_require (ui_view a))) (decreases %[a; 0]) =
  match a with
  | AChain ops -> ui_view_no_require_list ops
  | _ -> ()

and ui_view_no_require_list (#v: Type0) (#b: Type0) (#k: Type0) (ops: list (action v b k))
  : Lemma (ensures not (has_require_list (ui_view_list ops))) (decreases %[ops; 1]) =
  match ops with
  | [] -> ()
  | x :: rest ->
    ui_view_no_require x;
    ui_view_no_require_list rest

/// **`ui_view_no_flow`** (Phase 1976). No arm of the fourteen views as a
/// selection or a repeat: `ui_view` produces no `VChoose` and no
/// `VRepeat` at any depth — the UI tier branches in the TREE and repeats
/// through data binding, so its handlers are straight-line. This is the
/// syntactic fact that makes every pre-1976 UI statement hold unchanged
/// over the widened view: a view without the new shapes folds exactly
/// as before.
let rec ui_view_no_flow (#v: Type0) (#b: Type0) (#k: Type0) (a: action v b k)
  : Lemma (ensures not (has_flow (ui_view a))) (decreases %[a; 0]) =
  match a with
  | AChain ops -> ui_view_no_flow_list ops
  | _ -> ()

and ui_view_no_flow_list (#v: Type0) (#b: Type0) (#k: Type0) (ops: list (action v b k))
  : Lemma (ensures not (has_flow_list (ui_view_list ops))) (decreases %[ops; 1]) =
  match ops with
  | [] -> ()
  | x :: rest ->
    ui_view_no_flow x;
    ui_view_no_flow_list rest

/// **`ui_never_halts`.** The UI tier never halts: a leaf's refusal is a
/// diagnostic and the chain carries on, exactly as before Phase 1967.
/// `fold_no_halting_shape_no_halt` at a witness whose view has no guard
/// (`ui_view_no_require`) and none of Phase 1976's shapes
/// (`ui_view_no_flow`).
let ui_never_halts (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
                   (ax: axioms v b) (ar: arm v p)
                   (node_id: string) (a: action v b k) (s: store v) (pl: p)
  : Lemma (ensures not (fst (run ax ar node_id a s pl)).o_halted) =
  ui_view_no_require a;
  ui_view_no_flow a;
  fold_no_halting_shape_no_halt (ui_witness ax) ar node_id (ui_view a) s pl

/// **`chain_homomorphism`.** Phase 1715's action-level statement:
/// running `AChain (app xs ys)` is running `AChain xs` and then
/// `AChain ys` against the store the first left, with the effects and
/// the diagnostics concatenated in order and the placement threaded
/// through — unconditionally, because nothing at this witness halts. It
/// is `sequence_homomorphism` at the UI witness, through
/// `ui_view_list_app`, with its halting clause discharged by
/// `ui_view_no_require`.
let chain_homomorphism (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
                       (ax: axioms v b) (ar: arm v p)
                       (node_id: string) (xs: list (action v b k)) (ys: list (action v b k))
                       (s: store v) (pl: p)
  : Lemma
      (ensures
        (let (o1, p1) = run ax ar node_id (AChain xs) s pl in
         let (o2, p2) = run ax ar node_id (AChain ys) o1.o_store p1 in
         run ax ar node_id (AChain (app xs ys)) s pl ==
           ({ o_store = o2.o_store;
              o_effects = app o1.o_effects o2.o_effects;
              o_diagnostics = app o1.o_diagnostics o2.o_diagnostics;
              o_halted = false }, p2))) =
  ui_view_list_app xs ys;
  ui_view_no_require_list xs;
  ui_view_no_require_list ys;
  ui_view_no_flow_list xs;
  ui_view_no_flow_list ys;
  let (o1, p1) = fold_many (ui_witness ax) ar node_id (ui_view_list xs) s pl in
  fold_no_halting_shape_no_halt_list (ui_witness ax) ar node_id (ui_view_list xs) s pl;
  fold_no_halting_shape_no_halt_list (ui_witness ax) ar node_id (ui_view_list ys) o1.o_store p1;
  sequence_homomorphism (ui_witness ax) ar node_id (ui_view_list xs) (ui_view_list ys) s pl

// ─── 4. Host-reserved keys are untouched ─────────────────────────────

/// **`reserved_untouched`.** Phase 1715's statement: the store the fold
/// returns agrees with the store it was given at every host-reserved
/// key, for any arm that preserves them. It is `fold_reserved_untouched`
/// at the UI witness, whose reserved predicate is the host's
/// `is_reserved`.
let reserved_untouched (#v: Type0) (#b: Type0) (#k: Type0) (#p: Type0)
                       (ax: axioms v b) (ar: arm v p)
                       (node_id: string) (a: action v b k) (s: store v) (pl: p) (kk: key)
  : Lemma (requires arm_preserves_reserved ax.is_reserved ar /\ ax.is_reserved kk)
          (ensures lookup (fst (run ax ar node_id a s pl)).o_store kk == lookup s kk) =
  fold_reserved_untouched (ui_witness ax) ar node_id (ui_view a) s pl kk
