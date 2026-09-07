module Fuaran.UI.Tests.LocalWireTests

// ============================================================================
//  Fuaran-UI Phase 1538 — the WIRE half of `Binding.Local`, and the decoded
//  `Binding.Computed`.
//
//  `LocalBindingTests.fs` beside this one tests the CONSTRUCTOR: what
//  `binding.local` builds and how the resolver reads through it. This file
//  tests what survives DECODE, which is a different question and the one the
//  corpus fixture was answering wrongly:
//  `wire-format-fixtures/nodes/form-local-debounce.json` carries three
//  `"<closure>"` sentinels, and until this phase they restored to a `format`
//  returning `""`, a `parse` returning `Error "<closure>"` and an `onCommit`
//  returning a sentinel object — so a wire-authored debounced input rendered
//  empty and could never commit a keystroke.
//
//  Every assertion below is written to FAIL on the pre-1538 restores. The
//  identity tests fail because the old `format` answered `""` and the old
//  `parse` answered `Error`; the codec tests fail because there was no codec;
//  the `Computed` test fails because the old stand-in RESOLVED the slot's zero
//  rather than raising, which is the whole finding — a wrong answer
//  indistinguishable at the slot from a right one.
// ============================================================================

#nowarn "3261"

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops

// ─── helpers ────────────────────────────────────────────────────────────────

/// The `Binding` in a one-field form's value slot, at `'T = string`.
let private textFieldBinding (json: string) : Binding<string> =
    match JsonDecode.decodeNodeObj json with
    | Error e -> failtestf "decode failed: %s at %s — %s" e.Code e.Path e.Message
    | Ok node ->
        match node.Kind with
        | NodeKind.Form spec ->
            match spec.Fields with
            | [ f ] ->
                match f.Kind with
                | FormFieldKind.Text(Some b, _) -> b
                | other -> failtestf "expected a Text field with a value binding, got %A" other
            | fs -> failtestf "expected exactly one field, got %d" (List.length fs)
        | other -> failtestf "expected a Form, got %A" other

/// The `Binding` in a one-field form's value slot, at `'T = float`.
let private numberFieldBinding (json: string) : Binding<float> =
    match JsonDecode.decodeNodeObj json with
    | Error e -> failtestf "decode failed: %s at %s — %s" e.Code e.Path e.Message
    | Ok node ->
        match node.Kind with
        | NodeKind.Form spec ->
            match spec.Fields with
            | [ f ] ->
                match f.Kind with
                | FormFieldKind.Number(Some b, _) -> b
                | other -> failtestf "expected a Number field with a value binding, got %A" other
            | fs -> failtestf "expected exactly one field, got %d" (List.length fs)
        | other -> failtestf "expected a Form, got %A" other

