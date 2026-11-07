module FableLaws.AttestationLaw

// ============================================================================
//  Law 10 (Phase 2570) — a page signed on a server verifies in the browser.
//
//  The specification's `attestation/page-corpus.json` holds pages the .NET
//  ECDSA P-256 provider signed, beside the refusals a verifier must name: no
//  key, a foreign key, drift, a moved tree, a forged signature, an impostor key
//  and a key on another curve. Each vector is verified through the PUBLISHED
//  provider — `UiWitness.ecdsaP256`, which is the BCL on .NET and WebCrypto
//  under Fable — and its verdict printed. Every verdict is held to the vector's
//  `expect`, and a disagreement is a violation on either pipeline; the runner's
//  line comparison is the cross-pipeline claim, so the two providers must also
//  agree with each other about every refusal's detail.
//
//  Asynchronous, as the provider is: WebCrypto's verify is a promise. So the
//  law answers an `Async`, which `Program.fs` runs to completion on .NET and
//  starts under Node, printing the TOTAL line only once this law has answered.
//
//  The corpus is read from the specification clone: `FUARAN_WIRE_FIXTURES`
//  when it is set (it must then hold `manifest.json`), else the first
//  `wire-format-fixtures/` found climbing from the working directory. An absent
//  corpus is a violation, never a skip.
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.OpStream.Abstractions

type AttestationLaw = { Lines: string list; Violations: int }

#if FABLE_COMPILER
open Fable.Core

[<Import("readFileSync", "fs")>]
let private readFileSync (path: string, encoding: string) : string = jsNative

[<Import("existsSync", "fs")>]
let private existsSync (path: string) : bool = jsNative

[<Import("dirname", "path")>]
let private dirname (path: string) : string = jsNative

[<Import("join", "path")>]
let private joinPath (a: string, b: string) : string = jsNative

/// `""` when unset: an absent variable is `undefined`, which no F# `null` pattern matches.
[<Emit("(process.env.FUARAN_WIRE_FIXTURES ?? '')")>]
let private fixturesVariable () : string = jsNative

[<Emit("process.cwd()")>]
let private workingDirectory () : string = jsNative

let private read (path: string) = readFileSync (path, "utf8")
let private exists (path: string) = existsSync path

let private parentOf (path: string) : string option =
    let p = dirname path in if p = path then None else Some p

let private combine (a: string) (b: string) = joinPath (a, b)
#else
let private read (path: string) = System.IO.File.ReadAllText path
let private exists (path: string) = System.IO.File.Exists path

let private parentOf (path: string) : string option =
    match System.IO.Path.GetDirectoryName path with
    | null -> None
    | p when p = path -> None
    | p -> Some p

let private combine (a: string) (b: string) = System.IO.Path.Combine(a, b)

let private fixturesVariable () : string =
    System.Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES"

let private workingDirectory () : string =
    System.IO.Directory.GetCurrentDirectory()
#endif

/// The corpus root, or why there is none.
let private corpusRoot () : Result<string, string> =
    match fixturesVariable () with
    | null -> None
    | raw when raw.Trim() = "" -> None
    | raw -> Some(raw.Trim())
    |> function
        | Some root when exists (combine root "manifest.json") -> Ok root
        | Some _ -> Error "FUARAN_WIRE_FIXTURES names no corpus"
        | None ->
            let rec climb (dir: string) (depth: int) =
                let candidate = combine dir "wire-format-fixtures"

                if exists (combine candidate "manifest.json") then
                    Ok candidate
                elif depth >= 12 then
                    Error "no wire-format-fixtures found above the working directory"
                else
                    match parentOf dir with
                    | Some up -> climb up (depth + 1)
                    | None -> Error "no wire-format-fixtures found above the working directory"

            climb (workingDirectory ()) 0

let private str (name: string) (fields: (string * JVal) list) : string option =
    fields
    |> List.tryPick (fun (k, v) ->
        match v with
        | JStr s when k = name -> Some s
        | _ -> None)

