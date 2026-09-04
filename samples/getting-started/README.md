# Getting started — the six-lesson tour

Six short lessons that between them explain what this language is for. Each is a
runnable console program you can read in one sitting, and the whole tour runs in
about a second.

```bash
dotnet run --project samples/getting-started              # the whole tour
dotnet run --project samples/getting-started -- replay    # just one lesson
```

**Five of the six need no key, no network and no browser.** Only the last one
calls a model, and only when you supply your own key — so nothing here is
unrunnable because you have not signed up for anything.

## The lessons

| | Lesson | What it shows |
|---|---|---|
| 1 | `authoring` | A user interface is a **value**. Build a typed tree with ordinary functions, encode it to canonical JSON, render it to HTML with no browser. |
| 2 | `ops` | **Edit the tree, don't regenerate it.** A `TreeOp` is a typed, addressed edit that applies deterministically and fails by name. |
| 3 | `replay` | A session is a **hash-chained list of ops**, so it replays exactly, time-travels for free, and detects tampering. |
| 4 | `safety` | **Safety is a property of the shape.** Malformed and unsafe emissions are refused by the decoder because the vocabulary has no code case to strip. |
| 5 | `operations` | **Declared operations need no model.** A control publishes typed operations; a structural search dispatches one deterministically, offline. |
| 6 | `ai` | **Bring your own key**: prompt → wire JSON → strict decode → render. The whole loop, in one file. |

## Running lesson 6 with your own key

```bash
# PowerShell
$env:ANTHROPIC_API_KEY = "sk-..."
dotnet run --project samples/getting-started -- ai

# bash
export ANTHROPIC_API_KEY="sk-..."
dotnet run --project samples/getting-started -- ai

# or, without setting an environment variable at all
dotnet run --project samples/getting-started -- ai --key sk-...
```

The key is read from this process, sent to the provider you chose, and used for
one request. Nothing is stored and nothing is logged. There is no SDK involved —
one `HttpClient` and one JSON body — so pointing the sample at a different
provider is a different URL and a different field name.

## What to read next

- **`samples/catalog`** — the whole component vocabulary, rendered in a browser.
- **`docs/AI_AUTHORING_GUIDE.md`** — what to put in a system prompt so a model
  emits trees that decode first time.
- **`docs/ERROR_CODES.md`** — the refusals from lesson 4, in full.
- **The wire-format specification** — the language-neutral definition every host
  conforms to, with its executable conformance corpus. F#, TypeScript and Python
  are co-equal hosts of it: a tree authored in one is read by all of them.

## A note on what is not here

Lesson 5 declares its own patterns, in its own file. There is also a curated
seed catalogue in `Fuaran.UI.FastPath`, and either is a fine place to start.
What no sample can show you is a pattern bank **learned** from a corpus of real
sessions — a resolver that gets better the more it is used. That is not part of
the open language tier, and its absence is deliberate rather than an oversight.
