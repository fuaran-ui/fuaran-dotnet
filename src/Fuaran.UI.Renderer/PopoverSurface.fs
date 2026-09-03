module Fuaran.UI.Renderer.PopoverSurface

// ============================================================================
//  Fuaran — the anchored, light-dismissed popover surface (Phase 1119)
//
//  `ModalSpec.Modality = Popover` selects the NON-BLOCKING overlay. The wire
//  says only that, plus `Anchor` — the id of the node the surface belongs to.
//  Everything in this file is renderer-owned: where the surface is placed,
//  which way it flips when the viewport runs out, how far off the anchor it
//  sits, and which gestures close it. Nothing on the wire names a pixel, an
//  edge, an offset or an event, which is the affordance→op charter's line and
//  the reason this file exists rather than four more wire members.
//
//  ── What a popover is NOT, stated because each absence is a decision ──────
//    * No scrim. The page behind stays visible, reachable and interactive; a
//      backdrop that swallowed clicks would make this a modal with a different
//      class name.
//    * No focus trap. The reader may `Tab` straight out, and nothing pulls
//      focus in on open. A transient surface that captured the keyboard would
//      be a keyboard trap (WCAG 2.1.2) in every case where the reader did not
//      want it.
//    * No `aria-modal`. That attribute tells assistive technology the rest of
//      the page is inert, which for this surface is FALSE. It is omitted
//      entirely rather than emitted as `"false"`: `false` is already the ARIA
//      default, so writing it adds no information and invites a reader to think
//      a claim was made and denied. `role="dialog"` is kept — a dialog that
//      does not block is exactly what ARIA's non-modal dialog is, and the
//      lighter roles each carry a promise this surface cannot keep (`menu`
//      demands `menuitem` children, `tooltip` demands non-interactive content,
//      `region` demands a name and joins the landmark rota).
//
//  ── Why a function component ──────────────────────────────────────────────
//  The placement, the scroll/resize re-placement and the document-level
//  dismiss listeners are all EFFECTS with a lifetime, and the composition-based
//  renderer holds no hooks. So — exactly as `LocalBindings`, `ComboboxControl`
//  and `FileUploadDrop` do — the affordance is wrapped as a React function
//  component invoked through `React.createElement`. The surface's children are
//  built by `Render.fs` and passed in, so there is exactly one definition of
//  the markup and this file adds only the behaviour around it.
//
//  ── The hydration contract (Phase 289, honoured rather than excepted) ─────
//  The RENDERED markup of this component is byte-identical to the SSR floor:
//  same wrapper, same classes, same `role`, same `[hidden]`. The placement is
//  applied IMPERATIVELY to the element after mount, never as a `style` prop, so
//  React hydration finds the DOM the server emitted. That is the same posture
//  the modal's focus management already takes — an additive client-only
//  enhancement that does not alter the hydrated DOM.
//
//  ── The unanchored fallback is deliberate ─────────────────────────────────
//  When `Anchor` is absent, or names a node that is not in the document, this
//  file positions NOTHING: the surface stays in the document flow where the
//  node sits. It does not guess a location and it does not centre itself in the
//  viewport, because a surface floating in the middle of the screen with no
//  scrim is the one outcome a reader cannot interpret. The pre-emit validator
//  reports both shapes (FUARAN122), so the fallback is a described state rather
//  than a silent one, and it is exactly what a no-script host renders.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

/// Place `surface` against the node whose `data-fuaran-node-id` is `anchorId`,
/// and return whether it was placed. `false` means no anchor resolved and the
/// caller should leave the surface exactly where the document put it.
///
/// The whole body is one emit because it is viewport arithmetic with no F#
/// shape worth modelling — the `assignFiles` precedent. Three decisions are in
/// here and each is bounded on purpose:
///
///   * **Coordinates are `position: fixed`.** `getBoundingClientRect` is already
///     viewport-relative, so fixed positioning needs no scroll arithmetic and
///     no offset-parent archaeology — the two things that make hand-rolled
///     anchoring wrong on a page with any transformed ancestor.
///   * **The flip is a single either/or**, below the anchor or above it, chosen
///     by which side has room and preferring below on a tie. There is no
///     left/right placement axis and no "auto" strategy: a second axis needs a
///     preference to choose between them, and a preference is a wire member the
///     charter declines.
///   * **The clamp is to the viewport with the same 8px gutter as the offset.**
///     A surface pushed off-screen is unreadable, and the reader has no way to
///     scroll a fixed element into view.
[<Emit("""(function(surface, anchorId){
  try {
    if (!surface || !anchorId || typeof document === 'undefined') return false;
    var anchor = document.querySelector('[data-fuaran-node-id="' + String(anchorId).replace(/"/g, '\\"') + '"]');
    if (!anchor) return false;
    var gap = 8;
    var a = anchor.getBoundingClientRect();
    surface.style.position = 'fixed';
    surface.style.top = '0px';
    surface.style.left = '0px';
    var s = surface.getBoundingClientRect();
    var vw = window.innerWidth || 0;
    var vh = window.innerHeight || 0;
    var roomBelow = vh - a.bottom - gap;
    var roomAbove = a.top - gap;
    var placeAbove = (s.height > roomBelow) && (roomAbove > roomBelow);
    var top = placeAbove ? (a.top - gap - s.height) : (a.bottom + gap);
    var left = a.left;
    var maxLeft = vw - s.width - gap;
    if (left > maxLeft) left = maxLeft;
    if (left < gap) left = gap;
    surface.style.top = String(Math.round(top)) + 'px';
    surface.style.left = String(Math.round(left)) + 'px';
    surface.setAttribute('data-fuaran-popover-placement', placeAbove ? 'above' : 'below');
    return true;
  } catch (e) {
    return false;
  }
})($0, $1)""")>]
let private placeAgainst (surface: obj) (anchorId: string) : bool = jsNative

