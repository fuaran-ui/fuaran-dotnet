# Renderer tenancy — what is scoped, what is still process-global, and the shape that would fix it

_Phase 1648. This is a DESIGN NOTE and ships no behaviour. Phase 1532 moved several renderer globals
onto `RenderContext` and recorded three it could not, because they need a decision rather than a
patch. That decision is what this page works out, so that the phase that takes it starts from a
shape rather than from the finding._

## The property at stake

One process, many trees. An SSR host serving several tenants, a `Mount` boundary hosting a guest, a
test runner sharing one process across cases — in each, two trees are being rendered that must not
see each other's declarations. Anything the renderer holds in a module-level `mutable` is shared by
all of them, so the property fails silently: nothing errors, one tenant simply reads another's
registration.

`RenderContext` is the carrier that already exists for this. It threads `Scope`, `Sources`,
`Runtime`, `TelemetrySink`, `SessionContext` and `ActionSink` through every render, and a
`Mount` boundary re-points `Scope` for its guest's subtree. Where a thing can ride that record, it
should.

## What is already scoped

- **`Binding.State` reads and `Action.SetState` writes** — `RenderContext.Scope` selects
  `StateStore.forScope`, so a guest's state is isolated from its host's (Phase 266, §4o).
- **Custom renderers** — `IFuaranRuntime.TryGetCustomRendererInScope` takes the scope.
- **Persistent-key declarations** — declared per store instance, and cleared with it
  (Phase 1532; see `docs/migrations/1532-state-store-reset-clears-declarations.md`).

## What is still process-global, and why each is different

### 1. `Render.renderGuestHook` — global, and CORRECTLY so

`renderGuestHook` is set exactly once, in-module, at the bottom of `Render.fs`
(`renderGuestHook <- Some render`). It exists to break a recursion the module structure cannot
express directly: a `Mount` needs to render its guest with the renderer that is still being defined.
It is a module-initialisation detail, not a registration surface — nothing outside the module writes
it, and there is nothing tenant-specific for it to carry.

**No change is needed here, and this is worth stating because it looks like the other two.** A phase
that scoped all three uniformly would add a field to `RenderContext` that every render carries and
no render varies.

### 2. `Render.guestSeam` — global, and the fix is mechanical

`installGuestSeam` / `clearGuestSeam` / `currentGuestSeam` are a host-facing registration: a host
installs the seam that resolves guest trees. Two hosts in one process share one seam, and the last
installer wins.

It is `obj`-typed for the same reason `renderGuestHook` is (the renderer cannot name the host's
`'Msg`), which is incidental here — the fix does not touch the typing:

```fsharp
// on RenderContext
GuestSeam: GuestSeam option        // None = the process-global default, as today
```

resolved as `ctx.GuestSeam |> Option.orElse (currentGuestSeam ())`. A host that installs globally is
byte-identical to today; a host that wants per-tenant resolution passes one per render, and a `Mount`
boundary can hand its guest a different seam from its host's. **The global stays as the default** —
retiring it would break every host that installs at startup, for no gain to a host that has only one.

This is a task line, not a decision. It was grouped with item 3 in the residue and does not share
its problem.

### 3. `Affordances.providers` — global, and it CANNOT ride `RenderContext` as written

This is the decision the residue named, and the reason it is one.

`Affordances.registerProvider` appends to a module-level list; `Affordances.enumerate` folds it. The
enumeration answers "what natural-language commands does this page declare", and its **only** reader
is `DebugGlobal.getAffordances(moduleId?)` — the `window.__fuaran` console surface, called from a
devtools REPL by a human or an agent.

That caller has no `RenderContext` and cannot be given one. It is not inside a render; it is a
question asked of the page, from outside, at an arbitrary moment. Threading the registry through
`RenderContext` would put the registry somewhere its reader structurally cannot reach — which is why
this is not a patch.

**Three shapes are available, and they are not equivalent:**

**(a) Scope-keyed registry, keyed the way `StateStore` is.** `registerProvider` takes an optional
scope; `enumerate` takes one; `getAffordances` gains an optional scope argument that a caller
supplies. Consistent with the tenancy model already in the tree, and the smallest conceptual
addition. Its cost is on the DEBUG surface: `getAffordances()` with no scope has to mean something,
and both available meanings are wrong — the global registrations only (silently under-reporting a
scoped page) or the union across scopes (a cross-tenant read from an untrusted console, which is the
disclosure this whole area exists to prevent). **The honest form is that the no-scope call answers
the global registry only and SAYS SO in its payload**, so a caller can tell "nothing declared" from
"nothing declared at this scope".

