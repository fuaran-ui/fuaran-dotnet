module Fuaran.UI.Tests.RenderContextBuilder

// ============================================================================
//  Phase 2067 — one render-context builder.
//
//  `Render.RenderContext.create` is the one place the client context is built:
//  every convenience entry, the `Mount` guest context and the catalog go
//  through it. These cases pin what it builds (the two pre-render passes, the
//  convenience defaults, `RenderPolicy.defaults`), what `inScope` layers over
//  it, and that no full record literal has grown back anywhere else in the
//  repository — the shape that made every new field a many-site edit.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// F# 10 `box _` types as `obj | null`; the State map and the store take a
// non-null `obj`. Same `nn` workaround the other test modules use.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

/// A grid whose `$state.members` binding declares a default — the tree's one
/// state seed.
let private seededGridJson =
    """{"id":"member-grid","kind":{"$type":"DataGrid","columns":[{"field":"team","kind":{"$type":"Text"},"label":"Team"}],"rowKeyField":"team","source":{"$type":"State","defaultValue":[{"team":"Ops"}],"key":"members"}}}"""

let private tree: Node<obj> =
    let grid =
        match Fuaran.UI.Generated.decodeNode seededGridJson with
        | Ok node -> node
        | Error e -> failwithf "decode failed: %s" (Fuaran.Core.DecodeError.describe e)

    Fuaran.stack
        "builder-root"
        { Defaults.stack with
            Children =
                [ grid
                  Fuaran.fragmentDecl
                      "card-decl"
                      { Defaults.fragmentDecl with
                          Name = "card"
                          Body = Fuaran.divider "card-body" } ] }

// ─── The literal guard ──────────────────────────────────────────────────────

/// Every field of the record has to be named in a full literal, so the field
/// that starts a fresh traversal (`ExpandingFragments` set to the empty set)
/// marks one. The `with` copy in the fragment arm sets it to something else.
/// This file spells the marker only inside the regex below and in two halves
/// in the synthetic case, so it does not count itself.
let private literalMarker = Regex(@"\bExpandingFragments\s*=\s*Set\.empty\b")

/// The `(file, count)` pairs carrying a full `RenderContext` literal.
let internal literalSites (files: (string * string) list) : (string * int) list =
    files
    |> List.choose (fun (path, text) ->
        match literalMarker.Matches(text).Count with
        | 0 -> None
        | n -> Some(path, n))

let private repoRoot =
    lazy
        (let rec climb (dir: DirectoryInfo option) =
            match dir with
            | None -> None
            | Some d ->
                if
                    File.Exists(Path.Combine(d.FullName, "run.ps1"))
                    && File.Exists(Path.Combine(d.FullName, "Fuaran.sln"))
                then
                    Some d.FullName
                else
                    climb (Option.ofObj d.Parent)

         match climb (Some(DirectoryInfo(AppContext.BaseDirectory))) with
         | Some root -> root
         | None -> failwith "repository root not found above the test binary")

let private sourceFiles () : (string * string) list =
    let root = repoRoot.Value
    let sep = string Path.DirectorySeparatorChar

    [ "src"; "samples"; "templates" ]
    |> List.map (fun d -> Path.Combine(root, d))
    |> List.filter Directory.Exists
    |> List.collect (fun d -> Directory.GetFiles(d, "*.fs", SearchOption.AllDirectories) |> List.ofArray)
    |> List.filter (fun p ->
        not (p.Contains(sep + "bin" + sep))
        && not (p.Contains(sep + "obj" + sep))
        && not (p.Contains(sep + "node_modules" + sep))
        && not (p.Contains(sep + "fable_modules" + sep)))
    |> List.map (fun p -> Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllText p)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "RenderContext builder (Phase 2067)"
        [ test "create runs both pre-render passes over the tree" {
              let ctx = Render.RenderContext.create BindingResolver.empty ignore tree

              Expect.isTrue (Map.containsKey "members" ctx.Sources.State) "the tree's state seed is laid into Sources"

              Expect.isTrue
                  (Map.containsKey (FragmentId "card") ctx.Fragments)
                  "the tree's fragment declaration is registered"

              Expect.isEmpty (Set.toList ctx.ExpandingFragments) "no fragment is expanding at the root"
          }

          test "a furnished State value wins over the tree's seed" {
              let furnished =
                  { BindingResolver.empty with
                      State = Map.ofList [ "members", nn "host" ] }

              let ctx = Render.RenderContext.create furnished ignore tree
              Expect.equal (ctx.Sources.State["members"]) (nn "host") "the host owns named data; a seed is a floor"
          }

          test "create pins the convenience defaults and RenderPolicy.defaults" {
              let ctx = Render.RenderContext.create BindingResolver.empty ignore tree

              Expect.isTrue (obj.ReferenceEquals(ctx.Runtime, Runtime.diagnostic)) "the diagnostic runtime"
              Expect.isNone ctx.TelemetrySink "no telemetry sink"
              Expect.isNone ctx.ActionSink "no action sink"
              Expect.isNone ctx.UploadSink "no upload sink"
              Expect.isNone ctx.CurrentNodeId "no current node"
              Expect.isFalse ctx.InErrorBoundary "not under an error boundary"
              Expect.isEmpty (Map.toList ctx.SessionContext) "no session context"
              Expect.equal ctx.Policy RenderPolicy.defaults "the default policy"
              Expect.equal ctx.Policy.Egress Sanitize.denyNonLocalEgress "default-deny egress"
              Expect.isNone ctx.Policy.CustomHashFloor "the process hash floor"
              Expect.equal ctx.Policy.Csp Csp.Permissive "the permissive CSP posture"
              Expect.isNone ctx.Policy.Scope "the root scope"
          }

          test "inScope merges the scope's snapshot over furnished State and sets the scope" {
              StateStore.resetAllScopes ()

              try
                  (StateStore.forScope "builder-scope").Set("members", nn "scoped")

                  let furnished =
                      { BindingResolver.empty with
                          State = Map.ofList [ "members", nn "host"; "other", nn "host-only" ] }

                  let ctx =
                      Render.RenderContext.create furnished ignore tree
                      |> Render.RenderContext.inScope "builder-scope"

                  Expect.equal ctx.Policy.Scope (Some "builder-scope") "the scope is set on the policy"
                  Expect.equal (ctx.Sources.State["members"]) (nn "scoped") "a scoped value wins"
                  Expect.equal (ctx.Sources.State["other"]) (nn "host-only") "a furnished value survives"
                  Expect.equal ctx.Policy.Egress RenderPolicy.defaults.Egress "the rest of the policy is untouched"
              finally
                  StateStore.resetAllScopes ()
          }

          test "the literal guard sees a full literal when one is there" {
              let sites =
                  literalSites
                      [ "a.fs", "{ Sources = s\n  ExpandingFragments" + " = Set.empty\n  Scope = None }"
                        "b.fs", "{ ctx with ExpandingFragments = Set.add k ctx.ExpandingFragments }" ]

              Expect.equal sites [ "a.fs", 1 ] "a literal is found and a `with` copy is not"
          }

          test "no full RenderContext literal remains outside RenderContext.create" {
              let sites = literalSites (sourceFiles ())

              Expect.equal
                  sites
                  [ "src/Fuaran.UI.Renderer/Render.fs", 1 ]
                  "build the context with `RenderContext.create` plus `with`; the one literal is `create` itself"
          } ]
