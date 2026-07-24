module Fuaran.UI.RetrievalSource

// ============================================================================
//  Semantic-retrieval source seam — the data-plane SIBLING of
//  `IClientQuerySource` (Phase 325 core).
//
//  Some questions are not relational — "find prior cases like this" is semantic
//  retrieval, not SQL. This is the retrieval analogue of the relational seam: a
//  host-pluggable source returns ranked hits as the canonical retrieval `Table`
//  (`hitSchema` below), so retrieval results flow through the **same** typed
//  query→`Binding` thread + validator (`QueryBinding.check` / Phase 323) as
//  relational rows. ONE UI contract for both data planes — a hit's `title`
//  (`string`) cannot bind into a numeric gauge any more than a SQL `string`
//  column can; the mismatch is the same typed defect (FUARAN066/067).
//
//  TRANSPORT IS A HOST CONCERN (deferred, infra-bound). The actual retrieval —
//  embeddings, vector store, hybrid dense+sparse fusion — runs server-side on a
//  hosted endpoint or a paired-device peer the browser reaches over its live
//  channel; the browser only *issues requests and renders typed hits*, never
//  embeds/indexes. Those transports are private, infra-bound host implementations
//  of `IClientRetrievalSource` and are NOT in this OSS, Fable-clean substrate —
//  this module is the transport-agnostic seam + the canonical hit schema + the
//  hit→`Table` projection only. (See Phase 325 for the transport legs.)
//
//  PORTABILITY / PARITY. FSharp.Core + `Fuaran.Core.Column` only,
//  async-at-the-boundary, stateless, identity-by-value, Fable-clean — the seam +
//  the typed thread run identically in the browser and under .NET, exactly like
//  `QuerySource`.
// ============================================================================

open Fuaran.Core

/// The canonical typed schema of a retrieval hit — the contract a retrieval
/// source produces and a retrieval dashboard is typed against. Ranked by
/// `score`; `title` + `snippet` carry the citation text; `sourceId` addresses the
/// document; `date` is the hit's timestamp (ISO string, Fable-clean — no host
/// `DateTime`). A retrieval dashboard binds these exactly as a relational one
/// binds SQL columns, through the same Phase 323 thread.
let hitSchema: Schema =
    [ "score", FloatType
      "title", StringType
      "snippet", StringType
      "sourceId", StringType
      "date", DateType ]

/// One ranked retrieval hit (the row shape behind `hitSchema`).
type Hit =
    {
        Score: float
        Title: string
        Snippet: string
        SourceId: string
        /// ISO-8601 date (`YYYY-MM-DD`) — a string, so the model stays Fable-clean
        /// and byte-identical across hosts (the `Fuaran.Core.Column` `Date` cell).
        Date: string
    }

module Hit =

    /// Project ranked hits into the canonical retrieval `Table` (order preserved)
    /// — the seam by which retrieval results enter the SAME columnar typed thread
    /// as a relational query result. The table's `Schema` is `hitSchema`, so
    /// `QuerySource.checkDashboard hitSchema dashboard` / `QueryBinding.check`
    /// type a retrieval dashboard with no retrieval-specific code path.
    let toTable (hits: Hit list) : Table =
        { Schema = hitSchema
          Columns =
            [ Column.create "score" FloatType (hits |> List.map (fun h -> Float h.Score))
              Column.create "title" StringType (hits |> List.map (fun h -> Str h.Title))
              Column.create "snippet" StringType (hits |> List.map (fun h -> Str h.Snippet))
              Column.create "sourceId" StringType (hits |> List.map (fun h -> Str h.SourceId))
              Column.create "date" DateType (hits |> List.map (fun h -> Date h.Date)) ] }

/// A retrieval request: the natural-language query + how many hits to return +
/// optional metadata filters (`(field, value)` equality constraints the host
/// applies). Distinct from `QuerySource.QueryRequest` (relational SQL / Transform)
/// — semantic retrieval has its own request semantics, but the same typed `Table`
/// result.
type RetrievalRequest =
    { Query: string
      TopK: int
      Filters: (string * string) list }

module RetrievalRequest =

    /// A bare top-k retrieval over `query`, no filters.
    let create (query: string) (topK: int) : RetrievalRequest =
        { Query = query
          TopK = topK
          Filters = [] }

/// Why a retrieval failed. Named + total (GP5); the retrieval analogue of
/// `QuerySource.QuerySourceError`.
[<RequireQualifiedAccess>]
type RetrievalError =
    /// The retrieval endpoint (hosted or paired-device peer) could not be reached.
    | SourceUnavailable of detail: string
    /// The retrieval ran but failed (bad index, embedding error, permission).
    | RetrievalFailed of detail: string
    /// The retrieval exceeded its time budget.
    | Timeout

module RetrievalError =

    /// A stable human string for a retrieval error.
    let message (e: RetrievalError) : string =
        match e with
        | RetrievalError.SourceUnavailable d -> "retrieval source unavailable: " + d
        | RetrievalError.RetrievalFailed d -> "retrieval failed: " + d
        | RetrievalError.Timeout -> "retrieval timed out"

/// The host-pluggable semantic-retrieval seam. Returns ranked hits as the
/// canonical retrieval `Table`, so retrieval results type-check through the SAME
/// 323 thread as relational rows. Transport-agnostic (hosted HTTP / paired-device
/// peer are host impls), async-at-the-boundary, stateless, identity-by-value.
type IClientRetrievalSource =
    abstract member Retrieve: request: RetrievalRequest -> Async<Result<Table, RetrievalError>>

/// In-memory / mock retrieval sources — the offline shape + the test seam (no
/// embeddings, no vector store: a fixed or computed hit list).
module InMemoryRetrievalSource =

    /// A source backed by a synchronous resolver over the request (wrapped at the
    /// async boundary), projecting the returned hits into the canonical `Table`.
    let ofResolver (resolve: RetrievalRequest -> Result<Hit list, RetrievalError>) : IClientRetrievalSource =
        { new IClientRetrievalSource with
            member _.Retrieve(request) =
                async {
                    match resolve request with
                    | Error e -> return Error e
                    | Ok hits -> return Ok(Hit.toTable hits)
                } }

    /// A source that returns the same fixed ranked hits (honouring `TopK` by
    /// truncation) for every request — the minimal offline proof + test seam.
    let ofHits (hits: Hit list) : IClientRetrievalSource =
        ofResolver (fun request -> Ok(hits |> List.truncate (max 0 request.TopK)))
