# Fuaran AI-authoring prompt pack (wire-format v1)

A copy-pasteable pack that makes **any** AI emit canonical Fuaran UI JSON – your own
provider call, or the in-browser `fuaran-live` playground. Everything here is generated
from the canonical wire-format corpus and drift-checked in the build, so it can never
fall out of step with the JSON the Fuaran renderer actually accepts.

## Contents

| File | What it is |
|---|---|
| [`system-prompt.md`](system-prompt.md) | The system prompt. Teaches the flat `kind.$type` wire shape, the node kinds, bindings, and the emit rules. Every JSON example in it is corpus-derived, and the per-kind required-fields table is derived from `schema.json` – both drift-checked. |
| [`few-shot.jsonl`](few-shot.jsonl) | One `{ prompt, decoder, fixture, tree }` per line – canonical natural-language-request → wire-tree pairs. Use as few-shot examples (or eval seeds). `decoder` is `node` (a full tree) or `op` (a tree edit). |
| [`schema.json`](schema.json) | The canonical Draft 2020-12 JSON Schema for a `Node` / `TreeOp`. A byte copy of `wire-format-fixtures/schema.json`. Feed it to provider-native constrained/structured output, or to an off-the-shelf validator. |
| [`tool-defs.json`](tool-defs.json) | The four runtime-introspection tool definitions (`getNodeState` / `getBindingValue` / `getRenderedDom` / `getRuntimeErrors`) for the closed-loop authoring pattern, in Anthropic tool-use shape. |
| [`manifest.json`](manifest.json) | Machine-readable index of the pack (so a host like `fuaran-live` can load it programmatically). |

## Use it in your own provider call

1. Pass the contents of `system-prompt.md` as the request's **system** prompt.
2. Optionally seed the conversation with a few lines from `few-shot.jsonl` (each `tree`
   is a valid assistant emission for its `prompt`).
3. For the strongest guarantees, constrain the model's output to `schema.json` via your
   provider's structured-output / JSON-schema mode. The top-level schema is
   `oneOf: [ Node, TreeOp ]`; `$defs/Node` and `$defs/TreeOp` are exposed if you want to
   constrain to one shape.
4. To run the **closed loop** (emit → observe → repair), register the four tools from
   `tool-defs.json` and wire them to the Fuaran runtime's introspection surface
   (`Fuaran.UI.AiTools`). The model emits a tree, calls a tool to see what the renderer
   did, and emits a `TreeOp` to fix it.

The model's reply is a single JSON document – a `Node` or a `TreeOp`. Validate it
against `schema.json` before applying; the decoder also returns a precise
`DecodeError` (code + JSONPath) on any wire-shape violation.

## Use it from `fuaran-live`

`fuaran-live` loads this pack via `manifest.json`, sends `system-prompt.md` + the
user's prompt to the provider with the user's own key (BYOK), and renders the returned
tree through the public `@fuaran-ui/renderer`. The pack references only the public
Fuaran surface, so it ships with the OSS playground unchanged.

## Provenance + drift

- The wire shape is specified language-neutrally in
  [`../WIRE_FORMAT.md`](../WIRE_FORMAT.md); the executable conformance corpus is
  `wire-format-fixtures/` at the workspace root.
- Regenerate the whole pack: `dotnet fsi docs/tools/authoring-pack.fsx --write`.
- The build runs `authoring-pack.fsx --check`; if any example here diverges from the
  corpus, the build fails. You never hand-edit a `tree` or an example block – you edit
  the fixture and regenerate.

See also [`../AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) for the long-form
authoring guide (patterns, when-to-use, recovery), and
[`../ERROR_CODES.md`](../ERROR_CODES.md) for the decode-error cheat sheet.
