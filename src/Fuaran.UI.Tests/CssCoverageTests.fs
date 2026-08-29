module Fuaran.UI.Tests.CssCoverageTests

#nowarn "3261" // DirectoryInfo.Parent is legitimately nullable here (the climb's terminator).

// ============================================================================
//  Phase 431 — CSS class-vocabulary <-> stylesheet coverage conformance.
//
//  Class-NAME parity between the F# and TypeScript renderers is corpus-locked,
//  and the wire format's forward-coupling rule makes a new kind update encoder,
//  decoder and corpus in one commit. The class -> CSS-*rule* axis had no such
//  enforcement: a class could be emitted with no matching rule and only a prose
//  note in HOST-STYLING-CHECKLIST caught it. Two families had already slipped
//  through that way, and one of them — `fuaran-field-range*`, the Range and
//  DateRange pair control — was a shipped control rendering as bare browser
//  inputs on every host serving the packaged sheet.
//
//  This module turns the prose contract into an executable one. It enumerates
//  every class the renderer can emit and fails when one has no rule in
//  `content/fuaran-reference.css`.
//
//  THE ENUMERATION HAS TWO SOURCES, because the vocabulary is built two ways.
//
//   (1) PROJECTED — the suffix families are BUILT by concatenation
//       (`"fuaran-tone-" + toneVar tone`), so no literal in the tree spells the
//       finished class. These are enumerated by RUNNING the real Theme.fs
//       projections over every case of every DU they range over, with the cases
//       obtained by reflection: a case added to `ToneVariant` or `Motion`
//       enters this enumeration with no edit here.
//
//   (2) SCANNED — the structural per-spec vocabulary (`fuaran-field-range`,
//       `fuaran-card-body`, …) is written as string literals at the emission
//       site. These are read out of the renderer sources themselves, so a class
//       added to a renderer enters the enumeration with no edit here either.
//       Scanning the SOURCE rather than a hand-transcribed list is the whole
//       point: a list would have to be remembered, and the gap this phase
//       exists to close is exactly what happens when it is not.
//
//  WHAT COUNTS AS COVERED is "the stylesheet carries a selector naming this
//  class", including inside a compound or descendant selector — that is how the
//  sheet legitimately styles most of the vocabulary.
//
//  A class the sheet deliberately leaves bare is DECLARED below with a reason,
//  and the declaration is falsifiable rather than a mute-list: a `CoveredBy`
//  entry names the class that carries the chrome instead and the suite asserts
//  THAT class has a rule, and every declared entry must still be emitted, so an
//  exemption outliving its class fails rather than accumulating.
//
//  SOURCE (2) IS A SHAPE HEURISTIC, and the second declared list exists because
//  of it. `classTokenShape` cannot tell a class name from any other
//  `fuaran-`-shaped literal in the same file — a window-level DOM event name, a
//  storage key, the suffix of a data attribute. Today the near misses dodge it
//  by naming convention alone (`"data-fuaran-provenance"` is spelled with the
//  `data-` prefix inside the literal, so the anchored shape rejects it), which
//  is luck rather than a rule: the same attribute written as two concatenated
//  fragments would be scanned as an emittable class and then reported as
//  unstyled. `declaredNonClassTokens` is where such a literal is named, with
//  the same falsifiability the absences carry — an entry the scan no longer
//  matches FAILS, so the list cannot outlive the literal it describes.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open FSharp.Reflection
open Expecto
open Fuaran.UI.Types
open Fuaran.UI
open Fuaran.UI.Renderer

// ─── Inputs ────────────────────────────────────────────────────────────────

/// The packaged reference stylesheet, copied into the test bin by the fsproj
/// (the `ThemeTests.fs` precedent).
let private referenceCssPath: string =
    Path.Combine(AppContext.BaseDirectory, "fuaran-reference.css")

/// The renderer sources, copied into the test bin by the fsproj under
/// `renderer-sources/{core,client,server}/`. Copied rather than resolved by
/// walking up to the repo root so the scan cannot silently read a different
/// checkout's sources than the ones this build compiled.
let private rendererSourceDir: string =
    Path.Combine(AppContext.BaseDirectory, "renderer-sources")

