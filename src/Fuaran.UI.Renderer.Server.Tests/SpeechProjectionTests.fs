module Fuaran.UI.Renderer.Server.Tests.SpeechProjectionTests

// ============================================================================
//  Phase 1813 — the speech projection, pinned.
//
//  Four questions, each asked in the projection's output rather than of its
//  source: does `speak` win over derivation; is every absent node reported by
//  id; can a bound string inject SSML; and is the script a function of the
//  tree and its sources alone (the fixed-clock context). Then the corpus leg:
//  every node fixture in the wire-format corpus is projected, and each root's
//  own utterances must carry the class its kind's `speech` ruling in
//  `render-fidelity.json` declares — with a coverage assertion that every
//  ruled kind is exercised, so a new kind cannot arrive with a ruling the
//  projection does not honour.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops
open Fuaran.UI.Renderer.Server.Speech

// ─── Builders ───────────────────────────────────────────────────────────────

let private lit s = TextSource.Literal s

let private bare (id: string) (kind: NodeKind<obj>) : Node<obj> =
    { Id = id
      Kind = kind
      Accessibility = Option.None
      Fallback = None
      ExtraAttributes = Option.None
      Tooltip = None
      Visible = None
      Motion = Option.None
      State = Option.None
      Style = Option.None }

let private withA11y (f: Accessibility -> Accessibility) (n: Node<obj>) : Node<obj> =
    { n with
        Accessibility = Some(f (n.Accessibility |> Option.defaultValue Defaults.Accessibility.empty)) }

let private speaking (words: string) =
    withA11y (fun a -> { a with Speak = Some(lit words) })

let private heading id level text =
    bare
        id
        (NodeKind.Heading
            { Defaults.heading with
                Level = level
                Text = lit text })

let private metric id label value trend =
    bare
        id
        (NodeKind.Metric
            { Defaults.metric with
                Label = lit label
                Value = Binding.Static(Some value)
                Format = CellFormat.Number(Some 0)
                Trend = trend |> Option.map (fun t -> Binding.Static(Some t))
                TrendFormat = Some(CellFormat.Percent(Some 1)) })

let private card id (title: string) (kids: Node<obj> list) =
    Fuaran.card
        id
        { Defaults.card with
            Heading = Some(lit title)
            Children = kids }

let private column id (kids: Node<obj> list) =
    Fuaran.dashboard
        id
        { Defaults.dashboard with
            Children = kids }

let private texts (script: SpeechScript) = script.Utterances |> List.map _.Text

let private omissionOf (id: string) (omissions: SpeechOmission list) =
    omissions |> List.tryFind (fun o -> o.NodeId = id)

// ─── The dashboard read by ear (acceptance) ─────────────────────────────────

let private dashboardTree =
    column
        "dash"
        [ heading "title" 1 "Quarterly results"
          card
              "kpis"
              "Headline figures"
              [ metric "revenue" "Revenue" 1250.0 (Some 0.052)
                metric "churn" "Churn" 3.0 None
                (metric "nps" "Net promoter score" 41.0 None
                 |> speaking "Net promoter score is 41, the highest this year") ]
          bare
              "note"
              (NodeKind.Fact
                  { Defaults.fact with
                      Label = lit "Region"
                      Value = lit "EMEA" })
          Fuaran.icon "logo" "star" ]

