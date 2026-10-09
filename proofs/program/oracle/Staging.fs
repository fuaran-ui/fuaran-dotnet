module Staging
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

type res<'a> =
| ROk of 'a
| RErr of Prims.string


let uu___is_ROk = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| ROk (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ROk__item__value = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| ROk (value) -> begin
     value
     end))


let uu___is_RErr = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| RErr (reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RErr__item__reason = (fun ( projectee  :  res<'a> ) -> (match (projectee) with
| RErr (reason) -> begin
     reason
     end))


let rec app = (fun ( xs  :  Prims.list<'a> ) ( ys  :  Prims.list<'a> ) -> (match (xs) with
| [] -> begin
     ys
     end
| (x)::rest -> begin
     (x)::(app rest ys)
     end))


let rec rev = (fun ( xs  :  Prims.list<'a> ) -> (match (xs) with
| [] -> begin
     []
     end
| (x)::rest -> begin
     (app (rev rest) ((x)::[]))
     end))


let rec length = (fun ( xs  :  Prims.list<'a> ) -> (match (xs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::rest -> begin
     ((Prims.parse_int "1") + (length rest))
     end))

type store<'t, 'b> = {st_tree : 't; st_bindings : 'b}


let __proj__Mkstore__item__st_tree = (fun ( projectee  :  store<'t, 'b> ) -> (match (projectee) with
| {st_tree = st_tree; st_bindings = st_bindings} -> begin
     st_tree
     end))


let __proj__Mkstore__item__st_bindings = (fun ( projectee  :  store<'t, 'b> ) -> (match (projectee) with
| {st_tree = st_tree; st_bindings = st_bindings} -> begin
     st_bindings
     end))

type server_effect<'v, 'o, 'q> =
| RunQuery of Prims.string * 'q
| ApplyOps of Prims.list<'o>
| HostCall of Prims.string * 'v * opt<Prims.string>
| EmitPatch of Prims.list<'o>
| Notify of Prims.string * 'v


let uu___is_RunQuery = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| RunQuery (name, query) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RunQuery__item__name = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| RunQuery (name, query) -> begin
     name
     end))


let __proj__RunQuery__item__query = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| RunQuery (name, query) -> begin
     query
     end))


let uu___is_ApplyOps = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| ApplyOps (ops) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ApplyOps__item__ops = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| ApplyOps (ops) -> begin
     ops
     end))


let uu___is_HostCall = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| HostCall (fn, args, into) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__HostCall__item__fn = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| HostCall (fn, args, into) -> begin
     fn
     end))


let __proj__HostCall__item__args = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| HostCall (fn, args, into) -> begin
     args
     end))


let __proj__HostCall__item__into = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| HostCall (fn, args, into) -> begin
     into
     end))


let uu___is_EmitPatch = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| EmitPatch (patch) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__EmitPatch__item__patch = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| EmitPatch (patch) -> begin
     patch
     end))


let uu___is_Notify = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| Notify (channel, payload) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Notify__item__channel = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| Notify (channel, payload) -> begin
     channel
     end))


let __proj__Notify__item__payload = (fun ( projectee  :  server_effect<'v, 'o, 'q> ) -> (match (projectee) with
| Notify (channel, payload) -> begin
     payload
     end))

type stage<'a, 'v, 'o, 'q> =
| SCompute of 'a
| SEffect of server_effect<'v, 'o, 'q>


let uu___is_SCompute = (fun ( projectee  :  stage<'a, 'v, 'o, 'q> ) -> (match (projectee) with
| SCompute (action) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SCompute__item__action = (fun ( projectee  :  stage<'a, 'v, 'o, 'q> ) -> (match (projectee) with
| SCompute (action) -> begin
     action
     end))


let uu___is_SEffect = (fun ( projectee  :  stage<'a, 'v, 'o, 'q> ) -> (match (projectee) with
| SEffect (eff) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SEffect__item__eff = (fun ( projectee  :  stage<'a, 'v, 'o, 'q> ) -> (match (projectee) with
| SEffect (eff) -> begin
     eff
     end))

type denial =
| Unregistered of Prims.string
| GateRefused of Prims.string


let uu___is_Unregistered : denial  ->  Prims.bool = (fun ( projectee  :  denial ) -> (match (projectee) with
| Unregistered (capability) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Unregistered__item__capability : denial  ->  Prims.string = (fun ( projectee  :  denial ) -> (match (projectee) with
| Unregistered (capability) -> begin
     capability
     end))


let uu___is_GateRefused : denial  ->  Prims.bool = (fun ( projectee  :  denial ) -> (match (projectee) with
| GateRefused (capability) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__GateRefused__item__capability : denial  ->  Prims.string = (fun ( projectee  :  denial ) -> (match (projectee) with
| GateRefused (capability) -> begin
     capability
     end))

type diagnostic<'d> =
| Bounded of 'd
| Denied of denial
| Failed of Prims.string * Prims.string
| PerformFailed of Prims.string * Prims.string


let uu___is_Bounded = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Bounded (inner) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bounded__item__inner = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Bounded (inner) -> begin
     inner
     end))


let uu___is_Denied = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Denied (why) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Denied__item__why = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Denied (why) -> begin
     why
     end))


let uu___is_Failed = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Failed (capability, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Failed__item__capability = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Failed (capability, reason) -> begin
     capability
     end))


let __proj__Failed__item__reason = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| Failed (capability, reason) -> begin
     reason
     end))


let uu___is_PerformFailed = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| PerformFailed (capability, reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PerformFailed__item__capability = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| PerformFailed (capability, reason) -> begin
     capability
     end))


let __proj__PerformFailed__item__reason = (fun ( projectee  :  diagnostic<'d> ) -> (match (projectee) with
| PerformFailed (capability, reason) -> begin
     reason
     end))


let capability = (fun ( e  :  server_effect<'v, 'o, 'q> ) -> (match (e) with
| RunQuery (uu___, uu___1) -> begin
     "RunQuery"
     end
| ApplyOps (uu___) -> begin
     "ApplyOps"
     end
| HostCall (fn, uu___, uu___1) -> begin
     (Prims.strcat "host:" fn)
     end
| EmitPatch (uu___) -> begin
     "EmitPatch"
     end
| Notify (uu___, uu___1) -> begin
     "Notify"
     end))

type bounded_outcome<'b, 'eff, 'd> = {bo_store : 'b; bo_effects : Prims.list<'eff>; bo_diagnostics : Prims.list<'d>}


let __proj__Mkbounded_outcome__item__bo_store = (fun ( projectee  :  bounded_outcome<'b, 'eff, 'd> ) -> (match (projectee) with
| {bo_store = bo_store; bo_effects = bo_effects; bo_diagnostics = bo_diagnostics} -> begin
     bo_store
     end))


