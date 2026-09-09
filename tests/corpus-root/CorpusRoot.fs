// ============================================================================
//  The ONE corpus-root resolver (Phase 1647).
//
//  Every suite in this repo that certifies against the shared
//  `wire-format-fixtures/` corpus used to carry its own upward walk — twenty-odd
//  near-identical `climb`/`walk` functions, each answering the same question
//  slightly differently. They agreed on the common case and diverged on the two
//  that matter: a single-repo checkout (skip vs. fail) and a **git worktree**.
//
//  The worktree is the one that motivated this file. A worktree cut per the
//  campaign recipe lives at `<workspace-root>/wt/<NN>/`, so the walk from the
//  test binary climbs `wt\<NN>` → `wt` → `<workspace-root>` and finds no
//  `wire-format-fixtures/` at any of them: the corpus is a sibling of the REPO
//  (`Fuaran/Fuaran-UI/wire-format-fixtures`), not of the workspace root. Every
//  corpus-reading suite therefore SKIPS in a worktree, and a worker gets a green
//  gate that certified against nothing — which is precisely the shape the
//  "absent corpus must be a loud failure, never a skip" rule exists to prevent,
//  arriving through the back door of a resolver rather than a gate.
//
//  So: `FUARAN_WIRE_FIXTURES` is the override, and it is the same variable name
//  `Fuaran.UI.FastPath.Tests` already used — this file generalises that seam
//  rather than minting a second one.
//
//  Two properties are deliberate:
//
//  * **A SET-but-wrong override RAISES; it never falls through to the walk.**
//    Pointing the variable at a directory holding no `manifest.json` is a
//    configuration error, and silently walking past it certifies against a
//    corpus nobody named. `tryFind` returns `None` only when the variable is
//    unset AND the walk finds nothing — the genuine single-repo checkout.
//  * **The walk is unchanged.** It still climbs from `AppContext.BaseDirectory`
//    looking for `<dir>/manifest.json`, so every existing checkout resolves
//    exactly as before and this file changes no behaviour where the corpus was
//    already found.
//
//  Callers that need a SUBDIRECTORY (`nodes/`, `dag/`, `merge-conformance/`)
//  combine it themselves and keep their own existence check: a corpus root that
//  is present but lacks a family is a different statement from one that is
//  absent, and this module does not collapse the two.
// ============================================================================

module Fuaran.Tests.CorpusRoot

open System
open System.IO

/// The environment variable that overrides the walk.
[<Literal>]
let EnvVarName = "FUARAN_WIRE_FIXTURES"

/// The corpus clone's directory name — the interface every host resolves by
/// (the repo it is cloned from has been renamed; the directory name has not).
[<Literal>]
let DirName = "wire-format-fixtures"

/// A directory is the corpus root iff it holds `manifest.json`.
let private holdsCorpus (dir: string) =
    File.Exists(Path.Combine(dir, "manifest.json"))

/// The override, if the variable names anything at all. Raises when it names
/// something that is not a corpus — see the header.
let private fromEnv () : string option =
    match Environment.GetEnvironmentVariable EnvVarName with
    | null -> None
    | "" -> None
    | raw ->
        let path = raw.Trim()

        if path = "" then
            None
        elif holdsCorpus path then
            Some(Path.GetFullPath path)
        else
            failwithf
                "%s is set to '%s', which holds no manifest.json. The variable must name the corpus root itself (the directory containing manifest.json, nodes/, ops/ …). Unset it to fall back to the upward walk."
                EnvVarName
                path

/// The historical resolution: climb from the test binary's base directory
/// looking for a `wire-format-fixtures/` sibling.
let private byWalk () : string option =
    let rec climb (dir: DirectoryInfo | null) : string option =
        match dir with
        | null -> None
        | d ->
            let candidate = Path.Combine(d.FullName, DirName)

            if holdsCorpus candidate then
                Some candidate
            else
                climb d.Parent

    climb (DirectoryInfo AppContext.BaseDirectory)

/// The corpus root, or `None` on a genuine single-repo checkout.
let tryFind () : string option =
    match fromEnv () with
    | Some root -> Some root
    | None -> byWalk ()

/// The corpus root, failing loudly and naming the override when absent.
let find () : string =
    match tryFind () with
    | Some root -> root
    | None ->
        failwithf
            "%s/manifest.json not found walking up from %s. The corpus is a sibling clone of this repo (fuaran-ui/fuaran-ui-specification); in a git worktree it is not on the walk at all, so set %s to its path."
            DirName
            AppContext.BaseDirectory
            EnvVarName

/// The sentence a suite prints when it degrades to a skip, so the reader is
/// told about the override rather than left to conclude the checkout is wrong.
[<Literal>]
let AbsentSkipReason =
    "wire-format-fixtures/ absent (single-repo or worktree checkout) — set FUARAN_WIRE_FIXTURES to its path to check it"
