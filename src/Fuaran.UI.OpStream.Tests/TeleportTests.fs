module Fuaran.UI.OpStream.Tests.TeleportTests

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
                            { Fields =
                                [ { Id = "draft-name"
                                    Label = TextSource.Literal "Full name"
                                    Kind = FormFieldKind.textDeclarative (binding.state "draft-name" "")
                                    Required = true
                                    Help = None
                                    Rule = None }
                                  { Id = "draft-team"
                                    Label = TextSource.Literal "Team"
                                    Kind = FormFieldKind.textDeclarative (binding.state "draft-team" "")
                                    Required = false
                                    Help = None
                                    Rule = None } ]
                              OnSubmit = Action.SetState("wizard-step", Some(JInt 2), None)
                              SubmitLabel = TextSource.Literal "Continue"
                              Disabled = None }
                        Fuaran.markdown "step-review" "All done — review and finish." ]
                    // Closure-carrying: encodes as the "<closure>" sentinel.
                    OnSelect = Some(fun i -> Action.Dispatch(Selected i)) }
              Fuaran.button
                  "wiz-share"
                  { Label = TextSource.Literal "Share this session"
                    // Wire-survivable: rides the bundle as data.
                    OnClick = Action.WriteToClipboard "https://demo.example/teleport"
                    Variant = ButtonVariant.Secondary
                    Icon = None
                    Tooltip = None
                    Disabled = None } ] }

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
          } ]
