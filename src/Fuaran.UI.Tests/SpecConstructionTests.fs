module Fuaran.UI.Tests.SpecConstructionTests

#nowarn "3261" // DirectoryInfo.Parent is legitimately nullable here (the climb's terminator).

// ============================================================================
//  Phase 1106 — the Defaults-`with` construction discipline.
//
//  A spec record grows additively: the wire format's forward-coupling rule adds
//  a slot, `Defaults` gains its identity value, and every DOCUMENT that omitted
//  the slot still decodes to exactly what it decoded to before. That is the
//  design, and on the wire it holds. In SOURCE it did not: F# requires a record
//  literal to name every field (FS0764), so each additive field broke every
//  full literal in the repo — the media wave's five `ImageSpec` fixtures and the
//  SSR-parity trees, once per phase — which made wire-additive growth
//  source-breaking in practice and put the churn on whoever added the field.
//
//  The language already ships the fix. `{ Defaults.image with Src = … }` names
//  only what differs, so a new slot arrives at its default with no edit. This
//  module makes that the enforced convention at the AUTHORING sites rather than
//  a habit: it reads the sources and fails on a full spec-record literal that
//  has not declared itself deliberate.
//
//  WHAT IS GOVERNED, and why the boundary sits there.
//
//   * The SPECS are derived from `Defaults.fs`: every PUBLIC `Defaults.x`
//     record value of four or more fields. Derived rather than listed, so a new
//     spec is governed the moment it gains a default — which is the same commit
//     that makes the idiom available for it. The design-token family (`Theme`
//     and the records it is built from) is declared out below WITH ITS REASON,
//     and the declaration is checked for completeness: a new public default is
//     either governed or named there, never silently neither.
//
//   * The FILES are the authoring sites — the test projects, `samples/` and
//     `benchmarks/`. Library sources are deliberately NOT governed, and that is
//     the load-bearing half of the boundary: a decoder RECONSTRUCTS a record
//     from external data, and there the full literal is exactly right, because
//     FS0764 is what makes a new wire field impossible to forget in the codec.
//     The same error is a defect at an authoring site and a safety net at a
//     reconstruction site; this lint is the line between them.
//
//  THE ESCAPE IS A MARKER, NOT A MUTE-LIST. A literal whose full-ness is the
//  assertion — a test that exists to catch field additions — carries
//
//      // FULL-LITERAL(ImageSpec): <why the full-ness is the point>
//
//  on a comment line just above it. The marker NAMES THE SPEC, so it cannot
//  drift onto a different literal and keep working, and it is written where the
//  next reader of the literal will see it rather than in a list elsewhere.
//
//  The scanner's own go-red is asserted below against synthetic sources: a
//  check that cannot fail is not evidence, and a source-reading check whose
//  parser has quietly stopped matching anything reports a clean repo.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

// ─── Locating the checkout ─────────────────────────────────────────────────

/// The repo root, found by climbing from the test binary. Identified by two
/// files rather than one, so a directory that merely happens to hold a
/// `Fuaran.sln` cannot be mistaken for it.
let private repoRoot: string =
    let rec climb (dir: DirectoryInfo) =
        if isNull (box dir) then
            None
        elif
            File.Exists(Path.Combine(dir.FullName, "Fuaran.sln"))
            && File.Exists(Path.Combine(dir.FullName, "src", "Fuaran.UI", "Defaults.fs"))
        then
            Some dir.FullName
        else
            climb dir.Parent

    match climb (DirectoryInfo(AppContext.BaseDirectory)) with
    | Some root -> root
    | None ->
        // Never skip: a construction lint that finds no sources reports a clean
        // repo, which is the one answer it must never give by accident.
        failwith
            "SpecConstruction: could not locate the repo root above the test binary — the construction sites could not be read, so this lint has proved nothing."

// ─── A comment- and string-aware view of F# source ─────────────────────────

/// Placeholder standing in for one character of string or char-literal content.
/// Deliberately not whitespace and not a brace: a scanner reading this view can
/// never be confused by a `{`, a quote or a `//` that lives inside a string,
/// and a field's value span still keeps its real extent (blanking a string to
/// spaces would make `Name = "a"` and `Name = "b"` look alike, which is exactly
/// the bug that would let this lint mistake one literal for another).
[<Literal>]
let private Fill = '\001'

