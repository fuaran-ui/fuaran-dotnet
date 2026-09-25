module Fuaran.UI.FastPath.Tests.Program

open Expecto

// Phase 1478 added an `--emit-laws <dir>` flag here that wrote
// `laws/capability-laws.json` into the shared corpus. Since fuaran-core Phase
// 235 that file is Core's to emit (its own `--emit-laws`), and this suite only
// READS it — so there is no flag left, and this tier emits no law set.
[<EntryPoint>]
let main argv = runTestsInAssemblyWithCLIArgs [] argv
