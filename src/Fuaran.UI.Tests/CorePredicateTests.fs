module Fuaran.UI.Tests.CorePredicateTests

// ============================================================================
//  Phase 2042: each core predicate has ONE definition.
//
//  Every rule pinned here used to be written more than once, and the copies had
//  drifted apart. For each one there is an input on which two parts of the tier
//  gave different answers. This file holds one test per rule, built from that
//  input, and the test fails while the copies disagree. A source census next to
//  each test keeps a new copy from growing back. Behavioural tests cannot see a
//  new copy: on the day it is written it agrees, and that is how the old copies
//  arrived.
//
//  * Write-back target: `BindingWalk.isWriteBackTarget`. The dead-on-decode
//    lint had its own predicate, and that predicate called a `Local` with a
//    declared `commitTo` unwritable.
//  * Action recursion: the generated `Action.fold`, landed by Phase 2039. Only
//    the census is here; the behavioural `Confirm` tests sit beside the two
//    walks they cover.
//  * Chart X class: `QueryBinding` classes X by chart kind, and FUARAN087 is
//    the rule it has to agree with.
//  * UTF-8: one encoder, Core's (`Fuaran.Core.Hash.utf8Bytes`, since Phase
//    2309), which the hash and the byte-length meter both read.
//    It matches the BCL on ill-formed UTF-16.
//  * Extra-attribute keys: `ExtraAttributeKey.isAllowed`, read by the smart
//    constructor, the build-time check and the renderer floor alike.
// ============================================================================

open System
open System.IO
open System.Text
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Core

type private Msg = NoOp

let private node (id: string) (kind: NodeKind<Msg>) : Node<Msg> =
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      Fallback = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

/// A source file the test project copies into its output (see the
/// `predicate-sources` Content items). A census with nothing to read would
/// report every file as clean, so a missing copy fails.
let private source (relative: string) : string =
    let path = Path.Combine(AppContext.BaseDirectory, "predicate-sources", relative)

    if not (File.Exists path) then
        failwithf "census source not found at %s; check the predicate-sources Content items" path

    File.ReadAllText path

// ─── Write-back target ───────────────────────────────────────────────────────

/// A `Binding.Local` with no writable re-sync source and no hook. Only
/// `commitTo` decides whether it has anywhere to commit.
let private localCommitting (commitTo: string option) : Binding<int> =
    Binding.Local(LocalFlushTrigger.OnBlur, string, Binding.Static(Some 0), None, (fun _ -> Ok 0), None, commitTo)

let private tabsOver (active: Binding<int>) : Node<Msg> =
    node
        "tabs"
        (NodeKind.Tabs
            { Defaults.tabs<Msg> with
                ActiveIndex = active
                OnSelect = Some(fun (_: int) -> Action.Chain []) })

/// What the lint's remedy says about the value binding: `true` when it tells
/// the author the binding is already writable.
let private lintCallsWritable (tree: Node<Msg>) : bool =
    match
        DeadOnDecode.lint tree
        |> List.filter (fun f -> f.Location.Slot = "TabsSpec.onSelect")
    with
    | [ f ] ->
        f.Remedy
        |> Option.exists (fun r -> r.Contains("already writable", StringComparison.Ordinal))
    | other -> failwithf "expected exactly one TabsSpec.onSelect finding, got %A" other

let private writeBackTests =
    testList
        "write-back target"
        [ test "a Local with commitTo is writable to the lint, as it is to the validator" {
              // The shard's input. The validator treats this slot as live, so
              // FUARAN069 is silent. The lint used to tell the author to rebind it.
              let tree = tabsOver (localCommitting (Some "k"))
              Expect.isTrue (BindingWalk.isWriteBackTarget (localCommitting (Some "k"))) "the one predicate"
              Expect.isTrue (lintCallsWritable tree) "the dead-on-decode remedy reads the same predicate"
          }

          test "the lint agrees with BindingWalk.isWriteBackTarget on every binding shape" {
              let shapes: (string * Binding<int>) list =
                  [ "State", Binding.State("k", None)
                    "Filter", Binding.Filter("f", None)
                    "Filter with default", Binding.Filter("f", Some 1)
                    "Static", Binding.Static(Some 0)
                    "Local, commitTo", localCommitting (Some "k")
                    "Local, nowhere", localCommitting None
                    "Local over State",
                    Binding.Local(
                        LocalFlushTrigger.OnBlur,
                        string,
                        Binding.State("k", None),
                        None,
                        (fun _ -> Ok 0),
                        None,
                        None
                    ) ]

              for name, binding in shapes do
                  Expect.equal
                      (lintCallsWritable (tabsOver binding))
                      (BindingWalk.isWriteBackTarget binding)
                      (sprintf "the lint and the predicate disagree on %s" name)
          }

          test "census: DeadOnDecode holds no write-back predicate of its own" {
              let text = source "ui/DeadOnDecode.fs"
              Expect.isFalse (text.Contains("let private isWritable (", StringComparison.Ordinal)) "private copy gone"
              Expect.stringContains text "BindingWalk.isWriteBackTarget" "it calls the one definition"
          } ]

