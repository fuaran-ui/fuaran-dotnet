module Fuaran.UI.Tests.PrintCascadeTests

// ============================================================================
//  Phase 1124 — the print stylesheet's cascade contract against Phase 1473's
//  authored break control.
//
//  THE CLAIM THIS MODULE EXISTS TO MAKE FALSIFIABLE: an authored declaration
//  wins over a default. Phase 1473 admitted four wire members — `keepTogether`,
//  `breakBefore`, `keepRowsTogether`, `repeatHeader` — that a document opts
//  into, each projected as one class; Phase 1124 added a `@media print` block of
//  DEFAULTS that apply to documents declaring nothing. The two blocks live in
//  one stylesheet and reach overlapping properties, so "the author wins" is not
//  self-evident: it is a fact about source order and selector specificity that
//  a later edit could reverse in either block, with nothing red anywhere and no
//  symptom until someone prints.
//
//  It is asserted in three parts because there are three distinct ways to lose
//  it, and only the first is about ordering:
//
//   1. ORDER. At equal specificity CSS takes the LAST declaration, so the
//      defaults block must precede the break-control block. Reversing them is a
//      one-line move a tidying pass could make while the file still reads
//      perfectly.
//   2. NO CONTESTED PROPERTY. The strongest form of "the author wins" is that
//      there is nothing to win: the defaults block declares no `break-before`
//      at all, so `.fuaran-break-before-page` is uncontested however the file is
//      ordered. Order is then the backstop rather than the mechanism.
//   3. NO SUBSUMED MEMBER. A blanket `tr { break-inside: avoid }` default would
//      not CONFLICT with `keepRowsTogether` — it would make it VACUOUS, which is
//      worse, because a wire member whose declaration changes no rendering is a
//      fake affordance and nothing in the build would report it. This is the one
//      of the three that is not a cascade fact at all, and it is here because it
//      is the failure the same edit would produce.
//
//  The whole module reads the PACKAGED stylesheet — the artefact a host serves —
//  rather than a copy or a model of it, so the thing asserted is the thing that
//  ships.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

/// The packaged reference stylesheet, copied into the test bin by the fsproj
/// (the `CssCoverageTests` precedent).
let private referenceCssPath: string =
    Path.Combine(AppContext.BaseDirectory, "fuaran-reference.css")

let private css: string = File.ReadAllText referenceCssPath

/// Comments stripped, because every property name this module hunts for is also
/// discussed in the prose above the block that uses it — the defaults block's
/// own comment names `break-before` and `tr` precisely to explain why neither
/// appears in a rule. A scan that read comments would fail on the explanation.
let private cssNoComments: string = Regex.Replace(css, @"/\*[\s\S]*?\*/", "")

/// The two `@media print` blocks, as (startIndex, body) pairs in source order.
/// Balanced-brace scan rather than a regex: the bodies contain nested rule
/// blocks, so `\{[^}]*\}` would stop at the first inner close and silently
/// examine a fragment.
let private printBlocks () : (int * string) list =
    let marker = "@media print"

    let rec scan (from: int) (acc: (int * string) list) =
        let idx = cssNoComments.IndexOf(marker, from, StringComparison.Ordinal)

        if idx < 0 then
            List.rev acc
        else
            let openBrace = cssNoComments.IndexOf('{', idx)

            let rec close (i: int) (depth: int) =
                if i >= cssNoComments.Length then
                    i
                elif cssNoComments[i] = '{' then
                    close (i + 1) (depth + 1)
                elif cssNoComments[i] = '}' then
                    if depth = 1 then i else close (i + 1) (depth - 1)
                else
                    close (i + 1) depth

            let closeBrace = close openBrace 0
            let body = cssNoComments.Substring(openBrace + 1, closeBrace - openBrace - 1)
            scan (closeBrace + 1) ((idx, body) :: acc)

    scan 0 []

/// The block that projects Phase 1473's authored members — identified by the
/// classes themselves rather than by position, so the identification cannot be
/// the thing that goes wrong.
let private authoredBlock () =
    printBlocks ()
    |> List.filter (fun (_, body) -> body.Contains ".fuaran-break-inside-avoid")

/// The Phase 1124 defaults block: every print block that is not the authored
/// one.
let private defaultBlocks () =
    printBlocks ()
    |> List.filter (fun (_, body) -> not (body.Contains ".fuaran-break-inside-avoid"))

