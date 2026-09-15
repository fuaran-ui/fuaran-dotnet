<!-- SPDX-License-Identifier: Apache-2.0 -->
# `proofs/` — the proof tier

This directory holds an **F\* proof leg** over this repository's own wire vocabulary: a pinned
prover, three models, a claims ladder, and a script that checks each model from a cold cache and
holds the generated ones to a fresh generation from `src/Fuaran.UI.Idl/idl.json`.

Run it:

```powershell
pwsh ./proofs/check.ps1            # one cold-cache verification of every model
pwsh ./proofs/check.ps1 -Runs 3    # what CI runs
pwsh ./proofs/check.ps1 -SkipOracleHost   # leave the host families to the ordinary gate
```

The first run downloads the pinned prover (~200 MB, hash-verified against `fstar-pin.json`) into
`proofs/.fstar/`, which is gitignored. `$env:FSTAR_HOME` pointing at a matching release is used
instead when it is set.

## What it proves

**One theorem, stated carefully, and its scope is the whole of the care:**

> For every value of every **modelled** kind of this repository's wire vocabulary, decoding its
> encoding returns that value — at every depth, through every list, map, optional member and
> omit-at-default — and every input reaches exactly one of `Ok` / `Error`.

The models it is proved over are **generated** from `src/Fuaran.UI.Idl/idl.json` by
`Fuaran.Core.Idl.Codegen`'s F\* target — the same engine that generates the tier's structural layer
— so a kind added to the vocabulary re-proves itself at the next `check.ps1` rather than waiting
for someone to write a clause. `proofs.json` at the repository root is the claims ladder in a form
a tool can read, and the `Proofs.Ladder` family holds every row of it to this tree.

## What it does NOT prove

The list is longer than the claim, which is the honest shape for a proof tier.

- **It says nothing about any host's decoder.** Every conformant host — the .NET tier here, and
  the TypeScript, Python, Go and Rust tiers — ships a hand-written, tuned decoder, and each is
  certified against the shared wire-format conformance corpus, not against this. What is modelled
  here is the decoder the F\* target derives from the vocabulary. The two are held to the same
  specification and neither is evidence about the other.
- **It says nothing about the twenty-three kinds outside the proof vocabulary.** Twenty of
  forty-three are modelled. The emitted header of `Vocabulary.fst` names every one of the others
  with its reason, and "Coverage" below is the decision and the measurement behind it.
- **It does not characterise what the decoder ACCEPTS.** The round trip covers one direction:
  everything the encoder can produce is read back exactly. The converse — `Ok? (dec el) == wf el`,
  an independent statement of the accept set — is a **named halt**, inherited from the generator
  and repeated here because it is the first thing a reader assumes. The only acceptance predicate
  the emitter could generate from the same walk is the decoder's own accept set restated clause for
  clause, and a theorem relating a definition to a second rendering of itself is true and empty. A
  characterisation worth the name needs a schema-shaped predicate stated independently, which is a
  model-sized artefact of its own and is not in this phase.
- **It is not a second proof engine and not a second pin.** The engine
  (`proofs/kit/check-proof-leg.ps1`), the prover pin, the runtime shims and `WireDecode.fst` are
  **copies** of the kit's, declared in this repository's `copies.json`, so the estate's copy
  registry names a copy that has drifted before anybody meets a red gate they did not cause.
- **Nothing generated here ships in a package.** No `Fuaran.UI.*` assembly gains code, no build
  gains a generation step, and nothing at runtime reads any of it.

## Coverage — the decision, and the measurement that decided it

Coverage is `FStarTarget.proofKinds`: **every kind the target can express that introduces no
declared type beyond the node envelope's own closure**. On this vocabulary that is **20 of 43
kinds**. It is a RULE rather than a list, which is the property that matters — a kind added over
types the model already carries enters at the next regeneration and re-proves itself, and a kind
that brings a new object of its own is named in the emitted header with what it would add.

The twenty-three kinds outside it are of **two different sorts, and the header tells them apart**:

