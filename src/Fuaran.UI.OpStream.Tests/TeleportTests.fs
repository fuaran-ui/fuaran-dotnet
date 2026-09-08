module Fuaran.UI.OpStream.Tests.TeleportTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

// ============================================================================
//  Phase 437 — teleport state-bundle codec.
//
//  Corpus-style coverage of the FT1 contract:
//    - encode/decode round-trip is byte-exact (the decoded bundle re-encodes
//      to the identical string) and deterministic;
//    - resume material survives: the Binding.State map (wizard step, buffered
//      form draft), the bounded op-history window, and the chain head;
//    - FGP 3: closure slots come back inert sentinels, wire-survivable
//      actions come back as dispatchable data;
//    - rejects: tampered chain head / state → DigestMismatch; oversized
//      input and deflate bombs → Oversize; garbage / wrong version /
//      missing fields / duplicate NodeIds → their typed errors;
//    - budget: the exemplar wizard bundle fits the QR version-40-L ceiling.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

// ─── Exemplar app: a mid-interaction onboarding wizard ──────────────────────

/// A representative "immortal app" surface: heading + 3-step stepper (step 2
/// active) + a buffered form draft + share/dispatch buttons. Interaction
/// state lives in `Binding.State` slots, so the bundle's state map is exactly
/// what resume needs to land mid-wizard.
let private exemplarTree () : Node<TestMsg> =
    Fuaran.stack
        "teleport-wizard"
        { Orientation = Orientation.Vertical
          Wrap = false
          Children =
            [ Fuaran.heading
                  "wiz-title"
                  { Level = 1
                    Text = TextSource.Literal "Onboarding"
                    Variant = HeadingVariant.Standard }
              Fuaran.stepper
                  "wiz-steps"
                  { ActiveStep = binding.state "wizard-step" 0
                    Children =
                      [ Fuaran.markdown "step-welcome" "Welcome — tell us who you are."
                        Fuaran.form
                            "step-details"
                            { Defaults.form with
                                Fields =
                                    [ { Defaults.formField with
                                          Id = "draft-name"
                                          Label = TextSource.Literal "Full name"
                                          Kind = FormFieldKind.textDeclarative (binding.state "draft-name" "")
                                          Required = true }
                                      { Defaults.formField with
                                          Id = "draft-team"
                                          Label = TextSource.Literal "Team"
                                          Kind = FormFieldKind.textDeclarative (binding.state "draft-team" "") } ]
                                OnSubmit = Action.SetState("wizard-step", Some(JInt 2), None)
                                SubmitLabel = TextSource.Literal "Continue" }
                        Fuaran.markdown "step-review" "All done — review and finish." ]
                    // Closure-carrying: encodes as the "<closure>" sentinel.
                    OnSelect = Some(fun i -> Action.Dispatch(Selected i)) }
              Fuaran.button
                  "wiz-share"
                  { Defaults.button with
                      Label = TextSource.Literal "Share this session"
                      // Wire-survivable: rides the bundle as data.
                      OnClick = Action.WriteToClipboard(TextSource.Literal "https://demo.example/teleport") } ] }

let private exemplarState: Map<string, JVal> =
    Map.ofList
        [ "wizard-step", JInt 1
          "draft-name", JStr "Ada Lovelace"
          "draft-team", JStr "Compilers"
          // A float exercises the canonical number layout through the
          // digest-recompute path.
          "progress", JFloat 0.5 ]

let private exemplarBundle () : TeleportBundle<TestMsg> =
    { Tree = exemplarTree ()
      State = exemplarState
      History =
        [ TreeOp.InsertChild(NodeId "wiz-steps", Fuaran.markdown "step-review" "All done — review and finish.")
          TreeOp.RemoveNode(NodeId "step-stale") ]
      ChainHead = Some(String.replicate 64 "a") }

/// Boxed literals sites requiring nonnull-obj (Expecto expectations, the
/// snapshot map). Same launder as `Fuaran.UI.AiTools.Tests`.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private encodeOk (bundle: TeleportBundle<'Msg>) : string =
    match Teleport.encode bundle with
    | Ok s -> s
    | Error e -> failtestf "encode failed: %A" e

let private decodeOk (encoded: string) : DecodedTeleport =
    match Teleport.decode encoded with
    | Ok d -> d
    | Error e -> failtestf "decode failed: %A" e

