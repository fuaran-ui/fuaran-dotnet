module BoundedFold
type opt<'a> =
| ONone
| OSome of 'a


let uu___is_ONone = (fun ( projectee  :  opt<'a> ) -> (match (projectee) with
| ONone -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OSome = (fun ( projectee  :  opt<'a> ) -> (match (projectee) with
| OSome (item) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OSome__item__item = (fun ( projectee  :  opt<'a> ) -> (match (projectee) with
| OSome (item) -> begin
     item
     end))


let rec app = (fun ( xs  :  Prims.list<'a> ) ( ys  :  Prims.list<'a> ) -> (match (xs) with
| [] -> begin
     ys
     end
| (x)::rest -> begin
     (x)::(app rest ys)
     end))


type key = Prims.string


type store<'v> = Prims.list<(key * 'v)>


let rec lookup = (fun ( s  :  store<'v> ) ( k  :  key ) -> (match (s) with
| [] -> begin
     ONone
     end
| ((k', x))::rest -> begin
      
if (Prims.op_Equals k' k) then begin
     OSome (x)
     end else begin
     (lookup rest k)
     end
     end))


let rec write = (fun ( s  :  store<'v> ) ( k  :  key ) ( x  :  'v ) -> (match (s) with
| [] -> begin
     (((k), (x)))::[]
     end
| ((k', y))::rest -> begin
      
if (Prims.op_Equals k' k) then begin
     (((k), (x)))::rest
     end else begin
     (((k'), (y)))::(write rest k x)
     end
     end))

type resolution<'v> =
| Resolved of 'v
| NotResolved
| Errored of Prims.string


let uu___is_Resolved = (fun ( projectee  :  resolution<'v> ) -> (match (projectee) with
| Resolved (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Resolved__item__value = (fun ( projectee  :  resolution<'v> ) -> (match (projectee) with
| Resolved (value) -> begin
     value
     end))


let uu___is_NotResolved = (fun ( projectee  :  resolution<'v> ) -> (match (projectee) with
| NotResolved -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Errored = (fun ( projectee  :  resolution<'v> ) -> (match (projectee) with
| Errored (message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Errored__item__message = (fun ( projectee  :  resolution<'v> ) -> (match (projectee) with
| Errored (message) -> begin
     message
     end))

type bound<'e> =
| BLiteral of Prims.nat
| BParameter of 'e * Prims.nat * Prims.nat


let uu___is_BLiteral = (fun ( projectee  :  bound<'e> ) -> (match (projectee) with
| BLiteral (count) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__BLiteral__item__count = (fun ( projectee  :  bound<'e> ) -> (match (projectee) with
| BLiteral (count) -> begin
     count
     end))


let uu___is_BParameter = (fun ( projectee  :  bound<'e> ) -> (match (projectee) with
| BParameter (count, lo, hi) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__BParameter__item__count = (fun ( projectee  :  bound<'e> ) -> (match (projectee) with
| BParameter (count, lo, hi) -> begin
     count
     end))


let __proj__BParameter__item__lo = (fun ( projectee  :  bound<'e> ) -> (match (projectee) with
| BParameter (count, lo, hi) -> begin
     lo
     end))


let __proj__BParameter__item__hi = (fun ( projectee  :  bound<'e> ) -> (match (projectee) with
| BParameter (count, lo, hi) -> begin
     hi
     end))

type action_view<'a, 'e, 'v> =
| VSequence of 'a * Prims.list<action_view<'a, 'e, 'v>>
| VAssign of 'a * key * opt<'v> * opt<'e>
| VCall of 'a * Prims.string * Prims.bool
| VRequire of 'a * 'e
| VChoose of 'a * 'e * action_view<'a, 'e, 'v> * action_view<'a, 'e, 'v> * opt<'e>
| VRepeat of 'a * bound<'e> * action_view<'a, 'e, 'v>
| VEach of 'a * Prims.list<action_view<'a, 'e, 'v>>
| VLeaf of 'a


let uu___is_VSequence = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VSequence (act, ops) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VSequence__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VSequence (act, ops) -> begin
     act
     end))


let __proj__VSequence__item__ops = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VSequence (act, ops) -> begin
     ops
     end))


let uu___is_VAssign = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VAssign (act, state_key, value, value_from) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VAssign__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VAssign (act, state_key, value, value_from) -> begin
     act
     end))


let __proj__VAssign__item__state_key = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VAssign (act, state_key, value, value_from) -> begin
     state_key
     end))


let __proj__VAssign__item__value = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VAssign (act, state_key, value, value_from) -> begin
     value
     end))


let __proj__VAssign__item__value_from = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VAssign (act, state_key, value, value_from) -> begin
     value_from
     end))


let uu___is_VCall = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VCall (act, endpoint, declares_target) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VCall__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VCall (act, endpoint, declares_target) -> begin
     act
     end))


let __proj__VCall__item__endpoint = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VCall (act, endpoint, declares_target) -> begin
     endpoint
     end))


let __proj__VCall__item__declares_target = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VCall (act, endpoint, declares_target) -> begin
     declares_target
     end))


let uu___is_VRequire = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRequire (act, condition) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VRequire__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRequire (act, condition) -> begin
     act
     end))


let __proj__VRequire__item__condition = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRequire (act, condition) -> begin
     condition
     end))


let uu___is_VChoose = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VChoose (act, entry, when_true, when_false, exit) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VChoose__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VChoose (act, entry, when_true, when_false, exit) -> begin
     act
     end))


let __proj__VChoose__item__entry = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VChoose (act, entry, when_true, when_false, exit) -> begin
     entry
     end))


let __proj__VChoose__item__when_true = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VChoose (act, entry, when_true, when_false, exit) -> begin
     when_true
     end))


let __proj__VChoose__item__when_false = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VChoose (act, entry, when_true, when_false, exit) -> begin
     when_false
     end))


let __proj__VChoose__item__exit = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VChoose (act, entry, when_true, when_false, exit) -> begin
     exit
     end))


let uu___is_VRepeat = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRepeat (act, count, body) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VRepeat__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRepeat (act, count, body) -> begin
     act
     end))


let __proj__VRepeat__item__count = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRepeat (act, count, body) -> begin
     count
     end))


let __proj__VRepeat__item__body = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VRepeat (act, count, body) -> begin
     body
     end))


let uu___is_VEach = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VEach (act, elements) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VEach__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VEach (act, elements) -> begin
     act
     end))


let __proj__VEach__item__elements = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VEach (act, elements) -> begin
     elements
     end))


let uu___is_VLeaf = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VLeaf (act) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VLeaf__item__act = (fun ( projectee  :  action_view<'a, 'e, 'v> ) -> (match (projectee) with
| VLeaf (act) -> begin
     act
     end))

type leaf_outcome<'eff> =
| Emit of 'eff
| Refuse of Prims.string
| Decline


