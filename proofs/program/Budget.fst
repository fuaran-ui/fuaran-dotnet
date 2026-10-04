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

/// Phase 1716 — the interaction budget, modelled in F* and proved.
///
/// # What this module is
///
/// A hand-written model of `Budget.actionCascadeCost` and
/// `Budget.treeCost` (`src/Fuaran.Program.Bounded/Budget.fs`) and of the
/// G2 stage of `BoundedDriver.step` that consumes them
/// (`src/Fuaran.Program.Bounded/BoundedDriver.fs:201-241`), clause for
/// clause. Every definition below names its F# counterpart in the
/// comment above it. The differential host
/// (`tests/Fuaran.Program.Parity.Tests/ProofOracleTests.fs`) runs the
/// EXTRACTION of this module beside production over the trees the
/// bounded driver's own suite drives and over generated trees that
/// straddle the ceiling, and requires the two to return the same
/// integer — that host is the only thing that says this model is about
/// the code that ships.
///
/// [Phase 1715](BoundedFold.fst) proves the other half of WS6.1(c): that
/// the interpreter invokes no closure a tree carries. Bounded CODE and
/// bounded COST are the two halves of running an untrusted tree on
/// shared infrastructure, and 1715's `proofs.json` says in as many words
/// that the budget is out of its theorem. This is that theorem.
///
/// # What is opaque, and why
///
/// Two things, and they are opaque for the two different reasons a model
/// has.
///
///   * **The saturation bound `mx`** — `System.Int32.MaxValue` in
///     production — is a PARAMETER. This model's integers are F*'s,
///     which are unbounded, so the .NET bound is not something the model
///     can be built out of; it is something the host supplies and the
///     theorems are quantified over. That is the honest statement, and
///     it is also the strongest one available: everything below holds
///     for every bound at all, so nothing here is an artefact of the
///     particular width production happens to have.
///   * **The per-node classification** — which `NodeKind` is
///     data-bearing and which of its fields carries the data — reaches
///     the model as a `cost_shape` the differential host projects.
///     `NodeKind` has scores of arms and none of them is this budget's
///     business: `nodeCost` reads exactly four of them and, from each,
///     exactly two numbers. Modelling the union would be modelling the
///     tree vocabulary, and the vocabulary is not what a budget is about.
///     What the model DOES own is the arithmetic performed on those two
///     numbers and the walk that accumulates it — which is what the
///     theorems are about, and what the host cannot supply.
///
/// The admitted branch of `step` is opaque for the first reason again:
/// what the driver does with an event it admits is 1715's subject and
/// the renderer's, and modelling it here would be modelling it twice.
///
/// One payload position is an opaque TYPE, and it is the assumed rung.
/// Production prices a `Binding.Static` payload with `Seq.truncate` over
/// a `seq`, which may be lazy and may be unbounded; a list is neither.
/// `payload` below is that list, and `counted` is the counting cap — so
/// what is proved is that the cap is applied and that the cost it
/// produces is bounded by it, never that a `seq` is finite.
///
/// # The theorems
///
///   * `sat_monotone` — the saturating add and multiply are monotone in
///     both arguments, never answer below zero, never answer above the
///     bound, and agree with ordinary arithmetic everywhere below it.
///     This is the clause the file's own comment stakes the budget on: a
///     cost is a budget comparand, and an overflow that wrapped NEGATIVE
///     would read as cheap and admit the very tree the budget exists to
///     refuse.
///   * `treecost_exact_below_ceiling` — when the walk's answer is at or
///     below the ceiling it is the EXACT cost, equal to what the same
///     walk with no ceiling at all would have returned. The early stop
///     costs nothing in the case where the number is used as a number.
///   * `treecost_strict_above_ceiling` — when the exact cost is above the
///     ceiling the walk's answer is above the ceiling too. The early stop
///     never turns a refusal into an admission, which is the only
///     direction that matters.
///   * `treecost_terminates` — the walk terminates on every finite tree,
///     carried by the `decreases` clause on `walk` rather than by a
///     depth counter, and the number of nodes it visits is bounded BOTH
///     by the size of the tree and by `ceiling + 1`. The second bound is
///     the ceiling acting as fuel: a tree ten thousand times over budget
///     costs the same as one marginally over, which is the claim
///     `treeCost`'s own comment makes.
///   * `breach_pure` — a breach mutates nothing. The session the driver
///     returns is the session it was given, the refusal is a named
///     outcome carrying its reason, and there are no patches and no
///     effects. Never a throw: the model has no exceptions, and the
///     `Tot` effect on `step` is what says production's total signature
///     is not merely a convention.

