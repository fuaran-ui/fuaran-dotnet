module Fuaran.UI.Hashing

// ============================================================================
//  Fuaran — pure, Fable-safe SHA-256 (Phase 164).
//
//  `Fuaran.UI` ships its source for Fable consumers, so it must compile under
//  Fable — which rules out `System.Security.Cryptography`. This is a
//  dependency-free managed SHA-256 that produces the *same* lowercase-hex digest
//  as the BCL on .NET (and as the Phase-134 validator's body-shape hash) on
//  BOTH the .NET and Fable pipelines, so a `CustomContract`'s derived content
//  hash is identical across machines and runtimes.
//
//  Deliberately uses only `uint32` (no `uint64` / `int64`) and a manual nibble
//  table (no `ToString("x8")` format) — the subset Fable's numeric emulation
//  handles unambiguously. Verified against `System.Security.Cryptography.SHA256`
//  for a corpus of inputs in `Fuaran.UI.Tests`.
// ============================================================================

let private k: uint32[] =
    [| 0x428a2f98u
       0x71374491u
       0xb5c0fbcfu
       0xe9b5dba5u
       0x3956c25bu
       0x59f111f1u
       0x923f82a4u
       0xab1c5ed5u
       0xd807aa98u
       0x12835b01u
       0x243185beu
       0x550c7dc3u
       0x72be5d74u
       0x80deb1feu
       0x9bdc06a7u
       0xc19bf174u
       0xe49b69c1u
       0xefbe4786u
       0x0fc19dc6u
       0x240ca1ccu
       0x2de92c6fu
       0x4a7484aau
       0x5cb0a9dcu
       0x76f988dau
       0x983e5152u
       0xa831c66du
       0xb00327c8u
       0xbf597fc7u
       0xc6e00bf3u
       0xd5a79147u
       0x06ca6351u
       0x14292967u
       0x27b70a85u
       0x2e1b2138u
       0x4d2c6dfcu
       0x53380d13u
       0x650a7354u
       0x766a0abbu
       0x81c2c92eu
       0x92722c85u
       0xa2bfe8a1u
       0xa81a664bu
       0xc24b8b70u
       0xc76c51a3u
       0xd192e819u
       0xd6990624u
       0xf40e3585u
       0x106aa070u
       0x19a4c116u
       0x1e376c08u
       0x2748774cu
       0x34b0bcb5u
       0x391c0cb3u
       0x4ed8aa4au
       0x5b9cca4fu
       0x682e6ff3u
       0x748f82eeu
       0x78a5636fu
       0x84c87814u
       0x8cc70208u
       0x90befffau
       0xa4506cebu
       0xbef9a3f7u
       0xc67178f2u |]

let private rotr (x: uint32) (n: int) : uint32 = (x >>> n) ||| (x <<< (32 - n))

/// 32-bit wrapping add that stays exact under Fable's float-backed numerics.
/// Fable emits uint32 `+` as a plain JS `+` (no wrap); JS numbers are exact
/// only below 2^53, and SHA-256's working variables roughly double every four
/// rounds — one block stays just under the ceiling (why single-block digests
/// were correct), but the unmasked carry into a SECOND block crossed 2^53 and
/// silently lost precision (multi-block digests diverged in the browser).
/// Masking each add keeps every operand below 2^32. On .NET the mask is a
/// no-op — uint32 addition wraps natively — so digests are unchanged there.
let inline private (.+.) (x: uint32) (y: uint32) : uint32 = (x + y) &&& 0xFFFFFFFFu

/// UTF-8 encode a string to bytes (BMP + surrogate pairs). Pure managed.
let private utf8Bytes (s: string) : ResizeArray<byte> =
    let out = ResizeArray<byte>()
    let mutable i = 0

    while i < s.Length do
        let c = int s[i]

        if c < 0x80 then
            out.Add(byte c)
        elif c < 0x800 then
            out.Add(byte (0xC0 ||| (c >>> 6)))
            out.Add(byte (0x80 ||| (c &&& 0x3F)))
        elif c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length then
            let lo = int s[i + 1]
            let cp = 0x10000 + ((c - 0xD800) <<< 10) + (lo - 0xDC00)
            out.Add(byte (0xF0 ||| (cp >>> 18)))
            out.Add(byte (0x80 ||| ((cp >>> 12) &&& 0x3F)))
            out.Add(byte (0x80 ||| ((cp >>> 6) &&& 0x3F)))
            out.Add(byte (0x80 ||| (cp &&& 0x3F)))
            i <- i + 1
        else
            out.Add(byte (0xE0 ||| (c >>> 12)))
            out.Add(byte (0x80 ||| ((c >>> 6) &&& 0x3F)))
            out.Add(byte (0x80 ||| (c &&& 0x3F)))

        i <- i + 1

    out

