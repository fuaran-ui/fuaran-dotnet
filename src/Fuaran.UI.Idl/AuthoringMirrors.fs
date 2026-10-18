module Fuaran.UI.AuthoringMirrors

// ---------------------------------------------------------------------------
// Phase 2241 — the generator for the in-repo authoring mirrors.
//
// Two emissions, both pure functions of the vocabulary (`idl.json`, parsed)
// and the declared dialect (`AuthoringDialect`):
//
//   * `analyzerVocabulary` — the VB XML-literal analyzer's `Kinds`,
//     `Structural` and `Attributes` tables, which a netstandard2.0 analyzer
//     cannot read from the vocabulary at run time and so carries as source;
//   * `csharpEnumMap`      — the C# veneer's `ToFs` translation seam, one arm
//     per IDL case.
//
// Both REFUSE rather than emit when a declaration is no longer true or a new
// vocabulary member has no declared spelling: the generator is the pin now,
// and an error list names every entry to fix at once.
// ---------------------------------------------------------------------------

open System
open System.Text
open Fuaran.Core.Idl
open Fuaran.UI.AuthoringDialect

/// camelCase wire field name to the kebab-case attribute spelling the VB
/// dialect uses throughout.
let kebab (name: string) : string =
    let sb = StringBuilder()

    name
    |> String.iteri (fun i c ->
        if Char.IsUpper c && i > 0 then
            sb.Append '-' |> ignore

        sb.Append(Char.ToLowerInvariant c) |> ignore)

    sb.ToString()

/// Whether a wire field of this type can be authored as an XML ATTRIBUTE.
/// Scalars and enums can; a `TextSource`, or a `Binding` over a scalar, can (the
/// translator lifts the string). Everything else takes a child-element shape or
/// is unauthorable, so its absence from an attribute row is structural.
let isAttributeEligible (t: IdlType) : bool =
    match t with
    | TStr
    | TInt
    | TBool
    | TFloat
    | TEnum _ -> true
    | TUnion("TextSource", _) -> true
    | TUnion("Binding", [ (TStr | TInt | TBool | TFloat) ]) -> true
    | _ -> false

/// A closure is unauthorable in XML in any shape.
let private isClosure (t: IdlType) =
    match t with
    | TClosure
    | TFn _ -> true
    | _ -> false

/// The generic child list — authored as the element's own nested content.
let private isPlainNodeChildren (name: string) (t: IdlType) =
    name = "children"
    && (match t with
        | TList TNode -> true
        | _ -> false)

/// One attribute a wire kind's row carries BY DERIVATION — an attribute-eligible
/// field spelled in kebab-case and not declared unspelled. The safety pin in the
/// analyzer suite reads these: each must be something the C# options record for
/// the kind can carry, because that record is the only thing the VB translator
/// can set.
type DerivedAttribute =
    { Element: string
      Field: string
      Attribute: string }

let private keySet (entries: (string * string) list) = entries |> List.map fst |> Set.ofList

let private rowedKinds (idl: Idl) =
    let exempt = keySet noAttributeRow
    idl.Kinds |> List.filter (fun k -> not (exempt.Contains k.Tag))

/// Every derived attribute, in kind then field order.
let derivedAttributes (idl: Idl) : DerivedAttribute list =
    let unspelled = keySet unspelledFields

    [ for k in rowedKinds idl do
          for f in k.Fields do
              let attr = kebab f.Name

              if
                  isAttributeEligible f.Type
                  && not (unspelled.Contains(sprintf "%s.%s" k.Tag attr))
              then
                  yield
                      { Element = k.Tag
                        Field = f.Name
                        Attribute = attr } ]

let private splitKey (key: string) =
    match key.IndexOf '.' with
    | i when i > 0 -> Some(key.Substring(0, i), key.Substring(i + 1))
    | _ -> None

