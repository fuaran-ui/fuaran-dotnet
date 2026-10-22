/// Phase 2295 — the design-provider seam's conformance family (`design/`,
/// `wire-format-fixtures/DESIGN_SEAM.md`). Every vector is built from a record and written by the
/// reference encoder, and `Corpus.emit` proves each one before it is written: a round-trip vector
/// decodes and re-encodes byte-identically, a reject vector refuses with its named code at its
/// named path.
module Fuaran.UI.JsonDecode.Tests.DesignSeamFixtures

open System
open System.Security.Cryptography
open System.Text
open Fuaran.Core
open Fuaran.UI.Design

/// The corpus directory the family lives in.
let directory = "design"

type DesignExpect =
    | RoundTrip
    | Refuse of code: string * path: string

type DesignVector =
    {
        Id: string
        Decoder: string
        /// The document's bytes.
        Wire: string
        Expect: DesignExpect
        Description: string
    }

/// `sha256:` over a string's UTF-8 bytes — how a provider spells every hash the seam carries.
let hashOf (s: string) : string =
    "sha256:"
    + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes s)).ToLowerInvariant()

let private minimalRef: DesignSystemRef =
    { ProviderId = "directory"
      ProjectId = None
      BundleHash = hashOf "bundle: default theme"
      IdlVersion = 1 }

let private fullRef: DesignSystemRef =
    { minimalRef with
        ProviderId = "studio"
        ProjectId = Some "orders-console" }

let private minimalBrief: DesignBrief =
    { Module = "Orders"
      Purpose = None
      DataShapes = []
      Tasks = []
      Interactions = []
      Viewports = []
      Density = None
      Fragments = []
      DesignSystem = minimalRef }

let private optionalAbsentBrief: DesignBrief =
    { minimalBrief with
        DataShapes =
            [ { Name = "orders"
                Type = "Order"
                Cardinality = Cardinality.Many } ]
        Tasks = [ "find a late order" ]
        Interactions = [ Interaction.Filter; Interaction.Select ]
        Viewports = [ { Name = "desktop"; Width = 1280 } ]
        Fragments = [ "stat-card" ] }

let private fullBrief: DesignBrief =
    { Module = "Orders"
      Purpose = Some "Find, inspect and export the orders that need attention today."
      DataShapes =
        [ { Name = "orders"
            Type = "Order"
            Cardinality = Cardinality.Many }
          { Name = "summary"
            Type = "OrderSummary"
            Cardinality = Cardinality.One }
          { Name = "selected"
            Type = "Order"
            Cardinality = Cardinality.Optional } ]
      Tasks = [ "find a late order"; "compare two regions"; "export the day's orders" ]
      Interactions =
        [ Interaction.Filter
          Interaction.Select
          Interaction.Drill
          Interaction.Compare
          Interaction.Export
          Interaction.Navigate ]
      Viewports = [ { Name = "phone"; Width = 390 }; { Name = "desktop"; Width = 1440 } ]
      Density = Some Density.Compact
      Fragments = [ "stat-card"; "filter-bar" ]
      DesignSystem = fullRef }

let private minimalRequest: DesignRequest =
    { BriefHash = hashOf (SeamCodec.encodeBrief minimalBrief)
      DesignSystem = minimalRef
      State = RequestState.Manual
      Handle = None }

let private optionalAbsentRequest: DesignRequest =
    { minimalRequest with
        BriefHash = hashOf (SeamCodec.encodeBrief optionalAbsentBrief)
        State = RequestState.Ready }

let private fullRequest: DesignRequest =
    { BriefHash = hashOf (SeamCodec.encodeBrief fullBrief)
      DesignSystem = fullRef
      State = RequestState.Pending
      Handle = Some "req-0042" }

let private file (path: string) (mediaType: string) : DesignFile =
    { Path = path
      MediaType = mediaType
      Hash = hashOf ("file: " + path) }

/// `sha256:` over one line per file, in ordinal path order, of `<path> <hash>\n`.
let resultHashOf (screens: DesignScreen list) : string =
    screens
    |> List.collect _.Files
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Path, b.Path))
    |> List.map (fun f -> $"%s{f.Path} %s{f.Hash}\n")
    |> String.concat ""
    |> hashOf

let private fullScreens =
    [ { Name = "orders-list"
        Files =
          [ file "orders-list/index.html" "text/html"
            file "orders-list/screen.css" "text/css"
            file "orders-list/preview.png" "image/png" ] }
      { Name = "order-detail"
        Files = [ file "order-detail/index.html" "text/html" ] } ]

let private minimalResult: DesignResult =
    { Screens = []
      Annotations = None
      DesignSystem = minimalRef
      ContentHash = resultHashOf [] }

