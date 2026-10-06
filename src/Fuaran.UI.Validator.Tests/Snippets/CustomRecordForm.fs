module Snippets.CustomRecordForm

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

/// The generated case carries a `CustomSpec` record: no hash (FUARAN055),
/// interior ids with no registered renderer (FUARAN053).
let kind: NodeKind<unit> =
    NodeKind.Custom
        { ModuleId = "reporting"
          ComponentId = "Ring"
          Props = Map.empty
          ContentHash = None
          ExposedNodeIds = Some [ "ring-centre" ] }
