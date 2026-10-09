module Fuaran.UI.Tests.FormFieldValueBytesTests

// ============================================================================
//  Phase 2177 — the reshape of `FormFieldKind` moved NO value byte.
//
//  Every `FormFieldKind` case now carries its value as `Binding<JVal>` and ONE
//  handler, `onChange`, so the generated `value` / `onChange` projections reach
//  every case. The operator approved that reshape on one condition: the wire
//  value bytes do not move, and the ONLY wire difference is the Checkbox /
//  Toggle handler key (`onToggle` -> `onChange`).
//
//  `before` below is the record of every form-field value slot in the corpus's
//  node fixtures as they stood BEFORE the reshape (wire-format-fixtures@055358a,
//  read with a JSON reader independent of this repo's codec): the fixture, the
//  path, the case, the handler key it carried and the value member's exact
//  canonical bytes. The test re-reads the CURRENT corpus and requires every
//  recorded slot to be present with byte-identical value bytes, and its handler
//  key to be the recorded one with `onToggle` read as `onChange`. A fixture
//  added later is not in the record and is not judged here; a recorded slot
//  that moves is.
// ============================================================================

open System.IO
open Expecto
open Fuaran.Core

/// fixture, path, case, handler key ("-" = none), value bytes ("<absent>" = omitted).
let private before =
    """
      composite-tabs-panels.json	$.kind.children[1].kind.children[0].kind.fields[0].kind	Text	-	{"$type":"Static","value":"Ada Lovelace"}
      composite-tabs-panels.json	$.kind.children[1].kind.children[0].kind.fields[1].kind	Choice	-	{"$type":"Static","value":"dark"}
      filterable-static-dashboard.json	$.kind.children[0].kind.items[0].kind	Choice	-	<absent>
      filterable-static-dashboard.json	$.kind.children[0].kind.items[1].kind	Choice	-	<absent>
      filters-1.json	$.kind.items[0].kind	Text	onChange	{"$type":"Static","value":""}
      filters-1.json	$.kind.items[1].kind	Choice	onChange	{"$type":"Static","value":"all"}
      filters-date-range.json	$.kind.items[0].kind	DateTimeRange	-	<absent>
      filters-declarative.json	$.kind.items[0].kind	Text	-	<absent>
      filters-declarative.json	$.kind.items[1].kind	Choice	-	<absent>
      filters-declarative.json	$.kind.items[2].kind	Range	-	{"max":100,"min":0}
      filters-dependson-declared.json	$.kind.children[0].kind.items[0].kind	Choice	-	<absent>
      filters-dependson-declared.json	$.kind.children[0].kind.items[1].kind	Choice	-	<absent>
      filters-dependson-undeclared.json	$.kind.children[0].kind.items[0].kind	Choice	-	<absent>
      filters-param-source-declared.json	$.kind.children[0].kind.items[0].kind	Choice	-	<absent>
      filters-param-source-declared.json	$.kind.children[0].kind.items[1].kind	Choice	-	<absent>
      filters-param-source-undeclared.json	$.kind.children[0].kind.items[0].kind	Choice	-	<absent>
      filters-rating-colour.json	$.kind.items[0].kind	Rating	-	<absent>
      filters-rating-colour.json	$.kind.items[1].kind	Color	-	<absent>
      filters-segmented.json	$.kind.items[0].kind	SegmentedChoice	onChange	{"$type":"Static","value":"table"}
      filters-tokens.json	$.kind.items[0].kind	Tokens	-	<absent>
      form-1.json	$.kind.fields[0].kind	Text	onChange	{"$type":"Static","value":""}
      form-1.json	$.kind.fields[1].kind	Number	onChange	{"$type":"Static","value":0}
      form-1.json	$.kind.fields[2].kind	Checkbox	onToggle	{"$type":"Static","value":false}
      form-1.json	$.kind.fields[3].kind	Choice	onChange	{"$type":"Static","value":"basic"}
      form-1.json	$.kind.fields[4].kind	TextArea	onChange	{"$type":"Static","value":""}
      form-color.json	$.kind.fields[0].kind	Color	onChange	{"$type":"Static","value":"#FFAA00"}
      form-combobox-freetext.json	$.kind.fields[0].kind	Combobox	-	{"$type":"Static","value":"needs-a-second-look"}
      form-combobox-query.json	$.kind.fields[0].kind	Combobox	-	<absent>
      form-combobox-static.json	$.kind.fields[0].kind	Combobox	onChange	{"$type":"Static","value":"fra"}
      form-date-range.json	$.kind.fields[0].kind	DateTimeRange	onChange	{"from":"2026-03-01","to":"2026-03-08"}
      form-date-range.json	$.kind.fields[1].kind	DateTimeRange	-	{"$type":"State","defaultValue":{"from":"08:00","to":"17:00"},"key":"shift"}
      form-date-range.json	$.kind.fields[2].kind	DateTimeRange	onChange	{"from":"2026-03-01T09:00","to":"2026-03-01T17:00"}
      form-date.json	$.kind.fields[0].kind	DateTime	onChange	{"$type":"Static","value":"2026-01-15"}
      form-date.json	$.kind.fields[1].kind	DateTime	onChange	{"$type":"Static","value":"08:30"}
      form-date.json	$.kind.fields[2].kind	DateTime	onChange	{"$type":"Static","value":"2026-03-01T14:00"}
      form-declarative-minimal.json	$.kind.fields[0].kind	Text	-	<absent>
      form-declarative-minimal.json	$.kind.fields[1].kind	Number	-	<absent>
      form-declarative-minimal.json	$.kind.fields[2].kind	Choice	-	<absent>
      form-declarative-minimal.json	$.kind.fields[3].kind	DateTime	-	<absent>
      form-declarative.json	$.kind.fields[0].kind	Text	-	{"$type":"State","defaultValue":"","key":"profileName"}
      form-declarative.json	$.kind.fields[1].kind	Number	-	{"$type":"State","defaultValue":0,"key":"profileAge"}
      form-declarative.json	$.kind.fields[2].kind	Checkbox	-	{"$type":"State","defaultValue":false,"key":"profileAgree"}
      form-declarative.json	$.kind.fields[3].kind	Choice	-	{"$type":"State","key":"profileTier"}
      form-field-rules.json	$.kind.fields[0].kind	Text	-	<absent>
      form-field-rules.json	$.kind.fields[1].kind	Text	-	<absent>
      form-field-rules.json	$.kind.fields[2].kind	Text	-	<absent>
      form-field-rules.json	$.kind.fields[3].kind	DateTime	-	<absent>
      form-field-rules.json	$.kind.fields[4].kind	DateTime	-	<absent>
      form-local-1.json	$.kind.fields[0].kind	Text	onChange	{"$type":"Local","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":"","key":"salary"},"onCommit":"<closure>","parse":"<closure>"}
      form-local-debounce.json	$.kind.fields[0].kind	Text	onChange	{"$type":"Local","flushOn":{"$type":"OnDebounce","milliseconds":250},"format":"<closure>","initialFrom":{"$type":"Static","value":"draft@example.com"},"onCommit":"<closure>","parse":"<closure>"}
      form-local-declared.json	$.kind.fields[0].kind	Number	-	{"$type":"Local","codec":{"$type":"Number","decimals":2},"commitTo":"order.unitPrice","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":0,"key":"order.unitPrice"},"parse":"<closure>"}
      form-ranged.json	$.kind.fields[0].kind	RangedNumber	onChange	{"$type":"Static","value":2024}
      form-ranged.json	$.kind.fields[1].kind	RangedNumber	onChange	{"$type":"Static","value":10}
      form-ranged.json	$.kind.fields[2].kind	RangedNumber	onChange	{"$type":"Static","value":100}
      form-rating-halves.json	$.kind.fields[0].kind	Rating	-	{"$type":"Static","value":3.5}
      form-rating-halves.json	$.kind.fields[1].kind	Rating	-	<absent>
      form-rating.json	$.kind.fields[0].kind	Rating	onChange	{"$type":"Static","value":4}
      form-rule-tokens.json	$.kind.fields[0].kind	Text	-	<absent>
      form-rule-tokens.json	$.kind.fields[1].kind	Text	-	<absent>
      form-rule-tokens.json	$.kind.fields[2].kind	Text	-	<absent>
      form-rule-tokens.json	$.kind.fields[3].kind	Text	-	<absent>
      form-rule-tokens.json	$.kind.fields[4].kind	Text	-	<absent>
      form-rule-tokens.json	$.kind.fields[5].kind	Text	-	<absent>
      form-rule-tokens.json	$.kind.fields[6].kind	Text	-	<absent>
      form-segmented.json	$.kind.fields[0].kind	SegmentedChoice	onChange	{"$type":"Static","value":"effective"}
      form-segmented.json	$.kind.fields[1].kind	SegmentedChoice	onChange	{"$type":"Static"}
      form-toggle.json	$.kind.fields[0].kind	Toggle	-	<absent>
      form-toggle.json	$.kind.fields[1].kind	Checkbox	-	<absent>
      form-tokens-freetext.json	$.kind.fields[0].kind	Tokens	-	<absent>
      form-tokens-query.json	$.kind.fields[0].kind	Tokens	-	<absent>
      form-tokens-suggested.json	$.kind.fields[0].kind	Tokens	onChange	{"$type":"Static","value":["deu","fra"]}
      frag-stdlib-filter-bar.json	$.kind.body.kind.items[0].kind	Text	-	<absent>
      frag-stdlib-filter-bar.json	$.kind.body.kind.items[1].kind	Choice	-	<absent>
"""

