module Fuaran.UI.Tests.FragmentApplyTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ============================================================================
//  Phase 180 — renderer-side fragment application (FragmentApply): tree-slot
//  substitution, hygienic id namespacing, totality self-reference refusal.
// ============================================================================

/// A parameterised card: a dashboard with a `content` slot marker (an unbound
/// FragmentRef) + a title pane. Holes: a `title` value + a `content` slot.
let private cardFragment: ParamFragment<unit> =
    let body =
        Fuaran.dashboard
            "card-root"
            { Defaults.dashboard<unit> with
                Children =
                    [ Fuaran.markdown "card-title" "Title"
                      { Id = NodeId "content"
                        Kind =
                          NodeKind.FragmentRef
                              { Name = FragmentId "content"
                                Args = Map.empty }
                        State = Defaults.stateBehaviour
                        Style = Defaults.style
                        Accessibility = None
                        Motion = None
                        ExtraAttributes = None } ] }

    { Name = FragmentId "card"
      Holes =
        [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), None)
          HoleDecl.Slot("content", None) ]
      Body = body
      Effect = EffectClass.pureDeterministic }

let private slotArg: Node<unit> = Fuaran.markdown "body" "slot content"

let private childIds (n: Node<unit>) : string list =
    match n.Kind with
    | NodeKind.Box( s) -> s.Children |> List.map (fun c -> let (NodeId i) = c.Id in i)
    | _ -> []

[<Tests>]
let tests =
    testList
        "Fragment.Apply"
        [ test "application with a complete arg set renders the bound tree (slot substituted)" {
              match
                  FragmentApply.apply
                      cardFragment
                      "ref1"
                      (Map.ofList [ "title", (box "Hello" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ])
              with
              | Ok app ->
                  // The FragmentRef slot marker is gone; the arg subtree took its
                  // place (namespaced under the ref id).
                  let ids = childIds app.Tree
                  Expect.contains ids "card-title" "the title pane survives"
                  Expect.contains ids "ref1.body" "the slot arg is inserted + namespaced"
                  Expect.isFalse (List.contains "content" ids) "the slot marker is replaced"

                  Expect.equal
                      app.ValueBindings
                      (Map.ofList [ "ref1.title", (box "Hello" |> Unchecked.nonNull) ])
                      "value binding keyed by hole address"
              | Error e -> failtestf "apply should succeed: %s" e
          }

          test "hygiene: two refs binding the same fragment produce DOM-unique ids (no capture)" {
              let app1 =
                  FragmentApply.apply
                      cardFragment
                      "refA"
                      (Map.ofList [ "title", (box "A" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ])

              let app2 =
                  FragmentApply.apply
                      cardFragment
                      "refB"
                      (Map.ofList [ "title", (box "B" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ])

              match app1, app2 with
              | Ok a1, Ok a2 ->
                  Expect.contains (childIds a1.Tree) "refA.body" "ref A namespaces its slot id"
                  Expect.contains (childIds a2.Tree) "refB.body" "ref B namespaces its slot id"
                  // The two refs' value bindings live in distinct addresses.
                  Expect.isFalse
                      (Set.intersect
                          (Set.ofSeq (Map.toSeq a1.ValueBindings |> Seq.map fst))
                          (Set.ofSeq (Map.toSeq a2.ValueBindings |> Seq.map fst))
                       |> Set.isEmpty
                       |> not)
                      "value-binding addresses are disjoint between refs"
              | _ -> failtest "both applications should succeed"
          }

          test "totality: a slot argument referencing the fragment itself is refused" {
              // The slot arg is itself a FragmentRef back to "card" — unbounded.
              let recursiveArg: Node<unit> =
                  { Id = NodeId "loop"
                    Kind =
                      NodeKind.FragmentRef
                          { Name = FragmentId "card"
                            Args = Map.empty }
                    State = Defaults.stateBehaviour
                    Style = Defaults.style
                    Accessibility = None
                    Motion = None
                    ExtraAttributes = None }

              let r =
                  FragmentApply.apply
                      cardFragment
                      "ref1"
                      (Map.ofList [ "title", (box "X" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", recursiveArg ])

              Expect.isError r "self-referential slot arg refused (totality)"
          }

          test "a missing required hole is an error" {
              let r =
                  FragmentApply.apply
                      cardFragment
                      "ref1"
                      (Map.ofList [ "title", (box "X" |> Unchecked.nonNull) ])
                      Map.empty

              Expect.isError r "unbound 'content' slot is refused"
          }

          test "a value-space violation is an error" {
              let r =
                  FragmentApply.apply
                      cardFragment
                      "ref1"
                      (Map.ofList [ "title", (box "" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ])

              Expect.isError r "empty title violates StringLen(1,40)"
          }

          // ── Slot kind-constraint enforcement (task 16) ──────────────────────
          //
          // A `HoleDecl.Slot(_, Some "<Kind>")` requires its bound subtree's
          // kind-tag (`Kind.name`) to equal the constraint. Previously the
          // constraint round-tripped on the wire but was never checked; now a
          // matching kind binds and a mismatched kind is refused at bind time.
          let constrainedCard (constraintKind: string option) : ParamFragment<unit> =
              { cardFragment with
                  Holes =
                      [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), None)
                        HoleDecl.Slot("content", constraintKind) ] }

          test "a slot arg whose kind matches the constraint binds" {
              let r =
                  FragmentApply.apply
                      (constrainedCard (Some "Markdown"))
                      "ref1"
                      (Map.ofList [ "title", (box "Hello" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ]) // slotArg is a Markdown node

              Expect.isOk r "a Markdown slot arg satisfies the 'Markdown' constraint"
          }

          test "a slot arg whose kind violates the constraint is refused" {
              // slotArg is a Markdown node, but the slot demands a Box.
              match
                  FragmentApply.apply
                      (constrainedCard (Some "Box"))
                      "ref1"
                      (Map.ofList [ "title", (box "Hello" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ])
              with
              | Error e ->
                  Expect.stringContains e "Box" "the error names the required kind"
                  Expect.stringContains e "Markdown" "the error names the actual kind"
              | Ok _ -> failtest "a Markdown arg must not satisfy a 'Box' slot constraint"
          }

          test "an unconstrained slot (None) accepts any kind" {
              let r =
                  FragmentApply.apply
                      (constrainedCard None)
                      "ref1"
                      (Map.ofList [ "title", (box "Hello" |> Unchecked.nonNull) ])
                      (Map.ofList [ "content", slotArg ])

              Expect.isOk r "an unconstrained slot binds any kind"
          } ]
