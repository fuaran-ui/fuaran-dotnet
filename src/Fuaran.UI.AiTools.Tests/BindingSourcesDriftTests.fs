module Fuaran.UI.AiTools.Tests.BindingSourcesDriftTests

// ============================================================================
//  Phase 213 — the drift guard.
//
//  `Fuaran.UI.AiTools` used to carry a HAND-DUPLICATED copy of the renderer's
//  binding-source record (`BindingProbeSources`), because AiTools must stay
//  free of `Fuaran.UI.Renderer`. The copy drifted: it never gained `Locale`
//  (Phase 102), `ComputedContext` (137), `CapabilityInvoker` (283), `Now`
//  (765) or `I18nResolver`. Nothing failed when it drifted — that is the whole
//  problem. Phase 213 promoted the record into `Fuaran.UI` (the package both
//  already depend on) so there is one field set; these tests are what makes a
//  re-split, or a field added on only one side, FAIL rather than pass quietly.
//
//  Both assertions are REFLECTIVE on purpose. A statically-typed comparison
//  would stop compiling under the regression it is meant to catch, and a build
//  error in an unrelated file is a much worse signal than a named test failure
//  that says exactly what diverged.
//
//  Note this TEST project references `Fuaran.UI.Renderer.Core` — the
//  emission-agnostic spine (FSharp.Core + Fable.Core, no Feliz / React). Since
//  Phase 1532 the shipping `Fuaran.UI.AiTools` package references it too, and
//  what that package must still carry none of is the rendering-framework
//  SUBSTRATE; the boundary test below asserts exactly that, and its own comment
//  records why the assertion was re-spelled.
// ============================================================================

open System.Reflection
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.UI.AiTools
open Fuaran.UI.AiTools.Types
open Fuaran.UI.AiTools.Seams

/// The canonical field set, pinned. Adding a field to
/// `Fuaran.UI/BindingSources.fs` is a deliberate act — it widens what every
/// host must furnish and what the introspection surface can see — so it lands
/// here in the same change, which is the moment to ask whether the probe's
/// declining arms in `BindingProbe.fs` should still decline.
let private canonicalFields =
    [ "QueryResults"
      "State"
      "Filters"
      "Selections"
      "ComputedContext"
      "I18n"
      "I18nResolver"
      "Locale"
      "CapabilityInvoker"
      "Now"
      // Phase 1586 — the session-held live-`Transform` store. It is a HOST
      // seam, not host data, and the probe declines it for the same reason it
      // declines `CapabilityInvoker`: there is nothing to read off a function
      // or an interface, and an introspection surface that reported one as
      // present would be reporting the wiring rather than the values.
      "LiveTransforms" ]

let private introspectionSourcesType =
    FSharpType.GetRecordFields(typeof<IntrospectionContext>)
    |> Array.find (fun (p: PropertyInfo) -> p.Name = "Sources")
    |> _.PropertyType

