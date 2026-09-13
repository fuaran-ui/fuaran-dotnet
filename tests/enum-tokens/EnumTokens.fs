// ============================================================================
//  Phase 1691 — the enum case-to-wire-token TABLE, and the corpus artefact
//  emitted from it.
//
//  DECLARED ONCE here and linked (not copied) into the two suites that need it,
//  on the `tests/corpus-root/CorpusRoot.fs` precedent: the EMITTER
//  (`Fuaran.UI.JsonDecode.Tests`, which writes `enum-tokens.json` beside every
//  other corpus artefact) and the CHECKER (`Fuaran.UI.Tests`, which holds the
//  typed vocabulary and pins the implementations against it). One renderer, so
//  the artefact the emitter writes and the artefact the checker expects cannot
//  disagree about formatting while disagreeing about nothing else.
//
//  WHY THE TABLE IS PARSED FROM `idl.json` RATHER THAN TAKEN FROM THE TYPED
//  VALUE. The emitter cannot see the typed value: `Fuaran.UI.Idl` is
//  deliberately not packable and is referenced only by the two projects that
//  regenerate or certify the vocabulary, so reaching it from the fixture
//  emitter would drag `Fuaran.Core.Idl` into a project that has no other need
//  of it. `idl.json` is the committed, canonical rendering of that same
//  declaration and is ALREADY read by the emitter (it copies it into the corpus
//  verbatim), so this derives from bytes the emitter has in hand. The checker
//  then closes the loop from the other side: it builds the table from the TYPED
//  `Fuaran.UI.Vocabulary.uiIdl` and asserts it equals the table parsed from the
//  committed bytes, so "the derivation read the right thing" is a red test and
//  not an assumption.
//
//  WHAT `cases` AND `hostCases` MEAN IN `idl.json` — the one detail everything
//  here rests on. `cases` is ALWAYS the WIRE contract. `hostCases` appears only
//  for an enum whose host case names differ from it, and is parallel to
//  `cases`. So an entry with no `hostCases` is the identity mapping, and an
//  entry with one is a declared non-identity mapping. That is the property this
//  file exists to publish: a future non-identity mapping has to be DECLARED
//  (a `hostCases` key appears, the artefact changes, the corpus diff shows it),
//  never discovered by an emitter that guessed the token from a case name and
//  compiled.
// ============================================================================

module Fuaran.Tests.EnumTokens

open System
open System.IO
open System.Text
open System.Text.Json

/// One enum's declared case-to-wire-token mapping.
///
/// `Cases` is `(host case name, wire token)` in DECLARED order — not sorted.
/// Declaration order is what `idl.json` preserves within an entry and what the
/// generated encoder's match arms follow, so sorting here would make a diff of
/// this artefact disagree with a diff of the generated layer for no gain.
type EnumTokenEntry =
    {
        /// The enum's name, as the vocabulary declares it.
        Name: string
        /// Whether the enum declares a NON-IDENTITY mapping. `false` means every
        /// token is its own case name; it is carried explicitly rather than left
        /// for the reader to derive, because a consumer should not have to
        /// compare strings to learn whether it is holding a declared mapping.
        Mapped: bool
        Cases: (string * string) list
    }

/// The artefact's own encoding version — bumped when the SHAPE of
/// `enum-tokens.json` changes, never when the vocabulary does. The same
/// convention `idl.json` states for its `version`.
[<Literal>]
let ArtifactVersion = 1

[<Literal>]
let FileName = "enum-tokens.json"

let private description =
    "The closed bare-string enum vocabularies of the Fuaran UI wire format, as the case-to-wire-token "
    + "mapping each one declares. Generated from idl.json; do not hand-edit. WIRE_FORMAT.md 3.5 states "
    + "the same closed sets from the same source, but states the WIRE side only: this artefact carries "
    + "the PAIRING, which is what an emitter needs and what an emitter that derives a token from a host "
    + "case name gets wrong while still compiling. Entries whose mapping is not the identity come first; "
    + "within each group entries are Ordinal by name, and within an entry cases are in declared order."

