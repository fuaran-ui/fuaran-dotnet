module Fuaran.UI.Renderer.Server.Tests.AdaptiveCardProjectionTests

// ============================================================================
//  Phase 1814 — the Adaptive Card projection, pinned.
//
//  Every card this file produces is validated against the PUBLISHED Adaptive
//  Cards 1.4 schema, vendored under `adaptive-card-schema/` with its licence,
//  by an off-the-shelf validator — and the validator is first shown to refuse a
//  card it should refuse, so a green run is evidence rather than silence.
//
//  Then: the per-kind ruling is held three ways (against the fidelity rows,
//  against the doc's mapping table, and against what every corpus node fixture
//  actually projects to); every degradation is named by node id; a tree over
//  the host limits is a typed refusal carrying the 1817 code; and every string
//  and URL reaches the card through the literal-text and destination-policy
//  seams.
// ============================================================================


open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Json.Schema
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops
open Fuaran.UI.Renderer.Server.AdaptiveCard

// ─── The published schema ───────────────────────────────────────────────────

let private schemaFile =
    Path.Combine(System.AppContext.BaseDirectory, "adaptive-card-schema", "adaptive-card-1.4.0.json")

/// Built lazily and forced inside test bodies: a failure here must be a failing
/// test, never an assembly that will not load.
let private schema = lazy (JsonSchema.FromText(File.ReadAllText schemaFile))

/// The schema's complaints about a JSON document, or `[]` when it validates.
let private schemaErrors (json: string) : string list =
    use doc = JsonDocument.Parse json

    let result =
        schema.Value.Evaluate(doc.RootElement, EvaluationOptions(OutputFormat = OutputFormat.List))

    if result.IsValid then
        []
    else
        // Deepest locations first: under `anyOf`, the shallow complaints are
        // every alternative the element is not, and the deep ones say why.
        [ for d in result.Details do
              if not (isNull d.Errors) then
                  for kv in d.Errors -> string d.InstanceLocation, kv.Value ]
        |> List.distinct
        |> List.sortByDescending (fun (location, _) -> location.Length)
        |> List.truncate 8
        |> List.map (fun (location, message) -> location + ": " + message)

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

let private column id (kids: Node<obj> list) =
    Fuaran.dashboard
        id
        { Defaults.dashboard with
            Children = kids }

let private heading id level text =
    bare
        id
        (NodeKind.Heading
            { Defaults.heading with
                Level = level
                Text = lit text })

let private fact id label value =
    bare
        id
        (NodeKind.Fact
            { Defaults.fact with
                Label = lit label
                Value = lit value })

let private field id label kind =
    { Defaults.formField<obj> with
        Id = id
        Label = lit label
        Kind = kind }

let private textValue (s: string) =
    Some(FieldValue.ofText (Binding.Static(Some s)))

let private dateField id label variant =
    field id label (FormFieldKind.DateTime(textValue "", None, variant, None, None, None))

let private button id label action =
    bare
        id
        (NodeKind.Button
            { Defaults.button<obj> with
                Label = lit label
                OnClick = action })

let private link id label href =
    bare
        id
        (NodeKind.Link
            { Defaults.link with
                Label = lit label
                Href = Binding.Static(Some href) })

let private image id alt src =
    bare
        id
        (NodeKind.Image
            { Defaults.image with
                Alt = lit alt
                Src = Binding.Static(Some src) })

/// A context whose destination policy declares one origin for every class.
let private allowing (host: string) (root: Node<obj>) =
    let policy =
        Sanitize.allowOrigin (Sanitize.EgressOrigin.ExactHost host) [] Sanitize.denyNonLocalEgress

    Render.mkContextWithEgress policy Registry.empty BindingResolver.empty root

let private unbounded =
    { defaults with
        Limits = HostLimits.unbounded }

