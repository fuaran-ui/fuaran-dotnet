module Fuaran.UI.Tests.SchemaFormParity

// ============================================================================
//  Phase 1921 — the schema-to-form parity table, asserted on THIS host.
//
//  `fixtures/schema-form-parity.json` is a byte-copy of the TypeScript mirror's
//  paired table (fuaran-ts `packages/ui/test/fixtures/schema-form-parity.json`,
//  Phase 1914), pinned twice: by the SHA-256 below, so a silently edited copy
//  fails here, and by a `copies.json` record, so the copy and its source are
//  compared whichever side moves. The table's `wire` column is this host's
//  CANONICAL encoding (`CanonicalJson.encodeNode`) of each derived Form.
//
//  What is asserted is the bytes the AI tool (`fuaran.formFromSchema`) and the
//  CLI verb (`fuaran scaffold form`) return: both are `deriveWireFromText` and
//  nothing else (pinned in their own suites), so holding that function to the
//  table holds both front ends to it. Before this phase one class diverged — a
//  schema `default` equal to the control's own empty default — because the
//  derivation authored an explicit auto-binding the §16 projection erases, and
//  `deriveWire` encodes structurally.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.SchemaForm

/// The SHA-256 of the copy — the fuaran-ts table as of its Phase 1914 commit
/// (7fbcfb2). Re-certifying the table there means re-copying it here and
/// updating this pin in the same change.
[<Literal>]
let private FixtureSha256 =
    "2b8cf78663df397575ceed896c3c0f230ee0b91089ac21880b662a5c8045d303"

/// The table's case count at that commit. A table that silently lost cases
/// would otherwise pass every remaining one.
[<Literal>]
let private CaseCount = 81

let private fixturePath =
    Path.Combine(AppContext.BaseDirectory, "fixtures", "schema-form-parity.json")

type private Case =
    { Name: string
      Schema: string
      Outcome: string
      Wire: string }

let private cases: Lazy<Case list> =
    lazy
        (use doc = JsonDocument.Parse(File.ReadAllText fixturePath)

         let str (c: JsonElement) (name: string) : string =
             match c.GetProperty(name).GetString() with
             | null -> failwithf "parity case member '%s' is not a string" name
             | s -> s

         [ for c in doc.RootElement.GetProperty("cases").EnumerateArray() ->
               { Name = str c "name"
                 Schema = str c "schema"
                 Outcome = str c "outcome"
                 Wire = str c "wire" } ])

let private options = SchemaFormOptions.defaults<obj>

[<Tests>]
let parity =
    testList
        "SchemaForm parity table (Phase 1921)"
        [ test "the fixture is the pinned copy of the TypeScript table" {
              let digest =
                  Security.Cryptography.SHA256.HashData(File.ReadAllBytes fixturePath)
                  |> Convert.ToHexString

              Expect.equal (digest.ToLowerInvariant()) FixtureSha256 "fixture sha256"
              Expect.equal cases.Value.Length CaseCount "case count"
          }

          test "the tool/CLI bytes are the table's canonical bytes, every case" {
              let mismatches =
                  [ for c in cases.Value do
                        let outcome, wire =
                            match deriveWireFromText options c.Schema with
                            | Ok w -> "form", w
                            | Error w -> "refused", w

                        if outcome <> c.Outcome || wire <> c.Wire then
                            yield sprintf "%s:\n  table: %s %s\n  tool:  %s %s" c.Name c.Outcome c.Wire outcome wire ]

              Expect.isEmpty mismatches (String.Join("\n", mismatches))
          }

          test "every derived Form is already §16-canonical, so the structural encoder IS the canonical one" {
              // The invariant the bytes above rest on, asserted independently of the
              // table: `deriveWire` sits below the projection's layer and encodes with
              // `Generated.encodeNode`, which is canonical only over a canonical tree.
              let offenders =
                  [ for c in cases.Value do
                        match Json.parseTolerantOfNull c.Schema with
                        | Ok schema ->
                            match derive options schema with
                            | Ok node ->
                                // `Node` carries closures and has no equality, so the
                                // projection's identity is read through the bytes.
                                if
                                    Generated.encodeNode node
                                    <> Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode node
                                then
                                    yield c.Name
                            | Error _ -> ()
                        | Error _ -> () ]

              Expect.isEmpty offenders "derived trees the §16 projection would still rewrite"
          } ]
