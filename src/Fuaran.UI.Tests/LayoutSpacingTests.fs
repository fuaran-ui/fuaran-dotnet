module Fuaran.UI.Tests.LayoutSpacingTests

// ============================================================================
//  Phase 2794 — a gap-spaced layout container owns the spacing between its
//  children (docs/DECISIONS.md D35).
//
//  Every node wrapper carries `.fuaran-node { margin-bottom }`, which is the
//  spacing of a node in plain flow. Inside a stack, a grid or a dashboard that
//  margin was ADDED to the container's `gap`: a stack declared at 12px read
//  20px, a grid's rows sat further apart than its columns, and a child box
//  carried one margin per level of nesting, which lifted its controls above a
//  lone control beside it in a bottom-aligned row.
//
//  The claims, each a source read of the PACKAGED stylesheet (the artefact a
//  host serves, copied into the test bin by the fsproj):
//
//   1. Each gap-spaced container zeroes the node margin on its DIRECT children,
//      with a selector that outranks `.fuaran-node` whatever the order.
//   2. Nothing else in the sheet gives those children a margin back, so the
//      effective spacing between them is the container's gap token alone.
//   3. A node in plain flow keeps its margin (the base rule is unchanged).
//   4. Masonry keeps its own container-owned row spacing: multi-column layout
//      has no row gap, so `.fuaran-layout-masonry > *` is the rule that spaces
//      its rows, and it is a container rule rather than a node margin.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

let private css: string =
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fuaran-reference.css"))

/// Comments stripped: the prose above a rule names the selectors it discusses.
let private cssNoComments: string = Regex.Replace(css, @"/\*[\s\S]*?\*/", "")

/// Every innermost rule as (selectors, declarations). A rule nested in an
/// at-rule is still innermost, so it is read too.
let private rules: (string list * string) list =
    Regex.Matches(cssNoComments, @"([^{}]+)\{([^{}]*)\}")
    |> Seq.map (fun m ->
        let selectors =
            m.Groups[1].Value.Split(',')
            |> Array.map (fun s -> Regex.Replace(s.Trim(), @"\s+", " "))
            |> Array.toList

        selectors, m.Groups[2].Value)
    |> Seq.toList

/// The value of `property` in a declaration block, if declared.
let private declared (property: string) (body: string) : string option =
    let m =
        Regex.Match(body, @"(?:^|[;\s])" + Regex.Escape property + @"\s*:\s*([^;]+)")

    if m.Success then Some(m.Groups[1].Value.Trim()) else None

let private rulesFor (selector: string) : string list =
    rules
    |> List.filter (fun (selectors, _) -> List.contains selector selectors)
    |> List.map snd

/// The gap-spaced layout containers whose direct children are node wrappers,
/// with the gap token each declares. D35 names these three and no others.
let private containers: (string * string) list =
    [ ".fuaran-layout-stack", "var(--fuaran-space-md, 12px)"
      ".fuaran-layout-grid", "var(--fuaran-space-md, 12px)"
      ".fuaran-layout-dashboard", "var(--fuaran-space-lg, 16px)" ]

[<Tests>]
let tests =
    testList
        "Phase 2794 - layout containers own the spacing between their children"
        [ for container, gapToken in containers do
              test $"{container}: its direct child nodes keep no margin" {
                  let child = container + " > .fuaran-node"

                  let zeroed =
                      rulesFor child
                      |> List.exists (fun body ->
                          declared "margin-bottom" body = Some "0" || declared "margin" body = Some "0")

                  Expect.isTrue
                      zeroed
                      $"no rule zeroes the node margin on `{child}`: the `.fuaran-node` margin is added to the container's gap, so the spacing a reader sees is not the gap the container declares (D35)"
              }

              test $"{container}: the spacing between its children is its gap token alone" {
                  let gaps = rulesFor container |> List.choose (declared "gap")

                  Expect.equal
                      gaps
                      [ gapToken ]
                      $"`{container}` declares a gap other than {gapToken}; D35 keeps the tokens and tightens to them"

                  // Any rule reaching the direct children: `> .fuaran-node` or
                  // `> *`. None may set a non-zero margin, or the effective
                  // spacing is the gap plus that margin again.
                  let reaching =
                      rules
                      |> List.filter (fun (selectors, _) ->
                          selectors
                          |> List.exists (fun s -> s = container + " > .fuaran-node" || s = container + " > *"))
                      |> List.map snd

                  for body in reaching do
                      for property in [ "margin"; "margin-bottom"; "margin-top"; "margin-block"; "margin-block-end" ] do
                          match declared property body with
                          | Some v when v <> "0" ->
                              failtestf
                                  "a rule gives the children of `%s` `%s: %s`, so the space between them is the gap plus that margin"
                                  container
                                  property
                                  v
                          | _ -> ()
              }

          test "a node in plain flow keeps its margin" {
              Expect.equal
                  (rulesFor ".fuaran-node" |> List.choose (declared "margin-bottom"))
                  [ "var(--fuaran-space-sm, 8px)" ]
                  "the base `.fuaran-node` margin moved; D35 keeps it as the spacing of a node no layout container spaces (a card body, a pane, a disclosure)"
          }

          test "masonry's rows are spaced by its own container rule" {
              Expect.equal
                  (rulesFor ".fuaran-layout-masonry > *" |> List.choose (declared "margin-bottom"))
                  [ "var(--fuaran-space-md, 12px)" ]
                  "multi-column layout has no row gap, so `.fuaran-layout-masonry > *` is what spaces a masonry's rows; D35 leaves it as the container's own rule"
          } ]
