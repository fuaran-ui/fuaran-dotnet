module Fuaran.UI.Design.Tests.BundleTests

// Phase 2294 — the design-system bundle's contract, over the default theme and the
// Dark and High-Contrast sample themes, plus the go-red probes that show each check
// can fail: a kind removed from the sweep, a Colors token named by a literal colour,
// and a card whose first line is not its marker. Lane `pure`: nothing here touches
// the filesystem (`Bundle.write` is exercised through the CLI verb).

open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest
open Fuaran.UI.Fragments
open Fuaran.UI.Design

let private themes: (string * Theme) list =
    [ "default", Defaults.theme
      "dark", Fuaran.Samples.Themes.Dark.theme
      "high-contrast", Fuaran.Samples.Themes.HighContrast.theme ]

let private defaults () = Bundle.BundleOptions.defaults ()

let private expectedCardCount () =
    (Bundle.idlKinds ()).Length + Stdlib.all<unit>.Length + 3

let private themeTests (name: string, theme: Theme) =
    testList
        name
        [ test "the bundle checks hold" {
              let bundle = Bundle.build theme None (defaults ())
              Expect.isEmpty (Bundle.check bundle) "a bundle built from the defaults satisfies its own contract"
          }

          test "card count is IDL kinds + stdlib fragments + Type, Spacing and Colors" {
              let bundle = Bundle.build theme None (defaults ())
              Expect.equal bundle.Cards.Length (expectedCardCount ()) "one card per kind, per fragment, per scale"
          }

          test "the content hash is stable across two builds" {
              let a = Bundle.build theme None (defaults ())
              let b = Bundle.build theme None (defaults ())
              Expect.equal a.ContentHash b.ContentHash "same theme, same options, same bytes"
              Expect.stringStarts a.ContentHash "sha256:" "the hash names its algorithm"
          }

          test "the Colors group names the seven tone roles, none of them a literal colour" {
              let bundle = Bundle.build theme None (defaults ())
              let roles = bundle.Colors |> List.map _.Role |> Set.ofList

              for tone in [ "Default"; "Subdued"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ] do
                  Expect.contains roles tone $"the {tone} role is offered"

              for r in bundle.Colors do
                  Expect.isFalse (Bundle.isLiteralColour r.Role) $"{r.Role} is a role, not a value"
          }

          test "the DTCG manifest beside the cards decodes back" {
              let bundle = Bundle.build theme None (defaults ())

              let file = bundle.Files |> List.find (fun f -> f.Path = "theme.tokens.json")

              match Decode.decode file.Content with
              | Ok m -> Expect.equal m.Tokens.Length bundle.Manifest.Tokens.Length "every token round-trips"
              | Error e -> failtest $"theme.tokens.json does not decode: {e}"
          } ]

[<Tests>]
let perTheme =
    testList "Design-system bundle — per theme" (themes |> List.map themeTests)

