module Fuaran.UI.Tests.SubmitPayload

// ============================================================================
//  Phase 820 — submit-payload semantics, client-tier core.
//
//  `SubmitPayload` (Renderer.Core) is the pure shared half of "submitting
//  posts the fields": the client harvest (each declared field's current value,
//  read exactly as the renderer's own controls read it — `tryResolve` over the
//  same Phase-596 auto-bind default), the Notify `"fields"` merge, and the
//  `onSubmit` action-tree rewrite. `renderForm`'s submit handler drives it in
//  the browser; these .NET tests pin the decisions without a browser render
//  (the `applyDispatchGate` precedent). The server-driven twin — the harvest
//  off the shim payload + the `InterpretSubmitCall` body — is pinned in
//  `Fuaran.UI.ServerDriven.Tests.FormBufferTests`.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Msg = Submitted

// F# 10 `box _` types as `obj | null`; the store surfaces take a non-null
// `obj`. Same `nn` workaround the other test modules use.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private field (id: string) (kind: FormFieldKind<Msg>) : FormField<Msg> =
    { Defaults.formField<Msg> with
        Id = id
        Kind = kind }

let private sourcesWith (state: (string * obj) list) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList state }

[<Tests>]
let tests =
    testList
        "Submit payload (Phase 820)"
        [ test "harvestFields reads each declared field as its control does (auto-bind by field id)" {
              let fields =
                  [ field "name" (FormFieldKind.Text(None, None))
                    field "age" (FormFieldKind.Number(None, None))
                    field "subscribed" (FormFieldKind.Checkbox(None, None))
                    // No selection stored — an unselected choice contributes
                    // nothing (like an unselected <select>).
                    field "plan" (FormFieldKind.Choice(Binding.Static(Some []), None, None)) ]

              let sources =
                  sourcesWith [ "name", nn "Ada"; "age", nn 30.0; "subscribed", nn true ]

              Expect.equal
                  (SubmitPayload.harvestFields sources fields)
                  [ "name", JStr "Ada"; "age", JFloat 30.0; "subscribed", JBool true ]
                  "declaration order, keyed by field id, no-selection omitted"
          }

          test "harvestFields honours an explicit State binding's own key" {
              let fields =
                  [ field "name" (FormFieldKind.Text(Some(Binding.State("custom.slot", Some "")), None)) ]

              let sources = sourcesWith [ "custom.slot", nn "from-custom" ]

              Expect.equal
                  (SubmitPayload.harvestFields sources fields)
                  [ "name", JStr "from-custom" ]
                  "the field's own binding decides the store key"
          }

          test "a Local-bound field contributes its resolved (committed/external) value, not a buffer" {
              let fields =
                  [ field
                        "draft"
                        (FormFieldKind.Text(
                            Some(
                                binding.local
                                    (Binding.Static(Some "committed"))
                                    LocalFlushTrigger.OnSubmit
                                    (fun _ -> Action.Dispatch Submitted)
                                    None
                                    (fun s -> Ok s)
                            ),
                            None
                        )) ]

              Expect.equal
                  (SubmitPayload.harvestFields BindingResolver.empty fields)
                  [ "draft", JStr "committed" ]
                  "a Local binding resolves through its initialFrom re-sync source"
          }

          test "pair fields contribute their first (min/from) value — the shim-harvest mirror" {
              let fields = [ field "span" (FormFieldKind.Range(None, None, None, None, None)) ]

              let sources = sourcesWith [ "span", nn ({ Min = 1.0; Max = 9.0 }: RangePair) ]

              Expect.equal
                  (SubmitPayload.harvestFields sources fields)
                  [ "span", JFloat 1.0 ]
                  "the pair's min end is the field's harvested scalar"
          }

          test "mergeIntoNotifyPayload appends a 'fields' key after the authored keys" {
              Expect.equal
                  (SubmitPayload.mergeIntoNotifyPayload [ "name", JStr "Ada" ] (JObj [ "kind", JStr "signup" ]))
                  (JObj [ "kind", JStr "signup"; "fields", JObj [ "name", JStr "Ada" ] ])
                  "authored keys first, fields appended"
          }

          test "mergeIntoNotifyPayload never clobbers — authored 'fields' and scalar payloads pass through" {
              let authored = JObj [ "fields", JStr "mine" ]

              Expect.equal
                  (SubmitPayload.mergeIntoNotifyPayload [ "name", JStr "Ada" ] authored)
                  authored
                  "an authored 'fields' key wins"

              Expect.equal
                  (SubmitPayload.mergeIntoNotifyPayload [ "name", JStr "Ada" ] (JStr "plain"))
                  (JStr "plain")
                  "a scalar payload has no slot to merge into"
          }

          test "attachToAction rewrites a Chain-nested Notify and leaves everything else untouched" {
              let fields = [ "name", JStr "Ada" ]

              match
                  SubmitPayload.attachToAction
                      fields
                      (Action.Chain [ Action.Dispatch Submitted; Action.Notify("chan", JObj []) ])
              with
              | Action.Chain [ Action.Dispatch Submitted; Action.Notify("chan", payload) ] ->
                  Expect.equal payload (JObj [ "fields", JObj fields ]) "the nested Notify gained the fields"
              | other -> failtestf "expected the rewritten Chain, got %A" other

              // A non-Notify action passes through REFERENCE-identical — the
              // "no Call/Notify ⇒ byte-identical actions" guarantee.
              let dispatch = Action.Dispatch Submitted

              Expect.isTrue
                  (obj.ReferenceEquals(SubmitPayload.attachToAction fields dispatch, dispatch))
                  "a submit action without Notify is returned unchanged (same instance)"
          } ]
