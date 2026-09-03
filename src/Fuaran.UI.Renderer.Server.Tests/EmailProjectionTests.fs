module Fuaran.UI.Renderer.Server.Tests.EmailProjectionTests

// ============================================================================
//  The email-safe render projection's conformance corpus (Phase 441).
//
//  Same discipline as the Phase 142 SSR-parity corpus, aimed at a different
//  property. Parity asks "do the two renderers agree?"; this asks three things
//  a digest has to be able to promise:
//
//   1. **The scope line holds.** `Email.scope` declares a posture for every
//      canonical wire kind, and every kind the Phase 442 fidelity manifest
//      calls `Behavioural` — inert server-side, behaviour at hydration —
//      projects to an open-live link rather than a control. Both halves are
//      measured against `RenderFidelity`, not restated here, so a NEW
//      interactive kind fails the build instead of shipping a dead button to
//      an inbox.
//
//   2. **The bytes are pinned.** Golden files under `email-corpus/`, compared
//      byte-for-byte. Determinism is asserted separately (render twice, same
//      bytes) because a golden that matches proves equality with the file, not
//      with the next render.
//
//   3. **Nothing email-hostile is emitted.** `Email.lint` over every fixture,
//      expected empty — AND a planted-construct test that makes the lint go red
//      on demand. A scanner that has never failed is not evidence; the second
//      test is what makes the first one mean something.
//
//  Regenerate the goldens deliberately, never casually:
//      FUARAN_APPROVE_EMAIL_CORPUS=1 dotnet run --project <this test project>
//  Then READ the diff. A changed golden is a changed email.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

// ─── Corpus location ────────────────────────────────────────────────────────

/// Walk up from the test assembly to this test project's own directory. The
/// email corpus is IN-REPO on purpose: the shared `wire-format-fixtures/` corpus
/// is the cross-host WIRE oracle, and an email projection is a .NET-side render
/// target no other host implements. Putting these fixtures there would assert a
/// conformance obligation on hosts that have no such projection.
///
/// `None` when the walk does not find the project — the assembly was copied
/// somewhere else, or is being run from outside the repo checkout.
let private tryCorpusDir () : string option =
    let rec walk (dir: DirectoryInfo) =
        if isNull dir then
            None
        else
            let candidate =
                Path.Combine(
                    dir.FullName,
                    "src",
                    "Fuaran.UI.Renderer.Server.Tests",
                    "Fuaran.UI.Renderer.Server.Tests.fsproj"
                )

            if File.Exists candidate then
                Some(Path.Combine(dir.FullName, "src", "Fuaran.UI.Renderer.Server.Tests", "email-corpus"))
            else
                walk dir.Parent

    walk (DirectoryInfo(AppContext.BaseDirectory))

/// The corpus directory, or a SKIP for the one test that needs it. The third
/// instance of the degrade-to-skip family `aa6e72a` closed in
/// `ScalarSsrParityTests` and `ChainCorpusTests`, and the one that commit
/// named and left open.
///
/// It is the mildest of the three — the locator is called from inside a test
/// rather than bound at module scope, so the `failwith` errored ONE test
/// instead of taking the whole assembly down through a type initialiser. Fixed
/// on the same argument regardless: absence of the repo checkout is a statement
/// about where the assembly is running, not about whether the email projection
/// is correct, and a red test says the second thing. The two other locators
/// would have gone on reading as the only members of a family with three.
let private corpusDir () : string =
    match tryCorpusDir () with
    | Some d -> d
    | None ->
        skiptest
            "src/Fuaran.UI.Renderer.Server.Tests/ not found walking up from the test assembly — the byte-pinned email corpus is IN-REPO, so this test needs the repo checkout (skipped when the assembly runs from elsewhere)"

let private approving =
    Environment.GetEnvironmentVariable "FUARAN_APPROVE_EMAIL_CORPUS" = "1"

// ─── Node builders ──────────────────────────────────────────────────────────

let private lit s = TextSource.Literal s

/// Badge and Sparkline have no smart constructor in the authoring surface, so
/// they are built from the record directly — the same node either way.
let private bare (id: string) (kind: NodeKind<obj>) : Node<obj> =
    { Id = id
      Kind = kind
      Accessibility = Option.None
      ExtraAttributes = Option.None
      Tooltip = None
      Motion = Option.None
      State = Option.None
      Style = Option.None }

