namespace Fuaran.UI

// ============================================================================
//  Fuaran — pure, Fable-safe UTF-8 / base64url / raw-DEFLATE primitives
//  (Phase 437 — the teleport state-bundle codec's byte substrate).
//
//  `Fuaran.UI` ships its source for Fable consumers, so everything here must
//  compile under Fable — which rules out `System.IO.Compression` and the BCL
//  `Convert`/`Encoding` seams whose byte behaviour differs (or is absent)
//  across pipelines. These are dependency-free managed implementations that
//  produce the *same bytes* on BOTH the .NET and Fable pipelines, so an
//  encoded teleport bundle is byte-identical across machines and runtimes —
//  the same posture as the Phase-164 `Fuaran.UI.Hashing` SHA-256.
//
//  DEFLATE (RFC 1951, raw — no zlib/gzip wrapper):
//    - `Deflate.compress` emits a single fixed-Huffman block with greedy
//      LZ77 matching (32 KB window, hash-chained). Fixed-Huffman + a
//      deterministic matcher means the output is a pure function of the
//      input bytes — no host library, no version drift, no cross-pipeline
//      divergence. (Dynamic-Huffman would shave a few percent; determinism
//      and portability outrank it here.)
//    - `Deflate.inflate` decodes the FULL raw-DEFLATE range (stored, fixed,
//      and dynamic-Huffman blocks), so bundles produced by another
//      conformant host with a standard deflate library decode fine. The
//      `maxOutput` cap bounds decompression of untrusted input (a deflate
//      bomb fails as a typed error, never unbounded allocation).
//
//  Deliberately int/uint32-only arithmetic (no int64) and byte-at-a-time
//  bit buffers (accumulators stay far below 2^31) — the subset Fable's
//  numeric emulation handles unambiguously.
// ============================================================================

/// Pure managed UTF-8 encode/decode (BMP + surrogate pairs). The encode half
/// mirrors `Fuaran.UI.Hashing`'s internal converter; the decode half is strict
/// (malformed sequences are a typed `Error`, never replacement characters —
/// a teleport payload is untrusted input and silent repair would break the
/// byte-exact round-trip contract).
module Utf8 =

    /// UTF-8 bytes of `s`.
    let encode (s: string) : byte[] =
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

        out.ToArray()

    /// Decode UTF-8 `bytes` to a string, or `Error` on a malformed sequence.
    let decode (bytes: byte[]) : Result<string, string> =
        let sb = System.Text.StringBuilder()
        let n = bytes.Length
        let mutable i = 0
        let mutable error: string option = None

        let cont (idx: int) : int =
            if idx < n && (int bytes[idx] &&& 0xC0) = 0x80 then
                int bytes[idx] &&& 0x3F
            else
                -1

        while error.IsNone && i < n do
            let b0 = int bytes[i]

            if b0 < 0x80 then
                sb.Append(char b0) |> ignore
                i <- i + 1
            elif b0 < 0xC2 then
                // 0x80–0xBF: stray continuation; 0xC0/0xC1: over-long encoding.
                error <- Some(sprintf "malformed UTF-8 byte 0x%02X at offset %d" b0 i)
            elif b0 < 0xE0 then
                let c1 = cont (i + 1)

                if c1 < 0 then
                    error <- Some(sprintf "truncated UTF-8 sequence at offset %d" i)
                else
                    sb.Append(char (((b0 &&& 0x1F) <<< 6) ||| c1)) |> ignore
                    i <- i + 2
            elif b0 < 0xF0 then
                let c1 = cont (i + 1)
                let c2 = cont (i + 2)

                if c1 < 0 || c2 < 0 then
                    error <- Some(sprintf "truncated UTF-8 sequence at offset %d" i)
                else
                    let cp = ((b0 &&& 0x0F) <<< 12) ||| (c1 <<< 6) ||| c2

                    if cp < 0x800 || (cp >= 0xD800 && cp <= 0xDFFF) then
                        error <- Some(sprintf "invalid UTF-8 code point at offset %d" i)
                    else
                        sb.Append(char cp) |> ignore
                        i <- i + 3
            elif b0 < 0xF5 then
                let c1 = cont (i + 1)
                let c2 = cont (i + 2)
                let c3 = cont (i + 3)

                if c1 < 0 || c2 < 0 || c3 < 0 then
                    error <- Some(sprintf "truncated UTF-8 sequence at offset %d" i)
                else
                    let cp = ((b0 &&& 0x07) <<< 18) ||| (c1 <<< 12) ||| (c2 <<< 6) ||| c3

                    if cp < 0x10000 || cp > 0x10FFFF then
                        error <- Some(sprintf "invalid UTF-8 code point at offset %d" i)
                    else
                        let v = cp - 0x10000
                        sb.Append(char (0xD800 + (v >>> 10))) |> ignore
                        sb.Append(char (0xDC00 + (v &&& 0x3FF))) |> ignore
                        i <- i + 4
            else
                error <- Some(sprintf "malformed UTF-8 byte 0x%02X at offset %d" b0 i)

        match error with
        | Some e -> Error e
        | None -> Ok(sb.ToString())

