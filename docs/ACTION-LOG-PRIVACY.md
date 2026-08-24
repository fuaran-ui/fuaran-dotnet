# Action-log privacy — the census

`ActionInvocation.describe` prints an `Action`'s **constructor** and the author-declared name that
identifies it — an endpoint, a channel, a state key, a tool name, a capability id, a node id — and
never a payload **value**. The durable user-action record defaults to that grade and makes
payload-bearing capture a per-host opt-in, "or the first durable write is a keystroke log".

That reasoning was never specific to the durable record. Any site that logs, traces, reports or
renders an `Action` has the same exposure. This document is the census of those sites: what each one
emits, and — where a site emits more than constructor grade — whether that is deliberate and why.

**An unenumerated surface cannot be claimed safe.** The census is the deliverable even where few
sites needed changing; the value is that the list is complete and that the ones left alone are left
alone on the record rather than by omission.

## The end user is not the opt-in party

Say this plainly, because a redaction default reads as though it settles the question and it does
not. The party who opts in to payload-bearing capture is the **host**, at wiring time. Where the
host is user-facing, obtaining the **user's** consent is the host's own obligation, and nothing in
this library discharges it. A redaction default reduces what is captured by accident; it says
nothing about what a host may lawfully or decently capture on purpose.

Retention is the same shape and is likewise not modelled here. An append-only sink is the retention
boundary, so retention is the host's. A policy baked into a wire record is one every host inherits
and none can change.

## Grades

Each site is classified by the most revealing thing it can emit:

| grade | meaning |
|---|---|
| **A** | the constructor only |
| **B** | the constructor plus an author-declared NAME (endpoint / channel / state key / tool / capability / node id) — no user value |
| **C** | a payload VALUE |

Grade B is the target for any log-shaped surface. `Navigate` and `SetState` are the two arms where
the difference between B and C is easy to lose: a route carries a query string and a query string
carries user data, and a state key is author-declared while the value written through it is whatever
a text control captured. Both are handled specifically, not merely covered by "use `describeAction`".

## The designated log-safe describers

| site | grade | note |
|---|---|---|
| `Fuaran.UI.Ops.Abstractions/ActionInvocation.fs` — `describe` | B | the chokepoint. `Navigate` reduced to `routePath`; `SetState` to its key; `Chain` names no constituent |
| `Fuaran.UI.Ops.Abstractions/ActionInvocation.fs` — `routePath` | — | the scrubber: cuts at `?` and at `#` |
| `Fuaran.UI.ServerDriven/Validation.fs` — `describeAction` | B | a pure delegate to `describe`; it exists so this tier takes no renderer dependency |

Everything log-shaped below either reaches one of these or is recorded as an exception.

## The durable user-action record

| site | grade | note |
|---|---|---|
| `ActionInvocation.fs` — `payloadFor` | B / C | `Redacted` yields `None` for all eleven cases; `PayloadBearing` yields the value. **The opt-in, working as designed.** |
| `ActionInvocation.fs` — `record`, `emit` | B / C | the single fan-out to every sink; mode-dependent |
| `ActionInvocation.fs` — `recordDescribed` | B | description-only, no payload slot |
| `Fuaran.UI.OpStream.Sqlite/ActionInvocationSqliteSink.fs` — `RecordActionInvocation` | B / C | where an opted-in payload lands on disk. Scanned for a network surface (see "The scans") |
| `Fuaran.UI.Renderer/Render.fs` — `runActionAs` | B / C | client-side emission point |
| `Fuaran.UI.ServerDriven/Driver.fs` — `recordInvocation` / `recordRejection` | B / C | server-driven emission point; the rejection leg is description-only |

**One amplification is worth naming rather than leaving to be discovered.** The form-submit leg
attaches harvested field values to the submitted `Action.Notify` payload before dispatch. Under
`PayloadBearing`, the recorded payload therefore contains **every form field the user typed**, not
only the payload the author declared. That is a correct consequence of the opt-in rather than a
defect in it — but a host reading "payload-bearing" as "the author's payload" would be wrong, and
this is the sentence that says so.

## Renderer diagnostics

