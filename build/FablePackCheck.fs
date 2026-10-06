/// Phase 2128 — every package this repository publishes that a Fable consumer can reach ships its
/// F# sources under `fable/`, or says in its own project file that it is .NET-only.
///
/// WHY. Under Fable a package that ships `fable/` sources is TRANSPILED, and one that does not is
/// read as a compiled ASSEMBLY. The two do not mix across a type boundary: an assembly whose public
/// types mention a type from a source-shipped package points at that package's ASSEMBLY, which Fable
/// has replaced with sources, so every consumer call site that crosses the boundary is FS0074 ("the
/// type is defined in an assembly that is not referenced"). `Fuaran.UI.Telemetry.Default` was
/// exactly that shape until Phase 2128: its `NoOpSink.create` returns the Abstractions package's
/// `IFuaranTelemetrySink`, and it packed no sources. A .NET build of the same consumer is green, and
/// so is every Fable compile inside this repository, because Fable compiles a ProjectReference from
/// source whatever the package ships — which is why nothing caught it.
///
/// THE RULE, over the published set (`packableProjects` in `Build.fs`, the list the `Pack` target
/// packs — the one declaration of what this repository publishes):
///
///   reachable   a published package whose `ProjectReference` closure holds a project that ships
///               `fable/` sources. Its public surface can carry a source-shipped type, so a Fable
///               consumer that references it meets the boundary above. Derived from the tree; a new
///               package joins the set by referencing a Fable package, with no edit here.
///
///   honest      a reachable package ships its own `.fs` sources under `fable/`, OR declares
///               `<FableDotNetOnly>why</FableDotNetOnly>` in its own fsproj. The property's VALUE is
///               the reason, so a declaration without one fails; the check prints every declaration
///               on every run, so none becomes a list nobody re-reads. The posture is the Fable
///               stage's own `<FablePortabilityExemption>` (Phase 1606).
///
///   closed      a package that ships `fable/` sources references only projects that ship them too:
///               a source-shipped package over a DLL-only dependency is the same boundary one level
///               down. And a package cannot both ship sources and declare itself .NET-only.
///
/// The check reads project files only. The packed ARTEFACT for the case that was broken is proved by
/// `tests/fable-pack-consumer/` in the Fable stage, which packs `Telemetry.Default` and its closure
/// and Fable-compiles a consumer against the packages.
///
/// GO-RED. `proveRules` runs the rules over a synthetic set reproducing the pre-2128 tree before the
/// real check runs, on every invocation — milliseconds — so a rule that stopped firing fails the gate
/// rather than passing a tree it can no longer see.
module FablePackCheck

open System
open System.IO
open System.Xml.Linq

/// What the check reads from one project file.
type ProjectFacts =
    {
        Name: string
        Path: string
        /// Packs `*.fs` under `PackagePath="fable\"` — the Fable stage's own definition of shipping.
        ShipsFable: bool
        /// `Some reason` (possibly blank) when the project declares `<FableDotNetOnly>`.
        DotNetOnly: string option
        /// Full paths of the projects it `ProjectReference`s.
        References: string list
    }

let private localName (e: XElement) = e.Name.LocalName

/// Read one project file. A `ProjectReference` resolves against the project's own directory.
let read (fsproj: string) : ProjectFacts =
    let fullPath = Path.GetFullPath fsproj
    let dir = nonNull (Path.GetDirectoryName fullPath)
    let doc = XDocument.Load fullPath
    let elements = doc.Descendants() |> Seq.toList

    let attr (name: string) (e: XElement) =
        e.Attribute(XName.Get name) |> Option.ofObj |> Option.map _.Value

    let shipsFable =
        elements
        |> List.exists (fun e ->
            localName e = "Content"
            && (match attr "PackagePath" e with
                | Some p -> p.Trim().TrimEnd('\\', '/') = "fable"
                | None -> false)
            && (match attr "Include" e with
                | Some inc -> inc.Split ';' |> Array.exists (fun p -> p.Trim().EndsWith "*.fs")
                | None -> false))

    let dotNetOnly =
        elements
        |> List.tryFind (fun e -> localName e = "FableDotNetOnly")
        |> Option.map (fun e -> e.Value.Trim())

    let references =
        elements
        |> List.filter (fun e -> localName e = "ProjectReference")
        |> List.choose (attr "Include")
        |> List.map (fun inc -> Path.GetFullPath(Path.Combine(dir, inc.Replace('\\', Path.DirectorySeparatorChar))))

    { Name = nonNull (Path.GetFileNameWithoutExtension fullPath)
      Path = fullPath
      ShipsFable = shipsFable
      DotNetOnly = dotNetOnly
      References = references }

/// Every project reachable from `root` over `ProjectReference`, `root` excluded.
let private closure (lookup: string -> ProjectFacts option) (root: ProjectFacts) : ProjectFacts list =
    let rec walk (seen: Set<string>) (acc: ProjectFacts list) (pending: string list) =
        match pending with
        | [] -> List.rev acc
        | path :: rest when seen.Contains path -> walk seen acc rest
        | path :: rest ->
            match lookup path with
            | Some facts -> walk (seen.Add path) (facts :: acc) (facts.References @ rest)
            | None -> walk (seen.Add path) acc rest

    walk (Set.singleton root.Path) [] root.References