let private projectOk (options: CardOptions) ctx root : JsonObject * Degradation list =
    match projectWith options ctx root with
    | Ok result -> result
    | Error refusal -> failtestf "expected a card, got a refusal: %s" (describeRefusal refusal)

let private bodyOf (card: JsonObject) : JsonObject list =
    [ for e in card["body"].AsArray() -> e.AsObject() ]

let private typeOf (e: JsonNode) = e["type"].GetValue<string>()

/// Every JSON object in the card, depth first, with its `type` when it has one.
let rec private allObjects (node: JsonNode) : JsonObject list =
    match node with
    | :? JsonObject as o ->
        o
        :: [ for kv in o do
                 if not (isNull kv.Value) then
                     yield! allObjects kv.Value ]
    | :? JsonArray as a ->
        [ for item in a do
              if not (isNull item) then
                  yield! allObjects item ]
    | _ -> []

let private typesIn (card: JsonObject) =
    allObjects card
    |> List.choose (fun o ->
        if o.ContainsKey "type" && not (isNull o["type"]) then
            Some(o["type"].GetValue<string>())
        else
            None)

let private rulesFor (id: string) (degradations: Degradation list) =
    degradations |> List.filter (fun d -> d.NodeId = id) |> List.map _.Rule

// ─── The display-and-input subtree (acceptance) ─────────────────────────────

let private sample =
    column
        "root"
        [ heading "title" 1 "Expense report"
          bare
              "intro"
              (NodeKind.Markdown
                  { Defaults.markdown with
                      Text =
                          lit
                              "Submitted by **Ada**, see [policy](https://cards.example/policy) and `code`.\n\n1. first\n2. second" })
          bare
              "facts"
              (NodeKind.SummaryList
                  { Defaults.summaryList with
                      Heading = Some(lit "Summary")
                      Children =
                          [ fact "who" "Owner" "Ada Lovelace"
                            fact "where" "Office" "London"
                            bare
                                "total"
                                (NodeKind.LabelValueRow
                                    { Defaults.labelValueRow with
                                        Label = lit "Total"
                                        Value = Binding.Static(Some 1234.5)
                                        Format = CellFormat.Number(Some 2, None) }) ] })
          image "receipt" "Receipt scan" "https://cards.example/receipt.png"
          link "more" "Open the ledger" "https://cards.example/ledger"
          Fuaran.box
              "row"
              { Defaults.box with
                  Layout = BoxLayout.Flex(Orientation.Horizontal, false, None)
                  Children = [ fact "a" "Status" "Pending"; fact "b" "Priority" "High" ] }
          bare
              "approve"
              (NodeKind.Form
                  { Defaults.form<obj> with
                      SubmitLabel = lit "Approve"
                      OnSubmit = Action.Invoke("expenses.approve", [])
                      Fields =
                          [ field "note" "Note" (FormFieldKind.TextArea(textValue "", None, 3))
                            field "amount" "Amount" (FormFieldKind.Number(textValue "12.5", None))
                            field "urgent" "Urgent" (FormFieldKind.Toggle(None, None))
                            field
                                "category"
                                "Category"
                                (FormFieldKind.Choice(
                                    Binding.Static(
                                        Some
                                            [ { Label = TextSource.Literal "Travel"
                                                Value = "travel" }
                                              { Label = TextSource.Literal "Meals"
                                                Value = "meals" } ]
                                    ),
                                    None,
                                    None
                                ))
                            dateField "on" "Date" DateTimeVariant.Date
                            dateField "at" "Time" DateTimeVariant.Time
                            dateField "when" "When" DateTimeVariant.DateTime ] })
          button "reject" "Reject" (Action.SetState("status", Some(Fuaran.Core.JStr "rejected"), None)) ]

