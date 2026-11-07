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
