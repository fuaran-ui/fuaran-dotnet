module Fuaran.UI.Telemetry.Tests.ProviderSubjectTests

open System
open Expecto
open Fuaran.UI.Telemetry.Abstractions

// ============================================================================
//  Phase 1637 — the subject a provider call was made under.
//
//  Two claims are checked here, and they are different kinds of claim.
//
//  The ADDITIVE claim: `ProviderCallTelemetry` gained a field and
//  `IFuaranTelemetrySink` gained nothing. Every previous member add on that
//  interface was a documented pre-1.0 minor break that every direct implementer
//  paid for, so "this one is not that" is worth checking rather than asserting.
//  It is checked twice, because the two halves catch different regressions: an
//  object expression implementing exactly the six members would stop compiling
//  if a seventh were added, and a reflected member set additionally catches a
//  RENAME or a removal, neither of which the object expression would notice.
//
//  The REDACTION claim: a subject carries no key material, refused at
//  construction rather than asserted in prose. The negative cases are the point
//  of the table, but the positive ones are load-bearing too — a guard that
//  refuses legitimate opaque identities is one a host routes around, and the
//  accept rows are what make the refusals evidence of discrimination rather
//  than of a predicate that says yes to everything.
// ============================================================================

/// The sink's member set as of Phase 171 + 183 + 330 — the six this phase must
/// leave exactly as it found them.
let private expectedSinkMembers =
    [ "RecordCacheStat"
      "RecordDeny"
      "RecordOpApply"
      "RecordProviderCall"
      "RecordRenderFailure"
      "RecordValidateOutcome" ]

/// Credential-shaped ids: `create` must refuse every one. The values are
/// syntactically shaped like the thing they imitate and are not credentials.
let private keyMaterialShapes =
    [ "an Authorization header value", "Bearer abc123.def456"
      "the same, lower-cased", "bearer abc123"
      "a basic-auth header value", "Basic dXNlcjpwYXNz"
      "a hyphenated secret key", "sk-notarealkeyvalue0000"
      "an underscored secret key", "sk_notarealkeyvalue0000"
      "a code-host personal token", "ghp_notarealtokenvalue0000"
      "a code-host fine-grained token", "github_pat_notarealtokenvalue0000"
      "a chat-platform bot token", "xoxb-000-000-notarealtoken"
      "a cloud access-key id", "AKIANOTAREALKEYID000"
      "a cloud API key", "AIzaNotARealKeyValue0000"
      "a PEM block", "-----BEGIN PRIVATE KEY-----"
      "a compact JWT", "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.c2ln"
      "a leading-space credential", "   Bearer abc123" ]

/// Ids a real host resolves. Every one must be ACCEPTED — these are the rows
/// that keep the table above discriminating.
let private legitimateIds =
    [ "a GUID", "6f2a7c1e-9d3b-4f10-a2c5-8e7b0d41f9aa"
      "a directory object id", "00u1a2b3c4d5e6f7g8h9"
      "an email-shaped principal", "person@example.com"
      "a hashed pseudonym", "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
      "a service-account name", "svc-emission-runner"
      "a numeric tenant id", "409811"
      // Not a credential: an id that merely CONTAINS a refused prefix rather
      // than starting with it. The guard is anchored, and this row says so.
      "an id containing but not starting with a prefix", "tenant-sk-northern-region"
      // `eyj` without the two dot separators is not a JWT — a base64url blob is
      // a perfectly ordinary opaque id.
      "an opaque blob that merely starts eyJ", "eyJhbGciOiJub25lIn0" ]