let private badge id label variant =
    bare
        id
        (NodeKind.Badge
            { Defaults.badge with
                Label = lit label
                Variant = variant })

let private sparkline id =
    bare id (NodeKind.Sparkline Defaults.sparkline)

/// A DataGrid in its CLIENT-LIBRARY form (`StaticRows = None`) — the shape that
/// has no server-side rows and therefore projects to an open-live link, as
/// distinct from `Fuaran.table`, which always produces the static form.
let private dynamicGrid id : Node<obj> =
    bare
        id
        (NodeKind.DataGrid
            { Source = Binding.Static Option.None
              RowKey = Option.None
              RowKeyField = Option.None
              SortStateKey = Option.None
              PageSize = Option.None
              PageStateKey = Option.None
              DefaultSort = Option.None
              EditStateKey = Option.None
              Reorderable = false
              TransferInKey = None
              TransferOutKey = None
              Columns = []
              OnRowClick = Option.None
              Editable = false
              StaticRows = Option.None
              KeepRowsTogether = false
              RepeatHeader = false })

let private metricTile id label value =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = lit label
            Value = Binding.Static(Some value)
            Format = CellFormat.Number(Some 0) }

// ─── Fixtures ───────────────────────────────────────────────────────────────

type private Fixture =
    { Name: string
      Options: Email.EmailOptions
      Node: Node<obj> }

/// Phase 1026 — every fixture below points at `example.invalid`, and the
/// ambient default refuses an undeclared origin. So the corpus's options
/// DECLARE it, which is deliberately the shape a real host writes rather than a
/// blanket `permissiveEgress`: the goldens then pin the ALLOWED path, and a
/// regression in the allowlist shows up as refusals across the corpus instead
/// of being masked by a policy that permits everything.
///
/// An empty class list means EVERY class (`allowOrigin`'s ergonomic reading),
/// which is what a digest needs — it carries `Link` hrefs and `Image` srcs both.
let private declaredEgress =
    Sanitize.denyNonLocalEgress
    |> Sanitize.allowOrigin (Sanitize.HostSuffix "example.invalid") []

let private baseOpts =
    { Email.defaults with
        EgressPolicy = declaredEgress }

let private liveOpts =
    { baseOpts with
        LiveUrl = Some "https://example.invalid/report" }

/// The KPI row a digest opens with — three metrics across, which is precisely
/// the layout flex and grid cannot express in an inbox and a table can.
let private kpiRow: Node<obj> =
    Fuaran.gridLayout
        "kpis"
        { Defaults.gridLayout<obj> with
            Cols = 3
            Children =
                [ metricTile "kpi-rev" "Revenue" 128400.0
                  metricTile "kpi-users" "Active users" 3271.0
                  metricTile "kpi-churn" "Churn" 42.0 ] }

let private briefing: Node<obj> =
    Fuaran.card
        "brief"
        { Defaults.card<obj> with
            Heading = Some(lit "Monday briefing")
            Children =
                [ Fuaran.heading
                      "brief-h"
                      { Defaults.heading with
                          Level = 2
                          Text = lit "This week" }
                  Fuaran.markdown "brief-md" "Revenue is **up**, churn is flat.\n\n- [x] shipped\n- [ ] pending"
                  Fuaran.table
                      "brief-tbl"
                      { Defaults.table<obj> with
                          Headers = [ lit "Region"; lit "Revenue" ]
                          Rows = [ [ lit "EMEA"; lit "84,100" ]; [ lit "AMER"; lit "44,300" ] ] }
                  Fuaran.link "brief-link" "https://example.invalid/report" "Open the full report" ] }

