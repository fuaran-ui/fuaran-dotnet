using System;
using Fuaran.UI.CSharp;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// ============================================================================
//  Phase 559 — the runtime facade end-to-end proof: a C#-ONLY consumer authors a
//  tree, validates it, applies tree-ops through the shared F# engine, and
//  maintains a verified hash chain — all without writing F#. Corpus-independent
//  (no wire-format-fixtures needed), so it runs on every checkout.
// ============================================================================
internal static class Runtime
{
    private const string Genesis =
        "0000000000000000000000000000000000000000000000000000000000000000";

    public static void Run(Harness h)
    {
        // ── Author (C# factory surface, no F#) ──────────────────────────────
        var m1 = Fuaran.Metric(new()
        {
            Id = "rev",
            Label = "Revenue",
            Value = 1234.5,
            Format = CellFormat.Currency("GBP"),
            Tone = Tone.Brand,
        });
        var card = Fuaran.Card(new() { Id = "insights", Heading = "Insights", Children = [m1] });

        // ── Validate (runtime pre-emit self-check) ──────────────────────────
        h.Check("validate: authored card is wire-valid", Fuaran.IsValid(card));
        h.Check("validate: authored card has no findings", Fuaran.Validate(card).Count == 0);

        // ── Apply: insert a second metric child through the F# engine ───────
        var m2 = Fuaran.Metric(new()
        {
            Id = "cost",
            Label = "Cost",
            Value = 500.0,
            Format = CellFormat.Currency("GBP"),
            Tone = Tone.Default,
        });
        var insertOp = Ops.InsertChild("insights", m2);

        var applied = Fuaran.Apply(card, insertOp);
        h.Check("apply: insert succeeds", applied.IsOk, applied.Error?.Message);
        h.Check(
            "apply: new tree carries both children",
            applied.IsOk && applied.Value!.Encode().Contains("\"id\":\"rev\"") && applied.Value.Encode().Contains("\"id\":\"cost\""));
        // Immutability: the input tree is unchanged (§4g "revert is implicit").
        h.Check("apply: input tree unmutated", !card.Encode().Contains("\"id\":\"cost\""));

        // ── Apply: the wire-op path (encode op → decode op → apply) ─────────
        var removeOp = Ops.RemoveNode("cost");
        var opJson = removeOp.Encode();
        h.Check("apply: op round-trips through wire JSON", Ops.FromJson(opJson).IsOk);
        var afterRemove = Fuaran.Apply(applied.Value!, Ops.FromJson(opJson).Value!);
        h.Check(
            "apply: decoded op removes the child",
            afterRemove.IsOk && !afterRemove.Value!.Encode().Contains("\"id\":\"cost\""));

        // ── Apply: structured error on a bad op (no FSharp.Core on surface) ─
        var bad = Fuaran.Apply(card, Ops.RemoveNode("does-not-exist"));
        h.Check(
            "apply: missing node → NodeNotFound",
            !bad.IsOk && bad.Error!.Code == ApplyErrorCode.NodeNotFound,
            bad.Error is null ? "(applied ok — expected a reject)" : bad.Error.Code.ToString());
        h.Check("apply: TryApply pattern works", !Fuaran.TryApply(card, Ops.RemoveNode("nope"), out _, out var e) && e is not null);

        // ── Op-stream: build + verify a tamper-evident hash chain (no F#) ───
        var actor = FuaranActor.Agent("claude", "opus-4", "agent-1");
        var ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var chain = new OpStreamChain("insights-stream");
        var e1 = chain.Append(insertOp, actor, ts, "prompt-1");
        var e2 = chain.Append(removeOp, actor, ts.AddSeconds(1), "prompt-2");

        h.Check("opstream: sequence is 1-based + contiguous", e1.Sequence == 1 && e2.Sequence == 2);
        h.Check("opstream: record 1 links to genesis", e1.PreviousHash == Genesis);
        h.Check("opstream: record 2 links to record 1", e2.PreviousHash == e1.Hash);
        h.Check("opstream: hashes are 64 hex chars", e1.Hash.Length == 64 && e2.Hash.Length == 64);
        h.Check("opstream: actor attribution recorded", e1.ActorId == "agent-1");

        var verification = chain.Verify();
        h.Check("opstream: chain verifies intact", verification.IsIntact, verification.Message);

        // Cross-host determinism: re-appending the SAME op/actor/timestamp under a
        // fresh chain reproduces the identical hash (the pre-image is shared).
        var chain2 = new OpStreamChain("insights-stream");
        var e1b = chain2.Append(insertOp, actor, ts, "prompt-1");
        h.Check("opstream: hash is deterministic for identical input", e1b.Hash == e1.Hash);
    }
}
