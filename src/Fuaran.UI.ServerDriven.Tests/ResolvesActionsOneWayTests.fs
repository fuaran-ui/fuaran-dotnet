module Fuaran.UI.ServerDriven.Tests.ResolvesActionsOneWayTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking
// (FS0044 at every mention); file-scoped for the reason `ValidationTests`
// gives: the mentions sit inside `testList` expressions.
#nowarn "44"

// ─── Phase 2071: the server-driven tier resolves actions one way ────────────
//
// One test per finding, each with the input the finding names, each red
// before the change and green after it:
//
//   * one JSON string writer — a `DomPatch` label carrying U+000C must reach
//     the client as `\u000c`, never as a raw control byte inside a JSON string;
//   * one event policy in `resolveAction`, applied to every kind;
//   * a Disclosure event with no `open` payload toggles;
//   * Tabs dispatches what the client dispatches (the paired test);
//   * one route normaliser for both navigation paths;
//   * one not-found fallback in `landedIndex`;
//   * `keyIntent` snaps.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.Navigation

type Msg =
    | Toggled of bool
    | TabChosen of int
    | TabTagged of string
    | StepChosen of int
    | NameFiltered of string
    | Bump

let private ev nodeId event payload : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = event
      Payload = Map.ofList payload
      LastSeq = 0 }

let private disclosure id isOpen : Node<Msg> =
    Fuaran.disclosure
        id
        { Defaults.disclosure<Msg> with
            Open = Binding.Static(Some isOpen)
            OnToggle = Some(fun b -> Action.Dispatch(Toggled b)) }

let private tabsWith onSelect onSelectTag tabTags : Node<Msg> =
    Fuaran.tabs
        "tab"
        { Defaults.tabs<Msg> with
            OnSelect = onSelect
            OnSelectTag = onSelectTag
            TabTags = tabTags }

let private bothTabs =
    tabsWith
        (Some(fun i -> Action.Dispatch(TabChosen i)))
        (Some(fun t -> Action.Dispatch(TabTagged t)))
        (Some [ "alpha"; "beta"; "gamma" ])

// ── the client's composite tab handler, transcribed ─────────────────────────
//
// The client renderer's `dispatchTabIndex` (`Fuaran.UI.Renderer/Render.fs`,
// the Tabs arm) is a closure inside a Feliz element, which the .NET runner
// cannot mount or fire, so its DISPATCH half is transcribed here, arm for arm:
// `OnSelect i` when present, then `OnSelectTag tags[i]` when both the handler
// and the tag overlay are present and the overlay has an entry at `i`. The
// write-back arms dispatch nothing and so contribute nothing to the list.
let private clientDispatches (spec: TabsSpec<Msg>) (i: int) : Action<Msg> list =
    [ match spec.OnSelect with
      | Some onSelect -> onSelect i
      | None -> ()
      match spec.OnSelectTag, spec.TabTags with
      | Some onTag, Some tags ->
          match List.tryItem i tags with
          | Some tag -> onTag tag
          | None -> ()
      | _ -> () ]

/// `Action` carries closures and so has no equality: compare what each
/// dispatched action DISPATCHES.
let private msgs (actions: Action<Msg> list) : Msg list =
    actions
    |> List.map (function
        | Action.Dispatch m -> m
        | other -> failwithf "not a dispatch: %A" other)

/// The server's resolution of the same click, flattened to the dispatched list.
let private serverDispatches (node: Node<Msg>) (i: int) : Action<Msg> list =
    match resolveAction node (ev node.Id "click" [ "index", LiveValue.Num(float i) ]) with
    | None -> []
    | Some(Action.Chain actions) -> actions
    | Some action -> [ action ]

let private specOf (node: Node<Msg>) =
    match node.Kind with
    | NodeKind.Tabs spec -> spec
    | other -> failwithf "not a tabs node: %A" other

// ── the pre-2071 DomPatch escaper, kept as the byte reference ───────────────
//
// For input with no control character other than \n, \r and \t, the one
// writer must produce exactly what DomPatch's own escaper produced, so every
// frame a shim has ever parsed is unchanged.
let private legacyDomPatchEscape (s: string) : string =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")

