namespace Fuaran.UI.Operability

// ============================================================================
//  Fuaran — a tree's operable surface, as a specified projection of it
//  (Phase 2517; normative text: WIRE_FORMAT §31).
//
//  `derive` reads what a tree already says about each field-bearing node — its
//  name, its label, its value space, its own action — plus the node's optional
//  declared `affordance`, and yields the affordances an agent-initiated
//  instruction may address. `resolve` is the reference matcher of the
//  resolution contract over those affordances.
//
//  Both are pure and total, FSharp.Core + Fuaran.UI only, and Fable-clean: the
//  same functions answer in the browser, in SSR and in the reference host's
//  corpus emitter, so every host reads the same affordances.
//
//  What this is NOT: an understanding of language. The matcher matches declared
//  forms against the WHOLE instruction; an instruction no form matches is
//  refused (`NotResolved`). That is the safety property, not a limitation to be
//  engineered away — see §31.6 "What is not claimed".
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

/// What resolving an affordance does (§31.3). Closed.
[<RequireQualifiedAccess>]
type AffordanceKind =
    /// Writes a value into the node's value space. Carries a typed slot.
    | Set
    /// Clears the node's value. Only where the node's affordance says `clearable`.
    | Clear
    /// Activates a button.
    | Click
    /// Activates a button whose own action is a navigation.
    | Navigate
    /// Reports the node's current value. Never dispatches; only a question-shaped
    /// instruction resolves to one.
    | Read

[<RequireQualifiedAccess>]
module AffordanceKind =
    /// The wire token, also the suffix of an affordance id.
    let toWire (kind: AffordanceKind) : string =
        match kind with
        | AffordanceKind.Set -> "set"
        | AffordanceKind.Clear -> "clear"
        | AffordanceKind.Click -> "click"
        | AffordanceKind.Navigate -> "navigate"
        | AffordanceKind.Read -> "read"

/// One member of an enumeration slot: the canonical option value, its label, and
/// the spoken aliases the node's affordance declares for it.
type SlotOption =
    { Value: string
      Label: string
      Aliases: string list }

/// The typed slot of a `Set` affordance (§31.3), read off the node's value space.
[<RequireQualifiedAccess>]
type Slot =
    /// A closed option set. A slot text matches an option's label, its value or
    /// one of its aliases (ASCII case-insensitively) and yields the option VALUE.
    | Enumeration of options: SlotOption list
    /// A decimal numeral within the declared bounds; `integral` refuses a fraction.
    | Number of min: float option * max: float option * integral: bool
    /// `on` / `off` and their spoken synonyms; yields `true` / `false`.
    | Boolean
    /// A bounded string. Only ever the slot of an AUTHORED pattern on a free-text
    /// node: the derivation never gives free text a slot of its own (§31.3).
    | Text of maxLength: int

/// What a resolved clause carries for its affordance.
[<RequireQualifiedAccess>]
type SlotValue =
    /// A `Clear`, `Click`, `Navigate` or `Read` — nothing to carry.
    | NoValue
    /// A `Set`: the canonical value the slot decoded.
    | Value of string

[<RequireQualifiedAccess>]
module SlotValue =
    let toOption (value: SlotValue) : string option =
        match value with
        | SlotValue.NoValue -> None
        | SlotValue.Value v -> Some v

/// One affordance of a tree (§31.3).
type OperableAffordance<'Msg> =
    {
        /// `<qualified node name>:<kind>` — unique within one derivation, and the
        /// string `ActionDescriptor.Operate` carries to the dispatch gate.
        Id: string
        /// The node name, qualified by the host's mount id when one was supplied
        /// (`<mount>/<name>`). What `data-ai-name` carries is the bare name.
        Node: string
        Kind: AffordanceKind
        /// The label the forms were derived from, ASCII-lowercased.
        Label: string
        /// The forms a clause must equal (no slot) or match around its `{value}`
        /// slot — the label-derived defaults first, then the authored patterns.
        Forms: string list
        Slot: Slot option
        /// The node's declared description, verbatim. Rendered, never parsed.
        Description: string option
        /// The node's OWN action applied to the slot value — `None` when the node
        /// has nothing to dispatch for it (a `Read`, or a value node with neither a
        /// handler nor a state-bound value to write back to).
        Resolve: SlotValue -> Action<'Msg> option
    }

