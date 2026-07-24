namespace Fuaran.UI.OpStream.Dag.Merge

open System.Text
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

// ============================================================================
//  ValidatorGate — semantic merge-conflict detection (Phase 184).
//
//  A structurally-clean, NodeId-disjoint merge (Phase 179) can still produce a
//  DOMAIN-invalid tree: a range violation, a broken cross-ref, a parallel-fifth
//  (music). Each of those is present in the MERGED tree but in NEITHER parent —
//  the merge *introduced* it. That is a SEMANTIC conflict, not a clean merge,
//  and it is surfaced through the existing Phase 179 `MergeConflict` recovery
//  envelope (the `CombinedCycle` class, which already names exactly this: a
//  post-merge whole-tree illegality no single op produced).
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
/// (`MergePolicy.lenient`, no validator) reproduces pre-184 behaviour exactly.
type MergePolicy<'Msg> =
    {
        /// The domain validator to run over the merge result. `None` = no gating.
        Validator: MergeValidator<'Msg> option
        /// `true` → a merge-introduced defect REFUSES the merge (a
        /// `NeedsManualMerge` carrying `CombinedCycle` conflicts). `false` → the
        /// clean structural merge proceeds; introduced defects surface as a
        /// post-merge diagnostic only.
        GateOnIntroducedDefect: bool
    }

module MergePolicy =
    /// No validator-gating — reproduces the pre-184 merge behaviour exactly.
    let lenient<'Msg> : MergePolicy<'Msg> =
        { Validator = None
          GateOnIntroducedDefect = false }

    /// Gate ON: an introduced defect refuses the merge.
    let gated<'Msg> (validator: MergeValidator<'Msg>) : MergePolicy<'Msg> =
        { Validator = Some validator
          GateOnIntroducedDefect = true }

    /// Gate OFF: the clean merge proceeds, introduced defects are a diagnostic.
    let diagnostic<'Msg> (validator: MergeValidator<'Msg>) : MergePolicy<'Msg> =
        { Validator = Some validator
          GateOnIntroducedDefect = false }

module ValidatorGate =
    /// Diff identity for the introduced-defect test — a defect is "the same"
    /// across trees iff its `(Code, NodeId, Facet)` matches.
    let private identity (d: MergeDefect) = d.Code, d.NodeId, d.Facet

    /// Defects present in `merged` but in NEITHER parent — the ones the merge
    /// INTRODUCED (a defect already present in a parent was not caused by the
    /// merge, so it is carried through, never flagged). Sorted canonically.
    let introducedDefects<'Msg>
        (validator: MergeValidator<'Msg>)
        (parentA: Node<'Msg>)
        (parentB: Node<'Msg>)
        (merged: Node<'Msg>)
        : MergeDefect list =
        let parentKeys =
            Set.union
                (validator parentA |> List.map identity |> Set.ofList)
                (validator parentB |> List.map identity |> Set.ofList)

        validator merged
        |> List.filter (fun d -> not (Set.contains (identity d) parentKeys))
        |> MergeDefect.sortCanonical

    /// Lift an introduced defect into the Phase 179 `MergeConflict` envelope.
    /// Class is `CombinedCycle` (post-merge whole-tree illegality); the
    /// validator's `Code` rides in `Base` (so the recovery pattern-match reuses
    /// it) and the human-readable description in `SecondaryTag`. There is no
    /// three-up authorship — both parents are defect-free for this cell by
    /// construction, so `KeepBase` (abandon the merge for this cell) is the
    /// enumerated recovery.
    let toConflict (d: MergeDefect) : MergeConflict =
        { NodeId = d.NodeId
          Facet = d.Facet
          Class = MergeConflictClass.CombinedCycle
          Base = d.Code
          Primary = None
          Secondary = None
          SecondaryTag = Some d.Message
          PrimacyHeld = false
          Choices = [ MergeChoice.KeepBase ]
          Hint = ApplyHint.empty }

    /// Mirror of `CanonicalJson`'s string escape (only `"`, `\`, control chars
    /// need escaping for our ASCII defect fields), kept local so the merge
    /// package takes no extra dependency for the verdict codec.
    let private appendEscaped (sb: StringBuilder) (s: string) : unit =
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore

    /// Canonical JSON of a gated-merge VERDICT: the introduced-defect set as a
    /// sorted array of `{code,facet,message,nodeId}` objects (object keys
    /// alphabetical, array entries in `(NodeId,Facet,Code)` order). Byte-stable
    /// across hosts, so `HashChain.sha256Hex` over it is the cross-host verdict
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
