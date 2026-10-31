# The page document

A **page document** is a stored page: a Fuaran UI tree, a sidecar of the **requests** its data sources name,
and the **provenance** of the document. A host stores it, verifies it and renders it without compiling
anything, because every number the page shows comes from a request the host resolves at render, never from
the tree. Beside it sits a derived **demanded document** — what the page can ever reach — which a signer signs
and a verifier recomputes.

The normative text is the specification's §32 (`WIRE_FORMAT.md` in the specification repository), and its
`page-document/` corpus family is the executable specification. This page is the .NET surface.

Phase 2522. Package `Fuaran.UI`; FSharp.Core, `Fuaran.Core.*`, `Fuaran.Compute.DataFrame` and
`Fuaran.Program.Bounded`; Fable-clean.

## The document

```fsharp
type PageRequest = { Kind: string; Payload: JVal; DependsOn: string list }
type PageProvenance = { SeededFrom: string option; OpsHeads: Map<string, string>; Author: string; At: string }
type PageDocument = { Tree: Node<obj>; Requests: Map<string, PageRequest>; Provenance: PageProvenance }
```

A data-bearing node names a request by binding its source as a `Binding.Query` on the request's key:

```json
{ "id": "revenue-today", "kind": { "$type": "Metric", "label": "Revenue today",
                                    "value": { "$type": "Query", "name": "revenue-today" } } }
```

| Call | What it does |
|---|---|
| `PageDocument.decode json` | Reads a document: one version, every undeclared member refused, each declared request kind's fields checked, then the binding rule. `Error (PageRefusal.Malformed e)` carries Core's `DecodeError` (code + path); `Error (PageRefusal.Unbound defects)` names every `DanglingRequestKey` (a query naming no request) and `UnnamedRequest` (a request no query names). |
| `PageDocument.encode doc` | The canonical bytes (`Rendering.CrossHost`). A canonical document round-trips byte-identically. |
| `PageDocument.validate doc` | What `decode` would refuse, for a document built in code. |
| `PageDocument.parameterDependents doc` | Parameter name to the request keys whose `dependsOn` names it — the map a selector change re-resolves by. |
| `PageDocument.treeHash` / `requestsHash` | The two `sha256:` content addresses a signature binds. |
| `PageRequest.reach request` | What a request of a declared kind reads: `presentation` (`tableId`, `metrics`, optional `subjectLevel`) and `pipeline` (`tableId`, `metrics`, `pipeline`). `Ok None` for any other kind, which is carried and never read. |

## The demanded projection

```fsharp
DemandedPage.ofDocument : PageDocument -> Result<DemandedPage, PageRefusal>
DemandedPage.check      : coverage: DemandedPage -> demanded: DemandedPage -> Result<unit, DemandedPage>
```

`ofDocument` derives the tables, metrics and subject levels the requests read, the pipeline verbs they run,
the node kinds the tree uses, the capabilities (`Mount` tags, `Invoke` ids) and endpoints (`Call`) the tree
names, and the request kinds the sidecar holds. `encode` / `decode` are its canonical wire form. `check` holds
a page to a host's declared coverage, itself written as a demanded document, and answers the **excess** in the
same vocabulary — every table, metric, level, verb, kind, capability, endpoint and request kind beyond it.

## Signing and verifying

```fsharp
SignedPage.sign   : IAttestationSink -> PageDocument -> Result<SignedPage, PageSignRefusal>
SignedPage.verify : ClaimVerifier<'Key> -> 'Key option -> PageDocument -> SignedPage
                    -> Async<Result<VerifiedPage, PageVerifyRefusal>>
```

The pattern is the bounded program core's signed envelope (`Fuaran.Program.Bounded.SignedEnvelope`), consumed
as a package: the signature is over a canonical preimage of (tree hash, requests hash, demanded bytes), made
through `Fuaran.Core.IAttestationSink`, and verification **recomputes** the demanded document from the page
before it checks the signature over the preimage it computed. The refusals are named apart: `NoKey` (a verify
with no key refuses), `ForeignKey`, `InvalidDocument`, `PageRulesBroken`, `Drift` (the excess and the
shortfall, each a demanded document) and `BadSignature` (carried and recomputed hashes beside each other).

The signature does not cover the provenance, and it does not say the reach is acceptable — that is
`DemandedPage.check`'s question.

## The page rules

`PreEmitValidate.validatePage options requestKeys tree` is the validator's page-document mode: the ordinary
walk, then two rules over the data-bearing slots (`Chart.source`, `DataGrid.source` / `rowTotal` /
`staticRows`, `Metric.value` / `trend` / comparison values, `Sparkline.source`):

- **FUARAN166** — the slot is not a `Binding.Query` naming a request in the sidecar.
- **FUARAN167** — the slot carries inline data (a `Static` with a value, or `staticRows`).

`pageRuleDefects` runs the two rules alone. `SignedPage.sign` refuses a page that breaks them, and
`SignedPage.verify` refuses to load one.

## Typing each source against its request

`QueryBinding.checkSources schemas tree` holds every query-bound reference to the resolved schema of the query
that sources it — a chart's axis and series to its `Source` request, a scalar sink to its own — so a host that
resolved each request once checks the whole page in one call. A reference whose request has no schema in the
map is not judged; an unresolved request is the host's refusal to render that source.

## Corpus

`page-document/page-document-vectors.json` in the specification repository is emitted by
`src/Fuaran.UI.Tests/PageDocumentTests.fs` (`FUARAN_REGEN=1`, with `FUARAN_WIRE_FIXTURES` naming the corpus)
and compared byte for byte on every run.