// ─── Action recursion ────────────────────────────────────────────────────────

let private actionWalkTests =
    testList
        "action recursion"
        [ test "census: the walks fold through Action.fold and recurse by hand nowhere" {
              // A hand recursion binds the Chain members, or a Confirm's
              // continuation, and walks it itself. Each walk below still matches
              // `Action.Chain _` and `Action.Confirm(prompt, _, _)`, which are
              // the exhaustive per-action arms and walk nothing.
              let handRecursion =
                  RegularExpressions.Regex(
                      @"\|\s*Action\.Chain\s*\(?\s*[a-z]\w*|\|\s*Action\.Confirm\s*\(\s*[^,()]*,\s*[a-z]\w*"
                  )

              for file in [ "ui/DeadOnDecode.fs"; "ui/AffordanceInertness.fs"; "ui/BindingWalk.fs" ] do
                  let text = source file

                  Expect.isFalse
                      (handRecursion.IsMatch text)
                      (sprintf "%s binds an Action.Chain / Confirm member list: a hand recursion has grown back" file)

                  Expect.stringContains text "Generated.Action.fold" (sprintf "%s folds through the one walk" file)
          } ]

// ─── Chart X class ───────────────────────────────────────────────────────────

let private queryChart (kind: ChartKind) (xScale: ChartXScale option) : Node<Msg> =
    node
        "cht"
        (NodeKind.Chart
            { Defaults.chart<Msg> with
                Kind = kind
                XScale = xScale
                Source = binding.query "sales" (fun (x: Row seq) -> x)
                XField = "x"
                YFields = [ "y" ] })

let private xMismatch (kind: ChartKind) (xScale: ChartXScale option) (xType: ColumnType) : bool =
    QueryBinding.check [ "x", xType; "y", FloatType ] (queryChart kind xScale)
    |> List.exists (fun d -> d.Code = QueryBinding.TypeMismatchCode && d.Column = "x")

/// FUARAN087 / FUARAN097 on the x field, over an embedded table that has the
/// same two columns.
let private xRefusedByValidator (kind: ChartKind) (xScale: ChartXScale option) (xType: ColumnType) : bool =
    let table: Table =
        { Schema = [ "x", xType; "y", FloatType ]
          Columns = [] }

    let tree =
        node
            "cht"
            (NodeKind.Chart
                { Defaults.chart<Msg> with
                    Kind = kind
                    XScale = xScale
                    Source = Binding.Transform(TransformSource.Data(DataSource.Embedded table), [], None)
                    XField = "x"
                    YFields = [ "y" ] })

    match PreEmitValidate.validate tree with
    | Ok() -> false
    | Error defects ->
        defects
        |> List.exists (fun d ->
            match d with
            | PreEmitValidate.PreEmitDefect.ChartFieldTypeMismatch(_, "x", _)
            | PreEmitValidate.PreEmitDefect.ChartTemporalXNotDate(_, "x", _) -> true
            | _ -> false)

let private chartXTests =
    testList
        "chart X class"
        [ test "a scatter over a float X raises no FUARAN066" {
              // The shard's input. FUARAN087 requires a numeric scatter X, and
              // this check used to call that X categorical and refuse it.
              Expect.isFalse (xMismatch ChartKind.Scatter None FloatType) "a float X is what a scatter plots"
              Expect.isFalse (xMismatch ChartKind.Scatter None IntType) "an int X likewise"
              Expect.isTrue (xMismatch ChartKind.Scatter None StringType) "a string X is still refused"
          }

          test "FUARAN066 refuses every X column FUARAN087 / FUARAN097 refuses, and admits none they refuse" {
              // Agreement in both directions, over every column type. The one
              // asymmetry is bool on a scatter X: FUARAN087 coerces it to 1/0,
              // while this check's numeric class admits int and float only, as
              // it does for every numeric sink. This check is stricter there,
              // never looser.
              let cases =
                  [ ChartKind.Scatter, None
                    ChartKind.Scatter, Some ChartXScale.Temporal
                    ChartKind.Line, Some ChartXScale.Temporal
                    ChartKind.Bar, Some ChartXScale.Temporal ]

              for kind, xScale in cases do
                  for t in ColumnType.all do
                      let validator = xRefusedByValidator kind xScale t
                      let query = xMismatch kind xScale t

                      if validator then
                          Expect.isTrue
                              query
                              (sprintf
                                  "%A/%A X typed %s: the validator refuses it, FUARAN066 must too"
                                  kind
                                  xScale
                                  (ColumnType.tag t))

                      if not query then
                          Expect.isFalse
                              validator
                              (sprintf
                                  "%A/%A X typed %s: FUARAN066 admits it, the validator must too"
                                  kind
                                  xScale
                                  (ColumnType.tag t))
          }

          test "a categorical X keeps its class on the other kinds" {
              Expect.isFalse (xMismatch ChartKind.Bar None StringType) "a string category"
              Expect.isTrue (xMismatch ChartKind.Bar None FloatType) "unchanged: a float is not a bar category"
          } ]