let __proj__Mkbounded_outcome__item__bo_effects = (fun ( projectee  :  bounded_outcome<'b, 'eff, 'd> ) -> (match (projectee) with
| {bo_store = bo_store; bo_effects = bo_effects; bo_diagnostics = bo_diagnostics} -> begin
     bo_effects
     end))


let __proj__Mkbounded_outcome__item__bo_diagnostics = (fun ( projectee  :  bounded_outcome<'b, 'eff, 'd> ) -> (match (projectee) with
| {bo_store = bo_store; bo_effects = bo_effects; bo_diagnostics = bo_diagnostics} -> begin
     bo_diagnostics
     end))

type op_view<'v, 'o> =
| OEdit of 'o
| ORequire of 'o
| OChoose of 'o * Prims.list<op_view<'v, 'o>> * Prims.list<op_view<'v, 'o>> * opt<'o>
| ORepeat of Prims.nat * Prims.list<op_view<'v, 'o>>
| OEach of Prims.list<Prims.list<op_view<'v, 'o>>>
| OEachOf of Prims.string * Prims.nat * Prims.list<'v> * Prims.list<Prims.list<op_view<'v, 'o>>>


let uu___is_OEdit = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEdit (op) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OEdit__item__op = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEdit (op) -> begin
     op
     end))


let uu___is_ORequire = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| ORequire (op) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ORequire__item__op = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| ORequire (op) -> begin
     op
     end))


let uu___is_OChoose = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OChoose (entry, when_true, when_false, exit) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OChoose__item__entry = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OChoose (entry, when_true, when_false, exit) -> begin
     entry
     end))


let __proj__OChoose__item__when_true = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OChoose (entry, when_true, when_false, exit) -> begin
     when_true
     end))


let __proj__OChoose__item__when_false = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OChoose (entry, when_true, when_false, exit) -> begin
     when_false
     end))


let __proj__OChoose__item__exit = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OChoose (entry, when_true, when_false, exit) -> begin
     exit
     end))


let uu___is_ORepeat = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| ORepeat (count, body) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ORepeat__item__count = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| ORepeat (count, body) -> begin
     count
     end))


let __proj__ORepeat__item__body = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| ORepeat (count, body) -> begin
     body
     end))


let uu___is_OEach = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEach (elements) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OEach__item__elements = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEach (elements) -> begin
     elements
     end))


let uu___is_OEachOf = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEachOf (collection, ceiling, extent, elements) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OEachOf__item__collection = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEachOf (collection, ceiling, extent, elements) -> begin
     collection
     end))


let __proj__OEachOf__item__ceiling = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEachOf (collection, ceiling, extent, elements) -> begin
     ceiling
     end))


let __proj__OEachOf__item__extent = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEachOf (collection, ceiling, extent, elements) -> begin
     extent
     end))


let __proj__OEachOf__item__elements = (fun ( projectee  :  op_view<'v, 'o> ) -> (match (projectee) with
| OEachOf (collection, ceiling, extent, elements) -> begin
     elements
     end))

type witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> = {w_compute : Prims.string  ->  'a  ->  'b  ->  bounded_outcome<'b, 'eff, 'd>; w_query : Prims.string  ->  'q  ->  'b  ->  res<'b>; w_apply : 'o  ->  't  ->  res<'t>; w_op_view : 'o  ->  op_view<'v, 'o>; w_read_extent : Prims.string  ->  't  ->  opt<Prims.list<'v>>; w_assign : Prims.string  ->  'v  ->  'b  ->  'b; w_slot_refused : Prims.string  ->  opt<Prims.string>; w_undo_compute : Prims.string  ->  'a  ->  'b  ->  opt<('b  ->  'b)>}


let __proj__Mkwitness__item__w_compute = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_compute
     end))


let __proj__Mkwitness__item__w_query = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_query
     end))


let __proj__Mkwitness__item__w_apply = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_apply
     end))


let __proj__Mkwitness__item__w_op_view = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_op_view
     end))


let __proj__Mkwitness__item__w_read_extent = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_read_extent
     end))


let __proj__Mkwitness__item__w_assign = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_assign
     end))


let __proj__Mkwitness__item__w_slot_refused = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_slot_refused
     end))


let __proj__Mkwitness__item__w_undo_compute = (fun ( projectee  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) -> (match (projectee) with
| {w_compute = w_compute; w_query = w_query; w_apply = w_apply; w_op_view = w_op_view; w_read_extent = w_read_extent; w_assign = w_assign; w_slot_refused = w_slot_refused; w_undo_compute = w_undo_compute} -> begin
     w_undo_compute
     end))

type registry<'t, 'v, 'o, 'q, 'p> = {r_gate : Prims.string  ->  Prims.bool; r_policy : server_effect<'v, 'o, 'q>  ->  opt<Prims.string>; r_lookup : Prims.string  ->  opt<'p>; r_perf : 'p  ->  'v  ->  res<'v>; r_op_perform : opt<('t  ->  'o  ->  ('p * 'v))>}


let __proj__Mkregistry__item__r_gate = (fun ( projectee  :  registry<'t, 'v, 'o, 'q, 'p> ) -> (match (projectee) with
| {r_gate = r_gate; r_policy = r_policy; r_lookup = r_lookup; r_perf = r_perf; r_op_perform = r_op_perform} -> begin
     r_gate
     end))


let __proj__Mkregistry__item__r_policy = (fun ( projectee  :  registry<'t, 'v, 'o, 'q, 'p> ) -> (match (projectee) with
| {r_gate = r_gate; r_policy = r_policy; r_lookup = r_lookup; r_perf = r_perf; r_op_perform = r_op_perform} -> begin
     r_policy
     end))


let __proj__Mkregistry__item__r_lookup = (fun ( projectee  :  registry<'t, 'v, 'o, 'q, 'p> ) -> (match (projectee) with
| {r_gate = r_gate; r_policy = r_policy; r_lookup = r_lookup; r_perf = r_perf; r_op_perform = r_op_perform} -> begin
     r_lookup
     end))


let __proj__Mkregistry__item__r_perf = (fun ( projectee  :  registry<'t, 'v, 'o, 'q, 'p> ) -> (match (projectee) with
| {r_gate = r_gate; r_policy = r_policy; r_lookup = r_lookup; r_perf = r_perf; r_op_perform = r_op_perform} -> begin
     r_perf
     end))


let __proj__Mkregistry__item__r_op_perform = (fun ( projectee  :  registry<'t, 'v, 'o, 'q, 'p> ) -> (match (projectee) with
| {r_gate = r_gate; r_policy = r_policy; r_lookup = r_lookup; r_perf = r_perf; r_op_perform = r_op_perform} -> begin
     r_op_perform
     end))

