using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.FSharp.Core;
using FsGen = Fuaran.UI.Generated;
using FsAction = Fuaran.UI.Generated.Action<object>;
using FsCanon = Fuaran.UI.OpStream.Abstractions.CanonicalJson;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 1153 — the veneer's Action vocabulary, pinned three ways.
//
//  1. A REFLECTION pin over the F# tier, in the spirit of Coverage.cs: every generated
//     spec slot typed as a bare `Action<'Msg>` (data, as opposed to the closure-shaped
//     `('a -> Action<'Msg>)` slots beside it) must have a `FuaranAction` member on the
//     corresponding options record. A FOURTH such slot added to the F# tier fails here
//     with no edit to this file, which is what "the veneer cannot regress" has to mean
//     if it is to survive the next kind. Coverage.cs cannot notice it: it reflects
//     NodeKind CASES, and this is a spec-record FIELD — the same blind spot Phase 801
//     recorded and Phase 873 worked around by hand.
//
//  2. BYTE-PARITY per slot against an independently-built F# smart-ctor tree (the
//     Phase 306/307 conformance shape), plus one verbatim tie to the committed corpus.
//
//  3. The DELIBERATE ABSENCE: no `Dispatch` on the facade, asserted rather than
//     documented, and `EncodeForTransport` accepting every tree this surface can author.
//
// Every check carries a negative control, so none can pass vacuously.
internal static class Actions
{
    public static void Run(Harness h)
    {
        SlotCoverage(h);
        Parity(h);
        DeliberateAbsence(h);
    }

    // ── 1. Every bare-Action spec slot is authorable ─────────────────────────────

    /// <summary>The generated spec slots whose type is an action VALUE rather than a
    /// host closure — discovered, never listed.</summary>
    private static IEnumerable<(Type Spec, PropertyInfo Slot)> BareActionSlots()
    {
        var optionOfAction = typeof(FSharpOption<>).MakeGenericType(typeof(FsAction));

        // `Generated` is an F# MODULE, so every spec record is a type NESTED inside the
        // `Fuaran.UI.Generated` class rather than a top-level type in a namespace of
        // that name — walking the assembly's top-level types finds nothing at all. The
        // "found slots at all" check below exists because that failure is silent.
        var generated = typeof(FsGen.Node<object>).DeclaringType!;

        foreach (var t in generated.GetNestedTypes(BindingFlags.Public))
        {
            var bare = t.Name.Split('`')[0];
            if (!bare.EndsWith("Spec", StringComparison.Ordinal))
            {
                continue;
            }

            Type closed;
            try
            {
                closed = t.IsGenericTypeDefinition
                    ? (t.GetGenericArguments().Length == 1 ? t.MakeGenericType(typeof(object)) : null!)
                    : t;
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (closed is null)
            {
                continue;
            }

            foreach (var p in closed.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.PropertyType == typeof(FsAction) || p.PropertyType == optionOfAction)
                {
                    yield return (closed, p);
                }
            }
        }
    }

    private static void SlotCoverage(Harness h)
    {
        var slots = BareActionSlots().ToList();

        // The pin is only meaningful if the discovery found anything at all — a
        // reflection walk that silently matches nothing is the failure mode this
        // guards against, not a pass.
        h.Check(
            "1153: the bare-Action slot discovery finds slots at all",
            slots.Count >= 3,
            $"found {slots.Count}: {string.Join(", ", slots.Select(s => $"{s.Spec.Name}.{s.Slot.Name}"))}");

        foreach (var (spec, slot) in slots)
        {
            var optionsName = spec.Name.Split('`')[0];
            optionsName = optionsName[..^"Spec".Length] + "Options";

            var options = typeof(FuaranNode).Assembly.GetType($"Fuaran.UI.CSharp.{optionsName}");
            if (options is null)
            {
                h.Check($"1153: {optionsName} exists for {spec.Name}.{slot.Name}", false, "no such options record on the veneer");
                continue;
            }

            var member = options.GetProperty(slot.Name, BindingFlags.Public | BindingFlags.Instance);

            h.Check(
                $"1153: {optionsName}.{slot.Name} authors {spec.Name}.{slot.Name}",
                member is not null && member.PropertyType == typeof(FuaranAction),
                member is null
                    ? $"no public {optionsName}.{slot.Name}"
                    : $"{optionsName}.{slot.Name} is {member.PropertyType.Name}, expected FuaranAction");
        }
    }