- **One is a genuine boundary.** `Tabs` is refused by the target outright: `Tabs.activeIndex`
  declares the default `VInt 0`, the model's numeric carriers are **opaque** (`num` / `flt`, as in
  `WireDecode`), and an opaque carrier has no literal to spell a numeric default with. The Core-side
  ask is to give the target a way to model a declared numeric default against an opaque carrier.
  **It is deliberately NOT closed from this side**: the alternative — declaring a renderable default
  on `Tabs.activeIndex` — would change a wire-visible omit-at-default value to make a proof
  convenient, which is the tail wagging the dog.
- **Twenty-two are held out by a cost control**, each named with the declared types it would add.

### The exhaustive set was the instruction, and it is REFUTED

The phase was written to decide coverage exhaustively over the expressible set — every kind the
target can express, which here is 42 of 43. It is not reachable at this vocabulary's scale, and the
gap is not close. Generated both ways from this repository's own `idl.json`, on the `Fuaran.Core`
0.25.0 target, 2026-09-15:

| selection | kinds | `Vocabulary.fst` | `VocabularyProofs.fst` | lemma heads | presence-pattern lemmas |
|---|---|---|---|---|---|
| `proofKinds` (adopted) | 20 of 43 | 1,630 lines / 177 KB | 5,939 lines / 573 KB | 682 | 548 |
| exhaustive expressible | 42 of 43 | 4,470 lines / **5.98 MB** | 713,272 lines / **114 MB** | **71,722** | **71,316** |

The mechanism is `fuaran-core#168`'s per-kind lemma shape, which is what made the round trip
discharge at all: a constructor with *k* conditional members is split into 2^*k* presence-pattern
lemmas so that no query carries more than one constructor's object shapes. That trade is excellent
at *k* = 2 and catastrophic at *k* = 16 — `DataGrid` alone has sixteen conditional members and
contributes 65,536 lemmas, each checked three times over varying seeds. The exhaustive model also
emits a single line of **5,072,945 characters**, which is by itself a sufficient reason not to
commit it.

Two independent corroborations were checked before the measurement was believed, because a
measurement whose falsifier you cannot name is not a measurement. `FStarTarget.proofKinds`'s own
docstring records that the whole expressible set of **this** vocabulary did not finish a single
check in twenty-five minutes; and `fuaran-core#150` measured the emitted proof script at the
`proofKinds` selection of this vocabulary *not discharging* under the pre-168 shape — a 65-goal node
query failing a `--quake` seed, a 2^*k* blow-up in the widest kind's arm — and committed the model
alone. The falsifier for the table above is direct: regenerate with the exhaustive selector and the
emitted file sizes are either what the table says or they are not.

**What was NOT concluded.** `fuaran-core#173` chose the exhaustive set for the engine's own
certification vocabularies, and was right to: there the node envelope is two scalars, so
`proofKinds` kept one kind of five and dropped the four carrying the type-model remainder. That is
the same rule meeting a vocabulary two orders of magnitude smaller, and the conclusion does not
transfer in either direction. The rule is the adopter's instrument at the scale it was measured at —
which is the sentence `fuaran-core#173` itself left for this phase to test, and it holds.

**When to widen it.** When a measurement says so and not before: regenerate, read the lemma count,
and bump the budget in `modules.json` as a recorded act. A kind-by-kind widening is expressible
today (the selector is a function of the IDL), and the cheapest wins are the eight kinds that add a
single declared type each.

## Running it

- **`-Runs 3` is the CI claim made literal** — three cold-cache verifications of every model, with
  every SMT query proved three times over varying seeds (`--quake 3`) and every `assume` / `admit`
  reported as an error.
- **The cache is per invocation** (`proofs/obj/cache-<pid>`), created and removed by the leg, so a
  second run in the same worktree cannot quietly falsify "cold cache".
- **Each module is measured against a budget and a FLOOR** declared in `modules.json`. An overshoot
  is a warning: prover time varies by machine and by load, so a budget is a smoke detector.
  An **undershoot fails the leg on the spot**, because a module that verified faster than it can
  possibly verify has not told you it is fast — it has told you the measuring apparatus is broken,
  and everything measured after it shares that apparatus. `-NoFloor` is the deliberate opt-out.
- **`VocabularyProofs` is the leg's whole cost**, by an order of magnitude. Read its number first.

## What is not here, and why

