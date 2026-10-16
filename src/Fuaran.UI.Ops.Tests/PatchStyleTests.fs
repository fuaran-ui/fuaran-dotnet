module Fuaran.UI.Tests.PatchStyle

// ============================================================================
//  Phase 2291 — `TreeOp.PatchStyle`, the field-level style edit.
//
//  Three claims, each pinned here:
//   (1) the APPLY is a patch over the node's current style — the fields the
//       delta names are set, every other field is carried, and a default
//       result normalises to the absent slot exactly as `UpdateStyle` does;
//   (2) two patches naming DIFFERENT fields of one node commute — applied in
//       either order they reach the same tree, which is the property a fold
//       that derives a concurrent restyle depends on;
//   (3) the wire tokens `StyleDelta.wireMembers` spells are the generated
//       `SemanticStyle` encoder's own, and the codec round-trips a delta
//       byte-identically with a default token spelled rather than omitted.
// ============================================================================

open Expecto
open FsCheck
open FsCheck.FSharp
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

type private Msg = | NoOp

let private leaf (id: string) : Node<Msg> = Fuaran.markdown id "text"

let private tree (style: SemanticStyle option) : Node<Msg> =
    let target = { leaf "m" with Style = style }

    Fuaran.box
        "root"
        { Defaults.box<Msg> with
            Children = [ target; leaf "other" ] }

let private styleOf (root: Node<Msg>) : SemanticStyle option =
    match Introspect.findNode (NodeId "m") root with
    | Some n -> n.Style
    | None -> failtest "target vanished"

let private applyOk (op: TreeOp<Msg>) (root: Node<Msg>) : Node<Msg> =
    match Apply.apply op root with
    | Ok t -> t
    | Error e -> failtestf "apply refused: %A" e

let private toned =
    { Defaults.style with
        Tone = ToneVariant.Brand
        Weight = StyleWeight.Spacious }

// The generated encoder's token for a style field: encode a record carrying
// that value and read the member back. Only meaningful for a NON-default value
// (the encoder omits defaults), which is the half this pins against.
let private generatedToken (style: SemanticStyle) (memberName: string) : string option =
    match Generated.encodeSemanticStyleJson style with
    | JObj fields ->
        fields
        |> List.tryPick (fun (k, v) ->
            match k, v with
            | k, JStr s when k = memberName -> Some s
            | _ -> None)
    | _ -> None

let private every<'T> (cases: 'T list) = cases

let private styleGen: Gen<SemanticStyle> =
    gen {
        let! direction = Gen.elements [ TextDirection.Auto; TextDirection.Ltr; TextDirection.Rtl ]
        let! emphasis = Gen.elements [ Emphasis.Quiet; Emphasis.Normal; Emphasis.Loud ]

        let! role =
            Gen.elements
                [ StyleRole.None
                  StyleRole.Eyebrow
                  StyleRole.Data
                  StyleRole.Lede
                  StyleRole.Caption ]

        let! tone =
            Gen.elements
                [ ToneVariant.Default
                  ToneVariant.Subdued
                  ToneVariant.Brand
                  ToneVariant.Success
                  ToneVariant.Warning
                  ToneVariant.Critical
                  ToneVariant.Info ]

        let! voice = Gen.elements [ FontVoice.Default; FontVoice.Display; FontVoice.Structural ]
        let! weight = Gen.elements [ StyleWeight.Compact; StyleWeight.Standard; StyleWeight.Spacious ]

        return
            { Direction = direction
              Emphasis = emphasis
              Role = role
              Tone = tone
              Voice = voice
              Weight = weight }
    }