/// The artefact's entry order: declared non-identity mappings first, then the
/// identity ones, each group Ordinal by name.
///
/// A FUNCTION rather than a convention, because two derivations produce this
/// table — the emitter's, from `idl.json`, and the checker's, from the typed
/// vocabulary — and an ordering agreed by convention is one they can disagree
/// about while agreeing about every token.
///
/// Mapped-first is not decoration. The identity entries are the boring forty;
/// the mapped ones are where an emitter that guesses is wrong, so they are what
/// a reader opening this file should meet.
let order (entries: EnumTokenEntry list) : EnumTokenEntry list =
    let byName = List.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))

    (entries |> List.filter (fun e -> e.Mapped) |> byName)
    @ (entries |> List.filter (fun e -> not e.Mapped) |> byName)

// ─── reading the table out of the committed idl.json bytes ─────────────────

let private stringsAt (name: string) (e: JsonElement) : string list option =
    match e.TryGetProperty name with
    | false, _ -> None
    | true, arr ->
        arr.EnumerateArray()
        |> Seq.map (fun v ->
            match v.GetString() |> Option.ofObj with
            | Some s -> s
            | None -> failwithf "idl.json: '%s' holds a non-string entry" name)
        |> List.ofSeq
        |> Some

/// The table, parsed from the canonical `idl.json` rendering of the vocabulary.
///
/// Raises rather than skipping on every malformed shape: this feeds a corpus
/// artefact other hosts certify against, so a silently-empty table would
/// publish "this vocabulary has no enums" and every consumer's check would pass
/// having compared nothing.
let ofIdlJson (idlJson: string) : EnumTokenEntry list =
    use doc = JsonDocument.Parse idlJson

    let enums =
        match doc.RootElement.TryGetProperty "enums" with
        | true, e -> e
        | false, _ -> failwith "idl.json has no 'enums' array — the enum-token table cannot be derived from it"

    let entries =
        [ for e in enums.EnumerateArray() do
              let name =
                  match e.TryGetProperty "name" with
                  | true, n ->
                      match n.GetString() |> Option.ofObj with
                      | Some s -> s
                      | None -> failwith "idl.json: an enum entry has a null 'name'"
                  | false, _ -> failwith "idl.json: an enum entry has no 'name'"

              let wires =
                  match stringsAt "cases" e with
                  | Some cs -> cs
                  | None -> failwithf "idl.json: enum '%s' has no 'cases' array" name

              match stringsAt "hostCases" e with
              | None ->
                  { Name = name
                    Mapped = false
                    Cases = wires |> List.map (fun c -> c, c) }
              | Some hosts ->
                  if List.length hosts <> List.length wires then
                      failwithf
                          "idl.json: enum '%s' declares %d hostCases against %d cases — the two arrays are parallel by contract"
                          name
                          (List.length hosts)
                          (List.length wires)

                  { Name = name
                    Mapped = true
                    Cases = List.zip hosts wires } ]

    order entries

// ─── rendering the corpus artefact ─────────────────────────────────────────

/// The artefact's exact bytes, as a string.
///
/// LF on every platform and the writer's 2-space indentation, matching every
/// other emitted corpus artefact: the corpus `.gitattributes` normalises
/// `eol=lf` on commit, so a CR written here is invisible to `git status` and
/// visible only to a consumer that byte-compares the working tree — the failure
/// `CorpusEmitEolTests` exists to catch.
let render (entries: EnumTokenEntry list) : string =
    let opts = JsonWriterOptions(Indented = true, NewLine = "\n")
    use stream = new MemoryStream()

    (use w = new Utf8JsonWriter(stream, opts)

     w.WriteStartObject()
     w.WriteNumber("version", ArtifactVersion)
     w.WriteString("source", "idl.json")
     w.WriteString("description", description)
     w.WriteStartArray("enums")

     for entry in entries do
         w.WriteStartObject()
         w.WriteString("name", entry.Name)
         w.WriteBoolean("mapped", entry.Mapped)
         w.WriteStartArray("cases")

         for (case, token) in entry.Cases do
             w.WriteStartObject()
             w.WriteString("case", case)
             w.WriteString("token", token)
             w.WriteEndObject()

         w.WriteEndArray()
         w.WriteEndObject()

     w.WriteEndArray()
     w.WriteEndObject()
     w.Flush())

    Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the artefact into a corpus directory. The one writer, so the emitter
/// and any future regeneration path cannot format it two ways.
let write (corpusDir: string) (entries: EnumTokenEntry list) : unit =
    File.WriteAllText(Path.Combine(corpusDir, FileName), render entries)