/// The analyzer's attribute rows: element name to its admitted attributes
/// (before the universal traits are unioned in), or the refusals.
let attributeRows (idl: Idl) : Result<Map<string, string list>, string list> =
    let errors = ResizeArray<string>()
    let kindTags = idl.Kinds |> List.map _.Tag |> Set.ofList
    let rowed = rowedKinds idl
    let rowedTags = rowed |> List.map _.Tag |> Set.ofList

    let fieldsOf tag =
        rowed |> List.find (fun k -> k.Tag = tag) |> _.Fields

    let eligibleAttrs tag =
        fieldsOf tag
        |> List.filter (fun f -> isAttributeEligible f.Type)
        |> List.map (fun f -> kebab f.Name)
        |> Set.ofList

    // The non-kind elements must not shadow a kind, nor each other.
    let dialectNames = (conveniences @ structuralChildren) |> List.map _.Name

    for n in dialectNames do
        if kindTags.Contains n then
            errors.Add(sprintf "dialect element <%s> is now a wire kind — drop its declaration" n)

    for n in
        dialectNames
        |> List.countBy id
        |> List.filter (fun (_, c) -> c > 1)
        |> List.map fst do
        errors.Add(sprintf "dialect element <%s> is declared twice" n)

    for (k, _) in noAttributeRow do
        if not (kindTags.Contains k) then
            errors.Add(sprintf "noAttributeRow names %s, which is not a wire kind" k)

    for (key, _) in unspelledFields do
        match splitKey key with
        | Some(tag, attr) when rowedTags.Contains tag && (eligibleAttrs tag).Contains attr -> ()
        | _ ->
            errors.Add(
                sprintf
                    "unspelledFields declares %s, which is not an attribute-eligible field of a rowed kind — the declaration is stale"
                    key
            )

    for (key, _) in attributeOnly do
        match splitKey key with
        | Some(tag, attr) when rowedTags.Contains tag ->
            if (eligibleAttrs tag).Contains attr then
                errors.Add(
                    sprintf "attributeOnly declares %s, which is now a derived spelling — the declaration is stale" key
                )
        | _ -> errors.Add(sprintf "attributeOnly declares %s, which names no rowed wire kind" key)

    for tag in cellFormatSugar do
        if not (rowedTags.Contains tag) then
            errors.Add(sprintf "cellFormatSugar names %s, which is not a rowed wire kind" tag)

    if errors.Count > 0 then
        Error(List.ofSeq errors)
    else
        let derived = derivedAttributes idl

        let kindRows =
            [ for k in rowed ->
                  let attrs =
                      [ yield "id"
                        yield! derived |> List.filter (fun d -> d.Element = k.Tag) |> List.map _.Attribute
                        yield!
                            attributeOnly
                            |> List.choose (fun (key, _) ->
                                match splitKey key with
                                | Some(tag, attr) when tag = k.Tag -> Some attr
                                | _ -> None)
                        if List.contains k.Tag cellFormatSugar then
                            yield! cellFormatFamily ]

                  k.Tag, attrs ]

        let dialectRows =
            (conveniences @ structuralChildren)
            |> List.filter _.HasRow
            |> List.map (fun e -> e.Name, e.Attributes)

        Ok(Map.ofList (kindRows @ dialectRows))

