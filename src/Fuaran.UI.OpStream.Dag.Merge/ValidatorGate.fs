namespace Fuaran.UI.OpStream.Dag.Merge

open System.Text
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ============================================================================
//  ValidatorGate — semantic merge-conflict detection (Phase 184).
//
//  A structurally clean merge can still produce a DOMAIN-invalid tree: a range
//  violation, a broken cross-ref, a parallel-fifth (music). Each of those is
//  present in the MERGED tree but in NEITHER parent — the merge *introduced*
//  it. That is a SEMANTIC conflict, not a clean merge, and the merge refuses
//  it with the introduced defects (`ReconcileFault.IntroducedDefects`; the
//  tree merge's `MergeConflict` envelope retired with it at Phase 2645).
//
//  DOMAIN-AGNOSTIC by construction. The merge package never interprets a
//  defect — it only DIFFS defect sets (merged vs each parent) and lifts the
//  introduced ones. Each domain plugs its own runtime `MergeValidator` walker:
//  a `Node<'Msg> -> MergeDefect list` over the live tree. (This is the RUNTIME
//  seam — distinct from the build-time `Fuaran.UI.Validator` F# AST walker,
//  which gates source, not a merge result. The merge package stays FSharp.Core
//  + Fuaran.UI only — no FCS dependency, Fable-clean.)
//
//  Determinism (FGP 5 / the wire forward-coupling rule): the introduced-defect
//  set is sorted canonically by `(NodeId, Facet, Code)` and `encodeVerdict`
//  serialises it to byte-stable JSON, so `sha256Hex` over it is the cross-host
//  (F# ↔ TS) verdict hash — the merge-conformance determinism artifact for a
//  REFUSED gated merge (the analogue of the outcome-hash for an auto-merge).
// ============================================================================

/// A domain-validity defect found by a runtime validator walking a candidate
/// `Node` tree. `Code` reuses the domain validator's stable defect code (e.g.
/// `"FUARAN050"`) so an AI consumer can pattern-match the failure class;
/// `NodeId` / `Facet` name the offending cell; `Message` is human-readable.
/// The introduced-defect diff keys on `(Code, NodeId, Facet)`.
type MergeDefect =
    { Code: string
      NodeId: string
      Facet: string
      Message: string }

module MergeDefect =
    /// Host-independent ordering key for a defect (NodeId, Facet, Code).
    let private orderKey (d: MergeDefect) = d.NodeId, d.Facet, d.Code

    /// Order a defect set deterministically — the verdict is byte-stable
    /// regardless of the walker's internal emission order.
    let sortCanonical (defects: MergeDefect list) : MergeDefect list = defects |> List.sortBy orderKey

/// A domain validator: walks a candidate tree, returns its defects. Each domain
/// plugs its own walker (scalar-range / cross-ref / counterpoint / …). The
/// merge is domain-agnostic — it only diffs defect SETS, never interprets them.
type MergeValidator<'Msg> = Node<'Msg> -> MergeDefect list

/// Validator-gating policy for a merge (Phase 184). Additive — the default
/// (`MergePolicy.Lenient`, no validator) reproduces pre-184 behaviour exactly.
///
/// A union of the three meaningful policies (Phase 2064). It replaced a record
/// of a validator option and a gate flag, whose fourth combination — a gate
/// with no validator — asked for gating and silently gated nothing; that
/// combination is now unrepresentable, and `MergePolicy.ofFields` (the record's
/// obsolete constructor) refuses it.
[<RequireQualifiedAccess>]
type MergePolicy<'Msg> =
    /// No validator-gating — reproduces the pre-184 merge behaviour exactly.
    | Lenient
    /// A merge-introduced defect REFUSES the merge (a `NeedsManualMerge`
    /// carrying `CombinedCycle` conflicts).
    | Gated of MergeValidator<'Msg>
    /// The clean structural merge proceeds; introduced defects surface as a
    /// post-merge diagnostic only.
    | Diagnostic of MergeValidator<'Msg>

    /// The domain validator to run over the merge result, if any — what the
    /// former record's `Validator` field held.
    member this.Validator: MergeValidator<'Msg> option =
        match this with
        | MergePolicy.Lenient -> None
        | MergePolicy.Gated validator
        | MergePolicy.Diagnostic validator -> Some validator

    /// Whether a merge-introduced defect refuses the merge — what the former
    /// record's `GateOnIntroducedDefect` field held.
    member this.GateOnIntroducedDefect: bool =
        match this with
        | MergePolicy.Gated _ -> true
        | MergePolicy.Lenient
        | MergePolicy.Diagnostic _ -> false

module MergePolicy =
    /// No validator-gating — reproduces the pre-184 merge behaviour exactly.
    let lenient<'Msg> : MergePolicy<'Msg> = MergePolicy.Lenient

    /// Gate ON: an introduced defect refuses the merge.
    let gated<'Msg> (validator: MergeValidator<'Msg>) : MergePolicy<'Msg> = MergePolicy.Gated validator

    /// Gate OFF: the clean merge proceeds, introduced defects are a diagnostic.
    let diagnostic<'Msg> (validator: MergeValidator<'Msg>) : MergePolicy<'Msg> = MergePolicy.Diagnostic validator

    /// The former record's two fields, as a policy. A gate with no validator
    /// is refused (`ArgumentException`): it gates nothing, and building it
    /// silently was the defect the union removes.
    [<System.Obsolete("MergePolicy is a union since Phase 2064: use MergePolicy.Lenient, MergePolicy.Gated or MergePolicy.Diagnostic.")>]
    let ofFields<'Msg> (validator: MergeValidator<'Msg> option) (gateOnIntroducedDefect: bool) : MergePolicy<'Msg> =
        match validator, gateOnIntroducedDefect with
        | None, false -> MergePolicy.Lenient
        | None, true ->
            invalidArg
                (nameof gateOnIntroducedDefect)
                "A merge gate with no validator gates nothing; use MergePolicy.Lenient, or supply the validator to gate on."
        | Some v, true -> MergePolicy.Gated v
        | Some v, false -> MergePolicy.Diagnostic v

