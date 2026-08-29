module Fuaran.UI.ServerDriven.FormValidation

open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

// ============================================================================
//  Runtime form validation — validate → error-patch round-trip (Phase 156).
//
//  Fuaran's `Fuaran.UI.Validator` is BUILD-TIME (an F# AST walker). The forms /
//  wizard / configurator app class — a server-driven sweet spot — needs a
//  *runtime* per-field feedback loop: a user submits values, the server validates
//  them against business rules, and field-level errors come back to the form.
//
//  This module is that round-trip on the server-driven path. On a form `submit`
//  (or a per-field commit) the driver runs a server-side validator over the
//  submitted values; a non-empty `FieldError list` lowers to closure-free
//  `DomPatch`es that mark the offending fields (`data-fuaran-field-error` +
//  message) and the state mutation is SUPPRESSED — no full re-render, the rest of
//  the form keeps its values + focus. The validation logic lives server-side
//  (where the rules + data are) and is never shipped to the browser.
//
//  ── Two layers, declared is non-bypassable ─────────────────────────────────
//  `enforceDeclared` re-checks the build-time-declared constraints (`Required`,
//  `RangedNumber` Min/Max, and — Phase 864 — a `FormField.Rule`) SERVER-SIDE at
//  runtime — the build-time validator
//  cannot catch a hostile client bypassing HTML5 validation, so "client
//  validation is not a trust boundary" is closed here, composing with the 152 G1
//  inbound gate. The host `FormValidator` adds business rules ON TOP; the driver
//  integration always runs declared enforcement first, so the trust floor holds
//  regardless of what the host supplies.
//
//  Phase 864 puts that posture in the wire format's own words: a declared rule
//  "is not a security boundary" and client enforcement "is an affordance, not a
//  gate", so "a host that accepts submissions MUST re-check every declared
//  constraint server-side". This module is where that re-check happens.
//
//  ── Per-field vs whole-form ────────────────────────────────────────────────
//  `validate` operates on a `Map<fieldId, LiveValue>`: one entry for a per-field
//  commit (validate-on-commit, as the user leaves a field), every field for a
//  whole-form submit (validate-on-submit). Both lower to the SAME field-error
//  patch shape, so the shim handles them identically.
//
//  Pure: `FieldError` / `enforceDeclared` / `lower` are functions of
//  `(form, values)` → data, so they unit-test headlessly and carry no
//  transport / renderer dependency (the driver step wrapper reuses the shipped
//  `Driver.step`).
//
//  NOT Fable-portable since Phase 864, and deliberately. `matchesPattern` uses
//  `System.Text.RegularExpressions` with a match TIMEOUT — the construct that
//  bounds catastrophic backtracking, and the one this file exists to have,
//  since a pathological pattern costs a server here rather than a tab. Fable
//  maps `Regex` onto JS `RegExp`, which has no timeout, so the guard could not
//  survive the translation. This module is server-tier by construction anyway
//  (it reads `Driver`'s `LiveSession`), so nothing under this package's `fable/`
//  content reaches it; the client tier gets its own gate, in the renderer, over
//  the attributes the browser enforces natively.
// ============================================================================

/// A per-field validation failure: the field to mark + the message to show.
type FieldError = { FieldId: string; Message: string }

/// The submitted form: the form node id, its spec, and the submitted field
/// values off the wire (keyed by field id). `Values` carries one entry on a
/// per-field commit, every field on a whole-form submit.
type FormSubmission<'Msg> =
    { FormNodeId: string
      Form: FormSpec<'Msg>
      Values: Map<string, LiveValue> }

/// Host-supplied business-rule validator. Runs server-side (where the rules +
/// data live); never shipped to the browser. Returns the field-level failures
/// (empty = the submission is valid as far as the host is concerned).
type FormValidator<'Msg> = FormSubmission<'Msg> -> FieldError list

// ─── declared-constraint enforcement (the trust-boundary floor) ───────────────

