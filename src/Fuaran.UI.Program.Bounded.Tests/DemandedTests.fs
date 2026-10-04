module Fuaran.UI.Program.Bounded.Tests.DemandedTests

// ─── The demanded-effect projection + the host-coverage validator ───
//
// `Demanded.ofTree` answers "what can this program EVER ask for" as data, by a
// total static walk over the closed action vocabulary — before any event runs.
// `Demanded.check` turns that into a verdict against one host's coverage.
//
// These tests pin three things, and the third is the one that keeps the other
// two honest:
//
//  1. WHAT IS DEMANDED — every action arm that reaches a host contributes, from
//     every wire-survivable slot, anywhere in the traversal surface.
//  2. WHAT IS REFUSED — an unregistered effect is named; a registered one the
//     gate refuses is named DIFFERENTLY; a covered tree yields nothing.
//  3. THAT THE CHECK CAN GO RED — the probe below deliberately widens a demand
//     past a host's coverage and asserts the checker reports it. A coverage
//     check that only ever runs against passing input is indistinguishable from
//     one that returns the empty list unconditionally, and the difference is
//     invisible in a green suite.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.UI.Program.BoundedDriver

// ─── fixtures ───────────────────────────────────────────────────────

/// A button carrying one action — the wire-survivable slot the walk reads.
let private btn (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            OnClick = action }

/// A dashboard wrapping the given children.
let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

/// One button's demands, the shape most tests want.
let private ofAction (action: Action<obj>) : DemandedProjection =
    Demanded.ofTree (dash [ btn "b" action ])

let private jstr (s: string) : Fuaran.Core.JVal = Fuaran.Core.JStr s

/// A host that registers the named effects and permits every one of them.
let private hostWith (names: string list) : HostCoverage =
    HostCoverage.nothing
    |> HostCoverage.withEffects names
    |> HostCoverage.permissive

let private stubRender (n: Node<obj>) : string = $"<f id='{n.Id}'/>"