/// Re-encode a decoded bundle (the storage-erased `Node<obj>` shapes).
let private reEncode (d: DecodedTeleport) : string =
    encodeOk
        { Tree = d.Tree
          State = d.State
          History = d.History
          ChainHead = d.ChainHead }

// ─── Envelope surgery for tamper fixtures ────────────────────────────────────

let private unpack (encoded: string) : (string * JVal) list =
    let payload = encoded.Substring Teleport.FormatPrefix.Length

    match Base64Url.decode payload with
    | Error e -> failtestf "unpack base64: %s" e
    | Ok compressed ->
        match Deflate.inflate 1048576 compressed with
        | Error e -> failtestf "unpack inflate: %A" e
        | Ok bytes ->
            match Utf8.decode bytes with
            | Error e -> failtestf "unpack utf8: %s" e
            | Ok json ->
                match Json.parse json with
                | Ok(JObj fields) -> fields
                | other -> failtestf "unpack parse: %A" other

let private repack (fields: (string * JVal) list) : string =
    Teleport.FormatPrefix
    + Base64Url.encode (Deflate.compress (Utf8.encode (Canon.render (JObj fields))))

let private replaceField (name: string) (value: JVal) (fields: (string * JVal) list) : (string * JVal) list =
    fields |> List.map (fun (k, v) -> if k = name then k, value else k, v)

