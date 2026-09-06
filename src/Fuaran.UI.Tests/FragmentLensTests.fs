module Fuaran.UI.Tests.FragmentLensTests

// ============================================================================
//  The subtree slots `FragmentApply` can SEE.
//
//  Every recursion in that module — namespacing, substitution, the totality
//  refusal — runs on one child lens, so a slot the lens cannot reach produces
//  three symptoms of one omission at once:
//
//    * the marker is not substituted, so a bare `FragmentRef` is what renders;
//    * the inserted subtree's ids are not namespaced, so two refs binding the
//      same fragment capture each other;
//    * a self-reference hidden there is invisible to the totality check that
//      exists to refuse unbounded expansion.
//
//  The lens used to see the eight straightforward containers and the two
//  error-boundary arms. Three places a subtree legitimately lives were missing
//  and each is exercised below: a `Switch` case (and its default), a nested
//  `FragmentDecl` body, and a node's `OnLoading` / `OnEmpty` alternative arms.
//
//  `StateBehaviour.OnError` is deliberately NOT covered and its absence is
//  asserted rather than assumed: it is a function `exn -> Node`, so there is no
//  subtree to visit until it is applied, and no traversal in any host can
//  substitute into one.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ── fixtures ───────────────────────────────────────────────────────────────

let private bare (id: string) (kind: NodeKind<unit>) : Node<unit> =
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None
      Tooltip = None }

/// An unbound `FragmentRef` — the marker form a slot takes inside a body.
let private marker (id: string) (name: string) : Node<unit> =
    bare id (NodeKind.FragmentRef { Name = name; Args = None })

let private slotArg: Node<unit> = Fuaran.markdown "body" "slot content"

/// A fragment whose body puts the `content` slot marker wherever `place` puts
/// it. Every case below differs only in that placement, so a failure names the
/// slot shape rather than the fixture.
let private fragmentWith (place: Node<unit> -> Node<unit>) : ParamFragment<unit> =
    { Defaults.fragmentDecl with
        Name = "card"
        Holes = Some [ HoleDecl.Slot("content", None) ]
        Body =
            Fuaran.dashboard
                "card-root"
                { Defaults.dashboard<unit> with
                    Children = [ place (marker "content" "content") ] } }

let private applied (fragment: ParamFragment<unit>) (arg: Node<unit>) =
    FragmentApply.apply fragment "ref1" Map.empty (Map.ofList [ "content", arg ])

/// Every id in the tree, through a walk that is INDEPENDENT of the lens under
/// test — `StructuralQuery.children` is the tier's own containment relation,
/// plus the two state arms it deliberately excludes. Reading the result with
/// the same lens that produced it would make the assertions circular.
let rec private allIds (node: Node<unit>) : string list =
    let stateArms =
        match node.State with
        | None -> []
        | Some st -> [ st.OnLoading; st.OnEmpty ] |> List.choose id

    node.Id :: ((StructuralQuery.children node @ stateArms) |> List.collect allIds)

