module Fuaran.UI.Tests.I18n

// ============================================================================
//  `Binding.I18n` + `II18nResolver` resolution semantics.
//
//  Asserts:
//  - `Binding.I18n` default resolution (passthrough) returns the
//    `I18nUnresolved key` shape (NOT a `Resolved "[i18n:key]"` string —
//    the resolver does emit that placeholder, but the Resolution layer
//    detects the shape and surfaces it as `I18nUnresolved`).
//  - A custom `II18nResolver` returns real translations.
//  - `makeI18nResolver` (the map-backed convenience) handles `{argName}`
//    substitution + missing keys.
//  - I18n args (each a `Binding<obj>`) resolve before being handed to
//    the resolver — `Binding.Static (box 42)` becomes the arg `42`.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

[<Tests>]
let tests =
    testList
        "Binding.I18n + II18nResolver"
        [ test "Binding.I18n resolves to I18nUnresolved under the default passthrough resolver" {
              let binding: Binding<string> = Binding.I18n("greeting.hello", Option.None)
              let resolution = BindingResolver.resolve BindingResolver.empty binding

              match resolution with
              | BindingResolver.I18nUnresolved key -> Expect.equal key "greeting.hello" "key preserved"
              | other -> failtestf "Expected I18nUnresolved, got %A" other
          }

          test "Binding.I18n resolves through a custom II18nResolver to the real translation" {
              let resolver =
                  { new II18nResolver with
                      member _.Resolve(key, _) =
                          match key with
                          | "greeting.hello" -> "Hello"
                          | other -> sprintf "[i18n:%s]" other }

              let sources =
                  { BindingResolver.empty with
                      I18nResolver = resolver }

              let binding: Binding<string> = Binding.I18n("greeting.hello", Option.None)
              let resolution = BindingResolver.resolve sources binding

              Expect.equal resolution (BindingResolver.Resolved "Hello") "Resolved to localised string"
          }

          test "Binding.I18n resolution misses surface as I18nUnresolved even with a custom resolver" {
              let resolver =
                  { new II18nResolver with
                      member _.Resolve(key, _) = sprintf "[i18n:%s]" key }

              let sources =
                  { BindingResolver.empty with
                      I18nResolver = resolver }

              let binding: Binding<string> = Binding.I18n("nonexistent.key", Option.None)
              let resolution = BindingResolver.resolve sources binding

              match resolution with
              | BindingResolver.I18nUnresolved key -> Expect.equal key "nonexistent.key" "key preserved"
              | other -> failtestf "Expected I18nUnresolved, got %A" other
          }

          test "makeI18nResolver consults the catalog map" {
              let catalog = Map.ofList [ "submit", "Submit"; "cancel", "Cancel" ]

              let sources =
                  { BindingResolver.empty with
                      I18nResolver = BindingResolver.makeI18nResolver catalog }

              let bindingSubmit: Binding<string> = Binding.I18n("submit", Option.None)
              let bindingMissing: Binding<string> = Binding.I18n("unknown", Option.None)

              Expect.equal
                  (BindingResolver.resolve sources bindingSubmit)
                  (BindingResolver.Resolved "Submit")
                  "Submit catalog entry resolves"

              match BindingResolver.resolve sources bindingMissing with
              | BindingResolver.I18nUnresolved key -> Expect.equal key "unknown" "missing key surfaces"
              | other -> failtestf "Expected I18nUnresolved, got %A" other
          }

          test "makeI18nResolver substitutes {argName} placeholders from args" {
              let catalog = Map.ofList [ "welcome", "Hello, {name}! You have {count} messages." ]

              let sources =
                  { BindingResolver.empty with
                      I18nResolver = BindingResolver.makeI18nResolver catalog }

              // F# 10 nullness: `box _` types as `obj | null`; Binding.Static
              // expects non-null `obj` for the typed payload. Launder via
              // Unchecked.nonNull (same pattern Tests.fs uses for its `nn`
              // helper).
              let args: Map<string, Binding<obj>> =
                  Map.ofList
                      [ "name", Binding.Static(box "Anna" |> Unchecked.nonNull)
                        "count", Binding.Static(box 3 |> Unchecked.nonNull) ]

              let binding: Binding<string> = Binding.I18n("welcome", Some args)
              let resolution = BindingResolver.resolve sources binding

              Expect.equal
                  resolution
                  (BindingResolver.Resolved "Hello, Anna! You have 3 messages.")
                  "Both placeholders substituted"
          }

          test "I18n args resolve via the binding pipeline — Binding.State arg resolves to state value" {
              let catalog = Map.ofList [ "tab", "Tab {n}" ]

              let sources =
                  { BindingResolver.empty with
                      I18nResolver = BindingResolver.makeI18nResolver catalog
                      State = Map.ofList [ "currentTab", box 7 |> Unchecked.nonNull ] }

              let args: Map<string, Binding<obj>> =
                  Map.ofList [ "n", Binding.State("currentTab", box 0 |> Unchecked.nonNull) ]

              let binding: Binding<string> = Binding.I18n("tab", Some args)
              let resolution = BindingResolver.resolve sources binding

              Expect.equal resolution (BindingResolver.Resolved "Tab 7") "State-bound arg resolves through the pipeline"
          }

          test "tryResolve returns None for I18nUnresolved" {
              let binding: Binding<string> = Binding.I18n("missing", Option.None)
              let result = BindingResolver.tryResolve BindingResolver.empty binding

              Expect.equal result Option.None "tryResolve folds I18nUnresolved to None"
          } ]
