module PureTierLaws.Laws

// The pure tier's structural-predicate ALGEBRA, defined ONCE.
//
// Two executors consume this module and neither owns a law:
//
//   * `Fuaran.UI.Tests/StructuralQueryTests.fs` compiles it as a linked source file and turns each
//     claim into an Expecto assertion, so a violation names the fixture, the law and the instance.
//   * `tests/pure-tier-laws/Program.fs` folds each claim into a per-law claim/violation count and
//     prints it, on .NET AND on the same sources transpiled to JavaScript, byte-compared.
//
// It had been written out twice — once in each executor — and the two copies had already drifted:
// the transpiled leg enumerated the cubic laws over five of the eleven pool members and checked
// neither absorption nor distributivity at all, so "the laws hold on both pipelines" quietly meant
// two different sets of laws. Two renderings of one oracle is the shape this repo keeps paying for,
// and an oracle is the worst place to keep paying it: a law that has drifted still goes green.
//
// So a law here is a DEFINITION, not an assertion. It yields `Claim`s — two match sets the law says
// are equal, labelled well enough for a failure to be actionable — and says nothing about how a
// disagreement should be reported. That is what lets one edit here redden both executors, which is
// the property the seam exists to have.
//
// THE WHOLE MODULE MUST SURVIVE FABLE. It is compiled to JavaScript as part of the transpiled leg,
// so: no `System.*`, no reflection, no file access, and no `sprintf` (Fable lowers format strings,
// and this module's strings reach a byte-compared output). Plain concatenation instead.

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.StructuralQuery

/// The predicate pool every law below is enumerated over. Small and fixed on purpose: the corpus is
/// a committed set and the pool is eleven members, so enumeration covers every pair and triple
/// outright and the result is reproducible without a shrinking story.
///
/// Each member is required to DISCRIMINATE over the corpus — to match somewhere and miss somewhere.
/// Both executors check that separately, because a law quantified over predicates that match nothing
/// is a law about the empty set, and it passes.
let pool: Predicate list =
    [ Predicate.Kind "Box"
      Predicate.Kind "DataGrid"
      Predicate.Kind "Callout"
      Predicate.Category NodeCategory.Layout
      Predicate.Category NodeCategory.Visualisation
      Predicate.Role "Dashboard"
      Predicate.ChildCount(Cmp.Gte, 2)
      Predicate.Tone "Critical"
      Predicate.BoundTo(Channel.Any, "region")
      Predicate.Dispatches Act.Any
      Predicate.HasDescendant(Predicate.Kind "DataGrid") ]

/// The ids a predicate matches in a tree.
let matched (p: Predicate) (t: Node<obj>) : Set<string> = (evaluate p t).Matched

/// Every node id in a tree, via the algebra's own identity element. The identity law below is what
/// pins this to the tree's real node set, so it is not a circular definition.
let everyNode (t: Node<obj>) : Set<string> = matched (Predicate.And []) t

/// One law's claim about one tree: two match sets the law says are equal, and enough labelling for a
/// failure to name which instance of which law broke.
type Claim =
    { Law: string
      Instance: string
      Left: Set<string>
      Right: Set<string> }

let holds (c: Claim) : bool = c.Left = c.Right

let private claim (law: string) (instance: string) (left: Set<string>) (right: Set<string>) : Claim =
    { Law = law
      Instance = instance
      Left = left
      Right = right }

/// The pool paired with its index, so a claim can name its operands without printing a predicate
/// (`%A` on a union is a format-string lowering, and this text reaches a byte-compared output).
let private indexed = pool |> List.indexed

let private at (i: int) = "a" + string i

// ── the laws ────────────────────────────────────────────────────────────────
//
// Each is `Node<obj> -> Claim seq`, lazily enumerated: the transpiled leg makes ~4,700 claims per
// fixture and there is no reason for any of them to outlive its own comparison.

let idempotence (t: Node<obj>) : Claim seq =
    seq {
        for i, a in indexed do
            yield claim "idem" ("and " + at i) (matched (Predicate.And [ a; a ]) t) (matched a t)
            yield claim "idem" ("or " + at i) (matched (Predicate.Or [ a; a ]) t) (matched a t)
    }

