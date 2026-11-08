module Fuaran.UI.OpStream.Tests.OpEncoderPinTests

// The bytes `CanonicalJson.encodeOp` gives every case in `OpEncoderPinCases`
// (Phase 2061). Captured from the hand-written appender encoder immediately
// before it was replaced by the JVal-plus-`Canonical.write` encoder over the
// generated binding layer; every pin below is that encoder's output EXCEPT the
// two ruled cases, each of which names its ruling:
//
//   - `static-int64` — an integer beyond Int32 rides as a double. Below 2^53
//     the digits are identical; past it (this case) the bytes are the double's,
//     which is what any reader of the longer integer token re-encodes to.
//   - `local-full` — `Local.codec` and `Local.commitTo` ride the op as they
//     ride a node; the old encoder dropped both, so two `ReplaceBinding`s
//     differing only there hashed identically and the persisted op lost them.
//
// The three null cases (`filter-default-null`, `selection-default-null`,
// `updateprop-native-null`) keep the old bytes ON PURPOSE: a host-built null is
// unencodable, and the bare `null` token no reader accepts is how a consumer
// that parses the encoding learns so (the program loop's `unencodable-op`).

open Expecto
open Fuaran.UI.OpStream.Abstractions

let private pins: (string * string) list =
    [ "static-none", """{"$type":"ReplaceBinding","binding":{"$type":"Static"},"slot":"Source","target":"n"}"""
      "static-some-null", """{"$type":"ReplaceBinding","binding":{"$type":"Static"},"slot":"Source","target":"n"}"""
      "static-string",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"a\"b\\c\u0001é"},"slot":"Source","target":"n"}"""
      "static-bool",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":true},"slot":"Source","target":"n"}"""
      "static-int",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":-42},"slot":"Source","target":"n"}"""
      "static-int64",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":9007199254740992},"slot":"Source","target":"n"}"""
      "static-float",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":3.25},"slot":"Source","target":"n"}"""
      "static-float-whole",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":2},"slot":"Source","target":"n"}"""
      "static-float-big",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":1E+21},"slot":"Source","target":"n"}"""
      "static-float-small",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":1E-07},"slot":"Source","target":"n"}"""
      "static-float-negzero",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":0},"slot":"Source","target":"n"}"""
      "static-float-nan",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"NaN"},"slot":"Source","target":"n"}"""
      "static-float-inf",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"Infinity"},"slot":"Source","target":"n"}"""
      "static-float-neginf",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"-Infinity"},"slot":"Source","target":"n"}"""
      "static-float32",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":1.5},"slot":"Source","target":"n"}"""
      "static-datetimeoffset",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":1700000000},"slot":"Source","target":"n"}"""
      "static-datetime-utc",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":1704164645},"slot":"Source","target":"n"}"""
      "static-list-opaque",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"<opaque>"},"slot":"Source","target":"n"}"""
      "static-record-opaque",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"<opaque>"},"slot":"Source","target":"n"}"""
      "static-jval-opaque",
      """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":"<opaque>"},"slot":"Source","target":"n"}"""
      "query-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Query","name":"rows"},"slot":"Source","target":"n"}"""
      "query-deps-empty",
      """{"$type":"ReplaceBinding","binding":{"$type":"Query","name":"rows"},"slot":"Source","target":"n"}"""
      "query-deps",
      """{"$type":"ReplaceBinding","binding":{"$type":"Query","dependsOn":["b","a"],"name":"rows"},"slot":"Source","target":"n"}"""
      "filter-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Filter","name":"status"},"slot":"Source","target":"n"}"""
      "filter-default",
      """{"$type":"ReplaceBinding","binding":{"$type":"Filter","defaultValue":"open","name":"status"},"slot":"Source","target":"n"}"""
      "filter-default-null",
      """{"$type":"ReplaceBinding","binding":{"$type":"Filter","defaultValue":null,"name":"status"},"slot":"Source","target":"n"}"""
      "selection-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Selection","nodeId":"grid"},"slot":"Source","target":"n"}"""
      "selection-full",
      """{"$type":"ReplaceBinding","binding":{"$type":"Selection","defaultValue":7,"field":"id","nodeId":"grid"},"slot":"Source","target":"n"}"""
      "selection-default-null",
      """{"$type":"ReplaceBinding","binding":{"$type":"Selection","defaultValue":null,"nodeId":"grid"},"slot":"Source","target":"n"}"""
      "state-bare", """{"$type":"ReplaceBinding","binding":{"$type":"State","key":"k"},"slot":"Source","target":"n"}"""
      "state-default",
      """{"$type":"ReplaceBinding","binding":{"$type":"State","defaultValue":1.25,"key":"k"},"slot":"Source","target":"n"}"""
      "state-default-null",
      """{"$type":"ReplaceBinding","binding":{"$type":"State","key":"k"},"slot":"Source","target":"n"}"""
      "now-bare", """{"$type":"ReplaceBinding","binding":{"$type":"Now"},"slot":"Source","target":"n"}"""
      "now-second",
      """{"$type":"ReplaceBinding","binding":{"$type":"Now","grain":"Second"},"slot":"Source","target":"n"}"""
      "now-day", """{"$type":"ReplaceBinding","binding":{"$type":"Now","grain":"Day"},"slot":"Source","target":"n"}"""
      "computed",
      """{"$type":"ReplaceBinding","binding":{"$type":"Computed","fn":"<closure>"},"slot":"Source","target":"n"}"""
      "i18n-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"I18n","key":"greet"},"slot":"Source","target":"n"}"""
      "i18n-args-empty",
      """{"$type":"ReplaceBinding","binding":{"$type":"I18n","args":{},"key":"greet"},"slot":"Source","target":"n"}"""
      "i18n-args",
      """{"$type":"ReplaceBinding","binding":{"$type":"I18n","args":{"n":{"$type":"State","key":"count"},"name":{"$type":"Static","value":"Ada"},"o":{"$type":"Static","value":{"a":false,"z":1}}},"key":"greet"},"slot":"Source","target":"n"}"""
      "local-minimal",
      """{"$type":"ReplaceBinding","binding":{"$type":"Local","flushOn":{"$type":"OnBlur"},"format":"<closure>","initialFrom":{"$type":"State","key":"k"},"parse":"<closure>"},"slot":"Source","target":"n"}"""
      "local-full",
      """{"$type":"ReplaceBinding","binding":{"$type":"Local","codec":{"$type":"Number","decimals":2},"commitTo":"target","flushOn":{"$type":"OnDebounce","milliseconds":250},"format":"<closure>","initialFrom":{"$type":"State","defaultValue":"a","key":"k"},"onCommit":"<closure>","parse":"<closure>"},"slot":"Source","target":"n"}"""
      "local-onsubmit",
      """{"$type":"ReplaceBinding","binding":{"$type":"Local","flushOn":{"$type":"OnSubmit"},"format":"<closure>","initialFrom":{"$type":"Static"},"parse":"<closure>"},"slot":"Source","target":"n"}"""
      "local-oncommitaction",
      """{"$type":"ReplaceBinding","binding":{"$type":"Local","flushOn":{"$type":"OnCommitAction"},"format":"<closure>","initialFrom":{"$type":"Static","value":3},"parse":"<closure>"},"slot":"Source","target":"n"}"""
      "format-number-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Number"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-number",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Number","decimals":2},"locale":{"$type":"Explicit","tag":"en-GB"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-currency-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Currency","isoCode":"GBP"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-currency",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Currency","decimals":0,"isoCode":"EUR"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-percent",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Percent","decimals":1},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-percent-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Percent"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-datetime-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-datestyle-Short",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","dateStyle":"Short"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-datestyle-Medium",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","dateStyle":"Medium"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-datestyle-Long",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","dateStyle":"Long"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-datestyle-Full",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","dateStyle":"Full"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-timestyle-Short",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","timeStyle":"Short"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-timestyle-Medium",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","timeStyle":"Medium"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-timestyle-Long",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","timeStyle":"Long"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-timestyle-Full",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"DateTime","timeStyle":"Full"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Second",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Second"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Second",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Second"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Minute",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Minute"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Minute",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Minute"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Hour",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Hour"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Hour",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Hour"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Day",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Day"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Day",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Day"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Week",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Week"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Week",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Week"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Month",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Month"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Month",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Month"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-relative-Year",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"RelativeTime","unit":"Year"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-Year",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since","unit":"Year"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-since-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Since"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Seconds-Compact",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Compact","unit":"Seconds"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Seconds-Clock",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Clock","unit":"Seconds"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Seconds-Long",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Long","unit":"Seconds"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Minutes-Compact",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Compact","unit":"Minutes"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Minutes-Clock",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Clock","unit":"Minutes"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Minutes-Long",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Long","unit":"Minutes"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Hours-Compact",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Compact","unit":"Hours"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Hours-Clock",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Clock","unit":"Hours"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-duration-Hours-Long",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Duration","style":"Long","unit":"Hours"},"locale":{"$type":"Ambient"},"source":{"$type":"State","defaultValue":1.5,"key":"amount"}},"slot":"Source","target":"n"}"""
      "format-source-static",
      """{"$type":"ReplaceBinding","binding":{"$type":"Format","format":{"$type":"Number"},"locale":{"$type":"Ambient"},"source":{"$type":"Static","value":0.1}},"slot":"Source","target":"n"}"""
      "transform-data",
      """{"$type":"ReplaceBinding","binding":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[40,2]}},"schema":[{"name":"amount","type":"float"}]}},"slot":"Source","target":"n"}"""
      "transform-params-empty",
      """{"$type":"ReplaceBinding","binding":{"$type":"Transform","pipeline":[],"source":{"columns":{"amount":{"validity":[true,true],"values":[40,2]}},"schema":[{"name":"amount","type":"float"}]}},"slot":"Source","target":"n"}"""
      "transform-live",
      """{"$type":"ReplaceBinding","binding":{"$type":"Transform","params":[{"from":{"$type":"State","defaultValue":"x","key":"d"},"name":"dept"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}}}],"source":{"$type":"Query","name":"rows"}},"slot":"Source","target":"n"}"""
      "expr-bare",
      """{"$type":"ReplaceBinding","binding":{"$type":"Expr","expr":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}}},"slot":"Source","target":"n"}"""
      "expr-params-empty",
      """{"$type":"ReplaceBinding","binding":{"$type":"Expr","expr":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}}},"slot":"Source","target":"n"}"""
      "expr-params",
      """{"$type":"ReplaceBinding","binding":{"$type":"Expr","expr":{"$type":"binary","left":{"$type":"col","name":"dept"},"op":"eq","right":{"$type":"param","name":"dept"}},"params":[{"from":{"$type":"Static","value":"eng"},"name":"dept"}]},"slot":"Source","target":"n"}"""
      "invoke-noargs",
      """{"$type":"ReplaceBinding","binding":{"$type":"Invoke","args":[],"capabilityId":"cap.x"},"slot":"Source","target":"n"}"""
      "invoke-args",
      """{"$type":"ReplaceBinding","binding":{"$type":"Invoke","args":[{"addr":"b","value":"2"},{"addr":"a","value":"1\u000a"}],"capabilityId":"cap.x"},"slot":"Source","target":"n"}"""
      "editnode", """{"$type":"EditNode","newKind":{"$type":"Markdown","text":"Body"},"target":"m"}"""
      "updateprop-native-null", """{"$type":"UpdateProp","path":"Label","target":"m","value":null}"""
      "updateprop-native-string", """{"$type":"UpdateProp","path":"Label","target":"m","value":"x\u0009y"}"""
      "updateprop-native-int", """{"$type":"UpdateProp","path":"Rows","target":"m","value":3}"""
      "updateprop-native-int64", """{"$type":"UpdateProp","path":"Rows","target":"m","value":5}"""
      "updateprop-native-float", """{"$type":"UpdateProp","path":"Rows","target":"m","value":3.5}"""
      "updateprop-native-nan", """{"$type":"UpdateProp","path":"Rows","target":"m","value":"NaN"}"""
      "updateprop-native-bool", """{"$type":"UpdateProp","path":"Rows","target":"m","value":false}"""
      "updateprop-native-opaque", """{"$type":"UpdateProp","path":"Rows","target":"m","value":"<opaque>"}"""
      "updateprop-wire",
      """{"$type":"UpdateProp","path":"Columns[0].Label","target":"m","value":{"a":true,"z":[1,0.5,"s"]}}"""
      "updatestyle-default", """{"$type":"UpdateStyle","style":{},"target":"m"}"""
      "updatestyle-tone", """{"$type":"UpdateStyle","style":{"emphasis":"Loud","tone":"Critical"},"target":"m"}"""
      "patchstyle-empty", """{"$type":"PatchStyle","style":{},"target":"m"}"""
      "patchstyle-tone", """{"$type":"PatchStyle","style":{"emphasis":"Quiet","tone":"Default"},"target":"m"}"""
      "updatestate-empty", """{"$type":"UpdateState","state":{},"target":"m"}"""
      "updatestate-nodes",
      """{"$type":"UpdateState","state":{"onEmpty":{"id":"e","kind":{"$type":"Markdown","text":"Empty"}},"onLoading":{"id":"l","kind":{"$type":"Markdown","text":"Loading"}}},"target":"m"}"""
      "updateaccessibility",
      """{"$type":"UpdateAccessibility","accessibility":{"describedBy":"d","hidden":{"$type":"State","defaultValue":true,"key":"h"},"label":{"$type":"Static","value":"L"}},"target":"m"}"""
      "updatetooltip-some", """{"$type":"UpdateTooltip","target":"m","tooltip":"Tip"}"""
      "updatetooltip-none", """{"$type":"UpdateTooltip","target":"m"}"""
      "insertchild",
      """{"$type":"InsertChild","child":{"id":"m","kind":{"$type":"Markdown","text":"Body"}},"parentId":"dash"}"""
      "removenode", """{"$type":"RemoveNode","target":"m\"q"}"""
      "movenode", """{"$type":"MoveNode","newParentId":"dash","target":"m"}"""
      "reorderchildren", """{"$type":"ReorderChildren","newOrder":["right","left"],"parentId":"dash"}"""
      "reorderchildren-empty", """{"$type":"ReorderChildren","newOrder":[],"parentId":"dash"}"""
      "replaceroot",
      """{"$type":"ReplaceRoot","node":{"accessibility":{"role":"main"},"id":"dash","kind":{"$type":"Box","children":[{"id":"left","kind":{"$type":"Markdown","text":"Left pane"}},{"id":"right","kind":{"$type":"Markdown","text":"Right pane"}}],"layout":{"$type":"Auto"},"role":"Dashboard"}}}"""
      "batch-empty", """{"$type":"Batch","ops":[]}"""
      "batch-nested",
      """{"$type":"Batch","ops":[{"$type":"RemoveNode","target":"left"},{"$type":"Batch","ops":[{"$type":"MoveNode","newParentId":"dash","target":"right"}]},{"$type":"ReplaceBinding","binding":{"$type":"State","defaultValue":"v","key":"k"},"slot":"Source","target":"n"}]}""" ]

[<Tests>]
let tests =
    testList
        "Phase 2061 — the op encoder's bytes, case by case"
        [ test "every case has a pin and every pin a case" {
              let caseNames = OpEncoderPinCases.cases |> List.map fst
              Expect.equal (pins |> List.map fst) caseNames "the pin list follows the case census, in order"
          }

          yield!
              OpEncoderPinCases.cases
              |> List.map (fun (name, op) ->
                  test name {
                      let expected = pins |> List.find (fun (n, _) -> n = name) |> snd
                      Expect.equal (CanonicalJson.encodeOp op) expected name
                  }) ]