/// Same-length view of F# source: comments become spaces (newlines kept) and
/// string / char-literal content becomes `Fill`. Offsets into the view are
/// offsets into the original.
let private analysisView (text: string) : string =
    let sb = Text.StringBuilder(text.Length)
    let n = text.Length

    let blank (a: int) (b: int) (filler: char) =
        for k in a .. b - 1 do
            sb.Append(if text[k] = '\n' then '\n' else filler) |> ignore

    let mutable i = 0

    while i < n do
        let two = if i + 2 <= n then text.Substring(i, 2) else ""
        let three = if i + 3 <= n then text.Substring(i, 3) else ""

        if three = "\"\"\"" then
            let close = text.IndexOf("\"\"\"", i + 3)
            let j = if close < 0 then n else close + 3
            blank i j Fill
            i <- j
        elif two = "@\"" then
            let mutable j = i + 2
            let mutable go = true

            while go && j < n do
                if text[j] = '"' then
                    if j + 1 < n && text[j + 1] = '"' then
                        j <- j + 2
                    else
                        j <- j + 1
                        go <- false
                else
                    j <- j + 1

            blank i j Fill
            i <- j
        elif text[i] = '"' then
            let mutable j = i + 1
            let mutable go = true

            while go && j < n do
                if text[j] = '\\' then
                    j <- j + 2
                elif text[j] = '"' then
                    j <- j + 1
                    go <- false
                elif text[j] = '\n' then
                    go <- false
                else
                    j <- j + 1

            blank i (min j n) Fill
            i <- min j n
        elif two = "//" then
            let nl = text.IndexOf('\n', i)
            let j = if nl < 0 then n else nl
            blank i j ' '
            i <- j
        elif two = "(*" then
            let mutable depth = 1
            let mutable j = i + 2

            while depth > 0 && j < n do
                if j + 1 < n && text[j] = '(' && text[j + 1] = '*' then
                    depth <- depth + 1
                    j <- j + 2
                elif j + 1 < n && text[j] = '*' && text[j + 1] = ')' then
                    depth <- depth - 1
                    j <- j + 2
                else
                    j <- j + 1

            blank i j ' '
            i <- j
        else
            let charLit = Regex.Match(text.Substring(i, min 4 (n - i)), @"^'(?:[^'\\\n]|\\.)'")

            if charLit.Success then
                blank i (i + charLit.Length) Fill
                i <- i + charLit.Length
            else
                sb.Append(text[i]) |> ignore
                i <- i + 1

    sb.ToString()

// ─── Record-literal scanning ───────────────────────────────────────────────

let private fieldRe =
    Regex(@"(?:^|[{;\n])[ \t]*([A-Z][A-Za-z0-9_]*)[ \t]*=(?!=)", RegexOptions.Multiline)

/// Blanks everything nested inside the body, so only the literal's OWN fields
/// are seen — a nested record's fields must not be counted as this one's.
let private depthZeroOnly (body: string) : string =
    let sb = Text.StringBuilder(body.Length)
    let mutable depth = 0

    for c in body do
        if c = '{' || c = '(' || c = '[' then
            depth <- depth + 1
            sb.Append(' ') |> ignore
        elif c = '}' || c = ')' || c = ']' then
            sb.Append(if depth > 1 then ' ' else c) |> ignore
            depth <- depth - 1
        else
            sb.Append(
                if depth = 0 then c
                elif c = '\n' then '\n'
                else ' '
            )
            |> ignore

    sb.ToString()

/// The field names a record-literal body names at its own nesting level.
let private topLevelFields (body: string) : Set<string> =
    depthZeroOnly body
    |> fieldRe.Matches
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Set.ofSeq

let private withFormRe = Regex(@"^\s*[A-Za-z_][\w.<>, ]*\s+with\b")