let uu___is_Emit = (fun ( projectee  :  leaf_outcome<'eff> ) -> (match (projectee) with
| Emit (emitted) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Emit__item__emitted = (fun ( projectee  :  leaf_outcome<'eff> ) -> (match (projectee) with
| Emit (emitted) -> begin
     emitted
     end))


let uu___is_Refuse = (fun ( projectee  :  leaf_outcome<'eff> ) -> (match (projectee) with
| Refuse (reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Refuse__item__reason = (fun ( projectee  :  leaf_outcome<'eff> ) -> (match (projectee) with
| Refuse (reason) -> begin
     reason
     end))


let uu___is_Decline = (fun ( projectee  :  leaf_outcome<'eff> ) -> (match (projectee) with
| Decline -> begin
     true
     end
| uu___ -> begin
     false
     end))

type witness<'a, 'e, 'v, 'eff> = {w_view : 'a  ->  action_view<'a, 'e, 'v>; w_lower : Prims.string  ->  'a  ->  store<'v>  ->  leaf_outcome<'eff>; w_describe : 'a  ->  Prims.string; w_resolve : store<'v>  ->  'e  ->  resolution<'v>; w_is_reserved : key  ->  Prims.bool; w_reserved_prefix : Prims.string; w_is_true : 'v  ->  Prims.bool; w_as_count : 'v  ->  opt<Prims.nat>}


let __proj__Mkwitness__item__w_view = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_view
     end))


let __proj__Mkwitness__item__w_lower = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_lower
     end))


let __proj__Mkwitness__item__w_describe = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_describe
     end))


let __proj__Mkwitness__item__w_resolve = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_resolve
     end))


let __proj__Mkwitness__item__w_is_reserved = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_is_reserved
     end))


let __proj__Mkwitness__item__w_reserved_prefix = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_reserved_prefix
     end))


let __proj__Mkwitness__item__w_is_true = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_is_true
     end))


let __proj__Mkwitness__item__w_as_count = (fun ( projectee  :  witness<'a, 'e, 'v, 'eff> ) -> (match (projectee) with
| {w_view = w_view; w_lower = w_lower; w_describe = w_describe; w_resolve = w_resolve; w_is_reserved = w_is_reserved; w_reserved_prefix = w_reserved_prefix; w_is_true = w_is_true; w_as_count = w_as_count} -> begin
     w_as_count
     end))

type diagnostic =
| DUnsupported of Prims.string * Prims.string
| DRefused of Prims.string * Prims.string * Prims.string


let uu___is_DUnsupported : diagnostic  ->  Prims.bool = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DUnsupported (node_id, action_name) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DUnsupported__item__node_id : diagnostic  ->  Prims.string = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DUnsupported (node_id, action_name) -> begin
     node_id
     end))


let __proj__DUnsupported__item__action_name : diagnostic  ->  Prims.string = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DUnsupported (node_id, action_name) -> begin
     action_name
     end))


let uu___is_DRefused : diagnostic  ->  Prims.bool = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DRefused (node_id, action_name, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DRefused__item__node_id : diagnostic  ->  Prims.string = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DRefused (node_id, action_name, reason) -> begin
     node_id
     end))


let __proj__DRefused__item__action_name : diagnostic  ->  Prims.string = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DRefused (node_id, action_name, reason) -> begin
     action_name
     end))


let __proj__DRefused__item__reason : diagnostic  ->  Prims.string = (fun ( projectee  :  diagnostic ) -> (match (projectee) with
| DRefused (node_id, action_name, reason) -> begin
     reason
     end))

type bounded_outcome<'v, 'eff> = {o_store : store<'v>; o_effects : Prims.list<'eff>; o_diagnostics : Prims.list<diagnostic>; o_halted : Prims.bool}


let __proj__Mkbounded_outcome__item__o_store = (fun ( projectee  :  bounded_outcome<'v, 'eff> ) -> (match (projectee) with
| {o_store = o_store; o_effects = o_effects; o_diagnostics = o_diagnostics; o_halted = o_halted} -> begin
     o_store
     end))


let __proj__Mkbounded_outcome__item__o_effects = (fun ( projectee  :  bounded_outcome<'v, 'eff> ) -> (match (projectee) with
| {o_store = o_store; o_effects = o_effects; o_diagnostics = o_diagnostics; o_halted = o_halted} -> begin
     o_effects
     end))


let __proj__Mkbounded_outcome__item__o_diagnostics = (fun ( projectee  :  bounded_outcome<'v, 'eff> ) -> (match (projectee) with
| {o_store = o_store; o_effects = o_effects; o_diagnostics = o_diagnostics; o_halted = o_halted} -> begin
     o_diagnostics
     end))


let __proj__Mkbounded_outcome__item__o_halted = (fun ( projectee  :  bounded_outcome<'v, 'eff> ) -> (match (projectee) with
| {o_store = o_store; o_effects = o_effects; o_diagnostics = o_diagnostics; o_halted = o_halted} -> begin
     o_halted
     end))

type handler_answer<'v, 'eff, 'p> = {h_store : store<'v>; h_effects : Prims.list<'eff>; h_diagnostics : Prims.list<diagnostic>; h_placement : 'p}


let __proj__Mkhandler_answer__item__h_store = (fun ( projectee  :  handler_answer<'v, 'eff, 'p> ) -> (match (projectee) with
| {h_store = h_store; h_effects = h_effects; h_diagnostics = h_diagnostics; h_placement = h_placement} -> begin
     h_store
     end))


let __proj__Mkhandler_answer__item__h_effects = (fun ( projectee  :  handler_answer<'v, 'eff, 'p> ) -> (match (projectee) with
| {h_store = h_store; h_effects = h_effects; h_diagnostics = h_diagnostics; h_placement = h_placement} -> begin
     h_effects
     end))


let __proj__Mkhandler_answer__item__h_diagnostics = (fun ( projectee  :  handler_answer<'v, 'eff, 'p> ) -> (match (projectee) with
| {h_store = h_store; h_effects = h_effects; h_diagnostics = h_diagnostics; h_placement = h_placement} -> begin
     h_diagnostics
     end))


let __proj__Mkhandler_answer__item__h_placement = (fun ( projectee  :  handler_answer<'v, 'eff, 'p> ) -> (match (projectee) with
| {h_store = h_store; h_effects = h_effects; h_diagnostics = h_diagnostics; h_placement = h_placement} -> begin
     h_placement
     end))

type handler_arm<'v, 'eff, 'p> = {answer : Prims.string  ->  Prims.string  ->  store<'v>  ->  'p  ->  opt<handler_answer<'v, 'eff, 'p>>}


let __proj__Mkhandler_arm__item__answer = (fun ( projectee  :  handler_arm<'v, 'eff, 'p> ) -> (match (projectee) with
| {answer = answer} -> begin
     answer
     end))


let inert_arm = (fun ( uu___  :  unit ) -> {answer = (fun ( uu___1  :  Prims.string ) ( uu___2  :  Prims.string ) ( uu___3  :  store<'v> ) ( uu___4  :  'p ) -> ONone)})