// The pinned digest preimage tag (WIRE_FORMAT §17) — used to forge a
// *consistent* digest for structurally-invalid envelopes, so the structural
// error surfaces rather than DigestMismatch.
let private forgeDigest (coreFields: (string * JVal) list) : string =
    Fuaran.UI.Hashing.sha256Hex ("fuaran-teleport:v1|" + Canon.render (JObj coreFields))

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — Teleport bundle"
        [ test "encode/decode round-trip is byte-exact" {
              let encoded = encodeOk (exemplarBundle ())
              let decoded = decodeOk encoded
              Expect.equal (reEncode decoded) encoded "decode → re-encode reproduces the identical string"
          }

          test "encode is deterministic" {
              Expect.equal (encodeOk (exemplarBundle ())) (encodeOk (exemplarBundle ())) "same bundle ⇒ same string"
          }

          test "resume material survives: state map, history window, chain head" {
              let decoded = decodeOk (encodeOk (exemplarBundle ()))

              Expect.equal decoded.State exemplarState "the Binding.State map round-trips"
              Expect.equal decoded.ChainHead (Some(String.replicate 64 "a")) "chain head round-trips"
              Expect.equal decoded.History.Length 2 "the bounded op-history window round-trips"

              // The wizard's mid-interaction identity: stable NodeIds + the
              // state keys their bindings read. Resume = seat these values
              // back into the state store; the decoded tree's Binding.State
              // readers (same keys, same NodeIds) pick them up.
              let treeWire = CanonicalJson.encodeNode decoded.Tree

              Expect.stringContains treeWire "\"key\":\"wizard-step\"" "stepper still reads the wizard-step slot"
              // Phase 596: the draft-name field's binding is the exact
              // auto-shape `State(field id, "")`, so the canonical bytes OMIT
              // its `value` key — the state key survives IMPLICITLY via the
              // field id, and decode re-synthesises the same State binding.
              // Assert the semantic survival, not the byte proxy.
              Expect.stringContains treeWire "\"id\":\"draft-name\"" "the draft-name field survives by id"

              let draftBindingRestored =
                  (Fuaran.UI.BindingWalk.collect decoded.Tree).Uses
                  |> List.exists (fun u ->
                      match u.Use with
                      | Fuaran.UI.BindingWalk.BindingUse.State "draft-name" -> true
                      | _ -> false)

              Expect.isTrue draftBindingRestored "decode re-synthesises State(draft-name) for the omitted-value field"

              let seated = Teleport.stateValues decoded.State
              Expect.equal (seated["wizard-step"]) (nn 1.0) "step index seats as the standard lowered number"
              Expect.equal (seated["draft-name"]) (nn "Ada Lovelace") "buffered draft seats as its string"
          }

          test "FGP 3 — closures come back inert, wire-survivable actions come back as data" {
              let decoded = decodeOk (encodeOk (exemplarBundle ()))
              let treeWire = CanonicalJson.encodeNode decoded.Tree

              Expect.stringContains treeWire "\"onSelect\":\"<closure>\"" "the stepper Dispatch closure is a sentinel"

              Expect.stringContains
                  treeWire
                  "{\"$type\":\"WriteToClipboard\",\"text\":\"https://demo.example/teleport\"}"
                  "the share action survives as dispatchable data"

              Expect.stringContains
                  treeWire
                  "{\"$type\":\"SetState\",\"key\":\"wizard-step\",\"value\":2}"
                  "the submit action survives as dispatchable data"
          }

          test "state capture is best-effort over the store snapshot shape" {
              let snapshot: Map<string, obj> =
                  Map.ofList
                      [ "s", nn "text"
                        "b", nn true
                        "i", nn 3
                        "f", nn 2.5
                        "already-jval", nn (JObj [ "x", JInt 1 ])
                        "host-typed", nn (System.DateTimeOffset.FromUnixTimeSeconds 0L) ]

              let captured = Teleport.captureState snapshot

              Expect.equal
                  captured
                  (Map.ofList
                      [ "s", JStr "text"
                        "b", JBool true
                        "i", JInt 3
                        "f", JFloat 2.5
                        "already-jval", JObj [ "x", JInt 1 ] ])
                  "primitives + JVal capture; host-typed content is dropped"
          }

          test "tampered chain head is rejected (DigestMismatch)" {
              let fields = unpack (encodeOk (exemplarBundle ()))

              let tampered =
                  repack (replaceField "chainHead" (JStr(String.replicate 64 "b")) fields)

              match Teleport.decode tampered with
              | Error(TeleportError.DigestMismatch _) -> ()
              | other -> failtestf "expected DigestMismatch, got %A" other
          }

          test "tampered state value is rejected (DigestMismatch)" {
              let fields = unpack (encodeOk (exemplarBundle ()))

              let tamperedState =
                  replaceField "state" (JObj(Map.toList (Map.add "wizard-step" (JInt 2) exemplarState))) fields

              match Teleport.decode (repack tamperedState) with
              | Error(TeleportError.DigestMismatch _) -> ()
              | other -> failtestf "expected DigestMismatch, got %A" other
          }

          test "oversized encoded input is rejected before decompression" {
              let encoded = encodeOk (exemplarBundle ())

              match
                  Teleport.decodeWith
                      { TeleportLimits.defaults with
                          MaxEncodedChars = 64 }
                      encoded
              with
              | Error(TeleportError.Oversize(64, _)) -> ()
              | other -> failtestf "expected Oversize, got %A" other
          }

          test "a deflate bomb is capped by the decoded-bytes limit" {
              let bomb =
                  { exemplarBundle () with
                      State = Map.ofList [ "pad", JStr(String.replicate 200000 "x") ] }

              let encoded = encodeOk bomb

              match
                  Teleport.decodeWith
                      { TeleportLimits.defaults with
                          MaxDecodedBytes = 4096 }
                      encoded
              with
              | Error(TeleportError.Oversize(4096, _)) -> ()
              | other -> failtestf "expected Oversize, got %A" other
          }

          test "non-bundle inputs are typed InvalidFormat / InvalidJson" {
              match Teleport.decode "hello" with
              | Error(TeleportError.InvalidFormat _) -> ()
              | other -> failtestf "expected InvalidFormat for a non-prefixed string, got %A" other

              match Teleport.decode "FT1.!!not-base64!!" with
              | Error(TeleportError.InvalidFormat _) -> ()
              | other -> failtestf "expected InvalidFormat for bad base64url, got %A" other

              let notJson =
                  Teleport.FormatPrefix
                  + Base64Url.encode (Deflate.compress (Utf8.encode "not json"))

              match Teleport.decode notJson with
              | Error(TeleportError.InvalidJson _) -> ()
              | other -> failtestf "expected InvalidJson, got %A" other
          }

          test "an unsupported envelope version is refused by name" {
              let core = [ "bundle", JStr "teleport@9" ]

              match Teleport.decode (repack (("digest", JStr(forgeDigest core)) :: core)) with
              | Error(TeleportError.UnsupportedVersion "teleport@9") -> ()
              | other -> failtestf "expected UnsupportedVersion, got %A" other
          }

          test "a structurally-broken envelope is a typed InvalidEnvelope" {
              // Valid version + consistent digest, but no tree.
              let core = [ "bundle", JStr Teleport.Version ]

              match Teleport.decode (repack (("digest", JStr(forgeDigest core)) :: core)) with
              | Error(TeleportError.InvalidEnvelope("$.tree", _)) -> ()
              | other -> failtestf "expected InvalidEnvelope $.tree, got %A" other
          }

          test "a duplicate-NodeId tree is refused (state re-seat needs stable identity)" {
              let tree: Node<TestMsg> =
                  Fuaran.stack
                      "dup-root"
                      { Orientation = Orientation.Vertical
                        Wrap = false
                        Children = [ Fuaran.markdown "dup" "one"; Fuaran.markdown "dup" "two" ] }

              let encoded =
                  encodeOk
                      { TeleportBundle.ofTree tree with
                          ChainHead = None }

              match Teleport.decode encoded with
              | Error(TeleportError.TreeInvalid defects) ->
                  Expect.exists
                      defects
                      (function
                      | PreEmitValidate.PreEmitDefect.DuplicateNodeId("dup", 2) -> true
                      | _ -> false)
                      "names the duplicated id"
              | other -> failtestf "expected TreeInvalid, got %A" other
          }

          test "budget — the exemplar wizard bundle fits the QR v40-L ceiling" {
              let full = encodeOk (exemplarBundle ())

              let treeOnly =
                  encodeOk
                      { TeleportBundle.ofTree (exemplarTree ()) with
                          ChainHead = None }

              let noHistory =
                  encodeOk
                      { exemplarBundle () with
                          History = []
                          ChainHead = None }

              // Measured sizes land in WIRE_FORMAT §17's budget table.
              printfn
                  "teleport budget measurement: tree-only=%d chars, tree+state=%d, full(+history+chain)=%d (QR v40-L ceiling %d, comfortable %d, URL %d)"
                  treeOnly.Length
                  noHistory.Length
                  full.Length
                  TeleportBudget.QrMaxBytes
                  TeleportBudget.QrComfortableBytes
                  TeleportBudget.UrlFragmentBytes

              match Teleport.encodeWithin TeleportBudget.QrMaxBytes (exemplarBundle ()) with
              | Ok s -> Expect.isLessThanOrEqual s.Length TeleportBudget.QrMaxBytes "exemplar under the hard QR ceiling"
              | Error e -> failtestf "exemplar bundle over the QR budget: %A" e
          }

          test "encodeWithin refuses an over-budget bundle with truncation guidance" {
              // Incompressible padding (a SHA-256 chain) — a repetitive pad
              // would deflate straight back under the budget.
              let pad =
                  [ for i in 1..128 -> Fuaran.UI.Hashing.sha256Hex (string i) ]
                  |> String.concat ""

              let padded =
                  { exemplarBundle () with
                      State = Map.ofList [ "pad", JStr pad ] }

              match Teleport.encodeWithin TeleportBudget.QrMaxBytes padded with
              | Error(TeleportError.Oversize(limit, message)) ->
                  Expect.equal limit TeleportBudget.QrMaxBytes "names the budget"
                  Expect.stringContains message "history" "points at the truncation remedy"
              | other -> failtestf "expected Oversize, got %A" other
          }

          test "decode enforces the format prefix as self-identification" {
              let encoded = encodeOk (exemplarBundle ())
              Expect.stringStarts encoded "FT1." "the FT1 tag leads the string"

              // The same payload under a foreign tag is not a teleport bundle.
              match Teleport.decode ("XX9." + encoded.Substring 4) with
              | Error(TeleportError.InvalidFormat _) -> ()
              | other -> failtestf "expected InvalidFormat, got %A" other
          }

          test "GO-RED: the format-prefix test is ORDINAL, so the tag check and the slice agree" {
              // `FT1.` is a WIRE TAG — four bytes the encoder emitted — and the
              // `Substring` that follows the test slices at that same fixed count.
              // Under the culture-sensitive default overload ICU ignores
              // zero-width formatting characters, so a string that does NOT begin
              // with the tag passes the test and is then sliced four characters in
              // from the WRONG place; and the answer can differ by machine
              // culture, so the same bundle decodes here and is refused there.
              //
              // The probe is verified before it is trusted: these assertions prove
              // nothing unless the two overloads actually disagree on this input.
              let encoded = encodeOk (exemplarBundle ())
              let disguised = "\u200d" + encoded

              Expect.isTrue
                  (disguised.StartsWith "FT1.")
                  "PROBE: the culture-sensitive overload accepts the disguised tag"

              Expect.isFalse
                  (disguised.StartsWith("FT1.", System.StringComparison.Ordinal))
                  "PROBE: the ordinal overload does not"

              let original = System.Globalization.CultureInfo.CurrentCulture

              try
                  // Set inside the test and restored below: the ambient culture
                  // must change none of the answers.
                  System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo "tr-TR"

                  // The MESSAGE is the discriminator, not the case. Under the
                  // culture-sensitive overload the tag check PASSES and the
                  // `Substring` then slices four characters in from the wrong
                  // place, so the refusal still arrives — as `InvalidFormat` from
                  // the base64url decoder, blaming the payload for a prefix that
                  // was never there. The ordinal check refuses at the tag and
                  // says so.
                  match Teleport.decode disguised with
                  | Error(TeleportError.InvalidFormat message) ->
                      Expect.stringContains
                          message
                          "prefix"
                          "the refusal attributes the failure to the missing tag, not to the payload"
                  | other -> failtestf "expected InvalidFormat for a bundle that only collates as tagged, got %A" other

                  // A genuine bundle still decodes under the same culture.
                  match Teleport.decode encoded with
                  | Ok _ -> ()
                  | Error e -> failtestf "a real bundle must still decode under tr-TR: %A" e
              finally
                  System.Globalization.CultureInfo.CurrentCulture <- original
          } ]