let private displaySubset: Node<obj> =
    Fuaran.stack
        "disp"
        { Defaults.stack<obj> with
            Children =
                [ badge "d-badge" "On track" BadgeVariant.Success
                  Fuaran.callout
                      "d-callout"
                      { Defaults.callout with
                          Heading = Some(lit "Heads up")
                          Body = lit "Two regions are pending sign-off."
                          Tone = ToneVariant.Warning }
                  Fuaran.factSpec
                      "d-fact"
                      { Defaults.fact with
                          Label = lit "Owner"
                          Value = lit "Analytics"
                          Help = Some(lit "rotates quarterly") }
                  Fuaran.labelValueRow
                      "d-lvr"
                      { Defaults.labelValueRow with
                          Label = lit "Gross margin"
                          Value = Binding.Static(Some 0.62)
                          Format = CellFormat.Percent(Some 1)
                          Emphasis = true }
                  Fuaran.listSpec
                      "d-list"
                      { Defaults.list with
                          Items = [ lit "First"; lit "Second" ]
                          Ordered = true }
                  Fuaran.progress
                      "d-prog"
                      { Defaults.progress with
                          Fraction = Binding.Static(Some 0.45)
                          Label = Some(lit "Sign-off") }
                  Fuaran.codeBlock "d-code" "fsharp" "let x = 1"
                  Fuaran.math "d-math" "x^2 + y^2"
                  Fuaran.imageSpec
                      "d-img"
                      { Defaults.image with
                          Src = Binding.Static(Some "https://example.invalid/logo.png")
                          Alt = lit "Logo" }
                  Fuaran.toast
                      "d-toast-open"
                      { Defaults.toast with
                          Message = lit "Report generated"
                          Open = Binding.Static(Some true) }
                  Fuaran.toast
                      "d-toast-closed"
                      { Defaults.toast with
                          Message = lit "This must not appear"
                          Open = Binding.Static(Some false) }
                  Fuaran.icon "d-icon" "bell-glyph"
                  Fuaran.skeleton "d-skel" 3 ] }

let private containers: Node<obj> =
    Fuaran.stack
        "cont"
        { Defaults.stack<obj> with
            Children =
                [ Fuaran.splitPanel
                      "c-split"
                      { Defaults.splitPanel<obj> with
                          Weight = 0.7
                          Children = [ metricTile "c-a" "Left" 1.0; metricTile "c-b" "Right" 2.0 ] }
                  Fuaran.divider "c-div"
                  Fuaran.disclosure
                      "c-disc"
                      { Defaults.disclosure<obj> with
                          Heading = lit "Method notes"
                          Open = Binding.Static(Some false)
                          Children = [ Fuaran.markdown "c-disc-body" "Collapsed in the app, expanded here." ] }
                  Fuaran.scrollArea
                      "c-scroll"
                      { Defaults.scrollArea<obj> with
                          Children = [ Fuaran.markdown "c-scroll-body" "No clipping in an inbox." ] }
                  Fuaran.summaryList
                      "c-sum"
                      { Defaults.summaryList<obj> with
                          Heading = Some(lit "Summary")
                          Children = [ Fuaran.markdown "c-sum-body" "One line." ] }
                  Fuaran.switch
                      "c-switch"
                      { Defaults.switch<obj> with
                          On = Binding.State("view", Option.None)
                          Cases =
                              [ { Match = "detail"
                                  Child = Fuaran.markdown "c-sw-d" "detail" } ]
                          Default = Fuaran.markdown "c-sw-def" "default branch" } ] }

/// Every kind that must project to an open-live link, in one tree. The
/// `no-url` variant renders the same tree with no live surface declared.
let private interactive: Node<obj> =
    Fuaran.stack
        "act"
        { Defaults.stack<obj> with
            Children =
                [ Fuaran.button
                      "a-btn"
                      { Defaults.button<obj> with
                          Label = lit "Approve"
                          Variant = ButtonVariant.Primary }
                  Fuaran.form "a-form" Defaults.form<obj>
                  Fuaran.select "a-select" Defaults.select<obj>
                  Fuaran.fileUpload "a-upload" Defaults.fileUpload<obj>
                  Fuaran.filters "a-filters" []
                  Fuaran.tabs "a-tabs" Defaults.tabs<obj>
                  Fuaran.stepper "a-stepper" Defaults.stepper<obj>
                  Fuaran.modal
                      "a-modal"
                      { Defaults.modal<obj> with
                          Heading = Some(lit "Confirm")
                          Open = Binding.Static(Some true) }
                  Fuaran.chart
                      "a-chart"
                      { Defaults.chart<obj> with
                          Kind = ChartKind.Bar
                          Title = Some(lit "Revenue by region") }
                  Fuaran.map "a-map" Defaults.map<obj>
                  sparkline "a-spark"
                  Fuaran.custom "a-custom" "m" "c" Map.empty Option.None []
                  dynamicGrid "a-grid" ] }