/// The answer of the resolution contract (§31.5).
[<RequireQualifiedAccess>]
type Outcome =
    /// Every clause resolved to exactly one affordance — in clause order.
    | Resolved of (string * SlotValue) list
    /// Some clause matched no form (or the instruction broke a cap). Nothing is
    /// dispatched: resolution is all-or-nothing.
    | NotResolved
    /// Some clause could be claimed by more than one affordance (or one
    /// affordance with two values). The ids of the first such clause.
    | Ambiguous of string list

[<RequireQualifiedAccess>]
module Operability =

    // ─── Text normalisation (§31.4) ──────────────────────────────────────────
    //
    // ASCII-only, deliberately. A Unicode lowercasing differs between runtimes
    // (one maps U+0130 to one code unit, another to two), so a culture-aware
    // fold would make one instruction resolve differently on two hosts. Folding
    // A-Z alone is length-preserving, which is also what lets a free-text slot
    // be captured from the instruction's ORIGINAL spelling at the same offsets.

    /// The slot token of a form. A form carries at most one.
    [<Literal>]
    let SlotToken = "{value}"

    /// The instruction length cap, in UTF-16 code units of the raw instruction.
    [<Literal>]
    let MaxInstructionLength = 512

    /// The clause cap: an instruction splitting into more clauses resolves to nothing.
    [<Literal>]
    let MaxClauses = 8

    /// The bound a free-text slot carries when an authored pattern gives one.
    [<Literal>]
    let TextSlotMaxLength = 256

    let private isSpace (c: char) =
        c = ' ' || c = '\t' || c = '\n' || c = '\r'

    let private asciiLower (c: char) =
        if c >= 'A' && c <= 'Z' then char (int c + 32) else c

    /// ASCII-lowercase. Length-preserving.
    let lower (text: string) : string =
        System.String(text.ToCharArray() |> Array.map asciiLower)

    /// Collapse every run of ASCII whitespace to one space and trim the ends.
    let collapse (text: string) : string =
        let sb = System.Text.StringBuilder()
        let mutable pendingSpace = false

        for c in text do
            if isSpace c then
                pendingSpace <- sb.Length > 0
            else
                if pendingSpace then
                    sb.Append ' ' |> ignore
                    pendingSpace <- false

                sb.Append c |> ignore

        sb.ToString()

    let private words (text: string) : string list =
        text.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray

    // ─── Derivation (§31.3) ──────────────────────────────────────────────────

    let private labelOf (name: string) (label: TextSource) : string =
        let text =
            match label with
            | TextSource.Literal t -> collapse (lower t)
            | _ -> ""

        if text <> "" then
            text
        else
            // A bound or translated label has no text a derivation can read
            // without a host, so the node name stands in, spelled as words.
            collapse (lower (name.Replace('-', ' ').Replace('_', ' ')))

    let private authored (patterns: string list) : string list =
        patterns |> List.map (lower >> collapse) |> List.filter (fun p -> p <> "")

    let private staticOptions (source: Binding<SelectOption list>) : SelectOption list option =
        match source with
        | Binding.Static(Some options) -> Some options
        | _ -> None

    let private enumeration (affordance: Affordance option) (options: SelectOption list) : Slot =
        let aliases =
            affordance |> Option.map (fun a -> a.Aliases) |> Option.defaultValue []

        options
        |> List.map (fun o ->
            { Value = o.Value
              Label = o.Label
              Aliases =
                aliases
                |> List.filter (fun a -> a.Value = o.Value)
                |> List.map (fun a -> collapse (lower a.Alias)) })
        |> Slot.Enumeration

    let private stateKeyOf (binding: Binding<'T> option) : string option =
        match binding with
        | Some(Binding.State(key, _)) -> Some key
        | _ -> None

    let private idOf (node: string) (kind: AffordanceKind) = node + ":" + AffordanceKind.toWire kind

    let private qualify (mountId: string option) (name: string) : string =
        match mountId with
        | Some mount when mount <> "" -> mount + "/" + name
        | _ -> name

    let private make
        (node: string)
        (kind: AffordanceKind)
        (label: string)
        (forms: string list)
        (slot: Slot option)
        (description: string option)
        (resolve: SlotValue -> Action<'Msg> option)
        : OperableAffordance<'Msg> =
        { Id = idOf node kind
          Node = node
          Kind = kind
          Label = label
          Forms = forms |> List.distinct
          Slot = slot
          Description = description
          Resolve = resolve }

    /// The affordances of one VALUE node — a `Select` or a `Form` field — given
    /// its typed slot (`None` for free text), its write and its clear.
    let private valueAffordances
        (node: string)
        (label: string)
        (affordance: Affordance option)
        (slot: Slot option)
        (write: string -> Action<'Msg> option)
        (clear: unit -> Action<'Msg> option)
        : OperableAffordance<'Msg> list =
        let patterns =
            affordance
            |> Option.map (fun a -> authored a.Patterns)
            |> Option.defaultValue []

        let readPatterns =
            affordance
            |> Option.map (fun a -> authored a.ReadPatterns)
            |> Option.defaultValue []

        let description = affordance |> Option.bind (fun a -> a.Description)

        let resolveSet (value: SlotValue) =
            match value with
            | SlotValue.Value v -> write v
            | SlotValue.NoValue -> None

        let set =
            match slot with
            // A typed value space: the label-derived set forms, then the authored ones.
            | Some typed ->
                [ make
                      node
                      AffordanceKind.Set
                      label
                      ([ "set " + label + " to " + SlotToken; "change " + label + " to " + SlotToken ]
                       @ patterns)
                      (Some typed)
                      description
                      resolveSet ]
            // Free text: no slot of its own, so no default forms. An authored
            // pattern IS the deliberate slot, a bounded string (§31.3).
            | None when not (List.isEmpty patterns) ->
                [ make node AffordanceKind.Set label patterns (Some(Slot.Text TextSlotMaxLength)) description resolveSet ]
            | None -> []

        let clearable =
            affordance |> Option.map (fun a -> a.Clearable) |> Option.defaultValue false

        let clearOne =
            if clearable then
                [ make node AffordanceKind.Clear label [ "clear " + label ] None description (fun _ -> clear ()) ]
            else
                []

        let read =
            make node AffordanceKind.Read label ([ "what is " + label ] @ readPatterns) None description (fun _ -> None)

        set @ clearOne @ [ read ]

    let private selectAffordances (mountId: string option) (name: string) (spec: SelectSpec<'Msg>) =
        let node = qualify mountId name
        let label = labelOf name spec.Label

        // A multi-select writes a LIST; a single slot value cannot say which
        // members to keep, so it is read-only here (§31.3).
        let multiple = spec.Multiple = Some true

        let slot =
            if multiple then
                None
            else
                staticOptions spec.Source |> Option.map (enumeration spec.Affordance)

        let write (v: string) : Action<'Msg> option =
            match spec.OnChange with
            | Some onChange -> Some(onChange (Some v))
            | None ->
                stateKeyOf spec.Value
                |> Option.map (fun key -> Action.SetState(key, Some(JStr v), None))

        let clear () : Action<'Msg> option =
            match spec.OnChange with
            | Some onChange -> Some(onChange None)
            | None ->
                stateKeyOf spec.Value
                |> Option.map (fun key -> Action.SetState(key, None, None))

        if multiple then
            // Read only: no set pattern can reach a list, and nothing clears one.
            valueAffordances
                node
                label
                (spec.Affordance
                 |> Option.map (fun a ->
                     { a with
                         Patterns = []
                         Clearable = false }))
                None
                write
                clear
        else
            valueAffordances node label spec.Affordance slot write clear

    let private fieldSlot (affordance: Affordance option) (kind: FormFieldKind<'Msg>) : Slot option =
        let integralStep (step: float option) =
            match step with
            | Some s -> s = floor s
            | None -> false

        match kind with
        | FormFieldKind.Number _ -> Some(Slot.Number(None, None, false))
        | FormFieldKind.RangedNumber(_, _, min, max, step)
        | FormFieldKind.Range(_, _, min, max, step) -> Some(Slot.Number(min, max, integralStep step))
        | FormFieldKind.Rating(allowHalf, max, _, _) -> Some(Slot.Number(Some 0.0, Some(float max), not allowHalf))
        | FormFieldKind.Checkbox _
        | FormFieldKind.Toggle _ -> Some Slot.Boolean
        | FormFieldKind.Choice(options, _, _)
        | FormFieldKind.SegmentedChoice(options, _, _, _)
        | FormFieldKind.Combobox(_, _, options, _) -> staticOptions options |> Option.map (enumeration affordance)
        | FormFieldKind.Text _
        | FormFieldKind.TextArea _
        | FormFieldKind.DateTime _
        | FormFieldKind.DateTimeRange _
        | FormFieldKind.Color _
        | FormFieldKind.Tokens _ -> None

    let private fieldValueOf (slot: Slot option) (text: string) : JVal =
        match slot with
        | Some Slot.Boolean -> JBool(text = "true")
        | Some(Slot.Number(_, _, _)) ->
            let n = float text

            if n = floor n && abs n <= 2147483647.0 then
                JInt(int n)
            else
                JFloat n
        | _ -> JStr text

    let private fieldAffordances (mountId: string option) (field: FormField<'Msg>) =
        let node = qualify mountId field.Id
        let label = labelOf field.Id field.Label
        let slot = fieldSlot field.Affordance field.Kind
        // An authored pattern on free text yields a text slot; its value is a string.
        let valueSlot =
            match slot with
            | Some s -> Some s
            | None -> Some(Slot.Text TextSlotMaxLength)

        let onChange = Generated.FormFieldKind.onChange field.Kind
        let valueKey = stateKeyOf (Generated.FormFieldKind.value field.Kind)

        let write (v: string) : Action<'Msg> option =
            let value = fieldValueOf valueSlot v

            match onChange with
            | Some f -> Some(f value)
            | None -> valueKey |> Option.map (fun key -> Action.SetState(key, Some value, None))

        let clear () : Action<'Msg> option =
            match onChange with
            | Some f -> Some(f (JStr ""))
            | None -> valueKey |> Option.map (fun key -> Action.SetState(key, None, None))

        valueAffordances node label field.Affordance slot write clear

    let private buttonAffordances (mountId: string option) (name: string) (spec: ButtonSpec<'Msg>) =
        let node = qualify mountId name
        let label = labelOf name spec.Label

        let patterns =
            spec.Affordance
            |> Option.map (fun a -> authored a.Patterns)
            |> Option.defaultValue []

        let description = spec.Affordance |> Option.bind (fun a -> a.Description)

        let kind, defaults =
            match spec.OnClick with
            | Action.Navigate _ -> AffordanceKind.Navigate, [ "go to " + label; "open " + label ]
            | _ -> AffordanceKind.Click, [ "click " + label; "press " + label ]

        [ make node kind label (defaults @ patterns) None description (fun _ -> Some spec.OnClick) ]

    let private withdrawn (affordance: Affordance option) =
        affordance
        |> Option.map (fun a -> a.NeverControllable)
        |> Option.defaultValue false

    /// The node's own affordances, before its children (§31.3). A node with an
    /// empty name, or whose affordance says `neverControllable`, yields nothing.
    let private own (mountId: string option) (node: Node<'Msg>) : OperableAffordance<'Msg> list =
        match node.Kind with
        | NodeKind.Select spec when node.Id <> "" && not (withdrawn spec.Affordance) ->
            selectAffordances mountId node.Id spec
        | NodeKind.Button spec when node.Id <> "" && not (withdrawn spec.Affordance) ->
            buttonAffordances mountId node.Id spec
        | NodeKind.Form spec ->
            spec.Fields
            |> List.filter (fun f -> f.Id <> "" && not (withdrawn f.Affordance))
            |> List.collect (fieldAffordances mountId)
        | _ -> []

    /// A tree's affordances, in document order (pre-order over the positions the
    /// node's KIND holds — `NodeChildren.Reach.kindHeld`: a `Mount`'s guest tree
    /// is its own scope, derived by the host under the guest's mount id).
    /// Pure and total: every tree has an answer, possibly empty.
    let derive (mountId: string option) (root: Node<'Msg>) : OperableAffordance<'Msg> list =
        let rec walk (node: Node<'Msg>) : OperableAffordance<'Msg> list =
            own mountId node
            @ (NodeChildren.children NodeChildren.Reach.kindHeld node |> List.collect walk)

        walk root

    // ─── Resolution (§31.4, §31.5) ───────────────────────────────────────────

    let private questionWords =
        set [ "what"; "what's"; "whats"; "which"; "who"; "how"; "is"; "are"; "does"; "do" ]

    /// Question-shaped: ends with `?`, or opens with a question word (§31.4).
    let isQuestion (normalised: string) : bool =
        normalised.EndsWith "?"
        || (match words normalised with
            | first :: _ -> questionWords.Contains first
            | [] -> false)

    let private trueWords = set [ "on"; "true"; "yes"; "enabled"; "checked" ]
    let private falseWords = set [ "off"; "false"; "no"; "disabled"; "unchecked" ]

    let private isNumeral (text: string) : bool =
        let body = if text.StartsWith "-" then text.Substring 1 else text
        let parts = body.Split('.')

        let digits (s: string) =
            s.Length > 0 && s |> Seq.forall (fun c -> c >= '0' && c <= '9')

        match parts with
        | [| whole |] -> digits whole
        | [| whole; fraction |] -> digits whole && digits fraction
        | _ -> false

    /// Decode a captured slot text against the slot. `lowered` is the capture
    /// ASCII-lowercased; `original` the same offsets of the instruction as typed.
    let private decodeSlot (slot: Slot) (lowered: string) (original: string) : string option =
        match slot with
        | Slot.Enumeration options ->
            options
            |> List.tryFind (fun o ->
                collapse (lower o.Label) = lowered
                || lower o.Value = lowered
                || List.contains lowered o.Aliases)
            |> Option.map (fun o -> o.Value)
        | Slot.Number(min, max, integral) ->
            if not (isNumeral lowered) then
                None
            else
                let n = float lowered

                let inBounds =
                    (match min with
                     | Some m -> n >= m
                     | None -> true)
                    && (match max with
                        | Some m -> n <= m
                        | None -> true)

                if inBounds && (not integral || n = floor n) then
                    Some lowered
                else
                    None
        | Slot.Boolean ->
            if trueWords.Contains lowered then Some "true"
            elif falseWords.Contains lowered then Some "false"
            else None
        | Slot.Text maxLength ->
            if original.Length > 0 && original.Length <= maxLength then
                Some original
            else
                None

    /// One form against one clause: the slot value it yields, if it matches.
    let private matchForm
        (affordance: OperableAffordance<'Msg>)
        (lowered: string)
        (original: string)
        (form: string)
        : SlotValue option =
        let at = form.IndexOf SlotToken

        if at < 0 then
            if lowered = form then Some SlotValue.NoValue else None
        else
            let prefix = form.Substring(0, at)
            let suffix = form.Substring(at + SlotToken.Length)

            if
                lowered.Length > prefix.Length + suffix.Length
                && lowered.StartsWith prefix
                && lowered.EndsWith suffix
            then
                let length = lowered.Length - prefix.Length - suffix.Length
                // Greedy: the slot takes everything between the anchored literals.
                let capturedLower = lowered.Substring(prefix.Length, length).Trim()
                let capturedOriginal = original.Substring(prefix.Length, length).Trim()

                match affordance.Slot with
                | Some slot when capturedLower <> "" ->
                    decodeSlot slot capturedLower capturedOriginal |> Option.map SlotValue.Value
                | _ -> None
            else
                None

    type private ClauseAnswer =
        | One of string * SlotValue
        | NoMatch
        | Several of string list

    let private resolveClause (affordances: OperableAffordance<'Msg> list) (lowered: string) (original: string) =
        let hits =
            affordances
            |> List.collect (fun a ->
                a.Forms
                |> List.choose (matchForm a lowered original)
                |> List.map (fun v -> a.Id, v))
            |> List.distinct

        match hits with
        | [] -> NoMatch
        | [ id, value ] -> One(id, value)
        | many -> Several(many |> List.map fst |> List.distinct)

    /// Strip the closing punctuation a sentence carries (`?`, `.`, `!`).
    let private unpunctuate (text: string) : string =
        let mutable t = text

        while t.Length > 0 && (t.EndsWith "?" || t.EndsWith "." || t.EndsWith "!") do
            t <- t.Substring(0, t.Length - 1).TrimEnd()

        t

    /// The resolution contract's reference matcher (§31.5):
    /// `resolve instruction affordances`.
    let resolve (instruction: string) (affordances: OperableAffordance<'Msg> list) : Outcome =
        if instruction.Length > MaxInstructionLength then
            Outcome.NotResolved
        else
            let original = collapse instruction
            let lowered = lower original
            let question = isQuestion lowered
            // `collapse` and `lower` are both length-preserving from here on, so
            // `original` and `lowered` stay aligned offset for offset.
            let original = unpunctuate original
            let lowered = unpunctuate lowered

            let eligible =
                affordances |> List.filter (fun a -> (a.Kind = AffordanceKind.Read) = question)

            // The clause split: on the whole words `and` / `then`, at the same
            // word offsets in both spellings.
            let split (text: string) =
                let rec go (acc: string list list) (current: string list) (ws: string list) =
                    match ws with
                    | [] -> List.rev (List.rev current :: acc)
                    | w :: rest when lower w = "and" || lower w = "then" -> go (List.rev current :: acc) [] rest
                    | w :: rest -> go acc (w :: current) rest

                go [] [] (words text) |> List.map (String.concat " ")

            let loweredClauses = split lowered
            let originalClauses = split original

            if lowered = "" then
                Outcome.NotResolved
            elif List.length loweredClauses > MaxClauses then
                Outcome.NotResolved
            elif loweredClauses |> List.exists (fun c -> c = "") then
                Outcome.NotResolved
            else
                let answers = List.map2 (resolveClause eligible) loweredClauses originalClauses

                if answers |> List.exists (fun a -> a = NoMatch) then
                    Outcome.NotResolved
                else
                    match
                        answers
                        |> List.tryPick (function
                            | Several ids -> Some ids
                            | _ -> None)
                    with
                    | Some ids -> Outcome.Ambiguous ids
                    | None ->
                        answers
                        |> List.choose (function
                            | One(id, v) -> Some(id, v)
                            | _ -> None)
                        |> Outcome.Resolved

    // ─── The corpus projection (§31.7) ───────────────────────────────────────
    //
    // The canonical JSON an affordance and an outcome are compared in across
    // hosts. `Resolve` is a closure and never crosses; everything else does.

    let private optional (key: string) (value: JVal option) : (string * JVal) list =
        match value with
        | Some v -> [ key, v ]
        | None -> []

    let slotJson (slot: Slot) : JVal =
        match slot with
        | Slot.Enumeration options ->
            JObj
                [ "kind", JStr "enumeration"
                  "options",
                  options
                  |> List.map (fun o ->
                      JObj(
                          [ "label", JStr o.Label; "value", JStr o.Value ]
                          @ (if List.isEmpty o.Aliases then
                                 []
                             else
                                 [ "aliases", JArr(o.Aliases |> List.map JStr) ])
                      ))
                  |> JArr ]
        | Slot.Number(min, max, integral) ->
            JObj(
                [ "kind", JStr "number"; "integral", JBool integral ]
                @ optional "min" (min |> Option.map JFloat)
                @ optional "max" (max |> Option.map JFloat)
            )
        | Slot.Boolean -> JObj [ "kind", JStr "boolean" ]
        | Slot.Text maxLength -> JObj [ "kind", JStr "text"; "maxLength", JInt maxLength ]

    let affordanceJson (affordance: OperableAffordance<'Msg>) : JVal =
        JObj(
            [ "id", JStr affordance.Id
              "node", JStr affordance.Node
              "kind", JStr(AffordanceKind.toWire affordance.Kind)
              "label", JStr affordance.Label
              "forms", JArr(affordance.Forms |> List.map JStr) ]
            @ optional "slot" (affordance.Slot |> Option.map slotJson)
            @ optional "description" (affordance.Description |> Option.map JStr)
        )

    let affordancesJson (affordances: OperableAffordance<'Msg> list) : JVal =
        JArr(affordances |> List.map affordanceJson)

    let outcomeJson (outcome: Outcome) : JVal =
        match outcome with
        | Outcome.Resolved clauses ->
            JObj
                [ "outcome", JStr "resolved"
                  "resolved",
                  clauses
                  |> List.map (fun (id, value) ->
                      JObj(
                          [ "id", JStr id ]
                          @ optional "value" (SlotValue.toOption value |> Option.map JStr)
                      ))
                  |> JArr ]
        | Outcome.NotResolved -> JObj [ "outcome", JStr "notResolved" ]
        | Outcome.Ambiguous ids -> JObj [ "outcome", JStr "ambiguous"; "ids", JArr(ids |> List.map JStr) ]

    /// Canonical text (WIRE_FORMAT §2), the form the corpus compares.
    let canonical (value: JVal) : string =
        Canonical.write Rendering.CrossHost value

    // ─── The DOM annotations (§31.8) ─────────────────────────────────────────
    //
    // What a renderer stamps on a node that DECLARES an affordance, so any agent
    // reading the page's accessibility tree sees the operable surface with no
    // protocol at all. Opt-in by declaration: a node with no `affordance` member
    // renders byte-for-byte as it did before Phase 2517, on every renderer.

    /// How many forms the spoken sentence carries, and its ceiling.
    [<Literal>]
    let SpokenFormLimit = 3

    [<Literal>]
    let SpokenLengthLimit = 240

    let private effectOf (kind: AffordanceKind) =
        match kind with
        | AffordanceKind.Set
        | AffordanceKind.Clear -> "write"
        | AffordanceKind.Read -> "read"
        | AffordanceKind.Click -> "invoke"
        | AffordanceKind.Navigate -> "navigate"

    let private shapeOf (slot: Slot option) =
        match slot with
        | Some(Slot.Enumeration _) -> "choice"
        | Some(Slot.Number _) -> "number"
        | Some Slot.Boolean -> "boolean"
        | Some(Slot.Text _) -> "text"
        | None -> "unknown"

    let private valuesJson (slot: Slot) : JVal =
        match slot with
        | Slot.Enumeration options ->
            JObj
                [ "kind", JStr "oneOf"
                  "values", JArr(options |> List.map (fun o -> JStr o.Value)) ]
        | Slot.Boolean -> JObj [ "kind", JStr "oneOf"; "values", JArr [ JStr "true"; JStr "false" ] ]
        | Slot.Number(min, max, _) ->
            JObj(
                [ "kind", JStr "numberRange" ]
                @ optional "min" (min |> Option.map JFloat)
                @ optional "max" (max |> Option.map JFloat)
            )
        | Slot.Text maxLength -> JObj [ "kind", JStr "textLength"; "maxLength", JInt maxLength ]

    let private spoken (forms: string list) (description: string option) : string option =
        let phrases =
            forms |> List.truncate SpokenFormLimit |> List.map (fun f -> "\"" + f + "\"")

        let sentence =
            match phrases, description with
            | [], None -> None
            | [], Some d -> Some("Accepts " + d + ".")
            | ps, None -> Some("Say: " + String.concat "; " ps + ".")
            | ps, Some d -> Some("Say: " + String.concat "; " ps + ". Accepts " + d + ".")

        sentence
        |> Option.map (fun t ->
            if t.Length <= SpokenLengthLimit then
                t
            else
                t.Substring(0, SpokenLengthLimit - 1).TrimEnd() + "…")

    /// The attributes for one addressable name and its affordances, in write
    /// order; empty when there are none (a withdrawn node is not addressable).
    let annotate (name: string) (affordances: OperableAffordance<'Msg> list) : (string * string) list =
        match affordances with
        | [] -> []
        | first :: _ ->
            let set = affordances |> List.tryFind (fun a -> a.Kind = AffordanceKind.Set)
            let isValue = affordances |> List.exists (fun a -> a.Kind = AffordanceKind.Read)
            let slot = set |> Option.bind (fun a -> a.Slot)

            let commands =
                affordances
                |> List.collect (fun a ->
                    a.Forms
                    |> List.map (fun f -> JObj [ "phrase", JStr f; "effect", JStr(effectOf a.Kind) ]))

            let aliases =
                match slot with
                | Some(Slot.Enumeration options) ->
                    options
                    |> List.collect (fun o ->
                        o.Aliases |> List.map (fun a -> JObj [ "alias", JStr a; "value", JStr o.Value ]))
                | _ -> []

            [ "data-ai-name", name
              if isValue then
                  "data-fuaran-shape", shapeOf slot
                  "data-fuaran-controllable", (if set.IsSome then "true" else "false")
              "data-fuaran-commands", canonical (JArr commands)
              if not (List.isEmpty aliases) then
                  "data-fuaran-aliases", canonical (JArr aliases)
              match slot with
              | Some s -> "data-fuaran-values", canonical (valuesJson s)
              | None -> ()
              match first.Description with
              | Some d -> "data-fuaran-description", d
              | None -> ()
              match spoken (affordances |> List.collect _.Forms) first.Description with
              | Some sentence -> "aria-description", sentence
              | None -> () ]

    /// The annotations of a `Select` or `Button` node that declares an
    /// affordance; empty for every other node.
    let nodeAnnotations (node: Node<'Msg>) : (string * string) list =
        let declared =
            match node.Kind with
            | NodeKind.Select spec -> spec.Affordance.IsSome
            | NodeKind.Button spec -> spec.Affordance.IsSome
            | _ -> false

        if declared && node.Id <> "" then
            annotate node.Id (own None node)
        else
            []

    /// The annotations of a `Form` field that declares an affordance.
    let fieldAnnotations (field: FormField<'Msg>) : (string * string) list =
        if field.Affordance.IsSome && field.Id <> "" && not (withdrawn field.Affordance) then
            annotate field.Id (fieldAffordances None field)
        else
            []