[<Tests>]
let acceptance =
    testList
        "AdaptiveCard — a display-and-input subtree (Phase 1814 acceptance)"
        [ testCase "the validator refuses what it should, so a pass below is evidence" (fun () ->
              let table =
                  """{"type":"AdaptiveCard","version":"1.4","body":[{"type":"Table","columns":[],"rows":[]}]}"""

              let typo =
                  """{"type":"AdaptiveCard","version":"1.4","body":[{"type":"RichTextBlock","inline":[]}]}"""

              Expect.isNonEmpty (schemaErrors table) "a 1.5 Table must not validate against the 1.4 schema"
              Expect.isNonEmpty (schemaErrors typo) "a misspelled member must not validate"

              Expect.isEmpty
                  (schemaErrors """{"type":"AdaptiveCard","version":"1.4","body":[]}""")
                  "and the empty card does")

          testCase "the projected card validates against the published 1.4 schema" (fun () ->
              let card, _ = projectOk defaults (allowing "cards.example" sample) sample
              let json = toJson card
              Expect.isEmpty (schemaErrors json) ("the card must validate:\n" + json)
              Expect.equal (card["version"].GetValue<string>()) SchemaVersion "the card declares the pinned version")

          testCase "the mapping table holds: text, facts, image, link, inputs, actions" (fun () ->
              let card, _ = projectOk defaults (allowing "cards.example" sample) sample
              let types = typesIn card |> Set.ofList

              for expected in
                  [ "RichTextBlock"
                    "TextRun"
                    "FactSet"
                    "Image"
                    "ColumnSet"
                    "Container"
                    "Input.Text"
                    "Input.Number"
                    "Input.Toggle"
                    "Input.ChoiceSet"
                    "Input.Date"
                    "ActionSet"
                    "Action.Execute" ] do
                  Expect.contains types expected (expected + " is emitted")

              for banned in [ "TextBlock"; "Action.Submit"; "Input.Time"; "Table" ] do
                  Expect.isFalse (types.Contains banned) (banned + " is never written"))

          testCase "the three facts of the summary list share one FactSet, after its heading" (fun () ->
              let card, _ = projectOk defaults (allowing "cards.example" sample) sample

              let factSets = allObjects card |> List.filter (fun o -> o.ContainsKey "facts")

              let sizes = factSets |> List.map (fun o -> o["facts"].AsArray().Count)
              Expect.contains sizes 3 "Owner, Office and Total are one FactSet"

              let total =
                  allObjects card
                  |> List.tryFind (fun o -> o.ContainsKey "title" && o["title"].GetValue<string>() = "Total")

              Expect.equal
                  (total |> Option.map (fun o -> o["value"].GetValue<string>()))
                  (Some "1234.50")
                  "the value is formatted through the shared projection")

          testCase "every degradation names its node and rule" (fun () ->
              let _, degradations = projectOk defaults (allowing "cards.example" sample) sample

              Expect.contains
                  (rulesFor "approve" degradations)
                  (DegradationRule.ActionAsExecute("Invoke", "expenses.approve"))
                  "the form submits through Action.Execute with the capability as its verb"

              Expect.contains
                  (rulesFor "approve" degradations)
                  (DegradationRule.InputSubstituted("at", "Input.Text (HH:MM)"))
                  "a time field is a patterned text input, declared"

              Expect.contains
                  (rulesFor "approve" degradations)
                  (DegradationRule.InputSubstituted("when", "Input.Date + Input.Text (HH:MM)"))
                  "a date-time field is a date + time pair, declared"

              Expect.contains
                  (rulesFor "reject" degradations)
                  (DegradationRule.ActionAsExecute("SetState", "fuaran.setState"))
                  "a state write is a round trip with a declared verb")

          testCase "the same tree, sources and options give the same bytes" (fun () ->
              let once () =
                  let card, d = projectOk defaults (allowing "cards.example" sample) sample
                  toJson card, d

              Expect.equal (once ()) (once ()) "the projection is deterministic") ]

// ─── Actions ────────────────────────────────────────────────────────────────