[<Tests>]
let acceptance =
    testList
        "Speech — a dashboard read by ear (Phase 1813 acceptance)"
        [ testCase "headings announce sections, metrics read label-value-trend, speak is said exactly" (fun () ->
              let script, _ = projectStatic dashboardTree

              Expect.equal
                  (toPlainText script)
                  ("Quarterly results\n"
                   + "Headline figures\n"
                   + "Revenue: 1250, trend 5.2%\n"
                   + "Churn: 3\n"
                   + "Net promoter score is 41, the highest this year\n"
                   + "Region: EMEA\n")
                  "the script a listener hears, in authored order"

              let first = script.Utterances.Head
              Expect.isTrue first.Emphasis "a heading is emphasised"
              Expect.equal first.PauseAfter Pause.Long "a heading is followed by a long pause")

          testCase "every node absent from the script is in the omission list with a reason" (fun () ->
              let script, omissions = projectStatic dashboardTree
              let saidIds = script.Utterances |> List.map _.NodeId |> Set.ofList

              // The containers speak through their children; the icon says nothing.
              Expect.equal
                  (omissionOf "logo" omissions |> Option.map _.Reason)
                  (Some OmissionReason.NothingSayable)
                  "the icon is reported, not dropped"

              for o in omissions do
                  Expect.isFalse (saidIds.Contains o.NodeId) (o.NodeId + " is both said and omitted")) ]

// ─── speak wins over derivation ─────────────────────────────────────────────

[<Tests>]
let speakWins =
    testList
        "Speech — accessibility.speak wins over derivation"
        [ testCase "a metric with speak says exactly that, and nothing derived" (fun () ->
              let script, _ =
                  projectStatic (metric "m" "Revenue" 10.0 (Some 0.1) |> speaking "Revenue is up")

              Expect.equal (texts script) [ "Revenue is up" ] "speak replaces the derived label-value-trend"
              Expect.equal script.Utterances.Head.Source UtteranceSource.Speak "the utterance is marked as authored")

          testCase "speak on an announced-only kind replaces the announcement" (fun () ->
              let form =
                  bare
                      "f"
                      (NodeKind.Form
                          { Defaults.form with
                              Fields = [ Defaults.formField; Defaults.formField ] })

              Expect.equal (texts (fst (projectStatic form))) [ "Form, 2 fields" ] "the announcement, derived"

              Expect.equal
                  (texts (fst (projectStatic (form |> speaking "A two-question survey"))))
                  [ "A two-question survey" ]
                  "speak wins")

          testCase "speak on a container replaces its heading but its children still speak" (fun () ->
              let tree =
                  card "c" "Figures" [ metric "m" "Churn" 3.0 None ]
                  |> speaking "This quarter's figures"

              Expect.equal (texts (fst (projectStatic tree))) [ "This quarter's figures"; "Churn: 3" ] "")

          testCase "speak on an omitted-by-ruling kind gives it words" (fun () ->
              let skeleton =
                  bare "s" (NodeKind.Skeleton Defaults.skeleton) |> speaking "Loading the report"

              let script, omissions = projectStatic skeleton
              Expect.equal (texts script) [ "Loading the report" ] "a skeleton with speak says it"
              Expect.isEmpty omissions "and is not reported absent")

          testCase "the corpus a11y-speak fixture says its speak line" (fun () ->
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some root ->
                  let json = File.ReadAllText(Path.Combine(root, "nodes", "a11y-speak.json"))

                  match JsonDecode.decodeNodeObj json with
                  | Error e -> failwithf "a11y-speak failed to decode: %A" e
                  | Ok node ->
                      let script, _ = projectStatic node

                      Expect.equal
                          (script.Utterances
                           |> List.filter (fun u -> u.NodeId = node.Id)
                           |> List.map _.Source)
                          [ UtteranceSource.Speak ]
                          "the fixture's root declares speak, so its one own utterance is the authored line") ]

// ─── Omissions are reported by node id ──────────────────────────────────────

