module FableLaws.EscapeLaw

// ============================================================================
//  Law 12 (Phase 2026) — the op-stream tier spells a control character the
//  same way on both pipelines.
//
//  The tier's private escapers wrote a control character through
//  `sprintf "\\u%04x"`, whose lowering under Fable is its own code path. They
//  are gone: every site now spells a string through Core's `Json.escapeInto`.
//  This law runs the sites a browser reaches over every control character, the
//  quote, the backslash and a surrogate pair, and holds each output to the rule
//  spelled out here from a hex table: `"` as `\"`, `\` as `\\`, U+0000-U+001F
//  as lower-case `\u00xx`, everything else raw. Each line carries the output's
//  length and a code-unit checksum rather than the output itself, so a line is
//  ASCII on both pipelines and the byte comparison sees any moved unit.
// ============================================================================

open System.Text
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

type EscapeLaw = { Lines: string list; Violations: int }

let private hex = "0123456789abcdef"

/// The canonical spelling, written out character by character from the table.
let private expectedQuoted (s: string) : string =
    let sb = StringBuilder()
    sb.Append '"' |> ignore

    for c in s do
        let code = int c

        if c = '"' then
            sb.Append "\\\"" |> ignore
        elif c = '\\' then
            sb.Append "\\\\" |> ignore
        elif code < 0x20 then
            sb.Append("\\u00").Append(hex[code >>> 4]).Append(hex[code &&& 0xF]) |> ignore
        else
            sb.Append c |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

let private cases: (string * string) list =
    [ for c in 0..0x1F -> "U+" + string hex[c >>> 4] + string hex[c &&& 0xF], "a" + string (char c) + "b" ]
    @ [ "quote", "\"q\""
        "backslash", "\\b\\"
        "surrogate pair", "x" + string (char 0xD83D) + string (char 0xDE00) + "y"
        "clean", "plain text" ]

let private updateWith (v: JVal) : TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "n", "p", PropValue.Wire v)

let private sites: (string * (string -> string)) list =
    [ "canonical", (fun s -> CanonicalJson.encodeOp (updateWith (JStr s)))
      "actor", (fun s -> Actor.encode (Actor.Human s))
      "result", (fun s -> StreamEntry.encodeResult (OpResultEnvelope.Failure(s, s)))
      "verdict",
      (fun s ->
          ValidatorGate.encodeVerdict
              [ { Code = "C"
                  NodeId = "n"
                  Facet = "f"
                  Message = s } ]) ]

let private checksum (s: string) : int =
    let mutable acc = 0

    for c in s do
        acc <- (acc * 31 + int c) % 1000003

    acc

let escapeLaw () : EscapeLaw =
    let rows =
        [ for site, encode in sites do
              for name, s in cases do
                  let out = encode s
                  let ok = out.Contains(expectedQuoted s)
                  // Spelled out: a bool renders `True` on .NET and `true` under Fable.
                  let verdict = if ok then "yes" else "no"
                  yield ok, $"escape {site} {name} ok={verdict} len={out.Length} sum={checksum out}" ]

    { Lines = rows |> List.map snd
      Violations = rows |> List.filter (fst >> not) |> List.length }