[<Tests>]
let actions =
    testList
        "AdaptiveCard — actions become OpenUrl, Execute or open-live"
        [ testCase "Navigate to an allowed absolute URL is Action.OpenUrl" (fun () ->
              let tree =
                  button "go" "Go" (Action.Navigate(lit "https://cards.example/next", NavigateTarget.Blank))

              let card, d = projectOk defaults (allowing "cards.example" tree) tree
              let action = (bodyOf card).Head["actions"].AsArray().[0]
              Expect.equal (typeOf action) "Action.OpenUrl" "an OpenUrl"
              Expect.equal (action["url"].GetValue<string>()) "https://cards.example/next" "at the allowed URL"
              Expect.isEmpty d "nothing degraded")

          testCase "a client-only action is open-live, never a dead button" (fun () ->
              let tree = button "print" "Print" Action.Print

              let card, d =
                  projectOk
                      { defaults with
                          LiveUrl = Some "https://app.example/report" }
                      (Render.mkContext BindingResolver.empty tree)
                      tree

              let action = (bodyOf card).Head["actions"].AsArray().[0]
              Expect.equal (typeOf action) "Action.OpenUrl" "linked to the live surface"

              Expect.equal
                  (action["url"].GetValue<string>())
                  "https://app.example/report#print"
                  "at the node's anchor on the live surface"

              Expect.equal (rulesFor "print" d) [ DegradationRule.ClientOnlyAction "Print" ] "and declared")

          testCase "with no live surface declared, open-live is a labelled note" (fun () ->
              let tree = button "copy" "Copy" (Action.WriteToClipboard(lit "x"))
              let card, _ = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              let element = (bodyOf card).Head
              Expect.equal (typeOf element) "RichTextBlock" "a note, not a link that goes nowhere"
              Expect.stringContains (toJson card) "available in the live view" "and it says so")

          testCase "a disabled button is a note, declared" (fun () ->
              let tree =
                  bare
                      "off"
                      (NodeKind.Button
                          { Defaults.button<obj> with
                              Label = lit "Send"
                              OnClick = Action.Invoke("send", [])
                              Disabled = Some(Binding.Static(Some true)) })

              let card, d = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              Expect.equal (typeOf (bodyOf card).Head) "RichTextBlock" "no action"
              Expect.equal (rulesFor "off" d) [ DegradationRule.Disabled ] "declared") ]

// ─── Sanitisation ───────────────────────────────────────────────────────────

[<Tests>]
let sanitisation =
    testList
        "AdaptiveCard — strings are literal, URLs pass the destination policy"
        [ testCase "a bound string cannot author a link in a Fact" (fun () ->
              let tree = fact "f" "Note" "[click](https://evil.example/?q=secret) **now**"
              let card, _ = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              let factObject = allObjects card |> List.find (fun o -> o.ContainsKey "value")
              let value = factObject["value"].GetValue<string>()

              Expect.equal
                  value
                  @"\[click\]\(https://evil.example/?q=secret\) \*\*now\*\*"
                  "every link- and emphasis-forming character is escaped"

              Expect.equal (escapeFactText "a_b") @"a\_b" "and the escape is the one function the projection calls")

          testCase "heading text is a literal TextRun, never markdown" (fun () ->
              let tree = heading "h" 2 "[x](https://evil.example)"
              let card, _ = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              let types = typesIn card
              Expect.isFalse (List.contains "TextBlock" types) "no TextBlock, whose text a host reads as markdown"
              Expect.isFalse (List.contains "Action.OpenUrl" types) "and no link appears")

          testCase "an undeclared origin is refused: the link is text and the image is its alt" (fun () ->
              let tree =
                  column
                      "c"
                      [ link "l" "Ledger" "https://evil.example/ledger"
                        image "i" "Chart of spend" "https://evil.example/pixel.png" ]

              let card, d = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              let json = toJson card
              Expect.isFalse (json.Contains "evil.example") ("no refused destination is written:\n" + json)
              Expect.stringContains json "Chart of spend" "the image degrades to its alt text"

              for id in [ "l"; "i" ] do
                  match rulesFor id d with
                  | [ DegradationRule.DestinationRefused(_, reason) ] ->
                      Expect.stringContains reason "evil.example" "the reason names the host, never the URL"
                      Expect.isFalse (reason.Contains "pixel.png") "the path never reaches the record"
                  | other -> failtestf "%s: expected one refusal, got %A" id other)

          testCase "a relative destination degrades: a card host resolves nothing relative" (fun () ->
              let tree = link "rel" "Settings" "/settings"
              let card, d = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              Expect.isFalse ((toJson card).Contains "/settings") "the relative URL is not written"
              Expect.equal (rulesFor "rel" d) [ DegradationRule.RelativeDestination "href" ] "declared")

          testCase "a markdown link to an undeclared origin keeps its text and loses its action" (fun () ->
              let tree =
                  bare
                      "md"
                      (NodeKind.Markdown
                          { Defaults.markdown with
                              Text = lit "see [here](https://evil.example/x)" })

              let card, d = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              Expect.isFalse ((toJson card).Contains "evil.example") "the destination is not written"
              Expect.stringContains (toJson card) "here" "the link text survives"
              Expect.equal (rulesFor "md" d |> List.map ruleId) [ "destination-refused" ] "declared") ]