**No oracle project, and no extracted F\#.** An oracle exists so a differential can run the
extracted model beside the **production** code over the same inputs. None of the three models has
production code on this side to run beside — `WireDecode` models combinators this repository does
not ship, and the generated pair models a decoder a generator emits into a consuming host rather
than one any package here contains. Extracting them anyway would commit generated F\# that nothing
compiles, calls or compares, which is what an oracle is meant to be the opposite of. All three are
therefore `$proofOnly` in `check.ps1`.

The exemption is narrow and it is not a hole: what an extraction diff buys for an extracted model —
*the artefact is the model, byte for byte* — the generated pair gets from the **generation diff**
one level further up, and `WireDecode` gets from `copies.json`.

`proofs/oracle/Prims.fs`, `proofs/oracle/FStar_Pervasives_Native.fs` and
`proofs/kit/templates/oracle.fsproj.template` are carried and declared anyway. That is a **decision,
not residue**: they are the kit's runtime floor, they GROW as models reach further into F\*'s
library, and a copy of them goes stale by the source moving rather than by anyone editing the copy.
Declared, the estate sweep keeps them current for nothing; undeclared, the day a model with
production code beside it lands here they would be a year behind and the first extraction would fail
for a reason nobody could attribute.

## The files

| File | What it is |
|---|---|
| `check.ps1` | The caller: this repository's three declarations — the models, the extraction exemption, the host families. The engine is `kit/check-proof-leg.ps1`. |
| `kit/` | The proof kit, copied by declaration. `check-proof-leg.ps1` is the engine; `templates/` are the shapes this repository's own files were cut from, kept verbatim so their drift is named — **except `templates/proofs.json`, which is deliberately NOT carried**, see below. |
| `fstar-pin.json` | The pinned prover release and its hash. A **copy** — the pin is one number in the kit's repository. |
| `WireDecode.fst` | The decode-combinator model the generated model opens. A **copy**. |
| `Vocabulary.fst` | **GENERATED** from `src/Fuaran.UI.Idl/idl.json`: the vocabulary's types, its discriminated encoder, its tag-dispatch decoder. |
| `VocabularyProofs.fst` | **GENERATED** from the same walk: the round trip and totality over `Vocabulary`. |
| `modules.json` | What the leg costs, per module: the budget (a smoke detector) and the floor (a gate), with the seeding rule for each. |
| `oracle/` | The kit's two runtime shims. Unused today — see "What is not here". |
| `../proofs.json` | The claims ladder. Checked against this tree by `Proofs.Ladder`. |

### One template is deliberately not carried

`templates/proofs.json`, the claims-ladder template, is **not** copied into this repository and has
no `copies.json` record. It is not a judgement about the template: the estate's `proofs` registry
walks **any** `proofs.json` under the workspace root, so a verbatim copy of the template inside an
adopter is read as a claims ladder and reports a phantom repository `<Your-Repo>` with two
`RM-PROOF-EVIDENCE-MISSING` warnings — a `proved` row citing `proofs/<Model>.fst` and a `policy` row
citing `proofs/README.md`, neither of which exists — on every estate sweep, for ever. Measured
2026-09-15: `roadmapctl proofs <workspace-root>` reports exactly that pair today, from the kit's own
copy, and an adopter carrying a second copy doubles it.

The template's content is preserved where it belongs — in this repository's own `../proofs.json`,
which was cut from it and which keeps its `$levels` block verbatim, since the CLOSED level set is
the part a ladder must not widen. The finding belongs to the kit rather than to this adopter, and
is reported upward as such: the fix is on the kit's side (a name the registry does not walk, or a
marker the registry skips), not a local workaround here.

## Regenerating

```powershell
dotnet run --project src/Fuaran.UI.Idl.Tests -- --emit-fstar
```

The one sanctioned write path for `Vocabulary.fst` and `VocabularyProofs.fst`. Both are committed,
and the `Proofs.Vocabulary` family holds each to a fresh generation from the committed `idl.json` —
a vocabulary that moves without a regeneration is **vocabulary drift**, and that family is where it
is named. Do not hand-edit either file; a hand edit that happens to verify is a theorem about a
document this repository does not send.
