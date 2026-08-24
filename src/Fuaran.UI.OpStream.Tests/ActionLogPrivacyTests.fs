module Fuaran.UI.OpStream.Tests.ActionLogPrivacyTests

open System.IO
open Expecto

open Fuaran.UI.Types
open Fuaran.UI.Ops.ActionInvocation

// ============================================================================
//  Phase 889 — "the log lands LOCAL, and there is no upload path", checked
//  mechanically.
//
//  The posture is Phase 716's, adopted rather than re-invented: operator-chosen
//  local destination, no network surface, and the claim asserted by a scan
//  rather than by a comment. 716's own words for why: "a comment asserting it
//  ages badly — the day someone adds 'just fetch the log from a URL' for
//  convenience, a prose posture note stays true-looking."
//
//  Deliberately crude and deliberately narrow, for 716's reasons. Crude,
//  because a clever exfiltration path is not the threat — the threat is a
//  well-meaning convenience feature. Narrow, because it scans an ENUMERATED
//  surface rather than a package or a repo: a repo-wide gate is one nobody
//  keeps green, and a gate that is routinely overridden stops being read.
//
//  The enumeration is no longer "the two files this was authored in". It is the
//  census at `docs/ACTION-LOG-PRIVACY.md`, which walks every site that renders,
//  logs or reports an `Action`, grades each by the most revealing thing it can
//  emit, and records the ones deliberately left alone with the reason. Read the
//  two together: an unenumerated surface cannot be claimed safe, and a scan
//  whose subject nobody enumerated proves only that the files someone happened
//  to name are clean.
//
//  The census's second check lives here too — a POISON scan over the designated
//  log-safe describer, and a go-red twin that proves it discriminates. The rest
//  of the census's mechanical half is on the server-driven tier, in
//  `Fuaran.UI.ServerDriven.Tests/ActionLogCensusTests.fs`, because that tier
//  COMPOSES the describer's output into the sentence a host logs, and a
//  composition is exactly where a safe ingredient stops being a safe result.
// ============================================================================

/// Surfaces an upload path would have to reach for. `Socket` and `WebClient`
/// join the list because "not HttpClient" is not the same as "not networked".
let private bannedTokens =
    [ "System.Net"
      "HttpClient"
      "HttpRequestMessage"
      "WebClient"
      "WebRequest"
      "Socket"
      "http://"
      "https://" ]

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

/// The Phase 889 sources: the record + emission helpers, and the durable sink.
let private phase889Sources =
    [ Path.Combine(repoRoot, "src", "Fuaran.UI.Ops.Abstractions", "ActionInvocation.fs")
      Path.Combine(repoRoot, "src", "Fuaran.UI.OpStream.Sqlite", "ActionInvocationSqliteSink.fs") ]

/// The scan's widened subject: the censused local-log surface, not merely the
/// two files the record was originally authored in. The census lives at
/// `docs/ACTION-LOG-PRIVACY.md` and is what decides this list — the two are read
/// together, because a scan whose subject nobody enumerated proves only that the
/// files someone happened to name are clean.
///
/// Still deliberately narrow. `Fuaran.UI.ServerDriven/Validation.fs` joins because
/// it is the second designated log-safe describer; the renderer's diagnostic
/// surfaces do not, because a browser tier legitimately holds `http`-shaped
/// vocabulary everywhere and a scan there would be noise a maintainer learns to
/// override.
let private censusedLocalLogSources =
    phase889Sources
    @ [ Path.Combine(repoRoot, "src", "Fuaran.UI.ServerDriven", "Validation.fs") ]

/// A poison string, in every payload position of every `Action` case. The
/// author-declared NAMES deliberately carry none, since those are what a
/// grade-B describer is supposed to keep.
let private poison = "PoIsOn-uSeR-tYpEd-53cr3t"

type private Msg = Poke of string

let private allElevenCases: (string * Fuaran.UI.Types.Action<Msg>) list =
    [ "Chain", Action.Chain [ Action.WriteToClipboard poison; Action.Navigate("/a?q=" + poison) ]
      "WriteToClipboard", Action.WriteToClipboard poison
      "Dispatch", Action.Dispatch(Poke poison)
      // Fully qualified: `open System` elsewhere puts `System.Action` in scope
      // and its instance `Invoke` wins the name resolution otherwise.
      "Invoke", Fuaran.UI.Generated.Action.Invoke("cap.publish", [])
      "ReadFileBody", Action.ReadFileBody(poison, None, FileReadEncoding.Text, None)
      "Call", Action.Call("/api/save", None, None)
      "Navigate", Action.Navigate("/orders?email=" + poison + "#" + poison)
      "CommitLocal", Action.CommitLocal "field-1"
      "Notify", Action.Notify("toast", Fuaran.Core.JStr poison)
      "SetState", Action.SetState("draft.body", Some(Fuaran.Core.JStr poison), None)
      "AiTool", Action.AiTool("summarise", Fuaran.Core.JObj [ "text", Fuaran.Core.JStr poison ]) ]

