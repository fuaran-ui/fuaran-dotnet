/// The row-source codec this tier owns (Phase 1960): the grid / chart / table row feed's wire,
/// WIRE_FORMAT.md §5 "Row payloads". It replaces the spine's retired row codec, whose bytes it
/// keeps for every cell outside the two hazards below and refuses for those two.
///
/// A feed encodes as a JSON array of row objects (an empty feed is `[]`, never `null`). A cell is
/// a scalar under §2 rule 11's recognised set; a `null` cell omits its key (rule 4); anything
/// else is the `"<opaque>"` sentinel in that cell position only. Canonicality (Ordinal key sort,
/// float layout, escaping) is `Canon.render`'s, never re-implemented here. Decode accepts the
/// typed array AND the legacy `"<opaque>"` sentinel, which reads as the empty feed indefinitely.
///
/// **The two refusals (operator ruling 2026-10-18, `docs/DECISIONS.md` D20).** Each is refused at
/// ENCODE with a typed `RowCellRefusal` naming the cell, because each wrote bytes no reader could
/// trust:
///
/// - a `DateTime` of `Unspecified` kind. Its instant is not determined by the value, and the old
///   encoder chose one through `ToUniversalTime()`, which reads the encoding machine's time zone.
///   A `Utc` or `Local` `DateTime`, and every `DateTimeOffset`, names its instant and encodes as
///   before;
/// - an `int64` outside ±2^53. Every integer in that range is exactly a double, so its bytes are
///   exact; past it the double the old encoder widened to was a different number.
///
/// The row is an open name→value map. It is spelled `Map<string, obj>` here rather than by the
/// spine's alias, so this file names nothing the spine is removing.
module Fuaran.UI.RowCodec

open Fuaran.Core

/// The residual-opaque sentinel: what a row feed carried before it was typed, and what a
/// non-scalar cell renders as.
[<Literal>]
let opaqueSentinel = "<opaque>"

/// The magnitude every integer up to which is exactly a double: 2^53.
[<Literal>]
let exactIntegerLimit = 9007199254740992L

/// Which hazard a refused cell carries.
[<RequireQualifiedAccess>]
type RowCellHazard =
    /// A `DateTime` whose `Kind` is `Unspecified`: it names no instant.
    | UnspecifiedDateTime
    /// An `int64` outside ±2^53, carried as the value: no double holds it exactly.
    | InexactInteger of int64

/// A refused cell: the row's position in the feed, the cell's key, and the hazard.
type RowCellRefusal =
    { Row: int
      Key: string
      Hazard: RowCellHazard }

module RowCellRefusal =

    /// The stable code each hazard is refused under, shared with the corpus's `row-encode`
    /// vectors (WIRE_FORMAT.md §5).
    let code (r: RowCellRefusal) : string =
        match r.Hazard with
        | RowCellHazard.UnspecifiedDateTime -> "ROW_CELL_UNSPECIFIED_DATETIME"
        | RowCellHazard.InexactInteger _ -> "ROW_CELL_INEXACT_INTEGER"

    /// One sentence naming the cell and the remedy.
    let describe (r: RowCellRefusal) : string =
        let at = sprintf "row %d, cell '%s'" r.Row r.Key

        match r.Hazard with
        | RowCellHazard.UnspecifiedDateTime ->
            at
            + ": a DateTime of Unspecified kind names no instant; give it a Kind (Utc or Local) or carry a DateTimeOffset"
        | RowCellHazard.InexactInteger n ->
            at
            + sprintf ": the int64 %d is outside +/-2^53, so no JSON number holds it exactly; carry it as a string" n

/// Raised by `encodeRows` (whose callers are total encoders) when a cell is refused.
/// `tryEncodeRows` is the same rule as a value.
exception RowCellRefused of RowCellRefusal

type private Cell =
    | Omit
    | Wire of JVal
    | Refused of RowCellHazard