module Budget

(* ───────────────────────────────────────────────────────────────────
   Small closed types the model owns — declared here rather than taken
   from `FStar.Pervasives.Native` or `FStar.List.Tot` so the extraction
   references `Prims` and nothing else, exactly as `BoundedFold.fst`
   does. `oracle/Prims.fs` is that whole runtime; a model reaching for a
   further name would fail the byte-diff in `check.ps1` rather than
   silently compile against a widened shim.
   ─────────────────────────────────────────────────────────────────── *)

/// F#: `'a option`.
type opt (a: Type0) =
  | ONone : opt a
  | OSome : item: a -> opt a

(* ───────────────────────────────────────────────────────────────────
   SATURATING ARITHMETIC — `Budget.satAdd` / `Budget.satMul`.

   Production computes the sum and the product in `int64` and compares
   against `Int32.MaxValue` before narrowing. Over F*'s unbounded
   integers the widening step is the identity, so the model is the
   comparison alone — and the two agree on every input production can
   actually be handed, because an `int64` sum of two `Int32`s and an
   `int64` product of two `Int32`s both fit in `int64` with room to
   spare. That coincidence is what keeps the assumed rung narrow: the
   model is over a wider domain than production, not a different one.
   ─────────────────────────────────────────────────────────────────── *)

let sat_add (mx: int) (a: int) (b: int) : int =
  if a + b > mx then mx else a + b

let sat_mul (mx: int) (a: int) (b: int) : int =
  if a * b > mx then mx else a * b

/// **`sat_monotone`.** Clause 1 of the theorem, in one statement: both
/// operations are monotone in both arguments, neither ever answers below
/// zero, neither ever answers above the bound, and below the bound both
/// are ordinary arithmetic. Monotonicity is what makes a cost a
/// comparand at all — a bigger tree must not price cheaper — and the
/// non-negativity is the literal thing `Budget.fs`'s own comment says a
/// wrapping add would break.
let sat_monotone (mx: int) (a1: nat) (b1: nat) (a2: nat) (b2: nat)
  : Lemma
      (requires mx >= 0 /\ a1 <= a2 /\ b1 <= b2)
      (ensures
        sat_add mx a1 b1 <= sat_add mx a2 b2 /\
        sat_mul mx a1 b1 <= sat_mul mx a2 b2 /\
        0 <= sat_add mx a1 b1 /\
        0 <= sat_mul mx a1 b1 /\
        sat_add mx a1 b1 <= mx /\
        sat_mul mx a1 b1 <= mx /\
        (a1 + b1 <= mx ==> sat_add mx a1 b1 == a1 + b1) /\
        (a1 * b1 <= mx ==> sat_mul mx a1 b1 == a1 * b1)) =
  // Nonlinear arithmetic is off in the solver by default, so the two
  // multiplication facts are supplied rather than searched for.
  FStar.Math.Lemmas.nat_times_nat_is_nat a1 b1;
  FStar.Math.Lemmas.lemma_mult_le_right b1 a1 a2;
  FStar.Math.Lemmas.lemma_mult_le_left a2 b1 b2

(* ───────────────────────────────────────────────────────────────────
   THE COUNTING CAP — `Budget.maxCountedRows` and the two `staticCount`
   helpers.

   This is the assumed rung. Production's payload is a `seq`, which may
   be lazy and may be unbounded; `payload` below is a LIST, which is
   neither. What is proved is that the cap is applied and what it bounds
   — never that a `seq` is finite.
   ─────────────────────────────────────────────────────────────────── *)

