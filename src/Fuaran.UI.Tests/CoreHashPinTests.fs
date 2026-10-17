module Fuaran.UI.Tests.CoreHashPinTests

// ============================================================================
//  Phase 2309 — one SHA-256: the tier's copy retired onto Core's, and no
//  persisted hash moved.
//
//  Every digest the tier takes now goes through `Fuaran.Core.Hash.sha256Hex`
//  (the chain's through `Fuaran.Core.OpStream.sha256Hash`, the byte meter
//  through `Hash.utf8Bytes`). The values below were RECORDED from the tier's own
//  implementation before it was deleted, and each is asserted against the code
//  that computes it now. They are consumer-side pins of Core's function, the
//  fuaran-core#104 precedent: a Core change that moved any of them would move
//  stored chain heads, checkpoints, teleport strings, attestation digests and
//  memo keys, and it fails here first.
//
//  The text corpus covers the SHA-256 padding boundaries (55/56/63/64/65 bytes),
//  multi-byte UTF-8 and ill-formed UTF-16. The ill-formed strings are built from
//  char values, because an F# literal lone surrogate compiles as U+FFFD — and
//  that equivalence is itself pinned: a lone surrogate digests exactly as U+FFFD
//  does, which is the platform encoder's answer.
// ============================================================================

open System
open System.Text
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

let private chars (cs: int list) : string =
    String(cs |> List.map char |> Array.ofList)

let private bclSha256 (s: string) : string =
    use sha = Security.Cryptography.SHA256.Create()

    sha.ComputeHash(Encoding.UTF8.GetBytes s)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

/// (name, input, digest recorded from the retired implementation).
let private textPins: (string * string * string) list =
    [ "empty", "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
      "abc", "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
      "two-block",
      "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq",
      "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
      "55 bytes", String.replicate 55 "a", "9f4390f8d30c2dd92ec9f095b65e2b9ae9b0a925a5258e241c9f1e910f734318"
      "56 bytes", String.replicate 56 "a", "b35439a4ac6f0948b6d6f9e3c6af0f5f590ce20f1bde7090ef7970686ec6738a"
      "63 bytes", String.replicate 63 "a", "7d3e74a05d7db15bce4ad9ec0658ea98e3f06eeecf16b4c6fff2da457ddc2f34"
      "64 bytes", String.replicate 64 "a", "ffe054fe7ae0cb6dc65c3af9b61d5209f439851db43d0ba5997337df154668eb"
      "65 bytes", String.replicate 65 "a", "635361c48bb9eab14198e76ea8ab7f1a41685d6ad62aa9146d301d4f17eb0ae0"
      "multi-byte UTF-8", "héllo wörld — ünïcôde", "8bf5fb110744ee202744a4335831b6e7254d2f5d8fcfc08b3746469a41afc450"
      "surrogate pair", "emoji 😀 surrogate-pair", "98643215dea53f4fe7ea155c00435743a23ace9895952541b31337f67034fa62"
      "lone high then ASCII", chars [ 0xD800; 0x41 ], "1b6d8a78363cf390895eba876b9fefb99d73506bb40fe31c4efeb2b58f7fe1fc"
      "lone high at the end", chars [ 0x41; 0xDBFF ], "d6f2163ef6b7400f2e7c67c952d32ca8f3e96cad49340bc2236a09c533cfce28"
      "lone low", chars [ 0xDC00; 0x41 ], "1b6d8a78363cf390895eba876b9fefb99d73506bb40fe31c4efeb2b58f7fe1fc"
      "low then high", chars [ 0xDC00; 0xD800 ], "52793f8dc1d85e409f8c88be99d8b31d58f676246340150f406289e04a11151e"
      "two highs then a low",
      chars [ 0xD800; 0xD800; 0xDC00 ],
      "8ba6d94d87e1b28ccb3d32b2f665db709c7ad2ba30ad6bc58a5e3c6aba1b9557"
      "U+FFFD then ASCII", chars [ 0xFFFD; 0x41 ], "1b6d8a78363cf390895eba876b9fefb99d73506bb40fe31c4efeb2b58f7fe1fc" ]

let private tree: Node<unit> = Fuaran.markdown "m1" "hello"

let private chainHead () : string =
    let ts = DateTimeOffset.FromUnixTimeSeconds 1700000000L

    let ops: TreeOp<unit> list =
        [ TreeOp.InsertChild(NodeId "root", Fuaran.markdown "a" "one")
          TreeOp.InsertChild(NodeId "root", Fuaran.markdown "b" "two")
          TreeOp.RemoveNode(NodeId "a") ]

    ops
    |> List.indexed
    |> List.fold
        (fun prev (i, op) -> HashChain.computeHash prev op (i + 1) ts (Human "u1") (Some "p1") OpResultEnvelope.Success)
        HashChain.genesisPreviousHash

let private teleport (bundle: TeleportBundle<unit>) : string =
    match Teleport.encode bundle with
    | Ok s -> s
    | Error e -> failtestf "teleport encode refused: %A" e