/// The rule-11 cell encode. The `float` test runs FIRST: under Fable every number satisfies every
/// numeric type test, so float-first routes every JS number through the canonical float layout,
/// byte-identical to .NET, where the boxed types are exact and the order is immaterial. Integral
/// floats render in integer form (rule 5), so a .NET `box 42` and a Fable `42` emit the same bytes.
let private encodeCell (v: objnull) : Cell =
    match v with
    | null -> Omit
    // Phase 2006: an exact decimal crosses as its canonical text, exactly the bytes the bare
    // string it replaced in a Transform row wrote. A decoded row reads it back as text.
    | :? Fuaran.UI.HostPrelude.DecimalCell as d -> Wire(JStr d.Text)
    | :? string as s -> Wire(JStr s)
    | :? bool as b -> Wire(JBool b)
    | :? float as f -> Wire(JFloat f)
    | :? int as n -> Wire(JInt n)
    | :? int64 as n ->
        if n > exactIntegerLimit || n < -exactIntegerLimit then
            Refused(RowCellHazard.InexactInteger n)
        else
            Wire(JFloat(float n))
    | :? float32 as f -> Wire(JFloat(float f))
    | :? System.DateTimeOffset as t -> Wire(JFloat(float (t.ToUnixTimeSeconds())))
    | :? System.DateTime as t ->
        if t.Kind = System.DateTimeKind.Unspecified then
            Refused RowCellHazard.UnspecifiedDateTime
        else
            Wire(JFloat(float (System.DateTimeOffset(t.ToUniversalTime(), System.TimeSpan.Zero).ToUnixTimeSeconds())))
    | _ -> Wire(JStr opaqueSentinel)

/// Encode a row feed, or name the first refused cell (in feed order, then Ordinal key order).
let tryEncodeRows (rows: Map<string, obj> seq) : Result<JVal, RowCellRefusal> =
    let encodeRow (i: int) (row: Map<string, obj>) : Result<JVal, RowCellRefusal> =
        let rec go acc cells =
            match cells with
            | [] -> Ok(JObj(List.rev acc))
            | (k, v) :: rest ->
                match encodeCell v with
                | Omit -> go acc rest
                | Wire j -> go ((k, j) :: acc) rest
                | Refused h -> Error { Row = i; Key = k; Hazard = h }

        go [] (Map.toList row)

    let rec all i acc (rest: Map<string, obj> list) =
        match rest with
        | [] -> Ok(JArr(List.rev acc))
        | row :: tail ->
            match encodeRow i row with
            | Ok j -> all (i + 1) (j :: acc) tail
            | Error r -> Error r

    all 0 [] (List.ofSeq rows)

/// Encode a row feed as a JSON array of row objects. Raises `RowCellRefused` on a refused cell;
/// the generated structural codec calls this, and its encoders are total over every other value.
let encodeRows (rows: Map<string, obj> seq) : JVal =
    match tryEncodeRows rows with
    | Ok j -> j
    | Error r -> raise (RowCellRefused r)

/// A decoded cell is a boxed scalar: numbers surface as `float` (JSON has one number population),
/// strings and bools as themselves. Nested arrays and objects are carried structurally so a
/// lenient ingest is not refused; they are display-opaque and re-encode as `"<opaque>"` cells.
let rec private decodeCell (j: JVal) : obj =
    match j with
    | JStr s -> s :> obj
    | JBool b -> b :> obj
    | JInt n -> float n :> obj
    | JFloat f -> f :> obj
    | JArr xs -> (xs |> List.map decodeCell) :> obj
    | JObj fields -> (fields |> List.map (fun (k, v) -> k, decodeCell v) |> Map.ofList) :> obj

/// Decode a rows payload: the typed array, or the legacy `"<opaque>"` sentinel (the empty feed).
/// Any other shape is a named error.
let decodeRows (j: JVal) : Result<Map<string, obj> seq, string> =
    match j with
    | JStr s when s = opaqueSentinel -> Ok Seq.empty
    | JArr xs ->
        let rec go acc rest =
            match rest with
            | [] -> Ok(List.rev acc |> Seq.ofList)
            | JObj fields :: tail -> go ((fields |> List.map (fun (k, v) -> k, decodeCell v) |> Map.ofList) :: acc) tail
            | other :: _ -> Error("rows: expected a row object, got " + JVal.kindName other)

        go [] xs
    | other ->
        Error(
            "rows: expected an array of row objects or \"<opaque>\", got "
            + JVal.kindName other
        )