/// Every brace-delimited block in a source, innermost first, as
/// `(openIndex, bodyInView, bodyInSource)`.
let private braceBlocks (view: string) (source: string) =
    let stack = Collections.Generic.Stack<int>()

    seq {
        for i in 0 .. view.Length - 1 do
            if view[i] = '{' then
                stack.Push i
            elif view[i] = '}' && stack.Count > 0 then
                let s = stack.Pop()
                yield (s, view.Substring(s + 1, i - s - 1), source.Substring(s + 1, i - s - 1))
    }

// ─── The governed spec set, derived from Defaults.fs ───────────────────────

/// A spec record small enough that a full literal costs nothing to keep in step
/// is not worth governing: `{ Defaults.markdown with Text = t }` is strictly
/// worse than `{ Text = t }`, and a field added to a one-field record is a
/// design event, not churn.
[<Literal>]
let private MinGovernedFields = 4

/// The design-token family — `Theme` and the records it is assembled from.
/// Declared out with its reason rather than filtered by a shape rule, because
/// the reason is not structural: a theme IS a complete palette, `ThemeTests`
/// pins every value in it against the reference stylesheet byte for byte, and
/// the sample themes under `samples/themes/` exist precisely to author one in
/// full. These records grow with the CSS contract, not with the wire format, so
/// the churn this lint exists to stop does not arise for them.
let private designTokenFamily: Set<string> =
    Set.ofList
        [ "Theme"
          "Tones"
          "ToneStateMatrix"
          "Interaction"
          "FocusRing"
          "Spacing"
          "FontScale"
          "FontWeight"
          "LineHeight"
          "Radius"
          "ButtonSize"
          "TabBar"
          "Segmented"
          "Breakpoints" ]

let private defaultsSource: string =
    File.ReadAllText(Path.Combine(repoRoot, "src", "Fuaran.UI", "Defaults.fs"))

let private defaultsBindingRe =
    Regex(
        @"(?m)^let[ \t]+(?<priv>private[ \t]+)?(?<name>[a-zA-Z][A-Za-z0-9_]*)(?:<[^>]*>)?[ \t]*:[ \t]*(?<ty>[A-Za-z0-9_.]+)(?:<[^>]*>)?[ \t]*=[ \t\r\n]*"
    )

/// Every public `Defaults.x` record value with its type and its field names,
/// read out of `Defaults.fs`. Read from source rather than by reflection
/// because the FIELD SET is what matters and reflection over a generic default
/// would need an instantiation; the file is the authority either way.
let private allDefaults: (string * string * Set<string>) list =
    let view = analysisView defaultsSource

    [ for m in defaultsBindingRe.Matches view do
          if not (m.Groups["priv"].Success) then
              let braceAt = view.IndexOf('{', m.Index + m.Length - 1)
              // Only a record literal starts a default we can read fields from;
              // a function or a computed value simply is not one.
              if
                  braceAt >= 0
                  && view.Substring(m.Index + m.Length, braceAt - (m.Index + m.Length)).Trim() = ""
              then
                  let mutable depth = 0
                  let mutable close = -1
                  let mutable i = braceAt

                  while close < 0 && i < view.Length do
                      if view[i] = '{' then
                          depth <- depth + 1
                      elif view[i] = '}' then
                          depth <- depth - 1

                          if depth = 0 then
                              close <- i

                      i <- i + 1

                  if close > braceAt then
                      let fields = topLevelFields (view.Substring(braceAt + 1, close - braceAt - 1))
                      yield (m.Groups["ty"].Value, m.Groups["name"].Value, fields) ]

/// Spec type -> (`Defaults` value name, its field set).
let private governedSpecs: Map<string, string * Set<string>> =
    allDefaults
    |> List.filter (fun (ty, _, fields) -> fields.Count >= MinGovernedFields && not (designTokenFamily.Contains ty))
    |> List.map (fun (ty, name, fields) -> ty, (name, fields))
    |> Map.ofList

// ─── The governed construction sites ───────────────────────────────────────