/// F#: `Budget.maxCountedRows`. The model owns the constant rather than
/// taking it as a parameter, and the differential host reads it back out
/// of the extraction, so there is one authority for it on the oracle
/// side and a divergence from production's private literal is a
/// divergence the host reports.
let max_counted_rows : int = 100000

/// F#: what `staticSeqCount` / `staticListCount` read out of a
/// data-bearing spec's `Source` binding — whether it is a
/// `Binding.Static (Some items)` and, if so, the items. A `Query` /
/// `State` / `Transform` binding resolves at render time from the host's
/// own store, so its size is not a property of the untrusted tree and is
/// `PAbsent` here exactly as it is zero there.
type payload =
  | PAbsent : payload
  | PRows : items: list unit -> payload

/// F#: `Seq.truncate maxCountedRows |> Seq.length`, and equally
/// `min maxCountedRows (List.length items)` — production spells the cap
/// twice because a `seq` and a `list` read differently, and the two
/// spellings agree on every finite input, so one function models both.
///
/// Written with an accumulator, which is not a modelling choice but a
/// runtime one: the extraction of this function is compiled and RUN by
/// the differential host, the cap is a hundred thousand, and a
/// non-tail-recursive count would exhaust the stack on a payload the
/// budget is specifically meant to price. A model whose extraction
/// cannot be run is not a differential.
let rec counted_from (cap: int) (xs: list unit) (acc: nat) : Tot nat (decreases xs) =
  if cap <= 0 then acc
  else
    match xs with
    | [] -> acc
    | _ :: rest -> counted_from (cap - 1) rest (acc + 1)

let counted (cap: int) (xs: list unit) : nat = counted_from cap xs 0

let static_count (cap: int) (p: payload) : nat =
  match p with
  | PAbsent -> 0
  | PRows items -> counted cap items

(* ───────────────────────────────────────────────────────────────────
   THE NODE — what `Budget.nodeCost` reads out of a `NodeKind`.

   Four kinds are data-bearing and the rest cost one. `Chart` and
   `DataGrid` weight a row count by a field/column count; `Map` and
   `Sparkline` weight nothing. The classification is the host's (see the
   header); the arithmetic and the tree walk are the model's.
   ─────────────────────────────────────────────────────────────────── *)

type cost_shape =
  /// Every kind that is not data-bearing. F#: the `| _ -> 1` arm.
  | SPlain : cost_shape
  /// F#: `NodeKind.Chart` (rows × `YFields`) and `NodeKind.DataGrid`
  /// (rows × `Columns`). `weights` is the LENGTH production takes, not
  /// the list — the fields themselves are never read.
  | SWeighted : rows: payload -> weights: int -> cost_shape
  /// F#: `NodeKind.Map` and `NodeKind.Sparkline`.
  | SRows : rows: payload -> cost_shape

/// F#: `Node<'a>`, projected onto its cost shape and its children. The
/// children are the MODEL's, not an axiom's: an opaque child arrow would
/// leave the walk's termination unprovable, and termination is one of the
/// things being proved.
type nd =
  | Nd : shape: cost_shape -> kids: list nd -> nd

/// F#: `Introspect.getChildren`, with its `None` read as the empty list
/// — production's `match` does nothing on `None`, which is what pushing
/// nothing means.
let kids (n: nd) : list nd =
  match n with
  | Nd _ ks -> ks

/// F#: `Budget.nodeCost` — the render cost of ONE node, excluding its
/// children. `max 1 (List.length ...)` is spelled out: a data-bearing
/// node with no declared field still costs its rows.
let node_cost (mx: int) (cap: int) (n: nd) : int =
  match n with
  | Nd SPlain _ -> 1
  | Nd (SWeighted rows weights) _ ->
    sat_add mx 1 (sat_mul mx (static_count cap rows) (if weights < 1 then 1 else weights))
  | Nd (SRows rows) _ -> sat_add mx 1 (static_count cap rows)

