module Fuaran.UI.Tests.FragmentRegistryFixture

// ============================================================================
//  Phase 2244 — the fragment registry tree, shared by both renderers' halves.
//
//  Compiled into `Fuaran.UI.Tests` (the client half) and linked into
//  `Fuaran.UI.Renderer.Server.Tests` (the server half), so the two halves
//  assert over ONE tree rather than two copies kept in step.
//
//  It declares a fragment in every position class `NodeChildren` names: each
//  position the registry walk enters (a container's ordered list, a
//  `FragmentDecl` body, both `ErrorBoundary` arms, a `Switch` case and its
//  default, and — since Phase 2285 — `state.onLoading` and `state.onEmpty`),
//  and each position it does NOT enter (the envelope `fallback`, a
//  `FragmentRef` slot argument). Two decls share the name `dup` to pin the
//  last-wins rule on an invalid tree.
//
//  When this tree was written, at the phase's start, the client's and the
//  server's private walks already produced the same registry over it — the
//  divergence the phase was filed against had been closed by Phase 2041. What
//  the phase changed is that the agreement is now by construction: both
//  renderers call `FragmentExpansion.collectFragments`.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

/// The text a registered fragment's body renders, by fragment name.
let bodyText (name: string) : string = "FRAGMENT-BODY-" + name

let private body (name: string) : Node<obj> =
    Fuaran.markdown ("body-" + name) (bodyText name)

let private decl (name: string) (bodyNode: Node<obj>) : Node<obj> =
    // fuaran-validator: disable-next-line FUARAN056 — negative fixture: `dup` is declared twice on purpose
    Fuaran.fragmentDecl
        ("decl-" + name)
        { Defaults.fragmentDecl<obj> with
            Name = name
            Body = bodyNode }

let private declOf (name: string) : Node<obj> = decl name (body name)

let private page (id: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<obj> with
            Children = children }

let private as' (kind: NodeKind<obj>) (node: Node<obj>) : Node<obj> = { node with Kind = kind }

/// The fragment names the registry holds for `tree`, each with the id of the
/// body it holds — in name order, as a `Map` lists them.
let registered: (string * string) list =
    [ "dup", "body-dup-second"
      "in-decl-body", "body-in-decl-body"
      "in-error-boundary-child", "body-in-error-boundary-child"
      "in-error-boundary-fallback", "body-in-error-boundary-fallback"
      "in-modal", "body-in-modal"
      "in-on-empty", "body-in-on-empty"
      "in-on-loading", "body-in-on-loading"
      "in-scroll-area", "body-in-scroll-area"
      "in-switch-case", "body-in-switch-case"
      "in-switch-default", "body-in-switch-default"
      "in-tabs", "body-in-tabs"
      "outer", "outer-body" ]

/// The fragment names declared in positions the walk does not enter.
let notRegistered: string list = [ "in-envelope-fallback"; "in-slot-argument" ]

/// The tree.
let tree: Node<obj> =
    let first = decl "dup" (Fuaran.markdown "body-dup-first" (bodyText "dup-first"))
    let second = decl "dup" (Fuaran.markdown "body-dup-second" (bodyText "dup"))

    let tabs =
        page "tabs" []
        |> as' (
            NodeKind.Tabs
                { Defaults.tabs<obj> with
                    Children = [ declOf "in-tabs" ] }
        )

    let modal =
        page "modal" []
        |> as' (
            NodeKind.Modal
                { Defaults.modal<obj> with
                    Children = [ declOf "in-modal" ] }
        )

    let scrollArea =
        page "scroll-area" []
        |> as' (
            NodeKind.ScrollArea
                { Defaults.scrollArea<obj> with
                    Children = [ declOf "in-scroll-area" ] }
        )

    let boundary =
        Fuaran.errorBoundary
            "boundary"
            { Defaults.errorBoundary<obj> with
                Child = declOf "in-error-boundary-child"
                Fallback = declOf "in-error-boundary-fallback" }

    let switch =
        Fuaran.switch
            "switch"
            { Defaults.switch<obj> with
                Cases =
                    [ { Child = declOf "in-switch-case"
                        Match = Some "never-selected"
                        When = None } ]
                Default = declOf "in-switch-default" }

    let outer = decl "outer" (page "outer-body" [ body "outer"; declOf "in-decl-body" ])

    let stateArms =
        { Fuaran.markdown "state-arms" "STATE-ARMS" with
            State =
                Some
                    { Defaults.stateBehaviour<obj> with
                        OnLoading = Some(declOf "in-on-loading")
                        OnEmpty = Some(declOf "in-on-empty") } }

    let envelopeFallback =
        { Fuaran.markdown "envelope-fallback" "ENVELOPE-FALLBACK" with
            Fallback = Some(declOf "in-envelope-fallback") }

    let slotArgument =
        // fuaran-validator: disable-next-line FUARAN057 — `outer` is declared through the local `decl` helper, which the rule cannot see
        let r = Fuaran.fragmentRef "slot-argument" "outer"

        match r.Kind with
        | NodeKind.FragmentRef spec ->
            { r with
                Kind =
                    NodeKind.FragmentRef
                        { spec with
                            Args = Some(Map.ofList [ "slot", FragmentArg.SlotArg(declOf "in-slot-argument") ]) } }
        | _ -> r

    page
        "page"
        [ first
          tabs
          modal
          scrollArea
          boundary
          switch
          outer
          stateArms
          envelopeFallback
          slotArgument
          second ]

