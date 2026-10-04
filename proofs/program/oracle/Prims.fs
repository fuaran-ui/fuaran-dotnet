// SPDX-License-Identifier: Apache-2.0
// Copyright 2026 Diametrical Ltd

/// Phase 1715 — the entire runtime the extracted model needs.
///
/// **The F# backend of the F* extractor ships no runtime library.** The
/// OCaml backend has one; the F# backend emits code referencing a
/// `Prims` module and leaves producing it to the consumer. This is that
/// module, and its size is the point: a handful of names, no dependency
/// beyond the framework, and nothing that could make the oracle agree
/// with production for a reason other than the model being right.
///
/// The names are not chosen — they are exactly what
/// `proofs/BoundedFold.fst`'s extraction references, and
/// `proofs/check.ps1` re-derives that set on every run: the extraction
/// is byte-compared against the committed `BoundedFold.fs`, so a model
/// change that reached for a further name would fail the diff rather
/// than silently compile against a shim someone widened.
///
/// `strcat` is the one this model leans on. The bounded fold's
/// diagnostics carry a log-safe description and a reason, both built by
/// concatenation, and comparing those verbatim against production is
/// most of what the differential host does.
module Prims

open System
open System.Globalization
open System.Numerics

type int = BigInteger
type nat = BigInteger
type bool = Boolean
type string = String
type list<'a> = Microsoft.FSharp.Collections.List<'a>

/// Integer literals reach the extraction as decimal text.
let parse_int (text: String) : int =
    BigInteger.Parse(text, NumberFormatInfo.InvariantInfo)

/// Structural equality. Used on strings and on the small closed unions
/// the model owns.
let op_Equals (left: 'a) (right: 'a) : bool = left = right

let strcat (left: String) (right: String) : String = left + right

/// Invariant decimal rendering.
let string_of_int (value: int) : String =
    value.ToString(NumberFormatInfo.InvariantInfo)
