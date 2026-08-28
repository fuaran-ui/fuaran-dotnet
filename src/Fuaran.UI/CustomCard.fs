namespace Fuaran.UI

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  Contract cards — the artefact that makes a foreign `Custom` node LEGIBLE
//  (Phase 1108; WIRE_FORMAT.md §25).
//
//  THE PROBLEM. A registered contract is first-class inside its own deployment:
//  the orchestrator emits against its declared prop schema, the validator checks
//  the prop bag, the renderer dispatches to the registered component. Cross the
//  deployment boundary and every one of those disappears at once. A host that
//  receives the same node has no contract, no schema, no renderer — so it emits
//  an identity-only placeholder and a reader learns nothing beyond two strings
//  they could have read off the wire themselves.
//
//  What the deployment had, and the foreign host did not, was never a RENDERER.
//  It was the DESCRIPTION: the prop rows, the content hash, the payload
//  languages, and one line saying what the thing is. `CustomKindCard` already
//  assembled all of it — for the orchestrator's prompt context — and it existed
//  only in-process. Making that card a specified, transportable artefact turns
//  "opaque elsewhere" into "legible-but-unrendered elsewhere" at a fraction of
//  the cost of a portable renderer, which is a much larger and much less certain
//  thing to build.
//
//  WHAT A CARD IS NOT. It is not a renderer, not a permission, and not evidence
//  that the component is safe to run. Nothing here dispatches to anything; a
//  card's only consumers are a prop VALIDATOR and a PLACEHOLDER. The trust
//  boundary is exactly where Phase 141 put it — at the host's own registry — and
//  a card arriving from anywhere cannot move it.
//
//  THE THREE-WAY HASH VERDICT is the load-bearing detail, and it is why this
//  module is more than a formatter. A card names a `moduleId`/`componentId`
//  pair, and a pair is an ADDRESS: two deployments can perfectly well ship
//  different components at the same address, and the same component at two
//  versions certainly will. So a card that matches by name is not thereby a
//  description of THIS node.
//
//    * `Matches`    — the node declared a hash and it equals the card's. The
//                     card describes this node; show everything.
//    * `Unverified` — there is nothing to compare (the node declared no hash, or
//                     the two name different algorithms). The card describes a
//                     component of this NAME. Show it, and say the claim is
//                     unverified — degrading to identity-only here would throw
//                     away the common case for no gain.
//    * `Mismatch`   — the node declared a hash and it differs. The card
//                     describes a DIFFERENT shape at the same address, so its
//                     summary and its prop rows are withheld: printing them
//                     would be the guess the degradation obligation forbids,
//                     and a confident wrong description is worse than none.
//
//  Pure data + FSharp.Core + the Core JSON model. The CODEC lives in
//  `Fuaran.UI.Ops.CustomCardJson` (it speaks the §6 `DecodeError` envelope,
//  which is declared there); everything a RENDERER needs is here, so both the
//  Fable client and the server renderer reach it without taking a decoder
//  dependency.
// ============================================================================

/// Whether a card can be said to describe the node in front of it. See the
/// header — the three cases carry different licences to speak, not different
/// degrees of confidence in one answer.
[<RequireQualifiedAccess>]
type CardHashVerdict =
    /// The node's declared hash equals the card's.
    | Matches
    /// Nothing to compare: the node declared no hash, or the two name different
    /// algorithms. Carries the reason so a surface can say WHICH.
    | Unverified of reason: string
    /// The node's declared hash differs from the card's — same address, other
    /// shape.
    | Mismatch

/// The card-derived answers about one prop bag: what is WRONG with it, what is
/// still OWED on it, and what this reader could not judge at all.
///
/// The third list is the one a card introduces and a registry never needed. A
/// registry holds the typed `PropType`; a card holds its TAG, and a card written
/// by a newer producer can name a type this consumer has never heard of.
/// Resolving that to "anything goes" would silently turn a check into a pass, so
/// it is reported as its own class — not a defect on the node (which may be
/// perfectly correct) but a stated limit on the reader.
type CardValidation =
    { Defects: CustomPropDefect list
      Obligations: CustomPayloadObligation list
      Unresolvable: string list }

