module Fuaran.UI.Renderer.RatingModel

// ============================================================================
//  Fuaran — the rating control's judgements, as pure functions (Phase 1130)
//
//  `FormFieldKind.Rating` puts three things on the wire and nothing else: the
//  size of the scale (`max`), whether entry moves in halves (`allowHalf`), and
//  where the value goes. Every keystroke, the star glyph, the partial fill and
//  the ARIA announcement are the renderers', under the affordance→op charter —
//  and every DECISION they make is here, in one place both renderers read.
//
//  ── Why here, and why pure ────────────────────────────────────────────────
//  Two reasons, and the second is the one that decided the file's home.
//
//  * The .NET test runner mounts no DOM (Feliz's .NET `ReactElement` is opaque,
//    so a rendered control's handler cannot be extracted and fired). A keyboard
//    model buried inside the React component would be unreachable by any test
//    in this repo; held out here, it is pinned directly.
//  * The hydrated control and the SSR floor must agree about what the same
//    figure LOOKS like and how it is READ OUT, and they are different projects
//    with no common renderer between them. `Fuaran.UI.Renderer.Core` is what
//    both reference — the `BindingResolver` / `SubmitPayload` precedent — so
//    putting the model here is the difference between one answer and two.
//
//  ── THE ARIA DECISION, recorded (the phase's a11y audit) ──────────────────
//  An interactive rating is `role="slider"`. NOT `role="radiogroup"`, and the
//  three reasons are worth keeping because the radiogroup reading is the one a
//  reader of the markup will reach for first (the stars LOOK like a set of
//  options):
//
//    1. A rating is a MAGNITUDE on a declared scale with a floor and a
//       ceiling, which is what `slider` means. `aria-valuenow` and
//       `aria-valuetext` are also the only ARIA members that can announce a
//       FRACTION at all — and the value slot is `Binding<float>` precisely so a
//       bound average (4.3 of 5) can be shown. A radiogroup has no spelling for
//       that figure; with `allowHalf` it would need 2·max radios to express one
//       continuous quantity.
//    2. The keyboard model does not change shape with the scale. One tab stop,
//       Arrow to adjust, Home / End to the ends — the APG slider pattern
//       exactly. A radiogroup is also one tab stop but needs a roving tabindex
//       over `max` elements, so the interaction's implementation grows with a
//       number the document chooses.
//    3. A radiogroup asserts mutually exclusive NAMED options. Stars are not
//       named options; they are positions. "Radio 3 of 5, not checked" is the
//       wrong sentence about a star, and no labelling fixes it, because the
//       role is what is wrong.
//
//  A rating that CANNOT be written — no handler, and a value binding nothing
//  can write back to (a `Query` average, the display case) — is `role="img"`
//  carrying the whole reading as its label, and takes no focus. A slider a
//  reader can focus and can never move is a fake affordance in the sense every
//  decline in this vocabulary's charter uses the term, and the honest markup
//  for a picture of a score is a picture.
//
//  ── And the SSR floor is a RADIO GROUP, which is not a contradiction ──────
//  Zero-JS, a `<span role="slider">` can be neither adjusted nor submitted, so
//  the static floor for a WRITABLE rating is native radios: the browser
//  supplies the group semantics, the arrow-key walk and the form submission
//  with no script of ours. The floor and the hydrated control differ because
//  what each medium can HONOUR differs — which is the same rule the whole ARIA
//  decision above rests on. A rating that cannot be written has no interaction
//  to floor, so both tiers emit the identical `role="img"` star row.
//  `docs/SSR.md` states the pair normatively.
// ============================================================================

// ─── the pure model ─────────────────────────────────────────────────────────

/// The granularity one keystroke moves, and the granularity a pointer commits
/// in. `allowHalf` is the only thing on the wire that can change it, and it can
/// only ever produce these two figures — which is why it is a bool and not a
/// `step` slot the document could fill with 0.3.
let step (allowHalf: bool) : float = if allowHalf then 0.5 else 1.0

/// Fold a figure into the scale `0 .. max`. Used on the way IN (a bound average
/// that overshoots is shown at the ceiling rather than drawn off the end) and
/// on the way OUT (a keystroke never leaves the scale).
///
/// It CLAMPS rather than refuses, deliberately: this is the render path, and a
/// control that vanished because its bound average was momentarily 5.2 would
/// take the whole form with it. The author is told about a static overshoot by
/// FUARAN132, and a submitted one is refused by the server-driven floor — both
/// places where refusing costs nothing a reader can see.
let clamp (max: int) (value: float) : float =
    if System.Double.IsNaN value then 0.0
    elif value < 0.0 then 0.0
    elif value > float max then float max
    else value

/// The nearest position a READER can enter, given the granularity. Applied to
/// what a keystroke or a pointer produces, never to what a binding resolves: a
/// bound 4.3 is displayed as 4.3 (that is the whole reason the slot is a
/// float), and only entry is quantised.
let snap (allowHalf: bool) (max: int) (value: float) : float =
    let s = step allowHalf
    clamp max (System.Math.Round(value / s) * s)

/// The keyboard model. `None` means "this key is not ours" — the handler leaves
/// it alone rather than swallowing it, so Tab, Enter and the host's own chords
/// still work.
///
/// Arrow RIGHT and UP increase; LEFT and DOWN decrease. Home is 0 and End is
/// the ceiling. Home going to ZERO rather than to one star is the deliberate
/// reading: `aria-valuemin` is 0, an empty row of stars is what "no rating"
/// looks like, and a reader who wants to take a rating back has no other
/// gesture for it.
let keyIntent (allowHalf: bool) (max: int) (current: float) (key: string) : float option =
    let s = step allowHalf
    let here = clamp max current

    match key with
    | "ArrowRight"
    | "ArrowUp" -> Some(clamp max (here + s))
    | "ArrowLeft"
    | "ArrowDown" -> Some(clamp max (here - s))
    | "Home" -> Some 0.0
    | "End" -> Some(float max)
    | _ -> None

/// How full each position is, left to right, as a fraction in `0.0 .. 1.0`.
/// One entry per position on the scale; a partial entry is what a bound average
/// looks like. The renderers draw this and nothing else, so a half star and a
/// 0.3 star come out of the same code path.
let fills (max: int) (value: float) : float list =
    let v = clamp max value

    [ for i in 1..max ->
          let filled = v - float (i - 1)

          if filled <= 0.0 then 0.0
          elif filled >= 1.0 then 1.0
          else filled ]

/// What a screen reader says. `aria-valuenow` carries the number; this carries
/// the SENTENCE, because "3.5" alone is not a rating and the scale is half the
/// fact. Whole figures read without a decimal point — "4 out of 5", not
/// "4.0 out of 5" — because that is what a reader would say aloud.
let valueText (max: int) (value: float) : string =
    let v = clamp max value

    let shown =
        if System.Math.Abs(v - System.Math.Round v) < 1e-9 then
            string (int (System.Math.Round v))
        else
            v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)

    shown + " out of " + string max

/// The class one position takes, from its fill. Three states rather than a
/// per-star inline width: the stylesheet owns the appearance, and an
/// EMPTY/PARTIAL/FULL vocabulary is what the class-coverage conformance suite
/// can enumerate. The exact fraction rides as a CSS custom property on the
/// partial position, which is a value and not a class.
let fillClass (fill: float) : string =
    if fill <= 0.0 then "fuaran-rating-star-empty"
    elif fill >= 1.0 then "fuaran-rating-star-full"
    else "fuaran-rating-star-partial"
