(*
   WireDecode — an F* model of Fuaran.Core's wire DECODE combinators, with decoder totality as a
   machine-checked theorem (fuaran-core Phase 135 — the attested-stack programme's theorem 1).

   WHAT IS MODELLED. `Fuaran.Core.Decode` (src/Fuaran.Core.Wire/Wire.fs), clause for clause:
   `getProp`, `asString` / `asInt` / `asBool` / `asFloat`, `kindOf`, `strField` / `intField`, and
   `mapList` — the array walker, in its accumulator-and-`rev` form, which is what production is.
   On top of them a REFERENCE VOCABULARY (`rnode`) with its encoder and the kind-dispatch node
   decoder a domain writes: read the `"kind"` tag, match it, read the declared members, walk the
   child array. And, separately, the Phase 102 read policy — see the boundary note below.

   WHAT IS PROVED.
     - `decode_total` — every combinator returns `Ok` or `Error` for EVERY input, never diverges
       and never throws, and WHICH outcome it returns is characterised structurally, so the
       failure classification is exhaustive rather than merely non-empty.
     - `decode_node_total` / `decode_node_wf` — the recursive node decoder is `Tot` on an
       arbitrary `jval` (its termination is the content: see the measure note below), and it
       succeeds on exactly the well-formed documents, `wf`.
     - `decode_encode_roundtrip` — `decode_node (encode n) == Ok n` for every `rnode`.
     - `lenient_agrees_off_policy` / `strict_unchanged_on_null_free` — the Phase 102 promise.

   WHAT IS NOT MODELLED, AND WHY — the theorem's boundary.

     - `Json.parse`, the string-to-`JVal` parser, is OUT OF SCOPE. Its totality is a property of a
       recursive-descent parser over bytes — the depth bound, escape handling, the int53 token
       guard, `MaxDepthExceeded` and `TrailingCharacters` — and is a separate phase. Everything
       here begins at a `JVal` that already exists. `Decode.parse` / `Decode.parseTolerantOfNull`
       are one-line delegates to it and are not modelled either.

     - THE PHASE 102 POLICY IS NOT A COMBINATOR PARAMETER, because in the shipped tree it is not
       one. `NullPolicy` is a parameter of `Json.parseDetailedWithPolicy` and of nothing else, and
       `JVal` has no null constructor, so no combinator can see a null. What the policy IS, at the
       layer this theorem is about, is a DOCUMENT-LEVEL READ NORMALISATION upstream of every
       combinator: erase object-member nulls, refuse a null that has no absence to erase it to.
       Section 7 models it there — `read : null_policy -> jvaln -> outcome jval`, from a document
       model that HAS a null into the wire model that does not, which is the type-level form of
       "tolerance is a read normalisation, never a new emission" (Wire.fs, `NullPolicy`'s doc).
       What section 7 therefore assumes, and states rather than hides, is that the parser's
       member-null absorption is equivalent to erasing member nulls from the document tree the
       strict grammar would otherwise produce. The near-miss tokens that make that an assumption
       rather than a theorem (`nul`, `nullish`) are grammar, and stay with `Json.parse`.

     - `Versioning.decodeTolerant` — the shipped GENERIC instance of the kind-dispatch pattern —
       is named here and not modelled: its `requiredProfile` read goes through
       `Versioning.Profile.tryParse`, string-splitting at a different layer. The pattern itself is
       modelled where a domain meets it, as `decode_node`.

     - THE NUMERIC PAYLOADS ARE OPAQUE. `jval` is parametric in `num` and `flt`, and `as_float`
       takes the widening `to_flt` as a parameter where F# writes `float i`. This is not a
       weakening: no combinator in `Decode` looks inside a number, it only moves one, so opaque
       carriers are the precise statement of what the layer does. It also keeps the extracted
       oracle dependent on `Prims` alone — F* has no F#-extractable float, and F*'s `int` is
       unbounded where .NET's is Int32.

   HOW TO READ IT. Every definition names its F# counterpart in the comment above it. Error
   MESSAGES are reproduced verbatim, not merely classified, because the differential host compares
   them: a model that agreed on `Ok`/`Error` alone would not notice a decoder that named the wrong
   expectation. Helpers are defined here rather than taken from `FStar.List.Tot` so that the
   extracted oracle depends on `Prims` alone.

   A NOTE ON THE MEASURE, because it is the theorem rather than an implementation detail. The node
   decoder recurses into a child array it obtained by NAME, so nothing structural is visible at the
   call site: `get_prop` therefore carries `Ok? r ==> jsize (Ok?.v r) < jsize el` in its RETURN
   TYPE, and that refinement is the whole termination argument. A `jval` is finite and a decoder
   that only ever descends into it cannot fail to stop — which is what "the decoder is total"
   means once the parser is out of scope.

   Apache-2.0, like everything beside it.
*)
module WireDecode

(* ======================================================================================
   0. The outcome (F#: `Result<'T, string>`, which every combinator in `Decode` returns —
      "so a failure NAMES what was expected", Wire.fs).
   ====================================================================================== *)

type outcome (a: Type) =
  | Ok    : v:a -> outcome a
  | Error : msg:string -> outcome a

(* F#: `Result.bind`, the `|>` the combinators are composed with. *)
let bind (#a #b: Type) (r: outcome a) (f: a -> outcome b) : Tot (outcome b) =
  match r with
  | Ok v -> f v
  | Error m -> Error m

(* ---- list helpers, self-contained so the extraction needs only `Prims` ---- *)

let rec rev_app (#a: Type) (l acc: list a) : Tot (list a) (decreases l) =
  match l with
  | [] -> acc
  | x :: t -> rev_app t (x :: acc)

(* F#: `List.rev` — the one `mapList` calls on its accumulator. *)
let rev (#a: Type) (l: list a) : Tot (list a) = rev_app l []

(* ======================================================================================
   1. The value model (F#: `JVal` in Wire.fs).

      Parametric in the two numeric carriers: see the header's note on opacity.
   ====================================================================================== *)

type jval (num flt: eqtype) =
  | JStr   : s:string -> jval num flt
  | JInt   : i:num -> jval num flt
  | JBool  : b:bool -> jval num flt
  | JFloat : f:flt -> jval num flt
  | JArr   : items:list (jval num flt) -> jval num flt
  | JObj   : fields:list (string & jval num flt) -> jval num flt

(* ======================================================================================
   2. The structural measure. PROOF-ONLY — erased at extraction, but the reason the
      recursive decoder below is accepted as `Tot` at all.
   ====================================================================================== *)

[@@ noextract_to "FSharp"]
let rec jsize (#num #flt: eqtype) (v: jval num flt) : Tot pos =
  match v with
  | JArr xs -> 1 + jsizes xs
  | JObj fs -> 1 + fsize fs
  | _ -> 1

and jsizes (#num #flt: eqtype) (xs: list (jval num flt)) : Tot nat =
  match xs with
  | [] -> 0
  | x :: t -> jsize x + jsizes t

and fsize (#num #flt: eqtype) (fs: list (string & jval num flt)) : Tot nat =
  match fs with
  | [] -> 0
  | (_, v) :: t -> jsize v + fsize t

(* The SMT solver reduces these on demand; the patterns spare every termination VC below the
   need to be told. Proof-only, like the measure itself. *)
[@@ noextract_to "FSharp"]
let jsize_at_least_one (#num #flt: eqtype) (v: jval num flt)
  : Lemma (ensures jsize v >= 1) [SMTPat (jsize v)] =
  match v with
  | JStr _ -> () | JInt _ -> () | JBool _ -> ()
  | JFloat _ -> () | JArr _ -> () | JObj _ -> ()

[@@ noextract_to "FSharp"]
let jsizes_cons (#num #flt: eqtype) (x: jval num flt) (t: list (jval num flt))
  : Lemma (ensures jsizes (x :: t) == jsize x + jsizes t) [SMTPat (jsizes (x :: t))] = ()

[@@ noextract_to "FSharp"]
let fsize_cons (#num #flt: eqtype) (k: string) (v: jval num flt) (t: list (string & jval num flt))
  : Lemma (ensures fsize ((k, v) :: t) == jsize v + fsize t) [SMTPat (fsize ((k, v) :: t))] = ()

(* ======================================================================================
   3. The combinators (F#: `module Decode`, Wire.fs), clause for clause.
   ====================================================================================== *)

(* F#: `Decode.kindName` — the word a failure names the actual shape with. *)
let kind_name (#num #flt: eqtype) (v: jval num flt) : Tot string =
  match v with
  | JStr _ -> "string"
  | JInt _ -> "int"
  | JBool _ -> "bool"
  | JFloat _ -> "float"
  | JArr _ -> "array"
  | JObj _ -> "object"

(* F#: the `List.tryFind` inside `getProp`, and its `None` arm's message. The refinement is
   what carries the subterm fact out of the lookup — see the header's note on the measure. *)
let rec find_field (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Tot (r: outcome (jval num flt) { Ok? r ==> jsize (Ok?.v r) <= fsize fs }) (decreases fs) =
  match fs with
  | [] -> Error ("missing property: " ^ name)
  | (k, v) :: t -> if k = name then Ok v else find_field name t

(* F#: `Decode.getProp`. *)
let get_prop (#num #flt: eqtype) (name: string) (el: jval num flt)
  : Tot (r: outcome (jval num flt) { Ok? r ==> jsize (Ok?.v r) < jsize el }) =
  match el with
  | JObj fields -> find_field name fields
  | other -> Error ("expected object, got " ^ kind_name other)

(* F#: `Decode.asString`. *)
let as_string (#num #flt: eqtype) (el: jval num flt) : Tot (outcome string) =
  match el with
  | JStr s -> Ok s
  | other -> Error ("expected string, got " ^ kind_name other)

(* F#: `Decode.asInt`. *)
let as_int (#num #flt: eqtype) (el: jval num flt) : Tot (outcome num) =
  match el with
  | JInt i -> Ok i
  | other -> Error ("expected int, got " ^ kind_name other)

(* F#: `Decode.asBool`. *)
let as_bool (#num #flt: eqtype) (el: jval num flt) : Tot (outcome bool) =
  match el with
  | JBool b -> Ok b
  | other -> Error ("expected bool, got " ^ kind_name other)

(* F#: `Decode.asFloat` — the numeric-normalisation clause. `to_flt` is F#'s `float i`; the
   model does not look inside a number (header, opacity). *)
let as_float (#num #flt: eqtype) (to_flt: num -> flt) (el: jval num flt) : Tot (outcome flt) =
  match el with
  | JFloat f -> Ok f
  | JInt i -> Ok (to_flt i)
  | other -> Error ("expected number, got " ^ kind_name other)

(* F#: `Decode.kindOf` — the discriminating `"kind"` tag of an object. *)
let kind_of (#num #flt: eqtype) (el: jval num flt) : Tot (outcome string) =
  bind (get_prop "kind" el) as_string

(* F#: `Decode.strField`. *)
let str_field (#num #flt: eqtype) (name: string) (el: jval num flt) : Tot (outcome string) =
  bind (get_prop name el) as_string

(* F#: `Decode.intField`. *)
let int_field (#num #flt: eqtype) (name: string) (el: jval num flt) : Tot (outcome num) =
  bind (get_prop name el) as_int

(* F#: the inner `go` of `Decode.mapList` — accumulate, short-circuit on the first error,
   `List.rev` at the end. Modelled in that form because that is the function production is. *)
let rec map_list_go (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (acc: list t) (xs: list (jval num flt))
  : Tot (outcome (list t)) (decreases xs) =
  match xs with
  | [] -> Ok (rev acc)
  | x :: rest ->
    (match d x with
     | Ok v -> map_list_go d (v :: acc) rest
     | Error m -> Error m)

(* F#: `Decode.mapList` — the array walker, and the only walker `Decode` has. *)
let map_list (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (el: jval num flt)
  : Tot (outcome (list t)) =
  match el with
  | JArr items -> map_list_go d [] items
  | other -> Error ("expected array, got " ^ kind_name other)

(* ======================================================================================
   4. A reference vocabulary and the kind-dispatch node decoder a domain builds from the
      combinators above (F#: the `kindObj` envelope of `Json.kindObj`, read back through
      `kindOf` + a match on the tag).

      One case per combinator: `RText` for `str_field`, `RFlag` for `get_prop` + `as_bool`,
      `RTags` for `map_list as_string` (the generic walker itself), `RGroup` for the
      recursive walk.
   ====================================================================================== *)

type rnode =
  | RText  : value:string -> rnode
  | RFlag  : on:bool -> rnode
  | RTags  : tags:list string -> rnode
  | RGroup : id:string -> items:list rnode -> rnode

(* The reference vocabulary's own measure — proof-only, and here for the same reason as
   `jsize`: it makes the two mutually-recursive round-trip lemmas below compare like with
   like instead of an `rnode` with a `list rnode`. *)
[@@ noextract_to "FSharp"]
let rec rsize (n: rnode) : Tot pos =
  match n with
  | RGroup _ items -> 1 + rsizes items
  | _ -> 1

and rsizes (ns: list rnode) : Tot nat =
  match ns with
  | [] -> 0
  | n :: t -> rsize n + rsizes t

[@@ noextract_to "FSharp"]
let rsize_at_least_one (n: rnode) : Lemma (ensures rsize n >= 1) [SMTPat (rsize n)] =
  match n with
  | RText _ -> () | RFlag _ -> () | RTags _ -> () | RGroup _ _ -> ()

[@@ noextract_to "FSharp"]
let rsizes_cons (n: rnode) (t: list rnode)
  : Lemma (ensures rsizes (n :: t) == rsize n + rsizes t) [SMTPat (rsizes (n :: t))] = ()

(* F#: `Json.kindObj tag fields` — the `"kind"` tag leads, the declared members follow in
   author order. *)
let rec encode (#num #flt: eqtype) (n: rnode) : Tot (jval num flt) (decreases n) =
  match n with
  | RText s -> JObj [("kind", JStr "text"); ("value", JStr s)]
  | RFlag b -> JObj [("kind", JStr "flag"); ("on", JBool b)]
  | RTags ts -> JObj [("kind", JStr "tags"); ("tags", JArr (encode_tags ts))]
  | RGroup id items ->
    JObj [("kind", JStr "group"); ("id", JStr id); ("items", JArr (encode_items items))]

and encode_tags (#num #flt: eqtype) (ts: list string) : Tot (list (jval num flt)) (decreases ts) =
  match ts with
  | [] -> []
  | s :: t -> JStr s :: encode_tags t

and encode_items (#num #flt: eqtype) (ns: list rnode) : Tot (list (jval num flt)) (decreases ns) =
  match ns with
  | [] -> []
  | n :: t -> encode n :: encode_items t

(* The node decoder. The `group` arm is written with explicit matches rather than `bind`
   because the refinement on `get_prop` — the termination argument — is not visible through a
   higher-order call, and `decode_items` is `map_list_go decode_node` INLINED for the same
   reason; `map_list_is_items` below proves the inlining costs no fidelity. *)
let rec decode_node (#num #flt: eqtype) (el: jval num flt)
  : Tot (outcome rnode) (decreases %[(jsize el <: nat); 0]) =
  match kind_of el with
  | Error m -> Error m
  | Ok tag ->
    if tag = "text" then
      (match str_field "value" el with
       | Error m -> Error m
       | Ok s -> Ok (RText s))
    else if tag = "flag" then
      (match get_prop "on" el with
       | Error m -> Error m
       | Ok v ->
         (match as_bool v with
          | Error m -> Error m
          | Ok b -> Ok (RFlag b)))
    else if tag = "tags" then
      (match get_prop "tags" el with
       | Error m -> Error m
       | Ok v ->
         (match map_list as_string v with
          | Error m -> Error m
          | Ok ts -> Ok (RTags ts)))
    else if tag = "group" then
      (match str_field "id" el with
       | Error m -> Error m
       | Ok id ->
         let r = get_prop "items" el in
         (match r with
          | Error m -> Error m
          | Ok v ->
            (match v with
             | JArr ys -> (match decode_items [] ys with
                           | Error m -> Error m
                           | Ok ns -> Ok (RGroup id ns))
             | other -> Error ("expected array, got " ^ kind_name other))))
    else Error ("unknown kind: " ^ tag)

and decode_items (#num #flt: eqtype) (acc: list rnode) (ys: list (jval num flt))
  : Tot (outcome (list rnode)) (decreases %[(jsizes ys <: nat); 1]) =
  match ys with
  | [] -> Ok (rev acc)
  | y :: rest ->
    (match decode_node y with
     | Ok n -> decode_items (n :: acc) rest
     | Error m -> Error m)

(* The inlined walk IS `mapList decodeNode`. *)
let rec map_list_is_items (#num #flt: eqtype) (acc: list rnode) (ys: list (jval num flt))
  : Lemma (ensures map_list_go (decode_node #num #flt) acc ys == decode_items acc ys)
          (decreases ys) =
  match ys with
  | [] -> ()
  | y :: rest ->
    (match decode_node y with
     | Ok n -> map_list_is_items (n :: acc) rest
     | Error _ -> ())

let map_list_decode_node (#num #flt: eqtype) (ys: list (jval num flt))
  : Lemma (ensures map_list (decode_node #num #flt) (JArr ys) == decode_items [] ys) =
  map_list_is_items [] ys

(* ======================================================================================
   5. THEOREM 1 — TOTALITY, and the exhaustive classification of the outcome.

      Totality of each combinator is carried by its `Tot` type: F* admits a definition only
      once it has shown the function is defined on every input of its domain and terminates,
      and `--report_assumes error` means nothing here is assumed. What a lemma adds is the
      CLASSIFICATION: which of the two outcomes each input reaches, decided structurally, so
      that "the failure classification is exhaustive" is a checked statement rather than an
      observation about a match having a catch-all arm.
   ====================================================================================== *)

(* Every combinator returns exactly one of `Ok` / `Error` — the two are exclusive and
   exhaustive — and which one is a structural property of the input. *)
let decode_total (#num #flt: eqtype) (to_flt: num -> flt) (name: string) (el: jval num flt)
  : Lemma (ensures
      (* exactly one outcome, per combinator *)
      (Ok? (as_string el) <==> ~(Error? (as_string el))) /\
      (Ok? (as_int el) <==> ~(Error? (as_int el))) /\
      (Ok? (as_bool el) <==> ~(Error? (as_bool el))) /\
      (Ok? (as_float to_flt el) <==> ~(Error? (as_float to_flt el))) /\
      (Ok? (get_prop name el) <==> ~(Error? (get_prop name el))) /\
      (* and WHICH one, structurally — the classification is exhaustive *)
      (Ok? (as_string el) <==> JStr? el) /\
      (Ok? (as_int el) <==> JInt? el) /\
      (Ok? (as_bool el) <==> JBool? el) /\
      (Ok? (as_float to_flt el) <==> (JFloat? el \/ JInt? el)) /\
      (Ok? (get_prop name el) <==> (JObj? el /\ Ok? (find_field name (JObj?.fields el)))) /\
      (Ok? (kind_of el) <==> (JObj? el /\ Ok? (find_field "kind" (JObj?.fields el)) /\
                              JStr? (Ok?.v (find_field "kind" (JObj?.fields el))))) /\
      (Ok? (str_field name el) <==> (JObj? el /\ Ok? (find_field name (JObj?.fields el)) /\
                                     JStr? (Ok?.v (find_field name (JObj?.fields el))))) /\
      (Ok? (int_field name el) <==> (JObj? el /\ Ok? (find_field name (JObj?.fields el)) /\
                                     JInt? (Ok?.v (find_field name (JObj?.fields el))))))
  = ()

(* The walker's classification: it succeeds exactly when the value is an array and every
   element decodes. `all_ok` is the model's reading of "short-circuits on the first error". *)
let rec all_ok (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (xs: list (jval num flt)) : Tot bool (decreases xs) =
  match xs with
  | [] -> true
  | x :: rest -> Ok? (d x) && all_ok d rest

let rec map_list_go_total (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (acc: list t) (xs: list (jval num flt))
  : Lemma (ensures Ok? (map_list_go d acc xs) == all_ok d xs) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
    (match d x with
     | Ok v -> map_list_go_total d (v :: acc) rest
     | Error _ -> ())

let map_list_total (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t) (el: jval num flt)
  : Lemma (ensures Ok? (map_list d el) == (JArr? el && all_ok d (JArr?.items el)))
  = match el with
    | JArr items -> map_list_go_total d [] items
    | _ -> ()

(* ---- the recursive decoder ---- *)

(* The documents `decode_node` accepts, as a structural predicate. It mentions only the
   NON-recursive combinators' success plus its own recursion, so it is a statement about the
   document's shape rather than a second copy of the decoder. *)
[@@ noextract_to "FSharp"]
let rec wf (#num #flt: eqtype) (el: jval num flt) : Tot bool (decreases %[(jsize el <: nat); 0]) =
  match kind_of el with
  | Error _ -> false
  | Ok tag ->
    if tag = "text" then Ok? (str_field "value" el)
    else if tag = "flag" then
      (match get_prop "on" el with
       | Error _ -> false
       | Ok v -> JBool? v)
    else if tag = "tags" then
      (match get_prop "tags" el with
       | Error _ -> false
       | Ok v -> Ok? (map_list (as_string #num #flt) v))
    else if tag = "group" then
      (Ok? (str_field "id" el) &&
       (match get_prop "items" el with
        | Error _ -> false
        | Ok v -> (match v with
                   | JArr ys -> wf_items ys
                   | _ -> false)))
    else false

and wf_items (#num #flt: eqtype) (ys: list (jval num flt))
  : Tot bool (decreases %[(jsizes ys <: nat); 1]) =
  match ys with
  | [] -> true
  | y :: rest -> wf y && wf_items rest

(* TOTALITY, stated: the decoder reaches an outcome on EVERY `jval`, and exactly one. That it
   type-checks at `Tot` is the proof that it terminates — the content of the theorem once
   `Json.parse` is out of scope, because a decoder that descends by NAME into a value it
   looked up has no syntactic guarantee of doing so. *)
let decode_node_total (#num #flt: eqtype) (el: jval num flt)
  : Lemma (ensures (Ok? (decode_node el) \/ Error? (decode_node el)) /\
                   ~(Ok? (decode_node el) /\ Error? (decode_node el)))
  = ()

(* … and the classification is exhaustive: it succeeds on exactly the well-formed documents,
   so every other document reaches a named `Error` and none reaches neither. *)
let rec decode_node_wf (#num #flt: eqtype) (el: jval num flt)
  : Lemma (ensures Ok? (decode_node el) == wf el) (decreases %[(jsize el <: nat); 0]) =
  match kind_of el with
  | Error _ -> ()
  | Ok tag ->
    if tag = "text" then ()
    else if tag = "flag" then ()
    else if tag = "tags" then ()
    else if tag = "group" then
      (match str_field "id" el with
       | Error _ -> ()
       | Ok _ ->
         (match get_prop "items" el with
          | Error _ -> ()
          | Ok v ->
            (match v with
             | JArr ys -> decode_items_wf [] ys
             | _ -> ())))
    else ()

and decode_items_wf (#num #flt: eqtype) (acc: list rnode) (ys: list (jval num flt))
  : Lemma (ensures Ok? (decode_items acc ys) == wf_items ys) (decreases %[(jsizes ys <: nat); 1]) =
  match ys with
  | [] -> ()
  | y :: rest ->
    decode_node_wf y;
    (match decode_node y with
     | Ok n -> decode_items_wf (n :: acc) rest
     | Error _ -> ())

(* ======================================================================================
   6. THE ROUND TRIP — the reference vocabulary's encoder is inverted by the decoder.
   ====================================================================================== *)

(* The walker's accumulator law over a list of encoded strings: `rev_app acc ts` is
   `reverse acc` followed by `ts`, which is what the `rev` at the end of `mapList` produces.
   The inductive step is definitional — `rev_app (s :: acc) t` unfolds to `rev_app acc (s :: t)`
   in one step — so the associativity this shape usually needs does not arise. *)
let rec map_list_encode_tags (#num #flt: eqtype) (acc: list string) (ts: list string)
  : Lemma (ensures map_list_go (as_string #num #flt) acc (encode_tags #num #flt ts) ==
                   Ok (rev_app acc ts))
          (decreases ts) =
  match ts with
  | [] -> ()
  | s :: t -> map_list_encode_tags #num #flt (s :: acc) t

(* THE ROUND TRIP. `encode` is the reference vocabulary's `Json.kindObj` envelope; the decoder
   above inverts it exactly, for every node, at every depth. *)
let rec decode_encode_roundtrip (#num #flt: eqtype) (n: rnode)
  : Lemma (ensures decode_node (encode #num #flt n) == Ok n) (decreases %[(rsize n <: nat); 0]) =
  match n with
  | RText _ -> ()
  | RFlag _ -> ()
  | RTags ts -> map_list_encode_tags #num #flt [] ts
  | RGroup _ items -> decode_encode_items #num #flt [] items

and decode_encode_items (#num #flt: eqtype) (acc: list rnode) (ns: list rnode)
  : Lemma (ensures decode_items acc (encode_items #num #flt ns) == Ok (rev_app acc ns))
          (decreases %[(rsizes ns <: nat); 1]) =
  match ns with
  | [] -> ()
  | n :: t ->
    decode_encode_roundtrip #num #flt n;
    decode_encode_items #num #flt (n :: acc) t

(* ======================================================================================
   7. The Phase 102 read policy — modelled where it lives, one layer above the combinators.

      See the header: `NullPolicy` is a parameter of `Json.parseDetailedWithPolicy` and of
      nothing else, and `JVal` has no null constructor, so a combinator cannot see a null.
      What the policy does to the value the combinators are handed is a normalisation on the
      DOCUMENT, and that is what is modelled: `jvaln` — the foreign document, which has a
      null — read into `jval`, which by its type cannot carry one.
   ====================================================================================== *)

(* F#: `NullPolicy` (Wire.fs). *)
type null_policy =
  | RejectNull
  | EraseMemberNull

(* The foreign document. F#: what the grammar of `Json.parse` accepts, before the policy
   decides what to do with the `null` token. *)
type jvaln (num flt: eqtype) =
  | NStr   : s:string -> jvaln num flt
  | NInt   : i:num -> jvaln num flt
  | NBool  : b:bool -> jvaln num flt
  | NFloat : f:flt -> jvaln num flt
  | NArr   : items:list (jvaln num flt) -> jvaln num flt
  | NObj   : fields:list (string & jvaln num flt) -> jvaln num flt
  | NNull  : jvaln num flt

[@@ noextract_to "FSharp"]
let rec nsize (#num #flt: eqtype) (d: jvaln num flt) : Tot pos =
  match d with
  | NArr xs -> 1 + nsizes xs
  | NObj fs -> 1 + fnsize fs
  | _ -> 1

and nsizes (#num #flt: eqtype) (xs: list (jvaln num flt)) : Tot nat =
  match xs with
  | [] -> 0
  | x :: t -> nsize x + nsizes t

and fnsize (#num #flt: eqtype) (fs: list (string & jvaln num flt)) : Tot nat =
  match fs with
  | [] -> 0
  | (_, v) :: t -> nsize v + fnsize t

[@@ noextract_to "FSharp"]
let nsize_at_least_one (#num #flt: eqtype) (d: jvaln num flt)
  : Lemma (ensures nsize d >= 1) [SMTPat (nsize d)] =
  match d with
  | NStr _ -> () | NInt _ -> () | NBool _ -> () | NFloat _ -> ()
  | NArr _ -> () | NObj _ -> () | NNull -> ()

[@@ noextract_to "FSharp"]
let nsizes_cons (#num #flt: eqtype) (x: jvaln num flt) (t: list (jvaln num flt))
  : Lemma (ensures nsizes (x :: t) == nsize x + nsizes t) [SMTPat (nsizes (x :: t))] = ()

[@@ noextract_to "FSharp"]
let fnsize_cons (#num #flt: eqtype) (k: string) (v: jvaln num flt) (t: list (string & jvaln num flt))
  : Lemma (ensures fnsize ((k, v) :: t) == nsize v + fnsize t) [SMTPat (fnsize ((k, v) :: t))] = ()

(* F#: the two messages `parseValue`'s `'n'` arm raises, verbatim — the tolerant policy names
   a DIFFERENT rejection at a position it declines to erase, deliberately, "since the remedy
   is different" (Wire.fs). Reproducing both is what lets the theorem below be honest about
   which half of the verdict is preserved. *)
let reject_msg: string = "null is not representable in the Fuaran wire JVal model"

let no_absence_msg: string =
  "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"

(* F#: `Json.parseDetailedWithPolicy`, at the tree. The erase fork is the ONE behavioural
   difference between the policies, and it is in member position only. *)
let rec read (#num #flt: eqtype) (p: null_policy) (d: jvaln num flt)
  : Tot (outcome (jval num flt)) (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NNull -> Error (if EraseMemberNull? p then no_absence_msg else reject_msg)
  | NStr s -> Ok (JStr s)
  | NInt i -> Ok (JInt i)
  | NBool b -> Ok (JBool b)
  | NFloat f -> Ok (JFloat f)
  | NArr xs ->
    (match read_items p xs with
     | Error m -> Error m
     | Ok vs -> Ok (JArr vs))
  | NObj fs ->
    (match read_fields p fs with
     | Error m -> Error m
     | Ok kvs -> Ok (JObj kvs))

and read_items (#num #flt: eqtype) (p: null_policy) (xs: list (jvaln num flt))
  : Tot (outcome (list (jval num flt))) (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> Ok []
  | x :: t ->
    (match read p x with
     | Error m -> Error m
     | Ok v ->
       (match read_items p t with
        | Error m -> Error m
        | Ok vs -> Ok (v :: vs)))

and read_fields (#num #flt: eqtype) (p: null_policy) (fs: list (string & jvaln num flt))
  : Tot (outcome (list (string & jval num flt))) (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> Ok []
  | (k, v) :: t ->
    if EraseMemberNull? p && NNull? v then
      read_fields p t
    else
      (match read p v with
       | Error m -> Error m
       | Ok jv ->
         (match read_fields p t with
          | Error m -> Error m
          | Ok r -> Ok ((k, jv) :: r)))

(* The inputs the policy NAMES: a `null` in object-member value position, anywhere in the
   document. Everything else is off-policy. *)
[@@ noextract_to "FSharp"]
let rec has_member_null (#num #flt: eqtype) (d: jvaln num flt) : Tot bool (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NArr xs -> has_member_null_items xs
  | NObj fs -> has_member_null_fields fs
  | _ -> false

and has_member_null_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Tot bool (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> false
  | x :: t -> has_member_null x || has_member_null_items t

and has_member_null_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Tot bool (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> false
  | (_, v) :: t -> NNull? v || has_member_null v || has_member_null_fields t

(* Any `null` at all, in any position. *)
[@@ noextract_to "FSharp"]
let rec has_null (#num #flt: eqtype) (d: jvaln num flt) : Tot bool (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NNull -> true
  | NArr xs -> has_null_items xs
  | NObj fs -> has_null_fields fs
  | _ -> false

and has_null_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Tot bool (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> false
  | x :: t -> has_null x || has_null_items t

and has_null_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Tot bool (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> false
  | (_, v) :: t -> has_null v || has_null_fields t

(* Two outcomes agree: the same value, or both refused. The message is deliberately NOT part
   of this — see `reject_msg` / `no_absence_msg` above, and the README's statement of exactly
   how far the Phase 102 promise reaches. *)
let agree (#a: Type) (o1 o2: outcome a) : prop =
  match o1, o2 with
  | Ok x, Ok y -> x == y
  | Error _, Error _ -> True
  | _, _ -> False

(* THEOREM: on every document the policy does not NAME — no `null` in object-member position
   anywhere — the tolerant reader's verdict is the strict reader's: the same value when both
   accept, and a refusal when either refuses. This is the Phase 102 promise, mechanised, and
   `agree` rather than `==` is where the promise actually stops: at the two positions the
   tolerant policy declines to erase (a bare root `null`, an array element) both readers
   refuse, and the tolerant one says so in different words on purpose. *)
let rec lenient_agrees_off_policy (#num #flt: eqtype) (d: jvaln num flt)
  : Lemma (requires not (has_member_null d))
          (ensures agree (read RejectNull d) (read EraseMemberNull d))
          (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NArr xs -> lenient_agrees_items #num #flt xs
  | NObj fs -> lenient_agrees_fields #num #flt fs
  | _ -> ()

and lenient_agrees_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Lemma (requires not (has_member_null_items xs))
          (ensures agree (read_items RejectNull xs) (read_items EraseMemberNull xs))
          (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t ->
    lenient_agrees_off_policy #num #flt x;
    lenient_agrees_items #num #flt t

and lenient_agrees_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Lemma (requires not (has_member_null_fields fs))
          (ensures agree (read_fields RejectNull fs) (read_fields EraseMemberNull fs))
          (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t ->
    lenient_agrees_off_policy #num #flt v;
    lenient_agrees_fields #num #flt t

(* … and on a document with no `null` at all the two readers are not merely in agreement,
   they are the same function — message included, there being no message. That is the
   sharpest form of "the policy governs exactly one thing". *)
let rec strict_unchanged_on_null_free (#num #flt: eqtype) (d: jvaln num flt)
  : Lemma (requires not (has_null d))
          (ensures read RejectNull d == read EraseMemberNull d)
          (decreases %[(nsize d <: nat); 0]) =
  match d with
  | NArr xs -> null_free_items #num #flt xs
  | NObj fs -> null_free_fields #num #flt fs
  | _ -> ()

and null_free_items (#num #flt: eqtype) (xs: list (jvaln num flt))
  : Lemma (requires not (has_null_items xs))
          (ensures read_items RejectNull xs == read_items EraseMemberNull xs)
          (decreases %[(nsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t ->
    strict_unchanged_on_null_free #num #flt x;
    null_free_items #num #flt t

and null_free_fields (#num #flt: eqtype) (fs: list (string & jvaln num flt))
  : Lemma (requires not (has_null_fields fs))
          (ensures read_fields RejectNull fs == read_fields EraseMemberNull fs)
          (decreases %[(fnsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t ->
    strict_unchanged_on_null_free #num #flt v;
    null_free_fields #num #flt t

(* ======================================================================================
   8. KEY-ORDER INVARIANCE (Phase 152) — WIRE_FORMAT §2 rule 2's obligation on the DECODER,
      and §20's one-answer rule at this layer, as lemmas.

      Rule 2 obliges every decoder to accept an object's members in ANY order; §20 ratifies that
      the same bytes produce the same tree on every conformant host. Both are pinned by `reject`
      and round-trip fixtures, neither was a theorem, and every fixture in the corpus is
      canonically ordered — so a decoder that silently depended on member order would pass all of
      them. What follows makes the obligation a property of the combinators instead.

      THE RELATION. `member_perm` relates two documents that differ only by the ORDER of object
      members, at any depth. Arrays are compared POINTWISE — an array's order is content, not
      presentation, and a model that permuted them too would be proving something false. Scalars
      must be equal. An object's members are matched BY KEY, and the matched values related
      recursively, so the relation is a congruence rather than a shallow list permutation:
      `{"a":{"x":1,"y":2}}` and `{"a":{"y":2,"x":1}}` are related, at any depth.

      THE PREMISE, AND WHY IT IS NECESSARY RATHER THAN CONVENIENT. `getProp` is a `List.tryFind`
      over the member list (Wire.fs), so it answers with the FIRST member of the given name.
      On a list with no repeated key that is order-independent; on one WITH a repeated key it is
      not — and nothing upstream excludes the case, since `Json.parseObject` appends every member
      with no key check and Phase 146's `parse_members` models exactly that accumulation. The
      duplicate-free premise is therefore carried HERE rather than inherited from the parser, and
      `duplicate_keys_break_order_invariance` proves it cannot be dropped: a literal reordering of
      a duplicate-keyed object changes `get_prop`'s answer. Matching by key is where the premise
      lives in this formulation — the relation declines to relate that pair rather than relating
      it and lying.

      BOTH DIRECTIONS ARE PROVED, and the second is the one that keeps the first from being
      vacuous. Forwards: every combinator's invariance holds for EVERY pair the relation relates,
      under no further hypothesis. Backwards: `perm_covers_reorder` shows the relation CONTAINS
      every reordering of a duplicate-free member list whose values are themselves related —
      which is one level of a deep reordering, with `member_perm`'s scalar arms as the base cases,
      so it is the induction step rather than a statement about the root. A relation nothing is
      related BY would satisfy every invariance lemma here and say nothing at all.

      WHAT IS STILL NOT CLAIMED. The relation is not proved transitive or symmetric; nothing below
      needs either, and neither is asserted anywhere. And the theorem is about the DECODE
      combinators — `Canon.render`'s key ordering on the way out is certified by the wire-format
      corpus, not here.
   ====================================================================================== *)

(* Remove the FIRST member of the given name, returning its value and the rest in order. Written
   to agree with `find_field` by construction — `extract_is_find` below discharges that — because
   a relation that matched a different occurrence from the one the decoder reads would prove
   invariance of something production does not do. *)
let rec extract_field (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Tot (option (jval num flt & list (string & jval num flt))) (decreases fs) =
  match fs with
  | [] -> None
  | (k, v) :: t ->
    if k = name then
      Some (v, t)
    else
      (match extract_field name t with
       | None -> None
       | Some (w, rest) -> Some (w, (k, v) :: rest))

(* THE RELATION. See the section header for what each arm is saying and why. *)
let rec member_perm (#num #flt: eqtype) (a b: jval num flt)
  : Tot bool (decreases %[(jsize a <: nat); 0]) =
  match a, b with
  | JStr x, JStr y -> x = y
  | JInt x, JInt y -> x = y
  | JBool x, JBool y -> x = y
  | JFloat x, JFloat y -> x = y
  | JArr xs, JArr ys -> items_perm xs ys
  | JObj fs, JObj gs -> fields_perm fs gs
  | _, _ -> false

and items_perm (#num #flt: eqtype) (xs ys: list (jval num flt))
  : Tot bool (decreases %[(jsizes xs <: nat); 1]) =
  match xs, ys with
  | [], [] -> true
  | x :: xt, y :: yt -> member_perm x y && items_perm xt yt
  | _, _ -> false

and fields_perm (#num #flt: eqtype) (fs gs: list (string & jval num flt))
  : Tot bool (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> Nil? gs
  | (k, v) :: t ->
    (match extract_field k gs with
     | None -> false
     | Some (w, rest) -> member_perm v w && fields_perm t rest)

(* Two outcomes of a combinator that RETURNS a value: the values related, or the same refusal.
   `==` would be wrong here and nowhere else in the section — `get_prop` hands back a subtree,
   which is itself permuted. Every other combinator returns a string, a number, a bool or a list
   of strings, and for those the section proves outright EQUALITY. *)
let outcome_perm (#num #flt: eqtype) (r1 r2: outcome (jval num flt)) : Tot bool =
  match r1, r2 with
  | Ok v1, Ok v2 -> member_perm v1 v2
  | Error m1, Error m2 -> m1 = m2
  | _, _ -> false

(* ---- duplicate-free member lists, and a literal reordering ---- *)

let rec has_key (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Tot bool (decreases fs) =
  match fs with
  | [] -> false
  | (k, _) :: t -> k = name || has_key name t

let rec keys_unique (#num #flt: eqtype) (fs: list (string & jval num flt))
  : Tot bool (decreases fs) =
  match fs with
  | [] -> true
  | (k, _) :: t -> not (has_key k t) && keys_unique t

(* Duplicate-freeness at EVERY depth — what the host filters a corpus document on before it
   shuffles it, using this very definition rather than a second one written in F#. *)
let rec keys_unique_deep (#num #flt: eqtype) (a: jval num flt)
  : Tot bool (decreases %[(jsize a <: nat); 0]) =
  match a with
  | JArr xs -> keys_unique_deep_items xs
  | JObj fs -> keys_unique fs && keys_unique_deep_fields fs
  | _ -> true

and keys_unique_deep_items (#num #flt: eqtype) (xs: list (jval num flt))
  : Tot bool (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> true
  | x :: t -> keys_unique_deep x && keys_unique_deep_items t

and keys_unique_deep_fields (#num #flt: eqtype) (fs: list (string & jval num flt))
  : Tot bool (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> true
  | (_, v) :: t -> keys_unique_deep v && keys_unique_deep_fields t

(* A LITERAL reordering of a member list — multiset equality of the members themselves, with no
   relation involved. It is what the refutation below needs to say "and yet these two are the
   same document with its members written in a different order". *)
let rec remove_member (#num #flt: eqtype) (m: string & jval num flt) (fs: list (string & jval num flt))
  : Tot (option (list (string & jval num flt))) (decreases fs) =
  match fs with
  | [] -> None
  | x :: t ->
    if x = m then
      Some t
    else
      (match remove_member m t with
       | None -> None
       | Some r -> Some (x :: r))

let rec list_perm (#num #flt: eqtype) (fs gs: list (string & jval num flt))
  : Tot bool (decreases fs) =
  match fs with
  | [] -> Nil? gs
  | x :: t ->
    (match remove_member x gs with
     | None -> false
     | Some r -> list_perm t r)

(* ---- the relation is reflexive, and it contains a reordering ---- *)

let rec member_perm_refl (#num #flt: eqtype) (a: jval num flt)
  : Lemma (ensures member_perm a a) (decreases %[(jsize a <: nat); 0]) =
  match a with
  | JArr xs -> items_perm_refl #num #flt xs
  | JObj fs -> fields_perm_refl #num #flt fs
  | _ -> ()

and items_perm_refl (#num #flt: eqtype) (xs: list (jval num flt))
  : Lemma (ensures items_perm xs xs) (decreases %[(jsizes xs <: nat); 1]) =
  match xs with
  | [] -> ()
  | x :: t ->
    member_perm_refl #num #flt x;
    items_perm_refl #num #flt t

and fields_perm_refl (#num #flt: eqtype) (fs: list (string & jval num flt))
  : Lemma (ensures fields_perm fs fs) (decreases %[(fsize fs <: nat); 1]) =
  match fs with
  | [] -> ()
  | (_, v) :: t ->
    member_perm_refl #num #flt v;
    fields_perm_refl #num #flt t

(* … and it contains every SELECTION: pulling any one member to the front of the list is a
   reordering the relation holds of. With reflexivity this is what makes the invariance theorems
   below non-vacuous on the model side — the relation is not merely the identity wearing a longer
   name. `perm_covers_reorder` further down is the general statement; this is the step of it a
   reader can check by eye. *)
let rec perm_covers_selection (#num #flt: eqtype) (k: string) (fs: list (string & jval num flt))
  : Lemma (ensures (match extract_field k fs with
                    | Some (v, rest) -> fields_perm fs ((k, v) :: rest)
                    | None -> True))
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k0, v0) :: t ->
    if k0 = k then
      (member_perm_refl #num #flt v0;
       fields_perm_refl #num #flt t)
    else
      (perm_covers_selection #num #flt k t;
       match extract_field k t with
       | None -> ()
       | Some (_, _) -> member_perm_refl #num #flt v0)

(* ======================================================================================
   … AND IT CONTAINS EVERY REORDERING OF A DUPLICATE-FREE MEMBER LIST.

   This is the converse direction, and without it the invariance theorems below could all be true
   of a relation that held of almost nothing. `perm_covers_reorder` states it in the form the
   induction on a document actually needs: `gs` is `fs` PERMUTED (`list_perm`, literal multiset
   equality of the members themselves — no relation involved) and then each value REPLACED by a
   related one in place (`fields_pointwise`). That composition is exactly one level of a deep
   reordering, with the scalar arms of `member_perm` as the base cases, so a reader has the whole
   induction rather than a statement about the root.

   `keys_unique fs` is where the premise the section header argues for is discharged, and the
   proof shows why: the step is the identification of the member a lookup will find with the
   member the permutation moved, and on a repeated key those are two different members.
   ====================================================================================== *)

(* How many members carry this name. A counting argument is what turns "the keys are unique" into
   "this permutation moved the member the lookup is about to find". *)
let rec key_count (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Tot nat (decreases fs) =
  match fs with
  | [] -> 0
  | (k, _) :: t -> (if k = name then 1 else 0) + key_count name t

(* The same key order, each value replaced by a related one. *)
let rec fields_pointwise (#num #flt: eqtype) (hs gs: list (string & jval num flt))
  : Tot bool (decreases hs) =
  match hs, gs with
  | [], [] -> true
  | (k1, v1) :: t1, (k2, v2) :: t2 -> k1 = k2 && member_perm v1 v2 && fields_pointwise t1 t2
  | _, _ -> false

let rec key_count_remove (#num #flt: eqtype) (name: string) (m: string & jval num flt)
  (gs: list (string & jval num flt))
  : Lemma (ensures (match remove_member m gs with
                    | Some r -> key_count name gs == key_count name r + (if fst m = name then 1 else 0)
                    | None -> True))
          (decreases gs) =
  match gs with
  | [] -> ()
  | x :: t -> if x = m then () else key_count_remove #num #flt name m t

let rec has_key_is_count (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Lemma (ensures has_key name fs == (key_count name fs > 0)) (decreases fs) =
  match fs with
  | [] -> ()
  | (_, _) :: t -> has_key_is_count #num #flt name t

let rec list_perm_key_count (#num #flt: eqtype) (name: string) (fs gs: list (string & jval num flt))
  : Lemma (requires list_perm fs gs)
          (ensures key_count name fs == key_count name gs)
          (decreases fs) =
  match fs with
  | [] -> ()
  | x :: t ->
    (match remove_member x gs with
     | None -> ()
     | Some r ->
       key_count_remove #num #flt name x gs;
       list_perm_key_count #num #flt name t r)

(* The identification the premise buys: where the removed member is the ONLY one of its name, the
   lookup finds it and the remainder is what the removal left. *)
let rec extract_after_remove (#num #flt: eqtype) (k: string) (v: jval num flt)
  (gs r: list (string & jval num flt))
  : Lemma (requires remove_member (k, v) gs == Some r /\ key_count k r == 0)
          (ensures extract_field k gs == Some (v, r))
          (decreases gs) =
  match gs with
  | [] -> ()
  | x :: t ->
    if x = (k, v) then
      ()
    else
      (match remove_member (k, v) t with
       | None -> ()
       | Some r' -> extract_after_remove #num #flt k v t r')

(* A lookup passes through the pointwise replacement, relating what each side finds. *)
let rec extract_pointwise (#num #flt: eqtype) (k: string) (hs gs: list (string & jval num flt))
  : Lemma (requires fields_pointwise hs gs)
          (ensures (match extract_field k hs, extract_field k gs with
                    | Some (v, hs'), Some (w, gs') -> member_perm v w /\ fields_pointwise hs' gs'
                    | None, None -> True
                    | _, _ -> False))
          (decreases hs) =
  match hs, gs with
  | [], [] -> ()
  | (k1, _) :: t1, (_, _) :: t2 -> if k1 = k then () else extract_pointwise #num #flt k t1 t2
  | _, _ -> ()

let rec perm_covers_reorder (#num #flt: eqtype) (fs hs gs: list (string & jval num flt))
  : Lemma (requires keys_unique fs /\ list_perm fs hs /\ fields_pointwise hs gs)
          (ensures fields_perm fs gs)
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k, v) :: t ->
    (match remove_member (k, v) hs with
     | None -> ()
     | Some hs' ->
       key_count_remove #num #flt k (k, v) hs;
       has_key_is_count #num #flt k t;
       list_perm_key_count #num #flt k t hs';
       extract_after_remove #num #flt k v hs hs';
       extract_pointwise #num #flt k hs gs;
       (match extract_field k gs with
        | None -> ()
        | Some (_, gs') -> perm_covers_reorder #num #flt t hs' gs'))

(* ---- what the relation carries at the root ---- *)

(* Related documents have the SAME SHAPE, so every combinator's refusal arm names the same word.
   Without this the message comparison the differential runs would be the only thing keeping a
   decoder from naming a different expectation on a reordered document. *)
let perm_shape (#num #flt: eqtype) (a b: jval num flt)
  : Lemma (requires member_perm a b)
          (ensures JStr? a == JStr? b /\ JInt? a == JInt? b /\ JBool? a == JBool? b /\
                   JFloat? a == JFloat? b /\ JArr? a == JArr? b /\ JObj? a == JObj? b /\
                   kind_name a == kind_name b)
  = ()

let perm_as_string (#num #flt: eqtype) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures as_string a == as_string b)
  = ()

let perm_as_int (#num #flt: eqtype) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures as_int a == as_int b)
  = ()

let perm_as_bool (#num #flt: eqtype) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures as_bool a == as_bool b)
  = ()

let perm_as_float (#num #flt: eqtype) (to_flt: num -> flt) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures as_float to_flt a == as_float to_flt b)
  = ()

(* ---- the lookup, which is the whole of it ---- *)

(* `extract_field` and `find_field` read the same member. *)
let rec extract_is_find (#num #flt: eqtype) (name: string) (fs: list (string & jval num flt))
  : Lemma (ensures (match extract_field name fs with
                    | Some (v, _) -> find_field name fs == Ok v
                    | None -> Error? (find_field name fs)))
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k, _) :: t -> if k = name then () else extract_is_find #num #flt name t

(* Removing a member of a DIFFERENT name cannot change what a lookup finds. *)
let rec find_field_drop_other (#num #flt: eqtype) (name: string) (k: string)
  (gs: list (string & jval num flt))
  : Lemma (requires k <> name)
          (ensures (match extract_field k gs with
                    | Some (_, rest) -> find_field name gs == find_field name rest
                    | None -> True))
          (decreases gs) =
  match gs with
  | [] -> ()
  | (k0, _) :: t -> if k0 = k then () else find_field_drop_other #num #flt name k t

(* THE CORE. A permuted member list answers every lookup with a related value, or with the same
   refusal — including the `missing property: <name>` one, which is the arm a reordering could
   most plausibly disturb and the arm a verdict-only comparison would not notice. *)
let rec find_field_perm (#num #flt: eqtype) (name: string) (fs gs: list (string & jval num flt))
  : Lemma (requires fields_perm fs gs)
          (ensures outcome_perm (find_field name fs) (find_field name gs))
          (decreases fs) =
  match fs with
  | [] -> ()
  | (k, _) :: t ->
    (match extract_field k gs with
     | None -> ()
     | Some (_, rest) ->
       extract_is_find #num #flt k gs;
       if k = name then
         ()
       else
         (find_field_drop_other #num #flt name k gs;
          find_field_perm #num #flt name t rest))

let get_prop_perm (#num #flt: eqtype) (name: string) (a b: jval num flt)
  : Lemma (requires member_perm a b)
          (ensures outcome_perm (get_prop name a) (get_prop name b)) =
  perm_shape #num #flt a b;
  match a, b with
  | JObj fs, JObj gs -> find_field_perm #num #flt name fs gs
  | _, _ -> ()

(* ---- the walker ---- *)

(* `map_list` is parametric in its element decoder, so the invariance is too: a decoder that
   itself respects the relation is lifted through the walk. The hypothesis is not a formality —
   `map_list` is the seam a domain plugs its own decoder into, and one that read member order
   would break the theorem exactly here. *)
let rec map_list_go_perm (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t)
  (respects: (v1: jval num flt) -> (v2: jval num flt) ->
             Lemma (requires member_perm v1 v2) (ensures d v1 == d v2))
  (acc: list t) (xs ys: list (jval num flt))
  : Lemma (requires items_perm xs ys)
          (ensures map_list_go d acc xs == map_list_go d acc ys)
          (decreases xs) =
  match xs, ys with
  | [], [] -> ()
  | x :: xt, y :: yt ->
    respects x y;
    (match d x with
     | Ok v -> map_list_go_perm #num #flt #t d respects (v :: acc) xt yt
     | Error _ -> ())
  | _, _ -> ()

let map_list_perm (#num #flt: eqtype) (#t: Type)
  (d: jval num flt -> outcome t)
  (respects: (v1: jval num flt) -> (v2: jval num flt) ->
             Lemma (requires member_perm v1 v2) (ensures d v1 == d v2))
  (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures map_list d a == map_list d b) =
  perm_shape #num #flt a b;
  match a, b with
  | JArr xs, JArr ys -> map_list_go_perm #num #flt #t d respects [] xs ys
  | _, _ -> ()

(* ---- the three composites, each `get_prop` followed by a scalar reader ---- *)

let kind_of_perm (#num #flt: eqtype) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures kind_of a == kind_of b) =
  get_prop_perm #num #flt "kind" a b;
  match get_prop "kind" a, get_prop "kind" b with
  | Ok v1, Ok v2 -> perm_as_string #num #flt v1 v2
  | _, _ -> ()

let str_field_perm (#num #flt: eqtype) (name: string) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures str_field name a == str_field name b) =
  get_prop_perm #num #flt name a b;
  match get_prop name a, get_prop name b with
  | Ok v1, Ok v2 -> perm_as_string #num #flt v1 v2
  | _, _ -> ()

let int_field_perm (#num #flt: eqtype) (name: string) (a b: jval num flt)
  : Lemma (requires member_perm a b) (ensures int_field name a == int_field name b) =
  get_prop_perm #num #flt name a b;
  match get_prop name a, get_prop name b with
  | Ok v1, Ok v2 -> perm_as_int #num #flt v1 v2
  | _, _ -> ()

(* ======================================================================================
   THE THEOREM — every combinator is key-order invariant.
   ====================================================================================== *)

let decode_perm_invariant (#num #flt: eqtype) (to_flt: num -> flt) (name: string)
  (a b: jval num flt)
  : Lemma (requires member_perm a b)
          (ensures
            (* the scalar readers are EQUAL, message included *)
            as_string a == as_string b /\
            as_int a == as_int b /\
            as_bool a == as_bool b /\
            as_float to_flt a == as_float to_flt b /\
            (* the member readers, likewise — their results carry no members of their own *)
            kind_of a == kind_of b /\
            str_field name a == str_field name b /\
            int_field name a == int_field name b /\
            (* … and the one combinator whose result is a subtree: related, not equal *)
            outcome_perm (get_prop name a) (get_prop name b) /\
            (* the walker, at the instance the reference vocabulary uses *)
            map_list (as_string #num #flt) a == map_list (as_string #num #flt) b) =
  get_prop_perm #num #flt name a b;
  kind_of_perm #num #flt a b;
  str_field_perm #num #flt name a b;
  int_field_perm #num #flt name a b;
  map_list_perm #num #flt #string (as_string #num #flt) (perm_as_string #num #flt) a b

(* §20's one-answer rule, at this layer: the same document in any member order decodes to the
   SAME TREE — literally equal, since a decoded node carries no object of its own — or to the
   same named refusal. This is the statement a conformant host actually owes a reader. *)
let rec decode_node_perm_invariant (#num #flt: eqtype) (a b: jval num flt)
  : Lemma (requires member_perm a b)
          (ensures decode_node a == decode_node b)
          (decreases %[(jsize a <: nat); 0]) =
  kind_of_perm #num #flt a b;
  (match kind_of a with
   | Error _ -> ()
   | Ok tag ->
     if tag = "text" then
       str_field_perm #num #flt "value" a b
     else if tag = "flag" then
       (get_prop_perm #num #flt "on" a b;
        match get_prop "on" a, get_prop "on" b with
        | Ok v1, Ok v2 -> perm_as_bool #num #flt v1 v2
        | _, _ -> ())
     else if tag = "tags" then
       (get_prop_perm #num #flt "tags" a b;
        match get_prop "tags" a, get_prop "tags" b with
        | Ok v1, Ok v2 -> map_list_perm #num #flt #string (as_string #num #flt) (perm_as_string #num #flt) v1 v2
        | _, _ -> ())
     else if tag = "group" then
       (str_field_perm #num #flt "id" a b;
        get_prop_perm #num #flt "items" a b;
        let ra = get_prop "items" a in
        let rb = get_prop "items" b in
        match ra, rb with
        | Ok v1, Ok v2 ->
          perm_shape #num #flt v1 v2;
          (match v1, v2 with
           | JArr ys, JArr zs -> decode_items_perm #num #flt [] ys zs
           | _, _ -> ())
        | _, _ -> ())
     else ())

and decode_items_perm (#num #flt: eqtype) (acc: list rnode) (xs ys: list (jval num flt))
  : Lemma (requires items_perm xs ys)
          (ensures decode_items acc xs == decode_items acc ys)
          (decreases %[(jsizes xs <: nat); 1]) =
  match xs, ys with
  | [], [] -> ()
  | x :: xt, y :: yt ->
    decode_node_perm_invariant #num #flt x y;
    (match decode_node x with
     | Ok n -> decode_items_perm #num #flt (n :: acc) xt yt
     | Error _ -> ())
  | _, _ -> ()

(* ======================================================================================
   THE PREMISE IS NECESSARY — the refutation, proved rather than asserted.

   Two documents that are the SAME MEMBERS in a different order, and `get_prop` answers
   differently. Nothing upstream excludes them: the parser appends every member, duplicate key or
   not. So the duplicate-free premise is not a convenience of this formulation, and a future
   session weakening `member_perm` to a plain list permutation would be proving something false —
   this lemma goes red first.
   ====================================================================================== *)

let duplicate_keys_break_order_invariance (#num #flt: eqtype) (x y: num)
  : Lemma (requires x <> y)
          (ensures
            (let a: jval num flt = JObj [("a", JInt x); ("a", JInt y)] in
             let b: jval num flt = JObj [("a", JInt y); ("a", JInt x)] in
             (* `b` IS `a` with its member list reordered … *)
             list_perm (JObj?.fields a) (JObj?.fields b) /\
             (* … the reordering is only possible because the keys repeat … *)
             not (keys_unique (JObj?.fields a)) /\
             (* … the decoder's answer MOVES … *)
             get_prop "a" a =!= get_prop "a" b /\
             (* … and so the relation must not, and does not, relate them. *)
             not (member_perm a b))) =
  ()