let private optionalAbsentResult: DesignResult =
    { Screens = fullScreens
      Annotations = None
      DesignSystem = minimalRef
      ContentHash = resultHashOf fullScreens }

let private fullResult: DesignResult =
    { Screens = fullScreens
      Annotations =
        Some
            [ { Screen = "orders-list"
                Components = [ "Filters"; "DataGrid"; "Metric"; "stat-card" ] }
              { Screen = "order-detail"
                Components = [ "Heading"; "SummaryList"; "Button" ] } ]
      DesignSystem = fullRef
      ContentHash = resultHashOf fullScreens }

let private roundTrip id decoder wire description =
    { Id = id
      Decoder = decoder
      Wire = wire
      Expect = RoundTrip
      Description = description }

let roundTrips: DesignVector list =
    [ roundTrip
          "design-ref-minimal"
          "design-system-ref"
          (SeamCodec.encodeRef minimalRef)
          "a design-system reference with its one optional member (projectId) absent; for this record the minimal and the every-optional-absent vectors are the same document"
      roundTrip
          "design-ref-full"
          "design-system-ref"
          (SeamCodec.encodeRef fullRef)
          "a design-system reference with every member present"
      roundTrip
          "design-brief-minimal"
          "design-brief"
          (SeamCodec.encodeBrief minimalBrief)
          "a brief with every optional member absent and every list empty: a list member is always present, empty or not"
      roundTrip
          "design-brief-optional-absent"
          "design-brief"
          (SeamCodec.encodeBrief optionalAbsentBrief)
          "a brief whose lists are populated and whose optional members (purpose, density, the reference's projectId) are absent"
      roundTrip
          "design-brief-full"
          "design-brief"
          (SeamCodec.encodeBrief fullBrief)
          "a brief with every member present, every interaction of the closed vocabulary and every cardinality"
      roundTrip
          "design-request-minimal"
          "design-request"
          (SeamCodec.encodeRequest minimalRequest)
          "a Manual request (the design is made interactively and retrieved by its declared export) with no handle"
      roundTrip
          "design-request-optional-absent"
          "design-request"
          (SeamCodec.encodeRequest optionalAbsentRequest)
          "a Ready request with its optional members (handle, the reference's projectId) absent"
      roundTrip
          "design-request-full"
          "design-request"
          (SeamCodec.encodeRequest fullRequest)
          "a Pending request with every member present; its briefHash is the hash of design-brief-full's bytes"
      roundTrip
          "design-result-minimal"
          "design-result"
          (SeamCodec.encodeResult minimalResult)
          "a result with no screens and no annotations; its contentHash is the hash of the empty file list"
      roundTrip
          "design-result-optional-absent"
          "design-result"
          (SeamCodec.encodeResult optionalAbsentResult)
          "a result with screens and its optional member (annotations) absent"
      roundTrip
          "design-result-full"
          "design-result"
          (SeamCodec.encodeResult fullResult)
          "a result with two screens of HTML, CSS and image files and a component inventory per screen, named as the bundle names its cards"
      roundTrip
          "design-exchange-same-system"
          "design-exchange"
          (SeamCodec.encodeExchange fullRequest fullResult)
          "a request and the result fetched for it, made against the same design-system bundle: the exchange rule holds" ]

/// A canonical document with one member at `path` (member names from the root) replaced, or
/// removed when `value` is `None`. Re-rendered canonically, so a reject vector differs from a
/// round-trip vector only in the defect it carries.
let private edit (wire: string) (path: string list) (value: JVal option) : string =
    let rec go (v: JVal) (path: string list) =
        match v, path with
        | JObj fields, [ name ] ->
            match value with
            | Some x when fields |> List.exists (fun (k, _) -> k = name) ->
                JObj(fields |> List.map (fun (k, y) -> if k = name then k, x else k, y))
            | Some x -> JObj(fields @ [ name, x ])
            | None -> JObj(fields |> List.filter (fun (k, _) -> k <> name))
        | JObj fields, name :: rest -> JObj(fields |> List.map (fun (k, y) -> if k = name then k, go y rest else k, y))
        | JArr items, [ index ] ->
            let i = int index

            match value with
            | Some x -> JArr(items |> List.mapi (fun j y -> if j = i then x else y))
            | None -> JArr(items |> List.indexed |> List.filter (fun (j, _) -> j <> i) |> List.map snd)
        | JArr items, index :: rest ->
            let i = int index
            JArr(items |> List.mapi (fun j y -> if j = i then go y rest else y))
        | _ -> failwithf "design fixture edit: no member at %A" path

    match Json.parse wire with
    | Ok v -> Canonical.write Rendering.CrossHost (go v path)
    | Error e -> failwithf "design fixture edit: the base document does not parse: %s" e