| site | grade | note |
|---|---|---|
| `Runtime.fs` — `ActionDescriptor.describe`, `Navigate` arm | B | **scrubbed to the path.** This label reaches a `Warn` line *and*, through the devtools apply gate, `DenyTelemetry.Reason`, which a host may persist |
| `Runtime.fs` — `ActionDescriptor.describe`, `ApplyTreeOp` arm | C | **deliberately left alone** — see "Deliberately left alone" below |
| `Runtime.fs` — `ActionDescriptor.describe`, other arms | A / B | endpoint, tool, channel, key, node id; `WriteToClipboard` is constructor-only |
| `Runtime.fs` — `DiagnosticRuntime.Navigate` | B | **scrubbed to the path.** This is the default runtime an unconfigured host gets, so its stderr is the likeliest place a query string is captured by something outliving the session |
| `Runtime.fs` — `DiagnosticRuntime`, other members | A / B | endpoint / channel / key / tool / file id; clipboard is constructor-only |
| `Runtime.fs` — `UnprivilegedGuestRuntime.Navigate` | B | **scrubbed to the path** |
| `Runtime.fs` — `UnprivilegedGuestRuntime`, other members | A / B | as above |
| `BrowserRuntime.fs` — `InvokeAiTool` | B | **the tool name only.** It stringified the whole argument bag to the browser console on the *success* path, so it ran on every invocation; an AI-tool argument bag is whatever the user typed, and a console line is what a screen recording or a support bundle captures verbatim |
| `BrowserRuntime.fs` — `Call` / `Notify` / `ReadFileBody` | A / B | endpoint + transport error; channel with the payload withheld; constructor-only |
| `Render.fs` — `runActionCore`, `SetState` / `Invoke` arms | B | state key, capability id; the `valueFrom` resolver message and the args bag are not printed |
| `Render.fs` — `treeStateWriteOutcome`, `applyDispatchGateOutcome` | B | refused state key; the gate label above |
| `Render.fs` — `treeNavigateOutcome`'s `Warn` | C | **deliberately left alone** — see below |
| `Resume.fs` — `interpret` | A | the `$type` discriminator string only |

## Server-driven rejection

| site | grade | note |
|---|---|---|
| `ServerDriven/Validation.fs` — `validate`, `RejectReason.describe` (`DispatchDenied`) | B | via `describeAction` |
| `ServerDriven/FormBuffer.fs` — the two deny legs | B | via `describeAction` |
| `ServerDriven.AspNetCore/Endpoints.fs` — `logReject` | B | for the dispatch-denied class |
| `ServerDriven.WebSocket/Endpoints.fs` — the receive loop | A | connection id and exception message; the raw body is not logged |

**That open finding is now CLOSED** (Phase 787). `RejectReason.PayloadOutOfBounds` used to echo the
user's chosen value into its detail string, which reached the always-on host log — an inbound
**event** value rather than an `Action` payload, so outside this census's subject, but landing in the
same log line through a function whose own doc comment promised no payload values. The three bounds
checks in `Validation.fs` now name the bound that was missed and withhold the value that missed it,
so the reason never carries it and there is no redaction step to forget.

| site | grade | note |
|---|---|---|
| `ServerDriven/Validation.fs` — `boundsCheck`, the select-options leg | B | names the bound; the submitted value is withheld |
| `ServerDriven/Validation.fs` — `boundsCheck`, the unknown-filter-name leg | B | the name is withheld too: an UNMATCHED name is client-supplied text, not an author-declared one |
| `ServerDriven/Validation.fs` — `boundsCheck`, the filter-options leg | B | the filter name MATCHED a declared filter, so it is author-declared and stays; the chosen value goes |

**The distinction that leg-two turns on is worth keeping.** A filter name is grade B when it names a
filter the node declares and grade C when it does not — same field, same type, and the grade depends
on whether the lookup succeeded. Withholding it in the failing branch is not excess caution: that
branch is reached precisely when the string came from the client and matched nothing the author
wrote.

A host that wants the offending value while developing logs the inbound event itself. That is a
deliberate host opt-in at wiring time — the same party, and the same shape, as `ActionCaptureMode`.

Covered by a poison scan in `Fuaran.UI.ServerDriven.Tests/ValidationTests.fs`, on the idiom described
under "The scans": poison in the submitted value, driven through `validate` rather than
hand-constructed, asserted absent from `describe`, and proved non-vacuous by also asserting the
describers still name the node id and the author-declared filter name. **The test it replaced passed
throughout the leak** — it built a reason carrying `'zzz' not among the select's options` by hand and
checked only that the node id survived, so it asserted the leak rather than catching it. Worth
recording as a shape, not just an incident: a describer test that constructs its own input is testing
the formatter, not the pipeline, and only the pipeline knows where the user's value entered.

## Introspection, validation and tooling — clean

