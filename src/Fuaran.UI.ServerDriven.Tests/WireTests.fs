module Fuaran.UI.ServerDriven.Tests.WireTests

// ─── Phase 152 Track B: DomPatch + ClientEffect wire encoding ───
//
// The shim's instruction set is a stable JSON contract. These pin the
// tagged-object camelCase shape (+ escaping + invariant-culture position)
// so a shim parser / host transport can rely on it.

open Expecto
open Fuaran.UI.ServerDriven

[<Tests>]
let domPatchTests =
    testList
        "DomPatch.encode"
        [ test "SetAttr / RemoveAttr" {
              Expect.equal
                  (DomPatch.encode (DomPatch.SetAttr("n1", "aria-label", "Save")))
                  """{"kind":"SetAttr","nodeId":"n1","name":"aria-label","value":"Save"}"""
                  "SetAttr shape"

              Expect.equal
                  (DomPatch.encode (DomPatch.RemoveAttr("n1", "disabled")))
                  """{"kind":"RemoveAttr","nodeId":"n1","name":"disabled"}"""
                  "RemoveAttr shape"
          }

          test "SetText escapes control characters" {
              Expect.equal
                  (DomPatch.encode (DomPatch.SetText("t", "a\"b\nc")))
                  """{"kind":"SetText","nodeId":"t","text":"a\"b\nc"}"""
                  "quotes + newline escaped"
          }

          test "ReplaceFragment / InsertFragment carry HTML; position is invariant-culture" {
              Expect.equal
                  (DomPatch.encode (DomPatch.ReplaceFragment("n", "<div>hi</div>")))
                  """{"kind":"ReplaceFragment","nodeId":"n","html":"<div>hi</div>"}"""
                  "ReplaceFragment shape"

              Expect.equal
                  (DomPatch.encode (DomPatch.InsertFragment("p", 2, "<span/>")))
                  """{"kind":"InsertFragment","parentId":"p","position":2,"html":"<span/>"}"""
                  "InsertFragment shape with numeric position"
          }

          test "RemoveNode / ReorderChildren" {
              Expect.equal
                  (DomPatch.encode (DomPatch.RemoveNode "gone"))
                  """{"kind":"RemoveNode","nodeId":"gone"}"""
                  "RemoveNode shape"

              Expect.equal
                  (DomPatch.encode (DomPatch.ReorderChildren("p", [ "b"; "a"; "c" ])))
                  """{"kind":"ReorderChildren","parentId":"p","orderedIds":["b","a","c"]}"""
                  "ReorderChildren id array in order"

              Expect.equal
                  (DomPatch.encode (DomPatch.MoveNode("x", "p2", 1)))
                  """{"kind":"MoveNode","nodeId":"x","newParentId":"p2","position":1}"""
                  "MoveNode (identity-preserving relocate) shape"
          }

          test "encodeList wraps a JSON array; kind discriminators are stable" {
              let patches = [ DomPatch.SetText("t", "x"); DomPatch.RemoveNode "y" ]

              Expect.equal
                  (DomPatch.encodeList patches)
                  """[{"kind":"SetText","nodeId":"t","text":"x"},{"kind":"RemoveNode","nodeId":"y"}]"""
                  "array of patches"

              Expect.equal (DomPatch.kind (DomPatch.SetAttr("a", "b", "c"))) "SetAttr" "kind discriminator"
              Expect.equal (DomPatch.encodeList []) "[]" "empty list"
          } ]

[<Tests>]
let clientEffectTests =
    testList
        "ClientEffect.encode"
        [ test "WriteToClipboard / Navigate / Focus" {
              Expect.equal
                  (ClientEffect.encode (ClientEffect.WriteToClipboard "copied"))
                  """{"kind":"WriteToClipboard","text":"copied"}"""
                  "clipboard"

              Expect.equal
                  (ClientEffect.encode (ClientEffect.Navigate "/reports/2026"))
                  """{"kind":"Navigate","route":"/reports/2026"}"""
                  "navigate"

              Expect.equal
                  (ClientEffect.encode (ClientEffect.Focus "field-1"))
                  """{"kind":"Focus","nodeId":"field-1"}"""
                  "focus"
          }

          test "Download / ReadFileBody" {
              Expect.equal
                  (ClientEffect.encode (ClientEffect.Download("/f.csv", "export.csv")))
                  """{"kind":"Download","url":"/f.csv","name":"export.csv"}"""
                  "download"

              Expect.equal
                  (ClientEffect.encode (ClientEffect.ReadFileBody("upload", "Base64")))
                  """{"kind":"ReadFileBody","nodeId":"upload","encoding":"Base64"}"""
                  "file read"
          }

          test "control characters: short forms for \\n \\r \\t, \\u00XX for the rest" {
              Expect.equal
                  (ClientEffect.encode (ClientEffect.WriteToClipboard "a\tb\nc"))
                  """{"kind":"WriteToClipboard","text":"a\tb\nc"}"""
                  "the three common controls keep their short escapes"

              // U+0001 / U+001F have no short form — emitting them raw would be
              // invalid JSON, so they must come out as \u00XX.
              Expect.equal
                  (ClientEffect.encode (ClientEffect.WriteToClipboard "a\u0001b\u001Fc"))
                  "{\"kind\":\"WriteToClipboard\",\"text\":\"a\\u0001b\\u001fc\"}"
                  "other C0 controls escape as \\u00XX, never raw"
          }

          test "encodeList wraps a JSON array; kind discriminators are stable" {
              Expect.equal
                  (ClientEffect.encodeList [ ClientEffect.Focus "a"; ClientEffect.Navigate "/x" ])
                  """[{"kind":"Focus","nodeId":"a"},{"kind":"Navigate","route":"/x"}]"""
                  "array of effects"

              Expect.equal
                  (ClientEffect.kind (ClientEffect.WriteToClipboard "x"))
                  "WriteToClipboard"
                  "kind discriminator"
          } ]