// ────────────────────────────────────────────────────────────────────────────
//  Phase 1602 — the corpus family (WIRE_FORMAT.md §12 + §17.6).
//
//  Everything above is this host talking to itself: it encodes an exemplar and
//  decodes it back, which proves the codec is self-consistent and proves
//  nothing at all about cross-host agreement. A bundle produced by the
//  reference encoder and checked into `wire-format-fixtures/teleport/` is a
//  different claim — bytes in, bytes out, no shared type model between the
//  producer and this reader — and it is the claim §17.6 makes. The TypeScript
//  host has read this family since Phase 1589; this is the F# host joining it.
//
//  The exemplar round trip stays where it is, as the ENCODER test. The corpus
//  cannot make that statement: every vector here is decode-only by
//  construction, because a vector's whole value is that this host did not
//  produce it.
// ────────────────────────────────────────────────────────────────────────────

/// Walk up from the test assembly to the workspace `wire-format-fixtures/`.
/// `None` in a bare single-repo clone — the same posture (and the same reason)
/// as `ChainCorpusTests` and `TreeOpMapLawsTests`: a missing input degrades to
/// a skip and never takes the assembly's type initializer down with it.
let private tryCorpusRoot () : string option =
    let rec walk (dir: DirectoryInfo | null) =
        match dir with
        | null -> None
        | d when File.Exists(Path.Combine(d.FullName, "wire-format-fixtures", "manifest.json")) ->
            Some(Path.Combine(d.FullName, "wire-format-fixtures"))
        | d -> walk d.Parent

    walk (DirectoryInfo AppContext.BaseDirectory)