/// URL-safe base64 (RFC 4648 §5) without padding — the alphabet a URL
/// fragment and a QR byte payload carry verbatim.
module Base64Url =

    [<Literal>]
    let private Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"

    /// Encode `bytes` as unpadded base64url.
    let encode (bytes: byte[]) : string =
        let sb = System.Text.StringBuilder()
        let n = bytes.Length
        let mutable i = 0

        while i + 2 < n do
            let v = (int bytes[i] <<< 16) ||| (int bytes[i + 1] <<< 8) ||| int bytes[i + 2]
            sb.Append(Alphabet[(v >>> 18) &&& 63]) |> ignore
            sb.Append(Alphabet[(v >>> 12) &&& 63]) |> ignore
            sb.Append(Alphabet[(v >>> 6) &&& 63]) |> ignore
            sb.Append(Alphabet[v &&& 63]) |> ignore
            i <- i + 3

        if n - i = 1 then
            let v = int bytes[i] <<< 16
            sb.Append(Alphabet[(v >>> 18) &&& 63]) |> ignore
            sb.Append(Alphabet[(v >>> 12) &&& 63]) |> ignore
        elif n - i = 2 then
            let v = (int bytes[i] <<< 16) ||| (int bytes[i + 1] <<< 8)
            sb.Append(Alphabet[(v >>> 18) &&& 63]) |> ignore
            sb.Append(Alphabet[(v >>> 12) &&& 63]) |> ignore
            sb.Append(Alphabet[(v >>> 6) &&& 63]) |> ignore

        sb.ToString()

    let private valueOf (c: char) : int =
        if c >= 'A' && c <= 'Z' then int c - int 'A'
        elif c >= 'a' && c <= 'z' then 26 + int c - int 'a'
        elif c >= '0' && c <= '9' then 52 + int c - int '0'
        elif c = '-' then 62
        elif c = '_' then 63
        else -1

    /// Decode unpadded base64url, or `Error` on an invalid character / length.
    let decode (s: string) : Result<byte[], string> =
        if s.Length % 4 = 1 then
            Error "invalid base64url length"
        else
            let out = ResizeArray<byte>()
            let mutable i = 0
            let mutable error: string option = None

            while error.IsNone && i < s.Length do
                let remaining = s.Length - i
                let take = min 4 remaining

                if take = 1 then
                    error <- Some "invalid base64url length"
                else
                    let mutable v = 0
                    let mutable j = 0

                    while error.IsNone && j < take do
                        let d = valueOf s[i + j]

                        if d < 0 then
                            error <- Some(sprintf "invalid base64url character '%c' at offset %d" s[i + j] (i + j))
                        else
                            v <- (v <<< 6) ||| d
                            j <- j + 1

                    if error.IsNone then
                        match take with
                        | 4 ->
                            out.Add(byte ((v >>> 16) &&& 0xFF))
                            out.Add(byte ((v >>> 8) &&& 0xFF))
                            out.Add(byte (v &&& 0xFF))
                        | 3 ->
                            // 18 significant bits → 2 bytes.
                            out.Add(byte ((v >>> 10) &&& 0xFF))
                            out.Add(byte ((v >>> 2) &&& 0xFF))
                        | _ ->
                            // take = 2: 12 significant bits → 1 byte.
                            out.Add(byte ((v >>> 4) &&& 0xFF))

                        i <- i + take

            match error with
            | Some e -> Error e
            | None -> Ok(out.ToArray())