/// The TypeScript tier's byte-copy of the reference stylesheet. Present in a
/// workspace checkout, absent in a bare single-repo one (the `Build.fs`
/// wire-corpus precedent) — found by climbing rather than pinned, since its
/// depth above this repo is a checkout layout, not a contract.
let private tryFindTsCssCopy () : string option =
    let rec climb (dir: DirectoryInfo) =
        if isNull (box dir) then
            None
        else
            let candidate =
                Path.Combine(dir.FullName, "fuaran-ts", "packages", "renderer", "css", "fuaran.css")

            if File.Exists candidate then
                Some candidate
            else
                climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

// ─── The stylesheet side ───────────────────────────────────────────────────

/// Every class name the reference stylesheet names in a selector — compound
/// (`.fuaran-callout.fuaran-tone-success`) and descendant selectors included,
/// because that is how most of the vocabulary is legitimately styled.
///
/// COMMENTS ARE STRIPPED FIRST, and that is load-bearing rather than tidy:
/// `.fuaran-tone-brand` occurs in this file only inside a comment recording
/// that the outer-scoped tone shape was RETIRED. A scan that counted it would
/// report a tone class as styled on the strength of a note saying it is not.
let private styledClasses () : Set<string> =
    let text = File.ReadAllText referenceCssPath
    let stripped = Regex.Replace(text, @"/\*[\s\S]*?\*/", "")

    Regex.Matches(stripped, @"\.(fuaran-[a-zA-Z0-9_-]+)")
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Set.ofSeq

// ─── Source (1): the projected suffix families ─────────────────────────────

