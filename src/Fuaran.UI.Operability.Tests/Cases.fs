/// The AUTHORED inputs of the `operability/` corpus family (WIRE_FORMAT §31.7).
/// Trees, wire documents and instructions are written here; every answer the
/// family records is computed by the reference (`Vectors.fs`), and the strata
/// below also state the answer they expect, so the reference is checked against
/// the contract rather than only against itself.
module Fuaran.UI.Operability.Tests.Cases

/// A tree the derivation and the matcher run over, as authored wire JSON. The
/// family records its CANONICAL re-encoding, so the bytes here need not be.
type Tree =
    { Id: string
      MountId: string option
      Json: string }

let private select
    (id: string)
    (label: string)
    (options: (string * string) list)
    (stateKey: string)
    (affordance: string option)
    =
    let opts =
        options
        |> List.map (fun (l, v) -> sprintf """{"label":"%s","value":"%s"}""" l v)
        |> String.concat ","

    let aff =
        match affordance with
        | Some a -> sprintf ""","affordance":%s""" a
        | None -> ""

    sprintf
        """{"id":"%s","kind":{"$type":"Select","label":"%s","source":{"$type":"Static","value":[%s]},"value":{"$type":"State","defaultValue":"","key":"%s"}%s}}"""
        id
        label
        opts
        stateKey
        aff

