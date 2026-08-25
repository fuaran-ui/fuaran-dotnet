module IdsParityProbe.Program

// The cross-pipeline VALUE probe for `Ids.deterministicCorrelationId` (Phase 960). Prints the id
// over a fixed corpus, one line per input, in a form that is byte-comparable between the .NET run
// and the Fable-under-node run: `./run-parity-probe.ps1` runs it both ways and diffs. Identical
// output is the claim neither the compile gate nor the .NET suite can make.
//
// The corpus is INDEXED rather than echoed, so the comparison never turns on console encoding —
// the two runtimes do not agree about how to write a lone surrogate or U+FFFF to a terminal, and a
// probe that reported that difference as a hash divergence would be worse than no probe. It spans
// the cases that separate the two pipelines' arithmetic: empty and single characters, multi-byte
// UTF-8 classes, a surrogate pair, every length 0..80 (short ASCII seeds can accidentally agree,
// so no carry pattern is missed), and realistic node-id-shaped seeds.

let corpus: string list =
    [ ""
      "a"
      "b"
      "c"
      "ab"
      "abc"
      "abcd"
      "foobar"
      "node-42|metric"
      "node-42|progress"
      "The quick brown fox jumps over the lazy dog"
      "0"
      "1"
      "9"
      " "
      // NUL, built rather than written: a raw NUL byte in the source makes git classify this
      // whole file as binary, which silently disables end-of-line normalisation for it.
      string (char 0)
      "ÿ"
      "café"
      "日本語"
      "😀" // U+1F600 as a surrogate pair
      "👩‍💻" // ZWJ sequence
      "�"
      "￿"
      "smoke" ]
    @ [ for n in 0..80 -> String.replicate n "a" ]
    @ [ for n in [ 1; 2; 3; 17; 31; 64; 128; 256 ] -> String.replicate n "xy" ]

[<EntryPoint>]
let main _ =
    corpus
    |> List.iteri (fun i s -> printfn "%03d %s" i (Fuaran.UI.Renderer.Ids.deterministicCorrelationId s))

    0
