using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;
using FsGen = Fuaran.UI.Generated;

namespace Fuaran.UI.CSharp;

// Phase 305 — the structural NodeKind cases: the bounded-escape Custom hatch, the
// ErrorBoundary graceful-degradation wrapper, and the reusable-subtree Fragment
// decl/ref pair.
//
// SPEC-CONSTRUCTION-TRIPWIRE — the `new FsGen.<X>(…)` calls below are positional on
// purpose. An additive spec slot lands here as CS7036, at the one site that decides
// whether the veneer exposes it or passes the F# default; that is the mechanism, not
// churn. See src/Fuaran.UI.Tests/SpecConstructionTests.fs ("The C# authoring veneer").
public static partial class Fuaran
{
    /// <summary>The bounded-escape hatch — a host-registered component addressed by module + component id.</summary>
    public static FuaranNode Custom(CustomOptions options) =>
        new(FsFactory.custom<object>(
            options.Id,
            options.ModuleId,
            options.ComponentId,
            Fs.Map((options.Props ?? new Dictionary<string, string>())
                .Select(kv => new KeyValuePair<string, global::Fuaran.Core.JVal>(kv.Key, global::Fuaran.Core.JVal.NewJStr(kv.Value)))),
            Fs.None<FsGen.ContentHash>(),
            Fs.List((options.ExposedNodeIds ?? Enumerable.Empty<string>()).Select(FsTypes.NodeId.NewNodeId))));

    /// <summary>A render-time error boundary — renders <c>Fallback</c> if <c>Child</c> throws.</summary>
    public static FuaranNode ErrorBoundary(ErrorBoundaryOptions options) =>
        new(FsFactory.errorBoundary<object>(
            options.Id,
            // Generated ErrorBoundarySpec ctor order (Child, Fallback) matches the
            // old hand order; only the declaring type moved to Fuaran.UI.Generated.
            new FsGen.ErrorBoundarySpec<object>(options.Child.Inner, options.Fallback.Inner)));

    /// <summary>A named reusable-subtree declaration (renders nothing at the decl site).</summary>
    public static FuaranNode FragmentDecl(FragmentDeclOptions options) =>
        new(FsFactory.fragmentDecl<object>(
            options.Id,
            // Generated FragmentDeclSpec ctor is Generated.fs declaration order
            // (Body, Name, Holes, Effect), not the old Name-first hand order.
            // `Name` is a bare string since the swap (the FragmentId wrapper
            // unwraps at this boundary); `Holes = None` / `Effect = None` ≡ the
            // old empty-hole-list / pure-deterministic degenerate shape (both
            // omitted on the wire).
            new FsGen.FragmentDeclSpec<object>(
                options.Body.Inner,
                options.Name,
                Fs.None<Microsoft.FSharp.Collections.FSharpList<FsGen.HoleDecl>>(),
                Fs.None<FsGen.EffectClass>())));

    /// <summary>A reference that expands the named fragment.</summary>
    public static FuaranNode FragmentRef(FragmentRefOptions options) =>
        new(FsFactory.fragmentRef<object>(options.Id, options.Name));

    /// <summary>An isolation/embedding boundary (§4o) — mounts an isolated guest scope at this
    /// point. The guest's message space lives behind the mount; <c>OnBubble</c> is opaque to the
    /// wire (same posture as <c>Button.OnClick</c>) and authors as a no-op on this surface. The
    /// default channel is out-only with no declared message shape, and an empty capability list
    /// is default-deny of every host-affecting guest action. Typed <c>Inputs</c> (fragment-arg
    /// hole bindings) remain F#-side authoring.</summary>
    public static FuaranNode Mount(MountOptions options) =>
        new(FsFactory.mount<object>(
            options.Id,
            // Generated MountSpec ctor is Generated.fs declaration order
            // (Capabilities, Channel, Inputs, OnBubble, ScopeId), not the old
            // ScopeId-first hand order. `Capabilities` is a bare string list since
            // the swap (the CapabilityTag wrapper unwraps at this boundary);
            // `Inputs = None` ≡ the old empty map, and `OnBubble = None` ≡ the old
            // no-op handler (both omitted on the wire).
            new FsGen.MountSpec<object>(
                Fs.List(options.Capabilities ?? Enumerable.Empty<string>()),
                new FsGen.GuestChannel(
                    options.TwoWay ? FsGen.ChannelDirection.TwoWay : FsGen.ChannelDirection.OutOnly,
                    Fs.OptStr(options.MessageShape)),
                Fs.None<Microsoft.FSharp.Collections.FSharpMap<string, FsGen.FragmentArg<object>>>(),
                Fs.None<Microsoft.FSharp.Core.FSharpFunc<object, global::Fuaran.UI.Generated.Action<object>>>(),
                options.ScopeId)));

