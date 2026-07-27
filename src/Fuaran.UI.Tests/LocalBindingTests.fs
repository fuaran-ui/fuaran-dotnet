module Fuaran.UI.Tests.LocalBindingTests

#nowarn "3261"

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  Typed-surface tests for `Binding<'T>.Local`.
//
//  Covers:
//   - The `binding.local` smart-ctor constructs a well-shaped `Binding.Local`
//     payload that round-trips its InitialFrom + FlushOn + Format + Parse.
//   - The renderer-side `BindingResolver.resolve` falls through a Local
//     binding to its `InitialFrom` source — proves the renderer-pre-render
//     read returns the model-side value, and the per-NodeId useState slot
//     is the only place the buffer-overlay lives.
//   - `Action.CommitLocal` is constructable + carries the typed nodeId.
//
//  These are unit tests of the `binding.local` CONSTRUCTOR: each builds the
//  binding as a standalone value and inspects the payload, which is exactly
//  the shape FUARAN044 requires to sit inside a FormFieldKind.Text/Number.
//  Wrapping them in a form field to satisfy the rule would change what is
//  under test from the binding to the field, so the code is suppressed here.
// fuaran-validator: disable FUARAN044 — constructor unit tests, no enclosing form field by design
//
//  Renderer-side React state behaviour (keystroke buffer / blur flush /
//  re-sync invariant) is exercised in the catalog's Playwright spec
//  (`samples/catalog/snapshot/local-bindings.spec.mts`) — those need a
//  real browser to drive the keyboard events and observe React's effect
//  cleanup, neither of which Expecto can simulate without dragging in
//  jsdom + react-test-renderer (not in this repo's package graph).
// ============================================================================

type private Msg =
    | SetSalary of decimal
    | NoOp

let private parseDecimalLenient (raw: string) : Result<decimal, string> =
    let cleaned = raw.Replace(",", "")

    match
        System.Decimal.TryParse(
            cleaned,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture
        )
    with
    | true, v -> Ok v
    | false, _ -> Error(sprintf "could not parse '%s' as a decimal" raw)

let private formatThousands (v: decimal) : string =
    v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Binding.Local"
        [ test "binding.local constructs a Binding.Local with the supplied InitialFrom + FlushOn" {
              let initialFrom: Binding<decimal> = binding.state "salary" 50000m

              let b: Binding<decimal> =
                  binding.local
                      initialFrom
                      LocalFlushTrigger.OnBlur
                      (fun v -> Action.dispatch (SetSalary v))
                      (Some formatThousands)
                      parseDecimalLenient

              match b with
              | Binding.Local local ->
                  Expect.equal local.FlushOn LocalFlushTrigger.OnBlur "FlushOn preserved"

                  match local.InitialFrom with
                  | Binding.State(key, dv) ->
                      Expect.equal key "salary" "InitialFrom key preserved"
                      Expect.equal dv 50000m "InitialFrom default preserved"
                  | other -> failtestf "Expected Binding.State InitialFrom, got %A" other

                  Expect.isTrue local.Format.IsSome "Format closure preserved"

                  match local.Parse "1,000" with
                  | Ok v -> Expect.equal v 1000m "Parse strips commas"
                  | Error e -> failtestf "Expected parse Ok, got Error %s" e
              | other -> failtestf "Expected Binding.Local, got %A" other
          }

          test "binding.local's OnCommit obj-erases the typed Action<'Msg>" {
              let initialFrom: Binding<decimal> = binding.``static`` 0m

              let b: Binding<decimal> =
                  binding.local
                      initialFrom
                      LocalFlushTrigger.OnBlur
                      (fun v -> Action.dispatch (SetSalary v))
                      (Some formatThousands)
                      parseDecimalLenient

              match b with
              | Binding.Local local ->
                  // OnCommit : 'T -> obj — the smart-ctor wraps the typed
                  // Action<'Msg> in a box; the renderer recovers via unbox.
                  let raw = local.OnCommit 250000m
                  let recovered = unbox<Action<Msg>> raw

                  match recovered with
                  | Action.Dispatch(SetSalary v) -> Expect.equal v 250000m "Dispatched Msg payload preserved"
                  | other -> failtestf "Expected Action.Dispatch(SetSalary 250000m), got %A" other
              | other -> failtestf "Expected Binding.Local, got %A" other
          }

          test "BindingResolver.resolve on Binding.Local falls through to InitialFrom" {
              let initialFrom: Binding<decimal> = binding.state "salary" 75000m

              let b: Binding<decimal> =
                  binding.local
                      initialFrom
                      LocalFlushTrigger.OnBlur
                      (fun v -> Action.dispatch (SetSalary v))
                      (Some formatThousands)
                      parseDecimalLenient

              let sources =
                  { Fuaran.UI.Renderer.BindingResolver.empty with
                      State = Map.ofList [ "salary", box 123456m ] }

              match Fuaran.UI.Renderer.BindingResolver.resolve sources b with
              | Fuaran.UI.Renderer.BindingResolver.Resolved v ->
                  Expect.equal v 123456m "Resolved value comes from the State source under InitialFrom's key"
              | other -> failtestf "Expected Resolved 123456m, got %A" other
          }

          test "BindingResolver falls through to InitialFrom default when sources empty" {
              let initialFrom: Binding<decimal> = binding.state "missing-key" 999m

              let b: Binding<decimal> =
                  binding.local
                      initialFrom
                      LocalFlushTrigger.OnBlur
                      (fun v -> Action.dispatch (SetSalary v))
                      (Some formatThousands)
                      parseDecimalLenient

              let sources = Fuaran.UI.Renderer.BindingResolver.empty

              match Fuaran.UI.Renderer.BindingResolver.resolve sources b with
              | Fuaran.UI.Renderer.BindingResolver.Resolved v ->
                  Expect.equal v 999m "Resolved value uses State binding's declared default"
              | other -> failtestf "Expected Resolved with declared default, got %A" other
          }

          test "Action.CommitLocal carries the typed nodeId" {
              let action: Action<Msg> = Action.CommitLocal "salary-input"

              match action with
              | Action.CommitLocal id -> Expect.equal id "salary-input" "NodeId preserved"
              | other -> failtestf "Expected Action.CommitLocal, got %A" other
          }

          test "LocalFlushTrigger.OnDebounce carries the millisecond payload" {
              let trigger = LocalFlushTrigger.OnDebounce 250

              match trigger with
              | LocalFlushTrigger.OnDebounce ms -> Expect.equal ms 250 "OnDebounce milliseconds preserved"
              | other -> failtestf "Expected OnDebounce 250, got %A" other
          }

          test "Defaults.localBinding's Parse returns the placeholder Error" {
              let defaultLocal: LocalBinding<decimal> = Defaults.localBinding<decimal>

              match defaultLocal.Parse "anything" with
              | Error msg ->
                  Expect.stringContains
                      msg
                      "no Parse function"
                      "Defaults placeholder Parse returns an Error noting the gap"
              | Ok _ -> failtest "Expected Defaults.localBinding.Parse to return Error"
          } ]
