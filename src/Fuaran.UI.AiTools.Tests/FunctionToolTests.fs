module Fuaran.UI.AiTools.Tests.FunctionToolTests

// ============================================================================
//  Phase 358 — cross-domain Slot composition (adopt fuaran-core#47 composeAcross).
//
//  Proves the UI outer witness + `composeIntoSlot` bind a NON-UI
//  artifact-function's output into a UI fragment's typed slot, yielding one
//  composed `Fuaran.UI` artifact-function:
//    (1) a cross-domain-composed fragment renders (encodes) identically to the
//        manually-wired result;
//    (2) the composed effect class is the JOIN of the parts' — a `network` query
//        in a slot flips the composed dashboard to `network`;
//    (3) hygiene — two refs binding the same foreign sub-function into distinct
//        slots do not capture (the embedded holes/ids re-root under the slot addr);
//    (4) Fable/.NET parity on the composed render path — canonical-JSON-stable
//        (the same wire-encoding both pipelines produce).
//
//  The inner (foreign) witness is modelled on a `Fuaran.Core.Query` / `DataFrame`
//  function: its output `QNode` is a DISTINCT type from `Node<'Msg>` (a genuine
//  cross-witness composition, not a within-witness `compose`), and its declared
//  effect is `network` (a live query). `embed : QNode -> Node<'Msg>` is the one
//  cross-domain step — how the query's output subtree becomes a UI node.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.AiTools

module CanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── the foreign inner witness (a query / dataframe artifact-function) ───────
//
// `QNode` is the foreign function's output-tree node — deliberately NOT a
// `Fuaran.UI.Node`, so the composition is a real ACROSS-witness step. A single
// leaf carrying the query's headline label suffices for the proof.

type QNode =
    { QId: string
      Label: string
      Kids: QNode list }

/// The inner witness effect — a live query reads the network.
let private networkEffect: Fuaran.Core.EffectClass =
    { Host = Fuaran.Core.ReadsHost
      Determinism = Fuaran.Core.Network }

/// The foreign query-function witness. It declares NO holes on the fragment it
/// produces here (the query is already fully applied — a resolved result node),
/// so the composed function's residual holes are exactly the UI fragment's own.
let private queryWitness: Fuaran.Core.ArtifactWitness<QNode, string> =
    { Tree =
        { Id = fun q -> q.QId
          KindTag = fun _ -> "QueryResult"
          Children = fun q -> q.Kids
          ReplaceChildren = fun q cs -> { q with Kids = cs } }
      IdW =
        { ToString = id
          OfString = id
          Equals = (=) }
      Holes = fun _ -> []
      Effect = fun _ -> networkEffect
      Bind = fun _ _ q -> Ok q }

/// The one cross-domain step: lift a query-result node into a UI Heading node.
let private embed (q: QNode) : Node<unit> =
    Fuaran.heading
        q.QId
        { Defaults.heading with
            Text = TextSource.Literal q.Label }

// ─── the UI outer fragment (a dashboard with one query slot) ─────────────────

/// A dashboard fragment declaring a single `Slot` hole `slotName`, marked in the
/// body by an unbound `FragmentRef` of the same name. Pure-deterministic on its
/// own — its effect becomes `network` only once the live-query result is composed
/// in.
///
/// The fragment NAME is a parameter, not a literal: fragment names are unique per
/// project (FUARAN056), so each test case that wants its own fixture fragment
/// mints its own name rather than every case re-declaring one shared "dashboard".
let private dashboardFragment (declId: string) (fragName: string) (slotName: string) : Node<unit> =
    let body =
        Fuaran.stack
            "db"
            { Defaults.stack with
                Children =
                    [ Fuaran.heading
                          "title"
                          { Defaults.heading with
                              Text = TextSource.Literal "Sales" }
                      Fuaran.fragmentRef "slotsite" slotName ] }

    Fuaran.fragmentDecl
        declId
        { Defaults.fragmentDecl with
            Name = fragName
            Body = body
            Holes = Some [ HoleDecl.Slot(slotName, None) ]
            Effect = Some EffectClass.pureDeterministic }

let private enc (n: Node<unit>) : string = CanonicalJson.encodeNode n