/// Everything a host needs to emit an honest placeholder for a `Custom` node it
/// cannot render, derived from a card rather than invented.
///
/// Derived rather than formatted-at-the-call-site on purpose: the degradation
/// obligation is a claim about EMITTED OUTPUT across hosts, so the two hosts must
/// agree on what the lines say before they can agree on the markup carrying
/// them.
type CardPlaceholder =
    {
        ModuleId: string
        ComponentId: string
        /// The identity line, always present and identical to the one an
        /// unregistered-and-uncarded node already emits.
        Label: string
        /// The card's one-line summary — `None` where the card declares none, and
        /// `None` under `Mismatch` whatever the card says.
        Summary: string option
        /// One line per declared prop: name, type tag, whether it is required,
        /// and the payload language where one is declared. Never a prop VALUE:
        /// the node's props are data the host was not asked to interpret, and
        /// spilling them into a placeholder is an information leak with no
        /// legibility gain. Empty under `Mismatch`.
        PropLines: string list
        HashVerdict: CardHashVerdict
        Validation: CardValidation
    }

module CustomCard =

    /// The `$card` format-version tag (WIRE_FORMAT.md §25.1). A single document
    /// version; an evolution is an explicit bump, never a silently-widened shape.
    [<Literal>]
    let formatVersion = "1"

    /// The `$cards` format-version tag of a card BUNDLE (§25.2).
    [<Literal>]
    let bundleFormatVersion = "1"

    /// The documented default location a host MAY publish its bundle at (§25.6).
    ///
    /// v1 transport is deliberately a convention and nothing more — no registry
    /// service, no fetching protocol, no negotiation. A host supplies its cards
    /// however it likes (bundled beside the app, read from disk, served here);
    /// this constant exists so that the hosts which do choose to serve them all
    /// choose the same path, which is the whole of what a convention buys.
    [<Literal>]
    let wellKnownPath = "/.well-known/fuaran-cards.json"

    /// The identity line every placeholder carries, carded or not. Byte-identical
    /// to the text the pre-1108 unregistered path already emitted, which is what
    /// makes "a host with neither card nor renderer is unchanged" true by
    /// construction rather than by re-checking.
    let label (moduleId: string) (componentId: string) : string =
        "[fuaran:custom " + moduleId + "." + componentId + "]"

    /// The payload annotation a card row carries, rendered exactly as
    /// `CustomRegistry.payloadTag` renders the contract-side declaration — the
    /// two must print the same thing or a reader comparing a card against the
    /// deployment that issued it sees a difference that is not there.
    let payloadTag (language: string option) (gate: string option) : string option =
        match language with
        | None -> Option.None
        | Some l ->
            match gate with
            | Some g -> Some(l + " (gate " + g + ")")
            | Option.None -> Some(l + " (NO GATE)")

    /// One prop row as a placeholder prints it.
    let propLine (p: CustomPropCard) : string =
        let required = if p.Required then " (required)" else ""

        match payloadTag p.PayloadLanguage p.PayloadGate with
        | Some tag -> p.Name + ": " + p.Type + required + " [" + tag + "]"
        | Option.None -> p.Name + ": " + p.Type + required

    /// Compare a node's declared content hash against the card's. See the header
    /// for why the three cases are not three confidence levels.
    let verifyHash (declared: ContentHash option) (card: CustomKindCard) : CardHashVerdict =
        match declared with
        | None -> CardHashVerdict.Unverified "the node declares no content hash, so there is nothing to compare"
        | Some d when d.Algorithm <> card.Hash.Algorithm ->
            CardHashVerdict.Unverified(
                "the node's hash is "
                + d.Algorithm
                + " and the card's is "
                + card.Hash.Algorithm
                + "; two digests under different algorithms cannot be compared"
            )
        | Some d when d.Hash = card.Hash.Hash -> CardHashVerdict.Matches
        | Some _ -> CardHashVerdict.Mismatch

    /// The stable marker a placeholder emits so the verdict is machine-readable
    /// and not merely legible. It is what the cross-host degradation obligation
    /// is asserted against (WIRE_FORMAT.md §25.4): prose in a placeholder is for
    /// a person, and a conformance suite needs a token.
    let verdictMarker (verdict: CardHashVerdict) : string =
        match verdict with
        | CardHashVerdict.Matches -> "described"
        | CardHashVerdict.Unverified _ -> "unverified"
        | CardHashVerdict.Mismatch -> "hash-mismatch"

    /// Recover a `PropSchema` from a card's rows, naming the rows whose type tag
    /// this build cannot resolve rather than guessing one for them.
    let toPropSchema (card: CustomKindCard) : PropSchema * string list =
        let resolved, unresolvable =
            card.Props
            |> List.fold
                (fun (acc, bad) p ->
                    match CustomRegistry.tryParsePropTypeTag p.Type with
                    | Some t ->
                        let declaration =
                            p.PayloadLanguage
                            |> Option.map (fun l ->
                                { Language = l
                                  Gate =
                                    p.PayloadGate
                                    |> Option.map (fun stamp ->
                                        // The stamp is `gate:version`, and a gate
                                        // name may itself contain a colon, so the
                                        // split is on the LAST one. A bare name
                                        // (no colon at all) is the empty-version
                                        // form `AsStamp` emits, not a malformed
                                        // stamp.
                                        match stamp.LastIndexOf ':' with
                                        | -1 -> { Gate = stamp; Version = "" }
                                        | i ->
                                            { Gate = stamp.Substring(0, i)
                                              Version = stamp.Substring(i + 1) }) })

                        (acc
                         @ [ { Name = p.Name
                               Type = t
                               Required = p.Required
                               PayloadLanguage = declaration } ]),
                        bad
                    | Option.None -> acc, bad @ [ p.Name ])
                ([], [])

        resolved, unresolvable

    /// Prop-validate a node's bag against a CARD — the same check a host holding
    /// the contract performs, available to a host that holds only the
    /// description. This is the half of the phase that is not cosmetic: a foreign
    /// host can now say a `Custom` node is malformed, where before it could only
    /// fail to render it.
    let validate (card: CustomKindCard) (props: Map<string, JVal>) : CardValidation =
        let schema, unresolvable = toPropSchema card
        let validation = CustomRegistry.validateSchema schema props

        { Defects = validation.Defects
          Obligations = validation.Obligations
          Unresolvable = unresolvable }

    /// Derive the whole placeholder for one node from one card.
    ///
    /// Under `Mismatch` the summary and the prop rows are withheld — see the
    /// header. Everything else (identity, verdict, and the fact that validation
    /// was not attempted) is still emitted, because a mismatch is a thing a
    /// reader most wants told rather than hidden.
    let describe (declared: ContentHash option) (props: Map<string, JVal>) (card: CustomKindCard) : CardPlaceholder =
        let verdict = verifyHash declared card

        let withheld = verdict = CardHashVerdict.Mismatch

        { ModuleId = card.ModuleId
          ComponentId = card.ComponentId
          Label = label card.ModuleId card.ComponentId
          Summary = (if withheld then Option.None else card.Summary)
          PropLines = (if withheld then [] else card.Props |> List.map propLine)
          HashVerdict = verdict
          Validation =
            if withheld then
                { Defects = []
                  Obligations = []
                  Unresolvable = [] }
            else
                validate card props }