    /// <summary>A state-bound conditional-child primitive (Phase 392) — renders the first
    /// <c>Cases</c> child whose <c>Match</c> equals the string form of the reactive StateStore
    /// value at <c>StateKey</c> (first-match-wins), else <c>Default</c>. The compositional shape
    /// for conditional regions / wizard panes / empty-state alternatives / mode toggles — no new
    /// vocabulary. State transitions arrive as ordinary <c>SetState</c> actions through the
    /// default-deny gate (no new dispatch path).</summary>
    public static FuaranNode Switch(SwitchOptions options) =>
        new(FsFactory.@switch<object>(
            options.Id,
            // Generated SwitchSpec ctor is Generated.fs declaration order (Cases,
            // Default, StateKey), not the old StateKey-first hand order. Each case
            // is now a generated SwitchCase record — its ctor is declaration order
            // (Child, Match), i.e. child-first, the reverse of the old tuple.
            new FsGen.SwitchSpec<object>(
                Fs.List((options.Cases ?? Enumerable.Empty<SwitchCase>())
                    .Select(c => new FsGen.SwitchCase<object>(c.Child.Inner, c.Match))),
                options.Default.Inner,
                // Phase 768 — the F# selector is now any Binding; the C#
                // authoring surface keeps the compact StateKey string and wraps
                // it in the State form here, which the encoder collapses back to
                // the `stateKey` wire spelling. Byte-identical output.
                FsGen.Binding<string>.NewState(
                    options.StateKey,
                    Microsoft.FSharp.Core.FSharpOption<string>.None))));
}

/// <summary>Options for <see cref="Fuaran.Custom"/>.</summary>
public sealed record CustomOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The registered module id.</summary>
    public required string ModuleId { get; init; }

    /// <summary>The registered component id.</summary>
    public required string ComponentId { get; init; }

    /// <summary>String-valued props passed to the custom renderer.</summary>
    public IReadOnlyDictionary<string, string>? Props { get; init; }

    /// <summary>Interior node ids the custom body exposes for addressing.</summary>
    public IEnumerable<string>? ExposedNodeIds { get; init; }
}

/// <summary>Options for <see cref="Fuaran.ErrorBoundary"/>.</summary>
public sealed record ErrorBoundaryOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The guarded child subtree.</summary>
    public required FuaranNode Child { get; init; }

    /// <summary>The fallback rendered if the child throws.</summary>
    public required FuaranNode Fallback { get; init; }
}

/// <summary>Options for <see cref="Fuaran.FragmentDecl"/>.</summary>
public sealed record FragmentDeclOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The fragment name refs address it by.</summary>
    public required string Name { get; init; }

    /// <summary>The reusable body.</summary>
    public required FuaranNode Body { get; init; }
}

/// <summary>Options for <see cref="Fuaran.FragmentRef"/>.</summary>
public sealed record FragmentRefOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The fragment name to expand.</summary>
    public required string Name { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Mount"/>.</summary>
public sealed record MountOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The guest runtime scope this mount embeds.</summary>
    public required string ScopeId { get; init; }

    /// <summary>Optional declared guest message shape (consulted by validation + the capability gate).</summary>
    public string? MessageShape { get; init; }

    /// <summary>Permit host→guest push (couples the two lifecycles). Default <c>false</c> = out-only,
    /// the safe posture for untrusted guests.</summary>
    public bool TwoWay { get; init; }

    /// <summary>Per-mount capability tags (e.g. <c>"notify"</c>, <c>"call:reports.*"</c>).
    /// Empty = default-deny of every host-affecting guest action.</summary>
    public IEnumerable<string>? Capabilities { get; init; }
}

/// <summary>One <c>(match, child)</c> case of a <see cref="Fuaran.Switch"/>.</summary>
public sealed record SwitchCase
{
    /// <summary>The value (string form of the state) that selects this case.</summary>
    public required string Match { get; init; }

    /// <summary>The child rendered when the state value matches.</summary>
    public required FuaranNode Child { get; init; }
}

/// <summary>Options for <see cref="Fuaran.Switch"/>.</summary>
public sealed record SwitchOptions
{
    /// <summary>The node id.</summary>
    public required string Id { get; init; }

    /// <summary>The reactive StateStore key whose value selects the rendered case.</summary>
    public required string StateKey { get; init; }

    /// <summary>The ordered <c>(match, child)</c> cases — first match wins.</summary>
    public required IEnumerable<SwitchCase> Cases { get; init; }

    /// <summary>The child rendered when no case matches (and the SSR/first-paint surface).</summary>
    public required FuaranNode Default { get; init; }
}
