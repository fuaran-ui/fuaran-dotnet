module internal Fuaran.UI.Renderer.PreparedSources

// ============================================================================
//  Fuaran — one prepared source per render (Phase 1890).
//
//  A dashboard binds a dozen `Transform` sites to ONE source: a chart, a
//  league table and a top-N list over the same state key, or over the same
//  embedded table. Each site used to turn that source into the evaluator's
//  input on its own. For a LIVE source that is the expensive half of the whole
//  site: the store value is lifted to `JVal`, row-major rows are transposed,
//  and Core's columnar codec decodes every cell, before the evaluator sees a
//  single typed column. Measured at Phase 1890 over 10,000 rows and twelve
//  sites, the conversion was three quarters of the render for a columnar store
//  value and nine tenths for row-major rows. This module makes it once per
//  source per render, and hands every site the same `Fuaran.Compute.Prepared`
//  value, which `DataFrame.evalPrepared` evaluates exactly as
//  `evalPipelineInEnv` evaluates the table it stands for.
//
//  THE SCOPE IS THE RENDER, AND A RENDER IS ONE `BindingSources` VALUE. The
//  resolver is handed nothing else that names a render: a renderer builds its
//  context's sources once and resolves every binding of the render against
//  that value, and a scoped subtree resolves against its own copy. So the
//  cache is held WEAKLY under that value: nothing here keeps a render's
//  sources alive, and when the render's context goes, its prepared sources go
//  with it. A host that resolves several renders against ONE value (a record it
//  keeps because nothing in it changed) shares preparations across them, which
//  the key below makes correct. Cross-render reuse of EVALUATIONS is the
//  session store's business (`ILiveTransformStore`, Phases 1586 / 1761), not
//  this module's.
//
//  THE KEY IS THE SOURCE OBJECT ITSELF, BY REFERENCE. Within one render a
//  state key resolves to one store value and an embedded source is one table,
//  so two sites over the same source hand in the same object. Identity is also
//  what makes the cache CORRECT rather than merely fast: a scoped subtree that
//  rebinds a key resolves a different object and therefore prepares its own
//  source, and a host that edits a slot stores a new value rather than
//  mutating the one a render already read — the same contract the renderers'
//  own reference-equality change detection rests on. No source is hashed: a
//  content key would cost the conversion it exists to save. The inner table is
//  weak too, so a long-lived `BindingSources` (a host that resolves against
//  one record for its whole life) retains nothing past its source values.
//
//  WHAT IS CACHED is the ANSWER, refusals included: a source that cannot be
//  read as a table refuses identically at every site, so the conversion that
//  failed once is not repeated eleven times to fail again.
//
//  The seam for an edit-driven refresh (Phase 1891) is this module: an edit
//  that produces a new store value from an old one can install the edited
//  version (`ColumnOps.applyPrepared` over the old one's `Prepared`) under the
//  new value, and every site of the render then reads the delta-built source.
// ============================================================================

open Fuaran.Core
open Fuaran.Compute

// The JavaScript arm below reads a `WeakMap`, whose `get` answers `undefined`
// for an absent key, so its `isNull` tests are load-bearing. Under Fable the
// ENTRY project's nullness setting governs the whole source graph, and the
// project-wide `<Nullable>disable</Nullable>` does not travel, so the
// suppression is file-scoped here, as in `BindingResolver.fs` and `Sanitize.fs`.
#nowarn "3261"

/// One source's answer, held by reference so a weak table can carry it
/// (`Result` is a struct).
[<AllowNullLiteral>]
type private Entry(answer: Result<Prepared, string>) =
    member _.Answer = answer

#if FABLE_COMPILER
open Fable.Core

[<Emit("new WeakMap()")>]
let private newWeakMap () : obj = jsNative

[<Emit("$0.get($1)")>]
let private weakGet (_map: obj) (_key: obj) : obj = jsNative

[<Emit("$0.set($1, $2)")>]
let private weakSet (_map: obj) (_key: obj) (_value: obj) : unit = jsNative

/// A `WeakMap` key must be an object; a primitive (a string store value) is
/// prepared afresh at each site, which costs nothing a cache could save.
[<Emit("$0 !== null && $0 !== undefined && (typeof $0 === 'object' || typeof $0 === 'function')")>]
let private isWeakKey (_key: obj) : bool = jsNative

let private scopes: obj = newWeakMap ()

let private scopeOf (sources: Fuaran.UI.BindingSources) : obj =
    let held = weakGet scopes (box sources)

    if isNull held then
        let fresh = newWeakMap ()
        weakSet scopes (box sources) fresh
        fresh
    else
        held

let private entryOf (sources: Fuaran.UI.BindingSources) (source: obj) (make: unit -> Entry) : Entry =
    if not (isWeakKey source) then
        make ()
    else
        let scope = scopeOf sources
        let held = weakGet scope source

        if isNull held then
            let fresh = make ()
            weakSet scope source (box fresh)
            fresh
        else
            unbox<Entry> held
#else
open System.Runtime.CompilerServices

let private scopes =
    ConditionalWeakTable<Fuaran.UI.BindingSources, ConditionalWeakTable<obj, Entry>>()

let private entryOf (sources: Fuaran.UI.BindingSources) (source: obj) (make: unit -> Entry) : Entry =
    let scope = scopes.GetValue(sources, (fun _ -> ConditionalWeakTable<obj, Entry>()))
    scope.GetValue(source, (fun _ -> make ()))
#endif

/// The prepared form of `source` for the render `sources` stands for: built by
/// `toTable` and `DataFrame.prepare` the first time a site of this render asks,
/// and the very same `Prepared` (or the very same refusal) for every later site
/// that hands in the same source object. `source` is the object the table is
/// read FROM — the resolved store value, or the embedded table itself.
let prepare
    (sources: Fuaran.UI.BindingSources)
    (source: obj)
    (toTable: unit -> Result<Table, string>)
    : Result<Prepared, string> =
    (entryOf sources source (fun () -> Entry(toTable () |> Result.map DataFrame.prepare))).Answer