// ── the three placements ───────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "the fragment child lens reaches every subtree slot"
        [ test "a slot marker inside a SWITCH CASE is substituted and namespaced" {
              let fragment =
                  fragmentWith (fun slot ->
                      bare
                          "sw"
                          (NodeKind.Switch
                              { Defaults.switch<unit> with
                                  Cases = [ { Match = "a"; Child = slot } ]
                                  Default = Fuaran.markdown "sw-default" "none" }))

              match applied fragment slotArg with
              | Ok app ->
                  let ids = allIds app.Tree
                  Expect.contains ids "ref1.body" "the argument is inserted, under the ref's namespace"
                  Expect.isFalse (List.contains "content" ids) "and the marker is gone"
                  Expect.contains ids "sw-default" "the default arm is left alone"
              | Error e -> failtestf "apply should succeed: %s" e
          }

          test "a slot marker in a switch's DEFAULT arm is substituted too" {
              let fragment =
                  fragmentWith (fun slot ->
                      bare
                          "sw"
                          (NodeKind.Switch
                              { Defaults.switch<unit> with
                                  Cases =
                                      [ { Match = "a"
                                          Child = Fuaran.markdown "sw-a" "a" } ]
                                  Default = slot }))

              match applied fragment slotArg with
              | Ok app ->
                  let ids = allIds app.Tree
                  Expect.contains ids "ref1.body" "the default arm's marker is substituted"
                  Expect.contains ids "sw-a" "and the case arm is left alone"

                  // The rebuilder splits at the case count; a mis-paired
                  // rebuild would put the argument on the case instead.
                  match app.Tree |> allIds |> List.filter (fun id -> id = "ref1.body") with
                  | [ _ ] -> ()
                  | other -> failtestf "the argument appears exactly once, got %d" other.Length
              | Error e -> failtestf "apply should succeed: %s" e
          }

          test "a slot marker inside a NESTED FragmentDecl body is substituted" {
              let fragment =
                  fragmentWith (fun slot ->
                      bare
                          "inner"
                          (NodeKind.FragmentDecl
                              { Defaults.fragmentDecl<unit> with
                                  Name = "inner"
                                  Body = slot }))

              match applied fragment slotArg with
              | Ok app -> Expect.contains (allIds app.Tree) "ref1.body" "the nested body's marker is substituted"
              | Error e -> failtestf "apply should succeed: %s" e
          }

          test "a slot marker inside an OnEmpty / OnLoading arm is substituted" {
              // These arms render INSTEAD of the node, so a marker left
              // unsubstituted there is a `FragmentRef` drawn to the reader at
              // exactly the moment the data was empty.
              let fragment =
                  fragmentWith (fun slot ->
                      { bare "stateful" (NodeKind.Markdown { Text = TextSource.Literal "body" }) with
                          State =
                              Some
                                  { Defaults.stateBehaviour<unit> with
                                      OnEmpty = Some slot
                                      OnLoading = Some(Fuaran.markdown "spinner" "loading") } })

              match applied fragment slotArg with
              | Ok app ->
                  let ids = allIds app.Tree
                  Expect.contains ids "ref1.body" "the OnEmpty marker is substituted"
                  Expect.contains ids "spinner" "and the OnLoading arm survives"
              | Error e -> failtestf "apply should succeed: %s" e
          }

          test "an ABSENT state arm stays absent — the rebuild does not invent one" {
              // The rebuilder puts back exactly the arms that were there. A
              // present-only list that got re-seated by position could turn a
              // `None` arm into a `Some`, which would make a node render an
              // alternative it never declared.
              let fragment =
                  fragmentWith (fun slot ->
                      { bare "stateful" (NodeKind.Markdown { Text = TextSource.Literal "body" }) with
                          State =
                              Some
                                  { Defaults.stateBehaviour<unit> with
                                      OnEmpty = Some slot
                                      OnLoading = None } })

              match applied fragment slotArg with
              | Ok app ->
                  let rec findStateful (n: Node<unit>) =
                      if n.Id = "stateful" then
                          Some n
                      else
                          StructuralQuery.children n |> List.tryPick findStateful

                  match findStateful app.Tree with
                  | Some node ->
                      match node.State with
                      | Some st ->
                          Expect.isNone st.OnLoading "the absent arm is still absent"
                          Expect.isSome st.OnEmpty "and the present one is still present"
                      | None -> failtest "the node kept its state behaviour"
                  | None -> failtest "the stateful node survives the rewrite"
              | Error e -> failtestf "apply should succeed: %s" e
          }

          // ── totality sees what the lens sees ────────────────────────────

          test "a self-reference hidden in a SWITCH CASE is refused" {
              // The totality check runs on the same lens. Before it reached
              // switch cases, this argument bound happily and expanded without
              // bound at render.
              let hidden =
                  bare
                      "wrap"
                      (NodeKind.Switch
                          { Defaults.switch<unit> with
                              Cases =
                                  [ { Match = "a"
                                      Child = marker "loop" "card" } ]
                              Default = Fuaran.markdown "d" "d" })

              Expect.isError (applied (fragmentWith id) hidden) "a self-reference inside a switch case is refused"
          }

          test "a self-reference hidden in an OnEmpty arm is refused" {
              let hidden =
                  { bare "wrap" (NodeKind.Markdown { Text = TextSource.Literal "x" }) with
                      State =
                          Some
                              { Defaults.stateBehaviour<unit> with
                                  OnEmpty = Some(marker "loop" "card") } }

              Expect.isError (applied (fragmentWith id) hidden) "a self-reference inside an alternative arm is refused"
          }

          test "a self-reference hidden in a nested FragmentDecl body is refused" {
              let hidden =
                  bare
                      "wrap"
                      (NodeKind.FragmentDecl
                          { Defaults.fragmentDecl<unit> with
                              Name = "inner"
                              Body = marker "loop" "card" })

              Expect.isError (applied (fragmentWith id) hidden) "a self-reference inside a nested body is refused"
          }

          test "an argument with NO self-reference still binds — the refusal is not blanket" {
              // The go-red twin for the three refusals above: widening the lens
              // must not make every nested argument fail.
              let innocent =
                  bare
                      "wrap"
                      (NodeKind.Switch
                          { Defaults.switch<unit> with
                              Cases =
                                  [ { Match = "a"
                                      Child = Fuaran.markdown "inner" "text" } ]
                              Default = Fuaran.markdown "d" "d" })

              match applied (fragmentWith id) innocent with
              | Ok app ->
                  let ids = allIds app.Tree
                  Expect.contains ids "ref1.wrap" "the argument binds and is namespaced"
                  Expect.contains ids "ref1.inner" "including the subtree the widened lens now reaches"
              | Error e -> failtestf "an innocent nested argument must still bind: %s" e
          } ]