[<Tests>]
let tests =
    testList
        "Phase 2291 — PatchStyle, the field-level style edit"
        [ test "a patch sets the fields it names and carries every other field" {
              let after =
                  applyOk
                      (TreeOp.PatchStyle(
                          NodeId "m",
                          { StyleDelta.empty with
                              Voice = Some FontVoice.Display }
                      ))
                      (tree (Some toned))

              Expect.equal
                  (styleOf after)
                  (Some { toned with Voice = FontVoice.Display })
                  "voice set; tone and weight carried, unlike a whole-record UpdateStyle"
          }

          test "a patch over an absent style is a patch over the default record" {
              let after =
                  applyOk
                      (TreeOp.PatchStyle(
                          NodeId "m",
                          { StyleDelta.empty with
                              Tone = Some ToneVariant.Info }
                      ))
                      (tree None)

              Expect.equal
                  (styleOf after)
                  (Some
                      { Defaults.style with
                          Tone = ToneVariant.Info })
                  "default + tone"
          }

          test "a patch whose result is the default record stores the absent slot" {
              let after =
                  applyOk
                      (TreeOp.PatchStyle(
                          NodeId "m",
                          { StyleDelta.empty with
                              Tone = Some ToneVariant.Default
                              Weight = Some StyleWeight.Standard }
                      ))
                      (tree (Some toned))

              Expect.isNone (styleOf after) "normalised to None, exactly as UpdateStyle normalises"
          }

          test "an in-process empty delta is the identity" {
              let before = tree (Some toned)
              let after = applyOk (TreeOp.PatchStyle(NodeId "m", StyleDelta.empty)) before
              Expect.equal (CanonicalJson.encodeNode after) (CanonicalJson.encodeNode before) "nothing moved"
          }

          test "an unknown target is NodeNotFound" {
              match
                  Apply.apply
                      (TreeOp.PatchStyle(
                          NodeId "nope",
                          { StyleDelta.empty with
                              Tone = Some ToneVariant.Info }
                      ))
                      (tree None)
              with
              | Error e -> Expect.equal e.Code ApplyErrorCode.NodeNotFound "the UpdateStyle code"
              | Ok _ -> failtest "a patch of an absent node applied"
          }

          test "two patches of DIFFERENT fields commute (either order, one tree)" {
              let tone =
                  TreeOp.PatchStyle(
                      NodeId "m",
                      { StyleDelta.empty with
                          Tone = Some ToneVariant.Brand }
                  )

              let voice =
                  TreeOp.PatchStyle(
                      NodeId "m",
                      { StyleDelta.empty with
                          Voice = Some FontVoice.Display }
                  )

              let start = tree None
              let ab = start |> applyOk tone |> applyOk voice
              let ba = start |> applyOk voice |> applyOk tone

              Expect.equal (CanonicalJson.encodeNode ab) (CanonicalJson.encodeNode ba) "order-independent"

              Expect.equal
                  (styleOf ab)
                  (Some
                      { Defaults.style with
                          Tone = ToneVariant.Brand
                          Voice = FontVoice.Display })
                  "both edits survive — the blend a whole-record op could not record"
          }

          test "the same pair as whole-record UpdateStyles does NOT commute (the defect the op closes)" {
              let tone =
                  TreeOp.UpdateStyle(
                      NodeId "m",
                      { Defaults.style with
                          Tone = ToneVariant.Brand }
                  )

              let voice =
                  TreeOp.UpdateStyle(
                      NodeId "m",
                      { Defaults.style with
                          Voice = FontVoice.Display }
                  )

              let start = tree None
              let ab = start |> applyOk tone |> applyOk voice
              let ba = start |> applyOk voice |> applyOk tone

              Expect.notEqual (CanonicalJson.encodeNode ab) (CanonicalJson.encodeNode ba) "last writer wins"
          }

          testCase "between is the patch that takes one style to another" (fun () ->
              let pairs = Gen.zip styleGen styleGen

              Check.One(
                  Config.QuickThrowOnFailure.WithMaxTest(500),
                  Prop.forAll (Arb.fromGen pairs) (fun (a, b) ->
                      let d = StyleDelta.between a b

                      let named = StyleDelta.fieldNames d |> List.length
                      let reaches = StyleDelta.applyTo a d = b
                      let emptyIffEqual = StyleDelta.isEmpty d = (a = b)
                      let totalIffSix = StyleDelta.isTotal d = (named = 6)
                      reaches && emptyIffEqual && totalIffSix)
              ))

          test "isTotal holds exactly when every field is set" {
              let all =
                  { Direction = Some TextDirection.Rtl
                    Emphasis = Some Emphasis.Quiet
                    Role = Some StyleRole.Caption
                    Tone = Some ToneVariant.Info
                    Voice = Some FontVoice.Structural
                    Weight = Some StyleWeight.Compact }

              Expect.isTrue (StyleDelta.isTotal all) "all six"
              Expect.isFalse (StyleDelta.isTotal { all with Role = None }) "five of six"

              Expect.equal
                  (StyleDelta.fieldNames all)
                  [ "direction"; "emphasis"; "role"; "tone"; "voice"; "weight" ]
                  "the pinned alphabetical member order"
          }

          test "every non-default token is the generated SemanticStyle encoder's own" {
              let d = Defaults.style

              let pairs: (SemanticStyle * StyleDelta * string) list =
                  [ for v in every [ TextDirection.Ltr; TextDirection.Rtl ] do
                        { d with Direction = v },
                        { StyleDelta.empty with
                            Direction = Some v },
                        "direction"
                    for v in every [ Emphasis.Quiet; Emphasis.Loud ] do
                        { d with Emphasis = v },
                        { StyleDelta.empty with
                            Emphasis = Some v },
                        "emphasis"
                    for v in every [ StyleRole.Eyebrow; StyleRole.Data; StyleRole.Lede; StyleRole.Caption ] do
                        { d with Role = v }, { StyleDelta.empty with Role = Some v }, "role"
                    for v in
                        every
                            [ ToneVariant.Subdued
                              ToneVariant.Brand
                              ToneVariant.Success
                              ToneVariant.Warning
                              ToneVariant.Critical
                              ToneVariant.Info ] do
                        { d with Tone = v }, { StyleDelta.empty with Tone = Some v }, "tone"
                    for v in every [ FontVoice.Display; FontVoice.Structural ] do
                        { d with Voice = v }, { StyleDelta.empty with Voice = Some v }, "voice"
                    for v in every [ StyleWeight.Compact; StyleWeight.Spacious ] do
                        { d with Weight = v },
                        { StyleDelta.empty with
                            Weight = Some v },
                        "weight" ]

              for style, delta, memberName in pairs do
                  Expect.equal
                      (StyleDelta.wireMembers delta
                       |> List.tryPick (fun (k, v) -> if k = memberName then Some v else None))
                      (generatedToken style memberName)
                      (sprintf "%s token for %A" memberName delta)
          }

          test "every DEFAULT token decodes back to the default (the half the encoder cannot show)" {
              let defaults =
                  { Direction = Some TextDirection.Auto
                    Emphasis = Some Emphasis.Normal
                    Role = Some StyleRole.None
                    Tone = Some ToneVariant.Default
                    Voice = Some FontVoice.Default
                    Weight = Some StyleWeight.Standard }

              let op: TreeOp<obj> = TreeOp.PatchStyle(NodeId "m", defaults)
              let wire = CanonicalJson.encodeOp op

              Expect.equal
                  wire
                  """{"$type":"PatchStyle","style":{"direction":"auto","emphasis":"Normal","role":"None","tone":"Default","voice":"Default","weight":"Standard"},"target":"m"}"""
                  "a default token in a delta is spelled, never omitted"

              match JsonDecode.decodeOp wire with
              | Ok(TreeOp.PatchStyle(NodeId "m", back)) -> Expect.equal back defaults "decodes to the same delta"
              | other -> failtestf "did not decode to the same PatchStyle: %A" other
          }

          test "an empty patch is refused at decode, WRONG_TYPE at $.style" {
              match JsonDecode.decodeOp """{"$type":"PatchStyle","style":{},"target":"m"}""" with
              | Error e ->
                  Expect.equal e.Code "WRONG_TYPE" "the reject-fieldrule-empty precedent"
                  Expect.equal e.Path "$.style" "at the record"
              | Ok op -> failtestf "an empty patch decoded: %A" op
          } ]
