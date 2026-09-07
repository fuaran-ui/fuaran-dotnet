module Fuaran.UI.Tests.LocalBindingErrorTests

// ============================================================================
//  A `Binding.Local` parse refusal, and the edit that has to precede a commit.
//
//  Two defects, one file, because they are the same sentence from two sides:
//  the component was reporting a state nobody could observe (a parse error set
//  into React state that nothing rendered), and committing a value nobody had
//  typed (`OnDebounce` firing on mount and on every external re-seed).
//
//  What is assertable HERE and what is not. The refusal's ATTRIBUTES are pure
//  data and are pinned directly — including the claim that they are the SAME
//  markers the form gate already uses, which is the whole reason no new class
//  entered the vocabulary that is parity-locked with the other renderers. The
//  effect ORDERING is not: `React.useEffect` needs a browser, and this repo's
//  package graph carries neither jsdom nor react-test-renderer (the note at the
//  head of `LocalBindingTests` records that, and the behavioural coverage lives
//  in the catalog's Playwright spec). So the dirty-flag guard is pinned the way
//  `CssCoverageTests` pins the renderer's class vocabulary: by reading the
//  renderer's own sources, the copy the test project puts in this bin.
//
//  That source scan is a REGRESSION pin and is stated as one — it proves the
//  guard is still written, not that React honours it. The second half is the
//  browser's to prove and is not claimed here.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Renderer

// ── The refusal, as data ───────────────────────────────────────────────────

[<Tests>]
let attributeTests =
    testList
        "a Local parse refusal is visible"
        [ test "a field with nothing wrong carries no refusal attributes at all" {
              // The byte-identical-DOM claim: a valid field emits exactly what
              // it emitted before this change.
              Expect.isEmpty
                  (LocalBindings.invalidFieldAttributes "salary-input" None)
                  "no attributes when there is no error"
          }

          test "a refusal marks the input invalid, carries the message, and points at the slot" {
              let attrs =
                  LocalBindings.invalidFieldAttributes "salary-input" (Some "expected a number")

              Expect.equal
                  attrs
                  [ "aria-invalid", "true"
                    "data-fuaran-field-error", "expected a number"
                    "aria-describedby", "salary-input-error" ]
                  "the three markers, with the message verbatim"
          }

          test "the message reaches the reader VERBATIM — the parser's words, not a paraphrase" {
              // A `Binding.Local`'s `Parse` is the author's own function and its
              // Error string is the only explanation of what the field wanted.
              // Replacing it with a generic sentence here would discard the one
              // piece of information the reader needs.
              let authored = "salary must be a whole number of pounds"

              let carried =
                  LocalBindings.invalidFieldAttributes "salary-input" (Some authored)
                  |> List.tryPick (fun (k, v) -> if k = "data-fuaran-field-error" then Some v else None)

              Expect.equal carried (Some authored) "the parser's own sentence"
          }

          test "the markers are the ONES THE FORM GATE ALREADY USES, not a second vocabulary" {
              // `FieldRules.markUnmet` sets `data-fuaran-field-error` +
              // `aria-invalid` on an unmet field, taking both from the
              // server-driven tier's field-error patches. A parse refusal is a
              // different cause with the same consequence for a reader, so it
              // must present identically — one CSS hook, one screen-reader
              // announcement, no new class in the parity-locked vocabulary.
              //
              // Read off the renderer source rather than asserted from memory,
              // so the claim breaks if either side is renamed.
              let source =
                  Path.Combine(AppContext.BaseDirectory, "renderer-sources", "client", "Render.fs")

              Expect.isTrue (File.Exists source) "the renderer source is in the bin"
              let text = File.ReadAllText source

              let names = LocalBindings.invalidFieldAttributes "f" (Some "m") |> List.map fst

              for name in [ "aria-invalid"; "data-fuaran-field-error" ] do
                  Expect.contains names name (sprintf "the refusal sets %s" name)
                  Expect.stringContains text ("\"" + name + "\"") (sprintf "and markUnmet sets the same %s" name)
          }

          test "the slot id is derived from the field id, so two fields never share one" {
              let a = LocalBindings.errorSlotId "salary-input"
              let b = LocalBindings.errorSlotId "email-input"

              Expect.notEqual a b "distinct fields, distinct slots"
              Expect.stringContains a "salary-input" "and the slot names its field"
          } ]

