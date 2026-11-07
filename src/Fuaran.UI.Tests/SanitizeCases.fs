module Fuaran.UI.Tests.SanitizeCases

// ============================================================================
//  Phase 2010 — the markdown floor's cases, shared by two pipelines.
//
//  The .NET suite (`SanitizeTests.fs`) and the Fable law harness
//  (`tests/fable-laws/SanitizeLaw.fs`, which links this file) run the SAME
//  cases, because the defect they pin shows on one pipeline only: under Fable
//  a string lowercase is `toLowerCase()`, which turns each U+0130 into two
//  units, so an index taken from a lowered copy and applied to the original
//  lands one position early per such character (fuaran-core#291 fixed Core's
//  copy of the floor; this is the renderer's). On .NET `ToLowerInvariant` is
//  length-preserving and the same cases pass, which is why the cases live
//  where both pipelines can run them.
//
//  NOTHING HERE MAY USE A CONSTRUCT FABLE CANNOT LOWER: no Expecto, no
//  reflection, no `StringComparer`. The floor predicate below is restated from
//  the CLAIM in `Sanitize.fs`'s comments, never by calling into it, so a scrub
//  and the check that judges it cannot share a defect by sharing a helper.
// ============================================================================

/// ASCII case fold of one UTF-16 unit — the comparison a browser makes for every token here.
let private fold (c: char) : char =
    if c >= 'A' && c <= 'Z' then char (int c + 32) else c

/// Whether `needle` (lower-case ASCII) occurs at `at` in `hay` under the ASCII fold.
let private foldedAt (hay: string) (needle: string) (at: int) : bool =
    if at < 0 || at + needle.Length > hay.Length then
        false
    else
        let mutable k = 0

        while k < needle.Length && fold hay[at + k] = needle[k] do
            k <- k + 1

        k = needle.Length

/// The element openers the floor strips.
let dangerousOpeners: string list =
    [ "<script"; "<iframe"; "<object"; "<embed"; "<form"; "<link"; "<meta" ]

/// The schemes the floor neutralises inside a tag.
let dangerousSchemes: string list = [ "javascript:"; "vbscript:" ]

let private isNameBoundary (s: string) (at: int) =
    at >= s.Length
    || (let c = s[at] in c = ' ' || c = '\t' || c = '\n' || c = '\r' || c = '/' || c = '>')

/// What the RENDERER's markdown floor forbids in its output, named, or `None`:
///
///  - a dangerous element opener whose name ends at a tag-name boundary (`<metadata>` is not
///    `<meta>`: Phase 1546);
///  - an `on*` handler inside a start tag — white space, `on` under the fold, a letter;
///  - a `javascript:` / `vbscript:` scheme inside a tag interior (the nearest `<` or `>` before it
///    is a `<`). Outside a tag the token is text the renderer escaped by construction, and the
///    floor leaves it as written (Phase 1523).
///
/// Narrower than Core's law family's markdown floor on the last two points of difference — the
/// boundary and the tag anchor — and the narrowing is the renderer's decided contract, not a gap
/// (`docs/DECISIONS.md` D25).
let floorBreach (s: string) : string option =
    let mutable breach = None
    let mutable inside = false
    let mutable i = 0

    while breach.IsNone && i < s.Length do
        let c = s[i]

        match
            dangerousOpeners
            |> List.tryFind (fun o -> foldedAt s o i && isNameBoundary s (i + o.Length))
        with
        | Some o -> breach <- Some("a live " + o + " element")
        | None ->
            if inside then
                match dangerousSchemes |> List.tryFind (fun p -> foldedAt s p i) with
                | Some p -> breach <- Some("a live " + p + " scheme inside a tag")
                | None ->
                    if
                        (c = ' ' || c = '\t' || c = '\n')
                        && i + 3 < s.Length
                        && fold s[i + 1] = 'o'
                        && fold s[i + 2] = 'n'
                        && System.Char.IsLetter s[i + 3]
                    then
                        breach <- Some "an inline on* handler inside a tag"

        if c = '<' then
            inside <- true
        elif c = '>' then
            inside <- false

        i <- i + 1

    breach

/// A run of `n` capital-I-with-dot-above characters (U+0130): the one character whose lower case
/// is two UTF-16 units under JavaScript's `toLowerCase()`.
let dottedRun (n: int) : string = System.String('İ', n)

/// A lone high surrogate, built from its value: an F# string LITERAL holding one compiles to
/// U+FFFD, which would test nothing.
let loneSurrogate: string = string (char 0xD800)

