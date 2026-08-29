module PureTierLaws.Program

// The pure tier's structural-predicate ALGEBRA, checked law by law over the shared wire-format
// corpus, in a form that is byte-comparable between the .NET run and the Fable-under-node run.
// `./run-pure-tier-laws.ps1` runs it both ways and diffs.
//
// TWO claims, and they are different claims:
//
//  1. THE LAWS HOLD UNDER NODE. `Fuaran.UI.Tests` enumerates these same laws on .NET; the repo's
//     only Fable leg is `-- Catalog`, which is a COMPILE gate and says nothing about behaviour.
//     Between the two there was no evidence at all that the transpiled algebra still obeys
//     idempotence, commutativity, associativity, the identities, involution, complement or de
//     Morgan.
//
//  2. THE TWO PIPELINES AGREE ABOUT THE MATCH SETS. Every line carries the SIZE of each pool
//     predicate's match set per fixture, not merely a law verdict — so a divergence that leaves
//     both sides internally lawful (a collection ordering difference, an arithmetic lowering, a
//     decode that drops a slot on one side) still shows up as a differing line. A law-only probe
//     would report two disagreeing pipelines as two green runs.
//
// Output discipline: every line is ASCII, fixed-width-indexed and derived only from COUNTS and
// fixture NAMES. Nothing echoes fixture CONTENT, because the two runtimes do not agree about
// writing a lone surrogate to a terminal and a probe that reported console encoding as an algebra
// divergence would be worse than no probe. That is the lesson tests/ids-parity-probe records; it
// applies unchanged here.

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.StructuralQuery

/// The predicate pool the laws are enumerated over — the same set
/// `Fuaran.UI.Tests/StructuralQueryTests` uses, deliberately, so a law that holds on one pipeline
/// and not the other is comparing like with like. Each member is asserted to DISCRIMINATE over the
/// corpus below: a law quantified over predicates that match nothing is a law about the empty set.
let private pool: Predicate list =
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

/// The pool restricted for the cubic laws. Associativity is enumerated over pool^3 per fixture,
/// which at the full pool is 1,331 evaluations per tree per law — bounded, but it dominates the
/// run under Node for no extra discrimination. The prefix is a deliberate, stated trade, not an
/// accident of writing `List.truncate` and forgetting: pairs and identities still run over the
/// WHOLE pool, and the claim counts printed at the end make the difference visible rather than
/// implied.
let private cubicPool = pool |> List.truncate 5

let private matched (p: Predicate) (t: Node<obj>) : Set<string> = (evaluate p t).Matched

/// Every node id in a tree, via the algebra's own identity element. The identity law below is what
/// pins this to the tree's real node set, so it is not a circular definition.
let private everyNode (t: Node<obj>) : Set<string> = matched (Predicate.And []) t

/// One law's verdict over one fixture: how many claims it made, and how many failed.
type private Verdict = { Claims: int; Violations: int }

let private zero = { Claims = 0; Violations = 0 }

let private check (v: Verdict) (holds: bool) : Verdict =
    { Claims = v.Claims + 1
      Violations = v.Violations + (if holds then 0 else 1) }

// ── the laws ────────────────────────────────────────────────────────────────

let private idempotence (t: Node<obj>) : Verdict =
    pool
    |> List.fold
        (fun v a ->
            let v = check v (matched (Predicate.And [ a; a ]) t = matched a t)
            check v (matched (Predicate.Or [ a; a ]) t = matched a t))
        zero

let private commutativity (t: Node<obj>) : Verdict =
    pool
    |> List.fold
        (fun v a ->
            pool
            |> List.fold
                (fun v b ->
                    let v =
                        check v (matched (Predicate.And [ a; b ]) t = matched (Predicate.And [ b; a ]) t)

                    check v (matched (Predicate.Or [ a; b ]) t = matched (Predicate.Or [ b; a ]) t))
                v)
        zero

let private associativity (t: Node<obj>) : Verdict =
    cubicPool
    |> List.fold
        (fun v a ->
            cubicPool
            |> List.fold
                (fun v b ->
                    cubicPool
                    |> List.fold
                        (fun v c ->
                            let v =
                                check
                                    v
                                    (matched (Predicate.And [ Predicate.And [ a; b ]; c ]) t = matched
                                        (Predicate.And [ a; Predicate.And [ b; c ] ])
                                        t)

                            check
                                v
                                (matched (Predicate.Or [ Predicate.Or [ a; b ]; c ]) t = matched
                                    (Predicate.Or [ a; Predicate.Or [ b; c ] ])
                                    t))
                        v)
                v)
        zero