let store_only = (fun ( s  :  store<'v> ) -> {o_store = s; o_effects = []; o_diagnostics = []; o_halted = false})


let declined = (fun ( node_id  :  Prims.string ) ( description  :  Prims.string ) ( s  :  store<'v> ) -> {o_store = s; o_effects = []; o_diagnostics = (DUnsupported (node_id, description))::[]; o_halted = false})


let refused = (fun ( node_id  :  Prims.string ) ( description  :  Prims.string ) ( reason  :  Prims.string ) ( s  :  store<'v> ) -> {o_store = s; o_effects = []; o_diagnostics = (DRefused (node_id, description, reason))::[]; o_halted = false})


let halted = (fun ( node_id  :  Prims.string ) ( description  :  Prims.string ) ( reason  :  Prims.string ) ( s  :  store<'v> ) -> {o_store = s; o_effects = []; o_diagnostics = (DRefused (node_id, description, reason))::[]; o_halted = true})


let halted_after = (fun ( o  :  bounded_outcome<'v, 'eff> ) ( node_id  :  Prims.string ) ( description  :  Prims.string ) ( reason  :  Prims.string ) -> {o_store = o.o_store; o_effects = o.o_effects; o_diagnostics = (app o.o_diagnostics ((DRefused (node_id, description, reason))::[])); o_halted = true})

type jval_payload<'v> =
| POk of opt<'v>
| PErr of Prims.string


let uu___is_POk = (fun ( projectee  :  jval_payload<'v> ) -> (match (projectee) with
| POk (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__POk__item__value = (fun ( projectee  :  jval_payload<'v> ) -> (match (projectee) with
| POk (value) -> begin
     value
     end))


let uu___is_PErr = (fun ( projectee  :  jval_payload<'v> ) -> (match (projectee) with
| PErr (message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PErr__item__message = (fun ( projectee  :  jval_payload<'v> ) -> (match (projectee) with
| PErr (message) -> begin
     message
     end))


let rec fold = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( x  :  action_view<'a, 'e, 'v> ) ( s  :  store<'v> ) ( pl  :  'p ) -> (match (x) with
| VAssign (act, state_key, value, value_from) -> begin
     (( 
if (w.w_is_reserved state_key) then begin
     (refused node_id (w.w_describe act) (Prims.strcat "State key \'" (Prims.strcat state_key (Prims.strcat "\' is under the host-reserved \'" (Prims.strcat w.w_reserved_prefix "\' namespace")))) s)
     end else begin
     (

let payload = (match (value_from) with
| OSome (expr) -> begin
     (match ((w.w_resolve s expr)) with
| Resolved (jv) -> begin
     POk (OSome (jv))
     end
| NotResolved -> begin
     POk (ONone)
     end
| Errored (m) -> begin
     PErr (m)
     end)
     end
| ONone -> begin
     POk (value)
     end)
in (match (payload) with
| POk (OSome (jv)) -> begin
     (store_only (write s state_key jv))
     end
| POk (ONone) -> begin
     (refused node_id (w.w_describe act) "valueFrom did not resolve to a value — no write performed" s)
     end
| PErr (m) -> begin
     (refused node_id (w.w_describe act) (Prims.strcat "valueFrom errored: " (Prims.strcat m " — no write performed")) s)
     end))
     end), (pl))
     end
| VCall (act, endpoint, declares_target) -> begin
      
if declares_target then begin
     (((refused node_id (w.w_describe act) "the call declares a result target; a handler declares where its own results land" s)), (pl))
     end else begin
     (match ((ar.answer node_id endpoint s pl)) with
| ONone -> begin
     (((declined node_id (w.w_describe act) s)), (pl))
     end
| OSome (ans) -> begin
     (({o_store = ans.h_store; o_effects = ans.h_effects; o_diagnostics = ans.h_diagnostics; o_halted = false}), (ans.h_placement))
     end)
     end
     end
| VRequire (act, condition) -> begin
     (((match ((w.w_resolve s condition)) with
| Resolved (jv) -> begin
      
if (w.w_is_true jv) then begin
     (store_only s)
     end else begin
     (halted node_id (w.w_describe act) "the guard did not hold" s)
     end
     end
| NotResolved -> begin
     (halted node_id (w.w_describe act) "the guard did not resolve to a value" s)
     end
| Errored (m) -> begin
     (halted node_id (w.w_describe act) m s)
     end)), (pl))
     end
| VLeaf (act) -> begin
     (((match ((w.w_lower node_id act s)) with
| Emit (emitted) -> begin
     {o_store = s; o_effects = (emitted)::[]; o_diagnostics = []; o_halted = false}
     end
| Refuse (reason) -> begin
     (refused node_id (w.w_describe act) reason s)
     end
| Decline -> begin
     (declined node_id (w.w_describe act) s)
     end)), (pl))
     end
| VChoose (act, entry, when_true, when_false, exit) -> begin
     (match ((w.w_resolve s entry)) with
| Resolved (jv) -> begin
     (

let took_true = (w.w_is_true jv)
in (

let uu___ =  
if took_true then begin
     (fold w ar node_id when_true s pl)
     end else begin
     (fold w ar node_id when_false s pl)
     end
in (match (uu___) with
| (o1, p1) -> begin
      
if o1.o_halted then begin
     ((o1), (p1))
     end else begin
     (match (exit) with
| ONone -> begin
     ((o1), (p1))
     end
| OSome (assertion) -> begin
     (match ((w.w_resolve o1.o_store assertion)) with
| Resolved (jv') -> begin
      
if (Prims.op_Equals (w.w_is_true jv') took_true) then begin
     ((o1), (p1))
     end else begin
     (((halted_after o1 node_id (w.w_describe act) ( 
if took_true then begin
     "the exit assertion did not hold after the true arm"
     end else begin
     "the exit assertion held after the false arm"
     end))), (p1))
     end
     end
| NotResolved -> begin
     (((halted_after o1 node_id (w.w_describe act) "the exit assertion did not resolve to a value")), (p1))
     end
| Errored (m) -> begin
     (((halted_after o1 node_id (w.w_describe act) (Prims.strcat "the exit assertion errored: " m))), (p1))
     end)
     end)
     end
     end)))
     end
| NotResolved -> begin
     (((halted node_id (w.w_describe act) "the branch condition did not resolve to a value" s)), (pl))
     end
| Errored (m) -> begin
     (((halted node_id (w.w_describe act) m s)), (pl))
     end)
     end
| VRepeat (act, count, body) -> begin
     (match (count) with
| BLiteral (n) -> begin
     (fold_repeat w ar node_id body n s pl)
     end
| BParameter (expr, lo, hi) -> begin
     (match ((w.w_resolve s expr)) with
| Resolved (jv) -> begin
     (match ((w.w_as_count jv)) with
| OSome (n) -> begin
      
if ((lo <= n) && (n <= hi)) then begin
     (fold_repeat w ar node_id body n s pl)
     end else begin
     (((halted node_id (w.w_describe act) "the repeat\'s bound is outside its declared range" s)), (pl))
     end
     end
| ONone -> begin
     (((halted node_id (w.w_describe act) "the repeat\'s bound did not resolve to a count" s)), (pl))
     end)
     end
| NotResolved -> begin
     (((halted node_id (w.w_describe act) "the repeat\'s bound did not resolve to a value" s)), (pl))
     end
| Errored (m) -> begin
     (((halted node_id (w.w_describe act) m s)), (pl))
     end)
     end)
     end
| VSequence (uu___, ops) -> begin
     (fold_many w ar node_id ops s pl)
     end
| VEach (uu___, elements) -> begin
     (fold_many w ar node_id elements s pl)
     end))
and fold_many = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( ops  :  Prims.list<action_view<'a, 'e, 'v>> ) ( s  :  store<'v> ) ( pl  :  'p ) -> (match (ops) with
| [] -> begin
     (((store_only s)), (pl))
     end
| (x)::rest -> begin
     (

let uu___ = (fold w ar node_id x s pl)
in (match (uu___) with
| (o1, p1) -> begin
      
if o1.o_halted then begin
     ((o1), (p1))
     end else begin
     (

let uu___1 = (fold_many w ar node_id rest o1.o_store p1)
in (match (uu___1) with
| (o2, p2) -> begin
     (({o_store = o2.o_store; o_effects = (app o1.o_effects o2.o_effects); o_diagnostics = (app o1.o_diagnostics o2.o_diagnostics); o_halted = o2.o_halted}), (p2))
     end))
     end
     end))
     end))
and fold_repeat = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( body  :  action_view<'a, 'e, 'v> ) ( n  :  Prims.nat ) ( s  :  store<'v> ) ( pl  :  'p ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     (((store_only s)), (pl))
     end else begin
     (

let uu___ = (fold w ar node_id body s pl)
in (match (uu___) with
| (o1, p1) -> begin
      
if o1.o_halted then begin
     ((o1), (p1))
     end else begin
     (

let uu___1 = (fold_repeat w ar node_id body (n - (Prims.parse_int "1")) o1.o_store p1)
in (match (uu___1) with
| (o2, p2) -> begin
     (({o_store = o2.o_store; o_effects = (app o1.o_effects o2.o_effects); o_diagnostics = (app o1.o_diagnostics o2.o_diagnostics); o_halted = o2.o_halted}), (p2))
     end))
     end
     end))
     end)


let run_action = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( act  :  'a ) ( s  :  store<'v> ) ( pl  :  'p ) -> (fold w ar node_id (w.w_view act) s pl))

type trace<'v> =
| TNothing
| TWrote of opt<'v>
| TSeq of Prims.list<trace<'v>>
| TChoose of Prims.bool * trace<'v>
| TRepeat of Prims.list<trace<'v>>
| TEach of Prims.list<trace<'v>>


let uu___is_TNothing = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TNothing -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TWrote = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TWrote (old) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TWrote__item__old = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TWrote (old) -> begin
     old
     end))


let uu___is_TSeq = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TSeq (steps) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TSeq__item__steps = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TSeq (steps) -> begin
     steps
     end))


let uu___is_TChoose = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TChoose (took_true, arm) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TChoose__item__took_true = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TChoose (took_true, arm) -> begin
     took_true
     end))


let __proj__TChoose__item__arm = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TChoose (took_true, arm) -> begin
     arm
     end))


let uu___is_TRepeat = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TRepeat (iterations) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TRepeat__item__iterations = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TRepeat (iterations) -> begin
     iterations
     end))


let uu___is_TEach = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TEach (elements) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TEach__item__elements = (fun ( projectee  :  trace<'v> ) -> (match (projectee) with
| TEach (elements) -> begin
     elements
     end))


let rec fold_traced = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( x  :  action_view<'a, 'e, 'v> ) ( s  :  store<'v> ) ( pl  :  'p ) -> (match (x) with
| VAssign (act, state_key, value, value_from) -> begin
      
if (w.w_is_reserved state_key) then begin
     (((refused node_id (w.w_describe act) (Prims.strcat "State key \'" (Prims.strcat state_key (Prims.strcat "\' is under the host-reserved \'" (Prims.strcat w.w_reserved_prefix "\' namespace")))) s)), (pl), (TNothing))
     end else begin
     (

let payload = (match (value_from) with
| OSome (expr) -> begin
     (match ((w.w_resolve s expr)) with
| Resolved (jv) -> begin
     POk (OSome (jv))
     end
| NotResolved -> begin
     POk (ONone)
     end
| Errored (m) -> begin
     PErr (m)
     end)
     end
| ONone -> begin
     POk (value)
     end)
in (match (payload) with
| POk (OSome (jv)) -> begin
     (((store_only (write s state_key jv))), (pl), (TWrote ((lookup s state_key))))
     end
| POk (ONone) -> begin
     (((refused node_id (w.w_describe act) "valueFrom did not resolve to a value — no write performed" s)), (pl), (TNothing))
     end
| PErr (m) -> begin
     (((refused node_id (w.w_describe act) (Prims.strcat "valueFrom errored: " (Prims.strcat m " — no write performed")) s)), (pl), (TNothing))
     end))
     end
     end
| VCall (act, endpoint, declares_target) -> begin
      
if declares_target then begin
     (((refused node_id (w.w_describe act) "the call declares a result target; a handler declares where its own results land" s)), (pl), (TNothing))
     end else begin
     (match ((ar.answer node_id endpoint s pl)) with
| ONone -> begin
     (((declined node_id (w.w_describe act) s)), (pl), (TNothing))
     end
| OSome (ans) -> begin
     (({o_store = ans.h_store; o_effects = ans.h_effects; o_diagnostics = ans.h_diagnostics; o_halted = false}), (ans.h_placement), (TNothing))
     end)
     end
     end
| VRequire (act, condition) -> begin
     (((match ((w.w_resolve s condition)) with
| Resolved (jv) -> begin
      
if (w.w_is_true jv) then begin
     (store_only s)
     end else begin
     (halted node_id (w.w_describe act) "the guard did not hold" s)
     end
     end
| NotResolved -> begin
     (halted node_id (w.w_describe act) "the guard did not resolve to a value" s)
     end
| Errored (m) -> begin
     (halted node_id (w.w_describe act) m s)
     end)), (pl), (TNothing))
     end
| VLeaf (act) -> begin
     (((match ((w.w_lower node_id act s)) with
| Emit (emitted) -> begin
     {o_store = s; o_effects = (emitted)::[]; o_diagnostics = []; o_halted = false}
     end
| Refuse (reason) -> begin
     (refused node_id (w.w_describe act) reason s)
     end
| Decline -> begin
     (declined node_id (w.w_describe act) s)
     end)), (pl), (TNothing))
     end
| VChoose (act, entry, when_true, when_false, exit) -> begin
     (match ((w.w_resolve s entry)) with
| Resolved (jv) -> begin
     (

let took_true = (w.w_is_true jv)
in (

let uu___ =  
if took_true then begin
     (fold_traced w ar node_id when_true s pl)
     end else begin
     (fold_traced w ar node_id when_false s pl)
     end
in (match (uu___) with
| (o1, p1, arm) -> begin
     (

let tr = TChoose (took_true, arm)
in  
if o1.o_halted then begin
     ((o1), (p1), (tr))
     end else begin
     (match (exit) with
| ONone -> begin
     ((o1), (p1), (tr))
     end
| OSome (assertion) -> begin
     (match ((w.w_resolve o1.o_store assertion)) with
| Resolved (jv') -> begin
      
if (Prims.op_Equals (w.w_is_true jv') took_true) then begin
     ((o1), (p1), (tr))
     end else begin
     (((halted_after o1 node_id (w.w_describe act) ( 
if took_true then begin
     "the exit assertion did not hold after the true arm"
     end else begin
     "the exit assertion held after the false arm"
     end))), (p1), (tr))
     end
     end
| NotResolved -> begin
     (((halted_after o1 node_id (w.w_describe act) "the exit assertion did not resolve to a value")), (p1), (tr))
     end
| Errored (m) -> begin
     (((halted_after o1 node_id (w.w_describe act) (Prims.strcat "the exit assertion errored: " m))), (p1), (tr))
     end)
     end)
     end)
     end)))
     end
| NotResolved -> begin
     (((halted node_id (w.w_describe act) "the branch condition did not resolve to a value" s)), (pl), (TNothing))
     end
| Errored (m) -> begin
     (((halted node_id (w.w_describe act) m s)), (pl), (TNothing))
     end)
     end
| VRepeat (act, count, body) -> begin
     (match (count) with
| BLiteral (n) -> begin
     (

let uu___ = (fold_traced_repeat w ar node_id body n s pl)
in (match (uu___) with
| (o, p', its) -> begin
     ((o), (p'), (TRepeat (its)))
     end))
     end
| BParameter (expr, lo, hi) -> begin
     (match ((w.w_resolve s expr)) with
| Resolved (jv) -> begin
     (match ((w.w_as_count jv)) with
| OSome (n) -> begin
      
if ((lo <= n) && (n <= hi)) then begin
     (

let uu___ = (fold_traced_repeat w ar node_id body n s pl)
in (match (uu___) with
| (o, p', its) -> begin
     ((o), (p'), (TRepeat (its)))
     end))
     end else begin
     (((halted node_id (w.w_describe act) "the repeat\'s bound is outside its declared range" s)), (pl), (TNothing))
     end
     end
| ONone -> begin
     (((halted node_id (w.w_describe act) "the repeat\'s bound did not resolve to a count" s)), (pl), (TNothing))
     end)
     end
| NotResolved -> begin
     (((halted node_id (w.w_describe act) "the repeat\'s bound did not resolve to a value" s)), (pl), (TNothing))
     end
| Errored (m) -> begin
     (((halted node_id (w.w_describe act) m s)), (pl), (TNothing))
     end)
     end)
     end
| VSequence (uu___, ops) -> begin
     (

let uu___1 = (fold_traced_many w ar node_id ops s pl)
in (match (uu___1) with
| (o, p', steps) -> begin
     ((o), (p'), (TSeq (steps)))
     end))
     end
| VEach (uu___, elements) -> begin
     (

let uu___1 = (fold_traced_many w ar node_id elements s pl)
in (match (uu___1) with
| (o, p', steps) -> begin
     ((o), (p'), (TEach (steps)))
     end))
     end))
and fold_traced_many = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( ops  :  Prims.list<action_view<'a, 'e, 'v>> ) ( s  :  store<'v> ) ( pl  :  'p ) -> (match (ops) with
| [] -> begin
     (((store_only s)), (pl), ([]))
     end
| (x)::rest -> begin
     (

let uu___ = (fold_traced w ar node_id x s pl)
in (match (uu___) with
| (o1, p1, t1) -> begin
      
if o1.o_halted then begin
     ((o1), (p1), ((t1)::[]))
     end else begin
     (

let uu___1 = (fold_traced_many w ar node_id rest o1.o_store p1)
in (match (uu___1) with
| (o2, p2, ts) -> begin
     (({o_store = o2.o_store; o_effects = (app o1.o_effects o2.o_effects); o_diagnostics = (app o1.o_diagnostics o2.o_diagnostics); o_halted = o2.o_halted}), (p2), ((t1)::ts))
     end))
     end
     end))
     end))
and fold_traced_repeat = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( body  :  action_view<'a, 'e, 'v> ) ( n  :  Prims.nat ) ( s  :  store<'v> ) ( pl  :  'p ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     (((store_only s)), (pl), ([]))
     end else begin
     (

let uu___ = (fold_traced w ar node_id body s pl)
in (match (uu___) with
| (o1, p1, t1) -> begin
      
if o1.o_halted then begin
     ((o1), (p1), ((t1)::[]))
     end else begin
     (

let uu___1 = (fold_traced_repeat w ar node_id body (n - (Prims.parse_int "1")) o1.o_store p1)
in (match (uu___1) with
| (o2, p2, ts) -> begin
     (({o_store = o2.o_store; o_effects = (app o1.o_effects o2.o_effects); o_diagnostics = (app o1.o_diagnostics o2.o_diagnostics); o_halted = o2.o_halted}), (p2), ((t1)::ts))
     end))
     end
     end))
     end)


let run_action_traced = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( ar  :  handler_arm<'v, 'eff, 'p> ) ( node_id  :  Prims.string ) ( act  :  'a ) ( s  :  store<'v> ) ( pl  :  'p ) -> (fold_traced w ar node_id (w.w_view act) s pl))


let rec reversible = (fun ( x  :  action_view<'a, 'e, 'v> ) -> (match (x) with
| VSequence (uu___, ops) -> begin
     (reversible_list ops)
     end
| VAssign (uu___, uu___1, uu___2, uu___3) -> begin
     true
     end
| VRequire (uu___, uu___1) -> begin
     true
     end
| VChoose (uu___, uu___1, when_true, when_false, exit) -> begin
     (((match (exit) with
| OSome (item) -> begin
     true
     end
| uu___2 -> begin
     false
     end) && (reversible when_true)) && (reversible when_false))
     end
| VRepeat (uu___, count, body) -> begin
     ((match (count) with
| BLiteral (count1) -> begin
     true
     end
| uu___1 -> begin
     false
     end) && (reversible body))
     end
| VEach (uu___, elements) -> begin
     (reversible_list elements)
     end
| VCall (uu___, uu___1, uu___2) -> begin
     false
     end
| VLeaf (uu___) -> begin
     false
     end))
and reversible_list = (fun ( ops  :  Prims.list<action_view<'a, 'e, 'v>> ) -> (match (ops) with
| [] -> begin
     true
     end
| (x)::rest -> begin
     ((reversible x) && (reversible_list rest))
     end))


let rec restorable = (fun ( tr  :  trace<'v> ) -> (match (tr) with
| TNothing -> begin
     true
     end
| TWrote (ONone) -> begin
     false
     end
| TWrote (OSome (uu___)) -> begin
     true
     end
| TSeq (steps) -> begin
     (restorable_list steps)
     end
| TChoose (uu___, arm) -> begin
     (restorable arm)
     end
| TRepeat (iterations) -> begin
     (restorable_list iterations)
     end
| TEach (elements) -> begin
     (restorable_list elements)
     end))
and restorable_list = (fun ( steps  :  Prims.list<trace<'v>> ) -> (match (steps) with
| [] -> begin
     true
     end
| (t)::rest -> begin
     ((restorable t) && (restorable_list rest))
     end))


let rec replicate = (fun ( n  :  Prims.nat ) ( x  :  'a ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     []
     end else begin
     (x)::(replicate (n - (Prims.parse_int "1")) x)
     end)


let act_of = (fun ( x  :  action_view<'a, 'e, 'v> ) -> (match (x) with
| VSequence (act, uu___) -> begin
     act
     end
| VAssign (act, uu___, uu___1, uu___2) -> begin
     act
     end
| VCall (act, uu___, uu___1) -> begin
     act
     end
| VRequire (act, uu___) -> begin
     act
     end
| VChoose (act, uu___, uu___1, uu___2, uu___3) -> begin
     act
     end
| VRepeat (act, uu___, uu___1) -> begin
     act
     end
| VEach (act, uu___) -> begin
     act
     end
| VLeaf (act) -> begin
     act
     end))


let rec reverse = (fun ( x  :  action_view<'a, 'e, 'v> ) ( tr  :  trace<'v> ) -> (match (((x), (tr))) with
| (VSequence (act, ops), TSeq (steps)) -> begin
     VSequence (act, (reverse_many ops steps))
     end
| (VAssign (act, state_key, uu___, uu___1), TWrote (OSome (old))) -> begin
     VAssign (act, state_key, OSome (old), ONone)
     end
| (VRequire (act, condition), uu___) -> begin
     VRequire (act, condition)
     end
| (VChoose (act, entry, when_true, uu___, OSome (exit)), TChoose (true, arm)) -> begin
     VChoose (act, exit, (reverse when_true arm), VSequence (act, []), OSome (entry))
     end
| (VChoose (act, entry, uu___, when_false, OSome (exit)), TChoose (false, arm)) -> begin
     VChoose (act, exit, VSequence (act, []), (reverse when_false arm), OSome (entry))
     end
| (VRepeat (act, BLiteral (n), body), TRepeat (iterations)) -> begin
     VSequence (act, (reverse_many (replicate n body) iterations))
     end
| (VEach (act, elements), TEach (steps)) -> begin
     VSequence (act, (reverse_many elements steps))
     end
| (uu___, uu___1) -> begin
     VSequence ((act_of x), [])
     end))
and reverse_many = (fun ( ops  :  Prims.list<action_view<'a, 'e, 'v>> ) ( steps  :  Prims.list<trace<'v>> ) -> (match (((ops), (steps))) with
| ((x)::rest, (t)::ts) -> begin
     (app (reverse_many rest ts) (((reverse x t))::[]))
     end
| (uu___, uu___1) -> begin
     []
     end))


let reverse_action = (fun ( w  :  witness<'a, 'e, 'v, 'eff> ) ( act  :  'a ) ( tr  :  trace<'v> ) -> (reverse (w.w_view act) tr))

type res<'v> =
| JResolved of 'v
| JNotResolved
| JErrored of Prims.string
| JI18nUnresolved of Prims.string


let uu___is_JResolved = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JResolved (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JResolved__item__value = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JResolved (value) -> begin
     value
     end))


let uu___is_JNotResolved = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JNotResolved -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_JErrored = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JErrored (message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JErrored__item__message = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JErrored (message) -> begin
     message
     end))


let uu___is_JI18nUnresolved = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JI18nUnresolved (i18n_key) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JI18nUnresolved__item__i18n_key = (fun ( projectee  :  res<'v> ) -> (match (projectee) with
| JI18nUnresolved (i18n_key) -> begin
     i18n_key
     end))

type res_text =
| SResolved of opt<Prims.string>
| SNotResolved
| SErrored of Prims.string
| SI18nUnresolved of Prims.string


let uu___is_SResolved : res_text  ->  Prims.bool = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SResolved (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SResolved__item__value : res_text  ->  opt<Prims.string> = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SResolved (value) -> begin
     value
     end))


let uu___is_SNotResolved : res_text  ->  Prims.bool = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SNotResolved -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SErrored : res_text  ->  Prims.bool = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SErrored (message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SErrored__item__message : res_text  ->  Prims.string = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SErrored (message) -> begin
     message
     end))


let uu___is_SI18nUnresolved : res_text  ->  Prims.bool = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SI18nUnresolved (i18n_key) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SI18nUnresolved__item__i18n_key : res_text  ->  Prims.string = (fun ( projectee  :  res_text ) -> (match (projectee) with
| SI18nUnresolved (i18n_key) -> begin
     i18n_key
     end))

type text_source<'b> =
| TLiteral of Prims.string
| TBound of 'b
| TI18n of Prims.string * 'b


let uu___is_TLiteral = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TLiteral (text) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TLiteral__item__text = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TLiteral (text) -> begin
     text
     end))


let uu___is_TBound = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TBound (binding) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TBound__item__binding = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TBound (binding) -> begin
     binding
     end))


let uu___is_TI18n = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TI18n (i18n_key, args) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TI18n__item__i18n_key = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TI18n (i18n_key, args) -> begin
     i18n_key
     end))


let __proj__TI18n__item__args = (fun ( projectee  :  text_source<'b> ) -> (match (projectee) with
| TI18n (i18n_key, args) -> begin
     args
     end))

type nav_target =
| NSelf
| NBlank


let uu___is_NSelf : nav_target  ->  Prims.bool = (fun ( projectee  :  nav_target ) -> (match (projectee) with
| NSelf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_NBlank : nav_target  ->  Prims.bool = (fun ( projectee  :  nav_target ) -> (match (projectee) with
| NBlank -> begin
     true
     end
| uu___ -> begin
     false
     end))

type file_encoding =
| FText
| FBase64
| FDataUrl


let uu___is_FText : file_encoding  ->  Prims.bool = (fun ( projectee  :  file_encoding ) -> (match (projectee) with
| FText -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FBase64 : file_encoding  ->  Prims.bool = (fun ( projectee  :  file_encoding ) -> (match (projectee) with
| FBase64 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FDataUrl : file_encoding  ->  Prims.bool = (fun ( projectee  :  file_encoding ) -> (match (projectee) with
| FDataUrl -> begin
     true
     end
| uu___ -> begin
     false
     end))

type call_target =
| CTState of Prims.string
| CTQuery of Prims.string


let uu___is_CTState : call_target  ->  Prims.bool = (fun ( projectee  :  call_target ) -> (match (projectee) with
| CTState (state_key) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CTState__item__state_key : call_target  ->  Prims.string = (fun ( projectee  :  call_target ) -> (match (projectee) with
| CTState (state_key) -> begin
     state_key
     end))


let uu___is_CTQuery : call_target  ->  Prims.bool = (fun ( projectee  :  call_target ) -> (match (projectee) with
| CTQuery (query_name) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CTQuery__item__query_name : call_target  ->  Prims.string = (fun ( projectee  :  call_target ) -> (match (projectee) with
| CTQuery (query_name) -> begin
     query_name
     end))

type client_effect =
| ENavigate of Prims.string * nav_target
| EClipboard of Prims.string
| EPrint
| EFocus of Prims.string
| EReadFileBody of Prims.string * Prims.string


let uu___is_ENavigate : client_effect  ->  Prims.bool = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| ENavigate (route, target) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ENavigate__item__route : client_effect  ->  Prims.string = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| ENavigate (route, target) -> begin
     route
     end))


let __proj__ENavigate__item__target : client_effect  ->  nav_target = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| ENavigate (route, target) -> begin
     target
     end))


let uu___is_EClipboard : client_effect  ->  Prims.bool = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EClipboard (text) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EClipboard__item__text : client_effect  ->  Prims.string = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EClipboard (text) -> begin
     text
     end))


let uu___is_EPrint : client_effect  ->  Prims.bool = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EPrint -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_EFocus : client_effect  ->  Prims.bool = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EFocus (node_id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EFocus__item__node_id : client_effect  ->  Prims.string = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EFocus (node_id) -> begin
     node_id
     end))


let uu___is_EReadFileBody : client_effect  ->  Prims.bool = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EReadFileBody (node_id, encoding) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EReadFileBody__item__node_id : client_effect  ->  Prims.string = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EReadFileBody (node_id, encoding) -> begin
     node_id
     end))


let __proj__EReadFileBody__item__encoding : client_effect  ->  Prims.string = (fun ( projectee  :  client_effect ) -> (match (projectee) with
| EReadFileBody (node_id, encoding) -> begin
     encoding
     end))

type action<'v, 'b, 'k> =
| AChain of Prims.list<action<'v, 'b, 'k>>
| AWriteToClipboard of text_source<'b>
| ADispatch of 'k
| AInvoke of Prims.string * 'b
| AReadFileBody of Prims.string * 'k * file_encoding * 'k
| ACall of Prims.string * 'k * opt<call_target>
| ANavigate of text_source<'b> * nav_target
| ACommitLocal of Prims.string
| ANotify of Prims.string * 'b
| ASetState of key * opt<'v> * opt<'b>
| AAiTool of Prims.string * 'b
| APrint
| AConfirm of text_source<'b> * action<'v, 'b, 'k> * opt<action<'v, 'b, 'k>>
| AFocus of Prims.string


let uu___is_AChain = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AChain (ops) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AChain__item__ops = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AChain (ops) -> begin
     ops
     end))


let uu___is_AWriteToClipboard = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AWriteToClipboard (text) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AWriteToClipboard__item__text = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AWriteToClipboard (text) -> begin
     text
     end))


let uu___is_ADispatch = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ADispatch (msg) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ADispatch__item__msg = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ADispatch (msg) -> begin
     msg
     end))


let uu___is_AInvoke = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AInvoke (capability_id, args) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AInvoke__item__capability_id = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AInvoke (capability_id, args) -> begin
     capability_id
     end))


let __proj__AInvoke__item__args = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AInvoke (capability_id, args) -> begin
     args
     end))