let private hexChars = "0123456789abcdef"

let private appendHex8 (sb: System.Text.StringBuilder) (x: uint32) : unit =
    for shift in [ 28; 24; 20; 16; 12; 8; 4; 0 ] do
        sb.Append(hexChars[int ((x >>> shift) &&& 0xFu)]) |> ignore

/// Lowercase-hex SHA-256 of a UTF-8 string. Matches the BCL byte-for-byte.
let sha256Hex (input: string) : string =
    let data = utf8Bytes input
    let byteLen = data.Count
    // Padding: 0x80, then zeros, then the 64-bit big-endian bit length.
    data.Add(0x80uy)

    while data.Count % 64 <> 56 do
        data.Add(0uy)

    // Bit length as two uint32 halves (no uint64 — Fable-safe). High half is 0
    // for any input under 512 MB, which covers every contract body shape.
    let lo = uint32 byteLen <<< 3
    let hi = (uint32 byteLen >>> 29)

    for shift in [ 24; 16; 8; 0 ] do
        data.Add(byte ((hi >>> shift) &&& 0xFFu))

    for shift in [ 24; 16; 8; 0 ] do
        data.Add(byte ((lo >>> shift) &&& 0xFFu))

    let mutable h0 = 0x6a09e667u
    let mutable h1 = 0xbb67ae85u
    let mutable h2 = 0x3c6ef372u
    let mutable h3 = 0xa54ff53au
    let mutable h4 = 0x510e527fu
    let mutable h5 = 0x9b05688cu
    let mutable h6 = 0x1f83d9abu
    let mutable h7 = 0x5be0cd19u

    let w = Array.zeroCreate<uint32> 64
    let blocks = data.Count / 64

    for b in 0 .. blocks - 1 do
        let off = b * 64

        for t in 0..15 do
            w[t] <-
                (uint32 data[off + t * 4] <<< 24)
                ||| (uint32 data[off + t * 4 + 1] <<< 16)
                ||| (uint32 data[off + t * 4 + 2] <<< 8)
                ||| (uint32 data[off + t * 4 + 3])

        for t in 16..63 do
            let s0 = (rotr w[t - 15] 7) ^^^ (rotr w[t - 15] 18) ^^^ (w[t - 15] >>> 3)
            let s1 = (rotr w[t - 2] 17) ^^^ (rotr w[t - 2] 19) ^^^ (w[t - 2] >>> 10)
            w[t] <- w[t - 16] .+. s0 .+. w[t - 7] .+. s1

        let mutable a = h0
        let mutable bb = h1
        let mutable c = h2
        let mutable d = h3
        let mutable e = h4
        let mutable f = h5
        let mutable g = h6
        let mutable h = h7

        for t in 0..63 do
            let s1 = (rotr e 6) ^^^ (rotr e 11) ^^^ (rotr e 25)
            let ch = (e &&& f) ^^^ ((~~~e) &&& g)
            let temp1 = h .+. s1 .+. ch .+. k[t] .+. w[t]
            let s0 = (rotr a 2) ^^^ (rotr a 13) ^^^ (rotr a 22)
            let maj = (a &&& bb) ^^^ (a &&& c) ^^^ (bb &&& c)
            let temp2 = s0 .+. maj
            h <- g
            g <- f
            f <- e
            e <- d .+. temp1
            d <- c
            c <- bb
            bb <- a
            a <- temp1 .+. temp2

        h0 <- h0 .+. a
        h1 <- h1 .+. bb
        h2 <- h2 .+. c
        h3 <- h3 .+. d
        h4 <- h4 .+. e
        h5 <- h5 .+. f
        h6 <- h6 .+. g
        h7 <- h7 .+. h

    let sb = System.Text.StringBuilder()

    for hv in [ h0; h1; h2; h3; h4; h5; h6; h7 ] do
        appendHex8 sb hv

    sb.ToString()

/// The canonical Phase-134 Custom *body-shape* hash — identical to the
/// validator's `computeBodyShapeHash` (same canonical string, same SHA-256), so
/// a contract-derived hash and a hand-set-then-validated hash agree. `propKeys`
/// is the props schema (keys only); both lists are sorted internally.
let customBodyShapeHash
    (moduleId: string)
    (componentId: string)
    (propKeys: string list)
    (exposedNodeIds: string list)
    : string =
    [ "fuaran-custom-body-shape:v1"
      "moduleId=" + moduleId
      "componentId=" + componentId
      "props=" + (propKeys |> List.sort |> String.concat ",")
      "exposed=" + (exposedNodeIds |> List.sort |> String.concat ",") ]
    |> String.concat "\n"
    |> sha256Hex
