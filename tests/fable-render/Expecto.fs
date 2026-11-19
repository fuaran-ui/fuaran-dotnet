module FableRender.Expecto

// ============================================================================
//  Phase 2550 — Expecto's call shape, for a suite that only ever runs under
//  Fable.
//
//  Expecto does not transpile, so the client render suite cannot reference it.
//  The tests are written against the subset of its surface they use —
//  `testList`, `testCase`, `Expect.*`, `failtest` — so they read like the .NET
//  suites beside them and could move to Expecto unchanged if it ever did.
//
//  The runner is deliberately small: run every case, catch what it throws,
//  print one line per case and a TOTAL line, and return the counts. The exit
//  status and the vacuity check (a run of zero tests is a failure) are
//  `Program.fs`'s and `drive.mjs`'s.
// ============================================================================

#if FABLE_COMPILER
exception AssertException of string

type Test =
    | Case of name: string * body: (unit -> unit)
    | List of name: string * tests: Test list

let testCase (name: string) (body: unit -> unit) : Test = Case(name, body)

let testList (name: string) (tests: Test list) : Test = List(name, tests)

let failtest (message: string) : 'T = raise (AssertException message)

[<RequireQualifiedAccess>]
module Expect =
    let isTrue (actual: bool) (message: string) : unit =
        if not actual then
            failtest message

    let isFalse (actual: bool) (message: string) : unit =
        if actual then
            failtest message

    let equal (actual: 'T) (expected: 'T) (message: string) : unit =
        if actual <> expected then
            failtest (sprintf "%s: expected %A, got %A" message expected actual)

    let isGreaterThan (actual: int) (bound: int) (message: string) : unit =
        if not (actual > bound) then
            failtest (sprintf "%s: expected more than %d, got %d" message bound actual)

    /// Reference identity — `===` under Fable, which is the whole subject of the
    /// element-identity claims.
    let same (actual: obj) (expected: obj) (message: string) : unit =
        if not (obj.ReferenceEquals(actual, expected)) then
            failtest (message + ": expected the identical object, got a different one")

    let notSame (actual: obj) (expected: obj) (message: string) : unit =
        if obj.ReferenceEquals(actual, expected) then
            failtest (message + ": expected a different object, got the identical one")

/// The verdict of one run: how many cases ran and how many failed.
type Verdict = { Tests: int; Failed: int }

/// Run every case depth-first, in declaration order, printing `PASS` / `FAIL`
/// per case. A case passes when it returns; anything it throws fails it.
let runTests (root: Test) : Verdict =
    let rec go (path: string) (test: Test) (acc: Verdict) : Verdict =
        match test with
        | List(name, tests) ->
            let here = if path = "" then name else path + " / " + name
            tests |> List.fold (fun a t -> go here t a) acc
        | Case(name, body) ->
            let full = if path = "" then name else path + " / " + name

            let failure =
                try
                    body ()
                    None
                with
                | AssertException message -> Some message
                | e -> Some("threw: " + e.Message)

            match failure with
            | None ->
                printfn "PASS %s" full
                { acc with Tests = acc.Tests + 1 }
            | Some message ->
                printfn "FAIL %s -- %s" full message

                { Tests = acc.Tests + 1
                  Failed = acc.Failed + 1 }

    let verdict = go "" root { Tests = 0; Failed = 0 }
    printfn "TOTAL tests=%d failed=%d" verdict.Tests verdict.Failed
    verdict
#endif