type staged_call<'v, 'p> = {sc_capability : Prims.string; sc_performer : 'p; sc_args : 'v; sc_into : opt<Prims.string>}


let __proj__Mkstaged_call__item__sc_capability = (fun ( projectee  :  staged_call<'v, 'p> ) -> (match (projectee) with
| {sc_capability = sc_capability; sc_performer = sc_performer; sc_args = sc_args; sc_into = sc_into} -> begin
     sc_capability
     end))


let __proj__Mkstaged_call__item__sc_performer = (fun ( projectee  :  staged_call<'v, 'p> ) -> (match (projectee) with
| {sc_capability = sc_capability; sc_performer = sc_performer; sc_args = sc_args; sc_into = sc_into} -> begin
     sc_performer
     end))


let __proj__Mkstaged_call__item__sc_args = (fun ( projectee  :  staged_call<'v, 'p> ) -> (match (projectee) with
| {sc_capability = sc_capability; sc_performer = sc_performer; sc_args = sc_args; sc_into = sc_into} -> begin
     sc_args
     end))


let __proj__Mkstaged_call__item__sc_into = (fun ( projectee  :  staged_call<'v, 'p> ) -> (match (projectee) with
| {sc_capability = sc_capability; sc_performer = sc_performer; sc_args = sc_args; sc_into = sc_into} -> begin
     sc_into
     end))

type step<'t, 'b, 'o> =
| TEdit of 't * 'o
| TCompute of opt<('b  ->  'b)>
| TReached of Prims.string
| TEmitted of Prims.string


let uu___is_TEdit = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TEdit (pre, op) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TEdit__item__pre = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TEdit (pre, op) -> begin
     pre
     end))


let __proj__TEdit__item__op = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TEdit (pre, op) -> begin
     op
     end))


let uu___is_TCompute = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TCompute (undo) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TCompute__item__undo = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TCompute (undo) -> begin
     undo
     end))


let uu___is_TReached = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TReached (capability1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TReached__item__capability = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TReached (capability1) -> begin
     capability1
     end))


let uu___is_TEmitted = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TEmitted (capability1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TEmitted__item__capability = (fun ( projectee  :  step<'t, 'b, 'o> ) -> (match (projectee) with
| TEmitted (capability1) -> begin
     capability1
     end))

type accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> = {ac_store : store<'t, 'b>; ac_halted : Prims.bool; ac_performed : Prims.list<Prims.string>; ac_externally : Prims.list<Prims.string>; ac_staged : Prims.list<staged_call<'v, 'p>>; ac_patches : Prims.list<'o>; ac_notifications : Prims.list<(Prims.string * 'v)>; ac_client_effects : Prims.list<'eff>; ac_diagnostics : Prims.list<diagnostic<'d>>; ac_trail : Prims.list<step<'t, 'b, 'o>>}


let __proj__Mkaccumulator__item__ac_store = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_store
     end))


let __proj__Mkaccumulator__item__ac_halted = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_halted
     end))


let __proj__Mkaccumulator__item__ac_performed = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_performed
     end))


let __proj__Mkaccumulator__item__ac_externally = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_externally
     end))


let __proj__Mkaccumulator__item__ac_staged = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_staged
     end))


let __proj__Mkaccumulator__item__ac_patches = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_patches
     end))


let __proj__Mkaccumulator__item__ac_notifications = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_notifications
     end))


let __proj__Mkaccumulator__item__ac_client_effects = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_client_effects
     end))


let __proj__Mkaccumulator__item__ac_diagnostics = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_diagnostics
     end))


let __proj__Mkaccumulator__item__ac_trail = (fun ( projectee  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {ac_store = ac_store; ac_halted = ac_halted; ac_performed = ac_performed; ac_externally = ac_externally; ac_staged = ac_staged; ac_patches = ac_patches; ac_notifications = ac_notifications; ac_client_effects = ac_client_effects; ac_diagnostics = ac_diagnostics; ac_trail = ac_trail} -> begin
     ac_trail
     end))

type outcome<'t, 'b, 'v, 'o, 'eff, 'd> = {oc_store : store<'t, 'b>; oc_committed : Prims.bool; oc_performed : Prims.list<Prims.string>; oc_patches : Prims.list<'o>; oc_notifications : Prims.list<(Prims.string * 'v)>; oc_client_effects : Prims.list<'eff>; oc_diagnostics : Prims.list<diagnostic<'d>>}


let __proj__Mkoutcome__item__oc_store = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_store
     end))


let __proj__Mkoutcome__item__oc_committed = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_committed
     end))


let __proj__Mkoutcome__item__oc_performed = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_performed
     end))


let __proj__Mkoutcome__item__oc_patches = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_patches
     end))


let __proj__Mkoutcome__item__oc_notifications = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_notifications
     end))


let __proj__Mkoutcome__item__oc_client_effects = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_client_effects
     end))


let __proj__Mkoutcome__item__oc_diagnostics = (fun ( projectee  :  outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {oc_store = oc_store; oc_committed = oc_committed; oc_performed = oc_performed; oc_patches = oc_patches; oc_notifications = oc_notifications; oc_client_effects = oc_client_effects; oc_diagnostics = oc_diagnostics} -> begin
     oc_diagnostics
     end))