    // ── 2. Byte-parity, slot by slot ─────────────────────────────────────────────

    private static void Parity(Harness h)
    {
        // Button.OnClick — a Notify, the action that reaches a host with no closure.
        var csButton = Fuaran.Button(new()
        {
            Id = "save",
            Label = "Save",
            Variant = ButtonVariant.Primary,
            OnClick = FuaranAction.Notify("audit.saved", Payload.Object(("id", 42), ("draft", false))),
        });

        h.ByteEqual("1153: Button.OnClick Notify mirrors the F# smart ctor", csButton.Encode(), ActionOracle.ButtonNotify());

        // Negative control: the same tree with a different channel must NOT match, so
        // the parity check above cannot be passing on the tree shape alone.
        var csButtonOther = Fuaran.Button(new()
        {
            Id = "save",
            Label = "Save",
            Variant = ButtonVariant.Primary,
            OnClick = FuaranAction.Notify("audit.other", Payload.Object(("id", 42), ("draft", false))),
        });
        h.Check("1153 control: a different Notify channel changes the bytes", csButtonOther.Encode() != ActionOracle.ButtonNotify());

        // And the pre-1153 default is unchanged: an unset OnClick is the empty chain
        // this veneer always emitted, so the addition is additive on the wire too.
        var plainButton = Fuaran.Button(new() { Id = "save", Label = "Save", Variant = ButtonVariant.Primary }).Encode();
        h.Check("1153: an unset OnClick still emits the empty chain", plainButton.Contains("\"onClick\":{\"$type\":\"Chain\",\"ops\":[]}"), plainButton);

        // Form.OnSubmit — a chain, proving the composite case reaches a slot.
        var csForm = Fuaran.Form(new()
        {
            Id = "hire",
            SubmitLabel = "Send",
            Fields = [FormField.Text("name", "Name")],
            OnSubmit = FuaranAction.Chain(
                FuaranAction.Notify("form.submitted", "hire"),
                FuaranAction.CallIntoQuery("/api/candidates", "candidates")),
        });
        h.ByteEqual("1153: Form.OnSubmit chain mirrors the F# smart ctor", csForm.Encode(), ActionOracle.FormChain());

        // Modal.OnDismiss — the optional slot. Present when set…
        var csModal = Fuaran.Modal(new()
        {
            Id = "confirm",
            Heading = "Discard changes?",
            OnDismiss = FuaranAction.Notify("modal.dismissed", "confirm"),
        });
        h.ByteEqual("1153: Modal.OnDismiss mirrors the F# smart ctor", csModal.Encode(), ActionOracle.ModalDismiss());

        // …and ABSENT when unset, which is the half that matters: an empty chain here
        // would be an explicit "raise nothing" where the wire said nothing at all.
        var plainModal = Fuaran.Modal(new() { Id = "confirm", Heading = "Discard changes?" }).Encode();
        h.Check("1153 control: no onDismiss when undeclared", !plainModal.Contains("onDismiss"), plainModal);

        // A verbatim tie to the committed corpus — the one check here that measures the
        // veneer against the SPECIFICATION rather than against another host in this repo.
        // `nodes/call-into.json` embeds its children as whole node documents, so a
        // C#-authored button appears in it byte for byte EXCEPT for one difference,
        // which is the Phase 307 reconciliation restated: the smart ctor injects
        // `"accessibility":{"role":"button"}` and the corpus fixture was authored bare.
        // That prefix is stripped explicitly rather than the check being weakened to a
        // shape assertion — and the strip is itself asserted, so a smart ctor that
        // stopped injecting ARIA would fail here rather than quietly skip.
        if (Corpus.Available)
        {
            var fixture = Corpus.ReadFixture("nodes/call-into.json");

            CorpusMatch(h, fixture, "btn-fetch-total", "Fetch total", FuaranAction.CallIntoState("/api/total", "total"));
            CorpusMatch(h, fixture, "btn-fetch-orders", "Fetch orders", FuaranAction.CallIntoQuery("/api/orders", "orders"));

            // Negative control: a wrong target must not be found, or `Contains` would be
            // reporting on the fixture's size rather than on the bytes.
            var wrong = BareButton("btn-fetch-total", "Fetch total", FuaranAction.CallIntoState("/api/total", "grand-total"));
            h.Check("1153 control: a wrong state key is not in the fixture", !fixture.Contains(wrong.Json), wrong.Json);
        }
    }