/// Raw DEFLATE (RFC 1951) — deterministic fixed-Huffman compressor + a full
/// (stored / fixed / dynamic) decompressor with an output cap.
module Deflate =

    /// A decompression failure. `OutputLimit` is the deflate-bomb guard — the
    /// decoded stream exceeded the caller's cap; `Malformed` is a structural
    /// defect in the stream itself.
    [<RequireQualifiedAccess>]
    type InflateError =
        | OutputLimit of limit: int
        | Malformed of message: string

    exception private InflateFail of InflateError

    // ── Shared RFC 1951 length/distance tables ──────────────────────────────

    let private lengthBase =
        [| 3
           4
           5
           6
           7
           8
           9
           10
           11
           13
           15
           17
           19
           23
           27
           31
           35
           43
           51
           59
           67
           83
           99
           115
           131
           163
           195
           227
           258 |]

    let private lengthExtra =
        [| 0
           0
           0
           0
           0
           0
           0
           0
           1
           1
           1
           1
           2
           2
           2
           2
           3
           3
           3
           3
           4
           4
           4
           4
           5
           5
           5
           5
           0 |]

    let private distBase =
        [| 1
           2
           3
           4
           5
           7
           9
           13
           17
           25
           33
           49
           65
           97
           129
           193
           257
           385
           513
           769
           1025
           1537
           2049
           3073
           4097
           6145
           8193
           12289
           16385
           24577 |]

    let private distExtra =
        [| 0
           0
           0
           0
           1
           1
           2
           2
           3
           3
           4
           4
           5
           5
           6
           6
           7
           7
           8
           8
           9
           9
           10
           10
           11
           11
           12
           12
           13
           13 |]

    /// Length (3..258) → index into `lengthBase` (length code − 257).
    let private lengthCodeIndex: int[] =
        let arr = Array.create 259 0

        for idx in 0..28 do
            let lo = lengthBase[idx]
            let hi = if idx = 28 then 258 else lengthBase[idx + 1] - 1

            for l in lo..hi do
                arr[l] <- idx

        arr

    let private distCodeFor (d: int) : int =
        let mutable idx = 29

        while distBase[idx] > d do
            idx <- idx - 1

        idx

    // ── Compressor — single fixed-Huffman block, greedy hash-chained LZ77 ───

    /// LSB-first bit writer (RFC 1951 bit order). Huffman codes go through
    /// `WriteHuff` (bit-reversed — codes are packed most-significant-bit
    /// first); block headers and extra bits go through `WriteBits`.
    type private BitWriter() =
        let bytes = ResizeArray<byte>()
        let mutable acc = 0
        let mutable nbits = 0

        member _.WriteBits(value: int, count: int) : unit =
            acc <- acc ||| (value <<< nbits)
            nbits <- nbits + count

            while nbits >= 8 do
                bytes.Add(byte (acc &&& 0xFF))
                acc <- acc >>> 8
                nbits <- nbits - 8

        member this.WriteHuff(code: int, len: int) : unit =
            let mutable rev = 0

            for i in 0 .. len - 1 do
                rev <- (rev <<< 1) ||| ((code >>> i) &&& 1)

            this.WriteBits(rev, len)

        member _.ToArray() : byte[] =
            if nbits > 0 then
                bytes.Add(byte (acc &&& 0xFF))
                acc <- 0
                nbits <- 0

            bytes.ToArray()

    /// Fixed literal/length code for symbol 0..287 → (code, bit length).
    let private fixedLitCode (sym: int) : int * int =
        if sym <= 143 then (0x30 + sym, 8)
        elif sym <= 255 then (0x190 + (sym - 144), 9)
        elif sym <= 279 then (sym - 256, 7)
        else (0xC0 + (sym - 280), 8)

    [<Literal>]
    let private WindowSize = 32768

    [<Literal>]
    let private MinMatch = 3

    [<Literal>]
    let private MaxMatch = 258

    /// Longest-match search cap. Bounds worst-case compress time on
    /// pathological input; part of the pinned deterministic output (changing
    /// it changes the emitted bytes, so treat it as frozen for FT1 payloads).
    [<Literal>]
    let private MaxChain = 64

    /// Compress `input` as a raw DEFLATE stream (one final fixed-Huffman
    /// block). Deterministic: the same input yields the same bytes on every
    /// host and pipeline.
    let compress (input: byte[]) : byte[] =
        let n = input.Length
        let w = BitWriter()
        w.WriteBits(1, 1) // BFINAL
        w.WriteBits(1, 2) // BTYPE = 01 (fixed Huffman)

        let hashSize = 1 <<< 15
        let head = Array.create hashSize -1
        let prev = Array.create (max 1 n) -1

        let hash3 (i: int) : int =
            ((int input[i] <<< 10) ^^^ (int input[i + 1] <<< 5) ^^^ int input[i + 2])
            &&& (hashSize - 1)

        let insert (i: int) : unit =
            if i + 2 < n then
                let h = hash3 i
                prev[i] <- head[h]
                head[h] <- i

        let writeSym (sym: int) : unit =
            let code, bits = fixedLitCode sym
            w.WriteHuff(code, bits)

        let mutable i = 0

        while i < n do
            let mutable bestLen = 0
            let mutable bestDist = 0

            if i + 2 < n then
                let mutable cand = head[hash3 i]
                let mutable chain = 0
                let limit = i - WindowSize
                let maxL = min MaxMatch (n - i)

                while cand >= 0 && cand >= limit && chain < MaxChain do
                    let mutable l = 0

                    while l < maxL && input[cand + l] = input[i + l] do
                        l <- l + 1

                    if l > bestLen then
                        bestLen <- l
                        bestDist <- i - cand

                    cand <- prev[cand]
                    chain <- chain + 1

            if bestLen >= MinMatch then
                let lc = lengthCodeIndex[bestLen]
                writeSym (257 + lc)
                w.WriteBits(bestLen - lengthBase[lc], lengthExtra[lc])
                let dc = distCodeFor bestDist
                // Fixed distance codes: the 5-bit code IS the distance code.
                w.WriteHuff(dc, 5)
                w.WriteBits(bestDist - distBase[dc], distExtra[dc])

                for j in i .. i + bestLen - 1 do
                    insert j

                i <- i + bestLen
            else
                writeSym (int input[i])
                insert i
                i <- i + 1

        writeSym 256 // end of block
        w.ToArray()

    // ── Decompressor — stored / fixed / dynamic blocks (puff-style) ─────────

    /// LSB-first bit reader over the compressed bytes.
    type private BitReader(data: byte[]) =
        let mutable pos = 0
        let mutable acc = 0
        let mutable nbits = 0

        member _.ReadBits(count: int) : int =
            while nbits < count do
                if pos >= data.Length then
                    raise (InflateFail(InflateError.Malformed "unexpected end of deflate stream"))

                acc <- acc ||| (int data[pos] <<< nbits)
                pos <- pos + 1
                nbits <- nbits + 8

            let v = acc &&& ((1 <<< count) - 1)
            acc <- acc >>> count
            nbits <- nbits - count
            v

        /// Drop the partial bit-buffer (stored-block alignment) and return the
        /// next whole byte.
        member _.AlignedByte() : int =
            acc <- 0
            nbits <- 0

            if pos >= data.Length then
                raise (InflateFail(InflateError.Malformed "unexpected end of deflate stream"))

            let b = int data[pos]
            pos <- pos + 1
            b

    /// A canonical Huffman decode table: `Counts[len]` = number of codes of
    /// each bit length, `Symbols` sorted by (length, symbol).
    type private Huffman = { Counts: int[]; Symbols: int[] }

    let private buildHuffman (lengths: int[]) : Huffman =
        let counts = Array.create 16 0

        for l in lengths do
            counts[l] <- counts[l] + 1

        counts[0] <- 0

        // Over-subscription check (an incomplete code is legal — e.g. the
        // single-distance-code case — and surfaces as a decode failure only
        // if a missing code is actually referenced).
        let mutable left = 1

        for len in 1..15 do
            left <- (left <<< 1) - counts[len]

            if left < 0 then
                raise (InflateFail(InflateError.Malformed "over-subscribed Huffman code"))

        let offs = Array.create 16 0

        for len in 1..14 do
            offs[len + 1] <- offs[len] + counts[len]

        let symbols = Array.create (Array.sum counts) 0

        for sym in 0 .. lengths.Length - 1 do
            if lengths[sym] <> 0 then
                symbols[offs[lengths[sym]]] <- sym
                offs[lengths[sym]] <- offs[lengths[sym]] + 1

        { Counts = counts; Symbols = symbols }

    let private decodeSym (br: BitReader) (h: Huffman) : int =
        let mutable code = 0
        let mutable first = 0
        let mutable index = 0
        let mutable len = 1
        let mutable result = -1

        while result < 0 do
            if len > 15 then
                raise (InflateFail(InflateError.Malformed "invalid Huffman code"))

            code <- code ||| br.ReadBits 1
            let count = h.Counts[len]

            if code - first < count then
                result <- h.Symbols[index + (code - first)]
            else
                index <- index + count
                first <- (first + count) <<< 1
                code <- code <<< 1
                len <- len + 1

        result

    let private fixedLit: Huffman =
        buildHuffman
            [| for i in 0..287 ->
                   if i <= 143 then 8
                   elif i <= 255 then 9
                   elif i <= 279 then 7
                   else 8 |]

    let private fixedDist: Huffman = buildHuffman (Array.create 30 5)

    let private clOrder =
        [| 16; 17; 18; 0; 8; 7; 9; 6; 10; 5; 11; 4; 12; 3; 13; 2; 14; 1; 15 |]

    let private readDynamicTables (br: BitReader) : Huffman * Huffman =
        let hlit = br.ReadBits 5 + 257
        let hdist = br.ReadBits 5 + 1
        let hclen = br.ReadBits 4 + 4

        if hlit > 286 || hdist > 30 then
            raise (InflateFail(InflateError.Malformed "dynamic block header out of range"))

        let clLengths = Array.create 19 0

        for i in 0 .. hclen - 1 do
            clLengths[clOrder[i]] <- br.ReadBits 3

        let clHuff = buildHuffman clLengths
        let total = hlit + hdist
        let lengths = Array.create total 0
        let mutable i = 0

        while i < total do
            let sym = decodeSym br clHuff

            if sym < 16 then
                lengths[i] <- sym
                i <- i + 1
            else
                let value, repeat =
                    if sym = 16 then
                        if i = 0 then
                            raise (InflateFail(InflateError.Malformed "repeat code with no previous length"))

                        lengths[i - 1], 3 + br.ReadBits 2
                    elif sym = 17 then
                        0, 3 + br.ReadBits 3
                    else
                        0, 11 + br.ReadBits 7

                if i + repeat > total then
                    raise (InflateFail(InflateError.Malformed "length repeat overruns table"))

                for _ in 1..repeat do
                    lengths[i] <- value
                    i <- i + 1

        if lengths[256] = 0 then
            raise (InflateFail(InflateError.Malformed "dynamic block has no end-of-block code"))

        buildHuffman lengths[0 .. hlit - 1], buildHuffman lengths[hlit..]

    /// Decompress raw DEFLATE `data`. `maxOutput` caps the decoded byte count
    /// (`InflateError.OutputLimit` beyond it — the untrusted-input bomb guard).
    let inflate (maxOutput: int) (data: byte[]) : Result<byte[], InflateError> =
        try
            let br = BitReader(data)
            let out = ResizeArray<byte>()

            let ensureRoom (extra: int) : unit =
                if out.Count + extra > maxOutput then
                    raise (InflateFail(InflateError.OutputLimit maxOutput))

            let inflateBlock (lit: Huffman) (dist: Huffman) : unit =
                let mutable eob = false

                while not eob do
                    let sym = decodeSym br lit

                    if sym < 256 then
                        ensureRoom 1
                        out.Add(byte sym)
                    elif sym = 256 then
                        eob <- true
                    elif sym > 285 then
                        raise (InflateFail(InflateError.Malformed "invalid length symbol"))
                    else
                        let li = sym - 257
                        let len = lengthBase[li] + br.ReadBits lengthExtra[li]
                        let dsym = decodeSym br dist

                        if dsym > 29 then
                            raise (InflateFail(InflateError.Malformed "invalid distance symbol"))

                        let d = distBase[dsym] + br.ReadBits distExtra[dsym]

                        if d > out.Count then
                            raise (InflateFail(InflateError.Malformed "distance reaches before stream start"))

                        ensureRoom len
                        let start = out.Count - d

                        for k in 0 .. len - 1 do
                            out.Add out[start + k]

            let mutable bfinal = false

            while not bfinal do
                bfinal <- br.ReadBits 1 = 1

                match br.ReadBits 2 with
                | 0 ->
                    // Stored block: align, LEN + one's-complement NLEN, raw copy.
                    let l0 = br.AlignedByte()
                    let l1 = br.AlignedByte()
                    let n0 = br.AlignedByte()
                    let n1 = br.AlignedByte()
                    let len = l0 ||| (l1 <<< 8)

                    if (len ^^^ 0xFFFF) <> (n0 ||| (n1 <<< 8)) then
                        raise (InflateFail(InflateError.Malformed "stored block length check failed"))

                    ensureRoom len

                    for _ in 1..len do
                        out.Add(byte (br.AlignedByte()))
                | 1 -> inflateBlock fixedLit fixedDist
                | 2 ->
                    let lit, dist = readDynamicTables br
                    inflateBlock lit dist
                | _ -> raise (InflateFail(InflateError.Malformed "reserved block type"))

            Ok(out.ToArray())
        with InflateFail e ->
            Error e