/// The rules, over the published set. `lookup` resolves ANY project path (a published package may
/// reference an unpublished project). Returns one line per violation, empty when the set is honest.
let findings (lookup: string -> ProjectFacts option) (published: ProjectFacts list) : string list =
    [ for p in published do
          let reached = closure lookup p
          let shippingDeps = reached |> List.filter _.ShipsFable

          match p.DotNetOnly with
          | Some reason when String.IsNullOrWhiteSpace reason ->
              yield $"%s{p.Name} declares <FableDotNetOnly> with no reason — the value IS the reason; write one"
          | _ -> ()

          if p.ShipsFable then
              if p.DotNetOnly.IsSome then
                  yield
                      $"%s{p.Name} ships fable/ sources AND declares <FableDotNetOnly> — it is one or the other; drop one"

              for dep in reached do
                  if not dep.ShipsFable then
                      yield
                          $"%s{p.Name} ships fable/ sources but references %s{dep.Name}, which ships none — a Fable consumer reads %s{dep.Name} as an assembly and every type crossing into it is FS0074; pack its sources under fable\\"
          elif not shippingDeps.IsEmpty && p.DotNetOnly.IsNone then
              let via = shippingDeps |> List.map _.Name |> String.concat ", "

              yield
                  $"%s{p.Name} is Fable-reachable (it references %s{via}, which ship fable/ sources) but ships no fable/ sources of its own, so a Fable consumer of it meets FS0074 at every type it hands across — pack its .fs under PackagePath=\"fable\\\" (the Telemetry.Abstractions convention) or declare <FableDotNetOnly>why</FableDotNetOnly> in its fsproj" ]

/// The go-red proof: the rules over synthetic sets, each with the answer it must give. Throws when
/// any rule stops firing (or starts firing on an honest set).
let proveRules () =
    let mk name ships dotNetOnly refs =
        { Name = name
          Path = $"/proof/%s{name}.fsproj"
          ShipsFable = ships
          DotNetOnly = dotNetOnly
          References = refs |> List.map (fun r -> $"/proof/%s{r}.fsproj") }

    let abstractions = mk "Abstractions" true None []

    let check label (set: ProjectFacts list) (published: ProjectFacts list) (expectRed: bool) =
        let table = set |> List.map (fun p -> p.Path, p) |> Map.ofList
        let got = findings table.TryFind published

        if got.IsEmpty = expectRed then
            failwithf
                "FablePackCheck's go-red proof failed (%s): expected %s, got %A — the rules no longer see the shape they exist for"
                label
                (if expectRed then "a finding" else "none")
                got

    // The pre-2128 tree: a DLL-only package over a source-shipped one, undeclared.
    let telemetryDefault = mk "Default" false None [ "Abstractions" ]
    check "pre-2128 Telemetry.Default" [ abstractions; telemetryDefault ] [ telemetryDefault ] true
    // Reached TRANSITIVELY through an unpublished, DLL-only middle project.
    let middle = mk "Middle" false None [ "Abstractions" ]
    let top = mk "Top" false None [ "Middle" ]
    check "transitive reach" [ abstractions; middle; top ] [ top ] true
    // The fix, and the declared alternative, are both honest.
    let fixedDefault = mk "Default" true None [ "Abstractions" ]
    check "source-shipped" [ abstractions; fixedDefault ] [ fixedDefault ] false
    let declared = mk "Server" false (Some "ASP.NET Core host") [ "Abstractions" ]
    check "declared .NET-only" [ abstractions; declared ] [ declared ] false
    // A declaration with no reason, a contradiction, and a source package over a DLL-only one.
    let blank = mk "Blank" false (Some " ") [ "Abstractions" ]
    check "blank reason" [ abstractions; blank ] [ blank ] true
    let both = mk "Both" true (Some "x") [ "Abstractions" ]
    check "ships AND declared" [ abstractions; both ] [ both ] true
    let dllDep = mk "DllDep" false None []
    let overDll = mk "OverDll" true None [ "DllDep" ]
    check "source over DLL" [ dllDep; overDll ] [ overDll ] true
    // A package reaching nothing that ships sources is out of scope.
    let plain = mk "Plain" false None [ "DllDep" ]
    check "unreachable" [ dllDep; plain ] [ plain ] false

/// Run the proof, then the rules over this repository. `published` are the fsproj paths the `Pack`
/// target packs; `srcRoot` is where every project a reference can resolve to lives.
let run (srcRoot: string) (published: string list) =
    proveRules ()

    let all =
        Directory.GetFiles(srcRoot, "*.fsproj", SearchOption.AllDirectories)
        |> Array.filter (fun p ->
            let parts = p.Split([| '\\'; '/' |])
            not (parts |> Array.exists (fun s -> s = "bin" || s = "obj")))
        |> Array.map read
        |> Array.map (fun f -> f.Path, f)
        |> Map.ofArray

    let publishedFacts =
        published
        |> List.map (fun p ->
            match all.TryFind(Path.GetFullPath p) with
            | Some f -> f
            | None -> failwithf "FablePackCheck: the published project %s is not under %s" p srcRoot)

    let declared = publishedFacts |> List.filter _.DotNetOnly.IsSome
    let shipping = publishedFacts |> List.filter _.ShipsFable

    printfn
        "FablePackCheck: %d published F# package(s) — %d ship fable/ sources, %d declared .NET-only"
        publishedFacts.Length
        shipping.Length
        declared.Length

    for d in declared do
        printfn "  .NET-only  %s — %s" d.Name (d.DotNetOnly |> Option.defaultValue "")

    match findings all.TryFind publishedFacts with
    | [] -> printfn "FablePackCheck: green — every Fable-reachable published package ships its sources or says why not"
    | problems ->
        for p in problems do
            eprintfn "  %s" p

        failwithf
            "FablePackCheck: %d Fable-reachable package(s) would break a Fable consumer (see above)"
            problems.Length