/// The literal cases, `(name, input, expected output)`. `name` is ASCII so a refutation can print
/// it on both pipelines; the strings themselves are never printed.
let literalCases: (string * string * string) list =
    [ for n in [ 1; 3; 11; 12 ] do
          for p in dangerousSchemes do
              let tag = if p = "javascript:" then "js" else "vbs"

              // A scheme inside a tag, after a run: the index the sweep takes must land on it.
              yield
                  (sprintf "run %d before %s in a tag" n tag,
                   "<a href=\"" + dottedRun n + p + "x\">y</a>",
                   "<a href=\"" + dottedRun n + "about:blankx\">y</a>")

              // The same token in body text is prose, and survives as written.
              yield (sprintf "run %d before %s in text" n tag, dottedRun n + p + "x", dottedRun n + p + "x")

              // A scan after the run: an element, a handler and a scheme further along.
              yield
                  (sprintf "run %d then element, handler and %s" n tag,
                   "<p>"
                   + dottedRun n
                   + "</p><script>alert(1)</script><a href=\""
                   + p
                   + "x\" onclick=\"y\">z</a>",
                   "<p>" + dottedRun n + "</p><a href=\"about:blankx\">z</a>")

          // A handler inside a tag that holds the run.
          yield
              (sprintf "run %d then a handler in the same tag" n,
               "<a " + dottedRun n + " onclick=\"x\">y</a>",
               "<a " + dottedRun n + ">y</a>")

      // A lone surrogate is one unit on both pipelines and must not move an index either.
      yield
          ("lone surrogate before a scheme in a tag",
           loneSurrogate + "<a href=\"javascript:x\">y</a>",
           loneSurrogate + "<a href=\"about:blankx\">y</a>")

      yield
          ("lone surrogate inside a tag before a handler",
           "<a " + loneSurrogate + " onload=\"x\">y</a>",
           "<a " + loneSurrogate + ">y</a>")

      // Removal splices its neighbours together. One pass of the sweep strips `<iframe>` AFTER the
      // `<script` step has run, joining `<scr` and `ipt>` into a live element; stripping the handler
      // from `<scri onx=""pt>` does the same. Core found both (Phase 349); the floor repeats the
      // pass until it changes nothing.
      yield ("element joined by removing an element", "<scr<iframe>ipt>alert(1)</script>", "")
      yield ("element joined by removing a handler", "<scri onx=\"\"pt>alert(1)", "alert(1)")

      yield ("handler joined by removing an element", "<a o<iframe></iframe>nclick=\"x\">y</a>", "<a>y</a>")

      // Any element swept after `<script` can join one, not only `<iframe>`.
      yield ("element joined after a scheme sweep", "<scr<form></form>ipt>x</script>", "")

      yield ("benign prose", "one only once onto online", "one only once onto online") ]

// ============================================================================
//  The certification: Core's sanitisation family over the renderer's floor.
//
//  `Conformance.sanitizeLawsAt` holds a host's six floor functions to the floor
//  Core's own copy claims. The renderer's copy claims a NARROWER floor in two
//  decided places (`docs/DECISIONS.md` D25), so two of the family's six laws are
//  red for this witness BY DESIGN, and the adopting run states that limit
//  rather than stepping around it (the `containerLaws` posture):
//
//   - the attribute law: `sanitizeExtraAttributes` emits the TRIMMED key
//     (Phase 788: the gate judges `key.Trim()`, so emitting the original would
//     emit a string the gate never inspected), where the law expects the key as
//     given; and the key gate refuses any character outside `[A-Za-z0-9-]`,
//     which Core's claim admits;
//   - the markdown floor law: a scheme in body text is prose and survives
//     (Phase 1523), and `<metadata>` is not `<meta>` (Phase 1546), where the law
//     forbids every occurrence.
//
//  A red law records only its FIRST counterexample, so the two red cells alone
//  could hide a genuine breach behind the decided one. The witness therefore
//  RECORDS every input the family hands it, and `judge` holds each of them to
//  the renderer's own claim: the floor above, and a key gate that admits only
//  what Core's claim admits. The family's generator is reused rather than
//  restated, so the inputs are exactly the ones Core's own floor is held to.
// ============================================================================

/// The inputs a run of the family handed the renderer's floor.
type Recorder =
    { Markdown: ResizeArray<string>
      Keys: ResizeArray<string>
      Maps: ResizeArray<Map<string, string>> }

/// The renderer's six floor functions as a `SanitizeWitness`, each recording its input.
let recordingWitness () : Fuaran.Core.SanitizeWitness * Recorder =
    let r =
        { Markdown = ResizeArray()
          Keys = ResizeArray()
          Maps = ResizeArray() }

    let witness: Fuaran.Core.SanitizeWitness =
        { SanitizeUrl = Fuaran.UI.Renderer.Sanitize.sanitizeUrl
          SanitizeUrlOrBlank = Fuaran.UI.Renderer.Sanitize.sanitizeUrlOrBlank
          IsAllowedAttributeKey =
            fun k ->
                r.Keys.Add k
                Fuaran.UI.Renderer.Sanitize.isAllowedExtraAttributeKey k
          IsSafeAttributeValue = Fuaran.UI.Renderer.Sanitize.isSafeExtraAttributeValue
          SanitizeAttributes =
            fun m ->
                r.Maps.Add m
                Fuaran.UI.Renderer.Sanitize.sanitizeExtraAttributes m
          ScrubMarkdown =
            fun s ->
                r.Markdown.Add s
                Fuaran.UI.Renderer.Sanitize.sanitizeMarkdownHtml s }

    witness, r

