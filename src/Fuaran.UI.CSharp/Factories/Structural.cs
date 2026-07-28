using System.Collections.Generic;
using System.Linq;
using FsFactory = global::Fuaran.UI.Fuaran;
using FsTypes = Fuaran.UI.Types;

namespace Fuaran.UI.CSharp;

// Phase 305 — the structural NodeKind cases: the bounded-escape Custom hatch, the
// ErrorBoundary graceful-degradation wrapper, and the reusable-subtree Fragment
// decl/ref pair.
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
            Fs.None<FsTypes.ContentHash>(),
            Fs.List((options.ExposedNodeIds ?? Enumerable.Empty<string>()).Select(FsTypes.NodeId.NewNodeId))));

    /// <summary>A render-time error boundary — renders <c>Fallback</c> if <c>Child</c> throws.</summary>
    public static FuaranNode ErrorBoundary(ErrorBoundaryOptions options) =>
        new(FsFactory.errorBoundary<object>(
            options.Id,
            new FsTypes.ErrorBoundarySpec<object>(options.Child.Inner, options.Fallback.Inner)));

    /// <summary>A named reusable-subtree declaration (renders nothing at the decl site).</summary>
    public static FuaranNode FragmentDecl(FragmentDeclOptions options) =>
        new(FsFactory.fragmentDecl<object>(
            options.Id,
            new FsTypes.FragmentDeclSpec<object>(
                FsTypes.FragmentId.NewFragmentId(options.Name),
                options.Body.Inner,
                Fs.Empty<FsTypes.HoleDecl>(),
                new FsTypes.EffectClass(FsTypes.HostEffect.Pure, FsTypes.DeterminismSource.Deterministic))));

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
            new FsTypes.MountSpec<object>(
                options.ScopeId,
                Fs.EmptyMap<string, FsTypes.FragmentArg<object>>(),
                new FsTypes.GuestChannel(
                    options.TwoWay ? FsTypes.ChannelDirection.TwoWay : FsTypes.ChannelDirection.OutOnly,
                    Fs.OptStr(options.MessageShape)),
                Fs.Func<object, global::Fuaran.UI.Generated.Action<object>>(_ =>
                    global::Fuaran.UI.Generated.Action<object>.NewChain(Fs.Empty<global::Fuaran.UI.Generated.Action<object>>())),
                Fs.List((options.Capabilities ?? Enumerable.Empty<string>())
                    .Select(FsTypes.CapabilityTag.NewCapabilityTag)))));

    /// <summary>A state-bound conditional-child primitive (Phase 392) — renders the first
    /// <c>Cases</c> child whose <c>Match</c> equals the string form of the reactive StateStore
    /// value at <c>StateKey</c> (first-match-wins), else <c>Default</c>. The compositional shape
    /// for conditional regions / wizard panes / empty-state alternatives / mode toggles — no new
    /// vocabulary. State transitions arrive as ordinary <c>SetState</c> actions through the
    /// default-deny gate (no new dispatch path).</summary>
    public static FuaranNode Switch(SwitchOptions options) =>
        new(FsFactory.@switch<object>(
            options.Id,
            new FsTypes.SwitchSpec<object>(
                options.StateKey,
                Fs.List((options.Cases ?? Enumerable.Empty<SwitchCase>())
                    .Select(c => System.Tuple.Create(c.Match, c.Child.Inner))),
                options.Default.Inner)));
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
