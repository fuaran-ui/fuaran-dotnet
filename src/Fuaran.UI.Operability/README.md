# Fuaran.UI.Operability

A Fuaran tree's **operable surface**, as a specified projection of the tree.

- `Operability.derive mountId tree` — a pure, total function from a `Node<'Msg>` to the
  affordances an agent-initiated instruction may address: per named `Select`, `Form` field
  and `Button`, its `Set` / `Clear` / `Read` / `Click` / `Navigate` affordances, each with
  its label-derived forms plus the node's declared `affordance` patterns, a slot typed by
  the node's value space, and `Resolve` — the node's own action applied to a slot value.
- `Operability.resolve instruction affordances` — the reference matcher of the resolution
  contract: whole-instruction anchoring, ASCII-lowercased literals, greedy typed slots,
  `and` / `then` clauses resolved all-or-nothing, and `Ambiguous` where two affordances
  could claim one clause.

The normative text is WIRE_FORMAT §31 in the wire-format specification; the
`operability/` corpus family certifies every host against it. The matcher does not
understand an instruction: it matches declared forms, and an instruction no form
matches is refused.
