# Migration — `Action.Choose` and `Action.Require` (Phase 2107, `Fuaran.UI` 0.105.0)

**What changes.** The closed `Action<'Msg>` union gains two cases, the bounded program core's selection and guard:

```fsharp
| Choose of condition: Binding<JVal> * whenTrue: Action<'Msg> * whenFalse: Action<'Msg> option * exit: Binding<JVal> option
| Require of condition: Binding<JVal>
```

No wire document written before this release changes: both are new `$type` discriminators (WIRE_FORMAT §3.6.34).

**What breaks.** Any exhaustive `match` over `Action` in your code. Under `TreatWarningsAsErrors` that is FS0025.

**The edit.** Add the two arms. A walk that recurses into nested actions should recurse into `whenTrue` and
`whenFalse` exactly as it does into `Chain`'s members and `Confirm`'s continuations — or, better, fold through the
generated `Fuaran.UI.Generated.Action.fold`, which reaches every nested action of every recursive case:

```diff
 match action with
 | Action.Chain actions -> …
 | Action.Confirm(_, onConfirm, onCancel) -> …
+| Action.Choose(condition, whenTrue, whenFalse, exit) -> …
+| Action.Require condition -> …
```

**If your code interprets actions itself** (a custom dispatcher rather than the bounded program loop): do not
evaluate the two cases. They are control flow whose one evaluator is the bounded core (`Fuaran.UI.Program`). Refuse a
gesture that reaches either one whole, before any member runs — refusing only the arm would let the members after a
`Require` run, which is the guard failing open. This is what the shipped client renderer and server-driven driver do.

**Authoring (C#).** `Actions.Choose(condition, whenTrue[, whenFalse])` and `Actions.Require(condition)`.

**Verify.** `dotnet build` is clean, and a tree carrying a `Choose` round-trips through
`Fuaran.UI.Ops.JsonDecode.decodeNode` and `Fuaran.UI.Generated.encodeNodeJson` unchanged.

**Rollback.** Pin `Fuaran.UI` 0.104.0; no document using the two cases decodes there.
