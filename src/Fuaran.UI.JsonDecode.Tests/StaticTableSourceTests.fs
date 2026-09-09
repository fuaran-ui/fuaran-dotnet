module Fuaran.UI.JsonDecode.Tests.StaticTableSourceTests

// ============================================================================
//  A static table's grid SOURCE encodes as `{"$type":"Static","value":[]}`.
//
//  Phase 286's cross-host tour found `Fuaran.table` encoding the rows-absent
//  grid source as `{"$type":"Static"}` where the corpus (`nodes/table-1.json`)
//  and `@fuaran-ui/ops` both say `…,"value":[]` — live at 0.70.0, and re-checked
//  against the raised pin, so not version skew.
//
//  The encoder was never wrong: it faithfully encoded two DIFFERENT values,
//  `Binding.Static None` (absent) and `Binding.Static (Some [])` (empty). What
//  was wrong is which the facade built. Phase 1647 changed the facade.
//
//  Why nothing caught it, and why this test is shaped as it is: `table-1` is
//  hand-built from the record and never goes through the facade, so the corpus —
//  the estate's own oracle — could not see the divergence, and moving the facade
//  moves no corpus byte. The assertion therefore has to compare the FACADE's
//  output against the corpus's, which is exactly the join no existing test made.
// ============================================================================

open System.IO
open Expecto
open Fuaran.UI

module CanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

[<Tests>]
let tests =
    testList
        "Phase 1647 — the facade's static table agrees with the corpus"
        [ test "Fuaran.table encodes its grid source the way nodes/table-1.json does" {
              let wire: string =
                  CanonicalJson.encodeNode (
                      Fuaran.table
                          "facade-table"
                          { Defaults.table with
                              Headers = [ Types.TextSource.Literal "Term" ]
                              Rows = [ [ Types.TextSource.Literal "MVU" ] ] }
                  )

              Expect.stringContains
                  wire
                  "\"source\":{\"$type\":\"Static\",\"value\":[]}"
                  "a static table's grid source is the EMPTY collection, not the absent one. `{\"$type\":\"Static\"}` is what `Binding.Static None` encodes to, and it is not what the corpus or @fuaran-ui/ops carry"

              // And the same shape the corpus actually holds, read from the
              // corpus rather than restated — a literal here would agree with
              // itself forever if the corpus moved.
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> ()
              | Some root ->
                  let fixture = Path.Combine(root, "nodes", "table-1.json")

                  if File.Exists fixture then
                      Expect.stringContains
                          (File.ReadAllText fixture)
                          "\"source\":{\"$type\":\"Static\",\"value\":[]}"
                          "nodes/table-1.json is the shape this facade is being held to; if IT moved, this test is the wrong way round and the corpus is the thing to read"
          } ]