let commutativity (t: Node<obj>) : Claim seq =
    seq {
        for i, a in indexed do
            for j, b in indexed do
                let where = at i + " " + at j

                yield
                    claim
                        "comm"
                        ("and " + where)
                        (matched (Predicate.And [ a; b ]) t)
                        (matched (Predicate.And [ b; a ]) t)

                yield
                    claim "comm" ("or " + where) (matched (Predicate.Or [ a; b ]) t) (matched (Predicate.Or [ b; a ]) t)
    }

/// Enumerated over the WHOLE pool — 11^3 triples per tree per operator. The transpiled leg used to
/// truncate to the first five members, a trade that bought run time at the cost of the two legs
/// checking different laws; unifying the definition retired it, and the claim counts both executors
/// print make the difference visible rather than implied.
let associativity (t: Node<obj>) : Claim seq =
    seq {
        for i, a in indexed do
            for j, b in indexed do
                for k, c in indexed do
                    let where = at i + " " + at j + " " + at k

                    yield
                        claim
                            "assoc"
                            ("and " + where)
                            (matched (Predicate.And [ Predicate.And [ a; b ]; c ]) t)
                            (matched (Predicate.And [ a; Predicate.And [ b; c ] ]) t)

                    yield
                        claim
                            "assoc"
                            ("or " + where)
                            (matched (Predicate.Or [ Predicate.Or [ a; b ]; c ]) t)
                            (matched (Predicate.Or [ a; Predicate.Or [ b; c ] ]) t)
    }

let identities (t: Node<obj>) : Claim seq =
    seq {
        yield claim "ident" "or [] is empty" (matched (Predicate.Or []) t) Set.empty

        for i, a in indexed do
            yield
                claim
                    "ident"
                    ("and [] is the unit of and, " + at i)
                    (matched (Predicate.And [ a; Predicate.And [] ]) t)
                    (matched a t)

            yield
                claim
                    "ident"
                    ("or [] is the unit of or, " + at i)
                    (matched (Predicate.Or [ a; Predicate.Or [] ]) t)
                    (matched a t)
    }

let negation (t: Node<obj>) : Claim seq =
    seq {
        let every = everyNode t

        for i, a in indexed do
            yield claim "neg" ("double negation " + at i) (matched (Predicate.Not(Predicate.Not a)) t) (matched a t)

            yield claim "neg" ("complement " + at i) (matched (Predicate.Not a) t) (Set.difference every (matched a t))
    }

let deMorgan (t: Node<obj>) : Claim seq =
    seq {
        for i, a in indexed do
            for j, b in indexed do
                let where = at i + " " + at j

                yield
                    claim
                        "demorgan"
                        ("and " + where)
                        (matched (Predicate.Not(Predicate.And [ a; b ])) t)
                        (matched (Predicate.Or [ Predicate.Not a; Predicate.Not b ]) t)

                yield
                    claim
                        "demorgan"
                        ("or " + where)
                        (matched (Predicate.Not(Predicate.Or [ a; b ])) t)
                        (matched (Predicate.And [ Predicate.Not a; Predicate.Not b ]) t)
    }

let absorption (t: Node<obj>) : Claim seq =
    seq {
        for i, a in indexed do
            for j, b in indexed do
                yield
                    claim
                        "absorb"
                        (at i + " " + at j)
                        (matched (Predicate.And [ a; Predicate.Or [ a; b ] ]) t)
                        (matched a t)
    }

let distributivity (t: Node<obj>) : Claim seq =
    seq {
        for i, a in indexed do
            for j, b in indexed do
                for k, c in indexed do
                    yield
                        claim
                            "distrib"
                            (at i + " " + at j + " " + at k)
                            (matched (Predicate.And [ a; Predicate.Or [ b; c ] ]) t)
                            (matched (Predicate.Or [ Predicate.And [ a; b ]; Predicate.And [ a; c ] ]) t)
    }

/// The law set, in the order both executors report it. Adding a law here adds it to the Expecto
/// suite and to the transpiled probe in the same edit — which is the point of the list being here
/// rather than in either of them.
let laws: (string * (Node<obj> -> Claim seq)) list =
    [ "idem", idempotence
      "comm", commutativity
      "assoc", associativity
      "ident", identities
      "neg", negation
      "demorgan", deMorgan
      "absorb", absorption
      "distrib", distributivity ]