let uu___is_AReadFileBody = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AReadFileBody (file_ref, file_handle, encoding, on_read) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AReadFileBody__item__file_ref = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AReadFileBody (file_ref, file_handle, encoding, on_read) -> begin
     file_ref
     end))


let __proj__AReadFileBody__item__file_handle = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AReadFileBody (file_ref, file_handle, encoding, on_read) -> begin
     file_handle
     end))


let __proj__AReadFileBody__item__encoding = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AReadFileBody (file_ref, file_handle, encoding, on_read) -> begin
     encoding
     end))


let __proj__AReadFileBody__item__on_read = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AReadFileBody (file_ref, file_handle, encoding, on_read) -> begin
     on_read
     end))


let uu___is_ACall = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ACall (endpoint, on_result, into) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ACall__item__endpoint = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ACall (endpoint, on_result, into) -> begin
     endpoint
     end))


let __proj__ACall__item__on_result = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ACall (endpoint, on_result, into) -> begin
     on_result
     end))


let __proj__ACall__item__into = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ACall (endpoint, on_result, into) -> begin
     into
     end))


let uu___is_ANavigate = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ANavigate (route, target) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ANavigate__item__route = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ANavigate (route, target) -> begin
     route
     end))


let __proj__ANavigate__item__target = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ANavigate (route, target) -> begin
     target
     end))