let private isEmptyValue (v: LiveValue option) : bool =
    match v with
    | None
    | Some LiveValue.Null
    | Some(LiveValue.Str "") -> true
    | _ -> false

let private asNumber (v: LiveValue option) : float option =
    match v with
    | Some(LiveValue.Num n) -> Some n
    | Some(LiveValue.Str s) ->
        match System.Double.TryParse s with
        | true, n -> Some n
        | _ -> None
    | _ -> None

// ─── declared FIELD RULES (`FormField.Rule`, Phase 864) ──────────────────────

/// Whitespace by enumeration rather than `Char.IsWhiteSpace`, so this check has
/// the same semantics as the client renderer's copy of it.
let private hasWhitespace (s: string) : bool =
    s |> Seq.exists (fun c -> c = ' ' || c = '\t' || c = '\n' || c = '\r')

/// The `format` check, DELIBERATELY simple — and the comment is the point,
/// because "simple" here is a decision rather than a shortcut.
///
/// A strict RFC 5322 email validator is not what this slot is for. It rejects
/// deliverable addresses (quoted local parts, new gTLDs, IDN) and accepts
/// undeliverable ones, so it trades false rejections of real users for no
/// security gain — and the specification says out loud that a rule is not a
/// security boundary. What `format` declares is the CONTROL the field is: the
/// client projects it as `<input type=email|url|tel>`, and the browser's native
/// check is itself structural, not authoritative. This mirrors that structural
/// shape so the two tiers agree on which submissions are refused; the authority
/// on whether an address exists is a confirmation email, not a regex.
let private matchesFormat (fmt: TextFormat) (s: string) : bool =
    match fmt with
    // Exactly what `<input type=email>` demands: one `@`, something either side
    // of it, no whitespace.
    | TextFormat.Email ->
        let parts = s.Split('@')
        parts.Length = 2 && parts[0] <> "" && parts[1] <> "" && not (hasWhitespace s)
    // `<input type=url>` demands an ABSOLUTE URL — a scheme and a body. The
    // scheme set is not enumerated here because the control does not enumerate
    // one either.
    | TextFormat.Url -> not (hasWhitespace s) && s.Contains "://" && not (s.StartsWith "://")
    // `<input type=tel>` enforces NOTHING natively, because telephone formats
    // vary worldwide. Inventing a shape here would make this floor refuse
    // submissions the client tier accepts, which is the one divergence the
    // shared-attribute design exists to prevent.
    | TextFormat.Tel -> true

/// The ceiling on evaluating one declared `pattern`.
///
/// `pattern` is ECMA-262 source arriving from a decoded tree, so a pathological
/// alternation is a catastrophic-backtracking vector against THIS process — the
/// one tier where that costs a server rather than a tab. A short timeout bounds
/// it; the value is generous for any pattern a form field legitimately carries
/// and far below anything an operator would notice.
let private patternTimeout = System.TimeSpan.FromMilliseconds 100.0

/// Does the value satisfy the declared `pattern`? HTML `pattern` semantics —
/// implicitly anchored to the WHOLE value, which is what the specification chose
/// so the browser, a static projection and this re-check agree without a second
/// definition of the rule.
///
/// Two failure modes are caught deliberately, and BOTH resolve to MET:
///   • a malformed pattern (`ArgumentException`, which `RegexParseException`
///     derives from) — a rule that is not a regex constrains nothing, and
///     turning an author's typo into a form nobody can submit is worse than the
///     slot not binding. Refusing a malformed pattern is a decode-time job (the
///     spec already makes three rule shapes decode errors), not this tier's.
///   • a timeout — resolving it to UNMET would let one hostile submission deny
///     a legitimate one purely by choosing a slow input.
/// The two failure classes above, as a predicate — an EXCEPTION FILTER rather
/// than two `with` arms, so the one arm that has no meaning under Fable can be
/// dropped at a declaration boundary instead of inside the handler.
///
/// A filter is the shape that keeps the .NET semantics exactly as they were:
/// `false` re-raises, so every exception outside these two classes still
/// propagates — a catch-all `| _ -> true` would silently swallow them, which is
/// a widening this tier must not do.
let private isBenignPatternFailure (ex: exn) : bool =
#if FABLE_COMPILER
    // Fable lowers `Regex` onto the JavaScript `RegExp`, which has no match
    // timeout at all — so `RegexMatchTimeoutException` cannot be raised on this
    // path, and the type test for it is one Fable refuses to emit (it can only
    // ever evaluate to false). The malformed-pattern class is the one that
    // survives; everything else propagates, as on .NET.
    ex :? System.ArgumentException
