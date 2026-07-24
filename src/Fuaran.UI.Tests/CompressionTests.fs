module Fuaran.UI.Tests.CompressionTests

// ============================================================================
//  Phase 437 — the teleport byte substrate: UTF-8 / base64url / raw DEFLATE.
//
//  The deflate legs assert three properties:
//    1. Round-trip: inflate (compress x) = x for representative and edge
//       inputs (empty, tiny, incompressible, highly repetitive, JSON-like).
//    2. Determinism: compress is a pure function of the input bytes (the
//       teleport bundle's "same bundle ⇒ same string" contract rides on it).
//    3. BCL interop (the conformance cross-check, .NET runner only): the BCL
//       `DeflateStream` decodes our output, and our inflater decodes the
//       BCL's — both directions, proving the streams are real RFC 1951 and
//       exercising the dynamic-Huffman decode path a foreign producer emits.
// ============================================================================

open System
open Expecto
open Fuaran.UI

let private rngBytes (seed: int) (count: int) : byte[] =
    // Deterministic xorshift so the corpus is stable across runs/hosts.
    let mutable s = uint32 (if seed = 0 then 0x9E3779B9 else seed)

    Array.init count (fun _ ->
        s <- s ^^^ (s <<< 13)
        s <- s ^^^ (s >>> 17)
        s <- s ^^^ (s <<< 5)
        byte (s &&& 0xFFu))

let private corpus: (string * byte[]) list =
    [ "empty", [||]
      "one byte", [| 42uy |]
      "two bytes", [| 0uy; 255uy |]
      "short ascii", Utf8.encode "hello, teleport"
      "repetitive", Array.create 4096 7uy
      "run of runs", Utf8.encode (String.replicate 200 "abcabcabd")
      "json-like",
      Utf8.encode (
          """{"bundle":"teleport@1","state":{"wizard-step":2,"draft-name":"Àëïôü — テスト"},"tree":{"id":"root","kind":{"$type":"Box"}}}"""
          |> String.replicate 8
      )
      "incompressible", rngBytes 1234 2048
      "window spill", rngBytes 99 40000 ]

#if !FABLE_COMPILER
let private bclInflate (data: byte[]) : byte[] =
    use src = new IO.MemoryStream(data)

    use ds =
        new IO.Compression.DeflateStream(src, IO.Compression.CompressionMode.Decompress)

    use out = new IO.MemoryStream()
    ds.CopyTo out
    out.ToArray()

let private bclDeflate (data: byte[]) : byte[] =
    use out = new IO.MemoryStream()

    do
        use ds =
            new IO.Compression.DeflateStream(out, IO.Compression.CompressionLevel.Optimal, true)

        ds.Write(data, 0, data.Length)

    out.ToArray()
#endif

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Compression"
        [ testList
              "Utf8"
              [ test "round-trips ascii, bmp, and astral text" {
                    for s in [ ""; "plain"; "Àëïôü — テスト"; "emoji \U0001F680 pair" ] do
                        match Utf8.decode (Utf8.encode s) with
                        | Ok back -> Expect.equal back s "utf-8 round-trip"
                        | Error e -> failtestf "utf-8 decode failed for %A: %s" s e
                }

                test "rejects malformed sequences" {
                    Expect.isError (Utf8.decode [| 0xC0uy; 0xAFuy |]) "over-long encoding rejected"
                    Expect.isError (Utf8.decode [| 0xE2uy; 0x28uy; 0xA1uy |]) "bad continuation rejected"
                    Expect.isError (Utf8.decode [| 0xF0uy; 0x9Fuy |]) "truncated sequence rejected"
                } ]

          testList
              "Base64Url"
              [ test "round-trips every remainder length" {
                    for len in 0..9 do
                        let bytes = rngBytes (100 + len) len
                        let enc = Base64Url.encode bytes

                        Expect.isFalse (enc.Contains "=") "unpadded"
                        Expect.isFalse (enc.Contains "+") "url-safe (+)"
                        Expect.isFalse (enc.Contains "/") "url-safe (/)"

                        match Base64Url.decode enc with
                        | Ok back -> Expect.equal back bytes (sprintf "round-trip at length %d" len)
                        | Error e -> failtestf "decode failed at length %d: %s" len e
                }

                test "known vector" {
                    // 0xFB 0xEF 0xBE → "----" in base64url (0x3E/0x3F land on - and _ variants).
                    Expect.equal (Base64Url.encode [| 0xFBuy; 0xEFuy; 0xBEuy |]) "----" "url-safe alphabet"

                    Expect.equal
                        (Base64Url.encode (Utf8.encode "any carnal pleas"))
                        "YW55IGNhcm5hbCBwbGVhcw"
                        "rfc vector"
                }

                test "rejects invalid input" {
                    Expect.isError (Base64Url.decode "abc+") "standard-alphabet + rejected"
                    Expect.isError (Base64Url.decode "abc=") "padding rejected"
                    Expect.isError (Base64Url.decode "abcde") "length ≡ 1 (mod 4) rejected"
                } ]

          testList
              "Deflate"
              [ test "round-trips the corpus" {
                    for name, input in corpus do
                        let packed = Deflate.compress input

                        match Deflate.inflate (input.Length + 64) packed with
                        | Ok back -> Expect.equal back input (sprintf "round-trip: %s" name)
                        | Error e -> failtestf "inflate failed for %s: %A" name e
                }

                test "compress is deterministic" {
                    for name, input in corpus do
                        let a = Deflate.compress input
                        let b = Deflate.compress (Array.copy input)
                        Expect.equal a b (sprintf "same input ⇒ same bytes: %s" name)
                }

                test "compresses repetitive input" {
                    let input = snd (List.item 4 corpus) // 4096 × 7uy
                    let packed = Deflate.compress input
                    Expect.isLessThan packed.Length 128 "4 KB of one byte packs far below 128 bytes"
                }

                test "output cap rejects a deflate bomb" {
                    let packed = Deflate.compress (Array.create 100_000 0uy)

                    match Deflate.inflate 4096 packed with
                    | Error(Deflate.InflateError.OutputLimit 4096) -> ()
                    | other -> failtestf "expected OutputLimit, got %A" other
                }

                test "rejects garbage input" {
                    match Deflate.inflate 4096 (rngBytes 7 64) with
                    | Error(Deflate.InflateError.Malformed _) -> ()
                    | Ok _ ->
                        // Random bytes can, rarely, decode as a valid stream; the
                        // seed above is pinned to a failing input.
                        failtest "expected Malformed for the pinned garbage input"
                    | Error e -> failtestf "expected Malformed, got %A" e
                }

#if !FABLE_COMPILER
                test "BCL DeflateStream decodes our output (conformance)" {
                    for name, input in corpus do
                        Expect.equal (bclInflate (Deflate.compress input)) input (sprintf "BCL reads ours: %s" name)
                }

                test "our inflater decodes BCL output incl. dynamic blocks (conformance)" {
                    // The empty case is excluded: a never-written DeflateStream
                    // emits 0 bytes — not an RFC 1951 stream (no final block) —
                    // which our inflater correctly rejects as truncated.
                    for name, input in corpus |> List.filter (fun (_, b) -> b.Length > 0) do
                        match Deflate.inflate (input.Length + 64) (bclDeflate input) with
                        | Ok back -> Expect.equal back input (sprintf "ours reads BCL: %s" name)
                        | Error e -> failtestf "inflate of BCL stream failed for %s: %A" name e
                }
#endif
                ] ]