[<Tests>]
let bindingSourcesDriftTests =
    testList
        "Phase 213 — introspection / renderer BindingSources unification"
        [ test "the introspection source shape IS the renderer's, not a copy of it" {
              Expect.equal
                  introspectionSourcesType
                  typeof<Fuaran.UI.Renderer.BindingResolver.BindingSources>
                  "IntrospectionContext.Sources and the renderer resolver's BindingSources must be ONE type — re-declaring either as its own record reopens the drift Phase 213 closed"

              Expect.equal
                  introspectionSourcesType
                  typeof<Fuaran.UI.BindingSources>
                  "the one type is the canonical record promoted into Fuaran.UI (FSharp.Core only), not a renderer type AiTools reaches for"
          }

          test "the canonical record carries every field the renderer resolves against" {
              let actual =
                  FSharpType.GetRecordFields(typeof<Fuaran.UI.BindingSources>)
                  |> Array.map _.Name
                  |> List.ofArray

              Expect.equal
                  actual
                  canonicalFields
                  "the canonical BindingSources field set changed — update `canonicalFields` here in the same change, and re-read BindingProbe.fs's declining arms while you are there"

              // Named separately because this is the field that actually
              // drifted, and a diff on a ten-item list is easy to skim past.
              Expect.contains
                  actual
                  "Locale"
                  "`Locale` is the field the hand-duplicated probe record silently dropped (Phase 102 landed it renderer-side only)"
          }

          test "the empty introspection context IS the renderer's empty sources" {
              Expect.equal
                  (box emptyContext.Sources)
                  (box Fuaran.UI.Renderer.BindingResolver.empty)
                  "a second set of identity-defaults is the same drift class as a second record"
          }

          // ── Phase 1532 — the boundary, named by the substrate it excludes ──
          //
          // Phase 213 spelled this "references no assembly whose name starts
          // `Fuaran.UI.Renderer`", and that spelling caught more than the
          // property it protects. The property is that the introspection surface
          // an AI consumer drives carries NO rendering-framework substrate; the
          // prefix also excluded `Fuaran.UI.Renderer.Core`, which is the
          // emission-agnostic spine — FSharp.Core + Fable.Core and no more, no
          // Feliz, no React, no browser. Phase 1532 delegates the probe's seven
          // hand-mirrored declining arms to that spine's resolver, so the prefix
          // form would now fail on a change that does not touch the property.
          //
          // The rewrite is a SHARPENING in two directions, not a relaxation: it
          // names the substrate rather than a prefix that happened to cover it,
          // and it walks the TRANSITIVE closure rather than the direct references,
          // so substrate arriving through a new intermediate is caught where the
          // direct check could never have seen it.
          test "the shipping AiTools assembly carries no rendering-framework substrate" {
              // Asserted on the SHIPPING assembly (`Fuaran.UI.AiTools`), never on
              // this test assembly.
              let banned =
                  [ "Fuaran.UI.Renderer" // the Feliz client renderer itself
                    "Fuaran.UI.Renderer.Server"
                    "Fuaran.UI.Renderer.Web"
                    "Feliz"
                    "Feliz.ViewEngine"
                    "Fable.React"
                    "Browser.Dom"
                    "React" ]

              let seen = System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)

              let found = ResizeArray<string>()

              // Assemblies that fail to load are skipped rather than failing the
              // test: an unresolvable framework facade is not evidence of a Feliz
              // dependency, and treating it as one would make this red for a
              // reason that has nothing to do with the boundary.
              let rec walk (asm: Assembly) =
                  for reference in asm.GetReferencedAssemblies() do
                      let name = reference.Name |> Option.ofObj |> Option.defaultValue ""

                      if name <> "" && seen.Add name then
                          if List.contains name banned then
                              found.Add(sprintf "%s (via %s)" name (asm.GetName().Name))

                          try
                              walk (Assembly.Load reference)
                          with _ ->
                              ()

              walk typeof<IntrospectionContext>.Assembly

              Expect.isEmpty
                  (List.ofSeq found)
                  "Fuaran.UI.AiTools must carry no Feliz / Fable.React / Browser / renderer assembly anywhere in its closure — the introspection surface is driven .NET-side and a rendering framework there is the dependency this boundary exists to keep out. Fuaran.UI.Renderer.Core is deliberately NOT banned: it is the emission-agnostic spine, and the binding probe delegates its resolution to it"

              // The negative control the prefix form never had: an empty `found`
              // means nothing unless the walk actually saw a closure.
              Expect.isGreaterThan
                  seen.Count
                  3
                  "the reference walk found almost nothing — a vacuous pass reports the same empty list as a genuine one"
          } ]

// ─── Phase 1532 — the delegated arms, asserted rather than described ────────
//
// Seven arms of `BindingProbe.tryResolveBinding` used to DECLINE with
// `NotResolvedYet` on the rationale that their resolution needed "renderer-side"
// machinery. The machinery moved to the emission-agnostic spine and the
// rationale stopped being true; these pin that the probe now reports what the
// resolver reports — and, in the two negative cases, that delegation did not
// turn a decline into an invention.

/// `box` laundered to the nonnull `obj` the store and `ResolvedValue` contracts
/// carry — the same helper shape `Fuaran.UI.Ops.Tests` uses.
let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

/// The table the scalar-Transform pins aggregate over.
let private salesTable =
    Fuaran.Core.Embedded
        { Schema = [ "amount", Fuaran.Core.FloatType ]
          Columns =
            [ Fuaran.Core.Column.create "amount" Fuaran.Core.FloatType [ Fuaran.Core.Float 40.0; Fuaran.Core.Float 2.0 ] ] }

let private contextWith (sources: Fuaran.UI.BindingSources) = { emptyContext with Sources = sources }