let private fixtures: Fixture list =
    [ { Name = "kpi-row"
        Options = baseOpts
        Node = kpiRow }
      { Name = "briefing"
        Options = liveOpts
        Node = briefing }
      { Name = "display-subset"
        Options = baseOpts
        Node = displaySubset }
      { Name = "containers"
        Options = baseOpts
        Node = containers }
      { Name = "open-live"
        Options = liveOpts
        Node = interactive }
      { Name = "open-live-no-url"
        Options = baseOpts
        Node = interactive } ]

let private renderFixture (f: Fixture) : string =
    Email.renderWith f.Options BindingResolver.empty f.Node

// ─── Assertions ─────────────────────────────────────────────────────────────

let private controlTags = [ "<form"; "<button"; "<input"; "<select"; "<textarea" ]

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)

[<Tests>]
let emailProjectionTests =
    testList
        "email projection (Phase 441)"
        [ // ── The scope line, measured against the fidelity manifest ──────────
          test "scope declares exactly one posture per canonical wire kind" {
              let declared = Email.scope |> List.map fst
              let canonical = RenderFidelity.wireKindNames

              Expect.equal
                  (List.length declared)
                  (List.length (List.distinct declared))
                  "a wire kind is declared twice in Email.scope"

              Expect.equal
                  (List.sort declared)
                  (List.sort canonical)
                  "Email.scope and RenderFidelity.wireKindNames disagree — a new NodeKind needs an email posture in the same change-set"
          }

          test "scope is ordinal-sorted, so a new kind is one clean insert" {
              let declared = Email.scope |> List.map fst

              Expect.equal
                  declared
                  (declared |> List.sortWith (fun a b -> String.CompareOrdinal(a, b)))
                  "Email.scope must stay ordinal-sorted (same diffability posture as the fidelity table)"
          }

          // This is the load-bearing one: it is the "never a half-working
          // control" rule, derived rather than asserted.
          test "every Behavioural kind projects to an open-live link" {
              Expect.isNonEmpty
                  Email.interactiveWireKinds
                  "the fidelity manifest declares no Behavioural kind — the derivation has broken, and the check below is vacuous"

              for kind in Email.interactiveWireKinds do
                  match Email.dispositionOf kind with
                  | Some(Email.Disposition.OpenLive _) -> ()
                  | other ->
                      failtestf
                          "%s is Behavioural in the fidelity manifest (inert server-side, behaviour at hydration) but its email posture is %A — an interactive kind must project to an open-live link, never render as a control"
                          kind
                          other
          }

          // ── The byte-pinned golden corpus ───────────────────────────────────
          test "every fixture matches its byte-pinned golden" {
              let dir = corpusDir ()

              if approving then
                  Directory.CreateDirectory dir |> ignore

              for f in fixtures do
                  let path = Path.Combine(dir, f.Name + ".html")
                  let actual = renderFixture f

                  if approving then
                      File.WriteAllText(path, actual)
                  else
                      Expect.isTrue
                          (File.Exists path)
                          (sprintf
                              "missing golden %s — regenerate with FUARAN_APPROVE_EMAIL_CORPUS=1 and read the diff"
                              path)

                      Expect.equal
                          actual
                          (File.ReadAllText path)
                          (sprintf "%s: the email projection's bytes moved" f.Name)
          }

          test "the projection is deterministic — same tree, same bytes" {
              for f in fixtures do
                  Expect.equal
                      (renderFixture f)
                      (renderFixture f)
                      (sprintf "%s: two renders of one tree disagreed" f.Name)
          }

          // ── The email-hostile-construct lint ────────────────────────────────
          test "no fixture emits an email-hostile construct" {
              for f in fixtures do
                  match Email.lint (renderFixture f) with
                  | [] -> ()
                  | findings ->
                      failtestf
                          "%s emitted %d email-hostile construct(s): %s"
                          f.Name
                          (List.length findings)
                          (findings
                           |> List.map (fun x -> x.Code + " (" + x.Construct + ")")
                           |> String.concat ", ")
          }

          // Verify the probe, not just the verdict: a scanner that has never
          // gone red proves nothing about the clean runs above.
          test "the lint goes red on a planted construct" {
              let planted =
                  "<div style=\"display:flex;gap:8px\"><button onclick=\"go()\">x</button><svg></svg></div>"

              let codes = Email.lint planted |> List.map _.Code |> List.distinct |> List.sort

              Expect.equal
                  codes
                  [ "EMAIL-CONTROL"
                    "EMAIL-DIV-LAYOUT"
                    "EMAIL-EMBED"
                    "EMAIL-FLEX"
                    "EMAIL-GRID"
                    "EMAIL-SCRIPT" ]
                  "the lint missed a construct the client matrix is known to break on"

              Expect.isEmpty (Email.lint "") "an empty document has nothing to find"
          }

          test "an open-live projection emits no form control" {
              // Both variants: with a live surface declared and without. The
              // no-URL path is the easier one to get wrong, because the obvious
              // implementation emits a dangling anchor.
              for name in [ "open-live"; "open-live-no-url" ] do
                  let f = fixtures |> List.find (fun x -> x.Name = name)
                  let html = renderFixture f

                  for tag in controlTags do
                      Expect.isFalse
                          (contains tag html)
                          (sprintf
                              "%s: emitted %s — an interactive kind must degrade to a link, not a dead control"
                              name
                              tag)

                  Expect.isTrue
                      (contains "data-fuaran-email-open-live" html)
                      (sprintf "%s: no open-live affordance emitted at all — the kinds vanished silently" name)
          }

          test "a live surface produces anchors; its absence produces none" {
              let withUrl = renderFixture (fixtures |> List.find (fun x -> x.Name = "open-live"))

              let withoutUrl =
                  renderFixture (fixtures |> List.find (fun x -> x.Name = "open-live-no-url"))

              Expect.isTrue
                  (contains "https://example.invalid/report#a-btn" withUrl)
                  "a declared live surface must produce a real anchor per open-live node"

              Expect.isFalse
                  (contains "<a " withoutUrl)
                  "with no live surface declared the projection must NOT emit a dangling anchor"

              Expect.isTrue
                  (contains "available in the live view" withoutUrl)
                  "with no live surface the reader must still be told the content is not in the email"
          }

          test "a chart's declared title survives into its open-live label" {
              let html = renderFixture (fixtures |> List.find (fun x -> x.Name = "open-live"))

              Expect.isTrue
                  (contains "Revenue by region" html)
                  "the chart's title is the reader's only clue about what the link leads to"
          }

          // ── Narrower content locks ──────────────────────────────────────────
          test "markdown task lists carry a glyph, never a checkbox control" {
              let html = renderFixture (fixtures |> List.find (fun x -> x.Name = "briefing"))

              Expect.isFalse (contains "<input" html) "a disabled checkbox is still a form control"
              Expect.isTrue (contains "&#9745;" html) "the checked task item lost its ballot glyph"
              Expect.isTrue (contains "&#9744;" html) "the unchecked task item lost its ballot glyph"
          }

          test "a closed Toast is omitted, not merely hidden" {
              let html =
                  renderFixture (fixtures |> List.find (fun x -> x.Name = "display-subset"))

              Expect.isTrue (contains "Report generated" html) "the open toast should render as a static notice"

              Expect.isFalse
                  (contains "This must not appear" html)
                  "a closed toast must not reach the document at all — [hidden] is not honoured everywhere"
          }

          test "a Disclosure renders expanded, so no content is silently lost" {
              let html = renderFixture (fixtures |> List.find (fun x -> x.Name = "containers"))

              Expect.isTrue
                  (contains "Collapsed in the app, expanded here." html)
                  "a closed Disclosure's body must still render — <details> is inert in Outlook"
          }

          test "a static table renders as a real table; a client-library grid links out" {
              let table = renderFixture (fixtures |> List.find (fun x -> x.Name = "briefing"))
              let grid = renderFixture (fixtures |> List.find (fun x -> x.Name = "open-live"))

              Expect.isTrue (contains "<th" table) "the staticRows form must render real header cells"
              Expect.isTrue (contains "EMEA" table) "the staticRows form must render its rows"

              Expect.isTrue
                  (contains "data-fuaran-email-open-live=\"a-grid\"" grid)
                  "a grid with no server-side rows has nothing to lay out and must link out"
          }

          test "the whole document entry is complete and lints clean" {
              let doc =
                  Email.renderDocument liveOpts "Monday briefing" BindingResolver.empty briefing

              Expect.stringStarts doc "<!DOCTYPE html>" "a sendable email is a document, not a fragment"
              Expect.stringContains doc "<title>Monday briefing</title>" "the subject rides the title"
              Expect.stringEnds doc "</html>" "the document must be closed"
              Expect.isEmpty (Email.lint doc) "the document wrapper introduced an email-hostile construct"
          } ]
