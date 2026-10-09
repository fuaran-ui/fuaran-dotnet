module Fuaran.UI.Program.Parity.Tests.OpaqueDispatchTests

// ─── `Dispatch` is an opaque leaf in the demanded document (Phase 2194) ──────
//
// `Action.Dispatch` carries a message for the host's own `update`, which no walk
// can see into. The UI witness views it as an OPAQUE leaf with reason class
// `in-process` (Program D40; WIRE_FORMAT §30.1's `Dispatch` row; this tier's
// DECISIONS D13), so the three artefacts a deployer reads say so:
//
//  1. the demanded projection names it in `opaqueLeaves`, and the document's
//     bytes carry it through a read-back;
//  2. a signed envelope over that projection carries the same member, so the
//     signature is over "cannot be analysed" rather than over "does nothing";
//  3. a host's coverage check refuses it until the host accepts the class —
//     the default this tier takes for every host it ships (D13).
//
// The lowering itself is certified against the corpus in `LowersToTests`; these
// tests pin what the lowering is FOR.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Program.Bounded
open Fuaran.UI.Program

let private btn (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            OnClick = action }

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

// Constructing the in-process arm raises FS0044; the suppression is scoped to
// the one binding that needs it.
#nowarn "44"

let private dispatch: Action<obj> = Action.Dispatch(box "a host message")

#warnon "44"

let private expected: OpaqueLeaf list =
    [ { Reason = "in-process"
        Name = "Dispatch" } ]

/// A tree whose one handler is a `Dispatch`, beside a static caption.
let private tree: Node<obj> =
    dash [ Fuaran.markdown "m" "static"; btn "send" dispatch ]

/// A sink that signs with a fixed string: the envelope's BYTES are the subject
/// here, not the cryptography, which `SignedEnvelopeTests` exercises for real.
let private sink: Fuaran.Core.IAttestationSink =
    { new Fuaran.Core.IAttestationSink with
        member _.Sign head =
            Some(
                { Head = head
                  KeyId = "test-key"
                  Signature = "unchecked" }
                : Fuaran.Core.Attestation
            )

        member _.Verify _ _ = false }

[<Tests>]
let tests =
    testList
        "Dispatch is an opaque leaf (Phase 2194)"
        [ test "the demanded projection names it, and the document's bytes carry it" {
              let projection = Demanded.ofTree tree
              Expect.equal projection.OpaqueLeaves expected "the projection names the escape and its class"
              Expect.isEmpty projection.Effects "and claims no effect it cannot see"

              let bytes = Fuaran.Program.Bounded.Demanded.encode projection

              match Fuaran.Program.Bounded.Demanded.decode bytes with
              | Ok read -> Expect.equal read.OpaqueLeaves expected "the document reads back with the escape named"
              | Error failure -> failtestf "the demanded document does not read back: %A" failure
          }

          test "a decoded wire Dispatch (the inert sentinel) is named the same way" {
              match UiWitness.decodeAction (Fuaran.Core.JObj [ "$type", Fuaran.Core.JStr "Dispatch" ]) with
              | Ok action ->
                  Expect.equal (Demanded.ofAction action).OpaqueLeaves expected "the wire's Dispatch is the same escape"
              | Error refusal -> failtestf "a wire Dispatch does not decode: %A" refusal
          }

          test "the signed envelope carries it" {
              match SignedEnvelope.sign sink Demanded.ofTree tree with
              | Ok signed ->
                  match Fuaran.Program.Bounded.Demanded.decode signed.Envelope with
                  | Ok read -> Expect.equal read.OpaqueLeaves expected "what was signed names the escape"
                  | Error failure -> failtestf "the signed document does not read back: %A" failure
              | Error refusal -> failtestf "sign refused: %A" refusal
          }

          test "coverage refuses it by default, and accepts it only when the host accepts the class (D13)" {
              Expect.equal
                  (Demanded.check HostCoverage.nothing tree)
                  [ CoverageFinding.UnacceptedOpaqueLeaf("in-process", "Dispatch") ]
                  "a host that has not accepted `in-process` is told so"

              Expect.isEmpty
                  (Demanded.check (HostCoverage.nothing |> HostCoverage.acceptingOpaque [ "in-process" ]) tree)
                  "a host that accepts the class is not"

              Expect.equal
                  (Demanded.check (HostCoverage.nothing |> HostCoverage.acceptingOpaque [ "teleported" ]) tree)
                  [ CoverageFinding.UnacceptedOpaqueLeaf("in-process", "Dispatch") ]
                  "accepting another class accepts nothing here"
          }

          test "the confirm carriers that ride Dispatch's slot are not the escape" {
              let confirm = Action.Confirm(TextSource.Literal "Sure?", Action.Print, None)

              Expect.isEmpty (Demanded.ofAction confirm).OpaqueLeaves "a confirm's question and its arms are analysable"

              Expect.isEmpty
                  (Demanded.ofTree (dash [ btn "c" (UiWitness.address confirm) ])).OpaqueLeaves
                  "an addressed confirm (carried in-process) is still a confirm"
          } ]