[<Tests>]
let tests =
    testList
        "Phase 1637 — the provider-call subject"
        [ testList
              "the sink is untouched"
              [ test "IFuaranTelemetrySink declares exactly the six members it declared before" {
                    let actual =
                        typeof<IFuaranTelemetrySink>.GetMethods()
                        |> Array.map _.Name
                        |> Array.sort
                        |> Array.toList

                    Expect.equal
                        actual
                        expectedSinkMembers
                        "the subject field is additive on the RECORD; the sink contract does not move, so no direct implementer pays for this phase"
                }

                test "RecordProviderCall still takes one ProviderCallTelemetry" {
                    match typeof<IFuaranTelemetrySink>.GetMethod "RecordProviderCall" with
                    | null -> failtest "the sink no longer declares RecordProviderCall at all"
                    | declared ->
                        Expect.equal
                            (declared.GetParameters() |> Array.map _.ParameterType)
                            [| typeof<ProviderCallTelemetry> |]
                            "one parameter, the record itself — the field rides the record, not the signature"
                }

                test "a sink implementing exactly those six members still satisfies the interface" {
                    // The compile-time half. A seventh abstract member would make
                    // this object expression fail to build, which is a louder and
                    // earlier signal than any assertion below it.
                    let sink =
                        { new IFuaranTelemetrySink with
                            member _.RecordOpApply _ = ()
                            member _.RecordDeny _ = ()
                            member _.RecordRenderFailure _ = ()
                            member _.RecordProviderCall _ = ()
                            member _.RecordCacheStat _ = ()
                            member _.RecordValidateOutcome _ = () }

                    ignore sink
                    Expect.isTrue true "the six-member implementation compiled and constructed"
                } ]

          testList
              "the subject carries no key material"
              [ for name, value in keyMaterialShapes do
                    test $"refused at construction: {name}" {
                        Expect.isTrue (TelemetrySubject.looksLikeKeyMaterial value) $"{name} reads as key material"

                        Expect.equal
                            (TelemetrySubject.create "user" value)
                            (Error "key-material")
                            $"{name} is refused by create, with the key-free classification token"
                    }

                for name, value in legitimateIds do
                    test $"accepted: {name}" {
                        Expect.isFalse
                            (TelemetrySubject.looksLikeKeyMaterial value)
                            $"{name} is an identity, not key material — a guard that refuses it is one a host routes around"

                        Expect.equal
                            (TelemetrySubject.create "user" value)
                            (Ok { Id = value; Kind = "user" })
                            $"{name} is accepted, and the id is stored verbatim"
                    } ]

          testList
              "create's other refusals"
              [ test "a blank kind is refused" {
                    Expect.equal (TelemetrySubject.create "" "user-1") (Error "blank-kind") "empty kind"
                    Expect.equal (TelemetrySubject.create "   " "user-1") (Error "blank-kind") "whitespace kind"
                }

                test "a blank id is refused" {
                    Expect.equal (TelemetrySubject.create "user" "") (Error "blank-id") "empty id"
                    Expect.equal (TelemetrySubject.create "user" "  ") (Error "blank-id") "whitespace id"
                }

                test "the kind is host vocabulary — this tier admits any non-blank tag" {
                    // The public tier coins no identity vocabulary, exactly as it
                    // coins no operation vocabulary. A host's own word for the
                    // distinction is accepted as given.
                    for kind in
                        [ "user"
                          "service-account"
                          "tenant"
                          "delegated"
                          "whatever-the-host-calls-it" ] do
                        Expect.equal
                            (TelemetrySubject.create kind "id-1")
                            (Ok { Id = "id-1"; Kind = kind })
                            $"the `{kind}` tag is recorded, not interpreted"
                } ]

          testList
              "the field on the record"
              [ test "a record constructed without a resolved identity carries None" {
                    let record: ProviderCallTelemetry =
                        { ProviderId = "p"
                          ModelId = "m"
                          Operation = ProviderOperation.Emit
                          Outcome = ProviderCallOutcome.Success
                          LatencyMs = 1.0
                          TokenUsage = None
                          SessionId = None
                          PromptId = None
                          UserId = "user-1"
                          Subject = None
                          Timestamp = DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero) }

                    Expect.isNone record.Subject "absence is the default, and it is what an unresolved identity says"
                }

                test "the subject is a second principal, not a replacement for UserId" {
                    // The divergent case is the one the field exists for: the work
                    // is for one principal and the call was paid for by another.
                    let subject =
                        match TelemetrySubject.create "service-account" "svc-emission-runner" with
                        | Ok s -> s
                        | Error reason -> failwithf "the fixture subject was refused: %s" reason

                    let record: ProviderCallTelemetry =
                        { ProviderId = "p"
                          ModelId = "m"
                          Operation = ProviderOperation.Emit
                          Outcome = ProviderCallOutcome.Success
                          LatencyMs = 1.0
                          TokenUsage = None
                          SessionId = None
                          PromptId = None
                          UserId = "user-1"
                          Subject = Some subject
                          Timestamp = DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero) }

                    Expect.equal record.UserId "user-1" "the tenant-side principal is untouched"

                    Expect.equal
                        (record.Subject |> Option.map _.Id)
                        (Some "svc-emission-runner")
                        "and the identity the call was made under sits beside it"
                } ]

          testList
              "the in-memory default sink is unaffected"
              [ test "a record carrying a subject round-trips through the in-memory sink" {
                    let subject =
                        match TelemetrySubject.create "user" "6f2a7c1e-9d3b-4f10-a2c5-8e7b0d41f9aa" with
                        | Ok s -> s
                        | Error reason -> failwithf "the fixture subject was refused: %s" reason

                    let record: ProviderCallTelemetry =
                        { ProviderId = "p"
                          ModelId = "m"
                          Operation = ProviderOperation.Emit
                          Outcome = ProviderCallOutcome.Success
                          LatencyMs = 1.0
                          TokenUsage = None
                          SessionId = None
                          PromptId = None
                          UserId = "user-1"
                          Subject = Some subject
                          Timestamp = DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero) }

                    let sink = Fuaran.UI.Telemetry.Default.InMemorySink()
                    (sink :> IFuaranTelemetrySink).RecordProviderCall record

                    Expect.equal
                        (sink.ProviderCallRecords |> List.map _.Subject)
                        [ Some subject ]
                        "the sink stores the record whole, subject included, with no code change of its own"
                } ] ]