/// Every node costs at least one and never more than the bound. Both
/// halves are load-bearing below: the lower bound is what makes the
/// ceiling act as fuel, and the upper bound is what keeps the running
/// total inside the range the saturating add is monotone on.
let node_cost_bounds (mx: int) (cap: int) (n: nd)
  : Lemma (requires mx >= 1) (ensures 1 <= node_cost mx cap n /\ node_cost mx cap n <= mx) =
  match n with
  | Nd SPlain _ -> ()
  | Nd (SWeighted rows weights) _ ->
    sat_monotone mx (static_count cap rows) (if weights < 1 then 1 else weights)
                    (static_count cap rows) (if weights < 1 then 1 else weights)
  | Nd (SRows rows) _ -> ()

(* ───────────────────────────────────────────────────────────────────
   THE SIZE MEASURE.

   `treeCost` is ITERATIVE in production — an explicit stack, because a
   recursive walk would be bounded by the caller's stack rather than by
   the budget, so the function whose whole job is to bound a tree's cost
   would itself be what an over-deep tree could kill. The model keeps the
   stack, so it keeps the ORDER, which is what lets the differential host
   compare the exact integer rather than only the verdict: above the
   ceiling the answer depends on which nodes were visited first.

   A stack-shaped walk is not structurally recursive on anything, so it
   needs a measure. This is it: the total number of nodes still pending.
   Popping one node and pushing its children takes the measure down by
   exactly one, whatever the tree's shape.
   ─────────────────────────────────────────────────────────────────── *)

let rec node_size (n: nd) : Tot nat (decreases %[n; 0]) =
  match n with
  | Nd _ ks -> 1 + forest_size ks

and forest_size (ns: list nd) : Tot nat (decreases %[ns; 1]) =
  match ns with
  | [] -> 0
  | n :: rest -> node_size n + forest_size rest

let node_size_kids (n: nd)
  : Lemma (ensures node_size n == 1 + forest_size (kids n)) [SMTPat (node_size n)] =
  ()

/// F#: `for kid in kids do pending.Push kid` — pushing a child list onto
/// a stack one at a time, so the LAST child ends up on top. Written as
/// its own function rather than as `append (reverse kids) rest` for the
/// same reason the model owns `opt`: one definition, one size lemma, and
/// nothing from outside `Prims`.
let rec push_all (ks: list nd) (stack: list nd) : Tot (list nd) (decreases ks) =
  match ks with
  | [] -> stack
  | k :: rest -> push_all rest (k :: stack)

let rec push_all_size_aux (ks: list nd) (stack: list nd)
  : Lemma
      (ensures forest_size (push_all ks stack) == forest_size ks + forest_size stack)
      (decreases ks) =
  match ks with
  | [] -> ()
  | k :: rest -> push_all_size_aux rest (k :: stack)

/// The same fact, carrying the trigger. Split in two because a `Lemma`
/// takes its SMT pattern where a recursive one takes its `decreases`,
/// and one declaration cannot hold both.
let push_all_size (ks: list nd) (stack: list nd)
  : Lemma
      (ensures forest_size (push_all ks stack) == forest_size ks + forest_size stack)
      [SMTPat (push_all ks stack)] =
  push_all_size_aux ks stack

(* ───────────────────────────────────────────────────────────────────
   THE WALK — `Budget.treeCost`.

   Production's loop condition is `pending.Count > 0 && total <= ceiling`
   and its return is whichever way it exits, so the two tests are read in
   that order here and the accumulator is returned on either.
   ─────────────────────────────────────────────────────────────────── *)

let rec walk (mx: int) (cap: int) (ceiling: int) (pending: list nd) (running: int)
  : Tot int (decreases (forest_size pending)) =
  if running > ceiling then running
  else
    match pending with
    | [] -> running
    | cur :: rest ->
      walk mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

/// F#: `Budget.treeCost` — the whole of it. One node on the stack, a
/// zero accumulator, and the walk.
let tree_cost (mx: int) (cap: int) (ceiling: int) (root: nd) : int =
  walk mx cap ceiling [ root ] 0

(* ───────────────────────────────────────────────────────────────────
   THE CASCADE COST — `Budget.actionCascadeCost`.

   The function makes exactly one distinction about an action: is it a
   `Chain`, and if so what does it contain. Every other arm reaches a
   wildcard worth one. So the model carries that distinction and no
   other, and the fourteen arms of the closed union — which
   `BoundedFold.fst` does carry, one for one — are not this model's
   subject.

   NOT saturating, deliberately: production's is `List.sumBy` over `int`,
   and modelling it as saturating would be modelling a function that does
   not exist. What bounds it in production is the `Int32` range, which
   this model is wider than — the assumed rung, stated in `proofs.json`.
   ─────────────────────────────────────────────────────────────────── *)