let uu___is_ACommitLocal = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ACommitLocal (node_id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ACommitLocal__item__node_id = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ACommitLocal (node_id) -> begin
     node_id
     end))


let uu___is_ANotify = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ANotify (channel, payload) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ANotify__item__channel = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ANotify (channel, payload) -> begin
     channel
     end))


let __proj__ANotify__item__payload = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ANotify (channel, payload) -> begin
     payload
     end))


let uu___is_ASetState = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ASetState (state_key, value, value_from) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ASetState__item__state_key = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ASetState (state_key, value, value_from) -> begin
     state_key
     end))


let __proj__ASetState__item__value = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ASetState (state_key, value, value_from) -> begin
     value
     end))


let __proj__ASetState__item__value_from = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| ASetState (state_key, value, value_from) -> begin
     value_from
     end))


let uu___is_AAiTool = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AAiTool (tool_name, args) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AAiTool__item__tool_name = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AAiTool (tool_name, args) -> begin
     tool_name
     end))


let __proj__AAiTool__item__args = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AAiTool (tool_name, args) -> begin
     args
     end))


let uu___is_APrint = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| APrint -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_AConfirm = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AConfirm (prompt, on_confirm, on_cancel) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AConfirm__item__prompt = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AConfirm (prompt, on_confirm, on_cancel) -> begin
     prompt
     end))


