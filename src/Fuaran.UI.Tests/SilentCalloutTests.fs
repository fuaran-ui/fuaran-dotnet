module Fuaran.UI.Tests.SilentCalloutTests

// ============================================================================
//  Phase 2157 - a Warning / Critical callout that no screen reader will be
//  told about is named by the validator (FUARAN165).
//
//  The routes to a silent callout in a tree, each pinned below: no
//  `accessibility` trait at all (what the wire decodes when the field is
//  absent, and what `mkCallout` builds), a trait with `liveRegion = off`, a
//  trait with neither a live region nor an announcing role, and a statically
//  hidden node. Neither renderer re-applies the callout default, so each of
//  these reaches the DOM with no announcement.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode

let private codesOf (n: Node<obj>) : string list =
    match PreEmitValidate.validate n with
    | Ok() -> []
    | Error ds -> ds |> List.map (fun d -> let code, _, _ = PreEmitValidate.describe d in code)

let private calloutWith (tone: ToneVariant) (a11y: Accessibility option) : Node<obj> =
    let n: Node<obj> =
        Fuaran.callout
            "c"
            { Defaults.callout with
                Body = TextSource.Literal "Disk almost full"
                Tone = tone }

    { n with Accessibility = a11y }

let private silent (tone: ToneVariant) (a11y: Accessibility option) =
    Expect.contains (codesOf (calloutWith tone a11y)) "FUARAN165" "the callout is silent and the validator says so"

let private announced (tone: ToneVariant) (a11y: Accessibility option) =
    Expect.isFalse (List.contains "FUARAN165" (codesOf (calloutWith tone a11y))) "the callout announces"

[<Tests>]
let tests =
    testList
        "Phase 2157 - silent Warning/Critical callout (FUARAN165)"
        [ testCase "no accessibility trait at all" (fun _ -> silent ToneVariant.Warning None)
          testCase "Critical with no trait" (fun _ -> silent ToneVariant.Critical None)

          testCase "liveRegion = off, even with role alert" (fun _ ->
              silent
                  ToneVariant.Critical
                  (Some
                      { Defaults.Accessibility.empty with
                          Role = Some Fuaran.UI.HostPrelude.AriaRole.Alert
                          LiveRegion = Some LiveRegionKind.Off }))

          testCase "a trait that names a label but announces nothing" (fun _ ->
              silent
                  ToneVariant.Warning
                  (Some
                      { Defaults.Accessibility.empty with
                          Label = Some(Binding.Static(Some "Disk")) }))

          testCase "statically hidden" (fun _ ->
              silent
                  ToneVariant.Warning
                  (Some
                      { Defaults.Accessibility.empty with
                          Role = Some Fuaran.UI.HostPrelude.AriaRole.Alert
                          LiveRegion = Some LiveRegionKind.Assertive
                          Hidden = Some(Binding.Static(Some true)) }))

          testCase "the default announces" (fun _ -> announced ToneVariant.Critical Defaults.Accessibility.callout)

          testCase "a polite live region announces" (fun _ ->
              announced
                  ToneVariant.Warning
                  (Some
                      { Defaults.Accessibility.empty with
                          LiveRegion = Some LiveRegionKind.Polite }))

          testCase "role alert alone announces" (fun _ ->
              announced
                  ToneVariant.Warning
                  (Some
                      { Defaults.Accessibility.empty with
                          Role = Some Fuaran.UI.HostPrelude.AriaRole.Alert }))

          testCase "other tones are not audited" (fun _ ->
              for t in
                  [ ToneVariant.Default
                    ToneVariant.Info
                    ToneVariant.Success
                    ToneVariant.Brand
                    ToneVariant.Subdued ] do
                  announced t None)

          testCase "a decoded wire callout without a trait is flagged" (fun _ ->
              let json =
                  """{"id":"c","kind":{"$type":"Callout","body":"Disk","tone":"Warning"}}"""

              match decodeNodeObj json with
              | Ok n -> Expect.contains (codesOf n) "FUARAN165" "the wire shape is the common route"
              | Error e -> failtestf "decode refused: %A" e) ]