let private formFieldKindTags =
    set
        [ "Text"
          "Number"
          "Checkbox"
          "Toggle"
          "Choice"
          "TextArea"
          "RangedNumber"
          "Range"
          "SegmentedChoice"
          "DateTime"
          "DateTimeRange"
          "Combobox"
          "Rating"
          "Color"
          "Tokens" ]

/// Every form-field slot in one parsed fixture: (path, case, handler key, value bytes).
let private slots (doc: JVal) : (string * string * string * string) list =
    let found = ResizeArray()

    let rec walk (v: JVal) (path: string) =
        match v with
        | JObj fields ->
            let field name =
                fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

            match field "kind" with
            | Some(JObj kind) when
                field "label" |> Option.isSome
                && (field "id" |> Option.isSome || field "name" |> Option.isSome)
                ->
                let member' name =
                    kind |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

                match member' "$type" with
                | Some(JStr tag) when formFieldKindTags.Contains tag ->
                    let handler =
                        [ "onChange"; "onToggle" ]
                        |> List.filter (fun h -> (member' h).IsSome)
                        |> String.concat ","

                    let value =
                        match member' "value" with
                        | Some v -> Canon.render v
                        | None -> "<absent>"

                    found.Add(path + ".kind", tag, (if handler = "" then "-" else handler), value)
                | _ -> ()
            | _ -> ()

            for (k, child) in
                fields
                |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b)) do
                walk child (path + "." + k)
        | JArr items -> items |> List.iteri (fun i child -> walk child (sprintf "%s[%d]" path i))
        | _ -> ()

    walk doc "$"
    List.ofSeq found