let __proj__AConfirm__item__on_confirm = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AConfirm (prompt, on_confirm, on_cancel) -> begin
     on_confirm
     end))


let __proj__AConfirm__item__on_cancel = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AConfirm (prompt, on_confirm, on_cancel) -> begin
     on_cancel
     end))


let uu___is_AFocus = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AFocus (node_id) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AFocus__item__node_id = (fun ( projectee  :  action<'v, 'b, 'k> ) -> (match (projectee) with
| AFocus (node_id) -> begin
     node_id
     end))


type outcome<'v> = bounded_outcome<'v, client_effect>


type arm<'v, 'p> = handler_arm<'v, client_effect, 'p>

type axioms<'v, 'b> = {is_reserved : key  ->  Prims.bool; reserved_prefix : Prims.string; resolve_jval : store<'v>  ->  'b  ->  res<'v>; resolve_scalar : store<'v>  ->  'b  ->  res_text; i18n_has : store<'v>  ->  Prims.string  ->  Prims.bool; resolve_text : store<'v>  ->  text_source<'b>  ->  Prims.string; sanitize_url : Prims.string  ->  opt<Prims.string>; route_path : Prims.string  ->  Prims.string}


let __proj__Mkaxioms__item__is_reserved = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     is_reserved
     end))


let __proj__Mkaxioms__item__reserved_prefix = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     reserved_prefix
     end))