/// The corpus fixture, verbatim — the bytes this phase must leave unchanged.
let private debounceFixtureJson =
    """{"id":"form-local-debounce","kind":{"$type":"Form","fields":[{"id":"email-input","kind":{"$type":"Text","onChange":"<closure>","value":{"$type":"Local","flushOn":{"$type":"OnDebounce","milliseconds":250},"format":"<closure>","initialFrom":{"$type":"Static","value":"draft@example.com"},"onCommit":"<closure>","parse":"<closure>"}},"label":"Email","required":true}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""

let private declaredFixtureJson =
    """{"id":"form-local-declared","kind":{"$type":"Form","fields":[{"id":"unit-price","kind":{"$type":"Number","value":{"$type":"Local","codec":{"$type":"Number","decimals":2},"commitTo":"order.unitPrice","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":0,"key":"order.unitPrice"},"parse":"<closure>"}},"label":"Unit price","required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""

/// A bare numeric `Local` with no codec — the identity at `'T = float`.
let private plainNumberLocalJson =
    """{"id":"f","kind":{"$type":"Form","fields":[{"id":"qty","kind":{"$type":"Number","value":{"$type":"Local","commitTo":"order.qty","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":0,"key":"order.qty"},"parse":"<closure>"}},"label":"Qty","required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""

let private wrongTypeCode =
    JsonDecode.DecodeErrorCode.toString JsonDecode.DecodeErrorCode.WRONG_TYPE

// ============================================================================

[<Tests>]
let tests =
    testList
        "Fuaran-UI Phase 1538 — Binding.Local on the wire"
        [

          // ── the identity codec ────────────────────────────────────────────

          test "a decoded Local round-trips its value through format and parse — the fixture's own shape" {
              // The acceptance criterion, stated as the fixture states it: the
              // Static `initialFrom` carries `draft@example.com`, and the value
              // must survive a display and an edit unchanged. Before 1538 this
              // asserted `""` on the first line and never reached the second.
              match textFieldBinding debounceFixtureJson with
              | Binding.Local(_, format, initialFrom, onCommit, parse, codec, commitTo) ->
                  Expect.equal (format "draft@example.com") "draft@example.com" "format is the identity at a text slot"

                  Expect.equal (parse "draft@example.com") (Ok "draft@example.com") "parse reads the text back verbatim"

                  //  carries function slots, so it has no structural
                  // equality to compare against — the shape is matched instead.
                  match initialFrom with
                  | Binding.Static(Some v) ->
                      Expect.equal v "draft@example.com" "initialFrom survives, as it always did"
                  | other -> failtestf "expected Static, got %A" other

                  Expect.isSome onCommit "the fixture declares an onCommit closure, so the sentinel restores as Some"
                  Expect.isNone codec "the fixture declares no codec"
                  Expect.isNone commitTo "the fixture declares no commit target"
              | other -> failtestf "expected a Binding.Local, got %A" other
          }

          test "a decoded Local at a NUMERIC slot parses the number its text denotes" {
              // Type-directed with no reflection: `parse` runs the slot's own
              // decoder, so the same restored placeholder answers a string at a
              // text slot and a float at a numeric one.
              match numberFieldBinding plainNumberLocalJson with
              | Binding.Local(_, format, _, _, parse, _, _) ->
                  Expect.equal (parse "42.5") (Ok 42.5) "a numeric slot reads the number"
                  Expect.equal (format 42.5) "42.5" "and renders it back"
                  Expect.equal (parse "-1.5e3") (Ok -1500.0) "exponent form is JSON number grammar"

                  Expect.isError (parse "1,234") "a grouping separator is not the JSON number grammar"
                  Expect.isError (parse "not a number") "and neither is arbitrary text"
              | other -> failtestf "expected a Binding.Local, got %A" other
          }

          test "the identity format spells booleans and numbers the way the wire does, not the way .NET does" {
              // `string true` is `True` on .NET and `true` in JavaScript; the
              // buffer has to read the same on every host, so the rendition goes
              // through the same canonical renderer the corpus bytes do.
              Expect.equal (HostPrelude.LocalCodec.identityFormat (box true)) "true" "lower-case, JSON spelling"
              Expect.equal (HostPrelude.LocalCodec.identityFormat (box false)) "false" "and its complement"
              Expect.equal (HostPrelude.LocalCodec.identityFormat (box 3.0)) "3" "a whole float has no decimal point"
              Expect.equal (HostPrelude.LocalCodec.identityFormat (box 3.5)) "3.5" "a fractional one does"
              Expect.equal (HostPrelude.LocalCodec.identityFormat (box "x")) "x" "a string is itself"
          }

          // ── the number grammar ────────────────────────────────────────────

          test "the buffer's number grammar is the JSON one, and nothing wider" {
              let accepts = [ "0"; "-0"; "0.5"; "-1.5e3"; "12"; "1E+2"; " 7 " ]
              let refuses = [ "+1"; ".5"; "1."; "01"; "1,234"; "0x10"; ""; "e5"; "1 2" ]

              for s in accepts do
                  Expect.isSome (HostPrelude.LocalCodec.tryNumberText s) (sprintf "'%s' is a JSON number" s)

              for s in refuses do
                  Expect.isNone (HostPrelude.LocalCodec.tryNumberText s) (sprintf "'%s' is not" s)
          }

          // ── the declared codec ────────────────────────────────────────────

          test "a declared Number codec renders at its decimals and inverts exactly" {
              match numberFieldBinding declaredFixtureJson with
              | Binding.Local(_, format, _, onCommit, parse, codec, commitTo) ->
                  Expect.equal codec (Some(Format.Number(Some 2))) "the codec rides the wire"
                  Expect.equal commitTo (Some "order.unitPrice") "and so does the commit target"
                  Expect.isNone onCommit "a declared commit target excludes the closure — they are mutually exclusive"

                  Expect.equal (format 3.14159) "3.14" "rendering rounds to the declared decimals"
                  Expect.equal (format 12.0) "12.00" "and pads to them"
                  Expect.equal (format -0.001) "0.00" "a negative that rounds to zero is not '-0'"

                  // The inverse is total on the TEXT the codec produced, which is
                  // the property a buffer needs; it is NOT total on values, and
                  // the specification says so rather than leaving it to be found.
                  Expect.equal
                      (parse (format 3.14159))
                      (Ok 3.14)
                      "parse after format is the identity on the codec's own text"

                  Expect.equal (parse "3.14159") (Ok 3.14159) "and a reader's full precision is kept on the way in"
              | other -> failtestf "expected a Binding.Local, got %A" other
          }

          test "fixed-point rendering rounds half away from zero and pads a bare fraction" {
              Expect.equal (HostPrelude.LocalCodec.fixedText 2 0.005) "0.01" "half rounds away from zero"
              Expect.equal (HostPrelude.LocalCodec.fixedText 2 -0.005) "-0.01" "in both directions"
              Expect.equal (HostPrelude.LocalCodec.fixedText 2 0.5) "0.50" "the integer part is present even at zero"
              Expect.equal (HostPrelude.LocalCodec.fixedText 0 2.5) "3" "zero decimals means no point at all"
              Expect.equal (HostPrelude.LocalCodec.fixedText 3 1.0) "1.000" "padding is to exactly the declared width"
          }

          // ── the two refusals ──────────────────────────────────────────────

          test "a codec case with no total inverse is refused, at the codec's own path" {
              let json =
                  """{"id":"f","kind":{"$type":"Form","fields":[{"id":"amount","kind":{"$type":"Number","value":{"$type":"Local","codec":{"$type":"Currency","isoCode":"GBP"},"commitTo":"order.amount","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":0,"key":"order.amount"},"parse":"<closure>"}},"label":"Amount","required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""

              match JsonDecode.decodeNodeObj json with
              | Ok _ -> failtest "a Currency codec has no parse; admitting it would format one way and read another"
              | Error e ->
                  Expect.equal
                      e.Code
                      wrongTypeCode
                      "the SetState value/valueFrom precedent — a combination, not a shape"

                  Expect.equal e.Path "$.kind.fields[0].kind.value.codec" "the path names the slot the author must fix"
          }

          test "a Number codec IS admitted — the refusal above is about the case, not about codecs" {
              // The go-red half of the test above: without it, a decoder that
              // refused every codec would pass the refusal assertion perfectly.
              match JsonDecode.decodeNodeObj declaredFixtureJson with
              | Error e -> failtestf "the admitted codec was refused: %s at %s" e.Code e.Path
              | Ok _ -> ()
          }

          test "onCommit and commitTo together are refused, at the commitTo path" {
              let json =
                  """{"id":"f","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"Local","commitTo":"form.email","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"Static","value":"a@b.c"},"onCommit":"<closure>","parse":"<closure>"}},"label":"Email","required":false}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""

              match JsonDecode.decodeNodeObj json with
              | Ok _ -> failtest "two commit destinations from one document would commit to two places"
              | Error e ->
                  Expect.equal e.Code wrongTypeCode "a field-combination refusal"
                  Expect.equal e.Path "$.kind.fields[0].kind.value.commitTo" "named at the declarative half"
          }

          test "either one ALONE decodes — the refusal is the pair" {
              match JsonDecode.decodeNodeObj debounceFixtureJson with
              | Error e -> failtestf "onCommit alone was refused: %s at %s" e.Code e.Path
              | Ok _ -> ()

              match JsonDecode.decodeNodeObj plainNumberLocalJson with
              | Error e -> failtestf "commitTo alone was refused: %s at %s" e.Code e.Path
              | Ok _ -> ()
          }

          // ── the write-back target ─────────────────────────────────────────

          test "a declared commitTo is the write-back destination FUARAN069 reads" {
              // The validator asks one question — where does this control's
              // change go — and a declared key answers it, overriding the
              // re-sync source's. A `Local` answering nothing is inert.
              match numberFieldBinding declaredFixtureJson with
              | Binding.Local _ as b ->
                  let target, opaque = BindingWalk.writeBackTargetOf b
                  Expect.equal target (Some "order.unitPrice") "the declared key is the destination"
                  Expect.isFalse opaque "and nothing opaque runs — there is no closure"
              | other -> failtestf "expected a Binding.Local, got %A" other

              // Neither closure nor key, over a non-writable re-sync source: the
              // inert-control condition, which is what the narrowing is for.
              let inert: Binding<string> =
                  Binding.Local(
                      LocalFlushTrigger.OnBlur,
                      (fun (v: string) -> v),
                      Binding.Static(Some "x"),
                      None,
                      Ok,
                      None,
                      None
                  )

              Expect.equal (BindingWalk.writeBackTargetOf inert) (None, false) "buffers a value with nowhere to put it"
          }

          // ── the decoded Computed ──────────────────────────────────────────

          test "a decoded Computed resolves to an ERROR naming its replacements — never to the slot's zero" {
              // The finding, in one assertion. Before 1538 the decoded `fn` was
              // `(fun _ -> Unchecked.defaultof<'T>)` and the resolver answered
              // `Resolved ""` here — a rendered empty string that a reader cannot
              // tell from a computation that ran and produced one.
              let json =
                  """{"id":"m","kind":{"$type":"Metric","label":"L","value":{"$type":"Computed","fn":"<closure>"}}}"""

              match JsonDecode.decodeNodeObj json with
              | Error e -> failtestf "the document is well-formed and must decode: %s at %s" e.Code e.Path
              | Ok node ->
                  match node.Kind with
                  | NodeKind.Metric spec ->
                      let sources = Fuaran.UI.Renderer.BindingResolver.empty

                      match Fuaran.UI.Renderer.BindingResolver.resolve<float> sources spec.Value with
                      | Fuaran.UI.Renderer.BindingResolver.Errored msg ->
                          Expect.equal
                              msg
                              HostPrelude.decodedComputedMessage
                              "the message is the remedy, surfaced verbatim"

                          Expect.stringContains msg "Binding.Expr" "and it names the case that replaced it"
                      | Fuaran.UI.Renderer.BindingResolver.Resolved v ->
                          failtestf "a decoded Computed resolved to %A — the silent-default defect this phase closes" v
                      | other -> failtestf "expected Errored, got %A" other
                  | other -> failtestf "expected a Metric, got %A" other
          }

          test "an in-process Computed still computes — the error is about DECODED ones only" {
              // The go-red half: a change that made every `Computed` error would
              // pass the test above and break the case for its actual users.
              let b: Binding<float> = Binding.Computed(fun _ -> 42.0)

              match Fuaran.UI.Renderer.BindingResolver.resolve<float> Fuaran.UI.Renderer.BindingResolver.empty b with
              | Fuaran.UI.Renderer.BindingResolver.Resolved v -> Expect.equal v 42.0 "the host closure runs"
              | other -> failtestf "expected Resolved, got %A" other
          } ]
