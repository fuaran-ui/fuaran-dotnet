module Fuaran.UI.ServerDriven.AspNetCore.Tests.TestSupport

// ─── Shared test plumbing for the SSE endpoint suites ──────────────────────
//
// Every wait here is SIGNAL-driven and holds no thread (the Phase 2497 rule,
// carried to this backend by Phase 1928): the bound is only how long a missing
// signal is waited for before the test fails. Nothing blocks a pool thread
// while it waits, so a loaded gate machine — or a pool pinned small — delays
// the signals without the test itself standing between them and the threads
// they need.

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks

let bound = TimeSpan.FromSeconds 10.0

/// A completion source whose continuations never run inline on the setter.
let signal () =
    TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

/// Await `signal` without holding a thread: true when it completes (in any
/// state) before the bound, false when the bound expires first.
let within (signal: Task) : Task<bool> =
    task {
        use expiry = new CancellationTokenSource()
        let! first = Task.WhenAny(signal, Task.Delay(bound, expiry.Token))
        expiry.Cancel()
        return obj.ReferenceEquals(first, signal)
    }

/// Await `cond` becoming true, re-reading it on an awaited timer (no thread is
/// held between reads) — for a state with no signal of its own to await, such
/// as a registry evicting on its own timer. False at the bound.
let eventuallyAsync (cond: unit -> bool) : Task<bool> =
    task {
        let deadline = DateTime.UtcNow + bound
        let mutable ok = cond ()

        while not ok && DateTime.UtcNow < deadline do
            do! Task.Delay 10
            ok <- cond ()

        return ok
    }

/// Run `invoke` on its own thread, so a bounded check that it RETURNS takes no
/// pool thread: a handler that blocked would not return, and the check fails at
/// the bound instead of hanging the suite.
let offPool (invoke: unit -> 'T) : Task<'T> =
    Task.Factory.StartNew(
        Func<'T> invoke,
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default
    )

/// A write-only response body the handler writes on its thread and the test
/// reads on its own. `Flushed` completes at the first flush — the stream
/// handler's first act once its connection is registered — and `Contains`
/// completes once the text written so far contains a needle, so a test awaits
/// what was streamed rather than polling for it. `failWrites` makes every write
/// throw, as a write to a dropped client does.
type SharedBody(failWrites: bool) =
    inherit Stream()
    let buffer = new MemoryStream()
    let gate = obj ()
    let flushed = signal ()
    let waiting = ResizeArray<string * TaskCompletionSource>()

    let text () =
        Encoding.UTF8.GetString(buffer.ToArray())

    /// Complete every waiter whose needle the text now contains. Callers hold `gate`.
    let settle () =
        let now = text ()

        for (needle, waiter) in List.ofSeq waiting do
            if now.Contains needle then
                waiter.TrySetResult() |> ignore
                waiting.Remove((needle, waiter)) |> ignore

    let append (bytes: byte[]) offset count =
        if failWrites then
            raise (IOException "the client is gone")

        lock gate (fun () ->
            buffer.Write(bytes, offset, count)
            settle ())

    new() = new SharedBody(false)

    member _.Text = lock gate text

    /// Completes at the first flush.
    member _.Flushed: Task = flushed.Task

    /// Completes once the text written contains `needle`.
    member _.Contains(needle: string) : Task =
        lock gate (fun () ->
            let waiter = signal ()
            waiting.Add((needle, waiter))
            settle ()
            waiter.Task)

    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = lock gate (fun () -> buffer.Length)

    override _.Position
        with get () = 0L
        and set _ = raise (NotSupportedException())

    override _.Flush() = flushed.TrySetResult() |> ignore

    override _.FlushAsync(_: CancellationToken) =
        flushed.TrySetResult() |> ignore
        Task.CompletedTask

    override _.Read(_: byte[], _: int, _: int) : int = raise (NotSupportedException())
    override _.Seek(_: int64, _: SeekOrigin) : int64 = raise (NotSupportedException())
    override _.SetLength(_: int64) = raise (NotSupportedException())
    override _.Write(bytes: byte[], offset: int, count: int) = append bytes offset count

    override _.WriteAsync(bytes: byte[], offset: int, count: int, _: CancellationToken) =
        append bytes offset count
        Task.CompletedTask

    override _.WriteAsync(bytes: ReadOnlyMemory<byte>, _: CancellationToken) =
        let copy = bytes.ToArray()
        append copy 0 copy.Length
        ValueTask()
