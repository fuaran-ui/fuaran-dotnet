module Fuaran.UI.Program.Server.Tests.ServerEffectTests

// ─── The server placement's effect seam ──────────────────────────────
//
// The browser placement's registry earned three properties that are easy to get
// backwards, and this suite pins the same three for the server vocabulary:
//
//   1. the vocabulary is closed by REGISTRATION, not by policy — a permissive
//      gate cannot reach a capability no performer was registered for;
//   2. the gate runs BEFORE anything else, so no side effect can precede the
//      policy decision (pinned in the handler suite, where there is a performer
//      whose running can be observed);
//   3. a denial names the CAPABILITY and never the payload.
//
// The third is the one a future change is most likely to break by accident,
// which is why it is tested against a payload with a recognisable value rather
// than by reading the code.

open Expecto
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

/// A payload string chosen so a leak is unmistakable in an assertion message.
[<Literal>]
let private secret = "SECRET-PAYLOAD-VALUE"

[<Tests>]
let tests =
    testList
        "server effect seam"
        [ test "the vocabulary is closed and enumerable" {
              let sample =
                  [ ServerEffect.RunQuery(
                        "rows",
                        Fuaran.Core.Embedded({ Schema = []; Columns = [] }: Fuaran.Core.Table),
                        []
                    )
                    ServerEffect.ApplyOps []
                    ServerEffect.HostCall("fn", Fuaran.Core.JStr secret, None)
                    ServerEffect.EmitPatch []
                    ServerEffect.Notify("channel", Fuaran.Core.JStr secret)
                    ServerEffect.Report
                        { Code = "code"
                          Severity = "warning"
                          Message = secret } ]

              Expect.equal
                  (sample |> List.map ServerEffect.kind)
                  ServerEffect.kinds
                  "every arm's discriminator is in the enumerated vocabulary, in order"

              Expect.equal (List.length ServerEffect.kinds) 6 "the vocabulary is the six declared arms"
          }

          test "a host call's capability is namespaced away from the built-in arms" {
              // A host function named after a built-in arm must not be
              // permitted by a gate rule about that arm — the two namespaces
              // are disjoint by construction, not by a host remembering to keep
              // them apart.
              let collidingName =
                  ServerEffect.capability (ServerEffect.HostCall("ApplyOps", Fuaran.Core.JObj [], None))

              Expect.equal collidingName "host:ApplyOps" "a host function is namespaced under host:"

              Expect.notEqual
                  collidingName
                  (ServerEffect.capability (ServerEffect.ApplyOps []))
                  "and so can never be confused with the built-in arm of the same name"
          }

          test "the default registry refuses everything" {
              let registry = ServerEffectRegistry.denyAll

              Expect.isFalse (registry.Gate "RunQuery") "the default gate refuses a read"
              Expect.isFalse (registry.Gate "ApplyOps") "and a mutation"
              Expect.isEmpty (ServerEffectRegistry.registered registry) "and no performer is registered"
          }

          test "registration does not permit, and permission does not register" {
              let registered =
                  ServerEffectRegistry.denyAll
                  |> ServerEffectRegistry.register "audit" (fun _ -> Ok(Fuaran.Core.JObj []))

              Expect.isFalse
                  (registered.Gate "host:audit")
                  "registering a performer leaves the gate exactly where it was"

              let permitted = ServerEffectRegistry.permissive ServerEffectRegistry.denyAll

              Expect.isEmpty
                  (ServerEffectRegistry.registered permitted)
                  "and opening the gate registers nothing — the vocabulary is closed by registration"
          }

          test "a denial describes the capability and never the payload" {
              let denials =
                  [ ServerEffectDenial.Unregistered "host:audit"
                    ServerEffectDenial.GateRefused "Notify" ]

              for denial in denials do
                  let text = ServerEffectDenial.describe denial

                  Expect.isFalse (text.Contains secret) $"a denial description must not carry a payload: {text}"

              Expect.stringContains
                  (ServerEffectDenial.describe (ServerEffectDenial.Unregistered "host:audit"))
                  "host:audit"
                  "but it does name the capability, which is the fact a host needs"

              Expect.notEqual
                  (ServerEffectDenial.describe (ServerEffectDenial.Unregistered "Notify"))
                  (ServerEffectDenial.describe (ServerEffectDenial.GateRefused "Notify"))
                  "and the two arms read differently — absent capability and refused use are different facts"
          }

          // ── Phase 1981: an op performer's receipt is checked like a host call's result ──

          test
              "an op contract admits a receipt that holds, refuses one that does not NAMING THE CONTRACT, and passes a raw refusal through" {
              let contract: OpContract<string, string> =
                  OpContract.at "names-the-op" (fun state op receipt ->
                      receipt.Detail = Fuaran.Core.JStr(state + "/" + op))

              // Phase 2165 (Program D39): a performer and a contract are handed the run's prefix
              // first; these ignore it, so each is checked at the prefix of a run's first op.
              let prefix = OpPrefix.atEntry "planned"

              let honest: OpPrefix<string> -> string -> string -> Result<OpReceipt, string> =
                  fun _ state op -> Ok(OpReceipt.ofDetail (Fuaran.Core.JStr(state + "/" + op)))

              let overreaching: OpPrefix<string> -> string -> string -> Result<OpReceipt, string> =
                  fun _ _ _ -> Ok(OpReceipt.ofDetail (Fuaran.Core.JStr secret))

              let refusing: OpPrefix<string> -> string -> string -> Result<OpReceipt, string> =
                  fun _ _ _ -> Error "the world refused"

              Expect.equal
                  (OpContract.check contract honest prefix "planned" "op")
                  (Ok(OpReceipt.ofDetail (Fuaran.Core.JStr "planned/op")))
                  "a receipt the contract holds of is the receipt, unchanged"

              Expect.equal
                  (OpContract.check contract overreaching prefix "planned" "op")
                  (Error "return-contract:names-the-op")
                  "a receipt the contract rejects is the host's own refusal, naming the contract"

              Expect.equal
                  (OpContract.describe contract)
                  (ReturnContract.describe
                      { Name = contract.Name
                        Holds = fun _ -> true })
                  "in the ONE vocabulary a return contract's refusal has — the op axis coins no second prefix"

              match OpContract.check contract overreaching prefix "planned" "op" with
              | Error reason -> Expect.isFalse (reason.Contains secret) "and the refusal never carries the receipt"
              | Ok _ -> failtest "the rejected receipt was admitted"

              Expect.equal
                  (OpContract.check contract refusing prefix "planned" "op")
                  (Error "the world refused")
                  "a raw refusal passes through with the performer's own reason — the contract is not consulted"
          }

          test
              "the contract is handed the planned state and the op, and performedChecked composes it into the performer" {
              let seen = ResizeArray<string * string>()

              let contract: OpContract<string, string> =
                  OpContract.at "recording" (fun state op _ ->
                      seen.Add(state, op)
                      true)

              match OpPerformance.performedChecked [ contract ] (fun _ _ _ -> Ok OpReceipt.none) with
              | OpPerformance.Performed perform ->
                  Expect.equal
                      (perform (OpPrefix.atEntry "planned") "planned" "write")
                      (Ok OpReceipt.none)
                      "the composed performer answers the receipt the raw one did"

                  Expect.equal
                      (List.ofSeq seen)
                      [ "planned", "write" ]
                      "and the contract saw the state and the op the performer was handed"
              | OpPerformance.InMemory -> failtest "a checked performer is a registered performer"

              match OpPerformance.performedWithoutReceipt (fun (_: string) (_: string) -> Ok()) with
              | OpPerformance.Performed perform ->
                  Expect.equal
                      (perform (OpPrefix.atEntry "planned") "planned" "write")
                      (Ok OpReceipt.none)
                      "a performer with nothing to say answers the empty receipt: no write, the inert empty detail"
              | OpPerformance.InMemory -> failtest "a receipt-less performer is still a registered performer"
          } ]