let __proj__Mkaxioms__item__resolve_jval = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     resolve_jval
     end))


let __proj__Mkaxioms__item__resolve_scalar = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     resolve_scalar
     end))


let __proj__Mkaxioms__item__i18n_has = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     i18n_has
     end))


let __proj__Mkaxioms__item__resolve_text = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     resolve_text
     end))


let __proj__Mkaxioms__item__sanitize_url = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     sanitize_url
     end))


let __proj__Mkaxioms__item__route_path = (fun ( projectee  :  axioms<'v, 'b> ) -> (match (projectee) with
| {is_reserved = is_reserved; reserved_prefix = reserved_prefix; resolve_jval = resolve_jval; resolve_scalar = resolve_scalar; i18n_has = i18n_has; resolve_text = resolve_text; sanitize_url = sanitize_url; route_path = route_path} -> begin
     route_path
     end))


let describe = (fun ( ax  :  axioms<'v, 'b> ) ( a  :  action<'v, 'b, 'k> ) -> (match (a) with
| ADispatch (uu___) -> begin
     "Dispatch"
     end
| ACall (endpoint, uu___, uu___1) -> begin
     (Prims.strcat "Call(" (Prims.strcat endpoint ")"))
     end
| ANotify (channel, uu___) -> begin
     (Prims.strcat "Notify(" (Prims.strcat channel ")"))
     end
| ANavigate (route, uu___) -> begin
     (match (route) with
| TLiteral (literal) -> begin
     (Prims.strcat "Navigate(" (Prims.strcat (ax.route_path literal) ")"))
     end
| uu___1 -> begin
     "Navigate(<bound>)"
     end)
     end
| ASetState (k1, uu___, uu___1) -> begin
     (Prims.strcat "SetState(" (Prims.strcat k1 ")"))
     end
| AAiTool (tool_name, uu___) -> begin
     (Prims.strcat "AiTool(" (Prims.strcat tool_name ")"))
     end
| AChain (uu___) -> begin
     "Chain"
     end
| ACommitLocal (node_id) -> begin
     (Prims.strcat "CommitLocal(" (Prims.strcat node_id ")"))
     end
| AWriteToClipboard (uu___) -> begin
     "WriteToClipboard"
     end
| APrint -> begin
     "Print"
     end
| AConfirm (uu___, uu___1, uu___2) -> begin
     "Confirm"
     end
| AFocus (node_id) -> begin
     (Prims.strcat "Focus(" (Prims.strcat node_id ")"))
     end
| AReadFileBody (uu___, uu___1, uu___2, uu___3) -> begin
     "ReadFileBody"
     end
| AInvoke (capability_id, uu___) -> begin
     (Prims.strcat "Invoke(" (Prims.strcat capability_id ")"))
     end))