/// The structural pin, at generation: every child element's anchor resolves,
/// every structured kind field is anchored or declared, and no declaration has
/// outlived its subject.
let private structuralRefusals (idl: Idl) : string list =
    let slots =
        [ for k in idl.Kinds do
              for f in k.Fields -> sprintf "%s.%s" k.Tag f.Name
          for r in idl.Records do
              for f in r.Fields -> sprintf "%s.%s" r.Name f.Name ]
        |> Set.ofList

    let anchors = structuralChildren |> List.choose _.Anchor |> Set.ofList
    let declared = keySet notChildAuthorable

    let dangling =
        structuralChildren
        |> List.choose (fun e ->
            match e.Anchor with
            | Some a when not (slots.Contains a) ->
                Some(sprintf "structural element <%s> names %s, which the vocabulary no longer has" e.Name a)
            | _ -> None)

    let structured =
        [ for k in idl.Kinds do
              for f in k.Fields do
                  if
                      not (
                          isAttributeEligible f.Type
                          || isClosure f.Type
                          || isPlainNodeChildren f.Name f.Type
                      )
                  then
                      yield sprintf "%s.%s" k.Tag f.Name ]
        |> Set.ofList

    let unspelled =
        structured
        |> Set.filter (fun key -> not (anchors.Contains key || declared.Contains key))
        |> Set.toList
        |> List.map (fun key ->
            sprintf
                "structured wire field %s has no child element and no declared divergence — add a structural element anchored on it, or declare it in AuthoringDialect.notChildAuthorable"
                key)

    let orphans =
        declared
        |> Set.filter (fun key -> not (structured.Contains key) || anchors.Contains key)
        |> Set.toList
        |> List.map (fun key ->
            sprintf
                "notChildAuthorable declares %s, which is now spelled or no longer a structured wire field — the declaration is stale"
                key)

    dangling @ unspelled @ orphans

/// The universal traits must each spell an attribute-eligible envelope field:
/// one of the node envelope's own, or one of a record the envelope reaches.
let private universalRefusals (idl: Idl) : string list =
    let envelopeRecords =
        idl.NodeFields
        |> List.choose (fun f ->
            match f.Type with
            | TRecord name -> Some name
            | _ -> None)
        |> Set.ofList

    let spellable =
        [ for f in idl.NodeFields do
              if isAttributeEligible f.Type then
                  yield kebab f.Name
          for r in idl.Records do
              if envelopeRecords.Contains r.Name then
                  for f in r.Fields do
                      if isAttributeEligible f.Type then
                          yield kebab f.Name ]
        |> Set.ofList

    universal
    |> List.filter (fun u -> not (spellable.Contains u))
    |> List.map (sprintf "universal attribute `%s` spells no attribute-eligible envelope field")

let private quoteList (items: string seq) =
    items |> Seq.map (sprintf "\"%s\"") |> String.concat ", "

let private generatedBanner (what: string) =
    [ "// <auto-generated>"
      sprintf "//     %s" what
      "//     Generated from src/Fuaran.UI.Idl/idl.json and src/Fuaran.UI.Idl/AuthoringDialect.fs by"
      "//     src/Fuaran.UI.Idl/AuthoringMirrors.fs. Do not edit: regenerate with"
      "//     FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests"
      "// </auto-generated>" ]