/// (artefact, how it is computed now, value recorded from the retired implementation).
let private artefactPins: (string * (unit -> string) * string) list =
    [ "StreamEntry.hashFn",
      (fun () -> StreamEntry.hashFn HashChain.genesisPreviousHash "{\"seq\":0}"),
      "85cc11ffd7dbdf7b144134e92b08bacb4cddab62d2369b6d1a941e5e17417082"
      "StreamEntry.hashFn over a lone surrogate",
      (fun () -> StreamEntry.hashFn HashChain.genesisPreviousHash (chars [ 0x61; 0xD800; 0x62 ])),
      "449ce0a4b917c93e2a1e7acb5230a6113ceb78ce87699888a1dfce0bb381dd65"
      "a three-record stream head (HashChain.computeHash)",
      chainHead,
      "9f8710b8d7296c882092e5080e0a29c8ada3b20eebc0d520178378b19855d1e0"
      "a checkpoint snapshot hash (HashChain.snapshotHash)",
      (fun () -> HashChain.snapshotHash HashChain.genesisPreviousHash 3 (CanonicalJson.encodeNode tree)),
      "7c9cbc21b040b6577dcae2714daa9ed390ccfdd04e2f9717788a68f65e3eec99"
      "a teleport string (tree only)",
      (fun () -> teleport (TeleportBundle.ofTree tree)),
      "FT1.q1ZKKs1LyUlVslIqSc1JLcgvKnEwVNJRSslMTy0uAYqmJRqZmacmplgapRpbWqYYpFkmJqdaWqaaGBmYm1mYGVomG5inJVokGaeaGCQam6WYpCaaJ6YlJyYmJVoYGqQlAc0qKUoFml-tlJkCNC8XZHp2Zh6QXa2kUlJZALLaN7EoOyW_PA-kOLUCZG1Gak5OvlJtbS0A"
      "a teleport string bound to a chain head",
      (fun () ->
          teleport
              { TeleportBundle.ofTree tree with
                  ChainHead = Some(chainHead ()) }),
      "FT1.q1ZKKs1LyUlVslIqSc1JLcgvKnEwVNJRSs5IzMzzSE1MAUpYplmYGxokWaSYG1maJVtYGBlYGqWaGlgYpBokGlkmWySmJBonGRmkpiYlG6SYGhkYmlsYm1skGVpamJqmGKYaAI1LyUxPLS4BmmWWbGhsmmZsCTQiydI42dw42STZHGiQaYq5pYVRarKFuXmiZZJ5UpKBsalZUlKKmYGloaGZsUmSsXFSokmKEdCskqJUoHOrlTJBbssFOTY7Mw_IrlZSKaksAPnEN7EoOyW_PA-kOLUCZG1Gak5OvlJtbS0A"
      "a document attestation digest (DocumentDescriptor.ofTree)",
      (fun () -> (DocumentDescriptor.ofTree "sha256" tree).Digest),
      "db0fa5109bc4e7206039b188d4cb19b3fbd9d207ca424e38023efcc81e626c90"
      "a fragment memo key (FragmentKey.structuralOf)",
      (fun () -> Fuaran.UI.Memo.FragmentKey.structuralOf "body-1" "ref-1" (Map.ofList [ "content", tree ])),
      "3ba18c6e3f58a777ef3d1c5e4584208b5a7ed2d8b5e24e7a3bb706a0feb54d6b"
      "a Custom body-shape hash",
      (fun () -> Hashing.customBodyShapeHash "sample" "sparkline" [ "points"; "label" ] [ "spark-line" ]),
      "89c9a1720ff8b6c141ac23726eddaf2a2916d351bffffac81fa2df4f84c1c49b"
      "a Custom body-shape hash over a lone surrogate",
      (fun () -> Hashing.customBodyShapeHash (chars [ 0x6D; 0xDC00 ]) "c" [ "x" ] []),
      "5d338b2a1791f3c7857e60b5953b59e76c86f48ab723847b3e479ee003096256" ]

[<Tests>]
let tests =
    testList
        "Phase 2309 — one SHA-256, no persisted hash moved"
        [ test "Core's SHA-256 reproduces every digest the retired implementation produced" {
              for name, input, recorded in textPins do
                  Expect.equal (Fuaran.Core.Hash.sha256Hex input) recorded (sprintf "recorded digest of %s" name)
                  Expect.equal (bclSha256 input) recorded (sprintf "BCL digest of %s" name)
          }

          test "a lone surrogate digests as U+FFFD, the platform encoder's answer" {
              Expect.equal
                  (Fuaran.Core.Hash.sha256Hex (chars [ 0xD800; 0x41 ]))
                  (Fuaran.Core.Hash.sha256Hex (chars [ 0xFFFD; 0x41 ]))
                  "the unguarded digest shares a pre-image with the replacement character"
          }

          test "every hashed artefact is byte-identical to its recorded value" {
              for name, compute, recorded in artefactPins do
                  Expect.equal (compute ()) recorded name
          }

          test "the chain HashFn is Core's named SHA-256 chain hash" {
              for prev, payload in [ HashChain.genesisPreviousHash, "{}"; "abc", chars [ 0xDBFF ]; "", "" ] do
                  Expect.equal
                      (StreamEntry.hashFn prev payload)
                      (Fuaran.Core.OpStream.sha256Hash prev payload)
                      "StreamEntry.hashFn is OpStream.sha256Hash"

                  Expect.equal
                      (StreamEntry.hashFn prev payload)
                      (Fuaran.Core.Hash.sha256Hex (prev + "|" + payload))
                      "and both are sha256(prev | payload)"
          } ]