/// The authoring sites: every test project, `samples/` and `benchmarks/`.
/// Discovered rather than listed, so a new test project is governed on the day
/// it is created — a list would have to be remembered, and the whole class of
/// defect here is what happens when it is not.
let private constructionSites: string list =
    let excluded (path: string) =
        let p = path.Replace('\\', '/')

        p.Contains "/obj/"
        || p.Contains "/bin/"
        || p.Contains "/fable_modules/"
        || p.Contains "/output/"
        || p.Contains "/node_modules/"

    let roots =
        [ yield!
              Directory.EnumerateDirectories(Path.Combine(repoRoot, "src"), "*.Tests")
              |> Seq.filter (fun d -> not (excluded d))
          for name in [ "samples"; "benchmarks" ] do
              let d = Path.Combine(repoRoot, name)

              if Directory.Exists d then
                  yield d ]

    roots
    |> List.collect (fun root ->
        Directory.EnumerateFiles(root, "*.fs", SearchOption.AllDirectories)
        |> List.ofSeq)
    |> List.filter (fun f -> not (excluded f))
    |> List.sort

// ─── The scan ──────────────────────────────────────────────────────────────

type Finding =
    { File: string
      Line: int
      Spec: string
      DefaultsValue: string }

let private markerRe =
    Regex(@"//\s*FULL-LITERAL\(\s*(?<spec>[A-Za-z0-9_]+)\s*\)\s*:")

/// Is a `FULL-LITERAL(<spec>)` marker for THIS spec present on one of the few
/// comment lines above the literal? The marker names the spec so that it cannot
/// drift onto a neighbouring literal and go on silencing something else.
let private markedFor (spec: string) (source: string) (openIndex: int) : bool =
    let before = source.Substring(0, openIndex)

    let lines = before.Split('\n') |> Array.rev |> Array.truncate 6

    lines
    |> Array.exists (fun l ->
        let m = markerRe.Match l
        m.Success && m.Groups["spec"].Value = spec)

/// Full spec-record literals in one source text.
let private scan (label: string) (source: string) : Finding list =
    let view = analysisView source

    [ for (openIndex, bodyView, _) in braceBlocks view source do
          if not (withFormRe.IsMatch bodyView) then
              let fields = topLevelFields bodyView

              match
                  governedSpecs
                  |> Map.tryPick (fun ty (name, spec) -> if spec = fields then Some(ty, name) else None)
              with
              | Some(ty, name) when not (markedFor ty source openIndex) ->
                  yield
                      { File = label
                        Line = 1 + (source.Substring(0, openIndex) |> Seq.filter ((=) '\n') |> Seq.length)
                        Spec = ty
                        DefaultsValue = name }
              | _ -> () ]

let private scanRepo () : Finding list =
    constructionSites
    |> List.collect (fun path -> scan (Path.GetRelativePath(repoRoot, path).Replace('\\', '/')) (File.ReadAllText path))

// ─── Synthetic sources for the go-red proof ────────────────────────────────

// The probes below are BUILT from the governed field set rather than typed out.
// A hand-written sample would have to be edited every time the record it
// imitates gains a field — which is the churn this whole module exists to
// remove, reintroduced inside its own go-red proof. Only field NAMES are read
// by the scanner, so `()` stands in for every value.

let private syntheticFullLiteral (spec: string) : string =
    let _, fields = governedSpecs[spec]

    fields
    |> Set.toList
    |> List.mapi (fun i f -> (if i = 0 then "            { " else "              ") + f + " = ()")
    |> String.concat "\n"
    |> fun body -> body + " }"

let private syntheticWithLiteral (spec: string) : string =
    let name, fields = governedSpecs[spec]
    sprintf "            { Defaults.%s with\n                %s = () }" name (fields |> Set.toList |> List.head)

let private wrap (body: string) (preamble: string) : string =
    sprintf "let x =\n    NodeKind.Image(\n%s%s\n    )\n" preamble body

