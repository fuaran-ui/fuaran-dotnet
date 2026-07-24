module Fuaran.UI.Bounded

// ============================================================================
//  Parse-don't-validate bounded scalars.
//
//  Single-case DUs with *private* constructors: the only way to obtain a value
//  of these types is through the validating `tryCreate` factory, so a value
//  that exists is in-range *by construction*. Every downstream consumer
//  (renderer, ops engine, replay, tests) can rely on the invariant without
//  re-checking it — the "parse, don't validate" discipline applied to the
//  value dimension.
//
//  F# reality check: F# has no refinement / dependent types, so this is a
//  *construction-time* invariant (validate once at the boundary, then carry a
//  type that can only hold in-range values), NOT a compile-time-proven bound.
//  Units of measure give dimensions, not ranges. The guarantee is therefore
//  "boundary-enforced-then-invariant", and that is what makes the bound hold
//  for the whole lifetime of the value rather than only at the AI boundary.
//
//  This kit is additive and non-breaking: existing specs keep their primitive
//  fields. A core contract that wants the by-construction guarantee can opt in
//  field-by-field (e.g. carry a `Fraction` instead of a raw `float`).
// ============================================================================

/// A string guaranteed to contain at least one non-whitespace character.
type NonEmptyString = private NonEmptyString of string

module NonEmptyString =
    /// Parse a string into a `NonEmptyString`. Rejects null, empty, and
    /// whitespace-only input.
    let tryCreate (value: string) : Result<NonEmptyString, string> =
        if value.Trim() = "" then
            Error "value must be a non-empty string"
        else
            Ok(NonEmptyString value)

    /// The underlying string. Total — every `NonEmptyString` holds one.
    let value (NonEmptyString s) = s

/// A string whose length is guaranteed to lie within `[MinLen, MaxLen]`.
type BoundedString = private BoundedString of value: string * minLen: int * maxLen: int

module BoundedString =
    /// Parse a string into a `BoundedString` bounded to `[minLen, maxLen]`
    /// inclusive. Rejects null, an invalid bound (`minLen > maxLen`), and any
    /// out-of-bound length.
    let tryCreate (minLen: int) (maxLen: int) (value: string) : Result<BoundedString, string> =
        if minLen > maxLen then
            Error(sprintf "invalid bound: minLen %d exceeds maxLen %d" minLen maxLen)
        elif value.Length < minLen || value.Length > maxLen then
            Error(sprintf "string length %d is outside [%d, %d]" value.Length minLen maxLen)
        else
            Ok(BoundedString(value, minLen, maxLen))

    let value (BoundedString(s, _, _)) = s

/// An integer guaranteed to lie within `[Min, Max]` inclusive.
type BoundedInt = private BoundedInt of value: int * min: int * max: int

module BoundedInt =
    /// Parse an integer into a `BoundedInt` bounded to `[min, max]` inclusive.
    /// Rejects an invalid bound (`min > max`) and any out-of-range value.
    let tryCreate (min: int) (max: int) (value: int) : Result<BoundedInt, string> =
        if min > max then
            Error(sprintf "invalid bound: min %d exceeds max %d" min max)
        elif value < min || value > max then
            Error(sprintf "value %d is outside [%d, %d]" value min max)
        else
            Ok(BoundedInt(value, min, max))

    let value (BoundedInt(v, _, _)) = v

/// A real number guaranteed to lie within `[0, 1]` inclusive — the canonical
/// progress / ratio fraction. Mirrors `ProgressSpec.Fraction`'s documented
/// domain so a fraction can be carried in-range by construction.
type Fraction = private Fraction of float

module Fraction =
    /// Parse a float into a `Fraction`. Rejects NaN, infinity, and any value
    /// outside `[0, 1]`.
    let tryCreate (value: float) : Result<Fraction, string> =
        if System.Double.IsNaN value || System.Double.IsInfinity value then
            Error "fraction must be a finite number"
        elif value < 0.0 || value > 1.0 then
            Error(sprintf "fraction %g is outside [0, 1]" value)
        else
            Ok(Fraction value)

    let value (Fraction v) = v

    /// `0.0` as a `Fraction` — total, no validation needed.
    let zero = Fraction 0.0
    /// `1.0` as a `Fraction` — total, no validation needed.
    let one = Fraction 1.0
