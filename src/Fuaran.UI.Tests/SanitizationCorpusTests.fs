module Fuaran.UI.Tests.SanitizationCorpus

// ============================================================================
//  The shared `sanitization/` corpus family, run against this host's URL floor.
//
//  Unlike every other corpus family this one is NOT byte-parity: the markup a
//  host wraps around a URL differs legitimately between the React renderer here,
//  a static-HTML emitter and a WASM client, so comparing those bytes would pin
//  accidents rather than the contract. Each case states an INVARIANT instead —
//  `reject` (refuse it) or `accept` (take it, and emit the normalised form) —
//  plus the reason the URL parser gives, which is what makes the case
//  meaningful.
//
//  The corpus verifies its own `reason` claims against a real WHATWG parser
//  (`sanitization/verify-against-url-parser.mjs`); this suite verifies that THIS
//  host agrees with the resulting invariants.
// ============================================================================

open System.IO
open System.Text.Json
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
    { Id: string
      Input: string
      Invariant: string
      Expected: string option }

let private cases () : Case list =
    match manifestPath () with
    | None -> []
    | Some path ->
        use doc = JsonDocument.Parse(File.ReadAllText path)

        [ for group in doc.RootElement.GetProperty("groups").EnumerateArray() do
              for c in group.GetProperty("cases").EnumerateArray() do
                  let str (name: string) =
                      match c.TryGetProperty name with
                      | true, v -> v.GetString() |> Option.ofObj
                      | _ -> None

                  { Id = str "id" |> Option.defaultValue "<unnamed>"
                    Input = str "input" |> Option.defaultValue ""
                    Invariant = str "invariant" |> Option.defaultValue ""
                    Expected = str "expected" } ]

[<Tests>]
let sanitizationCorpusTests =
    testList
        "sanitization corpus — the §19 URL floor"
        [ test "every url-floor case's invariant holds on this host" {
              let all = cases ()

              if List.isEmpty all then
                  skiptest "wire-format-fixtures/sanitization/manifest.json not found"

              // Printed so the scanned count is visible in the run — a loader that
              // silently parsed zero cases would otherwise read exactly as green as
              // one that ran them all.
              printfn "── sanitization/url-floor: %d cases ──" all.Length

              let failures =
                  all
                  |> List.choose (fun c ->
                      match c.Invariant, Sanitize.sanitizeUrl c.Input with
                      | "reject", Some got -> Some $"{c.Id}: expected REJECT, got %A{got}"
                      | "reject", None ->
                          // §19 rule 6 — the or-blank variant substitutes about:blank.
                          match Sanitize.sanitizeUrlOrBlank c.Input with
                          | "about:blank" -> None
                          | other -> Some $"{c.Id}: rejected, but sanitizeUrlOrBlank gave %A{other}"
                      | "accept", None -> Some $"{c.Id}: expected ACCEPT, was rejected"
                      | "accept", Some got ->
                          match c.Expected with
                          | Some want when want <> got -> Some $"{c.Id}: expected %A{want}, got %A{got}"
                          | _ -> None
                      | other, _ -> Some $"{c.Id}: unknown invariant %A{other}")

              Expect.isEmpty failures $"""url-floor invariants violated:{"\n  " + String.concat "\n  " failures}"""
          } ]
