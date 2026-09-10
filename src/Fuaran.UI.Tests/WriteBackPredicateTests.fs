module Fuaran.UI.Tests.WriteBackPredicateTests

// ============================================================================
//  Phase 1667 — the write-back predicate is ONE predicate, pinned by a test
//  rather than by the comment that claims it must be.
//
//  Three consumers ask the same question — "does this control's omitted-handler
//  write-back have somewhere to write" — and each chose different behaviour from
//  the answer: `PreEmitValidate` reports FUARAN069 when it is no, the client
//  renderer gives the rating control a different ARIA role, and the SSR renderer
//  emits a picture instead of an adjustable control. Until this phase each held
//  its OWN byte-identical copy of the three-line match, and each copy carried a
//  comment asserting the copies must stay one predicate. Three copies agree
//  until one of them is edited, which is the weakest possible form of that
//  guarantee, and the drift had in fact already happened in the other direction:
//  Phase 1538 narrowed the `Binding.Local` exemption in `writeBackTargetOf` and
//  said so in that function's doc comment, while all three copies went on
//  admitting EVERY `Local` unconditionally.
//
//  So there are two assertions here, and they pin different things:
//
//  * BEHAVIOURAL — FUARAN069's own emission agrees with
//    `BindingWalk.isWriteBackTarget` over a table of binding shapes that spans
//    the narrowing. This is the assertion that would catch a future edit to
//    either side, and it reaches the validator through its PUBLIC surface
//    (`PreEmitValidate.validate`) rather than the predicate it happens to call,
//    so it measures what a consumer sees.
//  * STRUCTURAL — a census over the renderer sources the test project copies
//    into its own output asserts neither renderer holds a `match` of its own.
//    No behavioural test can see a re-appearing copy: a fresh copy would AGREE
//    on the day it was written, which is exactly how the last one arrived.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types

type private Msg = Noop

/// A `Binding.Local` over the given re-sync source, with the two declarative
/// commit members supplied by the caller. Annotated constructors rather than a
/// helper with defaults, because the whole subject of this file is which of the
/// three destinations is present.
let private localOver
    (initialFrom: Binding<string>)
    (onCommit: (string -> obj) option)
    (commitTo: string option)
    : Binding<string> =
    Binding.Local(LocalFlushTrigger.OnBlur, (fun (v: string) -> v), initialFrom, onCommit, Ok, None, commitTo)

/// A one-field form whose Text field carries `binding` and NO change handler —
/// the shape FUARAN069 judges.
let private handlerFreeForm (binding: Binding<string>) : Node<Msg> =
    let field: FormField<Msg> =
        { Defaults.formField<Msg> with
            Id = "the-field"
            Kind = FormFieldKind.Text(Some binding, None) }

    Fuaran.form
        "frm"
        { Defaults.form<Msg> with
            Fields = [ field ] }

/// Whether `PreEmitValidate` reports the field inert — FUARAN069's own answer,
/// read through the validator's public surface.
let private validatorCallsItInert (binding: Binding<string>) : bool =
    match PreEmitValidate.validate (handlerFreeForm binding) with
    | Ok() -> false
    | Error defects ->
        defects
        |> List.contains (PreEmitValidate.PreEmitDefect.InertControl("frm", "FormField(the-field)"))

/// The table. Each row is (name, binding, expected-live), and the rows are
/// chosen so a predicate that got the narrowing wrong in EITHER direction fails:
/// four `Local` shapes, three of which are live for three different reasons and
/// one of which is not live at all.
let private cases: (string * Binding<string> * bool) list =
    [ "State", Binding.State("k", None), true
      "Filter with no declared default", Binding.Filter("f", None), true
      "Static", Binding.Static(Some "x"), false
      "Query", Binding.Query("q", (fun (_: obj) -> ""), None), false
      "Local with a declared commitTo", localOver (Binding.Static(Some "x")) None (Some "order.unitPrice"), true
      // The `onCommit` payload is an OPAQUE `obj` on the wire-facing shape, so
      // the closure's own return type says nothing here; what matters to the
      // predicate is only that a closure is present.
      "Local with an onCommit closure",
      localOver (Binding.Static(Some "x")) (Some(fun (v: string) -> Unchecked.nonNull (box v))) None,
      true
      "Local over a writable re-sync source", localOver (Binding.State("k", None)) None None, true
      "Local with none of the three", localOver (Binding.Static(Some "x")) None None, false ]