[<Tests>]
let tests =
    testList
        "PrintCascade"
        [
          // The probe before the verdict, on the `CssCoverage` pattern: a scan
          // that found no print blocks would pass every assertion below by
          // vacuity, and a green result is exactly what that would look like.
          test "the stylesheet carries both print blocks" {
              let blocks = printBlocks ()

              Expect.isGreaterThanOrEqual
                  (List.length blocks)
                  2
                  "expected at least two `@media print` blocks (Phase 1124's defaults and Phase 1473's break control) — the scan found fewer, so every cascade assertion below is vacuous"

              Expect.equal
                  (List.length (authoredBlock ()))
                  1
                  "expected exactly one `@media print` block projecting the Phase 1473 authored classes"

              Expect.isGreaterThanOrEqual
                  (List.length (defaultBlocks ()))
                  1
                  "expected at least one Phase 1124 print-defaults block"
          }

          // (1) ORDER.
          test "the defaults block precedes the authored break-control block" {
              let authoredAt = authoredBlock () |> List.head |> fst
              let lastDefaultAt = defaultBlocks () |> List.map fst |> List.max

              Expect.isLessThan
                  lastDefaultAt
                  authoredAt
                  "the Phase 1124 print DEFAULTS must be declared BEFORE the Phase 1473 authored break-control block. CSS takes the last declaration at equal specificity, so this ordering is what makes an authored `keepTogether` / `breakBefore` beat a default rather than the reverse. Moving either block reverses the meaning of the sheet with nothing else in the build noticing."
          }

          // (2) NO CONTESTED PROPERTY.
          test "the defaults block declares no break-before" {
              for _, body in defaultBlocks () do
                  Expect.isFalse
                      (Regex.IsMatch(body, @"(?<![-\w])(page-)?break-before\s*:"))
                      "the print-defaults block declares `break-before`, which is exactly the property `BoxSpec.breakBefore` (Phase 1473) projects through `.fuaran-break-before-page`. A default here would contest an authored declaration, leaving source order as the only thing deciding whose page break happens. Express a default some other way, or move it into the authored block with a specificity argument."
          }

          // (3) NO SUBSUMED MEMBER.
          test "the defaults block declares no fragmentation rule on table rows" {
              for _, body in defaultBlocks () do
                  // Each rule's selector list, paired with its declarations.
                  let rules =
                      Regex.Matches(body, @"([^{}]+)\{([^{}]*)\}")
                      |> Seq.map (fun m -> m.Groups[1].Value, m.Groups[2].Value)

                  for selector, declarations in rules do
                      let touchesRows = Regex.IsMatch(selector, @"(?<![-\w])tr(?![-\w])")

                      let fragments =
                          Regex.IsMatch(declarations, @"(?<![-\w])(page-)?break-(inside|before|after)\s*:")

                      Expect.isFalse
                          (touchesRows && fragments)
                          (sprintf
                              "the print-defaults block applies a fragmentation property to table rows (`%s`). `DataGridSpec.keepRowsTogether` (Phase 1473) is the wire member that says exactly this, and it projects onto `.fuaran-grid-rows-together tr` — a blanket default would make every declaration of it change no rendering at all, which is a shipped wire member turned fake affordance. Row cohesion is the document's to declare."
                              (selector.Trim()))
          }

          // The other half of (3), from the authored side: the projection this
          // block must not subsume has to still BE there. An exemption that
          // outlives the thing it protects is the failure `CssCoverage`'s
          // declared-absence lists are shaped to avoid, and the same applies
          // here — all four Phase 1473 classes are asserted present, so this
          // module fails rather than passing vacuously if the authored block is
          // ever emptied.
          test "the authored break-control classes are all still projected" {
              let _, body = authoredBlock () |> List.head

              for cls in
                  [ ".fuaran-break-inside-avoid"
                    ".fuaran-break-before-page"
                    ".fuaran-grid-rows-together"
                    ".fuaran-grid-repeat-header" ] do
                  Expect.isTrue
                      (body.Contains cls)
                      (sprintf
                          "`%s` has no rule in the authored `@media print` block — the Phase 1473 wire member it projects would decode and render as nothing"
                          cls)
          }

          // The defaults block's own content, pinned at the level of the
          // decisions its comment records rather than rule by rule. Three
          // claims, each one a thing the phase argued for and any of which a
          // later edit could drop without a symptom until someone prints.
          test "the defaults block sets no page geometry" {
              Expect.isFalse
                  (Regex.IsMatch(cssNoComments, @"@page\b"))
                  "the stylesheet declares an `@page` rule. Page size, margins and running furniture are the reader's, chosen in the print dialogue they are looking at, and the ratified `PrintLayout` charter row puts the paged MEDIUM outside the language entirely — the reference host's opinion about margins is not the language's."

              for _, body in defaultBlocks () do
                  Expect.isFalse
                      (body.Contains "print-color-adjust")
                      "the print-defaults block forces background printing. That spends the reader's ink to rescue a tone channel which must not have depended on colour in the first place, and it would hide the fact that it had: the repair is to make each tone legible WITHOUT its fill."
          }

          test "the defaults block expands collapsed detail and clipped content" {
              let body = defaultBlocks () |> List.map snd |> String.concat "\n"

              Expect.isTrue
                  (body.Contains ".fuaran-scrollarea"
                   && Regex.IsMatch(body, @"overflow\s*:\s*visible"))
                  "a scroll area still clips its content when printed — paper has no fold, so the clipped remainder is simply missing from the page"

              Expect.isTrue
                  (body.Contains "::details-content")
                  "a closed disclosure still prints closed — the `::details-content` leg is what reveals it on engines that hide the content through the pseudo-element"

              // The alternatives, deliberately NOT expanded. A tab panel and a
              // switch stage are branches the reader chose between; printing the
              // unchosen ones is a different document, not a completer one.
              Expect.isFalse
                  (body.Contains ".fuaran-tabs-panel" || body.Contains ".fuaran-switch-stage")
                  "the print-defaults block reaches a tab panel or a switch stage. Those are ALTERNATIVES the reader selected between, not collapsed detail of what is on the page — expanding them prints content the reader did not choose. The distinction is the block's own rule 4."
          }

          test "provenance chrome survives printing" {
              let body = defaultBlocks () |> List.map snd |> String.concat "\n"

              // Hiding either would print a table that cannot say which rows it
              // is showing. Asserted as an ABSENCE from every `display: none`
              // rule, which is where such a regression would land.
              let hidden =
                  Regex.Matches(body, @"([^{}]+)\{([^{}]*display\s*:\s*none[^{}]*)\}")
                  |> Seq.map (fun m -> m.Groups[1].Value)
                  |> String.concat " "

              for cls in [ ".fuaran-filters"; ".fuaran-filter-label"; ".fuaran-grid-pager-status" ] do
                  Expect.isFalse
                      (hidden.Contains cls)
                      (sprintf
                          "the print-defaults block hides `%s`. A filter bar and a pager status are the record of WHICH ROWS these are; a table printed without them makes a claim it cannot support. Only the pager's step BUTTONS are hidden — a step is an act, a status is a fact."
                          cls)
          } ]