[<Tests>]
let omissions =
    testList
        "Speech — omissions are reported by node id"
        [ testCase "a hidden subtree is excluded whole, every node named, the decider recorded" (fun () ->
              let hidden =
                  card "secret" "Internal" [ metric "a" "A" 1.0 None; metric "b" "B" 2.0 None ]
                  |> withA11y (fun a ->
                      { a with
                          Hidden = Some(Binding.Static(Some true)) })

              let script, omitted =
                  projectStatic (column "root" [ heading "h" 2 "Public"; hidden ])

              Expect.equal (texts script) [ "Public" ] "nothing inside the hidden card is said"

              Expect.equal
                  (omitted |> List.map (fun o -> o.NodeId, o.Reason, o.DecidedAt))
                  [ "secret", OmissionReason.Hidden, "secret"
                    "a", OmissionReason.Hidden, "secret"
                    "b", OmissionReason.Hidden, "secret" ]
                  "each node of the subtree, in authored order, pointing at the node that hid it")

          testCase
              "visible=false, a closed toast, a decorative image and an untaken branch are each reported"
              (fun () ->
                  let invisible =
                      { heading "gone" 3 "Gone" with
                          Visible = Some(Binding.Static(Some false)) }

                  let closed =
                      bare
                          "t"
                          (NodeKind.Toast
                              { Defaults.toast with
                                  Message = lit "Saved"
                                  Open = Binding.Static(Some false) })

                  let decorative = bare "img" (NodeKind.Image { Defaults.image with Alt = lit "" })

                  let sw =
                      bare
                          "sw"
                          (NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("mode", None)
                                  Cases =
                                      [ { Child = heading "a" 3 "Mode A"
                                          Match = Some "a"
                                          When = None }
                                        { Child = heading "b" 3 "Mode B"
                                          Match = Some "b"
                                          When = None } ]
                                  Default = heading "d" 3 "No mode" })

                  let sources =
                      { BindingResolver.empty with
                          State = Map.ofList [ "mode", box "b" ] }

                  let script, omitted =
                      projectWith sources (column "root" [ invisible; closed; decorative; sw ])

                  Expect.equal (texts script) [ "Mode B" ] "only the selected branch speaks"

                  Expect.equal
                      (omitted |> List.map (fun o -> o.NodeId, o.Reason))
                      [ "gone", OmissionReason.NotVisible
                        "t", OmissionReason.Closed
                        "img", OmissionReason.Decorative
                        "a", OmissionReason.NotTaken
                        "d", OmissionReason.NotTaken ]
                      "every absent node by id, with its reason")

          testCase "content inside an announced-only kind is reported, never read" (fun () ->
              let tabs =
                  bare
                      "tabs"
                      (NodeKind.Tabs
                          { Defaults.tabs with
                              Children = [ heading "p1" 3 "First panel"; heading "p2" 3 "Second panel" ] })

              let script, omitted = projectStatic tabs
              Expect.equal (texts script) [ "Tabbed section, 2 tabs" ] "announced as what it is"

              Expect.equal
                  (omitted |> List.map (fun o -> o.NodeId, o.Reason, o.DecidedAt))
                  [ "p1", OmissionReason.InsideAnnounced, "tabs"
                    "p2", OmissionReason.InsideAnnounced, "tabs" ]
                  "the panels are absent, and say why") ]

// ─── Order ──────────────────────────────────────────────────────────────────

[<Tests>]
let order =
    testList
        "Speech — authored order"
        [ testCase "liveRegion does not move a node out of authored order" (fun () ->
              let urgent =
                  heading "b" 3 "Second"
                  |> withA11y (fun a ->
                      { a with
                          LiveRegion = Some LiveRegionKind.Assertive })

              let script, _ =
                  projectStatic (column "root" [ heading "a" 3 "First"; urgent; heading "c" 3 "Third" ])

              Expect.equal (texts script) [ "First"; "Second"; "Third" ] "an assertive region is read where it stands") ]

// ─── Lowerings and SSML injection ───────────────────────────────────────────

/// A heading whose text is BOUND to a state value — the injection vector: the
/// author did not write this string, a data source did.
let private boundHeading (value: string) =
    let tree =
        bare
            "h"
            (NodeKind.Heading
                { Defaults.heading with
                    Text = TextSource.Bound(Binding.State("title", None)) })

    let sources =
        { BindingResolver.empty with
            State = Map.ofList [ "title", box value ] }

    projectWith sources tree

