module Fuaran.UI.Messaging

// ============================================================================
//  Typed cross-host messaging — a host-tier overlay on an untyped wire primitive.
//
//  `Action.Dispatch of 'Msg` carries a host CLOSURE. Closures cannot be
//  serialised, so the canonical encoder renders them as the `"<closure>"`
//  sentinel: a dispatch survives in-process (Fable, where the tree never crosses
//  a wire) and is lost the moment the tree is encoded. That is correct for a
//  host-neutral wire — a host's private message type has no business being in
//  it — but it means a wire-fed renderer can observe THAT an interaction
//  happened and never WHICH message the author meant.
//
//  This module closes that without changing the wire and without giving up
//  typing. The observation it rests on: `Action.Notify of channel: string *
//  payload: JVal` is ALREADY a named message with a data payload, already
//  encodes and decodes for real (not a sentinel), and is already routed by every
//  host through `IFuaranRuntime.Notify`. So the cross-host primitive exists; what
//  was missing is a typed way to reach it.
//
//  A `MessageContract<'Msg>` is that typed layer:
//
//    * the author writes a typed message and gets one back — statically checked
//      at BOTH ends, because in the usual deployment both ends are the same
//      codebase (an ASP.NET host serving a tree, and receiving the interaction);
//    * the wire carries a plain `Notify`, so a C# / VB / TypeScript host sees
//      `(channel, payload)` and routes it with identical functionality, just
//      without the typing its language cannot express;
//    * nothing in the wire format, the canonical encoding, or the hash-chain
//      changes, and no new `Action` case is admitted.
//
//  Typing is therefore kept where a language can express it and degrades to a
//  stringly interface only where it cannot, rather than being surrendered
//  everywhere to accommodate the weakest host.
//
//  WHAT IS STILL NOT STATICALLY CHECKED, stated plainly:
//   * the channel STRING at a cross-LANGUAGE boundary (F# emitting "SetYear"
//     against a TS handler listening for "setYear"). `Channels` below is the
//     manifest a generator or a conformance fixture pins that against.
//   * an LLM-emitted tree, which cannot know a host's message type at all. The
//     untyped form is the only correct one there.
//   * the contract value itself, which is hand-written. `roundTrips` is its law;
//     property-test it over generated messages and that hole closes.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types

/// A host's typed message vocabulary, expressed as data so it can cross the wire.
///
/// `Lower` and `Lift` are inverses — see `roundTrips`, which is the law every
/// contract must satisfy and the thing to property-test.
type MessageContract<'Msg> =
    {
        /// Lower a typed message to the wire-representable pair.
        Lower: 'Msg -> string * JVal
        /// Lift a pair back to a typed message. `None` when the channel is not
        /// one this contract owns, so several contracts can be tried in turn and
        /// an unknown channel is a miss rather than a crash.
        Lift: string -> JVal -> 'Msg option
        /// Every channel this contract can emit. The manifest a cross-language
        /// host generates its constants from, and what a conformance fixture
        /// pins so the two sides cannot drift apart silently.
        Channels: string list
    }

[<RequireQualifiedAccess>]
module MessageContract =

    /// Build a contract from the lowering and lifting functions plus the channel
    /// manifest. A named constructor so the channel list is never forgotten.
    let create (channels: string list) (lower: 'Msg -> string * JVal) (lift: string -> JVal -> 'Msg option) =
        { Lower = lower
          Lift = lift
          Channels = channels }

    /// The channel a message lowers onto, without building the payload.
    let channelOf (contract: MessageContract<'Msg>) (msg: 'Msg) : string = fst (contract.Lower msg)

    /// True when the contract claims this channel.
    let owns (contract: MessageContract<'Msg>) (channel: string) : bool = List.contains channel contract.Channels

    /// THE LAW: lowering a message and lifting it back yields the same message.
    ///
    /// A contract that fails this silently drops or corrupts interactions, which
    /// is exactly the failure the typed layer exists to prevent — so property-test
    /// it over generated messages rather than trusting the hand-written witness.
    let roundTrips (contract: MessageContract<'Msg>) (msg: 'Msg) : bool =
        let channel, payload = contract.Lower msg

        match contract.Lift channel payload with
        | Some lifted -> lifted = msg
        | None -> false

    /// Every channel a message can lower onto is declared in `Channels`. The
    /// second half of the manifest's honesty: `roundTrips` proves the data
    /// survives, this proves the manifest is complete.
    let declaresChannelFor (contract: MessageContract<'Msg>) (msg: 'Msg) : bool = owns contract (channelOf contract msg)

/// The wire-crossing twin of `Action.Dispatch`.
///
/// Same authoring ergonomics — hand it a typed message — but it lowers to a
/// `Notify`, so the message survives serialisation and every host can route it.
/// Use this anywhere the tree may be encoded (a server-rendered host, an
/// op-stream, an embedded browser renderer); `Action.Dispatch` remains correct
/// for the in-process Fable path, where nothing is ever serialised.
let dispatchTyped (contract: MessageContract<'Msg>) (msg: 'Msg) : Action<'Msg> =
    let channel, payload = contract.Lower msg
    Action.Notify(channel, payload)

/// The receiving half: route an incoming `(channel, payload)` — what a host's
/// `IFuaranRuntime.Notify` implementation is handed — back to a typed message.
///
/// `None` means no contract owned the channel, which a host should treat the way
/// it treats any other unrecognised input rather than ignoring silently.
let route (contract: MessageContract<'Msg>) (channel: string) (payload: JVal) : 'Msg option =
    contract.Lift channel payload

/// Try several contracts in order — a host composed of modules, each owning its
/// own message vocabulary, routes through one call.
let routeAny (contracts: MessageContract<'Msg> list) (channel: string) (payload: JVal) : 'Msg option =
    contracts |> List.tryPick (fun c -> c.Lift channel payload)
