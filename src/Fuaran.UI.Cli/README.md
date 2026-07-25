# Fuaran.UI.Cli

The Fuaran generative-UI CLI as a **dotnet tool** — the F# front-end of the
Fuaran CLI (sibling to the npm `@fuaran-ui/cli`). Zero MCP config; works against
the real generation endpoint or the local mock.

```bash
dotnet tool install -g Fuaran.UI.Cli
fuaran generate "a metric strip showing revenue" --mock   # offline, no secret
fuaran validate tree.json                                  # canonical-schema check
fuaran scaffold --target fsharp                            # F#/Fable integration boilerplate
```

## Commands

- **`generate <prompt> [--tree <file>] [--mock [url]]`** — a prompt (optionally
  repairing `--tree`) → a canonical wire tree, via `Fuaran.UI.Client`. Against
  the real endpoint (env config) or the local mock (`--mock`, no secret).
- **`validate <file>`** — wire JSON → pass/fail + canonical decode diagnostics,
  via `Fuaran.UI.Ops`.
- **`scaffold --target fsharp`** — the F#/Fable integration boilerplate
  (turn-loop over the endpoint, rendered through `Fuaran.UI.Renderer`;
  server-proxied).

`generate` and `validate` behave **identically** to the npm `@fuaran-ui/cli`
(they share the wire substrate). The recipe bank and the multi-target scaffold
templates are single-sourced in the TS tier (to avoid drift); `recipe` and the
`ts` scaffold target point to `@fuaran-ui/cli` / the MCP.

## Secrets

`FUARAN_ENDPOINT` / `FUARAN_ACCESS_TOKEN` / `FUARAN_PROVIDER_KEY` are read from
the **environment only** — never a flag, never printed. `--mock` needs no secret.
The endpoint URL + paid access token are the commercial gate; this CLI is a thin,
OSS-safe client over the public surfaces.
