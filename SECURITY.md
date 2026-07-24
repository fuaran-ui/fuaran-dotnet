# Security Policy

## Supported versions

Fuaran.UI is pre-1.0. Security fixes are applied to the latest released `0.x` version on the
`main` branch. Older pre-releases are not maintained.

## Reporting a vulnerability

Please report suspected vulnerabilities privately — do **not** open a public issue.

- **Preferred:** GitHub's private vulnerability reporting (the repository's **Security** tab →
  **Report a vulnerability**).
- **Or email:** andrew@fuaran.com — include a description, the affected version, and steps
  to reproduce.

We aim to acknowledge a report within five business days and to agree a disclosure timeline with
you. Please allow a reasonable window to ship a fix before any public disclosure.

## Scope

The `Fuaran.UI.*` package set renders typed trees — often authored by an AI — into the DOM, so its
security posture is documented explicitly:

- **Render-time injection safety:** [`SANITIZATION.md`](SANITIZATION.md) declares the posture at
  every string→DOM seam the renderer exposes (`href`/`src` scheme filtering, no
  `dangerouslySetInnerHTML` outside the documented seams, the Custom-renderer trust boundary).
  A render path that violates a declared guarantee there is a vulnerability we want to hear about.
- **Cryptographic posture:** [`CRYPTO.md`](CRYPTO.md) declares which hashes are cryptographic and
  which are integrity-only. A non-cryptographic default doing exactly what that document says is
  by design, not a defect.
- **Wire decoding:** a decode path that admits malformed wire as valid, or parser resource
  exhaustion (unbounded depth or size), is in scope.
- **Dispatch gating:** interactive dispatch is default-deny — a tree cannot invoke a host action
  the host did not register. A bypass of that gate is in scope.

Custom renderers registered by a **host** run with the host's trust — issues in host-supplied
custom-renderer code belong with the host application, not this repo.