[<Tests>]
let lowerings =
    testList
        "Speech — lowerings (plain text, SSML) and injection"
        [ testCase "plain text is one utterance per line, even when the source text carries line breaks" (fun () ->
              let script, _ =
                  projectStatic (column "r" [ heading "a" 2 "Line one\nstill line one"; heading "b" 3 "Two" ])

              Expect.equal (toPlainText script) "Line one still line one\nTwo\n" "")

          testCase "SSML carries emphasis and pause strengths from the script" (fun () ->
              let script, _ =
                  projectStatic (column "r" [ heading "a" 2 "Title"; metric "m" "Churn" 3.0 None ])

              Expect.equal
                  (toSsml script)
                  ("<speak>\n"
                   + "<s><emphasis level=\"moderate\">Title</emphasis></s><break strength=\"strong\"/>\n"
                   + "<s>Churn: 3</s><break strength=\"medium\"/>\n"
                   + "</speak>\n")
                  "")

          testCase "a bound string cannot inject SSML markup" (fun () ->
              let payload =
                  "</s><audio src=\"https://x.test/a.mp3\"/><s>& it's <mark name=\"m\"/>"

              let script, _ = boundHeading payload
              let ssml = toSsml script

              Expect.isFalse (ssml.Contains "<audio") "no element opens from bound data"
              Expect.isFalse (ssml.Contains "<mark") "no element opens from bound data"
              Expect.stringContains ssml "&lt;/s&gt;&lt;audio" "the metacharacters are escaped, not stripped"

              // The escaped document is still well-formed, with exactly the one
              // sentence the projection wrote.
              let doc = System.Xml.Linq.XDocument.Parse ssml

              Expect.equal
                  (doc.Root.Elements()
                   |> Seq.filter (fun e -> e.Name.LocalName = "s")
                   |> Seq.length)
                  1
                  "")

          testCase "characters XML cannot carry are dropped, so the document stays parseable" (fun () ->
              // Built at run time: a lone surrogate in a string LITERAL does not
              // survive compilation (it arrives as U+FFFD), and the probe must
              // carry a real one.
              let payload = "bell\u0007 nul\u0000 lone" + string (char 0xD800) + " ok"
              let script, _ = boundHeading payload
              let ssml = toSsml script
              System.Xml.Linq.XDocument.Parse ssml |> ignore
              Expect.stringContains ssml "bell nul lone ok" "the text survives, minus the illegal characters")

          testCase "the escaper covers all five metacharacters" (fun () ->
              Expect.equal
                  (escapeSsml "<a href=\"x\">'&'</a>")
                  "&lt;a href=&quot;x&quot;&gt;&apos;&amp;&apos;&lt;/a&gt;"
                  "") ]

// ─── Determinism under the fixed-clock context ──────────────────────────────

[<Tests>]
let determinism =
    testList
        "Speech — deterministic under the fixed-clock render context"
        [ testCase "the same tree and sources give the same script, and `now` comes from the sources" (fun () ->
              let clockHeading =
                  bare
                      "when"
                      (NodeKind.Heading
                          { Defaults.heading with
                              Text = TextSource.Bound(Binding.Now((fun (o: obj) -> unbox<string> o), None)) })

              let tree = column "r" [ clockHeading; dashboardTree ]

              let at (now: string) =
                  let sources = { BindingResolver.empty with Now = now }
                  let script, omitted = projectWith sources tree
                  toSsml script, omitted

              let a = at "2026-01-02T03:04:05Z"
              let b = at "2026-01-02T03:04:05Z"
              Expect.equal a b "two projections under one fixed clock are identical, omissions included"
              Expect.stringContains (fst a) "2026-01-02T03:04:05Z" "the clock is the context's, not the wall's"

              Expect.notEqual
                  (fst (at "2027-06-01T00:00:00Z"))
                  (fst a)
                  "and a different fixed clock is heard, so the probe can go red") ]

// ─── The corpus leg: every kind, against its render-fidelity ruling ─────────

let private corpusRoots () : (string * Node<obj>) list =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some root ->
        Directory.GetFiles(Path.Combine(root, "nodes"), "*.json")
        |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> Array.choose (fun path ->
            match JsonDecode.decodeNodeObj (File.ReadAllText path) with
            | Ok node -> Some(Path.GetFileNameWithoutExtension path, node)
            | Error _ -> None)
        |> List.ofArray

