module Fuaran.UI.Tests.ExprBinding

// ============================================================================
//  `Binding.Expr` — scalar logic over bound values (Fuaran-UI Phase 1534,
//  WIRE_FORMAT.md §3.3.2).
//
//  Four separate claims, kept apart on purpose:
//
//  1. THE OPERATOR SEMANTICS — what each shape in the demand census evaluates
//     to. These are the vectors the other four hosts have to reproduce, and
//     they assert VALUES rather than "it resolved", because a host that
//     returned a plausible-looking wrong number would pass a shape assertion.
//
//  2. PARAM RESOLUTION — that the params reach the expression from the same
//     binding sources a `Transform` param reads, including the LIST param that
//     resolves by substitution rather than through the scalar environment.
//
//  3. THE REFUSALS — that an unbound param and a type error are `Errored` and
//     never a substituted default, and that a null result is the slot's
//     ABSENCE rather than an error. This is the pair that distinguishes "could
//     not be computed" from "computed to nothing", and a host that collapsed
//     them would render a confident blank.
//
//  4. DETERMINISM ACROSS THE PIPELINES — the expression is evaluated by the
//     SAME `Fuaran.Core` evaluator a `derive` step uses, so the same expression
//     in an `Expr` and in a 1×1 `Transform` must agree. The last test states
//     that as an equality rather than as a comment: it is the property that
//     lets the specification carry one algebra instead of two, and it would
//     decay silently if a second evaluator were ever introduced here.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// `box` under F# 10's nullness rules — `BindingSources.State` is a
/// `Map<string, obj>` of non-nullable values, the shape the renderer's own
/// tests use.
let private nn (v: 'a) : obj = box v |> Unchecked.nonNull

let private lit (s: string) = Fuaran.Core.Lit(Fuaran.Core.Str s)
let private num (f: float) = Fuaran.Core.Lit(Fuaran.Core.Float f)

let private sources (state: (string * obj) list) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList state }

/// Resolve an `Expr` in a TEXT slot — the `resolveScalarText` coercion.
let private text (srcs: BindingResolver.BindingSources) (e: Fuaran.Core.ColExpr) ps =
    BindingResolver.resolveScalarText srcs (Binding.Expr(e, ps))

/// Resolve an `Expr` in a NUMERIC slot — the `resolveScalarFloat` coercion.
let private number (srcs: BindingResolver.BindingSources) (e: Fuaran.Core.ColExpr) ps =
    BindingResolver.resolveScalarFloat srcs (Binding.Expr(e, ps))

let private expectText (label: string) (expected: string) (r: BindingResolver.Resolution<string>) =
    match r with
    | BindingResolver.Resolved v -> Expect.equal v expected label
    | other -> failtestf "%s: expected Resolved %A, got %A" label expected other

let private expectFloat (label: string) (expected: float) (r: BindingResolver.Resolution<float>) =
    match r with
    | BindingResolver.Resolved v -> Expect.equal v expected label
    | other -> failtestf "%s: expected Resolved %f, got %A" label expected other

let private param (name: string) (from: Binding<Fuaran.Core.JVal>) : TransformParam = { From = from; Name = name }

// ─── 1. Operator semantics — the demand census's four intents ───────────────