    private const string InjectedButtonAria = "\"accessibility\":{\"role\":\"button\"},";

    /// <summary>Encode a C#-authored button and strip the ARIA the smart ctor injects,
    /// so what remains is comparable with the bare corpus fixture. Reports whether the
    /// strip actually happened.</summary>
    private static (string Json, bool Stripped) BareButton(string id, Text label, FuaranAction onClick)
    {
        var encoded = Fuaran.Button(new()
        {
            Id = id,
            Label = label,
            Variant = ButtonVariant.Primary,
            OnClick = onClick,
        }).Encode();

        return encoded.Contains(InjectedButtonAria)
            ? (encoded.Replace(InjectedButtonAria, string.Empty), true)
            : (encoded, false);
    }

    private static void CorpusMatch(Harness h, string fixture, string id, Text label, FuaranAction onClick)
    {
        var (json, stripped) = BareButton(id, label, onClick);

        h.Check($"1153: {id} carries the smart ctor's injected ARIA", stripped, json);
        h.Check($"1153: {id} matches nodes/call-into.json byte for byte (ARIA aside)", fixture.Contains(json), json);
    }

    // ── 3. What the veneer must NOT be able to author ────────────────────────────

    private static void DeliberateAbsence(Harness h)
    {
        var names = typeof(FuaranAction)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        h.Check(
            "1153: no Dispatch on the action facade",
            !names.Any(n => n.Contains("Dispatch", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", names));

        // The control for that absence: the vocabulary that IS meant to be there is.
        h.Check(
            "1153 control: Notify / Call / Chain are on the facade",
            names.Contains("Notify") && names.Contains("Call") && names.Contains("Chain")
            && names.Contains("CallIntoState") && names.Contains("CallIntoQuery"),
            string.Join(", ", names));

        // Phase 1126 — WriteToClipboard is on the facade, and it takes a `Text`
        // rather than a `string`. Both halves are asserted: a host that added the
        // member with the pre-1126 payload type would pass a name check and be
        // unable to author the shape the phase exists for.
        var clipboard = typeof(FuaranAction).GetMethod(
            "WriteToClipboard",
            BindingFlags.Public | BindingFlags.Static);

        h.Check("1126: WriteToClipboard is on the action facade", clipboard is not null, string.Join(", ", names));
        h.Check(
            "1126: its payload is a Text (a TextSource), not a string",
            clipboard?.GetParameters() is [{ ParameterType: var pt }] && pt == typeof(Text),
            clipboard?.GetParameters().FirstOrDefault()?.ParameterType.Name ?? "<absent>");

        // And there is no clipboard READ anywhere on the facade — the Appendix A
        // decline, asserted rather than described. This one is a name check on
        // purpose: the decline is about a capability existing at all.
        h.Check(
            "1126: no clipboard READ on the action facade",
            !names.Any(n => n.Contains("ReadClipboard", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("ReadFromClipboard", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", names));

        // Every tree this surface can author survives transport — the other half of
        // the Dispatch absence, stated as a property rather than as a promise.
        var tree = Fuaran.Card(new()
        {
            Id = "panel",
            Heading = "Actions",
            Children =
            [
                Fuaran.Button(new()
                {
                    Id = "ping",
                    Label = "Ping",
                    OnClick = FuaranAction.Chain(
                        FuaranAction.Notify("sample.ping", Payload.Object(("at", "now"))),
                        FuaranAction.CallIntoState("/api/status", "status")),
                }),
                Fuaran.Form(new()
                {
                    Id = "note",
                    Fields = [FormField.Text("body", "Note")],
                    OnSubmit = FuaranAction.Notify("note.submitted", true),
                }),
            ],
        });

        var ok = tree.TryEncodeForTransport(out var json, out var lossy);
        h.Check(
            "1153: a Notify/Call tree passes EncodeForTransport with no lossy slot",
            ok && lossy.Count == 0 && json == tree.Encode(),
            ok ? "(encoded, but the bytes differ from Encode())" : string.Join(", ", lossy.Select(l => $"{l.NodeId} ({l.Slot})")));
    }
}

// The independent F# smart-ctor oracle for the three action slots. Built with a
// hand-assembled spec through the F# smart constructors — a construction path
// distinct from the options records — so a byte match proves the veneer's
// options→spec mapping is faithful rather than merely self-consistent. FSharp.Core
// interop is isolated here, as it is in Program.cs's `Oracle`.
#nullable disable
internal static class ActionOracle
{
    private static FsGen.TextSource Lit(string s) => FsGen.TextSource.NewLiteral(s);

    private static FsAction Notify(string channel, params (string Key, global::Fuaran.Core.JVal Value)[] members) =>
        FsAction.NewNotify(
            channel,
            global::Fuaran.Core.JVal.NewJObj(
                Microsoft.FSharp.Collections.ListModule.OfSeq(
                    members.Select(m => Tuple.Create(m.Key, m.Value)))));

    internal static string ButtonNotify()
    {
        // Ctor arg order = Generated.fs ButtonSpec declaration order
        // (Label, OnClick, Variant, Icon, Tooltip, Disabled).
        var spec = new FsGen.ButtonSpec<object>(
            Lit("Save"),
            Notify("audit.saved", ("id", global::Fuaran.Core.JVal.NewJInt(42)), ("draft", global::Fuaran.Core.JVal.NewJBool(false))),
            FsGen.ButtonVariant.Primary,
            FSharpOption<string>.None,
            FSharpOption<FsGen.TextSource>.None,
            FSharpOption<FsGen.Binding<bool>>.None);

        return FsCanon.encodeNode(global::Fuaran.UI.Fuaran.button<object>("save", spec));
    }

    internal static string FormChain()
    {
        // Generated FormField declares (Id, Kind, Label, Required, Help, Rule); the
        // Text kind is (value, onChange), and the veneer's FormField.Text supplies a
        // no-op handler + a Static "" value, which this mirrors.
        var field = new FsGen.FormField<object>(
            "name",
            FsGen.FormFieldKind<object>.NewText(
                FSharpOption<FsGen.Binding<string>>.Some(FsGen.Binding<string>.NewStatic(FSharpOption<string>.Some(""))),
                FSharpOption<Microsoft.FSharp.Core.FSharpFunc<string, FsAction>>.Some(
                    FuncConvert.FromFunc<string, FsAction>(_ => FsAction.NewChain(Microsoft.FSharp.Collections.FSharpList<FsAction>.Empty)))),
            Lit("Name"),
            false,
            FSharpOption<FsGen.TextSource>.None,
            FSharpOption<FsGen.FieldRule>.None);

        var chain = FsAction.NewChain(
            Microsoft.FSharp.Collections.ListModule.OfSeq(new[]
            {
                FsAction.NewNotify("form.submitted", global::Fuaran.Core.JVal.NewJStr("hire")),
                FsAction.NewCall(
                    "/api/candidates",
                    FSharpOption<Microsoft.FSharp.Core.FSharpFunc<object, object>>.None,
                    FSharpOption<FsGen.CallResultTarget>.Some(FsGen.CallResultTarget.NewQuery("candidates"))),
            }));

        // Generated FormSpec ctor order (Fields, OnSubmit, SubmitLabel, Disabled).
        var spec = new FsGen.FormSpec<object>(
            Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { field }),
            chain,
            Lit("Send"),
            FSharpOption<FsGen.Binding<bool>>.None);

        return FsCanon.encodeNode(global::Fuaran.UI.Fuaran.form<object>("hire", spec));
    }

    internal static string ModalDismiss()
    {
        // Generated ModalSpec ctor order (Children, Dismissable, OnDismiss, Open,
        // Heading, Modality, Anchor). Phase 1119 appended the last two; Modal +
        // no anchor is the wire identity, so these bytes are unchanged.
        var spec = new FsGen.ModalSpec<object>(
            Microsoft.FSharp.Collections.FSharpList<FsGen.Node<object>>.Empty,
            true,
            FSharpOption<FsAction>.Some(FsAction.NewNotify("modal.dismissed", global::Fuaran.Core.JVal.NewJStr("confirm"))),
            FsGen.Binding<bool>.NewStatic(FSharpOption<bool>.Some(false)),
            FSharpOption<FsGen.TextSource>.Some(Lit("Discard changes?")),
            FsGen.ModalityKind.Modal,
            FSharpOption<string>.None);

        return FsCanon.encodeNode(global::Fuaran.UI.Fuaran.modal<object>("confirm", spec));
    }
}