let private navButton id route : Node<Msg> =
    Fuaran.button
        id
        { Defaults.button<Msg> with
            OnClick = Action.Navigate(TextSource.Literal route, NavigateTarget.Self) }

let private homeTree: Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children = [ Fuaran.markdown "title" "Home"; navButton "go-blank" "   " ] }

let private stubRender (n: Node<Msg>) : string = $"<f id='{n.Id}'/>"

let private session () =
    init (DriverServices.createPermissive stubRender) (fun (_: Msg) (m: int) -> m + 1) (fun (_: int) -> homeTree) 0

/// A fallback that records whether it ran, standing in for `Driver.step`.
let private recordingFallback (ran: bool ref) =
    fun (s: LiveSession<int, Msg>) (_: LiveEvent) ->
        ran.Value <- true

        s,
        { Patches = []
          Effects = []
          Rejected = None }

[<Tests>]
let tests =
    testList
        "Phase 2071 — the server-driven tier resolves actions one way"
        [ // ── one JSON string writer ──
          test "a DomPatch label carrying U+000C is escaped, so the frame parses" {
              let encoded = DomPatch.encode (DomPatch.SetText("t", "page\u000cbreak"))

              Expect.equal
                  encoded
                  """{"kind":"SetText","nodeId":"t","text":"page\u000cbreak"}"""
                  "\\u000c, lower-case hex"

              Expect.isFalse (encoded |> Seq.exists (fun c -> c < ' ')) "no raw control character anywhere"

              let frame =
                  FrameWire.encodeJson
                      { Seq = 1
                        Patches = [ DomPatch.ReplaceFragment("n", "<p>\u0001\u001f</p>") ]
                        Effects = [] }

              use doc = System.Text.Json.JsonDocument.Parse(frame: string)

              let html =
                  doc.RootElement.GetProperty("patches").[0].GetProperty("html").GetString()

              Expect.equal html "<p>\u0001\u001f</p>" "the client's parse recovers the fragment exactly"
          }

          test "DomPatch and ClientEffect quote a string identically (one writer)" {
              let probe = "a\"b\\c\nd\re\tf\u0000g\u000bh\u001fi é 漢 \U0001F600"
              let patch = DomPatch.encode (DomPatch.SetText("t", probe))
              let effect = ClientEffect.encode (ClientEffect.WriteToClipboard probe)

              let quotedIn (prefix: string) (s: string) =
                  s.Substring(prefix.Length, s.Length - prefix.Length - 1)

              Expect.equal
                  (quotedIn """{"kind":"SetText","nodeId":"t","text":""" patch)
                  (quotedIn """{"kind":"WriteToClipboard","text":""" effect)
                  "the same bytes for the same string"
          }

          test "the frame wire is byte-identical for input with no other control character" {
              for s in
                  [ ""
                    "plain"
                    "<div class=\"x\">a\\b</div>"
                    "line\nbreak\r\n\ttab"
                    "é 漢 \U0001F600 \u007f \u2028" ] do
                  Expect.equal
                      (DomPatch.encode (DomPatch.SetText("t", s)))
                      ("{\"kind\":\"SetText\",\"nodeId\":\"t\",\"text\":\""
                       + legacyDomPatchEscape s
                       + "\"}")
                      (sprintf "unchanged bytes for %A" s)
          }

          // ── one event policy in resolveAction ──
          test "resolveAction applies one event policy to every kind" {
              // An event outside a kind's action events resolves nothing, for
              // EVERY kind — not only for the Button and Select arms that used
              // to carry their own guard.
              let cases: (Node<Msg> * string) list =
                  [ disclosure "disc" false, "submit"
                    bothTabs, "submit"
                    Fuaran.stepper
                        "stp"
                        { Defaults.stepper<Msg> with
                            OnSelect = Some(fun i -> Action.Dispatch(StepChosen i)) },
                    "submit"
                    Fuaran.filters
                        "flt"
                        [ { Name = "name"
                            Label = TextSource.Literal "Name"
                            Kind =
                              FormFieldKind.Text(
                                  Some(FieldValue.ofText (Binding.Static(Some ""))),
                                  Some(FieldChange.ofText (fun v -> Action.Dispatch(NameFiltered v)))
                              ) } ],
                    "submit"
                    Fuaran.button
                        "btn"
                        { Defaults.button<Msg> with
                            OnClick = Action.Dispatch Bump },
                    "change" ]

              for node, event in cases do
                  let payload =
                      [ "index", LiveValue.Num 1.0
                        "open", LiveValue.Bool true
                        "name", LiveValue.Str "name"
                        "value", LiveValue.Str "beta" ]

                  Expect.isNone
                      (resolveAction node (ev node.Id event payload))
                      (sprintf "%s on %s resolves nothing" event node.Id)
          }

          test "a Form's bubbled field events stay unresolved; its submit resolves" {
              let form =
                  Fuaran.form
                      "frm"
                      { Defaults.form<Msg> with
                          OnSubmit = Action.Dispatch Bump }

              Expect.isNone (resolveAction form (ev "frm" "change" [])) "change is the form policy's"
              Expect.isNone (resolveAction form (ev "frm" "input" [])) "input is the form policy's"
              Expect.isSome (resolveAction form (ev "frm" "submit" [])) "submit resolves OnSubmit"
          }

          // ── a Disclosure with no payload toggles ──
          test "a payload-less click on an OPEN disclosure closes it" {
              match validate (fun _ -> true) (disclosure "disc" true) (ev "disc" "click" []) with
              | Ok { Action = Some(Action.Dispatch(Toggled false)) } -> ()
              | other -> failtestf "expected Toggled false, got %A" other

              match validate (fun _ -> true) (disclosure "disc" false) (ev "disc" "click" []) with
              | Ok { Action = Some(Action.Dispatch(Toggled true)) } -> ()
              | other -> failtestf "expected Toggled true from closed, got %A" other

              // An explicit payload still wins over the toggle.
              match
                  validate (fun _ -> true) (disclosure "disc" true) (ev "disc" "toggle" [ "open", LiveValue.Bool true ])
              with
              | Ok { Action = Some(Action.Dispatch(Toggled true)) } -> ()
              | other -> failtestf "expected the explicit open=true, got %A" other
          }

          // ── Tabs dispatches what the client dispatches (the paired test) ──
          test "a tab click with BOTH handlers dispatches both, as the client does" {
              match resolveAction bothTabs (ev "tab" "click" [ "index", LiveValue.Num 1.0 ]) with
              | Some(Action.Chain [ Action.Dispatch(TabChosen 1); Action.Dispatch(TabTagged "beta") ]) -> ()
              | other -> failtestf "expected Chain [TabChosen 1; TabTagged beta], got %A" other

              // A tag-addressed change reaches the same index, so the same pair.
              match resolveAction bothTabs (ev "tab" "change" [ "value", LiveValue.Str "gamma" ]) with
              | Some(Action.Chain [ Action.Dispatch(TabChosen 2); Action.Dispatch(TabTagged "gamma") ]) -> ()
              | other -> failtestf "expected Chain [TabChosen 2; TabTagged gamma], got %A" other
          }

          test "server and client agree on every handler combination and index" {
              let onSelect = Some(fun i -> Action.Dispatch(TabChosen i))
              let onTag = Some(fun t -> Action.Dispatch(TabTagged t))
              let tags = Some [ "alpha"; "beta" ]

              for s in [ onSelect; None ] do
                  for t in [ onTag; None ] do
                      for tt in [ tags; None ] do
                          let node = tabsWith s t tt

                          for i in [ 0; 1; 2 ] do
                              Expect.equal
                                  (msgs (serverDispatches node i))
                                  (msgs (clientDispatches (specOf node) i))
                                  (sprintf
                                      "OnSelect=%b OnSelectTag=%b TabTags=%b index=%d"
                                      s.IsSome
                                      t.IsSome
                                      tt.IsSome
                                      i)
          }

          test "a tag the overlay does not declare dispatches no tag handler" {
              // The client only ever dispatches a tag it read off `TabTags`, so a
              // client-chosen string is not one the server hands to `OnSelectTag`.
              let tagOnly =
                  tabsWith None (Some(fun t -> Action.Dispatch(TabTagged t))) (Some [ "alpha" ])

              Expect.isNone
                  (resolveAction tagOnly (ev "tab" "change" [ "value", LiveValue.Str "forged" ]))
                  "no dispatch"
          }

          // ── one route normaliser for both navigation paths ──
          test "a blank route is declined on the popstate path as on the action path" {
              let resolver: RouteResolver<Msg> =
                  fun _ -> failtest "a blank route must not reach the route table"

              let ranAction = ref false

              let _, actionOut =
                  stepWithRouting resolver (recordingFallback ranAction) (session ()) (ev "go-blank" "click" [])

              Expect.isTrue ranAction.Value "the action path hands a blank route to the fallback"
              Expect.isEmpty actionOut.Effects "no navigation effect on the action path"

              for blank in [ ""; "   " ] do
                  let ranPop = ref false

                  let _, popOut =
                      stepWithRouting
                          resolver
                          (recordingFallback ranPop)
                          (session ())
                          (ev "" "popstate" [ "route", LiveValue.Str blank ])

                  Expect.isTrue ranPop.Value (sprintf "the popstate path hands %A to the fallback too" blank)
                  Expect.isEmpty popOut.Effects (sprintf "no reload to %A" blank)
          }

          // ── one not-found fallback in landedIndex ──
          test "a child absent from its found parent lands at the append position" {
              let parent =
                  Fuaran.dashboard
                      "dash"
                      { Defaults.dashboard<Msg> with
                          Children = [ Fuaran.markdown "a" "a"; Fuaran.markdown "b" "b" ] }

              // The inserted child is not in the post-apply tree (a later op in
              // the same batch removed it). Index 1 would land it BEFORE "b";
              // the append position is 2 — the same fallback a missing parent
              // gets, which is the list's length there too (0).
              match
                  Lowering.lower stubRender parent [ TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "gone" "x") ]
              with
              | [ DomPatch.InsertFragment("dash", 2, _) ] -> ()
              | other -> failtestf "expected InsertFragment at 2, got %A" other

              match Lowering.lower stubRender parent [ TreeOp.MoveNode(NodeId "gone", NodeId "dash") ] with
              | [ DomPatch.MoveNode("gone", "dash", 2) ] -> ()
              | other -> failtestf "expected MoveNode at 2, got %A" other

              match
                  Lowering.lower stubRender parent [ TreeOp.InsertChild(NodeId "nowhere", Fuaran.markdown "c" "c") ]
              with
              | [ DomPatch.InsertFragment("nowhere", 0, _) ] -> ()
              | other -> failtestf "expected InsertFragment at 0 under a missing parent, got %A" other
          }

          // ── keyIntent snaps ──
          test "an arrow key from an off-grid value commits a snapped one" {
              let keyIntent = Fuaran.UI.Renderer.RatingModel.keyIntent
              Expect.equal (keyIntent false 5 2.3 "ArrowRight") (Some 3.0) "2.3 → 3, not 3.3"
              Expect.equal (keyIntent false 5 2.3 "ArrowLeft") (Some 2.0) "2.3 → 2, not 1.3"
              Expect.equal (keyIntent false 5 2.5 "ArrowRight") (Some 3.0) "a midpoint steps to the next position up"
              Expect.equal (keyIntent false 5 2.5 "ArrowDown") (Some 2.0) "and to the next position down"
              Expect.equal (keyIntent true 5 2.3 "ArrowUp") (Some 2.5) "half steps: 2.3 → 2.5"
              Expect.equal (keyIntent true 5 2.3 "ArrowLeft") (Some 2.0) "half steps: 2.3 → 2.0"
              Expect.equal (keyIntent false 5 4.7 "ArrowRight") (Some 5.0) "the ceiling still holds"
              Expect.equal (keyIntent false 5 0.3 "ArrowLeft") (Some 0.0) "the floor still holds"
              // On-grid values move exactly one step, as before.
              Expect.equal (keyIntent false 5 3.0 "ArrowRight") (Some 4.0) "3 → 4"
              Expect.equal (keyIntent true 5 3.5 "ArrowLeft") (Some 3.0) "3.5 → 3"
          } ]