[<Tests>]
let operatorSemantics =
    testList
        "Phase 1534 — Binding.Expr: the operator semantics"
        [ test "string building — the badge-preview intent (061/c3,c4,c6)" {
              text (sources []) (Fuaran.Core.ApplyFn(Fuaran.Core.Concat, [ lit "Ada"; lit " "; lit "Lovelace" ])) None
              |> expectText "concat of three literals" "Ada Lovelace"
          }

          test "arithmetic — the computed-total intent (044/c6)" {
              number (sources []) (Fuaran.Core.Binary(Fuaran.Core.Mul, num 12.5, num 4.0)) None
              |> expectFloat "12.5 * 4" 50.0
          }

          test "AND/OR/NOT — a boolean combination selecting a branch" {
              let e =
                  Fuaran.Core.Case(
                      [ Fuaran.Core.Binary(
                            Fuaran.Core.And,
                            Fuaran.Core.Lit(Fuaran.Core.Bool true),
                            Fuaran.Core.Not(Fuaran.Core.Lit(Fuaran.Core.Bool false))
                        ),
                        lit "ready" ],
                      lit "blocked"
                  )

              text (sources []) e None |> expectText "true AND NOT false" "ready"
          }

          test "the null test is TOTAL — it answers for a null, it does not propagate one" {
              let isEmpty (subject: Fuaran.Core.ColExpr) =
                  Fuaran.Core.Case([ Fuaran.Core.IsNull subject, lit "empty" ], lit "has a value")

              text (sources []) (isEmpty (Fuaran.Core.Lit Fuaran.Core.Null)) None
              |> expectText "isNull of null" "empty"

              text (sources []) (isEmpty (lit "x")) None
              |> expectText "isNull of a value" "has a value"
          }

          test "a BOOLEAN slot takes a bool cell and refuses truthiness" {
              let boolOf e =
                  BindingResolver.resolveScalarBool (sources []) (Binding.Expr(e, None))

              match boolOf (Fuaran.Core.Binary(Fuaran.Core.Gt, num 3.0, num 2.0)) with
              | BindingResolver.Resolved v -> Expect.isTrue v "3 > 2"
              | other -> failtestf "expected Resolved true, got %A" other

              // The go-red half of the claim: without the strict coercion a
              // truthiness rule would make this `Resolved true` and the test
              // above would still pass. Five hosts have to agree on this, and
              // the only rule they can all agree on is "no rule".
              match boolOf (num 1.0) with
              | BindingResolver.Errored _ -> ()
              | other -> failtestf "a numeric cell must not be read as a boolean, got %A" other
          } ]

// ─── 2. Param resolution — the same sources a Transform param reads ─────────

[<Tests>]
let paramResolution =
    testList
        "Phase 1534 — Binding.Expr: params"
        [ test "a State param reaches the expression" {
              let srcs = sources [ "form.firstName", nn "Ada"; "form.lastName", nn "Lovelace" ]

              text
                  srcs
                  (Fuaran.Core.ApplyFn(
                      Fuaran.Core.Concat,
                      [ Fuaran.Core.Param "firstName"; lit " "; Fuaran.Core.Param "lastName" ]
                  ))
                  (Some
                      [ param "firstName" (Binding.State("form.firstName", None))
                        param "lastName" (Binding.State("form.lastName", None)) ])
              |> expectText "concat over two State params" "Ada Lovelace"
          }

          test "arithmetic over two State params — the live order total" {
              let srcs = sources [ "form.unitPrice", nn 12.5; "form.quantity", nn 4.0 ]

              number
                  srcs
                  (Fuaran.Core.Binary(Fuaran.Core.Mul, Fuaran.Core.Param "unitPrice", Fuaran.Core.Param "quantity"))
                  (Some
                      [ param "unitPrice" (Binding.State("form.unitPrice", None))
                        param "quantity" (Binding.State("form.quantity", None)) ])
              |> expectFloat "unitPrice * quantity" 50.0
          }

          test "re-evaluating with a CHANGED param source changes the result" {
              // Without this the test above passes for a host that folded the
              // expression once and cached it, which is the whole failure mode a
              // "derived value over mutable state" case exists to avoid.
              let e = Fuaran.Core.Binary(Fuaran.Core.Mul, Fuaran.Core.Param "q", num 3.0)

              let ps = Some [ param "q" (Binding.State("form.q", None)) ]

              number (sources [ "form.q", nn 2.0 ]) e ps |> expectFloat "q = 2" 6.0
              number (sources [ "form.q", nn 5.0 ]) e ps |> expectFloat "q = 5" 15.0
          }

          test "a LIST param resolves by SUBSTITUTION into the in/items form" {
              // `InParam` never enters the scalar environment — it is rewritten
              // to `InList` before evaluation, exactly as a pipeline's is. A host
              // that wired only the scalar path fails here and nowhere else.
              let e =
                  Fuaran.Core.Case(
                      [ Fuaran.Core.InParam(Fuaran.Core.Param "status", "openStatuses"), lit "open" ],
                      lit "closed"
                  )

              let ps =
                  Some
                      [ param "status" (Binding.State("row.status", None))
                        param "openStatuses" (Binding.State("chip.statuses", None)) ]

              text (sources [ "row.status", nn "triage"; "chip.statuses", nn [ nn "triage"; nn "open" ] ]) e ps
              |> expectText "a member of the selected set" "open"

              text (sources [ "row.status", nn "closed"; "chip.statuses", nn [ nn "triage"; nn "open" ] ]) e ps
              |> expectText "not a member" "closed"
          } ]

// ─── 3. The refusals, and the absence that is NOT one ───────────────────────

