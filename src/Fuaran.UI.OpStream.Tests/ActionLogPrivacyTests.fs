module Fuaran.UI.OpStream.Tests.ActionLogPrivacyTests

open System.IO
open Expecto

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
//  well-meaning convenience feature. Narrow, because it scans the two files
//  Phase 889 authored and nothing else: a package-wide or repo-wide gate is one
//  nobody keeps green, and a gate that is routinely overridden stops being read.
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

[<Tests>]
let tests =
    testList
        "Phase 889 — the user-action log's privacy posture"
        [ test "the record and its durable sink open no network surface" {
              let offences =
                  phase889Sources
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
              for path in phase889Sources do
                  Expect.isTrue (File.Exists path) (sprintf "the guard's file discovery finds %s" path)

              let hits =
                  phase889Sources
                  |> List.filter (fun p -> (File.ReadAllText p).Contains "ActionInvocation")

              Expect.equal
                  (List.length hits)
                  (List.length phase889Sources)
                  "and the same read finds a token that is genuinely present in each file"
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