// ── The dirty-flag guard, read off the source ──────────────────────────────

let private localBindingsSource: string =
    Path.Combine(AppContext.BaseDirectory, "renderer-sources", "client", "LocalBindings.fs")

/// The lines of one component's body, from its `let private render<name>` to
/// the next top-level `let`. Scoped per component because BOTH have to carry
/// the guard and a scan over the whole file would let one of them pass on the
/// other's strength.
let private componentBody (lines: string array) (declaration: string) : string array =
    match lines |> Array.tryFindIndex (fun l -> l.StartsWith declaration) with
    | None -> [||]
    | Some start ->
        let rest = lines[start + 1 ..]

        let stop =
            rest
            |> Array.tryFindIndex (fun l -> l.StartsWith "let " || l.StartsWith "type ")
            |> Option.defaultValue rest.Length

        rest[.. stop - 1]

[<Tests>]
let dirtyGuardTests =
    testList
        "OnDebounce commits only after a reader's edit"
        [ test "the renderer source is in the bin" {
              Expect.isTrue (File.Exists localBindingsSource) (sprintf "expected %s" localBindingsSource)
          }

          test "both Local components guard the debounce on the dirty flag, set it on change, and clear it on re-seed" {
              let lines = File.ReadAllLines localBindingsSource

              let components =
                  [ "the text input", "let private renderLocalText"
                    "the number input", "let private renderLocalNumber" ]

              let failures =
                  [ for (name, declaration) in components do
                        let body = componentBody lines declaration

                        if Array.isEmpty body then
                            yield sprintf "%s — declaration '%s' not found (renamed?)" name declaration
                        else
                            let has (needle: string) =
                                body |> Array.exists (fun l -> l.Contains needle)

                            // The flag exists.
                            if not (has "React.useRef false") then
                                yield sprintf "%s — no dirty ref" name

                            // The reader's keystroke is what sets it.
                            if not (has "dirty.current <- true") then
                                yield sprintf "%s — nothing sets the dirty flag on change" name

                            // A commit and an external re-seed both clear it.
                            if not (has "dirty.current <- false") then
                                yield sprintf "%s — nothing clears the dirty flag" name

                            // And the debounce arm is gated on it — without
                            // this the effect arms a timer on mount and on
                            // every re-seed, which is the defect.
                            if not (has "if not dirty.current then") then
                                yield sprintf "%s — the OnDebounce arm is not gated on the dirty flag" name ]

              Expect.isEmpty failures (sprintf "unguarded debounce:\n  %s" (String.Join("\n  ", failures)))
          }

          test "GO-RED TWIN: the per-component scan really is per component" {
              // Without this, `componentBody` returning the whole file would
              // make the assertion above pass whenever EITHER component carried
              // the guard. Two synthetic declarations, one guarded and one not.
              let lines =
                  [| "let private renderLocalText (props: LocalTextProps) : ReactElement ="
                     "    let dirty = React.useRef false"
                     "    ()"
                     ""
                     "let private renderLocalNumber (props: LocalNumberProps) : ReactElement ="
                     "    ()" |]

              let textBody = componentBody lines "let private renderLocalText"
              let numberBody = componentBody lines "let private renderLocalNumber"

              Expect.isTrue
                  (textBody |> Array.exists (fun l -> l.Contains "React.useRef false"))
                  "the guarded component's body carries the ref"

              Expect.isFalse
                  (numberBody |> Array.exists (fun l -> l.Contains "React.useRef false"))
                  "and the unguarded one's body does NOT — the scan does not leak across components"
          } ]