[<Tests>]
let contract =
    testList
        "Design-system bundle — contract"
        [ test "every IDL kind is covered by the default sweep" {
              let covered = (defaults ()).Specimens |> List.map _.Kind |> Set.ofList

              let missing =
                  Bundle.idlKinds ()
                  |> List.map _.Tag
                  |> List.filter (fun k -> not (Set.contains k covered))

              Expect.isEmpty missing "a kind with no specimen would have no card"
          }

          test "go-red: a kind removed from the sweep fails the check, naming it" {
              let options =
                  { defaults () with
                      Specimens = (defaults ()).Specimens |> List.filter (fun s -> s.Kind <> "Metric") }

              let findings = Bundle.check (Bundle.build Defaults.theme None options)

              Expect.contains
                  findings
                  "IDL kind Metric has no Components card (no specimen in the sweep builds it)"
                  "the removed kind is named"

              Expect.equal findings.Length 1 "and nothing else is reported"
          }

          test "go-red: a Colors token named by a literal colour is refused" {
              let manifest =
                  { ThemeManifest.empty with
                      Tokens =
                          [ { Name = "color.accent"
                              Type = "color"
                              Value = "#ff0000"
                              Description = None
                              Role = None } ]
                      Roles =
                          [ { Role = ManifestRole.Named "#ff0000"
                              TokenName = "color.accent" } ] }

              let findings =
                  Bundle.check (Bundle.build Defaults.theme (Some manifest) (defaults ()))

              Expect.contains
                  findings
                  "Colors token \"#ff0000\" is a literal colour; the Colors group offers roles only"
                  "the hex-named token is refused"
          }

          test "a manifest's named roles are offered beside the tone roles" {
              let manifest =
                  { ThemeManifest.empty with
                      Tokens =
                          [ { Name = "color.neutral.0"
                              Type = "color"
                              Value = "#ffffff"
                              Description = None
                              Role = None } ]
                      Roles =
                          [ { Role = ManifestRole.Named "page-surface"
                              TokenName = "color.neutral.0" } ] }

              let bundle = Bundle.build Defaults.theme (Some manifest) (defaults ())
              Expect.isEmpty (Bundle.check bundle) "a role-named manifest satisfies the contract"

              let surface = bundle.Colors |> List.find (fun r -> r.Role = "page-surface")
              Expect.equal surface.Reference (Some "#ffffff") "the resolved value is kept as a reference"
              Expect.equal bundle.Colors.Length 8 "seven tone roles plus the named one"
          }

          test "literal-colour detection" {
              for name in [ "#fff"; "#3b5bdb"; "rgb(0 0 0)"; "oklch(0.6 0.1 250)"; "Red"; "transparent" ] do
                  Expect.isTrue (Bundle.isLiteralColour name) $"{name} is a literal colour"

              for name in [ "Brand"; "Critical"; "page-surface"; "body-text"; "redline" ] do
                  Expect.isFalse (Bundle.isLiteralColour name) $"{name} is a role"
          }

          test "go-red: a card whose first line is not its marker fails the check" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let tampered =
                  { bundle with
                      Cards =
                          bundle.Cards
                          |> List.map (fun c ->
                              if c.Name = "Badge" then
                                  { c with
                                      Html = c.Html.Replace("group=\"Components\"", "group=\"Widgets\"") }
                              else
                                  c) }

              Expect.contains
                  (Bundle.check tampered)
                  "card cards/components/Badge.html: marker group \"Widgets\" is not one of the five"
                  "a marker outside the five groups is refused"
          }

          test "every card's first line is the marker for its group" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              for c in bundle.Cards do
                  Expect.stringStarts c.Html (Bundle.marker c.Group + "\n") $"{c.Path} opens with its marker"
          }

          test "a brand stylesheet re-resolves the roles and moves the hash" {
              let plain = Bundle.build Defaults.theme None (defaults ())

              let branded =
                  Bundle.build
                      Defaults.theme
                      None
                      { defaults () with
                          Brand = Some ":root { --fuaran-tone-brand-bg: #123456; }" }

              Expect.isEmpty (Bundle.check branded) "a branded bundle satisfies the contract"
              Expect.notEqual branded.ContentHash plain.ContentHash "the brand reaches the bytes"

              let brand = branded.Colors |> List.find (fun r -> r.Role = "Brand")
              Expect.equal brand.Reference (Some "#123456") "the brand value is the role's reference"
          }

          test "a brand stylesheet that could close the style element is refused" {
              Expect.throws
                  (fun () ->
                      Bundle.build
                          Defaults.theme
                          None
                          { defaults () with
                              Brand = Some ":root {} </style><script>" }
                      |> ignore)
                  "the brand is inlined into a <style> element"
          }

          test "manifest.json lists every file, the groups, the hash and the IDL version" {
              let bundle = Bundle.build Defaults.theme None (defaults ())
              use doc = JsonDocument.Parse(Bundle.manifestJson bundle)
              let root = doc.RootElement

              Expect.equal (root.GetProperty("contentHash").GetString()) bundle.ContentHash "the hash"

              Expect.equal
                  (root.GetProperty("idl").GetProperty("version").GetInt32())
                  (Bundle.idlVersion ())
                  "the IDL version"

              Expect.equal (root.GetProperty("files").GetArrayLength()) bundle.Files.Length "every file"

              let grouped =
                  [ for g in root.GetProperty("groups").EnumerateObject() -> g.Value.GetArrayLength() ]
                  |> List.sum

              Expect.equal grouped bundle.Cards.Length "every card is filed under a group"
          }

          test "the content hash moves when one file moves" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let edited =
                  bundle.Files
                  |> List.map (fun f ->
                      if f.Path = "README.md" then
                          { f with Content = f.Content + "x" }
                      else
                          f)

              Expect.notEqual (Bundle.contentHashOf edited) bundle.ContentHash "a one-byte edit moves the hash"
          } ]
