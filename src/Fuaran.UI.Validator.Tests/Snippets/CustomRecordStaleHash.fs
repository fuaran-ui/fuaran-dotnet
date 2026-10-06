module Snippets.CustomRecordStaleHash

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let kind: NodeKind<unit> =
    NodeKind.Custom
        { ModuleId = "reporting"
          ComponentId = "Ring"
          Props = Map.ofList [ "value", JInt 3 ]
          ContentHash =
            Some
                { Algorithm = "SHA256"
                  Hash = "stale"
                  Strictness = HashStrictness.Enforced }
          ExposedNodeIds = None }
