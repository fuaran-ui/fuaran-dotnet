module Fuaran.UI.Validator.Manifest

// ============================================================================
//  Validator manifest (§4d / §4k Q3.1).
//
//  Module authors hand-write a tiny JSON file declaring the typed contract
//  the validator gates against: registered query names, the Msg DU's case
//  names, and (optionally) the row type each query returns.
//
//  The format is intentionally minimal — the validator does not infer; the
//  manifest IS the contract. Module authors keep it in sync with their
//  schema-registered queries and their `Msg` type. Future tooling could
//  generate it from the F# sources, but for v1 it is hand-written so the
//  contract surface stays explicit.
//
//  File-discovery convention (v1): the validator looks for
//  `<ProjectDir>/fuaran-validator.manifest.json` next to the .fsproj. A future
//  variant could let the .fsproj declare an explicit `<FuaranManifest>` item;
//  v1 keeps it convention-only.
// ============================================================================

open System.IO
open System.Text.Json
open System.Text.Json.Serialization

/// The wire shape of the manifest. Fields are stable-on-disk; renames are
/// breaking and demand a `version` bump in `VALIDATOR-MANIFEST.md`.
/// JsonSerializer can set absent fields to null even though defaults are
/// non-null, so every field is typed as `T | null` and the `parse` helper
/// substitutes empty defaults.
type ManifestDto() =
    [<JsonPropertyName("queries")>]
    member val Queries: string array | null = [||] with get, set

    [<JsonPropertyName("msgCases")>]
    member val MsgCases: string array | null = [||] with get, set

    /// Optional: per-query row-type name (used by `RowTypeCheck`). The
    /// stored value is the F# type's short name (`SaleRow`) — matched
    /// textually against the lambda parameter's type annotation in the
    /// source. Absence of an entry downgrades the row-type check from
    /// Error to Warning.
    [<JsonPropertyName("queryRowTypes")>]
    member val QueryRowTypes: System.Collections.Generic.Dictionary<string, string> | null =
        System.Collections.Generic.Dictionary() with get, set

    /// Project Custom-node ratio threshold.
    /// Default `0.05` — when the ratio of `NodeKind.Custom` construction
    /// sites to total Fuaran.X smart-ctor sites exceeds the threshold,
    /// `CustomHealthCheck` surfaces FUARAN054 (Advisory Warning) so the
    /// operator sees the creep. Consumers with legitimately high Custom
    /// usage (UI shells wrapping many third-party React components) can
    /// raise the threshold; the manifest declaration is the audit trail.
    /// Nullable: absence means "use the default".
    [<JsonPropertyName("customNodeRatio")>]
    member val CustomNodeRatio: System.Nullable<float> = System.Nullable() with get, set

/// In-memory view of the manifest after load. Same data, F#-native shapes.
type Manifest =
    {
        Queries: Set<string>
        MsgCases: Set<string>
        QueryRowTypes: Map<string, string>
        /// See `ManifestDto.CustomNodeRatio`. `None` means
        /// "use the validator's default threshold of 0.05".
        CustomNodeRatio: float option
    }

let empty: Manifest =
    { Queries = Set.empty
      MsgCases = Set.empty
      QueryRowTypes = Map.empty
      CustomNodeRatio = None }

let private jsonOptions () =
    let options = JsonSerializerOptions()
    options.PropertyNameCaseInsensitive <- true
    options.AllowTrailingCommas <- true
    options.ReadCommentHandling <- JsonCommentHandling.Skip
    options

/// Parse a manifest JSON string. Throws on malformed input — callers are
/// expected to surface the exception as a validator finding.
let parse (json: string) : Manifest =
    match JsonSerializer.Deserialize<ManifestDto>(json, jsonOptions ()) with
    | null -> empty
    | dto ->
        let queries =
            match dto.Queries with
            | null -> Set.empty
            | xs -> Set.ofArray xs

        let msgCases =
            match dto.MsgCases with
            | null -> Set.empty
            | xs -> Set.ofArray xs

        let queryRowTypes =
            match dto.QueryRowTypes with
            | null -> Map.empty
            | dict -> dict |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

        let customNodeRatio =
            if dto.CustomNodeRatio.HasValue then
                Some dto.CustomNodeRatio.Value
            else
                None

        { Queries = queries
          MsgCases = msgCases
          QueryRowTypes = queryRowTypes
          CustomNodeRatio = customNodeRatio }

/// Convention-based discovery: returns the manifest sibling-of-.fsproj path
/// if it exists, `None` otherwise. The validator emits a Warning when no
/// manifest is present — the structural checks (NodeId uniqueness) still
/// run; the schema-coupled checks (Binding.Query, Action.Dispatch, RowType)
/// produce no errors without a manifest because there is no contract to
/// gate against.
let discover (projectDir: string) : string option =
    let candidate = Path.Combine(projectDir, "fuaran-validator.manifest.json")
    if File.Exists candidate then Some candidate else None

/// Load a manifest from disk. Returns the empty manifest if `path` is missing
/// — caller decides whether to warn.
let load (path: string) : Manifest =
    if File.Exists path then
        File.ReadAllText path |> parse
    else
        empty