/// The laws the renderer's floor holds outright, by the opening of their statements.
let heldLaws: string list =
    [ "an accepted URL passes the floor"
      "the URL floor is idempotent"
      "the URL floor refuses every dangerous URL"
      "the markdown scrub is idempotent" ]

/// The laws red by design, each with the text its first counterexample must carry: the decided
/// divergence, met on the family's fixed vectors at iteration 0, so the text is deterministic.
let statedLimits: (string * string) list =
    [ "the attribute floor is exactly its claim", "sanitizeAttributes"
      "scrubbed markdown passes the floor", "a live javascript: scheme" ]

/// Core's key claim, restated: a trimmed `data-` / `aria-` key that is not an `on*` handler and not
/// `style`.
let private coreKeyClaim (k: string) =
    let t = k.Trim()

    t.Length > 0
    && (t.StartsWith("data-", System.StringComparison.Ordinal)
        || t.StartsWith("aria-", System.StringComparison.Ordinal))
    && not (t.Length >= 2 && fold t[0] = 'o' && fold t[1] = 'n')
    && not (t.Length = 5 && foldedAt t "style" 0)

let private isNameChar (c: char) =
    (c >= 'a' && c <= 'z')
    || (c >= 'A' && c <= 'Z')
    || (c >= '0' && c <= '9')
    || c = '-'

/// What a run of the family proves about the renderer's floor.
type Verdict =
    { Laws: int
      Held: int
      StatedLimit: int
      Inputs: int
      Refutations: string list }

/// Hold one run's results, and every input it handed the floor, to the renderer's claim.
let judge (results: Fuaran.Core.LawResult list) (r: Recorder) : Verdict =
    let starting (prefix: string) =
        results
        |> List.filter (fun x -> x.Law.StartsWith(prefix, System.StringComparison.Ordinal))

    let refutations =
        [ for prefix in heldLaws do
              match starting prefix with
              | [ x ] when x.Passed -> ()
              | [ _ ] -> yield "held law went red: " + prefix
              | xs -> yield "expected one law opening '" + prefix + "', found " + string (List.length xs)

          for prefix, text in statedLimits do
              match starting prefix with
              | [ x ] when x.Passed -> yield "stated limit no longer reached, assert it held instead: " + prefix
              | [ x ] ->
                  match x.Counterexample with
                  | Some c when c.Contains text -> ()
                  | _ -> yield "red for a reason outside its stated limit: " + prefix
              | xs -> yield "expected one law opening '" + prefix + "', found " + string (List.length xs)

          if r.Markdown.Count = 0 || r.Keys.Count = 0 || r.Maps.Count = 0 then
              yield "the family handed the floor no markdown, key or attribute map"

          for i in 0 .. r.Markdown.Count - 1 do
              match floorBreach (Fuaran.UI.Renderer.Sanitize.sanitizeMarkdownHtml r.Markdown[i]) with
              | Some breach -> yield "markdown input " + string i + ": " + breach
              | None -> ()

          for i in 0 .. r.Keys.Count - 1 do
              let k = r.Keys[i]
              let allowed = Fuaran.UI.Renderer.Sanitize.isAllowedExtraAttributeKey k

              if allowed && not (coreKeyClaim k) then
                  yield "key " + string i + ": admitted a key Core's claim refuses"
              elif not allowed && coreKeyClaim k && Seq.forall isNameChar (k.Trim()) then
                  yield
                      "key "
                      + string i
                      + ": refused a key Core's claim admits, with no character outside the name set"

          for i in 0 .. r.Maps.Count - 1 do
              let m = r.Maps[i]
              let kept = Fuaran.UI.Renderer.Sanitize.sanitizeExtraAttributes m

              let expected =
                  m
                  |> Map.fold
                      (fun acc k v ->
                          if
                              Fuaran.UI.Renderer.Sanitize.isAllowedExtraAttributeKey k
                              && Fuaran.UI.Renderer.Sanitize.isSafeExtraAttributeValue v
                          then
                              Map.add (k.Trim()) v acc
                          else
                              acc)
                      Map.empty

              if kept <> expected then
                  yield
                      "attribute map "
                      + string i
                      + ": kept other than the passing entries, re-keyed trimmed"
              elif Fuaran.UI.Renderer.Sanitize.sanitizeExtraAttributes kept <> kept then
                  yield "attribute map " + string i + ": not idempotent" ]

    { Laws = List.length results
      Held = List.length heldLaws
      StatedLimit = List.length statedLimits
      Inputs = r.Markdown.Count + r.Keys.Count + r.Maps.Count
      Refutations = refutations }