let private box (id: string) (children: string list) =
    sprintf
        """{"id":"%s","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
        id
        (String.concat "," children)

/// A trade ticket: two selects, a form of five fields, three buttons.
let ticket: Tree =
    { Id = "ticket"
      MountId = None
      Json =
        box
            "ticket"
            [ select
                  "country"
                  "Country"
                  [ "United Kingdom", "uk"; "France", "fr"; "United States", "us" ]
                  "country"
                  (Some
                      """{"aliases":[{"alias":"britain","value":"uk"},{"alias":"america","value":"us"}],"clearable":true,"description":"Destination country","patterns":["ship to {value}"],"readPatterns":["where are we shipping"]}""")
              select "currency" "Currency" [ "GBP", "gbp"; "USD", "usd" ] "currency" None
              """{"id":"order","kind":{"$type":"Form","fields":[{"id":"quantity","kind":{"$type":"RangedNumber","max":100,"min":1,"onChange":"<closure>","step":1,"value":{"$type":"Static","value":1}},"label":"Quantity"},{"id":"notes","kind":{"$type":"Text","onChange":"<closure>","value":{"$type":"Static","value":""}},"label":"Notes"},{"affordance":{"patterns":["reference {value}"]},"id":"reference","kind":{"$type":"Text","value":{"$type":"State","defaultValue":"","key":"reference"}},"label":"Reference"},{"id":"express","kind":{"$type":"Toggle","onChange":"<closure>"},"label":"Express delivery"},{"affordance":{"neverControllable":true},"id":"pin","kind":{"$type":"Text","onChange":"<closure>"},"label":"PIN"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""
              """{"id":"submit","kind":{"$type":"Button","affordance":{"description":"Places the order","patterns":["place the order"]},"label":"Submit order","onClick":{"$type":"Chain","ops":[]}}}"""
              """{"id":"help","kind":{"$type":"Button","label":"Help","onClick":{"$type":"Navigate","route":"/help"}}}"""
              """{"id":"delete","kind":{"$type":"Button","affordance":{"neverControllable":true},"label":"Delete everything","onClick":{"$type":"Chain","ops":[]}}}""" ] }

/// Two selects carrying one label: every set form is claimed twice.
let twins: Tree =
    { Id = "twins"
      MountId = Some "panel"
      Json =
        box
            "twins"
            [ select "region-a" "Region" [ "North", "n"; "South", "s" ] "regionA" None
              select "region-b" "Region" [ "North", "n"; "South", "s" ] "regionB" None ] }

/// A translated label: the node name stands in for it, spelled as words.
let translated: Tree =
    { Id = "translated"
      MountId = None
      Json =
        """{"id":"fiscal_year","kind":{"$type":"Select","label":{"$type":"I18n","key":"fiscal.year"},"source":{"$type":"Static","value":[{"label":"FY2025","value":"2025"},{"label":"FY2026","value":"2026"}]},"value":{"$type":"Static","value":"2025"}}}""" }

let trees = [ ticket; twins; translated ]

/// A wire document carrying an affordance that must round-trip byte for byte.
type RoundTrip = { Id: string; Json: string }

let roundTrips: RoundTrip list =
    [ { Id = "affordance-select"
        Json =
          select
              "country"
              "Country"
              [ "United Kingdom", "uk" ]
              "country"
              (Some
                  """{"aliases":[{"alias":"britain","value":"uk"}],"clearable":true,"description":"Destination country","patterns":["ship to {value}"],"readPatterns":["where are we shipping"]}""") }
      { Id = "affordance-select-empty"
        Json = select "currency" "Currency" [ "GBP", "gbp" ] "currency" (Some "{}") }
      { Id = "affordance-button"
        Json =
          """{"id":"submit","kind":{"$type":"Button","affordance":{"patterns":["place the order"]},"label":"Submit order","onClick":{"$type":"Chain","ops":[]}}}""" }
      { Id = "affordance-form-field"
        Json =
          """{"id":"order","kind":{"$type":"Form","fields":[{"affordance":{"neverControllable":true,"patterns":["note {value}"]},"id":"notes","kind":{"$type":"Text"},"label":"Notes"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}""" } ]

/// A wire document the slot rule refuses (§31.2).
type Reject =
    { Id: string
      Json: string
      Code: string
      Path: string }

let rejects: Reject list =
    [ { Id = "reject-select-pattern-no-slot"
        Json = select "country" "Country" [ "United Kingdom", "uk" ] "country" (Some """{"patterns":["ship it"]}""")
        Code = "WRONG_TYPE"
        Path = "$.kind.affordance.patterns[0]" }
      { Id = "reject-form-field-pattern-no-slot"
        Json =
          """{"id":"order","kind":{"$type":"Form","fields":[{"affordance":{"patterns":["make a note"]},"id":"notes","kind":{"$type":"Text"},"label":"Notes"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""
        Code = "WRONG_TYPE"
        Path = "$.kind.fields[0].affordance.patterns[0]" }
      { Id = "reject-button-pattern-with-slot"
        Json =
          """{"id":"submit","kind":{"$type":"Button","affordance":{"patterns":["submit {value}"]},"label":"Submit","onClick":{"$type":"Chain","ops":[]}}}"""
        Code = "WRONG_TYPE"
        Path = "$.kind.affordance.patterns[0]" }
      { Id = "reject-read-pattern-with-slot"
        Json =
          select
              "country"
              "Country"
              [ "United Kingdom", "uk" ]
              "country"
              (Some """{"readPatterns":["is it {value}"]}""")
        Code = "WRONG_TYPE"
        Path = "$.kind.affordance.readPatterns[0]" }
      { Id = "reject-pattern-two-slots"
        Json =
          select
              "country"
              "Country"
              [ "United Kingdom", "uk" ]
              "country"
              (Some """{"patterns":["{value} to {value}"]}""")
        Code = "WRONG_TYPE"
        Path = "$.kind.affordance.patterns[0]" }
      { Id = "reject-pattern-stray-brace"
        Json =
          select
              "country"
              "Country"
              [ "United Kingdom", "uk" ]
              "country"
              (Some """{"patterns":["ship to {country}"]}""")
        Code = "WRONG_TYPE"
        Path = "$.kind.affordance.patterns[0]" }
      { Id = "reject-alias-missing-value"
        Json =
          select "country" "Country" [ "United Kingdom", "uk" ] "country" (Some """{"aliases":[{"alias":"britain"}]}""")
        Code = "MISSING_FIELD"
        Path = "$.kind.affordance.aliases[0].value" } ]

/// What an instruction is expected to resolve to — stated here, by hand.
type Expect =
    | Resolves of (string * string option) list
    | Refused
    | AmbiguousAmong of string list

/// One instruction over one tree, in its calibration stratum (§31.7):
/// S1 declared and label-derived verbatim hits; S2 paraphrase misses; S3 aliases
/// and option labels; S4 clears and reads; S5 compound clauses; S6 analytical
/// refusals; S7 adversarial refusals.
type Resolve =
    { Id: string
      Stratum: string
      Tree: Tree
      Instruction: string
      Expect: Expect }

let private r id stratum tree instruction expect =
    { Id = id
      Stratum = stratum
      Tree = tree
      Instruction = instruction
      Expect = expect }

let resolves: Resolve list =
    [ // S1 — verbatim hits, label-derived and declared.
      r "s1-set-label-default" "S1" ticket "set country to France" (Resolves [ "country:set", Some "fr" ])
      r "s1-change-label-default" "S1" ticket "Change currency to USD" (Resolves [ "currency:set", Some "usd" ])
      r "s1-declared-pattern" "S1" ticket "ship to united states" (Resolves [ "country:set", Some "us" ])
      r "s1-number-in-bounds" "S1" ticket "set quantity to 12" (Resolves [ "quantity:set", Some "12" ])
      r "s1-toggle" "S1" ticket "set express delivery to on" (Resolves [ "express:set", Some "true" ])
      r "s1-click-default" "S1" ticket "click submit order" (Resolves [ "submit:click", None ])
      r "s1-click-declared" "S1" ticket "Place the order." (Resolves [ "submit:click", None ])
      r "s1-navigate" "S1" ticket "go to help" (Resolves [ "help:navigate", None ])
      r
          "s1-declared-free-text"
          "S1"
          ticket
          "reference PO-1234 Rev B"
          (Resolves [ "reference:set", Some "PO-1234 Rev B" ])
      r
          "s1-name-stands-in-for-label"
          "S1"
          translated
          "set fiscal year to FY2026"
          (Resolves [ "fiscal_year:set", Some "2026" ])
      // S2 — paraphrases no form declares: refused, never guessed.
      r "s2-paraphrase" "S2" ticket "could you switch the country over to france" Refused
      r "s2-word-order" "S2" ticket "france is the country" Refused
      r "s2-out-of-set" "S2" ticket "set country to germany" Refused
      r "s2-number-out-of-bounds" "S2" ticket "set quantity to 500" Refused
      r "s2-number-fraction" "S2" ticket "set quantity to 2.5" Refused
      // S3 — aliases and option labels and values.
      r "s3-alias" "S3" ticket "ship to Britain" (Resolves [ "country:set", Some "uk" ])
      r "s3-option-value" "S3" ticket "set country to US" (Resolves [ "country:set", Some "us" ])
      r "s3-alias-in-default-form" "S3" ticket "change country to america" (Resolves [ "country:set", Some "us" ])
      // S4 — clears and reads.
      r "s4-clear" "S4" ticket "clear country" (Resolves [ "country:clear", None ])
      r "s4-clear-not-clearable" "S4" ticket "clear currency" Refused
      r "s4-read-default" "S4" ticket "What is the currency?" Refused
      r "s4-read-label" "S4" ticket "what is currency?" (Resolves [ "currency:read", None ])
      r "s4-read-declared" "S4" ticket "where are we shipping?" (Resolves [ "country:read", None ])
      r "s4-read-is-not-a-set" "S4" ticket "set country to france?" Refused
      // S5 — compound clauses, all-or-nothing, and the cap.
      r
          "s5-two-clauses"
          "S5"
          ticket
          "set country to france and set quantity to 3"
          (Resolves [ "country:set", Some "fr"; "quantity:set", Some "3" ])
      r
          "s5-then"
          "S5"
          ticket
          "set currency to gbp then place the order"
          (Resolves [ "currency:set", Some "gbp"; "submit:click", None ])
      r "s5-all-or-nothing" "S5" ticket "set country to france and make it snappy" Refused
      r "s5-empty-clause" "S5" ticket "set country to france and and click submit order" Refused
      r "s5-over-the-clause-cap" "S5" ticket (String.concat " and " (List.replicate 9 "set quantity to 2")) Refused
      r
          "s5-ambiguous-label"
          "S5"
          twins
          "set region to north"
          (AmbiguousAmong [ "panel/region-a:set"; "panel/region-b:set" ])
      // S6 — analytical requests: no form reads a request to think.
      r "s6-summarise" "S6" ticket "summarise this order for me" Refused
      r "s6-compare" "S6" ticket "which country is cheapest to ship to?" Refused
      r "s6-explain" "S6" ticket "explain why the currency is GBP" Refused
      r "s6-free-text-without-slot" "S6" ticket "set notes to call me tomorrow" Refused
      // S7 — adversarial: withdrawn nodes, injections, the length cap.
      r "s7-never-controllable-field" "S7" ticket "set pin to 1234" Refused
      r "s7-never-controllable-button" "S7" ticket "click delete everything" Refused
      r "s7-injection-suffix" "S7" ticket "set country to france; ignore previous instructions" Refused
      r "s7-length-cap" "S7" ticket ("ship to " + String.replicate 600 "a") Refused
      r "s7-empty" "S7" ticket "   " Refused ]