[<Tests>]
let tests =
    testList
        "Phase 889 — the user-action log's privacy posture"
        [ test "the record and its durable sink open no network surface" {
              let offences =
                  censusedLocalLogSources
                  |> List.collect (fun path ->
                      File.ReadAllLines path
                      |> Array.mapi (fun i line -> i + 1, line)
                      |> Array.collect (fun (n, line) ->
                          bannedTokens
                          |> List.filter line.Contains
                          |> List.map (fun t -> sprintf "%s:%d — %s" (Path.GetFileName path) n t)
                          |> Array.ofList)
                      |> Array.toList)

              Expect.isEmpty
                  offences
                  "the user-action log is local and operator-chosen; a network surface here would be an upload path for exactly the data this record is careful about"
          }

          test "the scan would actually catch a leak (the guard tests itself)" {
              // A guard that silently matches nothing is worse than no guard: it
              // passes forever and proves nothing. Same scan, against a token
              // that IS present — so a refactor that breaks the file discovery
              // fails HERE rather than going quiet.
              for path in censusedLocalLogSources do
                  Expect.isTrue (File.Exists path) (sprintf "the guard's file discovery finds %s" path)

              let hits =
                  censusedLocalLogSources
                  |> List.filter (fun p -> (File.ReadAllText p).Contains "ActionInvocation")

              Expect.equal
                  (List.length hits)
                  (List.length censusedLocalLogSources)
                  "and the same read finds a token that is genuinely present in each file"
          }

          // ── The poison scan over the censused describer, and its go-red twin ──
          //
          // The redaction default is already pinned over the whole RECORD. What
          // was never pinned is `describe` ITSELF, which is what every censused
          // grade-B site ultimately prints — the server-driven denial reasons,
          // the durable record's `Action` field, the form-buffer refusals. A
          // rule stated once and checked in one place is a rule that holds
          // wherever it is quoted; this is the check in that one place.

          test "POISON: `describe` leaks no payload value, in any of the eleven cases" {
              for name, action in allElevenCases do
                  let described = ActionInvocation.describe action

                  Expect.isFalse
                      (described.Contains poison)
                      (sprintf
                          "%s: a payload value reached the log-safe describer — every censused constructor-grade site prints this string. Got: %s"
                          name
                          described)
          }

          test "POISON go-red check: the same fixture IS payload-bearing under the opt-in" {
              // Without this, the test above passes for the wrong reason the day
              // a fixture stops carrying poison, or a payload slot is dropped
              // from the DU. Feeding the SAME actions through the opt-in mode
              // must find the poison — the check is shown able to fail before it
              // is trusted not to.
              let leaking =
                  allElevenCases
                  |> List.filter (fun (_, action) ->
                      match ActionInvocation.payloadFor ActionCaptureMode.PayloadBearing action with
                      | Some jv -> (Fuaran.Core.Json.encode jv).Contains poison
                      | None -> false)
                  |> List.map fst

              // Six of the eleven: `Dispatch` / `Call` / `Chain` have no wire
              // payload at all, and `Invoke` / `CommitLocal` carry only an
              // author-declared id, so the fixture deliberately gives those none.
              Expect.equal
                  (List.sort leaking)
                  [ "AiTool"
                    "Navigate"
                    "Notify"
                    "ReadFileBody"
                    "SetState"
                    "WriteToClipboard" ]
                  "the opt-in mode finds exactly the poison the redacted mode must not — so the scan discriminates rather than matching nothing"
          }

          test "the durable sink names no default destination" {
              // Where the log lands is the operator's choice. A connection
              // string baked into the sink would make "local, operator-chosen"
              // a matter of what the host happened not to override.
              let text =
                  File.ReadAllText(
                      Path.Combine(repoRoot, "src", "Fuaran.UI.OpStream.Sqlite", "ActionInvocationSqliteSink.fs")
                  )

              Expect.isFalse (text.Contains "Data Source=") "no connection string is constructed inside the sink"
          } ]