/// Every node reachable from `root` (the structural walk, plus a resolved
/// fragment reference's body), each with the ids of its subtree.
let rec private reachable (fragments: Map<string, Node<obj>>) (n: Node<obj>) : Node<obj> list =
    let kids =
        match n.Kind with
        | NodeKind.FragmentRef spec ->
            match Map.tryFind spec.Name fragments with
            | Some body -> [ body ]
            | None -> []
        | _ -> StructuralQuery.children n

    n :: (kids |> List.collect (reachable fragments))

[<Tests>]
let corpus =
    testList
        "Speech — every corpus node fixture, against render-fidelity.json's speech column"
        [ testCase "each root's own utterances carry the class its kind's ruling declares" (fun () ->
              let failures =
                  [ for name, node in corpusRoots () do
                        let kind = RenderFidelity.wireNameOf node.Kind
                        let script, omitted = projectStatic node
                        let own = script.Utterances |> List.filter (fun u -> u.NodeId = node.Id)
                        let declaresSpeak = node.Accessibility |> Option.bind _.Speak |> Option.isSome

                        match RenderFidelity.speechOf kind with
                        | None -> yield name + ": kind " + kind + " has no speech ruling"
                        | Some ruling ->
                            let expected =
                                if declaresSpeak && not own.IsEmpty then
                                    Some UtteranceSource.Speak
                                else
                                    expectedSource ruling

                            let wrong = own |> List.filter (fun u -> Some u.Source <> expected)

                            if not wrong.IsEmpty then
                                yield
                                    sprintf
                                        "%s: %s ruled %s, but said %A"
                                        name
                                        kind
                                        (RenderFidelity.speechClassId ruling)
                                        (wrong |> List.map (fun u -> u.Source, u.Text))

                            match ruling with
                            | RenderFidelity.SpeechRuling.AnnouncedOnly _ when own.IsEmpty ->
                                yield name + ": an announced-only root announced nothing"
                            | RenderFidelity.SpeechRuling.Omitted _ when not declaresSpeak ->
                                if not (omitted |> List.exists (fun o -> o.NodeId = node.Id)) then
                                    yield name + ": an omitted-by-ruling root is missing from the omission list"
                            | _ -> () ]

              Expect.isEmpty failures "the projection must do what render-fidelity.json says it does")

          testCase "every node of every fixture is said, reported omitted, or speaks through a descendant" (fun () ->
              let failures =
                  [ for name, node in corpusRoots () do
                        let ctx = Render.mkContext BindingResolver.empty node
                        let script, omitted = project ctx node
                        let saidIds = script.Utterances |> List.map _.NodeId |> Set.ofList
                        let omittedIds = omitted |> List.map _.NodeId |> Set.ofList

                        let present (n: Node<obj>) =
                            saidIds.Contains n.Id
                            || (reachable ctx.Fragments n
                                |> List.tail
                                |> List.exists (fun d -> saidIds.Contains d.Id))

                        for n in reachable ctx.Fragments node do
                            if not (present n || omittedIds.Contains n.Id) then
                                yield
                                    sprintf
                                        "%s: node %s (%s) is neither said nor reported"
                                        name
                                        n.Id
                                        (RenderFidelity.wireNameOf n.Kind) ]

              Expect.isEmpty failures "nothing is dropped silently")

          testCase "the corpus exercises every ruled kind, so no ruling goes unchecked" (fun () ->
              let exercised =
                  corpusRoots ()
                  |> List.map (fun (_, n) -> RenderFidelity.wireNameOf n.Kind)
                  |> Set.ofList

              let unexercised =
                  RenderFidelity.speechRulings
                  |> List.map fst
                  |> List.filter (fun k -> not (exercised.Contains k))

              Expect.isEmpty
                  unexercised
                  "a speech ruling no corpus node-fixture root exercises - the conformance leg above cannot see it") ]
