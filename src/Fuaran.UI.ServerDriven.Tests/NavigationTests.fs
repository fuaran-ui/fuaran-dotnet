module Fuaran.UI.ServerDriven.Tests.NavigationTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

// ─── Phase 157 (Wave 18): server-driven navigation + routing ──────────────────
//
// Two modes, selected by the host route table (`RouteResolver`): a known route
// swaps the tree IN PLACE (diff + patch + history.pushState); an unknown route
// falls through to a FULL RELOAD (ClientEffect.Navigate — the browser loads the
// URL, the server SSRs it fresh). `popstate` swaps the tree back without pushing
// state. Routing stays host-owned; the language tier ships the mechanism only.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.Navigation

type Msg = Bump

type Model = int

let private update Bump (m: Model) : Model = m + 1

let private navButton id route =
    Fuaran.button
        id
        { Defaults.button<Msg> with
            OnClick = Action.Navigate route }

let private homeTree: Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.markdown "title" "Home"
                  navButton "go-about" "/about"
                  navButton "go-missing" "/missing"
                  Fuaran.button
                      "bump"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Bump } ] }

let private aboutTree: Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children = [ Fuaran.markdown "title" "About"; navButton "go-home" "/home" ] }

let private resolver: RouteResolver<Msg> =
    function
    | "/home" -> Some homeTree
    | "/about" -> Some aboutTree
    | _ -> None

let private view (_: Model) : Node<Msg> = homeTree

let private stubRender (n: Node<Msg>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private canon (n: Node<Msg>) : string = CanonicalJson.encodeNode n

let private clickEv nodeId : LiveEvent =
    { ConnId = "c"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private popstateEv route : LiveEvent =
    { ConnId = "c"
      NodeId = "root"
      Event = "popstate"
      Payload = Map.ofList [ "route", LiveValue.Str route ]
      LastSeq = 0 }

let private session () =
    init (DriverServices.createPermissive stubRender) update view 0

[<Tests>]
let tests =
    testList
        "Navigation + routing (Phase 157)"
        [ test "resolveNav swaps in place + pushes state for a known route" {
              match resolveNav stubRender resolver homeTree true "/about" with
              | InPlace(newTree, ops, patches, effects) ->
                  Expect.equal (canon newTree) (canon aboutTree) "swapped to the about tree"
                  Expect.isNonEmpty ops "the home→about diff produced ops"
                  Expect.isNonEmpty patches "the diff lowered to patches"
                  Expect.equal effects [ ClientEffect.PushState "/about" ] "URL synced via pushState"
              | FullReload _ -> failtest "expected an in-place swap for a known route"
          }

          test "resolveNav full-reloads an unknown route" {
              match resolveNav stubRender resolver homeTree true "/missing" with
              | FullReload route -> Expect.equal route "/missing" "unknown route → full reload"
              | InPlace _ -> failtest "expected a full reload for an unknown route"
          }

          test "resolveNav on a popstate swaps without pushing state" {
              match resolveNav stubRender resolver homeTree false "/about" with
              | InPlace(_, _, _, effects) -> Expect.isEmpty effects "no pushState on a back/forward"
              | FullReload _ -> failtest "expected an in-place swap"
          }

          test "firstPaintTree resolves a deep-linked route for SSR first paint" {
              Expect.equal
                  (firstPaintTree resolver "/about" |> Option.map canon)
                  (Some(canon aboutTree))
                  "a direct hit on /about resolves its tree for full SSR"

              Expect.isNone (firstPaintTree resolver "/missing") "an unknown deep link resolves to None (host 404)"
          }

          test "stepWithRouting swaps in place on a Navigate to a known route" {
              let noFallback _ _ =
                  failtest "fallback should not run for a nav event"

              let s2, out = stepWithRouting resolver noFallback (session ()) (clickEv "go-about")

              Expect.equal (canon s2.Tree) (canon aboutTree) "session tree swapped to /about"
              Expect.equal out.Effects [ ClientEffect.PushState "/about" ] "pushState emitted"
              Expect.isNonEmpty out.Patches "the swap produced patches"
          }

          test "stepWithRouting full-reloads a Navigate to an unknown route" {
              let noFallback _ _ =
                  failtest "fallback should not run for a nav event"

              let s2, out =
                  stepWithRouting resolver noFallback (session ()) (clickEv "go-missing")

              Expect.equal (canon s2.Tree) (canon homeTree) "session tree unchanged on a full reload"
              Expect.equal out.Effects [ ClientEffect.Navigate "/missing" ] "full-reload navigate emitted"
          }

          test "stepWithRouting swaps on a popstate without pushing state" {
              let noFallback _ _ =
                  failtest "fallback should not run for a popstate"

              let s2, out = stepWithRouting resolver noFallback (session ()) (popstateEv "/about")

              Expect.equal (canon s2.Tree) (canon aboutTree) "tree swapped to the popped route"
              Expect.isEmpty out.Effects "no pushState on a popstate"
          }

          test "stepWithRouting delegates a non-nav event to the fallback" {
              let mutable called = false

              let fallback s _ =
                  called <- true

                  s,
                  { Patches = []
                    Effects = []
                    Rejected = None }

              stepWithRouting resolver fallback (session ()) (clickEv "bump") |> ignore
              Expect.isTrue called "a non-nav (Dispatch) event flowed through the fallback step"
          }

          test "LiveConnection.EnableRouting drives navigation through the channel" {
              let channel = InMemoryChannel()
              let conn = LiveConnection("c", session (), channel)
              conn.EnableRouting resolver

              channel.Send(clickEv "go-about")

              Expect.equal (canon conn.Session.Tree) (canon aboutTree) "connection swapped to /about"

              match channel.Pushed with
              | [ frame ] ->
                  Expect.equal frame.Effects [ ClientEffect.PushState "/about" ] "pushState frame pushed"
                  Expect.isNonEmpty frame.Patches "in-place swap patches pushed"
              | other -> failtestf "expected one navigation frame, got %A" other
          }

          test "ClientEffect.PushState encodes to tagged camelCase JSON" {
              Expect.equal
                  (ClientEffect.encode (ClientEffect.PushState "/reports/2026"))
                  """{"kind":"PushState","route":"/reports/2026"}"""
                  "PushState wire shape"
          } ]
