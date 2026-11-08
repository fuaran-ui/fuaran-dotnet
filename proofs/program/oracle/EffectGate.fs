module EffectGate

let rec admitted : (Prims.string  ->  Prims.bool)  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( gate  :  Prims.string  ->  Prims.bool ) ( xs  :  Prims.list<Prims.string> ) -> (match (xs) with
| [] -> begin
     true
     end
| (c)::rest -> begin
     ((gate c) && (admitted gate rest))
     end))

type contract<'v> = {ct_name : Prims.string; ct_holds : 'v  ->  Prims.bool}


let __proj__Mkcontract__item__ct_name = (fun ( projectee  :  contract<'v> ) -> (match (projectee) with
| {ct_name = ct_name; ct_holds = ct_holds} -> begin
     ct_name
     end))


let __proj__Mkcontract__item__ct_holds = (fun ( projectee  :  contract<'v> ) -> (match (projectee) with
| {ct_name = ct_name; ct_holds = ct_holds} -> begin
     ct_holds
     end))


let contract_reason = (fun ( c  :  contract<'v> ) -> (Prims.strcat "return-contract:" c.ct_name))


let check_return = (fun ( c  :  contract<'v> ) ( perf  :  'p  ->  'v  ->  Staging.res<'v> ) ( tok  :  'p ) ( args  :  'v ) -> (match ((perf tok args)) with
| Staging.RErr (reason) -> begin
     Staging.RErr (reason)
     end
| Staging.ROk (result) -> begin
      
if (c.ct_holds result) then begin
     Staging.ROk (result)
     end else begin
     Staging.RErr ((contract_reason c))
     end
     end))


let checked_by = (fun ( ct  :  'p  ->  Staging.opt<contract<'v>> ) ( perf  :  'p  ->  'v  ->  Staging.res<'v> ) ( tok  :  'p ) ( args  :  'v ) -> (match ((ct tok)) with
| Staging.ONone -> begin
     (perf tok args)
     end
| Staging.OSome (c) -> begin
     (check_return c perf tok args)
     end))

type op_contract<'t, 'o, 'v> = {oc_name : Prims.string; oc_holds : 't  ->  'o  ->  'v  ->  Prims.bool}


let __proj__Mkop_contract__item__oc_name = (fun ( projectee  :  op_contract<'t, 'o, 'v> ) -> (match (projectee) with
| {oc_name = oc_name; oc_holds = oc_holds} -> begin
     oc_name
     end))


let __proj__Mkop_contract__item__oc_holds = (fun ( projectee  :  op_contract<'t, 'o, 'v> ) -> (match (projectee) with
| {oc_name = oc_name; oc_holds = oc_holds} -> begin
     oc_holds
     end))


let op_contract_reason = (fun ( c  :  op_contract<'t, 'o, 'v> ) -> (Prims.strcat "return-contract:" c.oc_name))


let op_at = (fun ( c  :  op_contract<'t, 'o, 'v> ) ( tree  :  't ) ( op  :  'o ) -> {ct_name = c.oc_name; ct_holds = (c.oc_holds tree op)})


let check_op = (fun ( c  :  op_contract<'t, 'o, 'v> ) ( perform  :  't  ->  'o  ->  Staging.res<'v> ) ( tree  :  't ) ( op  :  'o ) -> (match ((perform tree op)) with
| Staging.RErr (reason) -> begin
     Staging.RErr (reason)
     end
| Staging.ROk (receipt) -> begin
      
if (c.oc_holds tree op receipt) then begin
     Staging.ROk (receipt)
     end else begin
     Staging.RErr ((op_contract_reason c))
     end
     end))


let op_behaviour = (fun ( f  :  't  ->  'o  ->  ('p * 'v) ) ( perf  :  'p  ->  'v  ->  Staging.res<'v> ) ( tree  :  't ) ( op  :  'o ) -> (

let uu___ = (f tree op)
in (match (uu___) with
| (tok, args) -> begin
     (perf tok args)
     end)))

type window_limit = {wl_count : Staging.opt<(Prims.nat * Prims.int)>; wl_quantity : Staging.opt<(Prims.nat * Prims.int)>; wl_dwell : Staging.opt<Prims.int>}


let __proj__Mkwindow_limit__item__wl_count : window_limit  ->  Staging.opt<(Prims.nat * Prims.int)> = (fun ( projectee  :  window_limit ) -> (match (projectee) with
| {wl_count = wl_count; wl_quantity = wl_quantity; wl_dwell = wl_dwell} -> begin
     wl_count
     end))


let __proj__Mkwindow_limit__item__wl_quantity : window_limit  ->  Staging.opt<(Prims.nat * Prims.int)> = (fun ( projectee  :  window_limit ) -> (match (projectee) with
| {wl_count = wl_count; wl_quantity = wl_quantity; wl_dwell = wl_dwell} -> begin
     wl_quantity
     end))


let __proj__Mkwindow_limit__item__wl_dwell : window_limit  ->  Staging.opt<Prims.int> = (fun ( projectee  :  window_limit ) -> (match (projectee) with
| {wl_count = wl_count; wl_quantity = wl_quantity; wl_dwell = wl_dwell} -> begin
     wl_dwell
     end))

type window_counter = {wc_since : Prims.int; wc_count : Prims.nat; wc_qsince : Prims.int; wc_quantity : Prims.nat; wc_last : Staging.opt<Prims.int>}


let __proj__Mkwindow_counter__item__wc_since : window_counter  ->  Prims.int = (fun ( projectee  :  window_counter ) -> (match (projectee) with
| {wc_since = wc_since; wc_count = wc_count; wc_qsince = wc_qsince; wc_quantity = wc_quantity; wc_last = wc_last} -> begin
     wc_since
     end))


let __proj__Mkwindow_counter__item__wc_count : window_counter  ->  Prims.nat = (fun ( projectee  :  window_counter ) -> (match (projectee) with
| {wc_since = wc_since; wc_count = wc_count; wc_qsince = wc_qsince; wc_quantity = wc_quantity; wc_last = wc_last} -> begin
     wc_count
     end))


let __proj__Mkwindow_counter__item__wc_qsince : window_counter  ->  Prims.int = (fun ( projectee  :  window_counter ) -> (match (projectee) with
| {wc_since = wc_since; wc_count = wc_count; wc_qsince = wc_qsince; wc_quantity = wc_quantity; wc_last = wc_last} -> begin
     wc_qsince
     end))


let __proj__Mkwindow_counter__item__wc_quantity : window_counter  ->  Prims.nat = (fun ( projectee  :  window_counter ) -> (match (projectee) with
| {wc_since = wc_since; wc_count = wc_count; wc_qsince = wc_qsince; wc_quantity = wc_quantity; wc_last = wc_last} -> begin
     wc_quantity
     end))


let __proj__Mkwindow_counter__item__wc_last : window_counter  ->  Staging.opt<Prims.int> = (fun ( projectee  :  window_counter ) -> (match (projectee) with
| {wc_since = wc_since; wc_count = wc_count; wc_qsince = wc_qsince; wc_quantity = wc_quantity; wc_last = wc_last} -> begin
     wc_last
     end))


let fresh_counter : window_counter = {wc_since = (Prims.parse_int "0"); wc_count = (Prims.parse_int "0"); wc_qsince = (Prims.parse_int "0"); wc_quantity = (Prims.parse_int "0"); wc_last = Staging.ONone}


let within : Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.bool = (fun ( since  :  Prims.int ) ( w  :  Prims.int ) ( now  :  Prims.int ) -> ((now - since) < w))


let count_window : window_counter  ->  Prims.int  ->  Prims.int  ->  (Prims.int * Prims.nat) = (fun ( c  :  window_counter ) ( w  :  Prims.int ) ( now  :  Prims.int ) ->  
if ((c.wc_count > (Prims.parse_int "0")) && (within c.wc_since w now)) then begin
     ((c.wc_since), (c.wc_count))
     end else begin
     ((now), ((Prims.parse_int "0")))
     end)


let quantity_window : window_counter  ->  Prims.int  ->  Prims.int  ->  (Prims.int * Prims.nat) = (fun ( c  :  window_counter ) ( w  :  Prims.int ) ( now  :  Prims.int ) ->  
if ((c.wc_quantity > (Prims.parse_int "0")) && (within c.wc_qsince w now)) then begin
     ((c.wc_qsince), (c.wc_quantity))
     end else begin
     ((now), ((Prims.parse_int "0")))
     end)

type window_decision =
| Admitted of window_counter
| WithinDwell
| OverCount
| OverQuantity


let uu___is_Admitted : window_decision  ->  Prims.bool = (fun ( projectee  :  window_decision ) -> (match (projectee) with
| Admitted (counter) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Admitted__item__counter : window_decision  ->  window_counter = (fun ( projectee  :  window_decision ) -> (match (projectee) with
| Admitted (counter) -> begin
     counter
     end))


let uu___is_WithinDwell : window_decision  ->  Prims.bool = (fun ( projectee  :  window_decision ) -> (match (projectee) with
| WithinDwell -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OverCount : window_decision  ->  Prims.bool = (fun ( projectee  :  window_decision ) -> (match (projectee) with
| OverCount -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OverQuantity : window_decision  ->  Prims.bool = (fun ( projectee  :  window_decision ) -> (match (projectee) with
| OverQuantity -> begin
     true
     end
| uu___ -> begin
     false
     end))


let dwell_admits : window_limit  ->  Prims.int  ->  window_counter  ->  Prims.bool = (fun ( lim  :  window_limit ) ( now  :  Prims.int ) ( c  :  window_counter ) -> (match (lim.wl_dwell) with
| Staging.ONone -> begin
     true
     end
| Staging.OSome (d) -> begin
     (match (c.wc_last) with
| Staging.ONone -> begin
     true
     end
| Staging.OSome (last) -> begin
     ((now - last) >= d)
     end)
     end))


let count_admits : window_limit  ->  Prims.int  ->  window_counter  ->  Prims.bool = (fun ( lim  :  window_limit ) ( now  :  Prims.int ) ( c  :  window_counter ) -> (match (lim.wl_count) with
| Staging.ONone -> begin
     true
     end
| Staging.OSome (cap, w) -> begin
     (

let uu___ = (count_window c w now)
in (match (uu___) with
| (uu___1, n) -> begin
     ((n + (Prims.parse_int "1")) <= cap)
     end))
     end))


let quantity_admits : window_limit  ->  Prims.int  ->  Prims.nat  ->  window_counter  ->  Prims.bool = (fun ( lim  :  window_limit ) ( now  :  Prims.int ) ( q  :  Prims.nat ) ( c  :  window_counter ) -> (match (lim.wl_quantity) with
| Staging.ONone -> begin
     true
     end
| Staging.OSome (cap, w) -> begin
     (

let uu___ = (quantity_window c w now)
in (match (uu___) with
| (uu___1, sum) -> begin
     ((sum + q) <= cap)
     end))
     end))


let decide : window_limit  ->  Prims.int  ->  Prims.nat  ->  window_counter  ->  window_decision = (fun ( lim  :  window_limit ) ( now  :  Prims.int ) ( q  :  Prims.nat ) ( c  :  window_counter ) ->  
if (not ((dwell_admits lim now c))) then begin
     WithinDwell
     end else begin
      
if (not ((count_admits lim now c))) then begin
     OverCount
     end else begin
      
if (not ((quantity_admits lim now q c))) then begin
     OverQuantity
     end else begin
     (

let uu___ = (match (lim.wl_count) with
| Staging.ONone -> begin
     ((now), ((Prims.parse_int "0")))
     end
| Staging.OSome (uu___1, w) -> begin
     (count_window c w now)
     end)
in (match (uu___) with
| (since, n) -> begin
     (

let uu___1 = (match (lim.wl_quantity) with
| Staging.ONone -> begin
     ((now), ((Prims.parse_int "0")))
     end
| Staging.OSome (uu___2, w) -> begin
     (quantity_window c w now)
     end)
in (match (uu___1) with
| (qsince, sum) -> begin
     Admitted ({wc_since = since; wc_count = (n + (Prims.parse_int "1")); wc_qsince = qsince; wc_quantity = (sum + q); wc_last = Staging.OSome (now)})
     end))
     end))
     end
     end
     end)




