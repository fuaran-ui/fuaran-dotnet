module Budget
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


let sat_add : Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.int = (fun ( mx  :  Prims.int ) ( a  :  Prims.int ) ( b  :  Prims.int ) ->  
if ((a + b) > mx) then begin
     mx
     end else begin
     (a + b)
     end)


let sat_mul : Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.int = (fun ( mx  :  Prims.int ) ( a  :  Prims.int ) ( b  :  Prims.int ) ->  
if ((a * b) > mx) then begin
     mx
     end else begin
     (a * b)
     end)


let max_counted_rows : Prims.int = (Prims.parse_int "100000")

type payload =
| PAbsent
| PRows of Prims.list<unit>


let uu___is_PAbsent : payload  ->  Prims.bool = (fun ( projectee  :  payload ) -> (match (projectee) with
| PAbsent -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_PRows : payload  ->  Prims.bool = (fun ( projectee  :  payload ) -> (match (projectee) with
| PRows (items) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PRows__item__items : payload  ->  Prims.list<unit> = (fun ( projectee  :  payload ) -> (match (projectee) with
| PRows (items) -> begin
     items
     end))


let rec counted_from : Prims.int  ->  Prims.list<unit>  ->  Prims.nat  ->  Prims.nat = (fun ( cap  :  Prims.int ) ( xs  :  Prims.list<unit> ) ( acc  :  Prims.nat ) ->  
if (cap <= (Prims.parse_int "0")) then begin
     acc
     end else begin
     (match (xs) with
| [] -> begin
     acc
     end
| (uu___)::rest -> begin
     (counted_from (cap - (Prims.parse_int "1")) rest (acc + (Prims.parse_int "1")))
     end)
     end)


let counted : Prims.int  ->  Prims.list<unit>  ->  Prims.nat = (fun ( cap  :  Prims.int ) ( xs  :  Prims.list<unit> ) -> (counted_from cap xs (Prims.parse_int "0")))


let static_count : Prims.int  ->  payload  ->  Prims.nat = (fun ( cap  :  Prims.int ) ( p  :  payload ) -> (match (p) with
| PAbsent -> begin
     (Prims.parse_int "0")
     end
| PRows (items) -> begin
     (counted cap items)
     end))

type cost_shape =
| SPlain
| SWeighted of payload * Prims.int
| SRows of payload


let uu___is_SPlain : cost_shape  ->  Prims.bool = (fun ( projectee  :  cost_shape ) -> (match (projectee) with
| SPlain -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SWeighted : cost_shape  ->  Prims.bool = (fun ( projectee  :  cost_shape ) -> (match (projectee) with
| SWeighted (rows, weights) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SWeighted__item__rows : cost_shape  ->  payload = (fun ( projectee  :  cost_shape ) -> (match (projectee) with
| SWeighted (rows, weights) -> begin
     rows
     end))


let __proj__SWeighted__item__weights : cost_shape  ->  Prims.int = (fun ( projectee  :  cost_shape ) -> (match (projectee) with
| SWeighted (rows, weights) -> begin
     weights
     end))


let uu___is_SRows : cost_shape  ->  Prims.bool = (fun ( projectee  :  cost_shape ) -> (match (projectee) with
| SRows (rows) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SRows__item__rows : cost_shape  ->  payload = (fun ( projectee  :  cost_shape ) -> (match (projectee) with
| SRows (rows) -> begin
     rows
     end))

type nd =
| Nd of cost_shape * Prims.list<nd>


let uu___is_Nd : nd  ->  Prims.bool = (fun ( projectee  :  nd ) -> true)


let __proj__Nd__item__shape : nd  ->  cost_shape = (fun ( projectee  :  nd ) -> (match (projectee) with
| Nd (shape, kids) -> begin
     shape
     end))


let __proj__Nd__item__kids : nd  ->  Prims.list<nd> = (fun ( projectee  :  nd ) -> (match (projectee) with
| Nd (shape, kids) -> begin
     kids
     end))


let kids : nd  ->  Prims.list<nd> = (fun ( n  :  nd ) -> (match (n) with
| Nd (uu___, ks) -> begin
     ks
     end))


let node_cost : Prims.int  ->  Prims.int  ->  nd  ->  Prims.int = (fun ( mx  :  Prims.int ) ( cap  :  Prims.int ) ( n  :  nd ) -> (match (n) with
| Nd (SPlain, uu___) -> begin
     (Prims.parse_int "1")
     end
| Nd (SWeighted (rows, weights), uu___) -> begin
     (sat_add mx (Prims.parse_int "1") (sat_mul mx (static_count cap rows) ( 
if (weights < (Prims.parse_int "1")) then begin
     (Prims.parse_int "1")
     end else begin
     weights
     end)))
     end
| Nd (SRows (rows), uu___) -> begin
     (sat_add mx (Prims.parse_int "1") (static_count cap rows))
     end))


let rec node_size : nd  ->  Prims.nat = (fun ( n  :  nd ) -> (match (n) with
| Nd (uu___, ks) -> begin
     ((Prims.parse_int "1") + (forest_size ks))
     end))
and forest_size : Prims.list<nd>  ->  Prims.nat = (fun ( ns  :  Prims.list<nd> ) -> (match (ns) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (n)::rest -> begin
     ((node_size n) + (forest_size rest))
     end))


let rec push_all : Prims.list<nd>  ->  Prims.list<nd>  ->  Prims.list<nd> = (fun ( ks  :  Prims.list<nd> ) ( stack  :  Prims.list<nd> ) -> (match (ks) with
| [] -> begin
     stack
     end
| (k)::rest -> begin
     (push_all rest ((k)::stack))
     end))


let rec walk : Prims.int  ->  Prims.int  ->  Prims.int  ->  Prims.list<nd>  ->  Prims.int  ->  Prims.int = (fun ( mx  :  Prims.int ) ( cap  :  Prims.int ) ( ceiling  :  Prims.int ) ( pending  :  Prims.list<nd> ) ( running  :  Prims.int ) ->  
if (running > ceiling) then begin
     running
     end else begin
     (match (pending) with
| [] -> begin
     running
     end
| (cur)::rest -> begin
     (walk mx cap ceiling (push_all (kids cur) rest) (sat_add mx running (node_cost mx cap cur)))
     end)
     end)


let tree_cost : Prims.int  ->  Prims.int  ->  Prims.int  ->  nd  ->  Prims.int = (fun ( mx  :  Prims.int ) ( cap  :  Prims.int ) ( ceiling  :  Prims.int ) ( root  :  nd ) -> (walk mx cap ceiling ((root)::[]) (Prims.parse_int "0")))

type act =
| ALeaf
| AChain of Prims.list<act>


let uu___is_ALeaf : act  ->  Prims.bool = (fun ( projectee  :  act ) -> (match (projectee) with
| ALeaf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_AChain : act  ->  Prims.bool = (fun ( projectee  :  act ) -> (match (projectee) with
| AChain (ops) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AChain__item__ops : act  ->  Prims.list<act> = (fun ( projectee  :  act ) -> (match (projectee) with
| AChain (ops) -> begin
     ops
     end))


let rec action_cascade_cost : act  ->  Prims.int = (fun ( a  :  act ) -> (match (a) with
| AChain (ops) -> begin
     (cascade_sum ops)
     end
| ALeaf -> begin
     (Prims.parse_int "1")
     end))
and cascade_sum : Prims.list<act>  ->  Prims.int = (fun ( ops  :  Prims.list<act> ) -> (match (ops) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (x)::rest -> begin
     ((action_cascade_cost x) + (cascade_sum rest))
     end))

type budget = {b_max_actions : Prims.int; b_max_nodes : Prims.int}


let __proj__Mkbudget__item__b_max_actions : budget  ->  Prims.int = (fun ( projectee  :  budget ) -> (match (projectee) with
| {b_max_actions = b_max_actions; b_max_nodes = b_max_nodes} -> begin
     b_max_actions
     end))


let __proj__Mkbudget__item__b_max_nodes : budget  ->  Prims.int = (fun ( projectee  :  budget ) -> (match (projectee) with
| {b_max_actions = b_max_actions; b_max_nodes = b_max_nodes} -> begin
     b_max_nodes
     end))

type session<'v> = {se_store : 'v; se_node_count : Prims.int}


let __proj__Mksession__item__se_store = (fun ( projectee  :  session<'v> ) -> (match (projectee) with
| {se_store = se_store; se_node_count = se_node_count} -> begin
     se_store
     end))


let __proj__Mksession__item__se_node_count = (fun ( projectee  :  session<'v> ) -> (match (projectee) with
| {se_store = se_store; se_node_count = se_node_count} -> begin
     se_node_count
     end))

type step_output = {so_patches : Prims.int; so_effects : Prims.int; so_rejected : opt<Prims.string>}


let __proj__Mkstep_output__item__so_patches : step_output  ->  Prims.int = (fun ( projectee  :  step_output ) -> (match (projectee) with
| {so_patches = so_patches; so_effects = so_effects; so_rejected = so_rejected} -> begin
     so_patches
     end))


let __proj__Mkstep_output__item__so_effects : step_output  ->  Prims.int = (fun ( projectee  :  step_output ) -> (match (projectee) with
| {so_patches = so_patches; so_effects = so_effects; so_rejected = so_rejected} -> begin
     so_effects
     end))


let __proj__Mkstep_output__item__so_rejected : step_output  ->  opt<Prims.string> = (fun ( projectee  :  step_output ) -> (match (projectee) with
| {so_patches = so_patches; so_effects = so_effects; so_rejected = so_rejected} -> begin
     so_rejected
     end))

type admitted_branch<'v> = {run : session<'v>  ->  Prims.string  ->  act  ->  (session<'v> * step_output)}


let __proj__Mkadmitted_branch__item__run = (fun ( projectee  :  admitted_branch<'v> ) -> (match (projectee) with
| {run = run} -> begin
     run
     end))


let budget_message : Prims.string  ->  Prims.int  ->  Prims.string  ->  Prims.int  ->  Prims.string = (fun ( what  :  Prims.string ) ( cost  :  Prims.int ) ( cap_name  :  Prims.string ) ( cap  :  Prims.int ) -> (Prims.strcat what (Prims.strcat " " (Prims.strcat (Prims.string_of_int cost) (Prims.strcat " exceeds " (Prims.strcat cap_name (Prims.strcat " " (Prims.string_of_int cap))))))))


let refusal : Prims.string  ->  step_output = (fun ( reason  :  Prims.string ) -> {so_patches = (Prims.parse_int "0"); so_effects = (Prims.parse_int "0"); so_rejected = OSome (reason)})


let step = (fun ( bud  :  budget ) ( ab  :  admitted_branch<'v> ) ( sess  :  session<'v> ) ( node_id  :  Prims.string ) ( a  :  act ) -> (

let cost = (action_cascade_cost a)
in  
if (cost > bud.b_max_actions) then begin
     ((sess), ((refusal (budget_message "action cascade cost" cost "MaxActions" bud.b_max_actions))))
     end else begin
      
if (sess.se_node_count > bud.b_max_nodes) then begin
     ((sess), ((refusal (budget_message "tree cost" sess.se_node_count "MaxNodes" bud.b_max_nodes))))
     end else begin
     (ab.run sess node_id a)
     end
     end))


let breached = (fun ( bud  :  budget ) ( sess  :  session<'v> ) ( a  :  act ) -> (((action_cascade_cost a) > bud.b_max_actions) || (sess.se_node_count > bud.b_max_nodes)))