/// Every case of a DU whose cases are all nullary, by reflection. Asserts the
/// all-nullary property rather than filtering for it: silently skipping a case
/// that gained a payload would narrow the enumeration invisibly, which is the
/// failure mode this whole module exists to prevent.
let private nullaryCases<'T> () : 'T list =
    let cases = FSharpType.GetUnionCases typeof<'T>

    let payloaded =
        cases |> Array.filter (fun c -> c.GetFields().Length > 0) |> Array.map _.Name

    if payloaded.Length > 0 then
        failwithf
            "%s has non-nullary case(s) %s — the class enumeration reflects over its cases and can no longer construct them. Enumerate them explicitly here."
            typeof<'T>.Name
            (String.Join(", ", payloaded))

    cases
    |> Array.toList
    |> List.map (fun c -> FSharpValue.MakeUnion(c, [||]) :?> 'T)

/// The classes the Theme projections BUILD by concatenation, obtained by
/// running them rather than by restating their prefixes.
let private projectedClasses () : Set<string> =
    // `Theme.className` over the full cross-product of the SemanticStyle axes.
    // The record is built field-by-field rather than by copy-and-update on
    // `Defaults.style` deliberately: a new style axis then fails to compile
    // HERE, which is the forward coupling this module is for.
    let styleClasses =
        [ for tone in nullaryCases<ToneVariant> () do
              for weight in nullaryCases<StyleWeight> () do
                  for emphasis in nullaryCases<Emphasis> () do
                      for role in nullaryCases<StyleRole> () do
                          for voice in nullaryCases<FontVoice> () do
                              let style: SemanticStyle =
                                  { Defaults.style with
                                      Tone = tone
                                      Weight = weight
                                      Emphasis = emphasis
                                      Role = role
                                      Voice = voice }

                              yield! (Theme.className style).Split(' ') ]

    // The remaining families are composed at their emission site rather than in
    // Theme.fs, so the PREFIX is written here and only the suffix is derived.
    // The scan below sees each prefix as a bare trailing-dash fragment, which
    // is why it cannot supply these itself.
    let motionClasses =
        nullaryCases<Motion> ()
        |> List.map (fun m -> "fuaran-motion-" + Theme.motionVar m)

    let iconSizeClasses =
        nullaryCases<IconSize> ()
        |> List.map (fun s -> "fuaran-icon--" + Theme.iconSizeClass s)

    // Sentiment is sign(trend) x polarity, so both polarities over a positive,
    // negative and zero trend reach every sentiment the renderer can emit.
    let trendClasses =
        [ for polarity in nullaryCases<TrendPolarity> () do
              for trend in [ 1.0; -1.0; 0.0 ] do
                  "fuaran-metric-trend-" + fst (Theme.trendSentiment polarity trend) ]

    styleClasses @ motionClasses @ iconSizeClasses @ trendClasses
    |> List.filter (fun s -> s <> "")
    |> Set.ofList

// ─── Source (2): the structural vocabulary, scanned from the sources ───────

/// A finished class name: lower-case segments joined by single hyphens. The
/// shape deliberately rejects a trailing-hyphen fragment (`"fuaran-tone-"`, a
/// concatenation root whose completions come from source (1)) and anything
/// carrying a format specifier (`"fuaran-custom-%s-%s"`, an author-supplied
/// fragment that is not a fixed vocabulary member at all).
let private classTokenShape =
    Regex(@"^fuaran-[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled)

let private stringLiteral =
    Regex("\"((?:[^\"\\\\\r\n]|\\\\.)*)\"", RegexOptions.Compiled)

let private scannedClasses () : Set<string> =
    if not (Directory.Exists rendererSourceDir) then
        failwithf
            "Renderer sources absent at %s — the fsproj copies them into the test bin. A coverage scan with no sources to scan reports everything as covered."
            rendererSourceDir

    Directory.EnumerateFiles(rendererSourceDir, "*.fs", SearchOption.AllDirectories)
    |> Seq.collect (fun path ->
        // Line comments go first: they carry worked HTML examples naming
        // classes that no code path emits.
        let source = Regex.Replace(File.ReadAllText path, @"(?m)//.*$", "")

        stringLiteral.Matches source
        |> Seq.cast<Match>
        |> Seq.collect (fun m -> m.Groups[1].Value.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries))
        |> Seq.filter classTokenShape.IsMatch)
    |> Set.ofSeq

let private emittedClasses () : Set<string> =
    Set.union (projectedClasses ()) (scannedClasses ())

// ─── Phase 433 — the vocabulary fingerprint ────────────────────────────────
//
//  `Theme.vocabularyFingerprint` is a PINNED constant a served stylesheet is
//  stamped with, so an SSR host can refuse a sheet written against a different
//  class vocabulary. It cannot be computed inside the shipping library: half the
//  enumeration above is read out of the renderer SOURCES, which a package has no
//  access to at runtime. So the truth is computed HERE — over exactly the union
//  the coverage assertions run on, never a narrower restatement of it — and the
//  constant is checked against it. A vocabulary change therefore fails with the
//  value to paste, rather than leaving hosts asserting a fingerprint that no
//  longer describes what the renderer emits.

/// The digest scheme named by the `fv1:` tag on the constant: SHA-256 over the
/// class names sorted ORDINALLY and joined with `\n`, UTF-8, first 16 hex digits.
/// The sort is spelled out rather than left to `Set`'s ordering because the
/// bytes are a cross-tier contract — a stamped sheet is read by hosts that do
/// not share F#'s comparer.
let private fingerprintOf (classes: Set<string>) : string =
    let ordered =
        classes |> Set.toList |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    let bytes = Text.Encoding.UTF8.GetBytes(String.Join("\n", ordered))
    let digest = System.Security.Cryptography.SHA256.HashData bytes
    "fv1:" + Convert.ToHexString(digest).Substring(0, 16).ToLowerInvariant()

/// The fingerprint the packaged stylesheet is stamped with, read back out of the
/// header comment. `None` when the stamp is absent — which is a finding, not a
/// pass: an unstamped sheet is one no host can check.
let private stampedFingerprint () : string option =
    let m =
        Regex.Match(File.ReadAllText referenceCssPath, Regex.Escape Theme.vocabularyFingerprintMarker + @"\s*(\S+)")

    if m.Success then Some m.Groups[1].Value else None

// ─── Declared absences ─────────────────────────────────────────────────────

/// Why the reference stylesheet carries no rule for a class it can emit.
type private Absence =
    /// Another class in the same emission carries the chrome. The suite asserts
    /// the named class DOES have a rule, so the excuse is checkable rather than
    /// a mute-list entry that only has to be plausible.
    | CoveredBy of covering: string * note: string
    /// A deliberate bare hook: the reference sheet holds no opinion here and
    /// the class exists for consumer selectors and dev tooling.
    | BareHook of note: string

/// Whole prefix families the reference sheet leaves bare by contract. A family
/// entry rather than one entry per member because the reason is the same for
/// every member and always will be — these are the three axes documented as
/// consumer hooks in HOST-STYLING-CHECKLIST §2.0/§2.1.
let private unstyledFamilies: (string * string) list =
    [ "fuaran-kind-",
      "§2.1 per-NodeKind base hook. The kind class says WHAT a node is; the inner per-kind class \
       (`fuaran-layout-card`, `fuaran-callout`, `fuaran-metric`) carries the chrome. Styling the kind \
       class instead would put a rule on every node of that kind including ones a host has replaced."

      "fuaran-tone-",
      "§2.0 wrapper tone hook. Tone reaches the pixels through the per-component suffix classes \
       (`fuaran-callout-success`, `fuaran-metric-default`); the outer-scoped `.fuaran-tone-X .fuaran-Y` \
       shape was deliberately retired (see the comment above the per-tone Metric rules). A bare rule \
       here would tint every node carrying the tone, which is what that retirement was avoiding."

      "fuaran-weight-",
      "§2.0/§1.5 density hook, deliberately without a reference opinion. Every node wrapper carries one \
       of these three, so any rule here lands on every node in the tree at single-class specificity — \
       it would race the per-component rules by file order, and a `standard` rule restating a token's \
       default would defeat a consumer's `:root` override of it. Consumers safelist and bind these; \
       giving them reference rules is a design decision with estate-wide blast radius, not a \
       conformance fix."

      "fuaran-emphasis-",
      "§2.0/§1.5 prominence hook, deliberately without a reference opinion — same reasoning as \
       `fuaran-weight-` above, and the same three-on-every-node shape." ]

/// Individual classes the reference stylesheet leaves bare, each with the
/// reason. Ordered as the coverage failure prints them.
let private declaredAbsences: (string * Absence) list =
    [ "fuaran-callout-body", CoveredBy("fuaran-callout", "structural body wrapper; the callout carries the chrome")
      "fuaran-card-body", CoveredBy("fuaran-layout-card", "structural body wrapper; the card carries the chrome")
      "fuaran-modal-body", CoveredBy("fuaran-modal-dialog", "structural body wrapper; the dialog carries the chrome")
      "fuaran-stepper-body", CoveredBy("fuaran-stepper-step", "structural body wrapper; the steps carry the chrome")
      "fuaran-link-protected-wrap",
      CoveredBy("fuaran-link", "zero-paint wrapper around the anchor and its interstitial")
      "fuaran-link-protected", CoveredBy("fuaran-link", "modifier on the anchor; the link rule styles it")
      "fuaran-form-date", CoveredBy("fuaran-form-input", "per-kind modifier; emitted alongside the input class")
      "fuaran-file-upload-label", CoveredBy("fuaran-file-upload", "child of the styled upload wrapper")
      "fuaran-grid-cell-link", CoveredBy("fuaran-grid-cell", "anchor inside a styled cell")
      "fuaran-map-marker", CoveredBy("fuaran-map-marker-list", "list item; the list carries the layout")
      "fuaran-math", CoveredBy("fuaran-math-block", "grouping hook; the -block / -inline pair carries the chrome")
      "fuaran-progress", CoveredBy("fuaran-progress-bar", "grouping hook; bar / fill / label carry the chrome")
      "fuaran-sparkline-empty", CoveredBy("fuaran-sparkline", "empty-state modifier on the styled sparkline")
      "fuaran-split-pane-left", CoveredBy("fuaran-split-pane", "§3.1 — the base class styles both panes")
      "fuaran-split-pane-right", CoveredBy("fuaran-split-pane", "§3.1 — the base class styles both panes")
      "fuaran-list-ordered", CoveredBy("fuaran-list", "<ol> native numbering; `.fuaran-list` keeps list-style")
      "fuaran-list-unordered", CoveredBy("fuaran-list", "<ul> native markers; `.fuaran-list` keeps list-style")
      "fuaran-tabs-horizontal",
      CoveredBy("fuaran-tabs-vertical", "§3.1 — horizontal is the default axis; only the vertical one needs rules")
      "fuaran-custom-decode-error",
      CoveredBy("fuaran-kind-custom-placeholder", "refusal marker on an element the placeholder rule styles")

      "fuaran-form-checkbox",
      BareHook "native checkbox chrome, deliberately not restyled — the reference sheet restyles no native control"
      "fuaran-form-toggle", BareHook "native checkbox in switch role; same posture as the checkbox above"
      "fuaran-filter-checkbox", BareHook "the filter twin of the form checkbox; same posture"
      "fuaran-filter-toggle", BareHook "the filter twin of the form toggle; same posture"
      "fuaran-file-upload-input", BareHook "native file-input chrome, deliberately not restyled"
      "fuaran-file-upload-control", BareHook "the server renderer's file-input vocabulary; same native chrome"
      "fuaran-layout-separator", BareHook "the element is an <hr>; the reference sheet keeps the native rule"
      "fuaran-sparkline-line",
      BareHook "SVG geometry; stroke and fill ride presentation attributes, as the drawing hooks do"
      "fuaran-island", BareHook "hydration boundary, zero-paint by construction — a rule here would shift layout"
      "fuaran-mount-boundary",
      BareHook "isolation boundary marker; the guest's own subtree carries its scoped classes inside it"
      "fuaran-custom-wrapper",
      BareHook
          "wraps a HOST-registered custom renderer. Deliberately bare for the reason the custom-placeholder \
           comment gives: a host that registers a renderer must not inherit our visuals."
      "fuaran-custom-hash-mismatch",
      BareHook
          "strict-mode refusal marker on a wrapper that renders no body — there is nothing to paint, and the \
           fact is carried on the data attribute beside it" ]

/// Literals the SOURCE SCAN matches that are not CSS classes at all — the shape
/// heuristic's known misses, named rather than left to be mistaken for classes
/// the sheet forgot.
///
/// This is a different claim from a declared absence, which is why it is a
/// different list: an absence says "this class is emitted and the sheet leaves
/// it bare, here is what carries the chrome"; an entry here says "no element
/// ever carries this string in a `class` attribute, so asking the stylesheet
/// for a rule is the wrong question". Filing one as the other reads, to the
/// next person, as a styling gap nobody got round to.
///
/// FALSIFIABLE ON BOTH SIDES, and that is the whole reason it is a list of
/// pairs rather than a mute-set. The suite asserts every entry is still matched
/// by the scan — so a renamed or deleted literal fails here instead of leaving
/// a permanent exemption behind — and that the reference sheet does NOT style
/// it, since a rule for a token declared "never a class" means one of the two
/// is wrong.
///
/// These tokens stay INSIDE the fingerprinted enumeration deliberately. The
/// fingerprint is a pinned cross-tier constant over everything the sources and
/// projections yield; narrowing it to "only the real classes" would re-pin a
/// four-copy stamp in exchange for detecting strictly LESS renderer change,
/// which is the opposite of what it is for.
let private declaredNonClassTokens: (string * string) list =
    [ "fuaran-form-commit",
      "a window-level DOM EVENT name (the LocalBindings.fs OnSubmit flush), dispatched and listened for — \
       never written into a className" ]

let private absenceMap = Map.ofList declaredAbsences

let private nonClassTokens = declaredNonClassTokens |> List.map fst |> Set.ofList

let private familyReason (cls: string) : string option =
    unstyledFamilies
    |> List.tryPick (fun (prefix, note) ->
        if cls.StartsWith(prefix, StringComparison.Ordinal) then
            Some note
        else
            None)

let private isDeclared (cls: string) =
    (familyReason cls).IsSome
    || absenceMap.ContainsKey cls
    || nonClassTokens.Contains cls

// ─── Tests ─────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "CssCoverage"
        [
          // The probe before the verdict: a scan that matched nothing would
          // report perfect coverage, and a green result is exactly what it
          // would look like. Pin the floor and the two classes this phase was
          // filed over, so the enumeration cannot quietly stop enumerating.
          test "the enumeration actually enumerates" {
              let emitted = emittedClasses ()

              Expect.isGreaterThan
                  (Set.count emitted)
                  150
                  "the emitted-class enumeration collapsed — a scan matching nothing reports full coverage"

              Expect.isTrue
                  (emitted.Contains "fuaran-field-range")
                  "the scanned structural vocabulary is missing `fuaran-field-range` — source (2) is not reading the renderers"

              Expect.isTrue
                  (emitted.Contains "fuaran-weight-compact")
                  "the projected suffix families are missing `fuaran-weight-compact` — source (1) is not running the Theme projections"

              Expect.isGreaterThan
                  (Set.count (styledClasses ()))
                  150
                  "the reference-CSS selector scan collapsed — every class would then read as unstyled"
          }

          test "every emittable class has a reference-CSS rule or a declared absence" {
              let styled = styledClasses ()

              let uncovered =
                  emittedClasses ()
                  |> Set.filter (fun cls -> not (styled.Contains cls) && not (isDeclared cls))
                  |> Set.toList

              if not uncovered.IsEmpty then
                  failwithf
                      "%d emitted class(es) have no rule in fuaran-reference.css and no declared absence:\n  %s\n\nThree remedies, and which one applies is a question about the token, not about the sheet:\n  * it IS a class and should be styled -> add a rule to content/fuaran-reference.css;\n  * it IS a class the sheet deliberately leaves bare -> declare it in `declaredAbsences` in this file, naming either the class that carries the chrome instead (`CoveredBy`) or the reason there is no reference opinion (`BareHook`);\n  * it is NOT a class at all — a DOM event name, a storage key, a data-attribute fragment the source scan's shape heuristic cannot distinguish -> declare it in `declaredNonClassTokens` in this file with the role it really plays.\nA class emitted with no rule renders unstyled in every host serving the packaged sheet, and nothing else in the build says so."
                      uncovered.Length
                      (String.Join("\n  ", uncovered))
          }

          test "every declared absence names a class that is still emitted" {
              let emitted = emittedClasses ()

              let stale =
                  declaredAbsences
                  |> List.map fst
                  |> List.filter (fun cls -> not (emitted.Contains cls))

              Expect.isEmpty
                  stale
                  (sprintf
                      "declared absence(s) for class(es) the renderers no longer emit: %s — remove the entries. An exemption that outlives its class is how a mute-list starts."
                      (String.Join(", ", stale)))
          }

          test "every declared absence names a class the stylesheet does not already style" {
              let styled = styledClasses ()

              let redundant = declaredAbsences |> List.map fst |> List.filter styled.Contains

              Expect.isEmpty
                  redundant
                  (sprintf
                      "class(es) declared as deliberately unstyled that the stylesheet DOES style: %s — remove the entries, they now describe the opposite of what the sheet does."
                      (String.Join(", ", redundant)))
          }

          // The declared non-class tokens carry the same two falsifiers the
          // absences do, for the same reason: an exemption nothing checks is a
          // mute-list with a comment on it.
          test "every declared non-class token is still matched by the source scan" {
              let scanned = scannedClasses ()

              let stale =
                  declaredNonClassTokens
                  |> List.map fst
                  |> List.filter (fun tok -> not (scanned.Contains tok))

              Expect.isEmpty
                  stale
                  (sprintf
                      "declared non-class token(s) the renderer sources no longer yield: %s — remove the entries. The literal was renamed or deleted, and the declaration now exempts nothing while still reading as a live exception."
                      (String.Join(", ", stale)))
          }

          test "no declared non-class token is styled, or declared as an absence" {
              let styled = styledClasses ()

              let contradicted =
                  declaredNonClassTokens |> List.map fst |> List.filter styled.Contains

              Expect.isEmpty
                  contradicted
                  (sprintf
                      "token(s) declared as never-a-class that the reference stylesheet DOES style: %s — one of the two is wrong. Either the sheet carries a rule matching nothing, or the token really is emitted as a class and belongs in `declaredAbsences` (or simply covered)."
                      (String.Join(", ", contradicted)))

              let bothLists =
                  declaredNonClassTokens |> List.map fst |> List.filter absenceMap.ContainsKey

              Expect.isEmpty
                  bothLists
                  (sprintf
                      "token(s) declared BOTH as a bare class and as never-a-class: %s — the two lists make contradictory claims about the same literal. Keep the one that is true."
                      (String.Join(", ", bothLists)))
          }

          test "every CoveredBy excuse names a class that really is styled" {
              let styled = styledClasses ()

              let broken =
                  declaredAbsences
                  |> List.choose (fun (cls, absence) ->
                      match absence with
                      | CoveredBy(covering, _) when not (styled.Contains covering) -> Some(cls, covering)
                      | _ -> None)

              Expect.isEmpty
                  broken
                  (sprintf
                      "CoveredBy absence(s) whose covering class has no rule either: %s — the class is unstyled and so is its stated cover, which makes the exemption false rather than deliberate."
                      (String.Join(", ", broken |> List.map (fun (cls, covering) -> sprintf "%s -> %s" cls covering))))
          }

          // Phase 433. Two links in one place, because they fail for different
          // reasons and the remedy differs: the constant going stale means the
          // vocabulary moved and nobody re-pinned it, while the stamp going stale
          // means the constant moved and `-- Css` was not re-run.
          test "the pinned vocabulary fingerprint still describes the live vocabulary" {
              let live = fingerprintOf (emittedClasses ())

              Expect.equal
                  Theme.vocabularyFingerprint
                  live
                  (sprintf
                      "the emitted class vocabulary has changed, so `Theme.vocabularyFingerprint` no longer identifies it. Set it to `%s` in src/Fuaran.UI.Renderer.Core/Theme.fs, then run `dotnet run --project Build.fsproj -- Css` to restamp the stylesheet and its tier copies, in this change-set. A host asserting the old value would accept a sheet written against the old vocabulary."
                      live)
          }

          test "the reference stylesheet is stamped with the pinned fingerprint" {
              match stampedFingerprint () with
              | None ->
                  failwithf
                      "content/fuaran-reference.css carries no `%s` stamp — a served sheet with no fingerprint is one no host can check, which is the silent skew this phase closed. Run `dotnet run --project Build.fsproj -- Css`."
                      Theme.vocabularyFingerprintMarker
              | Some stamped ->
                  Expect.equal
                      stamped
                      Theme.vocabularyFingerprint
                      "the stylesheet's stamped fingerprint and `Theme.vocabularyFingerprint` disagree — a host would refuse the very sheet this renderer ships with. Run `dotnet run --project Build.fsproj -- Css` to restamp, and commit the tier copies with it."
          }

          // Phase 431 task 3. The TS tier ships a BYTE-COPY of this stylesheet,
          // so the cheapest way to extend the coverage floor across the tier is
          // to make the copy's identity executable: identical bytes means the
          // coverage proved above holds there unchanged. Skipped, loudly, in a
          // bare single-repo checkout where the sibling is not on disk.
          //
          // Phase 432 gave the copy a GENERATOR (`Build.fsproj -- Css`) and an
          // authoring-side drift gate (`-- CssCheck`, wired into `Check`), which
          // retired the hand-copy discipline this assertion originally stood in
          // for. It is KEPT rather than deleted because the two answer different
          // questions: the build target asks whether the four copies agree, while
          // this one carries the COVERAGE claim across the tier — the assertions
          // above prove the vocabulary is styled in the canonical sheet, and this
          // is what makes that proof transfer to the TypeScript one. Deleting it
          // would leave the coverage floor canonical-only, which is not what the
          // build target replaced.
          test "the TypeScript tier's stylesheet copy is byte-identical" {
              match tryFindTsCssCopy () with
              | None -> skiptest "fuaran-ts sibling not present in this checkout — byte-copy parity not checked here"
              | Some tsPath ->
                  let reference = File.ReadAllBytes referenceCssPath
                  let copy = File.ReadAllBytes tsPath

                  Expect.equal
                      (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData copy))
                      (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData reference))
                      (sprintf
                          "%s has drifted from the reference stylesheet, so the class coverage proved above does not carry to the TypeScript tier. The copy is generated: run `dotnet run --project Build.fsproj -- Css` and commit the result in the same change-set as the canonical edit."
                          tsPath)
          } ]
