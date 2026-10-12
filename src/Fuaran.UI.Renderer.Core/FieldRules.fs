module Fuaran.UI.Renderer.FieldRules

// ============================================================================
//  Fuaran — declared FIELD RULES (`FormField.Rule`, Phase 864), evaluated ONCE
//  (Phase 2070).
//
//  Two tiers evaluate a declared rule: the client renderer's submit gate (an
//  affordance — it must not submit while a rule is unmet) and the server-driven
//  tier's re-check (`Fuaran.UI.ServerDriven.FormValidation`, the trust floor a
//  hostile client cannot bypass). They must refuse the SAME submissions, so the
//  rule's semantics live here, once, and each tier calls it over its own value
//  type through the small adapter below. Two copies of these functions used to
//  sit one in each tier, byte-identical in places and structurally the same in
//  the rest — which is exactly how two tiers come to disagree about a value.
//
//  What this module does NOT do, deliberately:
//
//   • evaluate `pattern`. The client never does (it is Fable-compiled and the
//     browser's native `pattern` check is the one definition), and the server
//     does it with a .NET regex under a match TIMEOUT, which has no Fable
//     equivalent. So the pattern check is a parameter: the server passes its
//     regex predicate, the client passes `None`.
//   • resolve the authored `message`. A `TextSource` resolves against a render
//     context the client has and the server-driven tier deliberately lacks, so
//     the message policy is a parameter too.
//
//  Pure and Fable-clean: no regex, no clock, no DOM.
// ============================================================================

open Fuaran.UI.Types

/// The value adapter: the shapes a rule can read, from whichever value type a
/// tier harvests (`JVal` on the client, `LiveValue` on the server). A tier maps
/// its own value onto this once, and the rule semantics never see the tier's
/// type.
[<RequireQualifiedAccess>]
type RuleValue =
    /// A string value — the only shape the length, format and pattern slots
    /// read.
    | Text of string
    /// A number, whichever spelling the tier holds it in (an int and a float
    /// are one shape here, as they are on the wire).
    | Number of float
    /// A boolean — equality only, no ordering.
    | Flag of bool
    /// Anything else (null, an array, an object): never comparable.
    | Other

/// Whitespace test written by enumeration rather than through
/// `Char.IsWhiteSpace`, so the semantics are identical on both pipelines.
let hasWhitespace (s: string) : bool =
    s |> Seq.exists (fun c -> c = ' ' || c = '\t' || c = '\n' || c = '\r')

/// The `format` check, DELIBERATELY structural rather than authoritative.
///
/// A strict RFC 5322 email validator is not what this slot is for. It rejects
/// deliverable addresses (quoted local parts, new gTLDs, IDN) and accepts
/// undeliverable ones, so it trades false rejections of real users for no
/// security gain — and the specification says out loud that a rule is not a
/// security boundary. What `format` declares is the CONTROL the field is: the
/// renderers project it as `<input type=email|url|tel>`, and the browser's
/// native check is itself structural. This mirrors that shape, so the client
/// gate also covers a programmatically-seeded value (several native checks
/// apply only once a control is user-dirty) and the server floor refuses the
/// same submissions the projected attribute does.
let matchesFormat (fmt: TextFormat) (s: string) : bool =
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
    // vary worldwide. Claiming a shape here would make the gate and the
    // projected attribute disagree, which is the one outcome the
    // shared-attribute design exists to prevent.
    | TextFormat.Tel -> true

/// The generated sentence for an unmet `format`.
let formatMessage (fmt: TextFormat) : string =
    match fmt with
    | TextFormat.Email -> "Enter an email address."
    | TextFormat.Url -> "Enter a URL."
    | TextFormat.Tel -> "Enter a telephone number."

/// The control `type` a `format` rule selects for a text input. An absent rule
/// or an absent `format` leaves the pre-864 `text`, byte-identically. Both
/// renderers emit it, so the control the browser checks is the same one on
/// every leg.
let textInputType (rule: FieldRule option) : string =
    match rule |> Option.bind (fun r -> r.Format) with
    | Some TextFormat.Email -> "email"
    | Some TextFormat.Url -> "url"
    | Some TextFormat.Tel -> "tel"
    | None -> "text"

/// The value of the `data-fuaran-field-compare` DECLARATION both renderers emit
/// for a `compare` slot: `<op>:<sibling key>`. RECORDED KNOWN LIMIT — `compare`
/// has no HTML equivalent; the attribute is a declaration a reader can see, not
/// a constraint any platform machinery reads. An operand that is not a `State`
/// key is written with an empty key, because it is not read on any tier.
let compareDeclaration (rule: CompareRule) : string =
    let opText =
        match rule.Op with
        | CompareOp.Eq -> "eq"
        | CompareOp.Neq -> "neq"
        | CompareOp.Lt -> "lt"
        | CompareOp.Lte -> "lte"
        | CompareOp.Gt -> "gt"
        | CompareOp.Gte -> "gte"

    let againstText =
        match rule.Against with
        | Binding.State(key, _) -> key
        | _ -> ""

    opText + ":" + againstText