let halt = (fun ( cap  :  Prims.string ) ( reason  :  Prims.string ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> {ac_store = acc.ac_store; ac_halted = true; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = (Failed (cap, reason))::acc.ac_diagnostics; ac_trail = acc.ac_trail})


let deny = (fun ( why  :  denial ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> {ac_store = acc.ac_store; ac_halted = true; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = (Denied (why))::acc.ac_diagnostics; ac_trail = acc.ac_trail})


let staged_from = (fun ( cap  :  Prims.string ) ( f  :  't  ->  'o  ->  ('p * 'v) ) ( tree  :  't ) ( op  :  'o ) -> (

let uu___ = (f tree op)
in (match (uu___) with
| (tok, args) -> begin
     {sc_capability = cap; sc_performer = tok; sc_args = args; sc_into = ONone}
     end)))


let rec views = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( ops  :  Prims.list<'o> ) -> (match (ops) with
| [] -> begin
     []
     end
| (op)::rest -> begin
     ((w.w_op_view op))::(views w rest)
     end))


let rec trail_views = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( vs  :  Prims.list<op_view<'v, 'o>> ) ( tree  :  't ) -> (match (vs) with
| [] -> begin
     ROk (((tree), ([])))
     end
| (x)::rest -> begin
     (match ((trail_view w x tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', first) -> begin
     (match ((trail_views w rest tree')) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree'', more) -> begin
     ROk (((tree''), ((app first more))))
     end)
     end)
     end))
and trail_view = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( x  :  op_view<'v, 'o> ) ( tree  :  't ) -> (match (x) with
| ORequire (op) -> begin
     (match ((w.w_apply op tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (uu___) -> begin
     ROk (((tree), ([])))
     end)
     end
| OEdit (op) -> begin
     (match ((w.w_apply op tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree') -> begin
     ROk (((tree'), ((((tree), (op)))::[])))
     end)
     end
| OChoose (entry, when_true, when_false, exit) -> begin
     (

let took_true = (match ((w.w_apply entry tree)) with
| ROk (value) -> begin
     true
     end
| uu___ -> begin
     false
     end)
in (

let armed =  
if took_true then begin
     (trail_views w when_true tree)
     end else begin
     (trail_views w when_false tree)
     end
in (match (armed) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', recorded) -> begin
     (match (exit) with
| ONone -> begin
     ROk (((tree'), (recorded)))
     end
| OSome (assertion) -> begin
     (match ((w.w_apply assertion tree')) with
| ROk (uu___) -> begin
      
if took_true then begin
     ROk (((tree'), (recorded)))
     end else begin
     RErr ("the exit assertion held after the false arm")
     end
     end
| RErr (reason) -> begin
      
if took_true then begin
     RErr ((Prims.strcat "the exit assertion did not hold after the true arm: " reason))
     end else begin
     ROk (((tree'), (recorded)))
     end
     end)
     end)
     end)))
     end
| ORepeat (count, body) -> begin
     (trail_repeat w body count tree)
     end
| OEach (elements) -> begin
     (trail_each w elements tree)
     end
| OEachOf (collection, ceiling, uu___, elements) -> begin
     (match ((w.w_read_extent collection tree)) with
| OSome (xs) -> begin
      
if ((length xs) <= ceiling) then begin
     (trail_each w elements tree)
     end else begin
     RErr ("the collection\'s extent is over its declared ceiling")
     end
     end
| ONone -> begin
     RErr ("the state holds no collection under that name")
     end)
     end))
and trail_repeat = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( body  :  Prims.list<op_view<'v, 'o>> ) ( n  :  Prims.nat ) ( tree  :  't ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     ROk (((tree), ([])))
     end else begin
     (match ((trail_views w body tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', first) -> begin
     (match ((trail_repeat w body (n - (Prims.parse_int "1")) tree')) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree'', more) -> begin
     ROk (((tree''), ((app first more))))
     end)
     end)
     end)
and trail_each = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( elements  :  Prims.list<Prims.list<op_view<'v, 'o>>> ) ( tree  :  't ) -> (match (elements) with
| [] -> begin
     ROk (((tree), ([])))
     end
| (el)::rest -> begin
     (match ((trail_views w el tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', first) -> begin
     (match ((trail_each w rest tree')) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree'', more) -> begin
     ROk (((tree''), ((app first more))))
     end)
     end)
     end))


let trail_ops = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( ops  :  Prims.list<'o> ) ( tree  :  't ) -> (match ((trail_views w (views w ops) tree)) with
| RErr (uu___) -> begin
     []
     end
| ROk (uu___, recorded) -> begin
     recorded
     end))


let rec edits = (fun ( xs  :  Prims.list<('t * 'o)> ) -> (match (xs) with
| [] -> begin
     []
     end
| ((pre, op))::rest -> begin
     (TEdit (pre, op))::(edits rest)
     end))


let rec plan_views = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cap  :  Prims.string ) ( stage1  :  opt<('t  ->  'o  ->  ('p * 'v))> ) ( vs  :  Prims.list<op_view<'v, 'o>> ) ( tree  :  't ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) -> (match (vs) with
| [] -> begin
     ROk (((tree), (staged)))
     end
| (x)::rest -> begin
     (match ((plan_view w cap stage1 x tree staged)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', staged') -> begin
     (plan_views w cap stage1 rest tree' staged')
     end)
     end))
and plan_view = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cap  :  Prims.string ) ( stage1  :  opt<('t  ->  'o  ->  ('p * 'v))> ) ( x  :  op_view<'v, 'o> ) ( tree  :  't ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) -> (match (x) with
| ORequire (op) -> begin
     (match ((w.w_apply op tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (uu___) -> begin
     ROk (((tree), (staged)))
     end)
     end
| OEdit (op) -> begin
     (match ((w.w_apply op tree)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree') -> begin
     (match (stage1) with
| ONone -> begin
     ROk (((tree'), (staged)))
     end
| OSome (f) -> begin
     ROk (((tree'), (((staged_from cap f tree' op))::staged)))
     end)
     end)
     end
| OChoose (entry, when_true, when_false, exit) -> begin
     (

let took_true = (match ((w.w_apply entry tree)) with
| ROk (value) -> begin
     true
     end
| uu___ -> begin
     false
     end)
in (

let armed =  
if took_true then begin
     (plan_views w cap stage1 when_true tree staged)
     end else begin
     (plan_views w cap stage1 when_false tree staged)
     end
in (match (armed) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', staged') -> begin
     (match (exit) with
| ONone -> begin
     ROk (((tree'), (staged')))
     end
| OSome (assertion) -> begin
     (match ((w.w_apply assertion tree')) with
| ROk (uu___) -> begin
      
if took_true then begin
     ROk (((tree'), (staged')))
     end else begin
     RErr ("the exit assertion held after the false arm")
     end
     end
| RErr (reason) -> begin
      
if took_true then begin
     RErr ((Prims.strcat "the exit assertion did not hold after the true arm: " reason))
     end else begin
     ROk (((tree'), (staged')))
     end
     end)
     end)
     end)))
     end
| ORepeat (count, body) -> begin
     (plan_repeat w cap stage1 body count tree staged)
     end
| OEach (elements) -> begin
     (plan_each w cap stage1 elements tree staged)
     end
| OEachOf (collection, ceiling, uu___, elements) -> begin
     (match ((w.w_read_extent collection tree)) with
| OSome (xs) -> begin
      
if ((length xs) <= ceiling) then begin
     (plan_each w cap stage1 elements tree staged)
     end else begin
     RErr ("the collection\'s extent is over its declared ceiling")
     end
     end
| ONone -> begin
     RErr ("the state holds no collection under that name")
     end)
     end))
and plan_repeat = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cap  :  Prims.string ) ( stage1  :  opt<('t  ->  'o  ->  ('p * 'v))> ) ( body  :  Prims.list<op_view<'v, 'o>> ) ( n  :  Prims.nat ) ( tree  :  't ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) ->  
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     ROk (((tree), (staged)))
     end else begin
     (match ((plan_views w cap stage1 body tree staged)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', staged') -> begin
     (plan_repeat w cap stage1 body (n - (Prims.parse_int "1")) tree' staged')
     end)
     end)
and plan_each = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cap  :  Prims.string ) ( stage1  :  opt<('t  ->  'o  ->  ('p * 'v))> ) ( elements  :  Prims.list<Prims.list<op_view<'v, 'o>>> ) ( tree  :  't ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) -> (match (elements) with
| [] -> begin
     ROk (((tree), (staged)))
     end
| (el)::rest -> begin
     (match ((plan_views w cap stage1 el tree staged)) with
| RErr (code) -> begin
     RErr (code)
     end
| ROk (tree', staged') -> begin
     (plan_each w cap stage1 rest tree' staged')
     end)
     end))


let plan_ops = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cap  :  Prims.string ) ( stage1  :  opt<('t  ->  'o  ->  ('p * 'v))> ) ( ops  :  Prims.list<'o> ) ( tree  :  't ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) -> (plan_views w cap stage1 (views w ops) tree staged))


let rec map_bounded = (fun ( ds  :  Prims.list<'d> ) -> (match (ds) with
| [] -> begin
     []
     end
| (x)::rest -> begin
     (Bounded (x))::(map_bounded rest)
     end))


let slot_refused = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( into  :  opt<Prims.string> ) -> (match (into) with
| OSome (key) -> begin
     (w.w_slot_refused key)
     end
| ONone -> begin
     ONone
     end))


let plan_effect = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( e  :  server_effect<'v, 'o, 'q> ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (

let cap = (capability e)
in  
if (not ((reg.r_gate cap))) then begin
     (deny (GateRefused (cap)) acc)
     end else begin
     (match ((reg.r_policy e)) with
| OSome (defect) -> begin
     (halt cap defect acc)
     end
| ONone -> begin
     (

let performed = {ac_store = acc.ac_store; ac_halted = acc.ac_halted; ac_performed = (cap)::acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = acc.ac_diagnostics; ac_trail = acc.ac_trail}
in (match (e) with
| RunQuery (name, query) -> begin
     (match ((w.w_query name query performed.ac_store.st_bindings)) with
| RErr (kind) -> begin
     (halt cap kind acc)
     end
| ROk (bindings) -> begin
     {ac_store = (

let uu___ = performed.ac_store
in {st_tree = uu___.st_tree; st_bindings = bindings}); ac_halted = performed.ac_halted; ac_performed = performed.ac_performed; ac_externally = performed.ac_externally; ac_staged = performed.ac_staged; ac_patches = performed.ac_patches; ac_notifications = performed.ac_notifications; ac_client_effects = performed.ac_client_effects; ac_diagnostics = performed.ac_diagnostics; ac_trail = performed.ac_trail}
     end)
     end
| ApplyOps (ops) -> begin
     (match ((plan_ops w cap reg.r_op_perform ops performed.ac_store.st_tree acc.ac_staged)) with
| RErr (code) -> begin
     (halt cap code acc)
     end
| ROk (tree, staged) -> begin
     (

let trail = (app (rev (edits (trail_ops w ops performed.ac_store.st_tree))) acc.ac_trail)
in (match (reg.r_op_perform) with
| ONone -> begin
     {ac_store = (

let uu___ = performed.ac_store
in {st_tree = tree; st_bindings = uu___.st_bindings}); ac_halted = performed.ac_halted; ac_performed = performed.ac_performed; ac_externally = performed.ac_externally; ac_staged = performed.ac_staged; ac_patches = performed.ac_patches; ac_notifications = performed.ac_notifications; ac_client_effects = performed.ac_client_effects; ac_diagnostics = performed.ac_diagnostics; ac_trail = trail}
     end
| OSome (uu___) -> begin
     {ac_store = (

let uu___1 = acc.ac_store
in {st_tree = tree; st_bindings = uu___1.st_bindings}); ac_halted = acc.ac_halted; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = acc.ac_diagnostics; ac_trail = trail}
     end))
     end)
     end
| HostCall (fn, args, into) -> begin
     (match ((reg.r_lookup fn)) with
| ONone -> begin
     (deny (Unregistered (cap)) acc)
     end
| OSome (performer) -> begin
     (match ((slot_refused w into)) with
| OSome (reason) -> begin
     (halt cap reason acc)
     end
| ONone -> begin
     {ac_store = acc.ac_store; ac_halted = acc.ac_halted; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = ({sc_capability = cap; sc_performer = performer; sc_args = args; sc_into = into})::acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = acc.ac_diagnostics; ac_trail = (TReached (cap))::acc.ac_trail}
     end)
     end)
     end
| EmitPatch (ops) -> begin
     {ac_store = performed.ac_store; ac_halted = performed.ac_halted; ac_performed = performed.ac_performed; ac_externally = performed.ac_externally; ac_staged = performed.ac_staged; ac_patches = (app (rev ops) performed.ac_patches); ac_notifications = performed.ac_notifications; ac_client_effects = performed.ac_client_effects; ac_diagnostics = performed.ac_diagnostics; ac_trail = (TEmitted (cap))::performed.ac_trail}
     end
| Notify (channel, payload) -> begin
     {ac_store = performed.ac_store; ac_halted = performed.ac_halted; ac_performed = performed.ac_performed; ac_externally = performed.ac_externally; ac_staged = performed.ac_staged; ac_patches = performed.ac_patches; ac_notifications = (((channel), (payload)))::performed.ac_notifications; ac_client_effects = performed.ac_client_effects; ac_diagnostics = performed.ac_diagnostics; ac_trail = (TReached (cap))::performed.ac_trail}
     end))
     end)
     end))


let plan_stage = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( node_id  :  Prims.string ) ( s  :  stage<'a, 'v, 'o, 'q> ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (s) with
| SCompute (action) -> begin
     (

let out = (w.w_compute node_id action acc.ac_store.st_bindings)
in {ac_store = (

let uu___ = acc.ac_store
in {st_tree = uu___.st_tree; st_bindings = out.bo_store}); ac_halted = acc.ac_halted; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = (app (rev out.bo_effects) acc.ac_client_effects); ac_diagnostics = (app (map_bounded (rev out.bo_diagnostics)) acc.ac_diagnostics); ac_trail = (TCompute ((w.w_undo_compute node_id action acc.ac_store.st_bindings)))::acc.ac_trail})
     end
| SEffect (e) -> begin
     (plan_effect w reg e acc)
     end))


let rec plan = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( node_id  :  Prims.string ) ( stages  :  Prims.list<stage<'a, 'v, 'o, 'q>> ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (stages) with
| [] -> begin
     acc
     end
| (s)::rest -> begin
     (plan w reg node_id rest ( 
if acc.ac_halted then begin
     acc
     end else begin
     (plan_stage w reg node_id s acc)
     end))
     end))


let rec perform = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (staged) with
| [] -> begin
     acc
     end
| (call)::rest -> begin
     (match ((reg.r_perf call.sc_performer call.sc_args)) with
| RErr (reason) -> begin
     {ac_store = acc.ac_store; ac_halted = true; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = (PerformFailed (call.sc_capability, reason))::acc.ac_diagnostics; ac_trail = acc.ac_trail}
     end
| ROk (result) -> begin
     (

let recorded = {ac_store = acc.ac_store; ac_halted = acc.ac_halted; ac_performed = acc.ac_performed; ac_externally = (call.sc_capability)::acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = acc.ac_diagnostics; ac_trail = acc.ac_trail}
in (

let landed = (match (call.sc_into) with
| ONone -> begin
     recorded
     end
| OSome (key) -> begin
     {ac_store = (

let uu___ = recorded.ac_store
in {st_tree = uu___.st_tree; st_bindings = (w.w_assign key result recorded.ac_store.st_bindings)}); ac_halted = recorded.ac_halted; ac_performed = recorded.ac_performed; ac_externally = recorded.ac_externally; ac_staged = recorded.ac_staged; ac_patches = recorded.ac_patches; ac_notifications = recorded.ac_notifications; ac_client_effects = recorded.ac_client_effects; ac_diagnostics = recorded.ac_diagnostics; ac_trail = recorded.ac_trail}
     end)
in (perform w reg rest landed)))
     end)
     end))


let start = (fun ( s  :  store<'t, 'b> ) -> {ac_store = s; ac_halted = false; ac_performed = []; ac_externally = []; ac_staged = []; ac_patches = []; ac_notifications = []; ac_client_effects = []; ac_diagnostics = []; ac_trail = []})


let run_planned = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( node_id  :  Prims.string ) ( stages  :  Prims.list<stage<'a, 'v, 'o, 'q>> ) ( s  :  store<'t, 'b> ) -> (

let planned = (plan w reg node_id stages (start s))
in (

let final =  
if planned.ac_halted then begin
     planned
     end else begin
     (perform w reg (rev planned.ac_staged) planned)
     end
in  
if final.ac_halted then begin
     (({oc_store = s; oc_committed = false; oc_performed = (rev final.ac_externally); oc_patches = []; oc_notifications = []; oc_client_effects = []; oc_diagnostics = (rev final.ac_diagnostics)}), ((rev final.ac_trail)))
     end else begin
     (({oc_store = final.ac_store; oc_committed = true; oc_performed = (app (rev final.ac_performed) (rev final.ac_externally)); oc_patches = (rev final.ac_patches); oc_notifications = (rev final.ac_notifications); oc_client_effects = (rev final.ac_client_effects); oc_diagnostics = (rev final.ac_diagnostics)}), ((rev final.ac_trail)))
     end)))


let run = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( node_id  :  Prims.string ) ( stages  :  Prims.list<stage<'a, 'v, 'o, 'q>> ) ( s  :  store<'t, 'b> ) -> (

let planned = (plan w reg node_id stages (start s))
in (

let final =  
if planned.ac_halted then begin
     planned
     end else begin
     (perform w reg (rev planned.ac_staged) planned)
     end
in  
if final.ac_halted then begin
     {oc_store = s; oc_committed = false; oc_performed = (rev final.ac_externally); oc_patches = []; oc_notifications = []; oc_client_effects = []; oc_diagnostics = (rev final.ac_diagnostics)}
     end else begin
     {oc_store = final.ac_store; oc_committed = true; oc_performed = (app (rev final.ac_performed) (rev final.ac_externally)); oc_patches = (rev final.ac_patches); oc_notifications = (rev final.ac_notifications); oc_client_effects = (rev final.ac_client_effects); oc_diagnostics = (rev final.ac_diagnostics)}
     end)))


let exit_violation_reason = (fun ( took_true  :  Prims.bool ) ( answer  :  res<'t> ) -> (match (answer) with
| ROk (uu___) -> begin
     "the exit assertion held after the false arm"
     end
| RErr (reason) -> begin
     (Prims.strcat "the exit assertion did not hold after the true arm: " reason)
     end))

type journaled<'v> =
| JUnrun
| JValue of 'v
| JRefusal of Prims.string
| JIndeterminate


let uu___is_JUnrun = (fun ( projectee  :  journaled<'v> ) -> (match (projectee) with
| JUnrun -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_JValue = (fun ( projectee  :  journaled<'v> ) -> (match (projectee) with
| JValue (value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JValue__item__value = (fun ( projectee  :  journaled<'v> ) -> (match (projectee) with
| JValue (value) -> begin
     value
     end))


let uu___is_JRefusal = (fun ( projectee  :  journaled<'v> ) -> (match (projectee) with
| JRefusal (reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JRefusal__item__reason = (fun ( projectee  :  journaled<'v> ) -> (match (projectee) with
| JRefusal (reason) -> begin
     reason
     end))


let uu___is_JIndeterminate = (fun ( projectee  :  journaled<'v> ) -> (match (projectee) with
| JIndeterminate -> begin
     true
     end
| uu___ -> begin
     false
     end))

type journal<'v> = {j_step : Prims.nat  ->  journaled<'v>; j_recorded : Prims.nat  ->  opt<(Prims.string * opt<Prims.string>)>}


let __proj__Mkjournal__item__j_step = (fun ( projectee  :  journal<'v> ) -> (match (projectee) with
| {j_step = j_step; j_recorded = j_recorded} -> begin
     j_step
     end))


let __proj__Mkjournal__item__j_recorded = (fun ( projectee  :  journal<'v> ) -> (match (projectee) with
| {j_step = j_step; j_recorded = j_recorded} -> begin
     j_recorded
     end))

type durable<'v, 'p> = {d_journal : journal<'v>; d_subject : staged_call<'v, 'p>  ->  opt<Prims.string>; d_idempotent : staged_call<'v, 'p>  ->  Prims.bool; d_reinvoke : Prims.bool}


let __proj__Mkdurable__item__d_journal = (fun ( projectee  :  durable<'v, 'p> ) -> (match (projectee) with
| {d_journal = d_journal; d_subject = d_subject; d_idempotent = d_idempotent; d_reinvoke = d_reinvoke} -> begin
     d_journal
     end))


let __proj__Mkdurable__item__d_subject = (fun ( projectee  :  durable<'v, 'p> ) -> (match (projectee) with
| {d_journal = d_journal; d_subject = d_subject; d_idempotent = d_idempotent; d_reinvoke = d_reinvoke} -> begin
     d_subject
     end))


let __proj__Mkdurable__item__d_idempotent = (fun ( projectee  :  durable<'v, 'p> ) -> (match (projectee) with
| {d_journal = d_journal; d_subject = d_subject; d_idempotent = d_idempotent; d_reinvoke = d_reinvoke} -> begin
     d_idempotent
     end))


let __proj__Mkdurable__item__d_reinvoke = (fun ( projectee  :  durable<'v, 'p> ) -> (match (projectee) with
| {d_journal = d_journal; d_subject = d_subject; d_idempotent = d_idempotent; d_reinvoke = d_reinvoke} -> begin
     d_reinvoke
     end))


let indeterminate_step : Prims.string = "durable-indeterminate-step"


let replay_divergence : Prims.string = "durable-replay-divergence"

type decision<'v> =
| Serve of res<'v>
| Invoke of Prims.bool
| Diverged
| Undecided


let uu___is_Serve = (fun ( projectee  :  decision<'v> ) -> (match (projectee) with
| Serve (answer) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Serve__item__answer = (fun ( projectee  :  decision<'v> ) -> (match (projectee) with
| Serve (answer) -> begin
     answer
     end))


let uu___is_Invoke = (fun ( projectee  :  decision<'v> ) -> (match (projectee) with
| Invoke (overridden) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Invoke__item__overridden = (fun ( projectee  :  decision<'v> ) -> (match (projectee) with
| Invoke (overridden) -> begin
     overridden
     end))


let uu___is_Diverged = (fun ( projectee  :  decision<'v> ) -> (match (projectee) with
| Diverged -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Undecided = (fun ( projectee  :  decision<'v> ) -> (match (projectee) with
| Undecided -> begin
     true
     end
| uu___ -> begin
     false
     end))


let decide = (fun ( dur  :  durable<'v, 'p> ) ( k  :  Prims.nat ) ( call  :  staged_call<'v, 'p> ) -> (

let identity = ((call.sc_capability), ((dur.d_subject call)))
in (

let diverged = (match ((dur.d_journal.j_recorded k)) with
| OSome (recorded) -> begin
     (not ((Prims.op_Equals recorded identity)))
     end
| ONone -> begin
     false
     end)
in  
if diverged then begin
     Diverged
     end else begin
     (match ((dur.d_journal.j_step k)) with
| JValue (x) -> begin
     Serve (ROk (x))
     end
| JRefusal (r) -> begin
     Serve (RErr (r))
     end
| JUnrun -> begin
     Invoke (false)
     end
| JIndeterminate -> begin
      
if (dur.d_idempotent call) then begin
     Invoke (false)
     end else begin
      
if dur.d_reinvoke then begin
     Invoke (true)
     end else begin
     Undecided
     end
     end
     end)
     end)))


let land1 = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( call  :  staged_call<'v, 'p> ) ( result  :  'v ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (

let recorded = {ac_store = acc.ac_store; ac_halted = acc.ac_halted; ac_performed = acc.ac_performed; ac_externally = (call.sc_capability)::acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = acc.ac_diagnostics; ac_trail = acc.ac_trail}
in (match (call.sc_into) with
| ONone -> begin
     recorded
     end
| OSome (key) -> begin
     {ac_store = (

let uu___ = recorded.ac_store
in {st_tree = uu___.st_tree; st_bindings = (w.w_assign key result recorded.ac_store.st_bindings)}); ac_halted = recorded.ac_halted; ac_performed = recorded.ac_performed; ac_externally = recorded.ac_externally; ac_staged = recorded.ac_staged; ac_patches = recorded.ac_patches; ac_notifications = recorded.ac_notifications; ac_client_effects = recorded.ac_client_effects; ac_diagnostics = recorded.ac_diagnostics; ac_trail = recorded.ac_trail}
     end)))

type replay_result<'t, 'b, 'v, 'o, 'eff, 'd, 'p> = {rp_acc : accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p>; rp_replayed : Prims.list<Prims.nat>; rp_invoked : Prims.list<Prims.nat>; rp_indeterminate : Prims.list<Prims.nat>; rp_overrides : Prims.list<Prims.nat>}


let __proj__Mkreplay_result__item__rp_acc = (fun ( projectee  :  replay_result<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {rp_acc = rp_acc; rp_replayed = rp_replayed; rp_invoked = rp_invoked; rp_indeterminate = rp_indeterminate; rp_overrides = rp_overrides} -> begin
     rp_acc
     end))


let __proj__Mkreplay_result__item__rp_replayed = (fun ( projectee  :  replay_result<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {rp_acc = rp_acc; rp_replayed = rp_replayed; rp_invoked = rp_invoked; rp_indeterminate = rp_indeterminate; rp_overrides = rp_overrides} -> begin
     rp_replayed
     end))


let __proj__Mkreplay_result__item__rp_invoked = (fun ( projectee  :  replay_result<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {rp_acc = rp_acc; rp_replayed = rp_replayed; rp_invoked = rp_invoked; rp_indeterminate = rp_indeterminate; rp_overrides = rp_overrides} -> begin
     rp_invoked
     end))


let __proj__Mkreplay_result__item__rp_indeterminate = (fun ( projectee  :  replay_result<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {rp_acc = rp_acc; rp_replayed = rp_replayed; rp_invoked = rp_invoked; rp_indeterminate = rp_indeterminate; rp_overrides = rp_overrides} -> begin
     rp_indeterminate
     end))


let __proj__Mkreplay_result__item__rp_overrides = (fun ( projectee  :  replay_result<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (projectee) with
| {rp_acc = rp_acc; rp_replayed = rp_replayed; rp_invoked = rp_invoked; rp_indeterminate = rp_indeterminate; rp_overrides = rp_overrides} -> begin
     rp_overrides
     end))


let rec replay = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( dur  :  durable<'v, 'p> ) ( k  :  Prims.nat ) ( staged  :  Prims.list<staged_call<'v, 'p>> ) ( acc  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) -> (match (staged) with
| [] -> begin
     {rp_acc = acc; rp_replayed = []; rp_invoked = []; rp_indeterminate = []; rp_overrides = []}
     end
| (call)::rest -> begin
     (

let failed = (fun ( reason  :  Prims.string ) -> {ac_store = acc.ac_store; ac_halted = true; ac_performed = acc.ac_performed; ac_externally = acc.ac_externally; ac_staged = acc.ac_staged; ac_patches = acc.ac_patches; ac_notifications = acc.ac_notifications; ac_client_effects = acc.ac_client_effects; ac_diagnostics = (PerformFailed (call.sc_capability, reason))::acc.ac_diagnostics; ac_trail = acc.ac_trail})
in (match ((decide dur k call)) with
| Diverged -> begin
     {rp_acc = (failed replay_divergence); rp_replayed = []; rp_invoked = []; rp_indeterminate = []; rp_overrides = []}
     end
| Undecided -> begin
     {rp_acc = (failed indeterminate_step); rp_replayed = []; rp_invoked = []; rp_indeterminate = (k)::[]; rp_overrides = []}
     end
| Serve (RErr (reason)) -> begin
     {rp_acc = (failed reason); rp_replayed = (k)::[]; rp_invoked = []; rp_indeterminate = []; rp_overrides = []}
     end
| Serve (ROk (result)) -> begin
     (

let r = (replay w reg dur (k + (Prims.parse_int "1")) rest (land1 w call result acc))
in {rp_acc = r.rp_acc; rp_replayed = (k)::r.rp_replayed; rp_invoked = r.rp_invoked; rp_indeterminate = r.rp_indeterminate; rp_overrides = r.rp_overrides})
     end
| Invoke (overridden) -> begin
     (

let overrides =  
if overridden then begin
     (k)::[]
     end else begin
     []
     end
in (match ((reg.r_perf call.sc_performer call.sc_args)) with
| RErr (reason) -> begin
     {rp_acc = (failed reason); rp_replayed = []; rp_invoked = (k)::[]; rp_indeterminate = []; rp_overrides = overrides}
     end
| ROk (result) -> begin
     (

let r = (replay w reg dur (k + (Prims.parse_int "1")) rest (land1 w call result acc))
in {rp_acc = r.rp_acc; rp_replayed = r.rp_replayed; rp_invoked = (k)::r.rp_invoked; rp_indeterminate = r.rp_indeterminate; rp_overrides = (app overrides r.rp_overrides)})
     end))
     end))
     end))


let finish = (fun ( s  :  store<'t, 'b> ) ( final  :  accumulator<'t, 'b, 'v, 'o, 'eff, 'd, 'p> ) ->  
if final.ac_halted then begin
     {oc_store = s; oc_committed = false; oc_performed = (rev final.ac_externally); oc_patches = []; oc_notifications = []; oc_client_effects = []; oc_diagnostics = (rev final.ac_diagnostics)}
     end else begin
     {oc_store = final.ac_store; oc_committed = true; oc_performed = (app (rev final.ac_performed) (rev final.ac_externally)); oc_patches = (rev final.ac_patches); oc_notifications = (rev final.ac_notifications); oc_client_effects = (rev final.ac_client_effects); oc_diagnostics = (rev final.ac_diagnostics)}
     end)

type durable_outcome<'t, 'b, 'v, 'o, 'eff, 'd> = {do_outcome : outcome<'t, 'b, 'v, 'o, 'eff, 'd>; do_replayed : Prims.list<Prims.nat>; do_invoked : Prims.list<Prims.nat>; do_indeterminate : Prims.list<Prims.nat>; do_overrides : Prims.list<Prims.nat>}


let __proj__Mkdurable_outcome__item__do_outcome = (fun ( projectee  :  durable_outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {do_outcome = do_outcome; do_replayed = do_replayed; do_invoked = do_invoked; do_indeterminate = do_indeterminate; do_overrides = do_overrides} -> begin
     do_outcome
     end))


let __proj__Mkdurable_outcome__item__do_replayed = (fun ( projectee  :  durable_outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {do_outcome = do_outcome; do_replayed = do_replayed; do_invoked = do_invoked; do_indeterminate = do_indeterminate; do_overrides = do_overrides} -> begin
     do_replayed
     end))


let __proj__Mkdurable_outcome__item__do_invoked = (fun ( projectee  :  durable_outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {do_outcome = do_outcome; do_replayed = do_replayed; do_invoked = do_invoked; do_indeterminate = do_indeterminate; do_overrides = do_overrides} -> begin
     do_invoked
     end))


let __proj__Mkdurable_outcome__item__do_indeterminate = (fun ( projectee  :  durable_outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {do_outcome = do_outcome; do_replayed = do_replayed; do_invoked = do_invoked; do_indeterminate = do_indeterminate; do_overrides = do_overrides} -> begin
     do_indeterminate
     end))


let __proj__Mkdurable_outcome__item__do_overrides = (fun ( projectee  :  durable_outcome<'t, 'b, 'v, 'o, 'eff, 'd> ) -> (match (projectee) with
| {do_outcome = do_outcome; do_replayed = do_replayed; do_invoked = do_invoked; do_indeterminate = do_indeterminate; do_overrides = do_overrides} -> begin
     do_overrides
     end))


let durable_run = (fun ( w  :  witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  registry<'t, 'v, 'o, 'q, 'p> ) ( dur  :  durable<'v, 'p> ) ( node_id  :  Prims.string ) ( stages  :  Prims.list<stage<'a, 'v, 'o, 'q>> ) ( s  :  store<'t, 'b> ) -> (

let planned = (plan w reg node_id stages (start s))
in  
if planned.ac_halted then begin
     {do_outcome = (finish s planned); do_replayed = []; do_invoked = []; do_indeterminate = []; do_overrides = []}
     end else begin
     (

let r = (replay w reg dur (Prims.parse_int "0") (rev planned.ac_staged) planned)
in {do_outcome = (finish s r.rp_acc); do_replayed = r.rp_replayed; do_invoked = r.rp_invoked; do_indeterminate = r.rp_indeterminate; do_overrides = r.rp_overrides})
     end))


let read_entry_capability : Prims.string = "ReadEntry"


let entry_read_diverged : Prims.string = "durable-entry-read-diverged"


let entry_read_refusal : Prims.string  ->  Prims.string = (fun ( subject  :  Prims.string ) -> (Prims.strcat (Prims.strcat entry_read_diverged ":") subject))


let entry_read = (fun ( dur  :  journal<'v> ) ( k  :  Prims.nat ) ( subject  :  Prims.string ) ( live  :  res<'v> ) -> (

let identity = ((read_entry_capability), (OSome (subject)))
in (

let diverged = (match ((dur.j_recorded k)) with
| OSome (recorded) -> begin
     (not ((Prims.op_Equals recorded identity)))
     end
| ONone -> begin
     false
     end)
in  
if diverged then begin
     RErr ((entry_read_refusal subject))
     end else begin
     (match ((dur.j_step k)) with
| JValue (x) -> begin
     ROk (x)
     end
| JRefusal (r) -> begin
     RErr (r)
     end
| JUnrun -> begin
     live
     end
| JIndeterminate -> begin
     live
     end)
     end)))




