module ApplyParity

// ============================================================================
//  Cross-pipeline apply-parity harness (Phase 192).
//
//  The shared, pipeline-neutral core: given the canonical `wire-format-fixtures/
//  ops/*` op JSONs, apply each to a fixed base `Node<obj>` tree and produce a
//  deterministic canonical-JSON string for the outcome. The SAME module runs
//  under both F# pipelines:
//    - .NET (Expecto): `Fuaran.UI.Ops.Tests` reads the corpus, runs `evalOp`,
//      asserts each result against the committed golden (the oracle).
//    - Fable (browser): the `samples/apply-demo` host exposes `evalOp` on
//      `window.__fuaranParity`; the parity session asserts the SAME golden.
//
//  A divergence between the two is a defect — the canonical contract
//  (WIRE_FORMAT.md + the fixtures) is the oracle. The high-risk seam this
//  pins: the Phase-191 Fable-safe coercion (`coerceField` / the constructor-ref
//  Map detection) AND the Phase-192 Fable-safe float formatter
//  (`CanonicalJson.formatFiniteDouble`) — both replace .NET-only mechanisms
//  whose Fable mirror must produce byte-identical output. The base-tree floats
//  (1234.5 / 0.07) + the `ReplaceBinding` value (99.5) exercise the float path
//  in every successful re-encode.
//
//  This module is pure over (op JSON string) → (result string): no filesystem,
//  no `window`, no clock — so it compiles + runs identically on both pipelines.
//  The .NET test owns the corpus file-reading; the Fable host is handed the op
//  strings by the parity session.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

/// Row shape for the Phase 364 nested-path grid target. The accessors encode
/// as `"<closure>"`; only the column metadata (Label / Format / Width) is on
/// the wire, which is exactly the surface the nested `UpdateProp` paths edit.
type private ParityRow = { Channel: string; Spend: float }

/// The fixed base tree every corpus op applies against. Node ids match the
/// corpus op targets (`metric-1`, `stack-1`, `markdown-1`, `card-1`,
/// `dash-empty`, and the Phase 364 nested-path targets `grid-1` / `chart-1` /
/// `form-1`) so each op resolves its target. Smart-constructed as a
/// `Node<obj>` (the shape `JsonDecode.decodeOp` yields a `TreeOp<obj>` for).
/// The Metric carries float-valued bindings (Source 1234.5, Trend 0.07) so a
/// successful re-encode exercises the canonical float formatter on both pipelines.
/// The nested-path targets live directly under the root (never under
/// `stack-1` — `op-reorderchildren` enumerates that child list exactly).
let baseTree: Node<obj> =
    Fuaran.dashboard
        "dash-root"
        { Defaults.dashboard with
            Children =
                [ Fuaran.stack
                      "stack-1"
                      { Defaults.stack with
                          Children =
                              [ Fuaran.metric
                                    "metric-1"
                                    { Defaults.metric with
                                        Label = TextSource.Literal "Revenue"
                                        Value = Binding.Static 1234.5
                                        Format = format.currency "GBP"
                                        Tone = ToneVariant.Brand
                                        Trend = Some(Binding.Static 0.07)
                                        TrendFormat = Some(format.percent (Some 1)) }
                                Fuaran.markdown "markdown-1" "hello" ] }
                  Fuaran.card "card-1" { Defaults.card with Children = [] }
                  Fuaran.dashboard "dash-empty" Defaults.dashboard
                  Fuaran.grid
                      "grid-1"
                      { Defaults.grid<ParityRow, obj> with
                          Columns =
                              [ Column.text "Channel" (fun (r: ParityRow) -> r.Channel)
                                Column.text "Spend" (fun (r: ParityRow) -> string r.Spend) ] }
                  Fuaran.chart
                      "chart-1"
                      { Defaults.chart with
                          XField = "month"
                          YFields = [ "revenue"; "cost" ] }
                  Fuaran.form
                      "form-1"
                      { Defaults.form with
                          Fields =
                              [ { Defaults.formField with
                                    Id = "name"
                                    Label = TextSource.Literal "Name"
                                    Required = true }
                                { Defaults.formField with
                                    Id = "age"
                                    Label = TextSource.Literal "Age" } ] } ] }

/// Apply one wire op JSON to the base tree and project the outcome to a
/// deterministic canonical string:
///   - decode failure  → `"DECODE_ERR:<message>"`
///   - apply rejection → `"ERR:<ApplyErrorCode>"` (the enum case — culture-free)
///   - success         → `"OK:" + CanonicalJson.encodeNode <post-apply tree>`
/// Every branch is byte-stable across .NET and Fable: the success branch is the
/// canonical encoder (now Fable-portable), and the error branches carry the
/// enum/`%A` shape rather than any locale-sensitive rendering.
let evalOp (opJson: string) : string =
    match JsonDecode.decodeOp opJson with
    | Error e -> sprintf "DECODE_ERR:%s" e.Message
    | Ok op ->
        match Apply.apply op baseTree with
        | Error err -> sprintf "ERR:%A" err.Code
        | Ok newTree -> "OK:" + CanonicalJson.encodeNode newTree

/// Map `evalOp` over a list of (fixtureName, opJson) pairs — the shape the
/// .NET test feeds from the corpus and the Fable host receives from the session.
let run (ops: (string * string) list) : (string * string) list =
    ops |> List.map (fun (name, opJson) -> name, evalOp opJson)