/// Undo `placeAgainst`, so a surface that becomes unanchored (the anchor was
/// removed, or the modality changed under an `UpdateProp`) returns to the flow
/// rather than staying frozen at its last computed coordinates.
[<Emit("""(function(surface){
  try {
    if (!surface) return;
    surface.style.position = '';
    surface.style.top = '';
    surface.style.left = '';
    surface.removeAttribute('data-fuaran-popover-placement');
  } catch (e) { }
})($0)""")>]
let private clearPlacement (surface: obj) : unit = jsNative

/// Attach the light-dismiss listeners for as long as the surface is open, and
/// return the teardown. Two gestures, and the exclusions in each are the whole
/// content of the function:
///
///   * **`pointerdown` outside** — outside the SURFACE and outside the ANCHOR.
///     Excluding the anchor is not tidiness: the anchor is normally the control
///     that opened the popover, so a dismiss on its own pointerdown would race
///     the open it is about to perform and the popover would flicker shut.
///     `pointerdown` rather than `click` for the reason `ComboboxControl`
///     records — a `click` on a control inside the surface is preceded by a
///     blur/pointer sequence that can unmount the target first.
///   * **`Escape`** — on `document`, because nothing here holds focus. A
///     `keydown` on the surface would only fire when focus happened to be
///     inside it, which for a surface that deliberately traps nothing is most
///     of the time false.
///
/// Both listeners are registered in the BUBBLE phase. A capture-phase listener
/// would dismiss before a control inside the page could see its own event, which
/// is the difference between a light dismiss and a swallowed interaction.
[<Emit("""(function(surface, anchorId, onDismiss){
  if (typeof document === 'undefined') return function(){};
  var anchor = anchorId ? document.querySelector('[data-fuaran-node-id="' + String(anchorId).replace(/"/g, '\\"') + '"]') : null;
  var onPointerDown = function(e){
    var t = e.target;
    if (surface && surface.contains && surface.contains(t)) return;
    if (anchor && anchor.contains && anchor.contains(t)) return;
    onDismiss();
  };
  var onKeyDown = function(e){
    if (e.key === 'Escape' || e.key === 'Esc') onDismiss();
  };
  document.addEventListener('pointerdown', onPointerDown);
  document.addEventListener('keydown', onKeyDown);
  return function(){
    document.removeEventListener('pointerdown', onPointerDown);
    document.removeEventListener('keydown', onKeyDown);
  };
})($0, $1, $2)""")>]
let private attachLightDismiss (surface: obj) (anchorId: string) (onDismiss: unit -> unit) : unit -> unit = jsNative

/// Re-place on scroll and resize while open, and return the teardown. `scroll`
/// is registered with `capture: true` so a scroll inside any ancestor container
/// — not only the document — moves the surface with its anchor; a scroll event
/// does not bubble, so the capture phase is the only way to see one from a
/// nested scroller.
[<Emit("""(function(reposition){
  if (typeof window === 'undefined') return function(){};
  var run = function(){ reposition(); };
  window.addEventListener('scroll', run, true);
  window.addEventListener('resize', run);
  return function(){
    window.removeEventListener('scroll', run, true);
    window.removeEventListener('resize', run);
  };
})($0)""")>]
let private attachReflow (reposition: unit -> unit) : unit -> unit = jsNative

type PopoverProps =
    {| className: string
       hidden: bool
       anchor: string option
       dismissable: bool
       onDismiss: unit -> unit
       children: ReactElement list |}

let private renderPopover (props: PopoverProps) : ReactElement =
    let surfaceRef = React.useElementRef ()

    let anchorId = props.anchor |> Option.defaultValue ""
    let isOpen = not props.hidden

    // Placement. Runs after every commit and on every scroll/resize while open,
    // and does nothing at all when the surface is closed — a `[hidden]` element
    // has a zero rect, so measuring one would compute a placement from nothing
    // and cache it for the moment it opens.
    let noTeardown: unit -> unit = id

    React.useEffect (
        (fun () ->
            match surfaceRef.current with
            | None -> noTeardown
            | Some el ->
                let el = box el

                let reposition () =
                    if isOpen && placeAgainst el anchorId then
                        ()
                    else
                        clearPlacement el

                reposition ()

                if isOpen && anchorId <> "" then
                    attachReflow reposition
                else
                    noTeardown),
        [| box isOpen; box anchorId |]
    )

    // Light dismiss. Gated on `Dismissable` for the same reason the modal's
    // backdrop click is: a document that declares the surface non-dismissable
    // has asked for a surface its own controls close, and honouring that is not
    // this file's decision to overturn.
    React.useEffect (
        (fun () ->
            if isOpen && props.dismissable then
                match surfaceRef.current with
                | None -> noTeardown
                | Some el -> attachLightDismiss (box el) anchorId props.onDismiss
            else
                noTeardown),
        [| box isOpen; box anchorId; box props.dismissable |]
    )

    Html.div (
        [ prop.className props.className
          prop.ref surfaceRef
          if props.hidden then
              prop.custom ("hidden", "")
          match props.anchor with
          | Some a -> prop.custom ("data-fuaran-popover-anchor", a)
          | None -> ()
          prop.children props.children ]
    )

/// The public surface — `Render.fs` invokes this for a `Popover` modality and
/// renders the plain overlay itself for a `Modal`.
let surface (props: PopoverProps) : ReactElement =
    reactCreateElement (box renderPopover) (box props)
