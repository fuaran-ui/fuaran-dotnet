// Phase 2239 — a canonical member of the wrong type is a malformed member.
//
// THE PARITY TABLE. `fuaran-ts/packages/client/test/wireMember.test.ts` carries the
// same rows, in the same order, against the same entry points. The two clients live
// in separate repositories, so the table is held twice; a row changed in one is
// changed in the other in the same change-set, or the hosts stop agreeing on what a
// tolerant payload means.
//
// The rule: an alternate spelling is a fallback for an ABSENT canonical member (the
// key missing, or JSON `null`). A canonical member that is present and of the wrong
// type is malformed, so the alias is NOT read and the field reads as absent: the
// caller's existing absent-field handling applies.
//
// Each row: the field read, the members the body carries, and what the client
// reads (`-` is "absent"; a field with a default shows its default).

module Fuaran.UI.Client.Tests.WireMemberTests

open Expecto

open Fuaran.UI.Client

let private tree = "\"tree\":{\"id\":\"r\"}"

let private opt (v: string option) =
    match v with
    | Some s -> s
    | None -> "-"

let private detail (members: string) =
    match Wire.parseProducedDetail 200 $"{{{tree},{members}}}" with
    | Some d -> d
    | None -> failtestf "no produced detail for %s" members

let private produced (members: string) =
    match Wire.parseTurnResponse 200 $"{{{members}}}" with
    | TurnResult.Produced(treeJson, ops, version) -> Some(treeJson, ops, version)
    | _ -> None

let private failure (members: string) =
    match Wire.parseTurnResponse 422 $"{{{members}}}" with
    | TurnResult.TurnFailed err -> err
    | other -> failtestf "expected TurnFailed, got %A" other

let private read (field: string) (members: string) : string =
    match field with
    | "provider" -> (detail members).Provider |> opt
    | "servedModel" -> (detail members).ServedModel |> opt
    | "snapshot.state" ->
        (detail $"\"snapshot\":{{{members}}}").Snapshot
        |> Option.map (fun s -> s.State)
        |> opt
    | "snapshot.version" ->
        (detail $"\"snapshot\":{{{members}}}").Snapshot
        |> Option.bind (fun s -> s.Version)
        |> opt
    | "opsApplied" -> string (detail members).OpsApplied
    | "version" -> produced $"{tree},{members}" |> Option.map (fun (_, _, v) -> v) |> opt
    | "op.opId" ->
        produced $"{tree},{members}"
        |> Option.bind (fun (_, ops, _) -> List.tryHead ops)
        |> Option.map (fun o -> o.OpId)
        |> opt
    | "tree" -> produced members |> Option.map (fun (t, _, _) -> t) |> opt
    | "code" -> (failure members).Code
    | "message" -> (failure members).Message
    | "stage" -> TurnStage.label (failure members).Stage
    | "reason" ->
        match Wire.parseTurnResponse 401 $"{{{members}}}" with
        | TurnResult.AccessDenied reason -> reason
        | other -> failtestf "expected AccessDenied, got %A" other
    | other -> failtestf "unknown field %s" other