[<Tests>]
let delegationTests =
    testList
        "Phase 1532 — the probe delegates to the resolver"
        [ test "a scalar Transform returns the resolver's 1x1 result cell" {
              // `groupBy [] [sum amount]` — the canonical scalar terminal.
              let binding: Fuaran.UI.Types.Binding<float> =
                  Fuaran.UI.Types.Binding.Transform(
                      Fuaran.UI.Types.TransformSource.Data salesTable,
                      [ Fuaran.Core.GroupBy(
                            [],
                            [ { Name = "total"
                                Fn = Fuaran.Core.AggFn.Sum
                                Of = "amount" } ]
                        ) ],
                      None
                  )

              // Precondition, so a green assertion below cannot be vacuous: the
              // resolver itself computes this.
              Expect.equal
                  (Fuaran.UI.Renderer.BindingResolver.resolveScalarFloat
                      Fuaran.UI.Renderer.BindingResolver.empty
                      binding)
                  (Fuaran.UI.Renderer.BindingResolver.Resolved 42.0)
                  "precondition: the resolver resolves the scalar Transform"

              match
                  BindingProbe.tryResolveScalarBindingWith
                      Fuaran.UI.Renderer.BindingResolver.cellToFloat
                      emptyContext
                      binding
              with
              | ResolvedBindingResult.Resolved r ->
                  Expect.equal r.ResolvedValue (Some(nn 42.0)) "the probe reports the resolver's result cell"
                  Expect.equal r.Expression "$transform" "the wire expression still labels the binding kind"
              | ResolvedBindingResult.Failed f ->
                  failtestf "the probe declined a Transform the resolver resolves: %s" f.Message
          }

          test "a ROWS Transform still returns the rows, not a scalar complaint" {
              // The other half of the same rule: which entry point a slot uses is
              // a property of the SLOT. A DataGrid.Source Transform is its rows,
              // and resolving it through the scalar path would report a 2x1 shape
              // error for a binding that renders perfectly.
              let binding: Fuaran.UI.Types.Binding<Fuaran.UI.Types.Row seq> =
                  Fuaran.UI.Types.Binding.Transform(Fuaran.UI.Types.TransformSource.Data salesTable, [], None)

              match BindingProbe.tryResolveBinding emptyContext binding with
              | ResolvedBindingResult.Resolved r ->
                  match r.ResolvedValue with
                  | Some value -> Expect.equal (Seq.length (unbox<Fuaran.UI.Types.Row seq> value)) 2 "both rows"
                  | None -> failtest "the rows slot resolved with no value"
              | ResolvedBindingResult.Failed f -> failtestf "the probe declined a rows Transform: %s" f.Message
          }

          test "Now returns the host-furnished instant" {
              let ctx =
                  contextWith
                      { Fuaran.UI.BindingSources.empty with
                          Now = "2026-09-06T11:22:33Z" }

              let binding: Fuaran.UI.Types.Binding<string> =
                  Fuaran.UI.Types.Binding.Now((fun o -> string o), None)

              match BindingProbe.tryResolveBinding ctx binding with
              | ResolvedBindingResult.Resolved r ->
                  Expect.equal
                      r.ResolvedValue
                      (Some(nn "2026-09-06T11:22:33Z"))
                      "the probe reports the instant the renderer reads"
              | ResolvedBindingResult.Failed f ->
                  failtestf "the probe declined a Now the resolver resolves: %s" f.Message
          }

          test "an unfurnished instant stays the slot's empty state" {
              // Delegation must not turn a decline into an invention. With no
              // host instant the resolver says NotResolved and the probe must say
              // NotResolvedYet — never a plausible wrong date.
              let binding: Fuaran.UI.Types.Binding<string> =
                  Fuaran.UI.Types.Binding.Now((fun o -> string o), None)

              match BindingProbe.tryResolveBinding emptyContext binding with
              | ResolvedBindingResult.Failed f ->
                  Expect.equal f.Code BindingErrorCode.NotResolvedYet "an unset instant is the empty state"
              | ResolvedBindingResult.Resolved r ->
                  failtestf "the probe invented an instant the host never furnished: %A" r.ResolvedValue
          }

          test "I18n returns the catalogue's translation, and a miss stays a miss" {
              let ctx =
                  contextWith
                      { Fuaran.UI.BindingSources.empty with
                          I18nResolver =
                              Fuaran.UI.Renderer.BindingResolver.makeI18nResolver (Map.ofList [ "greet", "Hello" ]) }

              match BindingProbe.tryResolveBinding ctx (Fuaran.UI.Types.Binding.I18n("greet", None)) with
              | ResolvedBindingResult.Resolved r ->
                  Expect.equal r.ResolvedValue (Some(nn "Hello")) "the probe reports the catalogue's translation"
              | ResolvedBindingResult.Failed f -> failtestf "the probe declined a key the catalogue holds: %s" f.Message

              match BindingProbe.tryResolveBinding ctx (Fuaran.UI.Types.Binding.I18n("absent", None)) with
              | ResolvedBindingResult.Failed f ->
                  Expect.equal f.Code BindingErrorCode.NotResolvedYet "a missing translation is the empty state"
              | ResolvedBindingResult.Resolved r -> failtestf "an unregistered i18n key resolved to %A" r.ResolvedValue
          }

          test "Format returns the formatter's own string" {
              let ctx =
                  contextWith
                      { Fuaran.UI.BindingSources.empty with
                          Locale = "en-GB" }

              let fmt = Fuaran.UI.Types.Format.Percent(Some 0)

              let binding: Fuaran.UI.Types.Binding<string> =
                  Fuaran.UI.Types.Binding.Format(
                      Fuaran.UI.Types.Binding.Static(Some 0.42),
                      fmt,
                      Fuaran.UI.Types.LocaleSource.Ambient
                  )

              match BindingProbe.tryResolveBinding ctx binding with
              | ResolvedBindingResult.Resolved r ->
                  Expect.equal
                      r.ResolvedValue
                      (Some(nn (Fuaran.UI.Renderer.Formatting.format "en-GB" fmt 0.42)))
                      "the probe reports the shared formatter's string, never one of its own"
              | ResolvedBindingResult.Failed f ->
                  failtestf "the probe declined a Format the resolver resolves: %s" f.Message
          }

          test "Invoke returns the host invoker's ready value, and pending stays pending" {
              let ctx =
                  contextWith
                      { Fuaran.UI.BindingSources.empty with
                          CapabilityInvoker =
                              fun capabilityId _ ->
                                  if capabilityId = "answer" then
                                      Fuaran.UI.Types.Deferred.Ready(nn 42.0)
                                  else
                                      Fuaran.UI.Types.Deferred.Pending }

              match BindingProbe.tryResolveBinding ctx (Fuaran.UI.Types.Binding.Invoke("answer", [])) with
              | ResolvedBindingResult.Resolved r ->
                  Expect.equal r.ResolvedValue (Some(nn 42.0)) "the probe reports the host invoker's value"
              | ResolvedBindingResult.Failed f ->
                  failtestf "the probe declined an Invoke the host answered: %s" f.Message

              match BindingProbe.tryResolveBinding ctx (Fuaran.UI.Types.Binding.Invoke("other", [])) with
              | ResolvedBindingResult.Failed f ->
                  Expect.equal f.Code BindingErrorCode.NotResolvedYet "a pending capability is the empty state"
              | ResolvedBindingResult.Resolved r -> failtestf "a pending capability resolved to %A" r.ResolvedValue
          }

          test "Computed returns the closure's answer over the resolver's state merge" {
              let ctx =
                  contextWith
                      { Fuaran.UI.BindingSources.empty with
                          State = Map.ofList [ "count", nn 7 ] }

              let binding: Fuaran.UI.Types.Binding<int> =
                  Fuaran.UI.Types.Binding.Computed(fun (c: obj) ->
                      match (c :?> Fuaran.UI.Types.BindingContext).State.TryFind "count" with
                      | Some v -> unbox<int> v
                      | None -> -1)

              match BindingProbe.tryResolveBinding ctx binding with
              | ResolvedBindingResult.Resolved r ->
                  Expect.equal r.ResolvedValue (Some(nn 7)) "the probe reports the closure's answer"
              | ResolvedBindingResult.Failed f ->
                  failtestf "the probe declined a Computed the resolver resolves: %s" f.Message
          }

          test "the store-reading arms keep the probe's own richer error codes" {
              // The arms that were NOT delegated, and why: the resolver has one
              // undifferentiated `NotResolved` where the §4i tool surface reports
              // `SourceUnregistered` vs `NotResolvedYet` vs `TypeMismatch`.
              // Delegating them would have lost that vocabulary.
              let unregistered: Fuaran.UI.Types.Binding<float> =
                  Fuaran.UI.Types.Binding.Query("nope", (fun o -> unbox<float> o), None)

              match BindingProbe.tryResolveBinding emptyContext unregistered with
              | ResolvedBindingResult.Failed f ->
                  Expect.equal
                      f.Code
                      BindingErrorCode.SourceUnregistered
                      "an unregistered query is distinguishable from a pending one"
              | ResolvedBindingResult.Resolved r -> failtestf "an unregistered query resolved to %A" r.ResolvedValue

              let mistyped: Fuaran.UI.Types.Binding<float> =
                  Fuaran.UI.Types.Binding.Filter("f", None)

              let ctx =
                  contextWith
                      { Fuaran.UI.BindingSources.empty with
                          Filters = Map.ofList [ "f", nn "not a number" ] }

              match BindingProbe.tryResolveBinding ctx mistyped with
              | ResolvedBindingResult.Failed f ->
                  Expect.equal
                      f.Code
                      BindingErrorCode.TypeMismatch
                      "a filter store value of the wrong type is a TypeMismatch"
              | ResolvedBindingResult.Resolved r -> failtestf "a mistyped filter resolved to %A" r.ResolvedValue
          } ]