/// A manifest row this family cares about (WIRE_FORMAT §12).
type private CorpusFixture =
    { Id: string
      Kind: string
      Decoder: string
      InputFile: string
      ExpectedFile: string
      ExpectedErrorCode: string
      ExpectedPath: string
      Description: string }

let private manifestString (row: JsonElement) (name: string) : string =
    match row.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String ->
        match v.GetString() with
        | null -> ""
        | s -> s
    | _ -> ""

/// The `teleport-decode` / `teleport-reject` rows, read from the MANIFEST
/// rather than from the directory listing: the manifest is what a conformant
/// host's harness loads, so a payload sitting on disk that the manifest does
/// not list is not a conformance obligation and must not be run as one.
let private teleportFixtures (root: string) : CorpusFixture list =
    use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.map (fun row ->
        { Id = manifestString row "id"
          Kind = manifestString row "kind"
          Decoder = manifestString row "decoder"
          InputFile = manifestString row "inputFile"
          ExpectedFile = manifestString row "expectedFile"
          ExpectedErrorCode = manifestString row "expectedErrorCode"
          ExpectedPath = manifestString row "expectedPath"
          Description = manifestString row "description" })
    |> Seq.filter (fun f -> f.Kind = "teleport-decode" || f.Kind = "teleport-reject")
    |> List.ofSeq