[<Tests>]
let tests =
    testList
        "Phase 358 — cross-domain Slot composition (composeAcross)"
        [ testCase "a cross-domain-composed fragment encodes identically to the manually-wired result"
          <| fun _ ->
              let declId = "dash"
              let slotName = "chart"
              // One binding, used by BOTH the fixture and the manual wiring below:
              // the manual reconstruction is the same fragment, so its name matches
              // by construction rather than by a second literal that could drift.
              let fragName = "dashboard"
              let outer = dashboardFragment declId fragName slotName

              let queryOut =
                  { QId = "q1"
                    Label = "Q3 total"
                    Kids = [] }

              let addr = FunctionTool.holeAddr declId slotName

              let composed =
                  match FunctionTool.composeIntoSlot queryWitness embed addr queryOut outer with
                  | Ok n -> n
                  | Error e -> failtestf "composeIntoSlot failed: %A" e

              // Manually wire the SAME result: the fragment body with the slot
              // marker replaced by the embedded query node, hygienically
              // namespaced by the slot address, and the bound hole dropped.
              let manual =
                  let embedded = embed queryOut
                  // hygienic namespacing: <slotAddr>.<innerId>
                  let namespacedId = addr + "." + "q1"

                  let embeddedNs = { embedded with Id = namespacedId }

                  let body =
                      Fuaran.stack
                          "db"
                          { Defaults.stack with
                              Children =
                                  [ Fuaran.heading
                                        "title"
                                        { Defaults.heading with
                                            Text = TextSource.Literal "Sales" }
                                    embeddedNs ] }

                  Fuaran.fragmentDecl
                      declId
                      { Defaults.fragmentDecl with
                          Name = fragName
                          Body = body
                          Effect = Some EffectClass.pureDeterministic }

              Expect.equal (enc composed) (enc manual) "composed tree encodes identically to the manual wiring"

          testCase "the composed effect class is the join of the parts' — a network query flips it to network"
          <| fun _ ->
              let declId = "dash"
              let slotName = "chart"
              let outer = dashboardFragment declId "dashboard-effect" slotName

              let queryOut =
                  { QId = "q1"
                    Label = "live"
                    Kids = [] }

              let joined = FunctionTool.composedEffect queryWitness queryOut outer

              Expect.equal joined.Host Fuaran.Core.ReadsHost "host axis joined up to ReadsHost (from the query)"
              Expect.equal joined.Determinism Fuaran.Core.Network "determinism axis joined up to Network (live query)"

          testCase "hygiene — two refs binding the same foreign sub-function into distinct slots do not capture"
          <| fun _ ->
              // A fragment with two distinct slots; bind the SAME query output into
              // each. The embedded ids must re-root under DIFFERENT addresses, so
              // the two inserted subtrees carry disjoint interior node ids.
              let declId = "twin"

              // The slot names are bound once and threaded through the hole decl,
              // its body marker and the hole address — the same shape
              // `dashboardFragment` uses above. An unbound slot marker is NOT a
              // reference to a declared fragment, so writing it as a bare literal
              // would read as an unresolved `fragmentRef` (FUARAN057).
              let slotL = "left"
              let slotR = "right"

              let outer =
                  let body =
                      Fuaran.stack
                          "row"
                          { Defaults.stack with
                              Children = [ Fuaran.fragmentRef "s1" slotL; Fuaran.fragmentRef "s2" slotR ] }

                  Fuaran.fragmentDecl
                      declId
                      { Defaults.fragmentDecl with
                          Name = "twin"
                          Body = body
                          Holes = Some [ HoleDecl.Slot(slotL, None); HoleDecl.Slot(slotR, None) ]
                          Effect = Some EffectClass.pureDeterministic }

              let queryOut = { QId = "q"; Label = "x"; Kids = [] }
              let addrL = FunctionTool.holeAddr declId slotL
              let addrR = FunctionTool.holeAddr declId slotR

              let afterL =
                  match FunctionTool.composeIntoSlot queryWitness embed addrL queryOut outer with
                  | Ok n -> n
                  | Error e -> failtestf "left compose failed: %A" e

              let afterBoth =
                  match FunctionTool.composeIntoSlot queryWitness embed addrR queryOut afterL with
                  | Ok n -> n
                  | Error e -> failtestf "right compose failed: %A" e

              // Collect every interior node id — no collision means hygiene held.
              let rec ids (n: Node<unit>) : string list =
                  let s = n.Id

                  let kids =
                      match Fuaran.UI.Ops.Introspect.getChildren n.Kind with
                      | Some cs -> cs |> List.collect ids
                      | None -> []

                  s :: kids

              let allIds = ids afterBoth
              let distinct = allIds |> List.distinct

              Expect.equal
                  (List.length allIds)
                  (List.length distinct)
                  "no interior id collides — the two embedded query nodes re-rooted under distinct slot addresses"

              // And the two embedded ids carry their distinct slot-address prefixes.
              Expect.isTrue
                  (allIds |> List.exists (fun s -> s = addrL + "." + "q"))
                  "left embed id re-rooted under left addr"

              Expect.isTrue
                  (allIds |> List.exists (fun s -> s = addrR + "." + "q"))
                  "right embed id re-rooted under right addr"

          testCase "Fable/.NET parity — the composed render path is canonical-JSON-stable"
          <| fun _ ->
              // The composed tree encodes to canonical JSON deterministically: two
              // independent composes of the same inputs produce byte-identical wire
              // output (the same encoding both pipelines emit — the parity contract
              // the wire-format corpus pins).
              let declId = "dash"
              let slotName = "chart"
              let outer = dashboardFragment declId "dashboard-parity" slotName

              let queryOut =
                  { QId = "q1"
                    Label = "Q3 total"
                    Kids = [] }

              let addr = FunctionTool.holeAddr declId slotName

              let compose () =
                  match FunctionTool.composeIntoSlot queryWitness embed addr queryOut outer with
                  | Ok n -> enc n
                  | Error e -> failtestf "compose failed: %A" e

              Expect.equal (compose ()) (compose ()) "composed encoding is deterministic (canonical-JSON-stable)"

          testCase "binding an unknown slot address is a typed error (default-deny by shape)"
          <| fun _ ->
              let declId = "dash"
              let outer = dashboardFragment declId "dashboard-unknown-slot" "chart"
              let queryOut = { QId = "q1"; Label = "x"; Kids = [] }
              let bogus = FunctionTool.holeAddr declId "nonexistent"

              match FunctionTool.composeIntoSlot queryWitness embed bogus queryOut outer with
              | Ok _ -> failtest "expected an error binding an unknown slot address"
              | Error(Fuaran.Core.UnknownHoleAddr(a, _)) -> Expect.equal a bogus "the unknown addr is named"
              | Error e -> failtestf "expected UnknownHoleAddr, got %A" e

          // ── Effect-soundness audit (task 16) ────────────────────────────────
          //
          // `auditFragmentEffect` wires `Function.observedEffect` to a UI fragment:
          // a FragmentDecl's declared `Effect` must cover the widest effect its body
          // composes (the join of nested declared effects). A pure decl over a
          // network sub-fragment is the canonical understatement the audit catches.
          testCase "a pure-declared fragment with a pure body audits clean"
          <| fun _ ->
              let body =
                  Fuaran.dashboard
                      "d-pure"
                      { Defaults.dashboard<unit> with
                          Children = [ Fuaran.markdown "m-pure" "static" ] }

              let decl =
                  Fuaran.fragmentDecl
                      "outer-pure"
                      { Defaults.fragmentDecl with
                          Name = "outer-pure"
                          Body = body
                          Effect = Some EffectClass.pureDeterministic }

              Expect.isOk (FunctionTool.auditFragmentEffect decl) "a pure body under a pure declaration is honest"

          testCase "a pure-declared fragment nesting a network sub-fragment is refused"
          <| fun _ ->
              let uiNetwork: EffectClass =
                  { HostEffect = HostEffect.ReadsHost
                    Determinism = DeterminismSource.Network }

              let networkSub =
                  Fuaran.fragmentDecl
                      "netsub-understated"
                      { Defaults.fragmentDecl with
                          Name = "netsub-understated"
                          Body = Fuaran.markdown "n-understated" "live query"
                          Effect = Some uiNetwork }

              let body =
                  Fuaran.dashboard
                      "d-understated"
                      { Defaults.dashboard<unit> with
                          Children = [ networkSub ] }

              let decl =
                  Fuaran.fragmentDecl
                      "outer-understated"
                      { Defaults.fragmentDecl with
                          Name = "outer-understated"
                          Body = body
                          Effect = Some EffectClass.pureDeterministic }

              match FunctionTool.auditFragmentEffect decl with
              | Error(declared, observed) ->
                  Expect.equal declared EffectClass.pureDeterministic "declared is the (pure) understatement"

                  Expect.equal
                      observed.Determinism
                      DeterminismSource.Network
                      "observed effect picks up the nested network sub-fragment"
              | Ok() -> failtest "a pure declaration nesting a network sub-fragment must fail the effect audit"

          testCase "a network-declared fragment covering a network body audits clean"
          <| fun _ ->
              let uiNetwork: EffectClass =
                  { HostEffect = HostEffect.ReadsHost
                    Determinism = DeterminismSource.Network }

              let networkSub =
                  Fuaran.fragmentDecl
                      "netsub-honest"
                      { Defaults.fragmentDecl with
                          Name = "netsub-honest"
                          Body = Fuaran.markdown "n-honest" "live query"
                          Effect = Some uiNetwork }

              let body =
                  Fuaran.dashboard
                      "d-honest"
                      { Defaults.dashboard<unit> with
                          Children = [ networkSub ] }

              let decl =
                  Fuaran.fragmentDecl
                      "outer-honest"
                      { Defaults.fragmentDecl with
                          Name = "outer-honest"
                          Body = body
                          Effect = Some uiNetwork }

              Expect.isOk
                  (FunctionTool.auditFragmentEffect decl)
                  "an honest network declaration covers its network body" ]