let private identities (t: Node<obj>) : Verdict =
    let v = check zero (Set.isEmpty (matched (Predicate.Or []) t))

    pool
    |> List.fold
        (fun v a ->
            let v = check v (matched (Predicate.And [ a; Predicate.And [] ]) t = matched a t)

            check v (matched (Predicate.Or [ a; Predicate.Or [] ]) t = matched a t))
        v

let private negation (t: Node<obj>) : Verdict =
    let every = everyNode t

    pool
    |> List.fold
        (fun v a ->
            let v = check v (matched (Predicate.Not(Predicate.Not a)) t = matched a t)
            check v (matched (Predicate.Not a) t = Set.difference every (matched a t)))
        zero

let private deMorgan (t: Node<obj>) : Verdict =
    pool
    |> List.fold
        (fun v a ->
            pool
            |> List.fold
                (fun v b ->
                    let v =
                        check
                            v
                            (matched (Predicate.Not(Predicate.And [ a; b ])) t = matched
                                (Predicate.Or [ Predicate.Not a; Predicate.Not b ])
                                t)

                    check
                        v
                        (matched (Predicate.Not(Predicate.Or [ a; b ])) t = matched
                            (Predicate.And [ Predicate.Not a; Predicate.Not b ])
                            t))
                v)
        zero

let private laws: (string * (Node<obj> -> Verdict)) list =
    [ "idem", idempotence
      "comm", commutativity
      "assoc", associativity
      "ident", identities
      "neg", negation
      "demorgan", deMorgan ]

// ── the run ─────────────────────────────────────────────────────────────────

/// Zero-padded to three digits without `sprintf "%03d"` — Fable lowers the format string, and a
/// probe whose INDEX formatting differs between pipelines would report a padding difference as an
/// algebra divergence. Hand-rolled, so both sides run the same arithmetic.
let private pad3 (n: int) : string =
    let s = string n

    if s.Length >= 3 then s
    elif s.Length = 2 then "0" + s
    else "00" + s

[<EntryPoint>]
let main _ =
    let fixtures = Corpus.fixtures |> List.sortBy fst

    // A pool member that matches everywhere, or nowhere, makes every law below a statement about a
    // constant. Counted per predicate across the corpus and printed, so vacuity is visible rather
    // than assumed — the .NET suite asserts the same property and this is its portable half.
    let mutable hitCounts = pool |> List.map (fun _ -> 0)
    let mutable totalClaims = 0
    let mutable totalViolations = 0
    let mutable decoded = 0

    fixtures
    |> List.iteri (fun i (name, json) ->
        match Generated.decodeNode json with
        | Error _ ->
            // A fixture the pure tier cannot decode is reported, never skipped: both pipelines must
            // agree about WHICH fixtures decode, and a silent skip would hide exactly the
            // divergence this probe exists to find.
            printfn "FIX %s %s DECODE-FAIL" (pad3 i) name
        | Ok t ->
            decoded <- decoded + 1

            let sizes = pool |> List.map (fun p -> Set.count (matched p t))

            hitCounts <- List.map2 (fun total size -> total + (if size > 0 then 1 else 0)) hitCounts sizes

            printfn
                "FIX %s %s nodes=%d sets=%s"
                (pad3 i)
                name
                (Set.count (everyNode t))
                (sizes |> List.map string |> String.concat ",")

            let verdicts = laws |> List.map (fun (label, law) -> label, law t)

            for (_, v) in verdicts do
                totalClaims <- totalClaims + v.Claims
                totalViolations <- totalViolations + v.Violations

            printfn
                "LAW %s %s %s"
                (pad3 i)
                name
                (verdicts
                 |> List.map (fun (label, v) -> label + "=" + string v.Claims + "/" + string v.Violations)
                 |> String.concat " "))

    printfn
        "POOL discriminating=%s"
        (hitCounts
         |> List.map (fun hits ->
             if hits = 0 then "never"
             elif hits = decoded then "always"
             else string hits)
         |> String.concat ",")

    printfn
        "TOTAL fixtures=%d decoded=%d claims=%d violations=%d"
        (List.length fixtures)
        decoded
        totalClaims
        totalViolations

    if totalViolations = 0 then 0 else 1
