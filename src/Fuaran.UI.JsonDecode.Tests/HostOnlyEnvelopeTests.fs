module Fuaran.UI.JsonDecode.Tests.HostOnlyEnvelopeTests

// ============================================================================
//  `motion` and `extraAttributes` are HOST-ONLY, not dropped — Phase 1647.
//
//  A residue finding said that fuaran-py's decoder "drops `motion` and
//  `extraAttributes` from the node envelope (§3.1) — the same silent-omission
//  class as the `tooltip` drop it fixed", and that no corpus fixture carried
//  either member, "which is why every host's constant-green suite says
//  nothing". The remedy proposed was to author corpus fixtures carrying both,
//  "so the omission goes red on every host at once".
//
//  Measured, the premise is FALSE and the remedy would have been unauthorable.
//  Both members are declared in `src/Fuaran.UI.Idl/idl.json` with
//  `optionality: hostOnly`, `wire: "<closure>"` and `typescript: "never"`, so:
//
//    * the generated ENCODER emits no key for either, whatever the F# record
//      carries — an attempted fixture with `Motion = Some PulseDuringLoad` and a
//      three-entry `ExtraAttributes` map emits bytes byte-identical to a node
//      carrying neither, which is a fixture that certifies nothing while looking
//      like the strongest kind of one;
//    * the generated DECODER hard-codes `Ok None` for both rather than reading a
//      key, so there is no wire member for any host to drop;
//    * fuaran-py is therefore CONFORMANT here, not defective. So is every other
//      host. The silence the finding noticed is real and is the correct silence.
//
//  Making them wire members is a vocabulary widening — the §11 forward-coupling
//  ceremony across every host plus the `docs/VOCABULARY.md` admission gates —
//  not a fixture nobody had got round to writing.
//
//  What this file is, then, is the finding recorded as an ASSERTION rather than
//  as prose: it pins the host-only posture at the encoder, so the day someone
//  widens either member the obligation announces itself here, in the repo where
//  the widening happens, instead of being rediscovered from the other end as a
//  host that "drops" a member it was never sent.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

module CanonicalJson = Fuaran.UI.OpStream.Abstractions.CanonicalJson

[<Tests>]
let tests =
    testList
        "Phase 1647 — the host-only envelope members carry no wire bytes"
        [ test "motion and extraAttributes never reach the wire, however the record is filled" {
              let bare: Node<obj> =
                  { Id = "host-only-probe"
                    Kind = NodeKind.Markdown({ Text = TextSource.Literal "probe" })
                    State = None
                    Style = None
                    Accessibility = None
                    Motion = None
                    ExtraAttributes = None
                    Tooltip = None
                    Visible = None }

              let filled =
                  { bare with
                      Motion = Some Motion.PulseDuringLoad
                      ExtraAttributes = Some(Map.ofList [ "data-testid", "probe"; "id", "p" ]) }

              let bareWire: string = CanonicalJson.encodeNode bare
              let filledWire: string = CanonicalJson.encodeNode filled

              Expect.isFalse
                  (bareWire.Contains "motion" || bareWire.Contains "extraAttributes")
                  "the empty node must carry neither key"

              Expect.equal
                  filledWire
                  bareWire
                  "`motion` and `extraAttributes` are declared hostOnly in idl.json, so filling them must not move a wire byte. If this test is red, one of them has become a WIRE member — which is a vocabulary widening: it owes the WIRE_FORMAT §11 forward-coupling set (spec, idl.json, corpus fixtures, every host's codec) and the docs/VOCABULARY.md admission gates, and it owes fuaran-py in particular a decoder arm, since it correctly reads neither today."
          } ]