#else
    match ex with
    | :? System.ArgumentException -> true
    | :? System.Text.RegularExpressions.RegexMatchTimeoutException -> true
    | _ -> false
#endif

let private matchesPattern (pattern: string) (s: string) : bool =
    try
        // `\A(?:…)\z` is HTML `pattern`'s whole-value anchoring. The author's
        // source is WRAPPED rather than concatenated, so a top-level
        // alternation in it cannot escape the anchors.
        System.Text.RegularExpressions.Regex.IsMatch(
            s,
            "\\A(?:" + pattern + ")\\z",
            System.Text.RegularExpressions.RegexOptions.None,
            patternTimeout
        )
    with ex when isBenignPatternFailure ex ->
        true

/// Does `lhs <op> rhs` hold over two submitted values?
///
/// WIRE_FORMAT: same-variant ISO-8601 strings compare lexicographically in
/// chronological order (an ORDINAL string compare — no parsing, no locale, total
/// for every variant, borrowed wholesale from the `DateRange` ordered-pair
/// rule); numbers compare numerically; and **a comparison between values of
/// different shapes is UNMET, not an error** — a half-filled form is a normal
/// state. Note the direction that makes for a trust floor: a shape mismatch a
/// hostile client manufactures (posting a number as a string, say) refuses the
/// submission rather than admitting it.
let private compareLive (op: CompareOp) (lhs: LiveValue) (rhs: LiveValue) : bool =
    let ordering =
        match lhs, rhs with
        | LiveValue.Str x, LiveValue.Str y -> Some(System.String.CompareOrdinal(x, y))
        | LiveValue.Num x, LiveValue.Num y -> Some(compare x y)
        | _ -> None

    match op, lhs, rhs with
    // Booleans carry equality but no ordering: the specification defines
    // ordering for numbers and same-variant ISO-8601 strings only, so an
    // ordering operator over booleans is UNMET rather than given an invented
    // order.
    | CompareOp.Eq, LiveValue.Bool x, LiveValue.Bool y -> x = y
    | CompareOp.Neq, LiveValue.Bool x, LiveValue.Bool y -> x <> y
    | _, LiveValue.Bool _, _
    | _, _, LiveValue.Bool _ -> false
    | _ ->
        match ordering with
        | None -> false
        | Some c ->
            match op with
            | CompareOp.Eq -> c = 0
            | CompareOp.Neq -> c <> 0
            | CompareOp.Lt -> c < 0
            | CompareOp.Lte -> c <= 0
            | CompareOp.Gt -> c > 0
            | CompareOp.Gte -> c >= 0

/// THE MESSAGE SEAM. `FieldRule.Message` is a `TextSource`; `FieldError.Message`
/// is a plain string, because a field error lowers to a closure-free `DomPatch`
/// attribute value.
///
/// A `TextSource` is resolved against a render context — `Bound` reads a binding
/// and `I18n` reads a catalogue — and this tier HAS no render context by design:
/// it is the transport-agnostic validation core, and the HTML renderer is
/// injected precisely so no renderer dependency leaks in. So `Literal` resolves
/// to its text and every other shape falls back to the generated sentence, which
/// is a real message rather than a rendered key or an empty attribute. A host
/// that wants its authored `Bound` / `I18n` message on the wire resolves it in
/// its own `FormValidator` and returns the `FieldError` itself — that is what the
/// host-validator seam is for.
let private ruleMessage (rule: FieldRule) (generated: string) : string =
    match rule.Message with
    | Some(TextSource.Literal text) -> text
    | _ -> generated

