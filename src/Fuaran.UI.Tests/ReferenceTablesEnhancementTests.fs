module Fuaran.UI.Tests.ReferenceTablesEnhancement

#nowarn "3261" // `DirectoryInfo.Parent` is legitimately nullable here (the climb's terminator).

// ============================================================================
//  Phase 1532 — `content/fuaran-reference-tables.js` enhances idempotently.
//
//  The behaviour: `enhance` marks a table it has wired and returns early on a
//  table already carrying that mark, so a second pass binds no second set of
//  handlers; and a mutation observer re-scans, so a table that arrives after
//  load is reached rather than left static forever.
//
//  ── Why this is a SOURCE-SHAPE guard, and what that costs ─────────────────
//  These assertions read the file as text. They are not the test anyone would
//  write first: the honest test drives the script against a DOM and counts the
//  handlers bound to a header, and that needs a JavaScript runtime and a DOM,
//  neither of which this repo's gate has for `content/*.js` — the reference
//  stylesheet beside this file is covered the same way, by reading it.
//
//  So the claim these make is narrow and worth stating exactly: the guard
//  against the enhancement is present, it is checked before anything is bound,
//  and the re-scan exists. They cannot see a guard that is present and wrong.
//  The behaviour itself was verified against a DOM stub while the change was
//  made — the pre-1532 file bound 2 click handlers to a header after two
//  passes and reached a late-arriving table 0 times; the current file binds 1
//  and reaches it — and that probe is not committed, because a test with no
//  runner in the gate is a test nobody runs.
//
//  A JS harness for this directory is the thing that would replace these, and
//  it is a larger piece of work than one phase's task.
// ============================================================================

open System
open System.IO
open Expecto

/// Climb to the repo root, identified by two files rather than one so a nested
/// checkout cannot be mistaken for it. Same shape as `TrustedTypesTests`.
let private repoRoot: string =
    let rec climb (dir: DirectoryInfo) =
        if isNull dir then
            failwith "ReferenceTablesEnhancementTests: could not locate the repo root from the test binary's directory."
        elif
            File.Exists(Path.Combine(dir.FullName, "Fuaran.sln"))
            && File.Exists(Path.Combine(dir.FullName, "SANITIZATION.md"))
        then
            dir.FullName
        else
            climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

let private source =
    File.ReadAllText(Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer", "content", "fuaran-reference-tables.js"))

/// The body of `function enhance(table) { … }`, taken to the next top-level
/// `function ` at the same indent. Narrower than the whole file on purpose: the
/// ordering assertion below is about what `enhance` does first, and measured
/// against the file it would be satisfied by any `addEventListener` anywhere.
let private enhanceBody: string =
    let start = source.IndexOf("function enhance(table)", StringComparison.Ordinal)

    if start < 0 then
        failwith
            "the file no longer declares `function enhance(table)` — this guard is reading for a shape that has moved"

    let next = source.IndexOf("\n  function ", start + 1)
    let stop = if next < 0 then source.Length else next
    source.Substring(start, stop - start)

[<Tests>]
let tests =
    testList
        "Phase 1532 — reference table enhancement is idempotent"
        [ test "enhance marks the table it wired" {
              Expect.stringContains
                  enhanceBody
                  "table.setAttribute(ENHANCED, 'true')"
                  "a pass that binds handlers records that it did — without the mark there is nothing for the next pass to read"
          }

          test "the mark is read before anything is bound" {
              let guard =
                  enhanceBody.IndexOf("table.getAttribute(ENHANCED)", StringComparison.Ordinal)

              let firstBind = enhanceBody.IndexOf("addEventListener", StringComparison.Ordinal)

              Expect.isGreaterThan guard -1 "enhance reads the mark"
              Expect.isGreaterThan firstBind -1 "enhance binds at least one listener"

              Expect.isLessThan
                  guard
                  firstBind
                  "the mark is checked BEFORE the first listener is bound — a guard after the binding is not a guard, and the doubled handler is bound by the time it runs"
          }

          test "a table that arrives after load is re-scanned" {
              Expect.stringContains
                  source
                  "new MutationObserver("
                  "the file keeps looking — a single pass at load leaves a revealed panel or a swapped-in fragment static forever, with no exported function to call"

              Expect.stringContains
                  source
                  "observer.observe(document.body"
                  "and the observer is actually attached, which is a different statement from having constructed one"
          }

          test "the re-scan is guarded where there is no observer" {
              Expect.stringContains
                  source
                  "typeof MutationObserver === 'undefined'"
                  "an environment with no observer keeps the single initial pass rather than throwing on load"
          }

          test "the header no longer claims a second load is unsupported" {
              Expect.isFalse
                  (source.Contains("Loading it twice is not supported", StringComparison.Ordinal))
                  "the compatibility note said a second load doubles the click handlers; it no longer does, and a note that describes the old behaviour is worse than none"
          } ]
