/// Phase 2880 — the declared-member table the reference decoder keeps undeclared members
/// against (WIRE_FORMAT §15.8), rendered from `idl.json` exactly as `Generated.fs` is.
///
/// The hand decoder logs the members it reads, but the kinds it delegates to the generated
/// collecting decoders read through the generated layer, which logs nothing. This table is
/// what says, for those positions, which members the shape declares: for each object shape
/// (the node envelope, a kind, a record, a union case, an op) its wire members and the shape
/// of each value. A `hostOnly` row is declared too: the encoder never writes it, but a
/// decoder reads its `"<closure>"` sentinel (or its name) and drops it, a declared
/// normalisation rather than an undeclared member.
module Fuaran.UI.WireShapes

open System.Text
open System.Text.Json

let private header =
    [ "// GENERATED from src/Fuaran.UI.Idl/idl.json by src/Fuaran.UI.Idl/WireShapes.fs. Do not edit by hand:"
      "// FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Idl.Tests rewrites it, and the same suite fails"
      "// when it drifts. The declared wire members of every object shape (WIRE_FORMAT §15.8, Phase 2880)."
      "module Fuaran.UI.Ops.WireShapes"
      ""
      "/// What a declared member's value is, as far as finding the objects inside it goes."
      "[<RequireQualifiedAccess>]"
      "type Shape ="
      "    /// A scalar, an enum token, a closure sentinel or a payload carried whole: nothing to walk."
      "    | Leaf"
      "    /// A node: its own members are its own."
      "    | Node"
      "    /// A node kind, chosen by `$type` from `kinds`."
      "    | Kind"
      "    /// A tree op, chosen by `$type` from `ops`."
      "    | Op"
      "    | Record of string"
      "    /// A union, its case chosen by `$type`."
      "    | Union of string"
      "    | List of Shape"
      "    /// A map type: its keys are entries, never members."
      "    | Map of Shape"
      "" ]

/// A string member of an IDL entry; `idl.json` always carries it.
let private str (e: JsonElement) (name: string) : string =
    match e.GetProperty(name).GetString() with
    | null -> failwithf "idl.json: '%s' is not a string" name
    | s -> s

let rec private shape (t: JsonElement) : string =
    match str t "$type" with
    | "node" -> "Shape.Node"
    | "kind" -> "Shape.Kind"
    | "op" -> "Shape.Op"
    | "record" -> sprintf "Shape.Record %s" (JsonSerializer.Serialize(str t "name"))
    | "union" -> sprintf "Shape.Union %s" (JsonSerializer.Serialize(str t "name"))
    | "list" -> sprintf "Shape.List(%s)" (shape (t.GetProperty "of"))
    | "map" -> sprintf "Shape.Map(%s)" (shape (t.GetProperty "values"))
    | _ -> "Shape.Leaf"

let private members (extra: (string * string) list) (fields: JsonElement) : string =
    let declared =
        [ for f in fields.EnumerateArray() -> str f "name", shape (f.GetProperty "type") ]

    (extra @ declared)
    |> List.sortBy fst
    |> List.map (fun (n, s) -> sprintf "%s, %s" (JsonSerializer.Serialize n) s)
    |> String.concat "; "
    |> sprintf "[ %s ]"

let private fieldsOf (o: JsonElement) =
    match o.TryGetProperty "fields" with
    | true, f -> f
    | _ -> JsonDocument.Parse("[]").RootElement

/// The F# source of `src/Fuaran.UI.Ops/WireShapes.fs` for the given `idl.json` text.
let render (idlJson: string) : string =
    use doc = JsonDocument.Parse idlJson
    let root = doc.RootElement
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore
    header |> List.iter line
    let tag = [ "$type", "Shape.Leaf" ]

    line "/// The node envelope's wire members."

    line (
        sprintf
            "let node: (string * Shape) list = %s"
            (members [ "id", "Shape.Leaf"; "kind", "Shape.Kind" ] (root.GetProperty "nodeFields"))
    )

    line ""

    let table (name: string) (doc': string) (rows: (string * string) list) =
        line doc'
        line (sprintf "let %s: Map<string, (string * Shape) list> =" name)
        line "    Map.ofList"
        line "        ["

        for (k, v) in rows |> List.sortBy fst do
            line (sprintf "            %s, %s" (JsonSerializer.Serialize k) v)

        line "        ]"
        line ""

    table
        "kinds"
        "/// Each node kind's wire members, by `$type`."
        [ for k in root.GetProperty("kinds").EnumerateArray() -> str k "tag", members tag (fieldsOf k) ]

    table
        "records"
        "/// Each record's wire members, by name."
        [ for r in root.GetProperty("records").EnumerateArray() -> str r "name", members [] (fieldsOf r) ]

    table
        "ops"
        "/// Each tree op's wire members, by `$type`."
        [ for o in root.GetProperty("ops").EnumerateArray() -> str o "tag", members tag (fieldsOf o) ]

    line "/// Each union's cases, by `$type`, and each case's wire members."
    line "let unions: Map<string, Map<string, (string * Shape) list>> ="
    line "    Map.ofList"
    line "        ["

    for u in
        root.GetProperty("unions").EnumerateArray()
        |> Seq.sortBy (fun u -> str u "name") do
        line (sprintf "            %s," (JsonSerializer.Serialize(str u "name")))
        line "            Map.ofList"
        line "                ["

        for c in u.GetProperty("cases").EnumerateArray() |> Seq.sortBy (fun c -> str c "tag") do
            line (
                sprintf "                    %s, %s" (JsonSerializer.Serialize(str c "tag")) (members tag (fieldsOf c))
            )

        line "                ]"

    line "        ]"
    sb.ToString()
