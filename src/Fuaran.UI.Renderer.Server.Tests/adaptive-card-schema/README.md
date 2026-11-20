# Vendored Adaptive Cards schema (test asset)

`adaptive-card-1.4.0.json` is the **published** Adaptive Cards JSON Schema for card version 1.4,
vendored byte-for-byte so `AdaptiveCardProjectionTests` can validate every card the projection
(`Fuaran.UI.Renderer.Server.AdaptiveCard`, Phase 1814) writes against the format's own published
definition rather than against this repository's reading of it.

- **Source:** `https://raw.githubusercontent.com/microsoft/AdaptiveCards/93695cf2df1117ab08e36ebe707d0ced18e4e9b3/schemas/1.4.0/adaptive-card.json`
  (the last commit to touch that file, 2021-11-15).
- **SHA-256:** `e1750b01c13459b1d937ae0a8792d825314d54ddd22b1c8159cfd809d2caab09` (76,459 bytes).
- **Licence:** MIT, Copyright (c) 2017 Microsoft. The licence text is [`LICENSE`](LICENSE) beside
  this file, copied from the same commit, as the MIT terms require.

It is a test asset only: copied to the test output, never packed, and referenced by no shipping
project. Do not edit it. A move of the projection's target version vendors that version's schema
beside this one, under its own file name, and the test names which file it validates against.