/// Every declared-rule slot re-checked over one field's submitted value. The
/// FIRST unmet slot wins, matching the one-message-per-field shape the rest of
/// this module and the `data-fuaran-field-error` patch already carry.
let private checkRule (values: Map<string, LiveValue>) (field: FormField<'Msg>) : FieldError option =
    match field.Rule with
    | None -> None
    | Some rule ->
        let value = Map.tryFind field.Id values

        let text =
            match value with
            | Some(LiveValue.Str s) -> Some s
            | _ -> None

        // An EMPTY value is not length-, format- or pattern-checked: that is
        // exactly what the browser does with `minlength` / `type=email` /
        // `pattern` on a control the author did not mark required, and this
        // floor must refuse the same submissions the projected attributes do,
        // not more. `Required` above is the slot that makes emptiness a
        // failure.
        //
        // Length rules read a STRING value only — a rule never duplicates a
        // bound its control already holds, so a numeric control's bounds are
        // `RangedNumber`'s `min`/`max`, never a length.
        let lengthFormatPattern =
            match text with
            | Some s when s <> "" ->
                [ (match rule.MinLength with
                   | Some n when s.Length < n -> Some(sprintf "Must be at least %d characters." n)
                   | _ -> None)
                  (match rule.MaxLength with
                   | Some n when s.Length > n -> Some(sprintf "Must be at most %d characters." n)
                   | _ -> None)
                  (match rule.Format with
                   | Some fmt when not (matchesFormat fmt s) ->
                       Some(
                           match fmt with
                           | TextFormat.Email -> "Enter an email address."
                           | TextFormat.Url -> "Enter a URL."
                           | TextFormat.Tel -> "Enter a telephone number."
                       )
                   | _ -> None)
                  (match rule.Pattern with
                   | Some p when not (matchesPattern p s) -> Some "This value is not in the required format."
                   | _ -> None) ]
                |> List.tryPick id
            | _ -> None

        let compareUnmet =
            match rule.Compare with
            | None -> None
            | Some cmp ->
                match cmp.Against with
                | Binding.State(key, _) ->
                    // The sibling value is read from THIS submission's own
                    // values by the binding's `State` key — the shape
                    // WIRE_FORMAT describes, where a form field auto-binds
                    // `State(<its own id>)` so a sibling needs no addressing
                    // vocabulary of its own.
                    match value, Map.tryFind key values with
                    | Some l, Some r when compareLive cmp.Op l r -> None
                    | _ -> Some "This value does not match the field it is compared against."
                // RECORDED KNOWN LIMIT — only a `State`-keyed operand is read
                // here. Any other binding shape (`Query` / `Selection` /
                // `Filter` / …) names a source outside the submission, and this
                // tier holds only the submission. It is reported here rather
                // than refused, because refusing over an operand nobody read
                // would claim a check that was not performed; a host that can
                // resolve such an operand enforces it in its own
                // `FormValidator`, which composes ON TOP of this floor.
                | _ -> None

        [ lengthFormatPattern; compareUnmet ]
        |> List.tryPick id
        |> Option.map (fun generated ->
            { FieldId = field.Id
              Message = ruleMessage rule generated })

let private checkField (values: Map<string, LiveValue>) (field: FormField<'Msg>) : FieldError option =
    let value = Map.tryFind field.Id values

    if field.Required && isEmptyValue value then
        Some
            { FieldId = field.Id
              Message = "This field is required." }
    else
        let kindError =
            match field.Kind with
            | FormFieldKind.RangedNumber(_, _, cMin, cMax, _) ->
                match asNumber value with
                | Some n ->
                    match cMin, cMax with
                    | Some lo, _ when n < lo ->
                        Some
                            { FieldId = field.Id
                              Message = sprintf "Must be at least %g." lo }
                    | _, Some hi when n > hi ->
                        Some
                            { FieldId = field.Id
                              Message = sprintf "Must be at most %g." hi }
                    | _ -> None
                // Non-numeric / absent value on a non-required field: `Required`
                // already handled the empty case; nothing further to range-check.
                | None -> None
            | _ -> None

        // Phase 864 — the declared `rule` is checked AFTER the control's own
        // bounds, because a rule never duplicates a bound its control already
        // holds: the two constrain different things, and reporting the control's
        // own bound first keeps the message closest to what the user just typed.
        match kindError with
        | Some _ -> kindError
        | None -> checkRule values field

/// Re-check the build-time-declared constraints (`Required`, `RangedNumber`
/// Min/Max, and a `FormField.Rule`'s `minLength` / `maxLength` / `format` /
/// `pattern` / `compare`) over the submitted values, SERVER-SIDE. The trust
/// floor: a client bypassing HTML5 validation is still rejected. Composes with
/// G1 (152).
let enforceDeclared (submission: FormSubmission<'Msg>) : FieldError list =
    submission.Form.Fields |> List.choose (checkField submission.Values)

/// Combine declared enforcement (first) with a host business-rule validator.
/// The result is the validator the driver integration runs.
let combine (host: FormValidator<'Msg>) : FormValidator<'Msg> =
    fun submission -> enforceDeclared submission @ host submission

/// The no-op host validator — declared enforcement only.
let declaredOnly<'Msg> : FormValidator<'Msg> = fun _ -> []

// ─── field-error lowering ─────────────────────────────────────────────────────

/// Lower the field-error state to closure-free `DomPatch`es. Emits the FULL
/// state for every field of the form: an erroring field gets
/// `data-fuaran-field-error="<message>"`; every other field has the attribute
/// REMOVED — so the lowering is idempotent + self-correcting (a field that just
/// became valid drops its stale error without a separate clear pass). The host
/// CSS keys off the attribute to surface the message inline.
let lower (form: FormSpec<'Msg>) (errors: FieldError list) : DomPatch list =
    let errorByField = errors |> List.map (fun e -> e.FieldId, e.Message) |> Map.ofList

    [ for field in form.Fields ->
          match Map.tryFind field.Id errorByField with
          | Some message -> DomPatch.SetAttr(field.Id, "data-fuaran-field-error", message)
          | None -> DomPatch.RemoveAttr(field.Id, "data-fuaran-field-error") ]

// ─── driver integration (whole-form submit) ──────────────────────────────────

/// Step a session with form-aware validation. On a form `submit` event the
/// declared constraints + the host `validator` run over the submitted values
/// (`ev.Payload`, keyed by field id) BEFORE any mutation:
///   • invalid → the session is UNCHANGED, the output carries only the
///     field-error patches (mutation suppressed; `Rejected = None` — this is a
///     legitimate, gated submit that failed business validation, not a G1
///     boundary breach).
///   • valid → the field-error attributes are cleared and the normal
///     `Driver.step` runs (interpret → update → diff → lower).
/// Every non-form event delegates straight to `Driver.step`.
let stepWithValidation
    (validator: FormValidator<'Msg>)
    (session: LiveSession<'Model, 'Msg>)
    (ev: LiveEvent)
    : LiveSession<'Model, 'Msg> * StepOutput =
    match findNode (NodeId ev.NodeId) session.Tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Form(form) when ev.Event = "submit" ->
            let submission =
                { FormNodeId = ev.NodeId
                  Form = form
                  Values = ev.Payload }

            // Declared enforcement is non-bypassable — always first.
            let errors = enforceDeclared submission @ validator submission

            match errors with
            | [] ->
                let s2, out = step session ev

                s2,
                { out with
                    Patches = lower form [] @ out.Patches }
            | _ ->
                session,
                { Patches = lower form errors
                  Effects = []
                  Rejected = None }
        | _ -> step session ev
    | None -> step session ev