// ─── UTF-8 ───────────────────────────────────────────────────────────────────

/// Strings built from `char` values, because an F# literal lone surrogate
/// compiles as U+FFFD and could not test anything.
let private utf16Cases: (string * string) list =
    [ "lone high then ASCII", String([| char 0xD800; 'A' |])
      "lone high at the end", String([| 'A'; char 0xDBFF |])
      "lone low", String([| char 0xDC00; 'A' |])
      "low then high", String([| char 0xDC00; char 0xD800 |])
      "two highs then a low", String([| char 0xD800; char 0xD800; char 0xDC00 |])
      "a well-formed pair", String([| char 0xD83D; char 0xDE00 |])
      "BMP and ASCII", "café €" ]

let private bclSha256 (s: string) : string =
    use sha = Security.Cryptography.SHA256.Create()

    sha.ComputeHash(Encoding.UTF8.GetBytes s)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

let private utf8Tests =
    testList
        "UTF-8"
        [ test "Fuaran.Core.Hash.sha256Hex matches the BCL on ill-formed UTF-16" {
              for name, s in utf16Cases do
                  Expect.equal (Fuaran.Core.Hash.sha256Hex s) (bclSha256 s) (sprintf "digest of %s" name)
          }

          test "the byte-length meter and the hash count the same bytes" {
              for name, s in utf16Cases do
                  Expect.equal
                      (Fuaran.Core.Hash.utf8Bytes s).Length
                      (Encoding.UTF8.GetByteCount s)
                      (sprintf "Core encoder length of %s" name)

                  Expect.equal
                      (HostLimits.utf8Length s)
                      (Encoding.UTF8.GetByteCount s)
                      (sprintf "HostLimits length of %s" name)
          }

          test "census: one UTF-8 encoder" {
              Expect.isFalse
                  ((source "ui/HostLimits.fs").Contains("0xDBFF", StringComparison.Ordinal))
                  "HostLimits reads Core's encoder rather than decoding surrogates itself"

              // Phase 2309: the tier's own encoder and SHA-256 retired onto Core's. A copy
              // growing back would agree with Core on the day it was written.
              let hashing = source "ui/Hashing.fs"

              Expect.isFalse
                  (hashing.Contains("0xDBFF", StringComparison.Ordinal))
                  "Hashing holds no UTF-8 encoder of its own"

              Expect.isFalse
                  (hashing.Contains("0x428a2f98", StringComparison.OrdinalIgnoreCase))
                  "Hashing holds no SHA-256 of its own (the first round constant is its signature)"
          } ]

// ─── Extra-attribute keys ────────────────────────────────────────────────────

let private keyCases: (string * bool) list =
    [ "data-testid", true
      "aria-label", true
      "  data-trimmed  ", true
      "data-x onmouseover=alert(1)", false
      "data-x=1", false
      "onclick", false
      "ONCLICK", false
      "style", false
      "class", false
      "", false
      "   ", false ]

let private keyTests =
    testList
        "extra-attribute keys"
        [ test "the smart constructor drops a key that would open a second attribute" {
              // The shard's input. The renderer drops this key, so the smart
              // constructor used to accept a key that could never reach the DOM.
              let tagged =
                  node "m" (NodeKind.Metric Defaults.metric)
                  |> Node.withExtraAttribute "data-x onmouseover=alert(1)" "v"

              Expect.isNone tagged.ExtraAttributes "rejected, as the renderer floor rejects it"
          }

          test "the smart constructor, the renderer floor and the one predicate agree on every key" {
              for key, expected in keyCases do
                  Expect.equal (ExtraAttributeKey.isAllowed key) expected (sprintf "the predicate on %A" key)

                  Expect.equal
                      (Renderer.Sanitize.isAllowedExtraAttributeKey key)
                      expected
                      (sprintf "the renderer floor on %A" key)

                  let kept =
                      (node "m" (NodeKind.Metric Defaults.metric) |> Node.withExtraAttribute key "v").ExtraAttributes
                      |> Option.isSome

                  Expect.equal kept expected (sprintf "the smart constructor on %A" key)
          }

          test "census: no copy of the key rule outside ExtraAttributeKey" {
              for file in
                  [ "ui/Fuaran.fs"
                    "validator/ExtraAttributesCheck.fs"
                    "renderer-core/Sanitize.fs" ] do
                  let text = source file

                  let copies = RegularExpressions.Regex.Matches(text, "StartsWith\\(\"data-\"").Count

                  // Fuaran.fs holds the one definition.
                  let allowed = if file = "ui/Fuaran.fs" then 1 else 0
                  Expect.equal copies allowed (sprintf "%s states the data-* rule itself" file)
          } ]

[<Tests>]
let tests =
    testList
        "Phase 2042 — core predicates have one definition"
        [ writeBackTests; actionWalkTests; chartXTests; utf8Tests; keyTests ]