type text_result =
| ROk of Prims.string
| RErr of Prims.string


let uu___is_ROk : text_result  ->  Prims.bool = (fun ( projectee  :  text_result ) -> (match (projectee) with
| ROk (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ROk__item__value : text_result  ->  Prims.string = (fun ( projectee  :  text_result ) -> (match (projectee) with
| ROk (value) -> begin
     value
     end))


let uu___is_RErr : text_result  ->  Prims.bool = (fun ( projectee  :  text_result ) -> (match (projectee) with
| RErr (message) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RErr__item__message : text_result  ->  Prims.string = (fun ( projectee  :  text_result ) -> (match (projectee) with
| RErr (message) -> begin
     message
     end))


let unresolved_i18n : Prims.string  ->  Prims.string = (fun ( k  :  Prims.string ) -> (Prims.strcat "unresolved i18n key \'" (Prims.strcat k "\'")))


let rec ui_view = (fun ( a  :  action<'v, 'b, 'k> ) -> (match (a) with
| AChain (ops) -> begin
     VSequence (a, (ui_view_list ops))
     end
| ASetState (state_key, value, value_from) -> begin
     VAssign (a, state_key, value, value_from)
     end
| ACall (endpoint, uu___, into) -> begin
     VCall (a, endpoint, (match (into) with
| OSome (item) -> begin
     true
     end
| uu___1 -> begin
     false
     end))
     end
| AWriteToClipboard (uu___) -> begin
     VLeaf (a)
     end
| ADispatch (uu___) -> begin
     VLeaf (a)
     end
| AInvoke (uu___, uu___1) -> begin
     VLeaf (a)
     end
| AReadFileBody (uu___, uu___1, uu___2, uu___3) -> begin
     VLeaf (a)
     end
| ANavigate (uu___, uu___1) -> begin
     VLeaf (a)
     end
| ACommitLocal (uu___) -> begin
     VLeaf (a)
     end
| ANotify (uu___, uu___1) -> begin
     VLeaf (a)
     end
| AAiTool (uu___, uu___1) -> begin
     VLeaf (a)
     end
| APrint -> begin
     VLeaf (a)
     end
| AConfirm (uu___, uu___1, uu___2) -> begin
     VLeaf (a)
     end
| AFocus (uu___) -> begin
     VLeaf (a)
     end))
and ui_view_list = (fun ( ops  :  Prims.list<action<'v, 'b, 'k>> ) -> (match (ops) with
| [] -> begin
     []
     end
| (x)::rest -> begin
     ((ui_view x))::(ui_view_list rest)
     end))


let ui_lower = (fun ( ax  :  axioms<'v, 'b> ) ( node_id  :  Prims.string ) ( a  :  action<'v, 'b, 'k> ) ( s  :  store<'v> ) -> (match (a) with
| ANavigate (route, target) -> begin
     (

let resolved = (match (route) with
| TLiteral (literal) -> begin
     ROk (literal)
     end
| TBound (binding) -> begin
     (match ((ax.resolve_scalar s binding)) with
| SResolved (OSome (value)) -> begin
     ROk (value)
     end
| SResolved (ONone) -> begin
     RErr ("the route binding resolved to no value")
     end
| SNotResolved -> begin
     RErr ("the route binding did not resolve to a value")
     end
| SErrored (m) -> begin
     RErr (m)
     end
| SI18nUnresolved (kk) -> begin
     RErr ((unresolved_i18n kk))
     end)
     end
| TI18n (kk, uu___) -> begin
      
if (ax.i18n_has s kk) then begin
     ROk ((ax.resolve_text s route))
     end else begin
     RErr ((unresolved_i18n kk))
     end
     end)
in (match (resolved) with
| RErr (reason) -> begin
     Refuse ((Prims.strcat reason " — nothing was navigated to"))
     end
| ROk (r) -> begin
     (match ((ax.sanitize_url r)) with
| OSome (safe) -> begin
     Emit (ENavigate (safe, target))
     end
| ONone -> begin
     Refuse ("route is not a safe URL")
     end)
     end))
     end
| AWriteToClipboard (text) -> begin
     (

let payload = (match (text) with
| TLiteral (literal) -> begin
     ROk (literal)
     end
| TBound (binding) -> begin
     (match ((ax.resolve_scalar s binding)) with
| SResolved (OSome (value)) -> begin
     ROk (value)
     end
| SResolved (ONone) -> begin
     ROk ("")
     end
| SNotResolved -> begin
     RErr ("the payload binding did not resolve to a value")
     end
| SErrored (m) -> begin
     RErr (m)
     end
| SI18nUnresolved (kk) -> begin
     RErr ((unresolved_i18n kk))
     end)
     end
| TI18n (kk, uu___) -> begin
      
if (ax.i18n_has s kk) then begin
     ROk ((ax.resolve_text s text))
     end else begin
     RErr ((unresolved_i18n kk))
     end
     end)
in (match (payload) with
| ROk (value) -> begin
     Emit (EClipboard (value))
     end
| RErr (reason) -> begin
     Refuse ((Prims.strcat reason " — nothing was written to the clipboard"))
     end))
     end
| APrint -> begin
     Emit (EPrint)
     end
| AFocus (target_node_id) -> begin
     Emit (EFocus (target_node_id))
     end
| AReadFileBody (uu___, uu___1, encoding, uu___2) -> begin
     (

let enc = (match (encoding) with
| FText -> begin
     "Text"
     end
| FBase64 -> begin
     "Base64"
     end
| FDataUrl -> begin
     "DataUrl"
     end)
in Emit (EReadFileBody (node_id, enc)))
     end
| AConfirm (uu___, uu___1, uu___2) -> begin
     Decline
     end
| ANotify (uu___, uu___1) -> begin
     Decline
     end
| AAiTool (uu___, uu___1) -> begin
     Decline
     end
| AInvoke (uu___, uu___1) -> begin
     Decline
     end
| ADispatch (uu___) -> begin
     Decline
     end
| ACommitLocal (uu___) -> begin
     Decline
     end
| AChain (uu___) -> begin
     Decline
     end
| ASetState (uu___, uu___1, uu___2) -> begin
     Decline
     end
| ACall (uu___, uu___1, uu___2) -> begin
     Decline
     end))


let ui_resolve = (fun ( ax  :  axioms<'v, 'b> ) ( s  :  store<'v> ) ( binding  :  'b ) -> (match ((ax.resolve_jval s binding)) with
| JResolved (jv) -> begin
     Resolved (jv)
     end
| JNotResolved -> begin
     NotResolved
     end
| JErrored (m) -> begin
     Errored (m)
     end
| JI18nUnresolved (kk) -> begin
     Errored ((unresolved_i18n kk))
     end))


let ui_witness = (fun ( ax  :  axioms<'v, 'b> ) -> {w_view = ui_view; w_lower = (ui_lower ax); w_describe = (describe ax); w_resolve = (ui_resolve ax); w_is_reserved = ax.is_reserved; w_reserved_prefix = ax.reserved_prefix; w_is_true = (fun ( uu___  :  'v ) -> false); w_as_count = (fun ( uu___  :  'v ) -> ONone)})


let run = (fun ( ax  :  axioms<'v, 'b> ) ( ar  :  arm<'v, 'p> ) ( node_id  :  Prims.string ) ( a  :  action<'v, 'b, 'k> ) ( s  :  store<'v> ) ( pl  :  'p ) -> (run_action (ui_witness ax) ar node_id a s pl))