[<Tests>]
let refusals =
    testList
        "Phase 1534 — Binding.Expr: Errored, never a substituted default"
        [ test "a param whose source resolves to NOTHING leaves it UNBOUND — Errored" {
              // The param IS declared — a name the `params` list does not bind is
              // refused at decode — but its source produces no value at all. A
              // `Filter` with no chip written and no declared default is the
              // canonical shape. This reaches Core's strict `UnboundParam`
              // through the shared evaluator, which is the point: the expression
              // is not evaluated against a guessed value.
              match
                  number
                      (sources [])
                      (Fuaran.Core.Binary(Fuaran.Core.Mul, Fuaran.Core.Param "q", num 3.0))
                      (Some [ param "q" (Binding.Filter("chip.q", None)) ])
              with
              | BindingResolver.Errored _ -> ()
              | other -> failtestf "expected Errored on a param source that resolves to nothing, got %A" other
          }

          test "an UNWRITTEN State key is NULL, so the expression is ABSENT — not an error" {
              // The distinction this test exists for, and it is not the one the
              // phase text assumed. `Binding.State(key, None)` on a key nothing
              // has written resolves to the slot's default representation — that
              // is the shared `Binding.State` rule, not something `Expr`
              // chooses — so the param binds to a NULL cell, the arithmetic
              // propagates null, and the slot is absent.
              //
              // Which is right: a form field the reader has not filled in yet is
              // not an error, and an error surface on an untouched form is a
              // worse rendering than an empty one. The genuinely-unbound case
              // above stays loud, so the two are distinguished rather than
              // collapsed.
              match
                  number
                      (sources [])
                      (Fuaran.Core.Binary(Fuaran.Core.Mul, Fuaran.Core.Param "q", num 3.0))
                      (Some [ param "q" (Binding.State("form.q", None)) ])
              with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "expected NotResolved for an unwritten State param, got %A" other
          }

          test "a TYPE error is Errored, in the slot's own words" {
              match number (sources []) (lit "not a number") None with
              | BindingResolver.Errored m -> Expect.isTrue (m.Length > 0) "the error carries a message"
              | other -> failtestf "expected Errored on a text cell in a numeric slot, got %A" other
          }

          test "a NULL result is the slot's ABSENCE, not an error" {
              // The discriminator between "could not be computed" and "computed
              // to nothing". Collapsing them is how a host ends up rendering an
              // error surface for an empty value, or an empty value for an error.
              match text (sources []) (Fuaran.Core.Lit Fuaran.Core.Null) None with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "expected NotResolved on a null result, got %A" other
          } ]

// ─── 4. One algebra — Expr and a 1×1 Transform must agree ──────────────────

[<Tests>]
let oneAlgebra =
    testList
        "Phase 1534 — Binding.Expr evaluates through the SAME evaluator a derive step does"
        [ test "the same expression in an Expr and in a 1×1 Transform derive agree" {
              // This is the property that lets §3.3.2 say `Expr` mints no
              // operator and lets the specification carry ONE algebra. It is
              // asserted rather than assumed because a second evaluator
              // introduced here would break it silently — every other test in
              // this file would still pass.
              let e =
                  Fuaran.Core.ApplyFn(
                      Fuaran.Core.Concat,
                      [ lit "total: "
                        Fuaran.Core.Cast(Fuaran.Core.StringType, Fuaran.Core.Param "q") ]
                  )

              let srcs = sources [ "form.q", nn 7.0 ]
              let ps = [ param "q" (Binding.State("form.q", None)) ]

              let viaExpr = text srcs e (Some ps)

              let oneRow: Fuaran.Core.Table =
                  { Schema = [ "__unit", Fuaran.Core.BoolType ]
                    Columns =
                      [ { Name = "__unit"
                          Type = Fuaran.Core.BoolType
                          Cells = [ Fuaran.Core.Bool true ] } ] }

              let viaTransform =
                  BindingResolver.resolveScalarText
                      srcs
                      (Binding.Transform(
                          TransformSource.Data(Fuaran.Core.Embedded oneRow),
                          [ Fuaran.Core.Derive("v", e); Fuaran.Core.Project [ "v", "v" ] ],
                          Some ps
                      ))

              match viaExpr, viaTransform with
              | BindingResolver.Resolved a, BindingResolver.Resolved b -> Expect.equal a b "the two routes agree"
              | a, b -> failtestf "expected both to resolve; Expr = %A, Transform = %A" a b
          } ]