module ValidatorGate =
    // Phase 2312 — the diff and the verdict are Core's introduced-findings gate
    // (`Fuaran.Core.Validator.introducedDefects` / `verdict`), the gate every
    // other domain's merge reads. A merge defect enters it as a Core `Defect`
    // whose node is its cell — `(NodeId, Facet)` — so Core's `(code, node)`
    // identity is this gate's `(Code, NodeId, Facet)`, and as an Error, so an
    // introduced defect blocks under `Gated` exactly as it always has.

    /// A cell's id key: the node id length-prefixed, then the facet — injective
    /// and invertible whatever either part contains.
    let private cellWitness: Fuaran.Core.IdWitness<string * string> =
        { ToString = fun (nodeId, facet) -> string nodeId.Length + ":" + nodeId + facet
          OfString =
            fun key ->
                let colon = key.IndexOf ':'
                let length = int (key.Substring(0, colon))
                key.Substring(colon + 1, length), key.Substring(colon + 1 + length)
          Equals = fun a b -> a = b }

    let private toDefect (d: MergeDefect) : Fuaran.Core.Defect<string * string> =
        { Code = d.Code
          Severity = Fuaran.Core.Severity.Error
          Message = d.Message
          Node = Some(d.NodeId, d.Facet)
          Family = ""
          Related = [] }

    let private ofDefect (d: Fuaran.Core.Defect<string * string>) : MergeDefect =
        let nodeId, facet = d.Node |> Option.defaultValue ("", "")

        { Code = d.Code
          NodeId = nodeId
          Facet = facet
          Message = d.Message }

    /// Defects present in `merged` but in NEITHER parent — the ones the merge
    /// INTRODUCED (a defect already present in a parent was not caused by the
    /// merge, so it is carried through, never flagged), diffed by Core's
    /// `introducedDefects` with both parents as the baselines. Sorted
    /// canonically.
    let introducedDefects<'Msg>
        (validator: MergeValidator<'Msg>)
        (parentA: Node<'Msg>)
        (parentB: Node<'Msg>)
        (merged: Node<'Msg>)
        : MergeDefect list =
        let run tree = validator tree |> List.map toDefect

        Fuaran.Core.Validator.introducedDefects cellWitness [ run parentA; run parentB ] (run merged)
        |> List.map ofDefect
        |> MergeDefect.sortCanonical

    /// Core's gate verdict over a merge under `policy`: `Lenient` runs no
    /// validator and blocks nothing; `Diagnostic` reports the introduced defects
    /// and blocks nothing; `Gated` reports them and blocks on any. The defects
    /// are `introducedDefects`'s, in canonical order.
    let verdict<'Msg>
        (policy: MergePolicy<'Msg>)
        (parentA: Node<'Msg>)
        (parentB: Node<'Msg>)
        (merged: Node<'Msg>)
        : MergeDefect list * bool =
        let gatePolicy, introduced =
            match policy with
            | MergePolicy.Lenient -> Fuaran.Core.Validator.GatePolicy.Lenient, []
            | MergePolicy.Gated validator ->
                Fuaran.Core.Validator.GatePolicy.Gated, introducedDefects validator parentA parentB merged
            | MergePolicy.Diagnostic validator ->
                Fuaran.Core.Validator.GatePolicy.Diagnostic, introducedDefects validator parentA parentB merged

        let v = Fuaran.Core.Validator.verdict gatePolicy (introduced |> List.map toDefect)
        v.Introduced |> List.map ofDefect |> MergeDefect.sortCanonical, v.Blocked

    /// A string as a JSON string literal, through Core's canonical escape
    /// (Phase 2026) — the escape every other op-stream encoder uses.
    let private appendEscaped (sb: StringBuilder) (s: string) : unit =
        sb.Append '"' |> ignore
        Fuaran.Core.Json.escapeInto sb s
        sb.Append '"' |> ignore

    /// Canonical JSON of a gated-merge VERDICT: the introduced-defect set as a
    /// sorted array of `{code,facet,message,nodeId}` objects (object keys
    /// alphabetical, array entries in `(NodeId,Facet,Code)` order). Byte-stable
    /// across hosts, so `Fuaran.Core.Hash.sha256Hex` over it is the cross-host verdict
    /// hash — the determinism artifact for a refused validator-gated merge.
    let encodeVerdict (defects: MergeDefect list) : string =
        let sb = StringBuilder()
        sb.Append '[' |> ignore

        defects
        |> MergeDefect.sortCanonical
        |> List.iteri (fun i d ->
            if i > 0 then
                sb.Append ',' |> ignore

            sb.Append "{\"code\":" |> ignore
            appendEscaped sb d.Code
            sb.Append ",\"facet\":" |> ignore
            appendEscaped sb d.Facet
            sb.Append ",\"message\":" |> ignore
            appendEscaped sb d.Message
            sb.Append ",\"nodeId\":" |> ignore
            appendEscaped sb d.NodeId
            sb.Append '}' |> ignore)

        sb.Append ']' |> ignore
        sb.ToString()