**(b) Registry on the runtime.** `IFuaranRuntime` is already the per-host seam, already reached by
the debug surface, and already the thing a `Mount` re-points for a guest. Moving the registry onto it
makes tenancy fall out of a seam that exists rather than adding a parallel one. Its cost is a
member on a published interface — a breaking change for every implementor, and `IFuaranRuntime` has
several outside this repo.

**(c) Leave it global and DOCUMENT the boundary.** The registry declares what a PAGE can be asked to
do, and a page is one document. Multi-tenant SSR does not render into a shared DOM, so the
process-global registry is only genuinely shared in the case where two guests coexist in one
document — which is the `Mount` case, and `Mount` is already an isolation boundary with its own
scope. The residue's own note that "the affordance registry cannot ride a `RenderContext` as
written" is a fact about the reader, not about the requirement.

**The unresolved question, stated so the deciding phase does not have to re-find it:** whether a
`Mount` guest's affordances should be enumerable by the HOST page's console at all. If yes, (c) is
already correct and only needs a paragraph. If no, the registry needs a boundary, and (b) is the
cleaner of the two ways to give it one — at the price of a breaking interface change that wants its
own version. **That is a product question about what a guest owes its host, not an implementation
choice**, which is why this note stops here rather than picking.

#### DECIDED (Phase 1674): yes, enumerable — WITH ATTRIBUTION. Option (c), plus a field.

**The ruling.** A `Mount` guest's affordances ARE enumerable by the host page's console, and every
module now carries the scope that declared it: absent for the page itself, the guest's scope id for a
mounted one.

**Why the boundary the question contemplated would protect nothing.** `window.__fuaran` runs on the
host page, and a console on that page already holds the guest's DOM, its `data-fuaran-node-id`
attributes and its rendered content. Withholding the guest's *declarations* from a caller that can
read the guest's *output* is not an isolation boundary; it is a gap in a list. What `Mount` actually
isolates is STATE and DISPATCH, and an affordance declaration is a description of an offer rather
than the ability to take it — invoking one still goes through the guest's own dispatch path, which is
where the gate already is and where it should stay. The disclosure risk this area exists to prevent
is a cross-tenant read of DATA; a phrase a guest publishes so that it can be addressed is the
opposite of data it holds back.

**What WOULD have been wrong is the unattributed union**, and that is the half the three shapes above
did not separate out. Without a scope on each module a caller cannot tell a host affordance from a
guest's, and an agent driving the page by natural language addresses the wrong tenant — a defect that
presents as the command going to the wrong place, not as an error. So the answer is not (c) as
written but (c) plus the field.

**Why not (a) or (b).** (a) makes `getAffordances()` with no scope mean one of two things that are
both wrong, which the note above already says; that dilemma only exists because (a) treats the scope
as a FILTER. Treated as an ATTRIBUTE it disappears — the no-argument call answers everything and says
who declared each thing, and a caller that wants one tenant filters on a value it can see. (b) adds a
member to `IFuaranRuntime`, a published interface with implementors outside this repo, to answer a
question that does not require one.

**What landed.** `ModuleAffordance` gains `Scope: string option`, placed last (source-compatible at
`{ existing with … }`). `Affordances.registerProviderInScope` is the scoped registration;
`registerProvider` is now `registerProviderInScope None`, so a host that never mounts a guest is
unaffected by any of this. The stamp is applied by `enumerate` FROM THE REGISTRATION — a provider
cannot claim a scope it did not register under, which is the whole value of the field, and a test
asserts exactly that against a provider that tries. `getAffordances` emits `scope` on every module,
`null` for the page itself.

**Still process-global, deliberately.** The registry itself did not move. This ruling says the
global registry is CORRECT — one page, one registry, with tenancy carried per entry rather than per
store — so item 3 is closed rather than deferred.

## What this note deliberately does not cover

`Fuaran.UI.Renderer`'s other module-level mutables that are genuinely process-wide by nature — the
diagnostics channel, the module-init hooks above — and the `StateStore` scope registry itself, whose
lifecycle is documented at `StateStore.disposeScope`.