/// A host-supplied lookup of contract cards, keyed on `(moduleId, componentId)`.
///
/// Deliberately the same shape as `CustomRegistry` and deliberately NOT the same
/// thing. A registry says "I can render this"; a store says "I can describe
/// this". A host may hold either, both, or neither, and the four combinations
/// are all meaningful — which is exactly why folding cards into the renderer
/// registry (the obvious economy) would have been wrong: it would have made a
/// description unobtainable except where a renderer already existed, i.e. in
/// every case except the one this phase is about.
type CustomCardStore private (entries: Map<string * string, CustomKindCard>) =

    static member Empty = CustomCardStore(Map.empty)

    /// Add a card. Re-adding the same identity replaces it — the last writer
    /// wins, matching `CustomRegistry.Register`. A BUNDLE refuses duplicates at
    /// decode time (§25.2), which is where the ambiguity is detectable; by the
    /// time a host is folding cards into a store it has already chosen an order.
    member _.Add(card: CustomKindCard) : CustomCardStore =
        CustomCardStore(Map.add (card.ModuleId, card.ComponentId) card entries)

    /// The card for an identity, or `None`.
    member _.TryFind(moduleId: string, componentId: string) : CustomKindCard option =
        Map.tryFind (moduleId, componentId) entries

    /// Every card held, ordered by identity so a projection of the store is
    /// stable across runs.
    member _.Cards: CustomKindCard list = entries |> Map.toList |> List.map snd

    member _.Count = entries.Count

[<RequireQualifiedAccess>]
module CustomCardStore =
    /// Fold a card list into a store.
    let ofCards (cards: CustomKindCard list) : CustomCardStore =
        cards |> List.fold (fun (s: CustomCardStore) c -> s.Add c) CustomCardStore.Empty