// ─── Limits ─────────────────────────────────────────────────────────────────

[<Tests>]
let limits =
    testList
        "AdaptiveCard — host limits refuse before any card is produced"
        [ testCase "a tree over HostLimits.card is refused with the 1817 code" (fun () ->
              let tree =
                  column "big" [ for i in 1..200 -> fact ("f" + string i) "Row" (string i) ]

              match project (Render.mkContext BindingResolver.empty tree) tree with
              | Error(ProjectionRefusal.HostLimitsExceeded(limits, findings)) ->
                  Expect.equal limits "card" "the refusing declaration is named"

                  Expect.contains
                      (findings |> List.map fst)
                      (PreEmitValidate.hostLimitCode HostLimitKind.Nodes)
                      "the node budget refuses it, with its own code"

                  Expect.contains
                      (findings |> List.map fst)
                      (PreEmitValidate.hostLimitCode HostLimitKind.Children)
                      "and the children budget"
              | other -> failtestf "expected a host-limit refusal, got %A" other)

          testCase "the same tree projects when the host declares no limits" (fun () ->
              let tree =
                  column "big" [ for i in 1..200 -> fact ("f" + string i) "Row" (string i) ]

              let card, _ = projectOk unbounded (Render.mkContext BindingResolver.empty tree) tree
              Expect.isEmpty (schemaErrors (toJson card)) "and validates")

          testCase "the card's own bytes are held to MaxSerializedBytes, with the 1817 code" (fun () ->
              let tree = column "c" [ heading "h" 1 "Title"; fact "f" "Owner" "Ada" ]
              let treeBytes = HostLimits.serializedBytes tree

              let options =
                  { defaults with
                      Limits =
                          { HostLimits.unbounded with
                              Identity = "tight"
                              MaxSerializedBytes = Some treeBytes } }

              let cardBytes =
                  let card, _ = projectOk unbounded (Render.mkContext BindingResolver.empty tree) tree
                  System.Text.Encoding.UTF8.GetByteCount(toJson card)

              Expect.isGreaterThan cardBytes treeBytes "precondition: the card is larger than the tree it projects"

              match projectWith options (Render.mkContext BindingResolver.empty tree) tree with
              | Error(ProjectionRefusal.CardPayloadExceeded(limits, code, limit, measured)) ->
                  Expect.equal limits "tight" "the declaration is named"
                  Expect.equal code (PreEmitValidate.hostLimitCode HostLimitKind.SerializedBytes) "the 1817 code"
                  Expect.equal limit treeBytes "the declared limit"
                  Expect.equal measured cardBytes "the measured card"
              | other -> failtestf "expected a payload refusal, got %A" other) ]