| site | grade | note |
|---|---|---|
| `Fuaran.UI.AiTools/Tools.fs` — `extractProps` | — | action slots surface as an opaque `(name, F# type string)` pair; no constructor, no value |
| `Fuaran.UI.AiTools/ResponseRender.fs` — `writeBoxedValue` | — | its `%A` fallback only ever sees a `PropEntry.Value`, which is `None` for every action slot. **Worth watching**: an `extractProps` arm that ever boxed an action would dump it wholesale here with no change at that line |
| `Fuaran.UI.Ops/Introspect.fs`, `Apply.fs` | — | action fields are deliberately absent from the introspection and path surfaces |
| `Fuaran.UI/PreEmitValidate.fs` — the action-derived defects | B | node ids, state keys, endpoints, query names; no value is ever read |
| `Fuaran.UI/WireSurvivability.fs`, `DeadOnDecode.fs` | A | static constructor-name vocabulary; no instance is inspected |
| `Fuaran.UI.Validator/*` | A / B | F# AST source text — union case names parsed from source, not runtime values |
| `Fuaran.UI.Cli` | — | no `Action` reference and no action-derived output |
| `Fuaran.UI.Telemetry.*` — `InteractionTelemetry`, the drift aggregates | — | counts-only; no action is named, not even its constructor |

**One watch worth stating.** `Fuaran.UI.Content/Exemplar.fs` renders validator defects with `%A`
over the defect DU. Those defects are grade B today, but the `%A` is unbounded: a future defect case
that captured an action value would leak there with no edit at that line.

`Fuaran.UI.AiTools/Capabilities.fs` renders an invocation error with `%A` over an error type that
carries the offending argument values, and the string is rendered into the node's error subtree —
i.e. onto the screen. It is the `Binding.Invoke` path rather than `Action.Invoke`, so it is outside
this census's subject, but it is the same value class and is recorded for the same reason.

## Deliberately left alone

Each of these emits a payload value and each stays, for a reason that is about what the surface *is*
rather than about convenience.

- **The canonical wire encoders** — `Generated.fs`'s `encAction` / `encodeActionJson`, and the
  op-stream's canonical JSON. These *are* the wire and the hash-chain identity. Redaction here would
  not be privacy, it would be corruption.
- **The server-rendered resume envelope** (`Fuaran.UI.Renderer.Server/Resume.fs`). Every
  event-bearing node's action is inlined into the served HTML so the page can hydrate. It is
  human-readable in view-source, but it is a *functional payload* the client is about to hold
  anyway — not a log, and not an artefact that outlives the page.
- **Merge-conflict envelopes** (`Fuaran.UI.OpStream.Dag.Merge/TreeMerge.fs`). Canonical node JSON,
  actions included, is handed to the host as a recovery envelope. Scrubbing it would make the
  envelope useless for the one job it has, which is letting a human choose between two versions.
- **`ActionDescriptor.ApplyTreeOp`'s summary** — the raw op JSON in a devtools apply-gate refusal.
  In the in-page REPL that op is the caller's own input being echoed back to the caller. It does
  also reach `DenyTelemetry.Reason`, which is the reason this entry exists rather than being
  silently fine; a host that persists deny telemetry from an in-page REPL should know that.
- **`treeNavigateOutcome`'s `Warn`** (`Render.fs`) — the one raw route left in the renderer, kept
  deliberately and with its reasoning already in the source: a diagnostic a developer reads in their
  own console is a different surface from a log that outlives the session, and the **recorded**
  reason beside it keeps the scrubbed path. Recorded here so that the exception is visible in the
  census rather than only at the call site.
- **`ActionCaptureMode.PayloadBearing`** itself. It is the opt-in, and an opt-in that captured
  nothing would be a lie about what it offers.

## The scans

Two mechanical checks stand behind the claims above, both deliberately crude and deliberately narrow.
Crude, because a clever exfiltration path is not the threat — the threat is a well-meaning
convenience feature. Narrow, because a repo-wide gate is one nobody keeps green, and a gate that is
routinely overridden stops being read.

1. **No network surface on the local-log path** (`Fuaran.UI.OpStream.Tests/ActionLogPrivacyTests.fs`).
   A banned-token scan over the censused files that carry the durable record and the log-safe
   describers. A prose posture note ages badly — the day someone adds "just fetch the log from a
   URL" for convenience, the comment stays true-looking.
2. **Poison, over the censused describers** (same file, and
   `Fuaran.UI.ServerDriven.Tests/ActionLogCensusTests.fs`). Every one of the eleven `Action` cases
   is constructed carrying a distinctive poison string in every payload position, pushed through
   each grade-B describer, and the output asserted free of it.

**Both scans prove they can fail.** The banned-token scan re-runs itself against a token that is
genuinely present, so a refactor that breaks its file discovery fails loudly instead of going quiet.
The poison scan re-runs itself against the payload-bearing mode and asserts the poison **is** found —
so a describer that stopped seeing payloads at all, or a poison fixture that stopped carrying any,
cannot pass by vacuity.