/// The VB analyzer's vocabulary, as the C# source of the generated half of the
/// `Vocabulary` partial class.
let analyzerVocabulary (idl: Idl) : Result<string, string list> =
    let refusals = structuralRefusals idl @ universalRefusals idl

    match attributeRows idl, refusals with
    | Error es, _ -> Error(es @ refusals)
    | Ok _, (_ :: _ as rs) -> Error rs
    | Ok rows, [] ->
        let kinds =
            (idl.Kinds |> List.map _.Tag) @ (conveniences |> List.map _.Name)
            |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

        let structural =
            structuralChildren
            |> List.map _.Name
            |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

        let ordinal (xs: string seq) =
            xs |> Seq.distinct |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b))

        let lines =
            [ yield! generatedBanner "The VB XML-literal vocabulary the analyzer checks against."
              yield "using System;"
              yield "using System.Collections.Immutable;"
              yield ""
              yield "namespace Fuaran.UI.Analyzers.VisualBasic;"
              yield ""
              yield "internal static partial class Vocabulary"
              yield "{"
              yield
                  "    /// <summary>The node-kind element names: every wire kind, plus the element conveniences that lower onto one.</summary>"
              yield "    public static readonly ImmutableHashSet<string> Kinds = ImmutableHashSet.Create("
              yield "        StringComparer.Ordinal,"
              yield sprintf "        %s);" (quoteList kinds)
              yield ""
              yield "    /// <summary>Structural child elements — children of a kind, not kinds themselves.</summary>"
              yield "    public static readonly ImmutableHashSet<string> Structural = ImmutableHashSet.Create("
              yield "        StringComparer.Ordinal,"
              yield sprintf "        %s);" (quoteList structural)
              yield ""
              yield
                  "    /// <summary>Attributes every element admits: node-envelope traits rather than fields of any one kind.</summary>"
              yield "    private static readonly ImmutableHashSet<string> Universal ="
              yield sprintf "        ImmutableHashSet.Create(StringComparer.Ordinal, %s);" (quoteList universal)
              yield ""
              yield "    /// <summary>Per-element allowed attributes. An element absent from this table is not"
              yield "    /// attribute-checked (allow-any).</summary>"
              yield
                  "    public static readonly ImmutableDictionary<string, ImmutableHashSet<string>> Attributes = BuildAttributes();"
              yield ""
              yield "    private static ImmutableDictionary<string, ImmutableHashSet<string>> BuildAttributes()"
              yield "    {"
              yield
                  "        var b = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(StringComparer.Ordinal);"
              yield "        void Add(string el, params string[] attrs) => b[el] = Universal.Union(attrs);"
              yield ""
              // F# orders string keys ordinally, so the rows emit in a stable order.
              for KeyValue(el, attrs) in rows do
                  yield sprintf "        Add(\"%s\", %s);" el (quoteList (ordinal attrs))
              yield "        return b.ToImmutable();"
              yield "    }"
              yield "}" ]

        Ok((lines |> String.concat "\n") + "\n")

/// The C# veneer's `ToFs` seam for every declared enum mirror.
let csharpEnumMap (idl: Idl) : Result<string, string list> =
    let enums = idl.Enums |> List.map (fun e -> e.Name, e) |> Map.ofList

    let errors =
        [ for m in csharpEnums do
              match enums.TryFind m.Idl with
              | None -> yield sprintf "C# enum %s mirrors %s, which is not an IDL enum" m.CSharp m.Idl
              | Some _ -> ()
          for (n, c) in csharpEnums |> List.countBy _.CSharp do
              if c > 1 then
                  yield sprintf "C# enum %s is declared twice" n ]

    if not (List.isEmpty errors) then
        Error errors
    else
        let lines =
            [ yield! generatedBanner "The C# veneer's enum translation seam."
              yield "using System;"
              yield "using FsGen = Fuaran.UI.Generated;"
              yield ""
              yield "namespace Fuaran.UI.CSharp;"
              yield ""
              yield "/// <summary>The single translation seam between the C# enums and the F# DUs: one arm per"
              yield "/// vocabulary case, so a case the C# declaration lacks fails the compile here. A value"
              yield "/// outside the declared cases (a cast integer) throws: there is no fallback case.</summary>"
              yield "internal static class EnumMap"
              yield "{"
              for i, m in List.indexed csharpEnums do
                  let e = enums[m.Idl]

                  if i > 0 then
                      yield ""

                  yield sprintf "    internal static FsGen.%s ToFs(this %s v) =>" m.Idl m.CSharp
                  yield "        v switch"
                  yield "        {"

                  for c in e.Cases do
                      yield sprintf "            %s.%s => FsGen.%s.%s," m.CSharp c m.Idl c

                  // Phase 1930 — no fallback case: a value outside the declared
                  // cases is an authoring error, never a silently chosen case.
                  yield
                      sprintf
                          "            _ => throw new ArgumentOutOfRangeException(nameof(v), v, \"not a declared %s case\"),"
                          m.CSharp

                  yield "        };"
              yield "}" ]

        Ok((lines |> String.concat "\n") + "\n")