/// A `teleport-*` fixture's input DOCUMENT: the bundle string, plus the size
/// ceilings this vector is to be run under when it names any (§17.6). It is a
/// document, not a payload — a decoder handed the file's own bytes as a bundle
/// fails every vector in the family.
let private inputOf (root: string) (f: CorpusFixture) : string * TeleportLimits =
    match Json.parse (File.ReadAllText(Path.Combine(root, f.InputFile))) with
    | Ok(JObj fields) ->
        let encoded =
            match fields |> List.tryFind (fun (k, _) -> k = "encoded") with
            | Some(_, JStr s) -> s
            | _ -> failtestf "%s: input document has no string 'encoded' member" f.Id

        let limits =
            match fields |> List.tryFind (fun (k, _) -> k = "limits") with
            | Some(_, JObj limitFields) ->
                limitFields
                |> List.fold
                    (fun (acc: TeleportLimits) (k, v) ->
                        match k, v with
                        | "maxEncodedChars", JInt n -> { acc with MaxEncodedChars = int n }
                        | "maxDecodedBytes", JInt n -> { acc with MaxDecodedBytes = int n }
                        // Silently ignoring an unrecognised ceiling would run the
                        // vector under limits it did not ask for and report the
                        // result as conformance.
                        | other, _ -> failtestf "%s: unknown limit '%s' — §17.6 names two" f.Id other)
                    TeleportLimits.defaults
            | _ -> TeleportLimits.defaults

        encoded, limits
    | other -> failtestf "%s: input file is not a JSON object document — %A" f.Id other

/// A member of the expected envelope as canonical bytes; `None` when the
/// envelope omits it (§17.2 omits `state` / `history` / `chainHead` when empty).
let private envelopeMember (envelope: (string * JVal) list) (name: string) : string option =
    envelope
    |> List.tryPick (fun (k, v) -> if k = name then Some(Canon.render v) else None)

/// The `$`-rooted position a §17.4 refusal concerns. The mapping is NORMATIVE
/// and lives in WIRE_FORMAT.md §17.6, not here: a §17.4 error is a typed case
/// rather than a `(code, path)` pair, so fixing the position in the
/// specification is what stops two conformant hosts disagreeing about a fixture
/// while both pass. This implements that table and nothing else.
let private codeAndPath (e: TeleportError) : string * string =
    match e with
    | TeleportError.Oversize _ -> "Oversize", "$"
    | TeleportError.InvalidFormat _ -> "InvalidFormat", "$"
    | TeleportError.InvalidJson _ -> "InvalidJson", "$"
    | TeleportError.InvalidEnvelope(path, _) -> "InvalidEnvelope", path
    | TeleportError.UnsupportedVersion _ -> "UnsupportedVersion", "$.bundle"
    | TeleportError.DigestMismatch _ -> "DigestMismatch", "$.digest"
    | TeleportError.TreeDecode e -> "TreeDecode", e.Path
    | TeleportError.HistoryDecode(_, e) -> "HistoryDecode", e.Path
    | TeleportError.TreeInvalid _ -> "TreeInvalid", "$.tree"