// ─── tests ──────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 892 — demanded-effect projection + host-coverage validator"
        [

          // ── what is demanded ──────────────────────────────────────

          test "a tree demanding nothing projects an empty document" {
              let p = Demanded.ofTree (dash [ Fuaran.markdown "m" "static" ])
              Expect.isEmpty p.Effects "no effects"
              Expect.isEmpty p.HostCalls "no host calls"
              Expect.isEmpty p.StateNamespaces "no namespaces"
              Expect.isEmpty p.OpaqueHandlers "no opaque handlers"
          }

          test "the four effect-bearing arms are demanded by their registry discriminator" {
              // The names must be exactly the strings a host registry is keyed
              // on, or a demanded effect and a registered one never meet.
              Expect.equal
                  (ofAction (Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self))).Effects
                  [ "Navigate" ]
                  "Navigate"

              Expect.equal
                  (ofAction (Action.WriteToClipboard(TextSource.Literal "x"))).Effects
                  [ "WriteToClipboard" ]
                  "WriteToClipboard"

              Expect.equal
                  (ofAction (Action.ReadFileBody("f", None, FileReadEncoding.Text, None))).Effects
                  [ "ReadFileBody" ]
                  "ReadFileBody"

              // `Print` is payload-free, which makes it the arm most easily
              // missed: the projection's own arm-to-discriminator map carries a
              // wildcard, so an unnamed effect-bearing arm reports NOTHING here
              // rather than failing to compile. A host that does not offer
              // printing would then pass a coverage check it should fail.
              Expect.equal (ofAction Action.Print).Effects [ "Print" ] "Print"
          }

          test "the walk is STATIC — an unsafe route is still a Navigate demand" {
              // The interpreter refuses this route at dispatch time. The
              // projection is not asking whether a use will succeed; it is
              // asking what the program can ask for, and the answer is the same
              // either way. A walk that quietly dropped it would under-report
              // exactly the capability a host most wants to know about.
              let p =
                  ofAction (Action.Navigate(TextSource.Literal "javascript:alert(1)", NavigateTarget.Self))

              Expect.equal p.Effects [ "Navigate" ] "Navigate is demanded regardless of the route's safety"
          }

          test "Chain contributes every arm, de-duplicated and sorted" {
              let p =
                  ofAction (
                      Action.Chain
                          [ Action.Navigate(TextSource.Literal "/a", NavigateTarget.Self)
                            Action.WriteToClipboard(TextSource.Literal "x")
                            Action.Navigate(TextSource.Literal "/b", NavigateTarget.Self)
                            Action.SetState("cart.total", Some(jstr "1"), None) ]
                  )

              Expect.equal p.Effects [ "Navigate"; "WriteToClipboard" ] "distinct + sorted"

              Expect.equal
                  p.StateNamespaces
                  [ { Namespace = "cart"
                      Written = true
                      Read = false } ]
                  "the write's namespace"
          }

          test "each host-call arm names its own channel" {
              let p =
                  ofAction (
                      Action.Chain
                          [ Action.Call("/api/orders", None, None)
                            Action.Invoke("cap.print", [])
                            Action.Notify("audit", jstr "x")
                            Action.AiTool("summarise", jstr "x") ]
                  )

              Expect.equal
                  p.HostCalls
                  [ { Channel = "AiTool"
                      Name = "summarise" }
                    { Channel = "Call"
                      Name = "/api/orders" }
                    { Channel = "Invoke"
                      Name = "cap.print" }
                    { Channel = "Notify"; Name = "audit" } ]
                  "one demand per arm, sorted by channel then name"

              Expect.isEmpty p.Effects "none of them is a client effect"
          }

          test "a Call's own result target demands nothing — the handler declares where results land" {
              // DECISIONS.md D9 retired the tree-declared result target, and the
              // shared fold now REFUSES a call that carries one. So the target
              // can only ride on a call that reaches no host at all, and
              // projecting it would report a demand for a slot no host will ever
              // be asked to cover. The endpoint stays a demand: it is what a
              // coverage check is actually asking about.
              let intoState =
                  ofAction (Action.Call("/api/o", None, Some(CallResultTarget.State "orders.selected")))

              Expect.isEmpty intoState.StateNamespaces "a result target is not a write the TREE demands"

              Expect.equal intoState.HostCalls [ { Channel = "Call"; Name = "/api/o" } ] "only the endpoint is demanded"

              let intoQuery =
                  ofAction (Action.Call("/api/o", None, Some(CallResultTarget.Query "recent")))

              Expect.equal
                  intoQuery.HostCalls
                  [ { Channel = "Call"; Name = "/api/o" } ]
                  "and a query target names no query channel either"
          }

          test "a dispatch-time valueFrom READS its namespace without writing it" {
              // Phase 818's `valueFrom` is evaluated against the store when the
              // action fires, so what it reads is a demand of the running
              // program — unlike a display-slot read, which is the host reading
              // back what it supplied.
              let valueFrom: Binding<Fuaran.Core.JVal> = Binding.State("filters.region", None)

              let p = ofAction (Action.SetState("cart.total", None, Some valueFrom))

              Expect.equal
                  p.StateNamespaces
                  [ { Namespace = "cart"
                      Written = true
                      Read = false }
                    { Namespace = "filters"
                      Written = false
                      Read = true } ]
                  "cart is written, filters is read"
          }

          test "a key with no dot is its own namespace" {
              let p = ofAction (Action.SetState("msg", Some(jstr "x"), None))

              Expect.equal
                  p.StateNamespaces
                  [ { Namespace = "msg"
                      Written = true
                      Read = false } ]
                  "the whole key"
          }

          test "one namespace written AND read merges into a single entry" {
              let valueFrom: Binding<Fuaran.Core.JVal> = Binding.State("cart.source", None)

              let p = ofAction (Action.SetState("cart.total", None, Some valueFrom))

              Expect.equal
                  p.StateNamespaces
                  [ { Namespace = "cart"
                      Written = true
                      Read = true } ]
                  "both flags on one entry, never two rows for one namespace"
          }

          test "Form.OnSubmit and Modal.OnDismiss are walked, not just Button.OnClick" {
              let form =
                  Fuaran.form
                      "f"
                      { Defaults.form<obj> with
                          OnSubmit = Action.Navigate(TextSource.Literal "/thanks", NavigateTarget.Self) }

              let modal =
                  Fuaran.modal
                      "m"
                      { Defaults.modal<obj> with
                          OnDismiss = Some(Action.WriteToClipboard(TextSource.Literal "bye")) }

              let p = Demanded.ofTree (dash [ form; modal ])
              Expect.equal p.Effects [ "Navigate"; "WriteToClipboard" ] "all three wire-survivable slots are read"
          }

          test "the walk reaches a StateBehaviour branch, not only structural children" {
              // `OnEmpty` is a wire-encoded child node that renders in place of
              // the body. A demand parked there is as reachable as any other,
              // and a walk over structural children alone would miss it.
              let host = Fuaran.markdown "list" "body"

              let withEmpty =
                  { host with
                      State =
                          Some
                              { OnEmpty =
                                  Some(
                                      btn
                                          "empty-cta"
                                          (Action.Navigate(TextSource.Literal "/browse", NavigateTarget.Self))
                                  )
                                OnError = None
                                OnLoading = None } }

              let p = Demanded.ofTree (dash [ withEmpty ])
              Expect.equal p.Effects [ "Navigate" ] "the demand inside the empty-state branch is found"
          }

          test "a closure-held handler is reported as opaque, never silently dropped" {
              // A Select accepts events but resolves them through a closure the
              // wire cannot carry. On a decoded tree that closure is an inert
              // placeholder; on a hand-authored one it is not. Naming the node
              // is what lets a reader tell an exact projection from a lower
              // bound.
              let select =
                  Fuaran.select
                      "region"
                      { Defaults.select<obj> with
                          Label = TextSource.Literal "Region" }

              let p =
                  Demanded.ofTree (
                      dash
                          [ select
                            btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ]
                  )

              Expect.equal p.OpaqueHandlers [ "region" ] "the Select is named"
              Expect.equal p.Effects [ "Navigate" ] "the button's demand is still exact"
          }

          test "a Button is NOT opaque — its action is the wire-survivable slot" {
              let p = ofAction (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self))
              Expect.isEmpty p.OpaqueHandlers "a button resolves through the wire, not a closure"
          }

          // ── the projection as a document ──────────────────────────

          test "the projection is deterministic and self-describing" {
              let tree =
                  dash
                      [ btn "b1" (Action.Navigate(TextSource.Literal "/a", NavigateTarget.Self))
                        btn
                            "b2"
                            (Action.Chain
                                [ Action.WriteToClipboard(TextSource.Literal "x")
                                  Action.Navigate(TextSource.Literal "/a", NavigateTarget.Self) ]) ]

              let a = Demanded.ofTree tree
              let b = Demanded.ofTree tree
              Expect.equal a b "the same tree projects identically"
              Expect.equal (Demanded.encode a) (Demanded.encode b) "and encodes to the same bytes"

              let json = Demanded.encode a
              Expect.stringContains json "\"kind\":\"demanded\"" "carries its own kind"
              Expect.stringContains json (sprintf "\"version\":%d" Demanded.Version) "carries its own version"
              Expect.stringContains json "\"effects\":[\"Navigate\",\"WriteToClipboard\"]" "the effect set"

              // The server tier's key is present on EVERY document, `null` where
              // no server walk ran — which is why the version moved rather than
              // the key appearing only sometimes.
              Expect.stringContains json "\"server\":null" "no server walk ran, and the document says so"
              Expect.isNone a.Server "and the projection agrees"
          }

          test "the encoded document escapes hostile names rather than emitting raw JSON" {
              let p = ofAction (Action.Call("/api/\"x\"\n", None, None))
              let json = Demanded.encode p
              Expect.stringContains json "\\\"x\\\"" "quotes escaped"
              Expect.stringContains json "\\n" "newline escaped"
          }

          // ── what is refused ───────────────────────────────────────

          test "a tree demanding an unregistered effect is refused BY NAME" {
              let findings =
                  Demanded.check
                      HostCoverage.nothing
                      (dash [ btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ])

              Expect.equal findings [ CoverageFinding.UnregisteredEffect "Navigate" ] "one finding, naming the effect"

              Expect.stringContains
                  (Demanded.describe findings.Head)
                  "Navigate"
                  "and the description names it too — a finding a host cannot act on is not a finding"
          }

          test "registered-but-gate-refused is a DIFFERENT finding from unregistered" {
              // The same distinction the runtime denial DU draws: "this host has
              // no such capability" and "this host has it and refused this use
              // of it" have different remedies, and only the second is a policy
              // change.
              let gated =
                  HostCoverage.nothing
                  |> HostCoverage.withEffects [ "Navigate" ]
                  |> HostCoverage.withGate (fun _ -> false)

              let findings =
                  Demanded.check
                      gated
                      (dash [ btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ])

              Expect.equal findings [ CoverageFinding.GateRefusesEffect "Navigate" ] "gate refusal, not absence"
          }

          test "a permissive gate cannot reach an UNREGISTERED effect" {
              // The vocabulary is closed by registration, not by policy — so
              // the most permissive gate in the world still cannot cover an
              // effect nothing performs.
              let findings =
                  Demanded.check
                      (hostWith [ "WriteToClipboard" ])
                      (dash [ btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ])

              Expect.equal findings [ CoverageFinding.UnregisteredEffect "Navigate" ] "still absent"
          }

          test "a fully covered tree yields no findings" {
              let tree =
                  dash
                      [ btn
                            "b"
                            (Action.Chain
                                [ Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)
                                  Action.WriteToClipboard(TextSource.Literal "y") ]) ]

              Expect.isEmpty
                  (Demanded.check (hostWith [ "Navigate"; "WriteToClipboard" ]) tree)
                  "the host can serve everything this program can ask for"
          }

          test "every uncovered demand is reported, not just the first" {
              let tree =
                  dash
                      [ btn
                            "b"
                            (Action.Chain
                                [ Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)
                                  Action.WriteToClipboard(TextSource.Literal "y") ]) ]

              let findings = Demanded.check HostCoverage.nothing tree

              Expect.equal
                  findings
                  [ CoverageFinding.UnregisteredEffect "Navigate"
                    CoverageFinding.UnregisteredEffect "WriteToClipboard" ]
                  "a host correcting its registration wants the whole list"
          }

          test "an UNDECLARED host-call surface produces no host-call finding" {
              // The four host-call arms are documented no-ops on the bounded
              // path. Refusing a tree for naming one, against a host that never
              // claimed to broker them, would fire on most trees for a
              // capability nothing was going to perform.
              let findings =
                  Demanded.check (hostWith []) (dash [ btn "b" (Action.Call("/api/x", None, None)) ])

              Expect.isEmpty findings "silent until the host declares a surface"
          }

          test "a DECLARED host-call surface is checked" {
              let coverage = hostWith [] |> HostCoverage.withHostCalls [ "/api/allowed" ]

              let tree = dash [ btn "b" (Action.Call("/api/other", None, None)) ]

              Expect.equal
                  (Demanded.check coverage tree)
                  [ CoverageFinding.UncoveredHostCall("Call", "/api/other") ]
                  "outside the declared surface"

              Expect.isEmpty
                  (Demanded.check coverage (dash [ btn "b" (Action.Call("/api/allowed", None, None)) ]))
                  "inside it"
          }

          test "a DECLARED state-namespace set is checked; an undeclared one is not" {
              let tree = dash [ btn "b" (Action.SetState("cart.total", Some(jstr "1"), None)) ]

              Expect.isEmpty (Demanded.check (hostWith []) tree) "undeclared ⇒ unconstrained"

              let coverage = hostWith [] |> HostCoverage.withStateNamespaces [ "session" ]

              Expect.equal
                  (Demanded.check coverage tree)
                  [ CoverageFinding.UncoveredStateNamespace "cart" ]
                  "declared ⇒ checked"
          }

          test "checkProjection validates a document the host never held a tree for" {
              // The split that makes the projection portable: a document
              // emitted elsewhere is checkable here with no tree in sight.
              let projection =
                  Demanded.ofTree (dash [ btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ])

              Expect.equal
                  (Demanded.checkProjection HostCoverage.nothing projection)
                  [ CoverageFinding.UnregisteredEffect "Navigate" ]
                  "same verdict from the document alone"
          }

          // ── the server placement's strict construction path ───────

          test "initStrict refuses a session the host cannot cover; init still builds one" {
              let wire =
                  WireTree.ofDecoded (dash [ btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ])

              let services = BoundedServices.createPermissive stubRender

              match BoundedDriver.initStrict HostCoverage.nothing services empty wire with
              | Ok _ -> failtest "strict mode must refuse a tree demanding an unregistered effect"
              | Error findings ->
                  Expect.equal findings [ CoverageFinding.UnregisteredEffect "Navigate" ] "refused, naming the effect"

              // The DEFAULT posture is unchanged: construction succeeds and the
              // refusal happens where the effect is dispatched. Strict mode is
              // an opt-in on top, not a change of default.
              let session = BoundedDriver.init services empty wire
              Expect.equal session.BaseTree.Id "root" "the default path still builds the session"
          }

          test "initStrict admits a session the host CAN cover" {
              let wire =
                  WireTree.ofDecoded (dash [ btn "b" (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)) ])

              match
                  BoundedDriver.initStrict
                      (hostWith [ "Navigate" ])
                      (BoundedServices.createPermissive stubRender)
                      empty
                      wire
              with
              | Ok session -> Expect.equal session.BaseTree.Id "root" "admitted"
              | Error f -> failtestf "expected admission, got %A" f
          }

          // ── the probe: this check can go RED ──────────────────────

          test "PROBE — widening a demand past the host's coverage turns a passing check red" {
              // The falsifier for every green assertion above. One host, one
              // fixed coverage; the only thing that changes is the tree's
              // demand. If the checker were vacuous — returning the empty list
              // whatever it was handed — the first two expectations would still
              // pass and only this last one would catch it.
              let host = hostWith [ "WriteToClipboard" ]

              let within = dash [ btn "b" (Action.WriteToClipboard(TextSource.Literal "x")) ]
              Expect.isEmpty (Demanded.check host within) "the unwidened tree passes"

              let widened =
                  dash
                      [ btn
                            "b"
                            (Action.Chain
                                [ Action.WriteToClipboard(TextSource.Literal "x")
                                  Action.Navigate(TextSource.Literal "/escape", NavigateTarget.Self) ]) ]

              let findings = Demanded.check host widened

              Expect.equal
                  findings
                  [ CoverageFinding.UnregisteredEffect "Navigate" ]
                  "the widened demand is caught, and named"

              // And the same widening flips the strict construction path from
              // admitting to refusing — the check is wired, not merely present.
              let services = BoundedServices.createPermissive stubRender

              Expect.isTrue
                  (BoundedDriver.initStrict host services empty (WireTree.ofDecoded within)
                   |> Result.isOk)
                  "unwidened: admitted"

              Expect.isFalse
                  (BoundedDriver.initStrict host services empty (WireTree.ofDecoded widened)
                   |> Result.isOk)
                  "widened: refused"
          } ]
