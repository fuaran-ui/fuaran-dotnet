module Fuaran.UI.Tests.SanitizationCorpus

// ============================================================================
//  The shared `sanitization/` corpus family, run against this host's render-time
//  safety floor (`WIRE_FORMAT.md` §22; §19 for the URL group).
//
//  Unlike every other corpus family this one is NOT byte-parity: the markup a
//  host wraps around a payload differs legitimately between the React renderer
//  here, a static-HTML emitter and a native render projection, so comparing
//  those bytes would pin accidents rather than the contract. Each case states an
//  INVARIANT instead — `reject`, `accept`, or `inert` — and this suite asserts
//  that THIS host satisfies it.
//
//  The url-floor group's claims are verified by the corpus itself against a real
//  WHATWG parser (`sanitization/verify-against-url-parser.mjs`), so what is
//  checked here is agreement with an invariant established as true independently,
//  rather than agreement between two of our own assertions.
// ============================================================================

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

open Fuaran.UI.Renderer

let private manifestPath () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate =
                Path.Combine(d.FullName, "wire-format-fixtures", "sanitization", "manifest.json")

            if File.Exists candidate then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

type private Case =
    {
        Id: string
        Input: string
        Invariant: string
        Expected: string option
        /// `inert` only — regexes that must NOT match the rendered output.
        ForbiddenPattern: string list
        /// `inert` only — substrings that MUST appear (the legitimate cases).
        Required: string list
        /// `extra-attributes` only — which predicate the case addresses.
        Target: string option
    }

type private Group = { Id: string; Cases: Case list }

let private strings (el: JsonElement) (name: string) : string list =
    match el.TryGetProperty name with
    | true, arr ->
        [ for v in arr.EnumerateArray() do
              match v.GetString() |> Option.ofObj with
              | Some s -> s
              | None -> () ]
    | _ -> []

let private groups () : Group list =
    match manifestPath () with
    | None -> []
    | Some path ->
        use doc = JsonDocument.Parse(File.ReadAllText path)

        [ for g in doc.RootElement.GetProperty("groups").EnumerateArray() do
              let str (el: JsonElement) (name: string) =
                  match el.TryGetProperty name with
                  | true, v -> v.GetString() |> Option.ofObj
                  | _ -> None

              { Id = str g "id" |> Option.defaultValue "<unnamed>"
                Cases =
                  [ for c in g.GetProperty("cases").EnumerateArray() do
                        { Id = str c "id" |> Option.defaultValue "<unnamed>"
                          Input = str c "input" |> Option.defaultValue ""
                          Invariant = str c "invariant" |> Option.defaultValue ""
                          Expected = str c "expected"
                          ForbiddenPattern = strings c "forbiddenPattern"
                          Required = strings c "required"
                          Target = str c "target" } ] } ]

/// The `inert` check. A failure names the payload AND the pattern that matched,
/// because "live markup survived" without saying which construct sends the
/// reader back to re-derive it from the fixture.
///
/// A PATTERN rather than a substring, deliberately: an escaped payload still
/// contains the text `onclick=`, harmlessly, so a substring check would fail a
/// correct host. What must not exist is a live tag carrying the handler.
let private inertFailures (label: string) (rendered: string) (c: Case) : string list =
    let matched =
        c.ForbiddenPattern
        |> List.filter (fun p -> Regex.IsMatch(rendered, p, RegexOptions.IgnoreCase))
        |> List.map (fun p ->
            $"{c.Id} [{label}]: output matches forbidden pattern %A{p} — payload %A{c.Input} survived as live markup")

    // `required` is the other half, and the half that catches a host which
    // satisfies every forbidden pattern by discarding the content entirely.
    let missing =
        c.Required
        |> List.filter (fun r -> not (rendered.Contains r))
        |> List.map (fun r ->
            $"{c.Id} [{label}]: output is missing required %A{r} — the payload was stripped rather than escaped")

    matched @ missing