[<Tests>]
let tests =
    testList
        "SpecConstruction"
        [
          // The set the whole lint quantifies over. An empty or shrunken set
          // would make every assertion below vacuously true, which is precisely
          // how a source-reading check reports a clean repo after its parser
          // has stopped matching.
          test "the governed spec set is derived from Defaults.fs and is not empty" {
              Expect.isGreaterThan
                  governedSpecs.Count
                  10
                  "far fewer governed specs than Defaults.fs declares — the Defaults parser has stopped matching, and every assertion in this module is now vacuous"

              Expect.isTrue
                  (governedSpecs.ContainsKey "ImageSpec")
                  "ImageSpec is not governed — it is the record whose additive growth this discipline exists for, so its absence means the derivation is wrong"

              let name, fields = governedSpecs["ImageSpec"]
              Expect.equal name "image" "ImageSpec's default is Defaults.image"

              Expect.isTrue
                  (fields.Contains "Src" && fields.Contains "Alt")
                  "ImageSpec's field set was not read out of Defaults.fs"
          }

          // The exclusion is falsifiable rather than a mute-list: every public
          // default of governable size is either governed or named in the
          // design-token family, so a new one cannot be silently neither.
          test "every public Defaults record is either governed or declared out" {
              let unaccounted =
                  allDefaults
                  |> List.filter (fun (ty, _, fields) ->
                      fields.Count >= MinGovernedFields
                      && not (governedSpecs.ContainsKey ty)
                      && not (designTokenFamily.Contains ty))
                  |> List.map (fun (ty, name, _) -> sprintf "%s (Defaults.%s)" ty name)

              Expect.isEmpty
                  unaccounted
                  "a public Defaults record is neither governed by this lint nor declared into the design-token family — decide which it is (govern it by leaving it alone, or add it to designTokenFamily WITH the reason its full-ness is the point)"

              // And the exclusion must not outlive its subject: a name in the
              // family that Defaults.fs no longer declares is a stale mute.
              let declared = allDefaults |> List.map (fun (ty, _, _) -> ty) |> Set.ofList

              let stale =
                  designTokenFamily
                  |> Set.filter (fun ty -> not (declared.Contains ty))
                  |> Set.toList

              Expect.isEmpty
                  stale
                  "designTokenFamily names a record Defaults.fs no longer declares — delete the entry rather than carrying an exemption for nothing"
          }

          test "the construction sites are discovered, not empty" {
              Expect.isGreaterThan
                  constructionSites.Length
                  50
                  "far fewer construction sites than this repo has test and sample sources — the discovery walk is wrong, and a lint that reads nothing passes everything"
          }

          // The go-red proof. Run first in reading order for the same reason it
          // matters at all: the repo-wide assertion below is only evidence if
          // the scanner it uses can be shown to fail.
          test "the scanner reports a full literal, and stops reporting once it is fixed" {
              let full = wrap (syntheticFullLiteral "ImageSpec") ""

              Expect.equal
                  (scan "synthetic.fs" full |> List.map (fun f -> f.Spec))
                  [ "ImageSpec" ]
                  "a full ImageSpec literal must be reported"

              Expect.isEmpty
                  (scan "synthetic.fs" (wrap (syntheticWithLiteral "ImageSpec") ""))
                  "a Defaults-`with` literal must not be reported"

              Expect.isEmpty
                  (scan
                      "synthetic.fs"
                      (wrap
                          (syntheticFullLiteral "ImageSpec")
                          "        // FULL-LITERAL(ImageSpec): this one exists to catch field additions.\n"))
                  "a marker naming this spec must silence the finding"

              Expect.equal
                  (scan
                      "synthetic.fs"
                      (wrap
                          (syntheticFullLiteral "ImageSpec")
                          "        // FULL-LITERAL(MediaSpec): a marker for a different record.\n")
                   |> List.map (fun f -> f.Spec))
                  [ "ImageSpec" ]
                  "a marker naming a DIFFERENT spec must not silence this one — otherwise the marker drifts and keeps working"
          }

          // The lint itself.
          test "no unmarked full spec-record literal at an authoring site" {
              let findings = scanRepo ()

              let report =
                  findings
                  |> List.map (fun f ->
                      sprintf
                          "  %s:%d — full %s literal; write `{ Defaults.%s with … }`"
                          f.File
                          f.Line
                          f.Spec
                          f.DefaultsValue)
                  |> String.concat "\n"

              Expect.isEmpty
                  findings
                  (sprintf
                      "full spec-record literals at authoring sites — each one breaks (FS0764) the next time the record gains a field, which is what makes wire-additive growth source-breaking. Rewrite as `{ Defaults.x with … }`, naming only what differs. If the full-ness IS the assertion, say so with a `// FULL-LITERAL(<Spec>): <reason>` comment line above the literal.\n%s"
                      report)
          } ]