[<Tests>]
let corpusTests =
    testList
        "Fuaran.UI.OpStream — Teleport corpus family (§17.6)"
        [ test "the family is registered, and every row names the teleport entry point" {
              match tryCorpusRoot () with
              | None ->
                  skiptest
                      "wire-format-fixtures/ not found walking up from the test assembly — this family needs the workspace checkout (skipped in a bare single-repo clone)"
              | Some root ->
                  let fixtures = teleportFixtures root
                  let accepts = fixtures |> List.filter (fun f -> f.Kind = "teleport-decode")
                  let rejects = fixtures |> List.filter (fun f -> f.Kind = "teleport-reject")

                  // FAILING here, rather than reporting an empty suite green, is
                  // the point. The family's rows are hand-maintained, so a
                  // corpus regeneration that dropped them would otherwise leave
                  // every arm below quantifying over nothing — a green run that
                  // has silently stopped certifying the format.
                  Expect.isNonEmpty
                      accepts
                      "the corpus carries no `teleport-decode` rows. If it was just regenerated, the emit dropped this family — restore the rows (WIRE_FORMAT.md §12)."

                  Expect.isNonEmpty rejects "the corpus carries no `teleport-reject` rows (WIRE_FORMAT.md §12)."

                  let wrongDecoder = fixtures |> List.filter (fun f -> f.Decoder <> "teleport")

                  Expect.isEmpty
                      wrongDecoder
                      (sprintf
                          "these rows name an entry point other than `teleport`, so a harness would run them through the wrong decoder: %A"
                          (wrongDecoder |> List.map (fun f -> f.Id, f.Decoder)))
          }

          test "every accept vector decodes to the envelope the corpus holds" {
              match tryCorpusRoot () with
              | None -> skiptest "wire-format-fixtures/ not found — needs the workspace checkout"
              | Some root ->
                  let accepts =
                      teleportFixtures root |> List.filter (fun f -> f.Kind = "teleport-decode")

                  Expect.isNonEmpty accepts "no accept vectors — this arm would assert nothing"

                  for f in accepts do
                      let encoded, limits = inputOf root f

                      let decoded =
                          match Teleport.decodeWith limits encoded with
                          | Ok d -> d
                          | Error e -> failtestf "%s failed to decode (%s): %A" f.Id f.Description e

                      let expectedRaw = File.ReadAllText(Path.Combine(root, f.ExpectedFile))

                      let envelope =
                          match Json.parse expectedRaw with
                          | Ok(JObj fields) -> fields
                          | other -> failtestf "%s: expectedFile is not a JSON object — %A" f.Id other

                      // The expectation itself must be canonical. A hand edit
                      // that reordered a member or respaced the document would
                      // make every comparison below a comparison against bytes
                      // §2 does not permit — and would still pass, because both
                      // sides are re-rendered before they are compared.
                      Expect.equal
                          (Canon.render (JObj envelope))
                          expectedRaw
                          (sprintf "%s: expectedFile is not in canonical form (WIRE_FORMAT §2)" f.Id)

                      Expect.equal
                          (Canon.render (JStr decoded.Digest))
                          (envelopeMember envelope "digest" |> Option.defaultValue "")
                          (sprintf
                              "%s: the integrity digest, recomputed here, differs from the one the corpus carries"
                              f.Id)

                      // The cross-host claim: the decoded tree re-encodes to the
                      // SAME canonical bytes the corpus holds for the envelope's
                      // `tree` member. The CARRIED payload is not those bytes —
                      // it spells shorthand the node decoder normalises — so a
                      // decoder that treated it as opaque JSON fails here, which
                      // is exactly what §17.6 is for.
                      Expect.equal
                          (CanonicalJson.encodeNode decoded.Tree)
                          (envelopeMember envelope "tree" |> Option.defaultValue "")
                          (sprintf "%s: the decoded tree does not re-encode to the corpus's `tree` bytes" f.Id)

                      Expect.equal
                          (Canon.render (JObj(Map.toList decoded.State)))
                          (envelopeMember envelope "state" |> Option.defaultValue "{}")
                          (sprintf "%s: the resumed state map differs from the one the envelope carries" f.Id)

                      let expectedHistory =
                          match envelope |> List.tryPick (fun (k, v) -> if k = "history" then Some v else None) with
                          | Some(JArr items) -> items |> List.map Canon.render
                          | _ -> []

                      Expect.equal
                          (decoded.History |> List.map CanonicalJson.encodeOp)
                          expectedHistory
                          (sprintf "%s: the op-history window differs from the one the envelope carries" f.Id)

                      Expect.equal
                          (decoded.ChainHead |> Option.map (JStr >> Canon.render))
                          (envelopeMember envelope "chainHead")
                          (sprintf "%s: the chain head differs from the one the envelope carries" f.Id)
          }

          test "every reject vector is refused with the case and at the position §17.6 fixes" {
              match tryCorpusRoot () with
              | None -> skiptest "wire-format-fixtures/ not found — needs the workspace checkout"
              | Some root ->
                  let rejects =
                      teleportFixtures root |> List.filter (fun f -> f.Kind = "teleport-reject")

                  Expect.isNonEmpty rejects "no reject vectors — this arm would assert nothing"

                  for f in rejects do
                      let encoded, limits = inputOf root f

                      match Teleport.decodeWith limits encoded with
                      | Ok _ -> failtestf "%s DECODED, but %s" f.Id f.Description
                      | Error e ->
                          let code, path = codeAndPath e

                          Expect.equal code f.ExpectedErrorCode (sprintf "%s: %s" f.Id f.Description)

                          // Prefix matching, per §12 — a host may name a position
                          // deeper than the corpus's stated slot and is then more
                          // precise rather than divergent.
                          Expect.isTrue
                              (path.StartsWith(f.ExpectedPath, System.StringComparison.Ordinal))
                              (sprintf
                                  "%s: refused at '%s', which is not at or below the corpus's '%s'"
                                  f.Id
                                  path
                                  f.ExpectedPath)
          } ]