// ─── Structure: nothing dropped silently ────────────────────────────────────

[<Tests>]
let structure =
    testList
        "AdaptiveCard — every node is projected or named"
        [ testCase "an unselected Switch case and a hidden node are reported, not dropped" (fun () ->
              let tree =
                  column
                      "c"
                      [ bare
                            "sw"
                            (NodeKind.Switch
                                { Defaults.switch<obj> with
                                    On = Binding.Static(Some "a")
                                    Cases =
                                        [ { Child = heading "ha" 2 "A"
                                            Match = Some "a"
                                            When = None }
                                          { Child = heading "hb" 2 "B"
                                            Match = Some "b"
                                            When = None } ]
                                    Default = heading "hd" 2 "D" })
                        { heading "gone" 2 "Hidden" with
                            Visible = Some(Binding.Static(Some false)) }
                        Fuaran.icon "logo" "star" ]

              let card, d = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              let json = toJson card
              Expect.stringContains json "\"A\"" "the selected case projects"
              Expect.isFalse (json.Contains "\"B\"") "the other case does not"
              Expect.equal (rulesFor "hb" d) [ DegradationRule.NotTaken ] "and is reported as not taken"
              Expect.equal (rulesFor "hd" d) [ DegradationRule.NotTaken ] "as is the default"
              Expect.equal (rulesFor "gone" d) [ DegradationRule.NotVisible ] "a hidden node is reported"
              Expect.equal (rulesFor "logo" d) [ DegradationRule.Omitted ] "an icon is reported")

          testCase "an open-live node reports its descendants as inside it" (fun () ->
              let tree =
                  Fuaran.tabs
                      "tabs"
                      { Defaults.tabs with
                          Children = [ heading "t1" 2 "One"; heading "t2" 2 "Two" ] }

              let _, d = projectOk defaults (Render.mkContext BindingResolver.empty tree) tree
              Expect.equal (rulesFor "tabs" d) [ DegradationRule.OpenLive ] "the tab set is open-live"

              for id in [ "t1"; "t2" ] do
                  Expect.equal (rulesFor id d) [ DegradationRule.InsideOpenLive ] (id + " is inside it")) ]

// ─── The ruling, held three ways ────────────────────────────────────────────

let private corpusRoots () : (string * Node<obj>) list =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some root ->
        Directory.GetFiles(Path.Combine(root, "nodes"), "*.json")
        |> Array.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        |> Array.choose (fun path ->
            match JsonDecode.decodeNodeObj (File.ReadAllText path) with
            | Ok node -> Some(Path.GetFileNameWithoutExtension path, node)
            | Error _ -> None)
        |> List.ofArray

/// The doc's mapping table: every `| `Kind` | `class` | … |` row of the section
/// headed "The mapping table", up to the next heading.
let private docRows () : (string * string) list =
    match Fuaran.Tests.CorpusRoot.tryRepoRoot () with
    | None -> skiptest "the repository root is not on the walk from the test binary"
    | Some root ->
        File.ReadAllLines(Path.Combine(root, "docs", "ADAPTIVE-CARD-PROJECTION.md"))
        |> Array.skipWhile (fun line -> line <> "## The mapping table")
        |> Array.skip 1
        |> Array.takeWhile (fun line -> not (line.StartsWith "#"))
        |> Array.choose (fun line ->
            let cells = line.Split('|') |> Array.map _.Trim()

            if cells.Length > 3 && cells.[1].StartsWith "`" && cells.[1].EndsWith "`" then
                Some(cells.[1].Trim('`'), cells.[2].Trim('`'))
            else
                None)
        |> List.ofArray

