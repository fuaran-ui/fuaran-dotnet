module Fuaran.UI.Renderer.Affordances

// ============================================================================
//  Fuaran — the declared-affordance provider seam.
//
//  A page that was authored with natural-language command declarations knows
//  things a browser-hosted agent otherwise has to guess: which fields it may
//  drive, what phrasings the page already understands, which synonyms resolve,
//  and what values a field will accept. That knowledge is DECLARED by whoever
//  built the page — so an agent should be able to READ it rather than
//  rediscover it one failed attempt at a time.
//
//  This module is the seam through which a host publishes that declaration to
//  the in-page introspection surface (`window.__fuaran.getAffordances`) and,
//  through it, to the DevTools relay's `read.affordances` entry point.
//
//  ── What this module is, and deliberately is not ──────────────────────────
//
//  It is a REGISTRATION POINT and a VOCABULARY. It holds no declarations of its
//  own, derives nothing from the tree, and knows nothing about how any host
//  models a "field" or a "module". A host registers a provider; the surface
//  asks the registered providers; the answer is projected onto the wire. A page
//  with NO registered provider answers an EMPTY, well-formed enumeration —
//  never an error, because "this page declares no affordances" is a legitimate
//  answer to a legitimate question, and an error would make an ordinary page
//  indistinguishable from a broken one.
//
//  The vocabulary is therefore deliberately generic. `FieldShape` says how a
//  value is shaped, not what it means; `CommandEffect` says whether a phrase
//  reads, writes, navigates or invokes, not what any of those do; `ValueHint`
//  carries the three bound shapes an agent can act on without prose parsing.
//  Anything a host cannot express in that vocabulary belongs in `Description`,
//  which is free text an agent may render and MUST NOT parse.
//
//  ── Absence is the deny mechanism ─────────────────────────────────────────
//
//  A provider decides what to publish. A field or module a provider does not
//  return is simply not in the enumeration, and an agent cannot tell "withheld"
//  from "never existed" — which is the point. There is no `denied` marker and
//  no refusal class for a filtered module, because both would leak the
//  existence of what was filtered. `Controllable = false` is a DIFFERENT
//  statement: it means "you may ask about this, you may not set it", which is
//  only sayable about something the host chose to publish at all.
//
//  ── Purity ────────────────────────────────────────────────────────────────
//
//  FSharp.Core only, no browser dependency, identical on both pipelines — so
//  the .NET test runner pins the whole seam without a DOM, exactly as the
//  change hub and the relay protocol are pinned.
// ============================================================================

/// What a declared phrase does when it matches. A closed set: a client that
/// meets an unrecognised token treats the phrase as opaque and still renders it
/// (the relay's §10.3 posture for every closed set it carries).
[<RequireQualifiedAccess>]
type CommandEffect =
    /// Reports the current value of something. No mutation.
    | Read
    /// Sets a value.
    | Write
    /// Moves to another part of the application.
    | Navigate
    /// Runs a named operation whose effect is the host's business.
    | Invoke

[<RequireQualifiedAccess>]
module CommandEffect =

    /// The wire token. Kept beside the type so the projection cannot drift from
    /// the case set.
    let toWire (effect: CommandEffect) : string =
        match effect with
        | CommandEffect.Read -> "read"
        | CommandEffect.Write -> "write"
        | CommandEffect.Navigate -> "navigate"
        | CommandEffect.Invoke -> "invoke"

/// One declared natural-language command. `Phrase` is carried VERBATIM,
/// including any `{value}` slot syntax the host authored — the slot markers are
/// what tell an agent where its own words go, so normalising them away would
/// destroy the useful half of the declaration.
type DeclaredCommand =
    { Phrase: string
      Effect: CommandEffect }

/// The declared bound on a field's values, where there is one. Three shapes,
/// each one an agent can satisfy without parsing prose. A field with no
/// declared bound carries no hint at all rather than an "unconstrained" marker:
/// absence already says it, and a marker invites a client to branch on it.
[<RequireQualifiedAccess>]
type ValueHint =
    /// A closed set of accepted values, in the host's declared order.
    | OneOf of values: string list
    /// A numeric bound. Each end is optional — a half-open range is a real
    /// declaration, and forcing a sentinel at the open end would be a lie.
    | NumberRange of min: float option * max: float option * step: float option
    /// A bound on text length.
    | TextLength of minLength: int option * maxLength: int option

/// How a field's value is shaped. Deliberately coarse: an agent needs to know
/// whether to emit a number, a string, a member of a set or a flag, and nothing
/// finer is portable across hosts. `Unknown` is honest and available — a host
/// that cannot classify a field says so rather than guessing `Text`.
[<RequireQualifiedAccess>]
type FieldShape =
    | Text
    | Number
    | Boolean
    | Choice
    | Unknown

[<RequireQualifiedAccess>]
module FieldShape =

    let toWire (shape: FieldShape) : string =
        match shape with
        | FieldShape.Text -> "text"
        | FieldShape.Number -> "number"
        | FieldShape.Boolean -> "boolean"
        | FieldShape.Choice -> "choice"
        | FieldShape.Unknown -> "unknown"