let private corpus () =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | Some r -> r
    | None -> failtest "wire-format-fixtures/ not found — the corpus clone is missing"

[<Tests>]
let formFieldValueBytesTests =
    testList
        "Phase 2177 — FormFieldKind's reshape moved no value byte"
        [ test "every recorded form-field value slot is byte-identical, and its handler key is onChange" {
              let root = corpus ()

              let recorded =
                  before.Trim().Split('\n')
                  |> Array.map (fun line ->
                      match line.Trim().Split('\t') with
                      | [| fixture; path; tag; handler; value |] -> fixture, path, tag, handler, value
                      | _ -> failtestf "malformed record line: %s" line)

              Expect.isGreaterThan recorded.Length 0 "the record is not empty"

              let current =
                  recorded
                  |> Array.map (fun (fixture, _, _, _, _) -> fixture)
                  |> Array.distinct
                  |> Array.map (fun fixture ->
                      let file = Path.Combine(root, "nodes", fixture)

                      if not (File.Exists file) then
                          failtestf "recorded fixture %s is no longer in the corpus" fixture

                      match Json.parse (File.ReadAllText file) with
                      | Ok doc -> fixture, slots doc
                      | Error why -> failtestf "%s does not parse: %s" fixture why)
                  |> Map.ofArray

              for (fixture, path, tag, handler, value) in recorded do
                  match current[fixture] |> List.tryFind (fun (p, _, _, _) -> p = path) with
                  | None -> failtestf "%s %s: the recorded %s slot is gone" fixture path tag
                  | Some(_, tag', handler', value') ->
                      Expect.equal tag' tag (sprintf "%s %s: the case" fixture path)
                      Expect.equal value' value (sprintf "%s %s: the value bytes moved" fixture path)

                      let expected = if handler = "onToggle" then "onChange" else handler
                      Expect.equal handler' expected (sprintf "%s %s: the handler key" fixture path)
          }

          test "no form-field kind in the node corpus carries an onToggle key" {
              let root = corpus ()

              let offenders =
                  Directory.GetFiles(Path.Combine(root, "nodes"), "*.json")
                  |> Array.toList
                  |> List.collect (fun file ->
                      match Json.parse (File.ReadAllText file) with
                      | Ok doc ->
                          slots doc
                          |> List.filter (fun (_, _, handler, _) -> handler.Contains "onToggle")
                          |> List.map (fun (path, _, _, _) ->
                              (Path.GetFileName file |> Option.ofObj |> Option.defaultValue file) + " " + path)
                      | Error why -> failtestf "%s does not parse: %s" file why)

              Expect.isEmpty offenders "a form field's change handler is `onChange` on every kind"
          } ]