/// Ordinal comparison of two values, or `None` when the two are not
/// comparable. WIRE_FORMAT: same-variant ISO-8601 strings compare
/// lexicographically in chronological order (an ORDINAL string compare — no
/// parsing, no locale — borrowed wholesale from the `DateRange` ordering);
/// numbers compare numerically; **a comparison between values of different
/// shapes is UNMET, not an error** — a half-filled form is a normal state, and
/// a shape mismatch a hostile client manufactures refuses the submission rather
/// than admitting it. A NaN is not a number the specification orders, so it is
/// not comparable either.
let private ordering (lhs: RuleValue) (rhs: RuleValue) : int option =
    match lhs, rhs with
    | RuleValue.Text x, RuleValue.Text y -> Some(sign (System.String.CompareOrdinal(x, y)))
    | RuleValue.Number x, RuleValue.Number y ->
        if System.Double.IsNaN x || System.Double.IsNaN y then None
        elif x < y then Some -1
        elif x > y then Some 1
        else Some 0
    | _ -> None

/// Does `lhs <op> rhs` hold? Anything not comparable is UNMET (`false`).
let meets (op: CompareOp) (lhs: RuleValue) (rhs: RuleValue) : bool =
    match op, lhs, rhs with
    // Booleans carry equality but no ordering. The specification defines
    // ordering for numbers and same-variant ISO-8601 strings only, so an
    // ordering operator over booleans is UNMET rather than given an invented
    // order — claiming less than an order nobody wrote down.
    | CompareOp.Eq, RuleValue.Flag x, RuleValue.Flag y -> x = y
    | CompareOp.Neq, RuleValue.Flag x, RuleValue.Flag y -> x <> y
    | _, RuleValue.Flag _, _
    | _, _, RuleValue.Flag _ -> false
    | _ ->
        match ordering lhs rhs with
        | None -> false
        | Some c ->
            match op with
            | CompareOp.Eq -> c = 0
            | CompareOp.Neq -> c <> 0
            | CompareOp.Lt -> c < 0
            | CompareOp.Lte -> c <= 0
            | CompareOp.Gt -> c > 0
            | CompareOp.Gte -> c >= 0

/// The first unmet slot of one field's declared rule, as the message a tier
/// shows — or `None` when the rule is met or absent. The FIRST unmet slot wins,
/// in the order `minLength`, `maxLength`, `format`, `pattern`, `compare`: one
/// message per field, the shape the `data-fuaran-field-error` marker carries on
/// both tiers.
///
/// - `pattern` evaluates a declared `pattern` against a non-empty string value
///   (`None` where the tier leaves `pattern` to the browser).
/// - `message` resolves the authored `message` where the tier can; where it
///   returns `None` the generated sentence stands. The authored message is the
///   prose for SOME slot being unmet — one message per rule, not per slot — so
///   it replaces the generated sentence wholesale.
/// - `valueOf` reads a field's harvested value by id, already adapted.
///
/// An EMPTY value is not length-, format- or pattern-checked: that is exactly
/// what the browser does with `minlength` / `type=email` / `pattern` on a
/// control the author did not mark required, and the whole point of projecting
/// into the platform's attributes is that the two agree. `required` is the slot
/// that makes emptiness a failure. Length rules read a STRING value only — a
/// rule never duplicates a bound its control already holds, so a numeric
/// control's bounds are `RangedNumber`'s `min`/`max`, never a length.
let unmet
    (pattern: (string -> string -> bool) option)
    (message: TextSource -> string option)
    (valueOf: string -> RuleValue option)
    (field: FormField<'Msg>)
    : string option =
    match field.Rule with
    | None -> None
    | Some rule ->
        let value = valueOf field.Id

        let lengthFormatPattern =
            match value with
            | Some(RuleValue.Text s) when s <> "" ->
                [ (match rule.MinLength with
                   | Some n when s.Length < n -> Some(sprintf "Must be at least %d characters." n)
                   | _ -> None)
                  (match rule.MaxLength with
                   | Some n when s.Length > n -> Some(sprintf "Must be at most %d characters." n)
                   | _ -> None)
                  (match rule.Format with
                   | Some fmt when not (matchesFormat fmt s) -> Some(formatMessage fmt)
                   | _ -> None)
                  (match pattern, rule.Pattern with
                   | Some matches, Some p when not (matches p s) -> Some "This value is not in the required format."
                   | _ -> None) ]
                |> List.tryPick id
            | _ -> None

        let compareUnmet =
            match rule.Compare with
            | None -> None
            | Some cmp ->
                match cmp.Against with
                | Binding.State(key, _) ->
                    // The sibling value is read from the SAME harvest by the
                    // binding's `State` key — the shape WIRE_FORMAT describes,
                    // where a form field auto-binds `State(<its own id>)` so a
                    // sibling needs no addressing vocabulary of its own.
                    match value, valueOf key with
                    | Some l, Some r when meets cmp.Op l r -> None
                    | _ -> Some "This value does not match the field it is compared against."
                // RECORDED KNOWN LIMIT — only a `State`-keyed operand is read.
                // Any other binding shape names a source outside the harvest.
                // It is reported here rather than refused: refusing over an
                // operand nobody read would claim a check that was not
                // performed. A server host that can resolve such an operand
                // enforces it in its own validator, on top of this floor.
                | _ -> None

        [ lengthFormatPattern; compareUnmet ]
        |> List.tryPick id
        |> Option.map (fun generated -> rule.Message |> Option.bind message |> Option.defaultValue generated)