let private keyOf (v: JVal) : (string * KeyDirectoryEntry) option =
    match v with
    | JObj fields ->
        match str "name" fields, str "keyId" fields, str "algorithm" fields, str "publicKeySpki" fields with
        | Some name, Some keyId, Some algorithm, Some spki ->
            Some(
                name,
                { KeyId = keyId
                  Algorithm = algorithm
                  PublicKeySpki = spki
                  NotBefore = None
                  Expires = None
                  RevokedFrom = None }
            )
        | _ -> None
    | _ -> None

/// The verdict and the detail both providers must agree on: the refusal's name,
/// and for drift the excess, for a bad signature whether the tree moved.
let private verdictOf (result: Result<VerifiedPage, PageVerifyRefusal>) : string * string =
    match result with
    | Ok verified -> "Verified", "key=" + verified.KeyId
    | Error(PageVerifyRefusal.Drift drift) ->
        "Drift",
        "excess="
        + DemandedPage.encode drift.Excess
        + " shortfall="
        + DemandedPage.encode drift.Shortfall
    | Error(PageVerifyRefusal.BadSignature(carried, recomputed, _, _)) ->
        "BadSignature",
        (if carried = recomputed then
             "tree=unmoved"
         else
             "tree=moved")
    | Error(PageVerifyRefusal.ForeignKey(signedBy, offered)) ->
        "ForeignKey", "signed=" + signedBy + " offered=" + offered
    | Error refusal -> SignedPage.refusalName refusal, "-"

let private verifyPage (keys: Map<string, KeyDirectoryEntry>) (page: JVal) : Async<string * int> =
    async {
        match page with
        | JObj fields ->
            let id = defaultArg (str "id" fields) "?"
            let expect = defaultArg (str "expect" fields) "?"
            let offered = str "key" fields |> Option.bind (fun k -> Map.tryFind k keys)

            match
                str "document" fields |> Option.map PageDocument.decode,
                str "signed" fields |> Option.map SignedPage.decode
            with
            | Some(Ok doc), Some(Ok signed) ->
                let! result = SignedPage.verify Fuaran.UI.Program.UiWitness.ecdsaP256 offered doc signed
                let verdict, detail = verdictOf result
                let ok = verdict = expect

                return
                    sprintf
                        "ATTESTATION page %s %s %s%s"
                        id
                        verdict
                        detail
                        (if ok then "" else " VIOLATION expect=" + expect),
                    (if ok then 0 else 1)
            | _ -> return sprintf "ATTESTATION page %s UNREADABLE" id, 1
        | _ -> return "ATTESTATION page UNREADABLE", 1
    }

let attestationLaw () : Async<AttestationLaw> =
    async {
        try
            match corpusRoot () with
            | Error why ->
                return
                    { Lines = [ "ATTESTATION corpus ABSENT " + why ]
                      Violations = 1 }
            | Ok root ->
                match Canonical.read (read (combine (combine root "attestation") "page-corpus.json")) with
                | Ok(JObj fields) ->
                    let memberOf name =
                        fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

                    let keys =
                        match memberOf "keys" with
                        | Some(JArr ks) -> ks |> List.choose keyOf |> Map.ofList
                        | _ -> Map.empty

                    let pages =
                        match memberOf "pages" with
                        | Some(JArr ps) -> ps
                        | _ -> []

                    let mutable results = []

                    for page in pages do
                        let! line = verifyPage keys page
                        results <- results @ [ line ]

                    let violations = results |> List.sumBy snd
                    let empty = if List.isEmpty pages then 1 else 0

                    return
                        { Lines =
                            sprintf "ATTESTATION pages=%d keys=%d" (List.length pages) (Map.count keys)
                            :: (results |> List.map fst)
                          Violations = violations + empty }
                | _ ->
                    return
                        { Lines = [ "ATTESTATION corpus UNREADABLE" ]
                          Violations = 1 }
        with e ->
            return
                { Lines = [ "ATTESTATION FAILED " + TestSupport.sanitise e.Message ]
                  Violations = 1 }
    }