[<Tests>]
let ruling =
    testList
        "AdaptiveCard — the ruling, against the fidelity rows, the doc and the corpus"
        [ testCase "every fidelity row has a card ruling, so a new kind cannot ship without one" (fun () ->
              let ruled = RenderFidelity.cardRulings |> List.map fst |> Set.ofList

              let missing =
                  RenderFidelity.all
                  |> List.map _.Kind
                  |> List.filter (fun k -> not (ruled.Contains k))

              Expect.isEmpty missing "a kind with no Adaptive Card ruling in RenderFidelity.cardRulings")

          testCase "the doc's mapping table is the ruling, row for row" (fun () ->
              let expected =
                  RenderFidelity.cardRulings
                  |> List.map (fun (k, r) -> k, RenderFidelity.cardClassId r)

              Expect.equal
                  (docRows ())
                  expected
                  "docs/ADAPTIVE-CARD-PROJECTION.md's table must list every kind, in order, with its class")

          testCase "every corpus fixture projects to a card the published schema accepts" (fun () ->
              let failures =
                  [ for name, node in corpusRoots () do
                        let ctx =
                            Render.mkContextWithEgress
                                Sanitize.permissiveEgress
                                Registry.empty
                                BindingResolver.empty
                                node

                        match projectWith unbounded ctx node with
                        | Error refusal -> yield name + ": refused - " + describeRefusal refusal
                        | Ok(card, _) ->
                            match schemaErrors (toJson card) with
                            | [] -> ()
                            | errors -> yield name + ": " + String.concat " | " errors ]

              Expect.isEmpty
                  failures
                  ("every projected card validates against adaptive-card-1.4.0.json:\n"
                   + String.concat "\n" failures))

          testCase "each root emits only the elements its ruling declares, and degrades as ruled" (fun () ->
              let failures =
                  [ for name, node in corpusRoots () do
                        let kind = RenderFidelity.wireNameOf node.Kind
                        let ctx = Render.mkContext BindingResolver.empty node

                        match RenderFidelity.cardOf kind, projectWith unbounded ctx node with
                        | None, _ -> yield name + ": kind " + kind + " has no card ruling"
                        | _, Error refusal -> yield name + ": refused - " + describeRefusal refusal
                        | Some ruling, Ok(card, degradations) ->
                            let allowed = RenderFidelity.cardElements ruling |> Set.ofList
                            let visible = BindingResolver.isNodeVisible ctx.Sources node

                            for e in bodyOf card do
                                // A Switch / ErrorBoundary / FragmentRef root is
                                // its chosen child, which carries its own ruling.
                                if not allowed.IsEmpty && not (allowed.Contains(typeOf e)) then
                                    yield sprintf "%s: %s ruled %A emitted %s" name kind allowed (typeOf e)

                            let own = rulesFor node.Id degradations |> List.map ruleId

                            match ruling with
                            | RenderFidelity.CardRuling.OpenLive _ when visible ->
                                if own.IsEmpty then
                                    yield name + ": an open-live root is not in the degradation list"
                            | RenderFidelity.CardRuling.Omitted _ when visible ->
                                if not (List.contains "omitted" own) then
                                    yield name + ": an omitted root is not reported omitted"
                            | RenderFidelity.CardRuling.FlattenToText _ when visible ->
                                if own.IsEmpty then
                                    yield name + ": a flattened root is not in the degradation list"
                            | _ -> () ]

              Expect.isEmpty
                  failures
                  ("the projection must do what render-fidelity.json says it does:\n"
                   + String.concat "\n" failures))

          testCase "the corpus exercises every ruled kind, so no ruling goes unchecked" (fun () ->
              let exercised =
                  corpusRoots ()
                  |> List.map (fun (_, n) -> RenderFidelity.wireNameOf n.Kind)
                  |> Set.ofList

              let unexercised =
                  RenderFidelity.cardRulings
                  |> List.map fst
                  |> List.filter (fun k -> not (exercised.Contains k))

              Expect.isEmpty unexercised "a card ruling no corpus node-fixture root exercises") ]