/// `tree` plus one `FragmentRef` to every name above, registered or not — the
/// shape a renderer's OUTPUT answers the registry question from.
let withRefs: Node<obj> =
    let names = (registered |> List.map fst) @ notRegistered

    page
        "refs-page"
        (tree
         :: (names |> List.map (fun name -> Fuaran.fragmentRef ("ref-" + name) name)))

/// A chain of `depth` nested containers with a decl at the bottom — deep enough
/// that a recursive registry walk overflows the stack.
let deep (depth: int) : Node<obj> =
    let mutable node = declOf "at-the-bottom"

    for i in depth .. -1 .. 1 do
        node <- page (sprintf "level-%d" i) [ node ]

    node

// ─── Phase 2285 — the fragment-scope parity document ────────────────────────
//
// The same wire document is asserted, byte for byte, by fuaran-ts's renderer
// suite (`fragmentScopeParity.test.tsx`), so both hosts certify one answer:
// decls in `state.onLoading` / `state.onEmpty` are registered and resolve; a
// decl under the envelope `fallback` or in a `FragmentRef` slot argument is
// not, and its reference renders the unresolved placeholder.

/// The parity document, as wire JSON.
let parityWire: string =
    """{"id":"page","kind":{"$type":"Box","children":[{"id":"host","kind":{"$type":"Markdown","text":"HOST"},"state":{"onEmpty":{"id":"d-empty","kind":{"$type":"FragmentDecl","body":{"id":"inner-tpl-empty","kind":{"$type":"Markdown","text":"BODY-tpl-empty"}},"name":"tpl-empty"}},"onLoading":{"id":"d-loading","kind":{"$type":"FragmentDecl","body":{"id":"inner-tpl-loading","kind":{"$type":"Markdown","text":"BODY-tpl-loading"}},"name":"tpl-loading"}}}},{"id":"fb-host","kind":{"$type":"Markdown","text":"FB-HOST"},"fallback":{"id":"d-fallback","kind":{"$type":"FragmentDecl","body":{"id":"inner-tpl-fallback","kind":{"$type":"Markdown","text":"BODY-tpl-fallback"}},"name":"tpl-fallback"}}},{"id":"arg-ref","kind":{"$type":"FragmentRef","args":{"slot":{"$type":"SlotArg","tree":{"id":"d-arg","kind":{"$type":"FragmentDecl","body":{"id":"inner-tpl-arg","kind":{"$type":"Markdown","text":"BODY-tpl-arg"}},"name":"tpl-arg"}}}},"name":"tpl-loading"}},{"id":"ref-loading","kind":{"$type":"FragmentRef","name":"tpl-loading"}},{"id":"ref-empty","kind":{"$type":"FragmentRef","name":"tpl-empty"}},{"id":"ref-fallback","kind":{"$type":"FragmentRef","name":"tpl-fallback"}},{"id":"ref-arg","kind":{"$type":"FragmentRef","name":"tpl-arg"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""

/// The fragment names the registry holds for `parityWire`, in name order.
let parityRegistered: string list = [ "tpl-empty"; "tpl-loading" ]

/// The ids each resolving reference renders its body under.
let parityExpandedIds: string list =
    [ "ref-loading.inner-tpl-loading"; "ref-empty.inner-tpl-empty" ]

/// The fragment names whose references render the unresolved placeholder.
let parityUnresolved: string list = [ "tpl-fallback"; "tpl-arg" ]

/// `parityWire` decoded.
let parityTree: Node<obj> =
    match Fuaran.UI.Generated.decodeNode parityWire with
    | Ok node -> node
    | Error e -> failwithf "the parity document failed to decode: %A" e