let private table: (string * string * string) list =
    [
      // A string member: canonical, alias, both, and the wrong-type rows.
      "provider", "\"provider\":\"a\"", "a"
      "provider", "\"Provider\":\"b\"", "b"
      "provider", "\"provider\":\"a\",\"Provider\":\"b\"", "a"
      "provider", "\"provider\":7,\"Provider\":\"b\"", "-"
      "provider", "\"provider\":{\"x\":1},\"Provider\":\"b\"", "-"
      "provider", "\"provider\":false,\"Provider\":\"b\"", "-"
      "provider", "\"provider\":7", "-"
      "provider", "\"provider\":\"a\",\"Provider\":7", "a"
      // A JSON null canonical is absent, so the alias is read.
      "provider", "\"provider\":null,\"Provider\":\"b\"", "b"
      "provider", "\"provider\":null", "-"
      "servedModel", "\"servedModel\":[],\"ServedModel\":\"m\"", "-"
      "servedModel", "\"servedModel\":null,\"ServedModel\":\"m\"", "m"
      // The snapshot members.
      "snapshot.state", "\"state\":\"s\"", "s"
      "snapshot.state", "\"State\":\"S\"", "S"
      "snapshot.state", "\"state\":7,\"State\":\"done\"", "-"
      "snapshot.state", "\"state\":null,\"State\":\"done\"", "done"
      "snapshot.version", "\"state\":\"s\",\"version\":7,\"Version\":\"2\"", "-"
      "snapshot.version", "\"state\":\"s\",\"version\":null,\"Version\":\"2\"", "2"
      // The applied-op count, and the op list it falls back to.
      "opsApplied", "\"opsApplied\":3", "3"
      "opsApplied", "\"OpsApplied\":2", "2"
      "opsApplied", "\"opsApplied\":null,\"OpsApplied\":2", "2"
      "opsApplied", "\"opsApplied\":\"3\",\"OpsApplied\":2,\"ops\":[{\"opId\":\"a\",\"opJson\":\"{}\"}]", "1"
      "opsApplied", "\"ops\":7,\"Ops\":[{\"opId\":\"a\",\"opJson\":\"{}\"}]", "0"
      "opsApplied", "\"ops\":null,\"Ops\":[{\"opId\":\"a\",\"opJson\":\"{}\"}]", "1"
      // An applied op's own members.
      "op.opId", "\"ops\":[{\"opId\":\"a\",\"opJson\":\"{}\"}]", "a"
      "op.opId", "\"ops\":[{\"opId\":7,\"OpId\":\"a\",\"opJson\":\"{}\"}]", ""
      "op.opId", "\"ops\":[{\"opId\":null,\"OpId\":\"a\",\"opJson\":\"{}\"}]", "a"
      // The version echo.
      "version", "\"version\":\"1.6.0\"", "1.6.0"
      "version", "\"version\":7,\"Version\":\"1.2.0\"", ""
      "version", "\"version\":null,\"Version\":\"1.2.0\"", "1.2.0"
      // The tree, which is an object or a string.
      "tree", "\"tree\":{\"id\":\"r\"}", "{\"id\":\"r\"}"
      "tree", "\"tree\":7,\"TreeJson\":\"{\\\"id\\\":\\\"r\\\"}\"", "-"
      "tree", "\"tree\":null,\"TreeJson\":\"{\\\"id\\\":\\\"r\\\"}\"", "{\"id\":\"r\"}"
      "tree", "\"tree\":\"  \",\"TreeJson\":\"{\\\"id\\\":\\\"r\\\"}\"", "-"
      // A refusal's members, inside the envelope.
      "code", "\"error\":{\"code\":\"A\"}", "A"
      "code", "\"error\":{\"code\":7,\"Code\":\"B\"}", "TURN_FAILED"
      "code", "\"error\":{\"code\":null,\"Code\":\"B\"}", "B"
      "message", "\"error\":{\"message\":7,\"Message\":\"m\"}", "the turn failed"
      "stage", "\"error\":{\"stage\":7,\"Stage\":\"parse\"}", "provider"
      "stage", "\"error\":{\"stage\":null,\"Stage\":\"parse\"}", "parse"
      // The envelope itself: a wrong-typed `error` is malformed, so the body is the envelope.
      "code", "\"error\":\"boom\",\"code\":\"Z\"", "Z"
      "code", "\"error\":7,\"Error\":{\"code\":\"Y\"},\"code\":\"Z\"", "Z"
      "code", "\"error\":null,\"Error\":{\"code\":\"Y\"}", "Y"
      // A 401's reason member.
      "reason", "\"Reason\":\"r\"", "r"
      "reason", "\"Reason\":7,\"reason\":\"r\"", "access denied"
      "reason", "\"Reason\":null,\"reason\":\"r\"", "r" ]

[<Tests>]
let wireMemberTests =
    testList
        "the wire member parity table"
        [ for field, members, expected in table ->
              test $"{field} reads {{{members}}} as {expected}" {
                  Expect.equal (read field members) expected "what the client reads"
              } ]
