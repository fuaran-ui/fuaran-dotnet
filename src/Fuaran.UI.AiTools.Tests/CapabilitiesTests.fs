module Fuaran.UI.AiTools.Tests.Capabilities

#nowarn "3261"

// Phase 283 — the capability discovery + dispatch glue (Fuaran.UI.AiTools.Capabilities). The
// registry / arg-validation / replay laws are certified Fuaran.Core-side (capabilityLaws); this
// asserts the thin host glue: discovery enumerates, validation names refusals, makeInvoker maps to
// the Deferred async envelope.

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.AiTools

module CoreConf = Fuaran.Core.Conformance

/// Core's settled/pending/failed envelope, qualified: `Failed` alone also names a resolver fault.
type private CoreDeferred = Fuaran.Core.Deferred<obj>

let private mkCap (id: string) (det: Fuaran.Core.DeterminismSource) : Fuaran.Core.Capability =
    let sg: Fuaran.Core.Signature =
        { Name = id
          Holes =
            [ { Addr = "x"
                Name = "x"
                Kind = Fuaran.Core.ValueHole(Fuaran.Core.IntRange(0, 10))
                Required = true } ]
          Effect =
            { Host = Fuaran.Core.ReadsHost
              Determinism = det } }

    Fuaran.Core.Capability.create id sg Fuaran.Core.Server

let private registry =
    Fuaran.Core.CapabilityRegistry.empty
    |> Fuaran.Core.CapabilityRegistry.register (mkCap "forecast" Fuaran.Core.Effect.deterministic)
    |> Result.bind (Fuaran.Core.CapabilityRegistry.register (mkCap "score" Fuaran.Core.Effect.random))
    |> function
        | Ok r -> r
        | Error e -> failwithf "registry build failed: %A" e

/// A registry like `registry`, gated by one policy that answers `decision` for every call and told
/// of every refusal through `told`.
let private gated (decision: Fuaran.Core.PolicyDecision) (told: ResizeArray<string>) =
    registry
    |> Fuaran.Core.CapabilityRegistry.withGate
        { Policy = "kiosk-policy"
          Decide = fun _ _ -> decision }
    |> Fuaran.Core.CapabilityRegistry.onDenied (fun denial -> told.Add denial.Id)

let private assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
    match results |> List.filter (fun r -> not r.Passed) with
    | [] -> ()
    | failures ->
        failures
        |> List.map (fun r -> sprintf "  %s - %A" r.Law r.Counterexample)
        |> String.concat "\n"
        |> failtestf "%s failed:\n%s" context

/// The calls a model could make against `registry`: registered and invented ids, an argument in
/// space, out of space, missing and stray.
let private genCall (rng: Fuaran.Core.ConfRng.T) =
    let id, rng = Fuaran.Core.ConfRng.choose [ "forecast"; "score"; "ghost" ] rng
    let shape, rng = Fuaran.Core.ConfRng.intBelow 6 rng
    let x, rng = Fuaran.Core.ConfRng.intBelow 11 rng

    let args =
        match shape with
        | 0
        | 1
        | 2 -> [ "x", string x ]
        | 3 -> [ "x", "99" ]
        | 4 -> []
        | _ -> [ "x", string x; "stray", "1" ]

    (id, args), rng