[<Tests>]
let tests =
    testList
        "Phase 1667 — one write-back predicate"
        [ test "FUARAN069 agrees with BindingWalk.isWriteBackTarget on every shape" {
              for name, binding, expectedLive in cases do
                  let predicate = BindingWalk.isWriteBackTarget binding
                  let inert = validatorCallsItInert binding

                  Expect.equal
                      predicate
                      expectedLive
                      (sprintf "BindingWalk.isWriteBackTarget disagrees with the table for %s" name)

                  Expect.equal
                      inert
                      (not predicate)
                      (sprintf
                          "FUARAN069 and the predicate have diverged for %s: the predicate says live=%b, the validator says inert=%b"
                          name
                          predicate
                          inert)
          }

          test "the narrowing is LIVE — a Local with nowhere to commit is inert" {
              // Stated separately from the table because it is the one row whose
              // answer this phase CHANGED, and a table whose expectations were
              // edited alongside a predicate would not say so. Before 1667 every
              // `Binding.Local` was admitted unconditionally, so this field was
              // silently accepted while `writeBackTargetOf` — the function the
              // two-writers check reads — already called it inert.
              let nowhere = localOver (Binding.Static(Some "x")) None None

              Expect.equal
                  (BindingWalk.writeBackTargetOf nowhere)
                  (None, false)
                  "it buffers a value with nowhere to put it"

              Expect.isFalse (BindingWalk.isWriteBackTarget nowhere) "so the write-back default has no target"
              Expect.isTrue (validatorCallsItInert nowhere) "and FUARAN069 says so"
          }

          // ── The census: the copies cannot silently come back ───────────────
          //
          // Reads the renderer sources the test project copies into its own
          // output (the FormFieldWriteBackHarness / HotPathVocabulary
          // precedent: copied rather than resolved by climbing, so the scan
          // cannot read a different checkout's sources than the ones this build
          // compiled).
          test "census: neither renderer holds a write-back predicate of its own" {
              for tier in [ "client"; "server" ] do
                  let path =
                      Path.Combine(AppContext.BaseDirectory, "renderer-sources", tier, "Render.fs")

                  if not (File.Exists path) then
                      failwithf
                          "renderer source not found at %s — Fuaran.UI.Tests copies the renderer sources into its output; check the Content items. A shape scan with no source to scan reports every tier as clean."
                          path

                  let source = File.ReadAllText path

                  // The declaration, then everything up to the next blank line
                  // that starts a new declaration — enough to hold a three-line
                  // match and nothing more. Located by declaration rather than
                  // by line number, which has already drifted twice.
                  let marker = "isWriteBackTarget (binding: Binding<'T>) : bool ="
                  let i = source.IndexOf(marker, StringComparison.Ordinal)

                  if i < 0 then
                      failwithf
                          "census marker not found in the %s renderer — the scan cannot report on a declaration it did not locate."
                          tier

                  let body = source.Substring(i + marker.Length)
                  let body = body.Substring(0, min 400 body.Length)

                  Expect.stringContains
                      body
                      "BindingWalk.isWriteBackTarget binding"
                      (sprintf
                          "the %s renderer must DELEGATE to the one definition in BindingWalk, not answer for itself"
                          tier)

                  // The negative half: no local match. A copy re-appearing would
                  // agree on the day it was written, which is how the last one
                  // arrived and why a behavioural test cannot see this.
                  Expect.isFalse
                      (body.Contains("| Binding.Local _ -> true", StringComparison.Ordinal))
                      (sprintf
                          "the %s renderer has re-grown a local write-back match — that is the drift Phase 1667 closed, and it admits every Local unconditionally"
                          tier)
          } ]
