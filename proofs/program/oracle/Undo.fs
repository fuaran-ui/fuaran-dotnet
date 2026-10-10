module Undo
type undo_class<'t, 'o> =
| Inverse of ('t  ->  Prims.list<'o>)
| Compensate of ('t  ->  Prims.list<'o>)
| OneWay of Prims.string


let uu___is_Inverse = (fun ( projectee  :  undo_class<'t, 'o> ) -> (match (projectee) with
| Inverse (inverse) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Inverse__item__inverse = (fun ( projectee  :  undo_class<'t, 'o> ) -> (match (projectee) with
| Inverse (inverse) -> begin
     inverse
     end))


let uu___is_Compensate = (fun ( projectee  :  undo_class<'t, 'o> ) -> (match (projectee) with
| Compensate (compensation) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Compensate__item__compensation = (fun ( projectee  :  undo_class<'t, 'o> ) -> (match (projectee) with
| Compensate (compensation) -> begin
     compensation
     end))


let uu___is_OneWay = (fun ( projectee  :  undo_class<'t, 'o> ) -> (match (projectee) with
| OneWay (reason) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OneWay__item__reason = (fun ( projectee  :  undo_class<'t, 'o> ) -> (match (projectee) with
| OneWay (reason) -> begin
     reason
     end))

type defect =
| CompensatedOp
| OneWayOp
| OpaqueHostCall
| OutboundNotification
| EmittedPatch
| ComputeOutsideFragment


let uu___is_CompensatedOp : defect  ->  Prims.bool = (fun ( projectee  :  defect ) -> (match (projectee) with
| CompensatedOp -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OneWayOp : defect  ->  Prims.bool = (fun ( projectee  :  defect ) -> (match (projectee) with
| OneWayOp -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OpaqueHostCall : defect  ->  Prims.bool = (fun ( projectee  :  defect ) -> (match (projectee) with
| OpaqueHostCall -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OutboundNotification : defect  ->  Prims.bool = (fun ( projectee  :  defect ) -> (match (projectee) with
| OutboundNotification -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_EmittedPatch : defect  ->  Prims.bool = (fun ( projectee  :  defect ) -> (match (projectee) with
| EmittedPatch -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ComputeOutsideFragment : defect  ->  Prims.bool = (fun ( projectee  :  defect ) -> (match (projectee) with
| ComputeOutsideFragment -> begin
     true
     end
| uu___ -> begin
     false
     end))

type verdict =
| Reversible
| Compensable
| OneWayVerdict
| Unknown


let uu___is_Reversible : verdict  ->  Prims.bool = (fun ( projectee  :  verdict ) -> (match (projectee) with
| Reversible -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Compensable : verdict  ->  Prims.bool = (fun ( projectee  :  verdict ) -> (match (projectee) with
| Compensable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_OneWayVerdict : verdict  ->  Prims.bool = (fun ( projectee  :  verdict ) -> (match (projectee) with
| OneWayVerdict -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Unknown : verdict  ->  Prims.bool = (fun ( projectee  :  verdict ) -> (match (projectee) with
| Unknown -> begin
     true
     end
| uu___ -> begin
     false
     end))


let grade : defect  ->  verdict = (fun ( d  :  defect ) -> (match (d) with
| CompensatedOp -> begin
     Compensable
     end
| OneWayOp -> begin
     OneWayVerdict
     end
| OpaqueHostCall -> begin
     OneWayVerdict
     end
| OutboundNotification -> begin
     OneWayVerdict
     end
| EmittedPatch -> begin
     Unknown
     end
| ComputeOutsideFragment -> begin
     Unknown
     end))


let worst : verdict  ->  verdict  ->  verdict = (fun ( x  :  verdict ) ( y  :  verdict ) ->  
if ((match (x) with
| OneWayVerdict -> begin
     true
     end
| uu___ -> begin
     false
     end) || (match (y) with
| OneWayVerdict -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     OneWayVerdict
     end else begin
      
if ((match (x) with
| Unknown -> begin
     true
     end
| uu___ -> begin
     false
     end) || (match (y) with
| Unknown -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     Unknown
     end else begin
      
if ((match (x) with
| Compensable -> begin
     true
     end
| uu___ -> begin
     false
     end) || (match (y) with
| Compensable -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     Compensable
     end else begin
     Reversible
     end
     end
     end)


let rec verdict_of : Prims.list<defect>  ->  verdict = (fun ( ds  :  Prims.list<defect> ) -> (match (ds) with
| [] -> begin
     Reversible
     end
| (d)::rest -> begin
     (worst (grade d) (verdict_of rest))
     end))


let rec view_defects = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( vs  :  Prims.list<Staging.op_view<'v, 'o>> ) -> (match (vs) with
| [] -> begin
     []
     end
| (x)::rest -> begin
     (Staging.app (view_defect cls x) (view_defects cls rest))
     end))
and view_defect = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( x  :  Staging.op_view<'v, 'o> ) -> (match (x) with
| Staging.OEdit (op) -> begin
     (match ((cls op)) with
| Inverse (uu___) -> begin
     []
     end
| Compensate (uu___) -> begin
     (CompensatedOp)::[]
     end
| OneWay (uu___) -> begin
     (OneWayOp)::[]
     end)
     end
| Staging.ORequire (uu___) -> begin
     []
     end
| Staging.OChoose (uu___, when_true, when_false, uu___1) -> begin
     (Staging.app (view_defects cls when_true) (view_defects cls when_false))
     end
| Staging.ORepeat (uu___, body) -> begin
     (view_defects cls body)
     end
| Staging.OEach (elements) -> begin
     (view_defects_each cls elements)
     end
| Staging.OEachOf (uu___, uu___1, uu___2, elements) -> begin
     (view_defects_each cls elements)
     end
| Staging.OLetOf (uu___, uu___1, body) -> begin
     (view_defects cls body)
     end))
and view_defects_each = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( elements  :  Prims.list<Prims.list<Staging.op_view<'v, 'o>>> ) -> (match (elements) with
| [] -> begin
     []
     end
| (el)::rest -> begin
     (Staging.app (view_defects cls el) (view_defects_each cls rest))
     end))


let rec mem : defect  ->  Prims.list<defect>  ->  Prims.bool = (fun ( d  :  defect ) ( ds  :  Prims.list<defect> ) -> (match (ds) with
| [] -> begin
     false
     end
| (x)::rest -> begin
     ((Prims.op_Equals d x) || (mem d rest))
     end))


let rec distinct_from : Prims.list<defect>  ->  Prims.list<defect>  ->  Prims.list<defect> = (fun ( seen  :  Prims.list<defect> ) ( ds  :  Prims.list<defect> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::rest -> begin
      
if (mem d seen) then begin
     (distinct_from seen rest)
     end else begin
     (d)::(distinct_from ((d)::seen) rest)
     end
     end))


let stage_defects = (fun ( w  :  Staging.witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cls  :  'o  ->  undo_class<'t, 'o> ) ( reversible  :  'a  ->  Prims.bool ) ( s  :  Staging.stage<'a, 'v, 'o, 'q> ) -> (match (s) with
| Staging.SCompute (action) -> begin
      
if (reversible action) then begin
     []
     end else begin
     (ComputeOutsideFragment)::[]
     end
     end
| Staging.SEffect (e) -> begin
     (match (e) with
| Staging.RunQuery (uu___, uu___1) -> begin
     []
     end
| Staging.ApplyOps (ops) -> begin
     (distinct_from [] (view_defects cls (Staging.views w ops)))
     end
| Staging.HostCall (uu___, uu___1, uu___2) -> begin
     (OpaqueHostCall)::[]
     end
| Staging.EmitPatch (uu___) -> begin
     (EmittedPatch)::[]
     end
| Staging.Notify (uu___, uu___1) -> begin
     (OutboundNotification)::[]
     end)
     end))


let rec positioned : Prims.nat  ->  Prims.list<defect>  ->  Prims.list<(Prims.nat * defect)> = (fun ( k  :  Prims.nat ) ( ds  :  Prims.list<defect> ) -> (match (ds) with
| [] -> begin
     []
     end
| (d)::rest -> begin
     (((k), (d)))::(positioned k rest)
     end))


let rec reasons_from = (fun ( w  :  Staging.witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cls  :  'o  ->  undo_class<'t, 'o> ) ( reversible  :  'a  ->  Prims.bool ) ( stages  :  Prims.list<Staging.stage<'a, 'v, 'o, 'q>> ) ( k  :  Prims.nat ) -> (match (stages) with
| [] -> begin
     []
     end
| (s)::rest -> begin
     (Staging.app (positioned k (stage_defects w cls reversible s)) (reasons_from w cls reversible rest (k + (Prims.parse_int "1"))))
     end))


let reasons = (fun ( w  :  Staging.witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cls  :  'o  ->  undo_class<'t, 'o> ) ( reversible  :  'a  ->  Prims.bool ) ( stages  :  Prims.list<Staging.stage<'a, 'v, 'o, 'q>> ) -> (reasons_from w cls reversible stages (Prims.parse_int "0")))


let rec defects_of : Prims.list<(Prims.nat * defect)>  ->  Prims.list<defect> = (fun ( rs  :  Prims.list<(Prims.nat * defect)> ) -> (match (rs) with
| [] -> begin
     []
     end
| ((uu___, d))::rest -> begin
     (d)::(defects_of rest)
     end))


let posture = (fun ( w  :  Staging.witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( cls  :  'o  ->  undo_class<'t, 'o> ) ( reversible  :  'a  ->  Prims.bool ) ( stages  :  Prims.list<Staging.stage<'a, 'v, 'o, 'q>> ) -> (verdict_of (defects_of (reasons w cls reversible stages))))

type refusal = {rf_code : Prims.string; rf_step : Prims.nat; rf_reason : Prims.string}


let __proj__Mkrefusal__item__rf_code : refusal  ->  Prims.string = (fun ( projectee  :  refusal ) -> (match (projectee) with
| {rf_code = rf_code; rf_step = rf_step; rf_reason = rf_reason} -> begin
     rf_code
     end))


let __proj__Mkrefusal__item__rf_step : refusal  ->  Prims.nat = (fun ( projectee  :  refusal ) -> (match (projectee) with
| {rf_code = rf_code; rf_step = rf_step; rf_reason = rf_reason} -> begin
     rf_step
     end))


let __proj__Mkrefusal__item__rf_reason : refusal  ->  Prims.string = (fun ( projectee  :  refusal ) -> (match (projectee) with
| {rf_code = rf_code; rf_step = rf_step; rf_reason = rf_reason} -> begin
     rf_reason
     end))


let render : refusal  ->  Prims.string = (fun ( r  :  refusal ) -> (Prims.strcat (Prims.strcat (Prims.strcat r.rf_code "@") (Prims.string_of_int r.rf_step)) (Prims.strcat ": " r.rf_reason)))


let rec first_refused = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( steps  :  Prims.list<Staging.step<'t, 'b, 'o>> ) ( k  :  Prims.nat ) -> (match (steps) with
| [] -> begin
     Staging.ONone
     end
| (Staging.TEdit (uu___, op))::rest -> begin
     (match ((cls op)) with
| OneWay (reason) -> begin
     Staging.OSome ({rf_code = "undo-one-way-step"; rf_step = k; rf_reason = reason})
     end
| uu___1 -> begin
     (first_refused cls rest (k + (Prims.parse_int "1")))
     end)
     end
| (Staging.TCompute (Staging.ONone))::uu___ -> begin
     Staging.OSome ({rf_code = "undo-undecidable-step"; rf_step = k; rf_reason = "a compute stage outside the reversible fragment, or whose trace is not restorable"})
     end
| (Staging.TCompute (Staging.OSome (uu___)))::rest -> begin
     (first_refused cls rest (k + (Prims.parse_int "1")))
     end
| (Staging.TReached (cap))::uu___ -> begin
     Staging.OSome ({rf_code = "undo-one-way-step"; rf_step = k; rf_reason = cap})
     end
| (Staging.TEmitted (cap))::uu___ -> begin
     Staging.OSome ({rf_code = "undo-undecidable-step"; rf_step = k; rf_reason = cap})
     end))


let undo_ops = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( pre  :  't ) ( op  :  'o ) -> (match ((cls op)) with
| Inverse (f) -> begin
     (f pre)
     end
| Compensate (f) -> begin
     (f pre)
     end
| OneWay (uu___) -> begin
     []
     end))


let rec inverse_plan = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( reversed  :  Prims.list<Staging.step<'t, 'b, 'o>> ) -> (match (reversed) with
| [] -> begin
     []
     end
| (Staging.TEdit (pre, op))::rest -> begin
     (Staging.app (undo_ops cls pre op) (inverse_plan cls rest))
     end
| (uu___)::rest -> begin
     (inverse_plan cls rest)
     end))


let rec all_inverse = (fun ( cls  :  'o  ->  undo_class<'t, 'o> ) ( steps  :  Prims.list<Staging.step<'t, 'b, 'o>> ) -> (match (steps) with
| [] -> begin
     true
     end
| (Staging.TEdit (uu___, op))::rest -> begin
     ((match ((cls op)) with
| Inverse (inverse) -> begin
     true
     end
| uu___1 -> begin
     false
     end) && (all_inverse cls rest))
     end
| (uu___)::rest -> begin
     (all_inverse cls rest)
     end))


let rec apply_all = (fun ( w  :  Staging.witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( ops  :  Prims.list<'o> ) ( tree  :  't ) -> (match (ops) with
| [] -> begin
     Staging.ROk (tree)
     end
| (op)::rest -> begin
     (match ((w.w_apply op tree)) with
| Staging.RErr (code) -> begin
     Staging.RErr (code)
     end
| Staging.ROk (tree') -> begin
     (apply_all w rest tree')
     end)
     end))


let rec restore = (fun ( steps  :  Prims.list<Staging.step<'t, 'b, 'o>> ) ( bindings  :  'b ) -> (match (steps) with
| [] -> begin
     bindings
     end
| (Staging.TCompute (Staging.OSome (f)))::rest -> begin
     (f (restore rest bindings))
     end
| (uu___)::rest -> begin
     (restore rest bindings)
     end))


let undo_run = (fun ( w  :  Staging.witness<'t, 'b, 'v, 'o, 'q, 'a, 'eff, 'd> ) ( reg  :  Staging.registry<'t, 'v, 'o, 'q, 'p> ) ( node_id  :  Prims.string ) ( cls  :  'o  ->  undo_class<'t, 'o> ) ( canonical  :  't  ->  Prims.string ) ( entry  :  't ) ( committed  :  Prims.bool ) ( steps  :  Prims.list<Staging.step<'t, 'b, 'o>> ) ( post  :  Staging.store<'t, 'b> ) ->  
if (not (committed)) then begin
     Staging.RErr ((render {rf_code = "undo-uncommitted-plan"; rf_step = (Prims.parse_int "0"); rf_reason = "the run rolled back"}))
     end else begin
     (match ((first_refused cls steps (Prims.parse_int "0"))) with
| Staging.OSome (r) -> begin
     Staging.RErr ((render r))
     end
| Staging.ONone -> begin
     (

let inv = (inverse_plan cls (Staging.rev steps))
in (

let drifted = ((all_inverse cls steps) && (match ((apply_all w inv post.st_tree)) with
| Staging.RErr (uu___) -> begin
     true
     end
| Staging.ROk (restored) -> begin
     (not ((Prims.op_Equals (canonical restored) (canonical entry))))
     end))
in  
if drifted then begin
     Staging.RErr ((render {rf_code = "undo-inverse-drift"; rf_step = (Prims.parse_int "0"); rf_reason = "the declared inverses do not fold back to the recorded entry state"}))
     end else begin
     (

let out = (Staging.run w reg node_id ((Staging.SEffect (Staging.ApplyOps (inv)))::[]) post)
in  
if out.oc_committed then begin
     Staging.ROk ({Staging.oc_store = (

let uu___ = out.oc_store
in {Staging.st_tree = uu___.st_tree; Staging.st_bindings = (restore steps out.oc_store.st_bindings)}); Staging.oc_committed = out.oc_committed; Staging.oc_performed = out.oc_performed; Staging.oc_patches = out.oc_patches; Staging.oc_notifications = out.oc_notifications; Staging.oc_client_effects = out.oc_client_effects; Staging.oc_diagnostics = out.oc_diagnostics})
     end else begin
     Staging.ROk (out)
     end)
     end))
     end)
     end)




