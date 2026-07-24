module Fuaran.UI.AiTools.Tests.Capabilities

#nowarn "3261"

// Phase 283 — the capability discovery + dispatch glue (Fuaran.UI.AiTools.Capabilities). The
// registry / arg-validation / replay laws are certified Fuaran.Core-side (capabilityLaws); this
// asserts the thin host glue: discovery enumerates, validation names refusals, makeInvoker maps to
// the Deferred async envelope.

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.AiTools

let private mkCap (id: string) (det: Fuaran.Core.DeterminismSource) : Fuaran.Core.Capability =
    let sg: Fuaran.Core.Signature =
        { Name = id
          Holes =
            [ { Addr = "x"
                Name = "x"
                Kind = "value"
                Space = Some(Fuaran.Core.IntRange(0, 10))
                Slot = None
                Action = None
                Required = true } ]
          Effect =
            { Host = Fuaran.Core.ReadsHost
              Determinism = det } }

    Fuaran.Core.Capability.create id sg Fuaran.Core.Server

let private registry =
    Fuaran.Core.Registry.empty
    |> Fuaran.Core.Registry.register (mkCap "forecast" Fuaran.Core.Deterministic)
    |> Result.bind (Fuaran.Core.Registry.register (mkCap "score" Fuaran.Core.Random))
    |> function
        | Ok r -> r
        | Error e -> failwithf "registry build failed: %A" e

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
              | other -> failtestf "expected Error, got %A" other ]