[<Tests>]
let tests =
    testList
        "Capabilities"
        [ testCase "discover enumerates capabilities (id-sorted) with their signature schema"
          <| fun _ ->
              let entries = Capabilities.discover registry
              Expect.equal (entries |> List.map fst) [ "forecast"; "score" ] "id-sorted discovery"
              // each entry carries the JSON-Schema projection of the signature (a typed-args object)
              Expect.isTrue (entries |> List.forall (fun (_, schema) -> schema <> Fuaran.Core.JStr "")) "schema present"

          testCase "validate accepts in-space args + names every refusal"
          <| fun _ ->
              match Capabilities.validate registry "forecast" [ "x", "5" ] with
              | Ok cap -> Expect.equal cap.Id "forecast" "resolved + validated"
              | Error e -> failtestf "expected Ok, got %A" e

              match Capabilities.validate registry "forecast" [ "x", "99" ] with
              | Error(Fuaran.Core.ArgOutOfSpace _) -> ()
              | other -> failtestf "expected ArgOutOfSpace, got %A" other

              match Capabilities.validate registry "ghost" [] with
              | Error(Fuaran.Core.NoSuchCapability("ghost", _)) -> ()
              | other -> failtestf "expected NoSuchCapability, got %A" other

          testCase "makeInvoker validates then dispatches to the host body, mapping to Deferred"
          <| fun _ ->
              let invoker =
                  Capabilities.makeInvoker registry (fun cap _args -> Deferred.Ready(box (cap.Id + "!")))

              match invoker "forecast" [ "x", "5" ] with
              | Deferred.Ready v -> Expect.equal (unbox<string> v) "forecast!" "body ran on a valid invocation"
              | other -> failtestf "expected Ready, got %A" other

              // an ill-typed invocation short-circuits to Deferred.Error before the body runs
              match invoker "forecast" [ "x", "99" ] with
              | Deferred.Error _ -> ()
              | other -> failtestf "expected Error, got %A" other

          // Phase 2311 - the invoker dispatches through the registry's own `dispatch`, so the
          // registry's policy gate runs: a call the policy refuses never reaches the host body.
          testCase "a policy that denies the capability refuses the call and the host body never runs"
          <| fun _ ->
              let told = ResizeArray<string>()
              let mutable ran = 0

              let invoker =
                  Capabilities.makeInvoker
                      (gated (Fuaran.Core.PolicyDecision.deny "kiosk is read-only") told)
                      (fun cap _ ->
                          ran <- ran + 1
                          Deferred.Ready(box cap.Id))

              match invoker "forecast" [ "x", "5" ] with
              | Deferred.Error m ->
                  Expect.stringContains m "kiosk-policy" "the refusal names the gate"
                  Expect.stringContains m "kiosk is read-only" "the refusal carries the gate's reason"
              | other -> failtestf "expected a policy refusal, got %A" other

              Expect.equal ran 0 "the body never ran"
              Expect.equal (List.ofSeq told) [ "forecast" ] "the denial reached the registry's observer"

          testCase "a policy that parks the capability for approval surfaces the typed refusal, not a silent run"
          <| fun _ ->
              let told = ResizeArray<string>()
              let mutable ran = 0
              let parked = gated Fuaran.Core.PolicyDecision.NeedsApproval told

              let invoker =
                  Capabilities.makeInvoker parked (fun cap _ ->
                      ran <- ran + 1
                      Deferred.Ready(box cap.Id))

              match invoker "forecast" [ "x", "5" ] with
              | Deferred.Error m -> Expect.stringContains m "requires an approval" "the refusal says approval is needed"
              | other -> failtestf "expected an approval refusal, got %A" other

              Expect.equal ran 0 "the body never ran"

              match Capabilities.invoke parked "forecast" [ "x", "5" ] (fun _ _ -> Deferred.Ready(box 1)) with
              | Error(Fuaran.Core.ApprovalRequired "kiosk-policy") -> ()
              | other -> failtestf "expected ApprovalRequired, got %A" other

          testCase "a policy that allows the call lets the body run, and validation refuses before any gate"
          <| fun _ ->
              let told = ResizeArray<string>()

              match
                  Capabilities.makeInvoker
                      (gated Fuaran.Core.PolicyDecision.Allow told)
                      (fun cap _ -> Deferred.Ready(box cap.Id))
                      "forecast"
                      [ "x", "5" ]
              with
              | Deferred.Ready v -> Expect.equal (unbox<string> v) "forecast" "an allowed call reaches the body"
              | other -> failtestf "expected Ready, got %A" other

              let denying = gated (Fuaran.Core.PolicyDecision.deny "no") told

              match Capabilities.invoke denying "forecast" [ "x", "99" ] (fun _ _ -> Deferred.Ready(box 1)) with
              | Error(Fuaran.Core.ArgOutOfSpace _) -> ()
              | other -> failtestf "expected validation to refuse first, got %A" other

              Expect.isEmpty (List.ofSeq told) "a validation refusal is not a policy refusal"

          testCase "a host body that fails surfaces its own message through onError"
          <| fun _ ->
              match
                  Capabilities.makeInvoker registry (fun _ _ -> Deferred.Error "upstream down") "forecast" [ "x", "5" ]
              with
              | Deferred.Error m -> Expect.equal m "upstream down" "the body's message is not wrapped"
              | other -> failtestf "expected Error, got %A" other

          // Phase 2311 - the host dispatch path, certified at the tier's own seam.
          testCase "the capability invoker's dispatch certifies under Core's capabilityLawsAt (Capabilities.invoke)"
          <| fun _ ->
              let witness: Fuaran.Core.CapabilitySeamWitness<obj> =
                  { Registry = registry
                    Dispatch =
                      fun id args body ->
                          Capabilities.invoke registry id args (fun cap _ ->
                              match body cap () with
                              | CoreDeferred.Ready v -> Deferred.Ready v
                              | CoreDeferred.Pending -> Deferred.Pending
                              | CoreDeferred.Failed m -> Deferred.Error m)
                    GenCall = genCall }

              // x = 4 stays pending, x = 7 fails in the body, every other in-space value settles.
              let body (args: (string * string) list) (cap: Fuaran.Core.Capability) : Fuaran.Core.Deferred<obj> =
                  match args |> List.tryFind (fun (a, _) -> a = "x") with
                  | Some(_, "4") -> CoreDeferred.Pending
                  | Some(_, "7") -> CoreDeferred.Failed "seven is unlucky"
                  | _ -> CoreDeferred.Ready(box cap.Id)

              CoreConf.capabilityLawsAt witness body 20261021 200
              |> assertAllPassed "capabilityLawsAt over Capabilities.invoke"

          testCase "the registry's policy gate certifies under Core's policyLaws"
          <| fun _ ->
              // Self-contained over the kit's registries: it certifies the pinned gate that the
              // tests above show `makeInvoker` now reaches, not the tier's own code.
              CoreConf.policyLaws 20261021 100
              |> assertAllPassed "policyLaws over the pinned Fuaran.Core.Function"

          // Phase 1817 — the host emission budget as a capability report: a model ASKS before it emits.
          testCase "emissionLimits reports each declared limit with the code its breach is refused with"
          <| fun _ ->
              let limits =
                  { Fuaran.UI.HostLimits.named "kiosk" with
                      MaxNodes = Some 400
                      MaxSerializedBytes = Some 65536 }

              let expected =
                  Fuaran.Core.JObj
                      [ "identity", Fuaran.Core.JStr "kiosk"
                        "limits",
                        Fuaran.Core.JArr
                            [ Fuaran.Core.JObj
                                  [ "limit", Fuaran.Core.JStr "maxNodes"
                                    "value", Fuaran.Core.JInt 400
                                    "code", Fuaran.Core.JStr "FUARAN158" ]
                              Fuaran.Core.JObj
                                  [ "limit", Fuaran.Core.JStr "maxSerializedBytes"
                                    "value", Fuaran.Core.JInt 65536
                                    "code", Fuaran.Core.JStr "FUARAN162" ] ] ]

              Expect.equal (Capabilities.emissionLimits limits) expected "declared limits only, in record order"

          testCase "emissionLimits over the unbounded default reports no limits, not zero limits"
          <| fun _ ->
              Expect.equal
                  (Capabilities.emissionLimits Fuaran.UI.HostLimits.unbounded)
                  (Fuaran.Core.JObj [ "identity", Fuaran.Core.JStr "unbounded"; "limits", Fuaran.Core.JArr [] ])
                  "an empty list means no stated limit"

          testCase "emissionLimits over a preset states every limit it declares"
          <| fun _ ->
              match Capabilities.emissionLimits Fuaran.UI.HostLimits.email with
              | Fuaran.Core.JObj [ "identity", Fuaran.Core.JStr "email"; "limits", Fuaran.Core.JArr entries ] ->
                  Expect.equal entries.Length 5 "the email preset declares all five limits"
              | other -> failtestf "unexpected report shape %A" other ]