type act =
  | ALeaf : act
  | AChain : ops: list act -> act

let rec action_cascade_cost (a: act) : Tot int (decreases %[a; 0]) =
  match a with
  | AChain ops -> cascade_sum ops
  | ALeaf -> 1

and cascade_sum (ops: list act) : Tot int (decreases %[ops; 1]) =
  match ops with
  | [] -> 0
  | x :: rest -> action_cascade_cost x + cascade_sum rest

(* ───────────────────────────────────────────────────────────────────
   THE G2 GATE — `BoundedDriver.step`, from the validation to the fold.
   ─────────────────────────────────────────────────────────────────── *)

/// F#: `InteractionBudget`.
type budget = {
  b_max_actions: int;
  b_max_nodes: int;
}

/// F#: `BoundedSession`, projected onto what the gate reads and what
/// clause 3 says is untouched. The store is an opaque TYPE rather than a
/// modelled map, and that is the strongest form the claim has: the gate
/// cannot have written anything it cannot even look at.
type session (v: Type0) = {
  se_store: v;
  se_node_count: int;
}

/// F#: `BoundedStepOutput`, projected onto what clause 3 constrains. The
/// patch and effect lists reach the model as their LENGTHS, because "and
/// there are none" is the whole of what is claimed about them, and
/// `Rejected` as the reason it carries — the refusal is a named outcome,
/// so the name is the thing to carry.
type step_output = {
  so_patches: int;
  so_effects: int;
  so_rejected: opt string;
}

/// F#: everything `step` does with an event the budget ADMITS —
/// interpret, re-resolve, diff, lower. Opaque, and for the reason the
/// header gives: the fold is Phase 1715's subject and the rest is the
/// renderer's. Nothing below depends on what it answers.
noeq type admitted_branch (v: Type0) = {
  run: session v -> string -> act -> (session v & step_output);
}

/// F#: the two `sprintf` templates the breach carries. Modelled rather
/// than axiomatised, because the differential host compares the reason
/// string verbatim, exactly as `BoundedFold.fst` models the log-safe
/// action description for the same reason.
let budget_message (what: string) (cost: int) (cap_name: string) (cap: int) : string =
  strcat
    what
    (strcat
      " "
      (strcat
        (string_of_int cost)
        (strcat " exceeds " (strcat cap_name (strcat " " (string_of_int cap))))))

let refusal (reason: string) : step_output =
  { so_patches = 0; so_effects = 0; so_rejected = OSome reason }