/// One declared field an agent may address.
type FieldAffordance =
    {
        /// The name the host addresses this field by. Opaque to the client.
        Id: string
        Shape: FieldShape
        /// Whether a phrase may SET this field. A published field that is not
        /// controllable is readable and nothing more; a field that must not be
        /// touched at all is not published (see the header).
        Controllable: bool
        /// Declared phrases, in the host's declared order.
        Commands: DeclaredCommand list
        /// `(alias, canonical)` pairs — the synonyms the host will resolve on
        /// this field. Publishing them saves an agent a round of rejections it
        /// would otherwise have to learn from.
        Aliases: (string * string) list
        /// The declared value bound, where the host declared one.
        Values: ValueHint option
        /// Free-form human-readable note. A client MAY render it and MUST NOT
        /// parse it — everything machine-actionable is in the typed fields.
        Description: string option
    }

/// One declared module — a named region of the application with its own fields
/// and its own module-level phrases.
type ModuleAffordance =
    {
        Id: string
        /// Whether this module is the one currently in front of the user. A page
        /// with no notion of an active module reports `false` everywhere.
        Active: bool
        Fields: FieldAffordance list
        /// Phrases that address the module itself rather than one of its fields.
        Commands: DeclaredCommand list
    }

/// The whole answer. One field, so the shape stays additively extensible — a
/// later profile can add a sibling without moving `Modules`.
type AffordanceEnumeration = { Modules: ModuleAffordance list }

[<RequireQualifiedAccess>]
module AffordanceEnumeration =

    /// The answer a page with nothing declared gives. Well-formed, never an
    /// error — see the header.
    let empty: AffordanceEnumeration = { Modules = [] }

/// A source of declared affordances. The argument is a module-id HINT: a
/// provider MAY use it to avoid computing modules the caller did not ask for,
/// and MAY ignore it entirely, because `enumerate` filters the union afterwards
/// regardless. So a provider is never obliged to implement the filter, and a
/// provider that implements it wrongly cannot widen the answer.
type AffordanceProvider = string option -> AffordanceEnumeration

// ─── The provider registry ──────────────────────────────────────────────────
//
// Page-global mutable state, for the same reason `ChangeHub.pageHub` and the
// relay's published surface are: the surface object is rebuilt on every render,
// and a registration that lived inside one instance would be lost on the next.
// A host registers ONCE, at install time, and the handle it gets back is how a
// teardown removes it.

let mutable private providers: (int * AffordanceProvider) list = []
let mutable private nextProviderId = 0

/// Register a provider. Returns the handle that removes it again; calling the
/// handle more than once is harmless.
///
/// Providers are consulted in registration order and their modules concatenated,
/// with the FIRST declaration of a module id winning — so a host that installs
/// its own provider ahead of a library's can override that library's account of
/// a module without the library having to know.
let registerProvider (provider: AffordanceProvider) : unit -> unit =
    let id = nextProviderId
    nextProviderId <- nextProviderId + 1
    providers <- providers @ [ id, provider ]

    fun () -> providers <- providers |> List.filter (fun (existing, _) -> existing <> id)

/// How many providers are currently registered. For a host's own wiring
/// diagnostics; the enumeration itself is the answer a client reads.
let providerCount () : int = List.length providers

/// Remove every provider. Teardown / test hygiene.
let clearProviders () : unit =
    providers <- []
    nextProviderId <- 0

/// The current enumeration, optionally narrowed to one module.
///
/// Three properties this function owns, so no provider has to:
///
///  * **Total.** No registered provider, or every provider returning nothing,
///    yields the empty enumeration — never an error.
///  * **Filtered.** The module-id argument is applied HERE, to the union, so a
///    provider that ignored the hint still cannot answer wider than asked. An
///    unknown module id yields an empty enumeration rather than an error, for
///    the same reason a filtered one does: an agent must not be able to tell a
///    module that does not exist from one that was withheld.
///  * **Isolated.** A provider that throws is skipped and the remaining
///    providers still answer. A read of what a page declares must not be
///    brought down by one badly-behaved registration.
let enumerate (moduleId: string option) : AffordanceEnumeration =
    let collected =
        providers
        |> List.collect (fun (_, provider) ->
            try
                (provider moduleId).Modules
            with _ ->
                [])

    // First declaration of an id wins; `List.fold` keeps the order stable
    // rather than sorting, because a host's declared order is information.
    let deduped =
        collected
        |> List.fold
            (fun (seen: Set<string>, acc) m ->
                if seen.Contains m.Id then
                    seen, acc
                else
                    seen.Add m.Id, m :: acc)
            (Set.empty, [])
        |> snd
        |> List.rev

    let filtered =
        match moduleId with
        | None -> deduped
        | Some wanted -> deduped |> List.filter (fun m -> m.Id = wanted)

    { Modules = filtered }