let private reject id decoder wire code path description =
    { Id = id
      Decoder = decoder
      Wire = wire
      Expect = Refuse(code, path)
      Description = description }

let rejects: DesignVector list =
    [ reject
          "design-reject-ref-missing-hash"
          "design-system-ref"
          (edit (SeamCodec.encodeRef fullRef) [ "bundleHash" ] None)
          "DESIGN_MISSING_MEMBER"
          "$.bundleHash"
          "a reference missing its bundle hash: a design made against an unnamed bundle cannot be lowered against any"
      reject
          "design-reject-brief-ref-missing-hash"
          "design-brief"
          (edit (SeamCodec.encodeBrief fullBrief) [ "designSystem"; "bundleHash" ] None)
          "DESIGN_MISSING_MEMBER"
          "$.designSystem.bundleHash"
          "a brief whose design-system reference is missing its hash"
      reject
          "design-reject-ref-bad-hash"
          "design-system-ref"
          (edit (SeamCodec.encodeRef fullRef) [ "bundleHash" ] (Some(JStr(fullRef.BundleHash.ToUpperInvariant()))))
          "DESIGN_BAD_HASH"
          "$.bundleHash"
          "a hash spelled in uppercase: a hash is 'sha256:' and 64 LOWERCASE hexadecimal digits"
      reject
          "design-reject-brief-unknown-interaction"
          "design-brief"
          (edit (SeamCodec.encodeBrief fullBrief) [ "interactions"; "1" ] (Some(JStr "Hover")))
          "DESIGN_UNKNOWN_INTERACTION"
          "$.interactions[1]"
          "an interaction outside the closed vocabulary: nothing in the language lowers 'Hover'"
      reject
          "design-reject-brief-unknown-member"
          "design-brief"
          (edit (SeamCodec.encodeBrief fullBrief) [ "palette" ] (Some(JStr "#1d4ed8")))
          "DESIGN_UNKNOWN_MEMBER"
          "$.palette"
          "a brief carrying a member the record does not declare: a brief is an agreement, so an unknown member is a disagreement"
      reject
          "design-reject-brief-viewport-width"
          "design-brief"
          (edit (SeamCodec.encodeBrief fullBrief) [ "viewports"; "0"; "width" ] (Some(JInt 0)))
          "DESIGN_OUT_OF_RANGE"
          "$.viewports[0].width"
          "a viewport of no width"
      reject
          "design-reject-request-unknown-state"
          "design-request"
          (edit (SeamCodec.encodeRequest fullRequest) [ "state" ] (Some(JStr "Done")))
          "DESIGN_UNKNOWN_TOKEN"
          "$.state"
          "a request state outside Manual, Pending and Ready"
      reject
          "design-reject-request-wrong-type"
          "design-request"
          (SeamCodec.encodeBrief fullBrief)
          "DESIGN_WRONG_TYPE"
          "$.$type"
          "a brief handed to the request entry point: $type names the record a document is"
      reject
          "design-reject-result-file-missing-hash"
          "design-result"
          (edit (SeamCodec.encodeResult fullResult) [ "screens"; "0"; "files"; "0"; "hash" ] None)
          "DESIGN_MISSING_MEMBER"
          "$.screens[0].files[0].hash"
          "a screen file with no content hash"
      reject
          "design-reject-result-annotation-missing-components"
          "design-result"
          (edit (SeamCodec.encodeResult fullResult) [ "annotations"; "0"; "components" ] None)
          "DESIGN_MISSING_MEMBER"
          "$.annotations[0].components"
          "an annotation missing its component list: annotations are lenient about UNKNOWN members, never about missing required ones"
      reject
          "design-reject-exchange-system-mismatch"
          "design-exchange"
          (SeamCodec.encodeExchange
              fullRequest
              { fullResult with
                  DesignSystem =
                      { fullRef with
                          BundleHash = hashOf "bundle: dark theme" } })
          "DESIGN_SYSTEM_MISMATCH"
          "$.result.designSystem.bundleHash"
          "a result citing a different design-system bundle than the request it was fetched for" ]

/// The one lenient vector: an annotation carrying members its record does not declare decodes,
/// and re-encodes WITHOUT them. Its input is not canonical output, so it is the one vector whose
/// `expectedFile` differs from its `inputFile`.
let lenientAnnotation: DesignVector * string =
    let wire =
        edit (SeamCodec.encodeResult fullResult) [ "annotations"; "0"; "confidence" ] (Some(JFloat 0.75))

    { Id = "design-result-annotation-unknown-member"
      Decoder = "design-result"
      Wire = wire
      Expect = RoundTrip
      Description =
        "an annotation carrying a member its record does not declare: an annotation is evidence, so the member is ignored rather than refused, and the result re-encodes without it (expectedFile is design-result-full's bytes)" },
    "design-result-full"
