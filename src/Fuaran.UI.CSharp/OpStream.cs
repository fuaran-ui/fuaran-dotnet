using System;
using System.Collections.Generic;
using FsActor = Fuaran.UI.OpStream.Abstractions.Actor;
using FsEnvelope = Fuaran.UI.OpStream.Abstractions.OpResultEnvelope;
using FsHashChain = Fuaran.UI.OpStream.Abstractions.HashChain;
using FsOpRecord = Fuaran.UI.OpStream.Abstractions.OpRecord<object>;
using FsVerify = Fuaran.UI.OpStream.Abstractions.Verify;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Phase 559 — C#-native op-stream hash-chain helpers over
//  `Fuaran.UI.OpStream.Abstractions`. Enough for a .NET consumer to build and
//  verify a tamper-evident op-record chain (the "Notarised-style" provenance
//  log) WITHOUT touching F#: append computes each record's SHA-256 via the
//  shared `HashChain.computeHash`, and verify delegates to the shared
//  `Verify.chain`. There is no C# hash algorithm — the digest + pre-image are
//  the same the F# / TS hosts produce, so a chain built here verifies on any
//  host and vice-versa.
// ============================================================================

/// <summary>Who authored an op — the load-bearing AI-accountability fact folded into the hash.</summary>
public sealed class FuaranActor
{
    internal FsActor Inner { get; }

    private FuaranActor(FsActor inner) => Inner = inner;

    /// <summary>A human author, identified by <paramref name="id"/>.</summary>
    public static FuaranActor Human(string id) => new(FsActor.NewHuman(id));

    /// <summary>An AI agent — <paramref name="model"/> / <paramref name="version"/> double as corpus-quality metadata.</summary>
    public static FuaranActor Agent(string model, string version, string id) =>
        new(FsActor.NewAgent(model, version, id));

    /// <summary>The stable attribution id (user id for a human, agent id for an agent).</summary>
    public string Id => IdOf(Inner);

    /// <summary>The stable attribution id of an F# <c>Actor</c> (the module's <c>Actor.id</c> lives
    /// in a separate compiled class, so the DU cases are read directly here).</summary>
    internal static string IdOf(FsActor a) => a.IsHuman ? ((FsActor.Human)a).id : ((FsActor.Agent)a).id;
}

/// <summary>One record in a hash-chained op stream — the opaque handle over the F# <c>OpRecord</c>.</summary>
public sealed class OpStreamEntry
{
    internal FsOpRecord Inner { get; }

    internal OpStreamEntry(FsOpRecord inner) => Inner = inner;

    /// <summary>The 1-based sequence number (the first record is 1).</summary>
    public int Sequence => Inner.Sequence;

    /// <summary>This record's content hash (64 lower-case hex chars).</summary>
    public string Hash => Inner.Hash;

    /// <summary>The prior record's hash this one links to (the genesis sentinel for record 1).</summary>
    public string PreviousHash => Inner.PreviousHash;

    /// <summary>The attribution id of this record's author.</summary>
    public string ActorId => FuaranActor.IdOf(Inner.Actor);
}

/// <summary>The outcome of verifying a chain — <see cref="IsIntact"/>, plus a message when broken.</summary>
public sealed record ChainVerification
{
    /// <summary>Whether the chain links, each hash recomputes, and the sequence is contiguous from genesis.</summary>
    public required bool IsIntact { get; init; }

    /// <summary>The first violation described, or null when intact.</summary>
    public string? Message { get; init; }

    internal static ChainVerification Intact => new() { IsIntact = true };

    internal static ChainVerification Broken(string message) => new() { IsIntact = false, Message = message };
}

/// <summary>
/// A hash-chained, append-only op-record stream a .NET consumer builds without
/// F#. Each <see cref="Append"/> computes the next record's hash over the shared
/// canonical pre-image (previous hash + op + sequence + timestamp + actor +
/// prompt + outcome), so the chain is tamper-evident and cross-host verifiable.
/// </summary>
public sealed class OpStreamChain
{
    private readonly string _streamId;
    private readonly List<OpStreamEntry> _entries = new();

    /// <summary>Start an empty stream partitioned under <paramref name="streamId"/>.</summary>
    public OpStreamChain(string streamId) => _streamId = streamId;

    /// <summary>The records appended so far, in order.</summary>
    public IReadOnlyList<OpStreamEntry> Entries => _entries;

    /// <summary>Append an applied op to the chain, computing and linking its hash. Records a
    /// success outcome (v1 only records successful applies). <paramref name="timestamp"/>
    /// defaults to now; <paramref name="promptId"/> is folded into the hash when present.</summary>
    public OpStreamEntry Append(
        FuaranOp op,
        FuaranActor actor,
        DateTimeOffset? timestamp = null,
        string? promptId = null)
    {
        var sequence = _entries.Count + 1;
        var previousHash = _entries.Count == 0 ? FsHashChain.genesisPreviousHash : _entries[^1].Hash;
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        var prompt = Fs.OptStr(promptId);
        var envelope = FsEnvelope.Success;

        var hash = FsHashChain.computeHash<object>(previousHash, op.Inner, sequence, ts, actor.Inner, prompt, envelope);

        var record = new FsOpRecord(_streamId, sequence, previousHash, hash, op.Inner, prompt, actor.Inner, ts, envelope);
        var entry = new OpStreamEntry(record);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Verify the whole chain via the shared F# verifier — links, hash recomputation,
    /// and contiguous sequence from genesis.</summary>
    public ChainVerification Verify()
    {
        var records = new List<FsOpRecord>(_entries.Count);
        foreach (var e in _entries)
        {
            records.Add(e.Inner);
        }

        var result = FsVerify.chain<object>(records);
        return result.IsOk ? ChainVerification.Intact : ChainVerification.Broken(result.ErrorValue.ToString() ?? "chain verification failed");
    }
}
