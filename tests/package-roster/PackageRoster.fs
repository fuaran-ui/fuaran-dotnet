/// Phase 1865 — every published package built from F# ships its XML documentation file beside
/// each assembly it carries under `lib/`, so the `///` comments in its sources reach a consumer's
/// hover. Before 1865 no project set `GenerateDocumentationFile`, and every package shipped the DLL
/// alone.
///
/// THE SUBJECT IS THE ARCHIVE. The check packs the solution with a real `dotnet pack` and reads the
/// `.nupkg` files it produced, never the project text: a consumer restores the archive, and a
/// project can set the property and still ship no file (an explicit pack path, a later override,
/// `IncludeBuildOutput`). The property itself is set once, in `build/PackageDocumentation.targets`.
///
/// THE RULES, over the published set (`packableProjects` in `Build.fs`, the list `Pack` packs):
///
///   documented  in every package packed from an `.fsproj`, each `lib/<tfm>/<Name>.dll` has
///               `lib/<tfm>/<Name>.xml` beside it.
///
///   rostered    the pack produced a package for every published project, and nothing it produced
///               is outside the published set. Without the first half a project that stopped
///               packing would pass `documented` by having no archive to read; the second is the
///               shape of Phase 224, two packable projects that no release emitted.
///
/// NOT COVERED, and printed on every run so it does not become a silent exemption: packages packed
/// from a `.csproj` or `.vbproj`. C# and Visual Basic report a public member without a doc comment
/// as a warning, which `TreatWarningsAsErrors` makes an error, and those packages are not yet
/// documented to that standard. A package with no `lib/` assembly (an analyzer, a dotnet tool) has
/// nothing for a consumer's compiler to read, and is listed as such.
///
/// GO-RED. `proveRules` runs the rules over synthetic packages before the real check runs, on every
/// invocation, so a rule that stopped firing fails the gate rather than passing an archive it can no
/// longer see.
module PackageRoster

open System
open System.IO
open System.IO.Compression
open System.Xml.Linq

/// One produced package: its id (read from its own nuspec) and the archive's entry paths.
type Packed = { Id: string; Entries: string list }

/// One published project: its package id and whether it is compiled from F#.
type Published = { Id: string; FSharp: bool }

let private isLibAssembly (entry: string) =
    entry.StartsWith("lib/", StringComparison.Ordinal)
    && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)

/// The `lib/` assemblies of one package that have no `.xml` beside them.
let undocumented (entries: string list) =
    let present = set entries

    entries
    |> List.filter isLibAssembly
    |> List.filter (fun dll -> not (present.Contains(dll.Substring(0, dll.Length - ".dll".Length) + ".xml")))

let findings (published: Published list) (packed: Packed list) =
    let byId = packed |> List.map (fun p -> p.Id, p) |> Map.ofList
    let publishedIds = published |> List.map _.Id |> set

    [ for p in published do
          match byId.TryFind p.Id with
          | None -> yield $"%s{p.Id}: published, but the pack produced no package for it, so nothing read its archive"
          | Some archive when p.FSharp ->
              for dll in undocumented archive.Entries do
                  yield $"%s{p.Id}: %s{dll} ships without its XML documentation file"
          | Some _ -> ()
      for p in packed do
          if not (publishedIds.Contains p.Id) then
              yield $"%s{p.Id}: packed, but not in the published set (packableProjects in Build.fs)" ]

let proveRules () =
    let check label published packed expectRed =
        let got = findings published packed

        if got.IsEmpty = expectRed then
            failwithf
                "PackageRoster's go-red proof failed (%s): expected %s, got %A — the rules no longer see the shape they exist for"
                label
                (if expectRed then "a finding" else "none")
                got

    let fs id = { Id = id; FSharp = true }
    let dll id = $"lib/net10.0/%s{id}.dll"
    let xml id = $"lib/net10.0/%s{id}.xml"

    let documented =
        { Id = "A"
          Entries = [ dll "A"; xml "A" ] }
    // The pre-1865 archive: the DLL alone.
    check "DLL without its XML" [ fs "A" ] [ { Id = "A"; Entries = [ dll "A" ] } ] true
    check "documented" [ fs "A" ] [ documented ] false
    // The XML of ANOTHER assembly, or one in another folder, is not this assembly's.
    check
        "wrong name"
        [ fs "A" ]
        [ { Id = "A"
            Entries = [ dll "A"; xml "B" ] } ]
        true

    check
        "wrong folder"
        [ fs "A" ]
        [ { Id = "A"
            Entries = [ dll "A"; "lib/net9.0/A.xml" ] } ]
        true
    // A second assembly in the same package is held to the same rule.
    check
        "second assembly"
        [ fs "A" ]
        [ { documented with
              Entries = documented.Entries @ [ dll "B" ] } ]
        true
    // A published project with no archive cannot pass by absence, and an unpublished archive is named.
    check "no archive" [ fs "A"; fs "B" ] [ documented ] true

    check
        "unpublished archive"
        [ fs "A" ]
        [ documented
          { Id = "B"
            Entries = [ dll "B"; xml "B" ] } ]
        true
    // Out of scope: a non-F# package, and a package with no lib/ assembly.
    check "not F#" [ { Id = "A"; FSharp = false } ] [ { Id = "A"; Entries = [ dll "A" ] } ] false

    check
        "no lib/"
        [ fs "A" ]
        [ { Id = "A"
            Entries = [ "analyzers/dotnet/cs/A.dll" ] } ]
        false

let read (nupkg: string) =
    use zip = ZipFile.OpenRead nupkg

    let nuspec =
        zip.Entries
        |> Seq.find (fun e ->
            not (e.FullName.Contains '/')
            && e.FullName.EndsWith(".nuspec", StringComparison.Ordinal))

    use stream = nuspec.Open()

    let id =
        (XDocument.Load stream).Descendants()
        |> Seq.find (fun e -> e.Name.LocalName = "id")
        |> _.Value

    { Id = id
      Entries = zip.Entries |> Seq.map _.FullName |> List.ofSeq }

/// Run the proof, then pack and check this repository. `published` are the project paths the `Pack`
/// target packs; `pack outDir` runs the real `dotnet pack` of the solution into `outDir`.
let run (pack: string -> unit) (published: string list) =
    proveRules ()

    let published =
        published
        |> List.map (fun p ->
            { Id = Path.GetFileNameWithoutExtension(p: string) |> string
              FSharp = p.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) })

    let outDir =
        Path.Combine(Path.GetTempPath(), "fuaran-package-roster-" + Guid.NewGuid().ToString "N")

    try
        pack outDir
        let packed = Directory.GetFiles(outDir, "*.nupkg") |> Array.map read |> List.ofArray

        let noLib =
            packed |> List.filter (fun p -> not (p.Entries |> List.exists isLibAssembly))

        printfn
            "PackageRoster: %d published package(s), %d packed — %d checked for XML documentation"
            published.Length
            packed.Length
            (published |> List.filter _.FSharp).Length

        for p in published |> List.filter (fun p -> not p.FSharp) do
            printfn "  not covered  %s — not F#: C# and VB fail TreatWarningsAsErrors on an undocumented member" p.Id

        for p in noLib do
            printfn "  no lib/      %s — ships no assembly a consumer's compiler reads" p.Id

        match findings published packed with
        | [] -> printfn "PackageRoster: green — every published F# package ships its XML documentation file"
        | problems ->
            for p in problems do
                eprintfn "  %s" p

            failwithf "PackageRoster: %d finding(s) over the packed archives (see above)" problems.Length
    finally
        if Directory.Exists outDir then
            Directory.Delete(outDir, true)