/// F#: `BoundedDriver.step`'s G2 stage. The cascade cost is priced
/// first, then the tree cost the session was built with — production's
/// order, and it is observable, because the two refusals carry different
/// reasons.
let step (#v: Type0)
         (bud: budget) (ab: admitted_branch v)
         (sess: session v) (node_id: string) (a: act)
  : session v & step_output =
  let cost = action_cascade_cost a in
  if cost > bud.b_max_actions then
    sess, refusal (budget_message "action cascade cost" cost "MaxActions" bud.b_max_actions)
  else if sess.se_node_count > bud.b_max_nodes then
    sess, refusal (budget_message "tree cost" sess.se_node_count "MaxNodes" bud.b_max_nodes)
  else ab.run sess node_id a

/// F#: the disjunction of the two `if` guards above.
let breached (#v: Type0) (bud: budget) (sess: session v) (a: act) : bool =
  action_cascade_cost a > bud.b_max_actions || sess.se_node_count > bud.b_max_nodes

/// **`breach_pure`.** Clause 3. A breached step returns the session it
/// was given — not an equal one, the SAME one — with no patches, no
/// effects, and a named refusal. And it returns: the `Tot` effect on
/// `step` is what makes "never a throw" a thing the prover checks rather
/// than a thing a reader must notice, since a model with no exceptions
/// cannot express the alternative.
///
/// Quantified over EVERY admitted branch, which is what makes it a
/// statement about the gate: whatever the driver would have done to the
/// session had the budget admitted the event, a breach does none of it.
let breach_pure (#v: Type0)
                (bud: budget) (ab: admitted_branch v)
                (sess: session v) (node_id: string) (a: act)
  : Lemma
      (requires breached bud sess a)
      (ensures
        (let (sess', out) = step bud ab sess node_id a in
         sess' == sess /\
         out.so_patches == 0 /\
         out.so_effects == 0 /\
         OSome? out.so_rejected)) =
  ()

(* ───────────────────────────────────────────────────────────────────
   THE THEOREMS ABOUT THE WALK

   Everything from here on is ghost or a lemma: the unbounded walk and
   the step counter carry `noextract_to "FSharp"` and the lemmas are
   erased by the extractor, so the oracle the differential host runs is
   exactly the definitions above.
   ─────────────────────────────────────────────────────────────────── *)

/// The same walk with NO ceiling — the naive ordering `treeCost`'s
/// comment describes and rejects: price the whole tree, then compare.
/// It exists here only to be the thing the real walk is measured
/// against, which is what "exact" and "strictly greater" are statements
/// about.
[@@ noextract_to "FSharp"]
let rec walk_free (mx: int) (cap: int) (pending: list nd) (running: int)
  : Tot int (decreases (forest_size pending)) =
  match pending with
  | [] -> running
  | cur :: rest ->
    walk_free mx cap (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

[@@ noextract_to "FSharp"]
let exact_cost (mx: int) (cap: int) (root: nd) : int =
  walk_free mx cap [ root ] 0

/// The unbounded walk only ever grows its accumulator, and never past
/// the bound. This is the invariant everything else rests on, and it is
/// exactly where `sat_monotone`'s non-negativity earns its place: an add
/// that wrapped would break this line and every theorem below it.
let rec walk_free_bounds (mx: int) (cap: int) (pending: list nd) (running: int)
  : Lemma
      (requires mx >= 1 /\ 0 <= running /\ running <= mx)
      (ensures (let r = walk_free mx cap pending running in running <= r /\ r <= mx))
      (decreases (forest_size pending)) =
  match pending with
  | [] -> ()
  | cur :: rest ->
    node_cost_bounds mx cap cur;
    walk_free_bounds mx cap (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

/// Below the ceiling the two walks are the same function.
let rec walk_agrees_below (mx: int) (cap: int) (ceiling: int) (pending: list nd) (running: int)
  : Lemma
      (requires mx >= 1 /\ 0 <= running /\ running <= mx)
      (ensures
        (walk_free mx cap pending running <= ceiling ==>
          walk mx cap ceiling pending running == walk_free mx cap pending running))
      (decreases (forest_size pending)) =
  match pending with
  | [] -> ()
  | cur :: rest ->
    walk_free_bounds mx cap pending running;
    node_cost_bounds mx cap cur;
    walk_agrees_below mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

/// Above the ceiling the early stop never loses the breach.
let rec walk_strict_above (mx: int) (cap: int) (ceiling: int) (pending: list nd) (running: int)
  : Lemma
      (requires mx >= 1 /\ 0 <= running /\ running <= mx)
      (ensures
        (walk_free mx cap pending running > ceiling ==> walk mx cap ceiling pending running > ceiling))
      (decreases (forest_size pending)) =
  if running > ceiling then ()
  else
    match pending with
    | [] -> ()
    | cur :: rest ->
      node_cost_bounds mx cap cur;
      walk_strict_above mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

/// **`treecost_exact_below_ceiling`.** An answer at or below the ceiling
/// is the exact cost. This is the half that says the number is usable as
/// a number rather than only as a verdict — the session records it as
/// `NodeCount` and prices later events against it, so an answer that was
/// merely "not more than the ceiling" would be a different contract.
let treecost_exact_below_ceiling (mx: int) (cap: int) (ceiling: int) (root: nd)
  : Lemma
      (requires mx >= 1 /\ tree_cost mx cap ceiling root <= ceiling)
      (ensures tree_cost mx cap ceiling root == exact_cost mx cap root) =
  walk_strict_above mx cap ceiling [ root ] 0;
  walk_agrees_below mx cap ceiling [ root ] 0

/// **`treecost_strict_above_ceiling`.** A tree whose exact cost is above
/// the ceiling is priced above the ceiling. This is the half the budget
/// actually depends on: the early stop is an optimisation, and an
/// optimisation that could turn a refusal into an admission would be the
/// whole defect.
let treecost_strict_above_ceiling (mx: int) (cap: int) (ceiling: int) (root: nd)
  : Lemma
      (requires mx >= 1 /\ exact_cost mx cap root > ceiling)
      (ensures tree_cost mx cap ceiling root > ceiling) =
  walk_strict_above mx cap ceiling [ root ] 0

(* ───────────────────────────────────────────────────────────────────
   TERMINATION, AND THE WORK IT TAKES.

   `walk` is `Tot`, so it terminates: that is not a lemma below, it is
   the `decreases (forest_size pending)` clause on the definition itself,
   discharged when the module checks. What the lemmas add is the BOUND —
   and the second of the two is the interesting one, because it is
   bounded by the ceiling rather than by the tree.
   ─────────────────────────────────────────────────────────────────── *)

/// `walk` with the accumulator replaced by a pop counter, clause for
/// clause: the same guards in the same order and the same recursive
/// call, so it counts the iterations production's `while` performs.
[@@ noextract_to "FSharp"]
let rec walk_steps (mx: int) (cap: int) (ceiling: int) (pending: list nd) (running: int)
  : Tot nat (decreases (forest_size pending)) =
  if running > ceiling then 0
  else
    match pending with
    | [] -> 0
    | cur :: rest ->
      1 + walk_steps mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

let rec walk_steps_le_size (mx: int) (cap: int) (ceiling: int) (pending: list nd) (running: int)
  : Lemma
      (ensures walk_steps mx cap ceiling pending running <= forest_size pending)
      (decreases (forest_size pending)) =
  if running > ceiling then ()
  else
    match pending with
    | [] -> ()
    | cur :: rest ->
      walk_steps_le_size mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

/// The ceiling as fuel. Every node costs at least one, so every
/// iteration moves the accumulator at least one closer to the ceiling —
/// and once past it the walk stops. `ceiling < mx` is the side
/// condition, and it is not a technicality: at `ceiling = mx` the
/// accumulator saturates at a value the guard still admits, which is
/// precisely `InteractionBudget.unlimited`, where walking the whole tree
/// is the intended behaviour rather than a bound anyone wanted.
let rec walk_steps_bounded (mx: int) (cap: int) (ceiling: int) (pending: list nd) (running: int)
  : Lemma
      (requires mx >= 1 /\ ceiling < mx /\ 0 <= running /\ running <= mx)
      (ensures
        walk_steps mx cap ceiling pending running <= (if running > ceiling then 0 else ceiling + 1 - running))
      (decreases (forest_size pending)) =
  if running > ceiling then ()
  else
    match pending with
    | [] -> ()
    | cur :: rest ->
      node_cost_bounds mx cap cur;
      walk_steps_bounded mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur))

/// **`treecost_terminates`.** The walk terminates on every finite tree —
/// the `decreases` clause on `walk`, discharged where it is written —
/// and visits at most as many nodes as the tree has AND at most
/// `ceiling + 1`. The second bound is what `treeCost`'s comment claims
/// in prose: a tree ten thousand times over budget costs the same as one
/// marginally over, so the walk cannot itself become the unbounded work
/// it exists to refuse.
let treecost_terminates (mx: int) (cap: int) (ceiling: int) (root: nd)
  : Lemma
      (requires mx >= 1 /\ 0 <= ceiling /\ ceiling < mx)
      (ensures
        walk_steps mx cap ceiling [ root ] 0 <= ceiling + 1 /\
        walk_steps mx cap ceiling [ root ] 0 <= node_size root) =
  walk_steps_bounded mx cap ceiling [ root ] 0;
  walk_steps_le_size mx cap ceiling [ root ] 0