[<Tests>]
let sanitizationCorpusTests =
    let all = groups ()

    let groupCases id =
        all
        |> List.tryFind (fun g -> g.Id = id)
        |> Option.map _.Cases
        |> Option.defaultValue []

    let report name failures =
        Expect.isEmpty failures $"""{name} invariants violated:{"\n  " + String.concat "\n  " failures}"""

    testList
        "sanitization corpus — the §22 render-time floor"
        [ test "every group in the family is claimed by a leg below" {
              if List.isEmpty all then
                  skiptest "wire-format-fixtures/sanitization/manifest.json not found"

              // A group added to the corpus that no leg runs would be silently
              // untested here while reading as covered in the family — the exact
              // shape §22.2 refuses. Fail rather than pass by omission.
              let known = set [ "url-floor"; "markdown-body"; "text-source"; "extra-attributes" ]
              let unclaimed = all |> List.map _.Id |> List.filter (known.Contains >> not)

              Expect.isEmpty
                  unclaimed
                  $"""the corpus carries group(s) this host neither runs nor declares not-applicable: {String.concat ", " unclaimed}"""
          }

          test "url-floor — the URL-scheme floor (§19)" {
              let cases = groupCases "url-floor"

              if List.isEmpty cases then
                  skiptest "sanitization corpus not found"

              printfn "── sanitization/url-floor: %d cases ──" cases.Length

              cases
              |> List.choose (fun c ->
                  match c.Invariant, Sanitize.sanitizeUrl c.Input with
                  | "reject", Some got -> Some $"{c.Id}: expected REJECT, got %A{got}"
                  | "reject", None ->
                      match Sanitize.sanitizeUrlOrBlank c.Input with
                      | "about:blank" -> None
                      | other -> Some $"{c.Id}: rejected, but sanitizeUrlOrBlank gave %A{other}"
                  | "accept", None -> Some $"{c.Id}: expected ACCEPT, was rejected"
                  | "accept", Some got ->
                      match c.Expected with
                      | Some want when want <> got -> Some $"{c.Id}: expected %A{want}, got %A{got}"
                      | _ -> None
                  | other, _ -> Some $"{c.Id}: unknown invariant %A{other}")
              |> report "url-floor"
          }

          test "markdown-body — no payload survives as live markup (§22.1 rule 2)" {
              let cases = groupCases "markdown-body"

              if List.isEmpty cases then
                  skiptest "sanitization corpus not found"

              printfn "── sanitization/markdown-body: %d cases ──" cases.Length

              // The render path in order: the deterministic GFM renderer, which
              // escapes by construction, then the defence-in-depth sweep. The
              // obligation is on the pair, so the pair is what is asserted.
              cases
              |> List.collect (fun c ->
                  Sanitize.sanitizeMarkdownHtml (Markdown.toHtml c.Input)
                  |> fun rendered -> inertFailures "markdown" rendered c)
              |> report "markdown-body"
          }

          test "text-source — a text slot's payload arrives as text (§22.1 rule 1)" {
              let cases = groupCases "text-source"

              if List.isEmpty cases then
                  skiptest "sanitization corpus not found"

              printfn "── sanitization/text-source: %d cases ──" cases.Length

              // The markdown renderer is the seam a text-bearing string reaches on
              // this host, and it escapes by construction — which is what makes the
              // legitimate `a < b && c > d` case survive intact rather than stripped.
              cases
              |> List.collect (fun c -> inertFailures "text" (Markdown.toHtml c.Input) c)
              |> report "text-source"
          }

          test "extra-attributes — the key allowlist and the value floor (§22.1 rules 3-4)" {
              let cases = groupCases "extra-attributes"

              if List.isEmpty cases then
                  skiptest "sanitization corpus not found"

              printfn "── sanitization/extra-attributes: %d cases ──" cases.Length

              cases
              |> List.choose (fun c ->
                  let admitted =
                      match c.Target with
                      | Some "key" -> Sanitize.isAllowedExtraAttributeKey c.Input
                      | Some "value" -> Sanitize.isSafeExtraAttributeValue c.Input
                      | other -> failwithf "case %s has unknown target %A" c.Id other

                  match c.Invariant, admitted with
                  | "reject", true -> Some $"{c.Id}: expected REJECT, was admitted (%A{c.Input})"
                  | "accept", false -> Some $"{c.Id}: expected ACCEPT, was refused (%A{c.Input})"
                  | "reject", false
                  | "accept", true -> None
                  | other, _ -> Some $"{c.Id}: unknown invariant %A{other}")
              |> report "extra-attributes"
          } ]
