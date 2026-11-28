namespace Fuaran.UI.Renderer

// ============================================================================
//  Fuaran.UI.Renderer.ReactBinding — the renderer's own React binding
//  (Phase 2514).
//
//  The client renderer emits React elements, and until Phase 2514 it built
//  them through Feliz, so Feliz was in the restore closure of every host and
//  Feliz's `ReactElement` was in the renderer's public signatures. A host that
//  authored in something else still adopted the renderer's authoring DSL.
//
//  Two things replace it:
//
//    * `RenderedElement` — the public, opaque type every entry point returns.
//      At runtime it IS a React element, but a host can only hand it on (to a
//      root, to `Hydration`, into another `Custom` renderer); it cannot look
//      inside it. A host that wants Feliz converts at the edge with
//      `Fuaran.UI.Renderer.Feliz`.
//
//    * The internal binding below, scoped to exactly what the renderer calls:
//      the elements and properties `Render.fs` and the controls emit, the
//      hooks the controls and stores use, and `createElement`. Its spellings
//      follow the ones the renderer was written against (`Html.div`,
//      `prop.className`, `React.useState`), so the move changed no emission
//      site, and the source-reading tests that read those sites still read
//      the same text. It is internal: nothing outside this assembly can name
//      a React property, so React stays the renderer's implementation detail.
//
//  On .NET every member behaves as its browser twin's .NET shim always did: a
//  property is a (name, value) pair cast to `IReactProperty` (which throws, so
//  a renderer path that builds an element fails exactly where it used to), an
//  element factory or hook is `jsNative`, and `Html.none` is null.
// ============================================================================

/// An element the client renderer produced (Phase 2514). Opaque: a host passes
/// it to a React root, to `Hydration`, or back to the renderer, and never looks
/// inside. At runtime it is a React element; `Fuaran.UI.Renderer.Feliz` converts
/// it to and from Feliz's `ReactElement` for a host that authors in Feliz.
[<AllowNullLiteral>]
type RenderedElement = interface end

namespace Fuaran.UI.Renderer.ReactBinding

open System
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Fuaran.UI.Renderer

/// One property of an element under construction: a (name, value) pair.
type internal IReactProperty = interface end

/// One entry of an element's `style` object: a (name, value) pair.
type internal IStyleAttribute = interface end

/// A CSS length the `style` members accept beside a bare pixel count.
type internal ICssUnit = interface end

/// A React ref: a box whose `current` React (or the component) writes.
type internal IRefValue<'T> =
    abstract current: 'T with get, set

module internal ReactInterop =

    let inline mkAttr (key: string) (value: obj) : IReactProperty = unbox (key, value)

    let inline mkStyle (key: string) (value: obj) : IStyleAttribute = unbox (key, value)

    [<Import("createElement", "react")>]
    let createElementWithChildren
        (``type``: obj, props: obj, [<ParamArray>] children: seq<RenderedElement>)
        : RenderedElement =
        jsNative

    [<Import("createElement", "react")>]
    let createElementWithProps (``type``: obj, props: obj) : RenderedElement = jsNative

    /// Mount a function component: `componentFn` is the React component type,
    /// and `props` becomes its props object.
    let inline createComponent (componentFn: obj) (props: obj) : RenderedElement =
        createElementWithProps (componentFn, props)

    [<Import("Fragment", "react")>]
    let fragmentType: obj = jsNative

    [<Import("useState", "react")>]
    let useStateNative (initial: obj) : obj = jsNative

    [<Import("useRef", "react")>]
    let useRefNative (initial: obj) : obj = jsNative

    [<Import("useEffect", "react")>]
    let useEffectNative (setup: obj) : unit = jsNative

    [<Import("useEffect", "react")>]
    let useEffectWithDeps (setup: obj, dependencies: obj[]) : unit = jsNative

    [<Import("useLayoutEffect", "react")>]
    let useLayoutEffectNative (setup: obj) : unit = jsNative

    [<Import("useLayoutEffect", "react")>]
    let useLayoutEffectWithDeps (setup: obj, dependencies: obj[]) : unit = jsNative

    /// An effect whose setup returns its cleanup. The setup crosses as a
    /// one-argument delegate so the cleanup it returns is a one-argument
    /// function and not the second argument of an uncurried pair.
    [<Emit("() => $0()")>]
    let withCleanup (setup: Func<unit, (unit -> unit)>) : obj = jsNative

    /// Build an element of `tag` from a property list, lifting a `children`
    /// property out into React's child arguments.
    let createElement (tag: string) (props: IReactProperty list) : RenderedElement =
#if FABLE_COMPILER
        let pairs = unbox<(string * obj) list> props

        match pairs |> List.tryFind (fun (key, _) -> key = "children") with
        | Some(_, kids) ->
            let rest = pairs |> List.filter (fun (key, _) -> key <> "children")
            createElementWithChildren (box tag, createObj rest, unbox<seq<RenderedElement>> kids)
        | None -> createElementWithProps (box tag, createObj pairs)
#else
        ignore tag
        ignore props
        jsNative
#endif

/// The element factories the renderer emits through.
[<Erase>]
type internal Html =
    static member inline none: RenderedElement = unbox null
    static member inline text(value: string) : RenderedElement = unbox value
    static member inline a(props: IReactProperty list) = ReactInterop.createElement "a" props

    static member inline a(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "a", null, children)

    static member inline button(props: IReactProperty list) =
        ReactInterop.createElement "button" props

    static member inline button(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "button", null, children)

    static member inline div(props: IReactProperty list) = ReactInterop.createElement "div" props

    static member inline div(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "div", null, children)

    static member inline input(props: IReactProperty list) =
        ReactInterop.createElement "input" props

    static member inline input(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "input", null, children)

    static member inline label(props: IReactProperty list) =
        ReactInterop.createElement "label" props

    static member inline label(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "label", null, children)

    static member inline li(props: IReactProperty list) = ReactInterop.createElement "li" props

    static member inline li(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "li", null, children)

    static member inline nav(props: IReactProperty list) = ReactInterop.createElement "nav" props

    static member inline nav(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "nav", null, children)

    static member inline span(props: IReactProperty list) = ReactInterop.createElement "span" props

    static member inline span(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "span", null, children)

    static member inline style(props: IReactProperty list) =
        ReactInterop.createElement "style" props

    static member inline style(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "style", null, children)

    static member inline table(props: IReactProperty list) =
        ReactInterop.createElement "table" props

    static member inline table(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "table", null, children)

    static member inline tbody(props: IReactProperty list) =
        ReactInterop.createElement "tbody" props

    static member inline tbody(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "tbody", null, children)

    static member inline td(props: IReactProperty list) = ReactInterop.createElement "td" props

    static member inline td(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "td", null, children)

    static member inline th(props: IReactProperty list) = ReactInterop.createElement "th" props

    static member inline th(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "th", null, children)

    static member inline thead(props: IReactProperty list) =
        ReactInterop.createElement "thead" props

    static member inline thead(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "thead", null, children)

    static member inline tr(props: IReactProperty list) = ReactInterop.createElement "tr" props

    static member inline tr(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "tr", null, children)

    static member inline ul(props: IReactProperty list) = ReactInterop.createElement "ul" props

    static member inline ul(children: #seq<RenderedElement>) =
        ReactInterop.createElementWithChildren (box "ul", null, children)

/// The element properties the renderer sets.
[<Erase>]
type internal prop =
    static member inline custom(key: string, value: 't) = ReactInterop.mkAttr key value
    static member inline key(value: string) = ReactInterop.mkAttr "key" value
    static member inline key(value: int) = ReactInterop.mkAttr "key" value
    static member inline id(value: string) = ReactInterop.mkAttr "id" value
    static member inline className(value: string) = ReactInterop.mkAttr "className" value

    static member inline role([<ParamArray>] roles: string[]) =
        ReactInterop.mkAttr "role" (String.concat " " roles)

    static member inline style(properties: IStyleAttribute list) =
        ReactInterop.mkAttr "style" (createObj !!properties)

    static member inline ref(ref: IRefValue<HTMLElement option>) = ReactInterop.mkAttr "ref" ref

    static member inline children(elements: seq<RenderedElement>) = ReactInterop.mkAttr "children" elements

    static member inline text(value: string) =
        ReactInterop.mkAttr "children" [ value ]

    static member inline text(value: int) =
        ReactInterop.mkAttr "children" [ value ]

    static member inline text(value: float) =
        ReactInterop.mkAttr "children" [ value ]

    static member inline dangerouslySetInnerHTML(content: string) =
        ReactInterop.mkAttr "dangerouslySetInnerHTML" (createObj [ "__html" ==> content ])

    static member inline ``type'``(value: string) = ReactInterop.mkAttr "type" value
    static member inline inputMode(value: string) = ReactInterop.mkAttr "inputMode" value
    static member inline href(value: string) = ReactInterop.mkAttr "href" value
    static member inline htmlFor(value: string) = ReactInterop.mkAttr "htmlFor" value
    static member inline pattern(value: string) = ReactInterop.mkAttr "pattern" value
    static member inline placeholder(value: string) = ReactInterop.mkAttr "placeholder" value

    static member inline value(value: string) = ReactInterop.mkAttr "value" value
    static member inline value(value: int) = ReactInterop.mkAttr "value" value
    static member inline value(value: float) = ReactInterop.mkAttr "value" value
    static member inline value(value: bool) = ReactInterop.mkAttr "value" value

    static member inline value(values: seq<string>) =
        ReactInterop.mkAttr "value" (ResizeArray values)

    static member inline min(value: int) = ReactInterop.mkAttr "min" value
    static member inline min(value: float) = ReactInterop.mkAttr "min" value
    static member inline max(value: int) = ReactInterop.mkAttr "max" value
    static member inline max(value: float) = ReactInterop.mkAttr "max" value
    static member inline step(value: int) = ReactInterop.mkAttr "step" value
    static member inline step(value: float) = ReactInterop.mkAttr "step" value
    static member inline minLength(value: int) = ReactInterop.mkAttr "minLength" value
    static member inline maxLength(value: int) = ReactInterop.mkAttr "maxLength" value
    static member inline rows(value: int) = ReactInterop.mkAttr "rows" value
    static member inline colSpan(value: int) = ReactInterop.mkAttr "colSpan" value
    static member inline tabIndex(index: int) = ReactInterop.mkAttr "tabIndex" index

    static member inline ariaExpanded(value: bool) =
        ReactInterop.mkAttr "aria-expanded" value

    static member inline autoPlay(value: bool) = ReactInterop.mkAttr "autoPlay" value
    static member inline controls(value: bool) = ReactInterop.mkAttr "controls" value
    static member inline disabled(value: bool) = ReactInterop.mkAttr "disabled" value
    static member inline draggable(value: bool) = ReactInterop.mkAttr "draggable" value
    static member inline hidden(value: bool) = ReactInterop.mkAttr "hidden" value
    static member inline isChecked(value: bool) = ReactInterop.mkAttr "checked" value
    static member inline isOpen(value: bool) = ReactInterop.mkAttr "open" value
    static member inline loop(value: bool) = ReactInterop.mkAttr "loop" value
    static member inline multiple(value: bool) = ReactInterop.mkAttr "multiple" value
    static member inline muted(value: bool) = ReactInterop.mkAttr "muted" value
    static member inline required(value: bool) = ReactInterop.mkAttr "required" value

    static member inline onClick(handler: MouseEvent -> unit) = ReactInterop.mkAttr "onClick" handler

    static member inline onMouseDown(handler: MouseEvent -> unit) =
        ReactInterop.mkAttr "onMouseDown" handler

    static member inline onMouseEnter(handler: MouseEvent -> unit) =
        ReactInterop.mkAttr "onMouseEnter" handler

    static member inline onMouseLeave(handler: MouseEvent -> unit) =
        ReactInterop.mkAttr "onMouseLeave" handler

    static member inline onTouchStart(handler: TouchEvent -> unit) =
        ReactInterop.mkAttr "onTouchStart" handler

    static member inline onTouchEnd(handler: TouchEvent -> unit) =
        ReactInterop.mkAttr "onTouchEnd" handler

    static member inline onKeyDown(handler: KeyboardEvent -> unit) = ReactInterop.mkAttr "onKeyDown" handler
    static member inline onFocus(handler: FocusEvent -> unit) = ReactInterop.mkAttr "onFocus" handler
    static member inline onBlur(handler: FocusEvent -> unit) = ReactInterop.mkAttr "onBlur" handler
    static member inline onPaste(handler: ClipboardEvent -> unit) = ReactInterop.mkAttr "onPaste" handler
    static member inline onScroll(handler: Event -> unit) = ReactInterop.mkAttr "onScroll" handler
    static member inline onSubmit(handler: Event -> unit) = ReactInterop.mkAttr "onSubmit" handler

    static member inline onDragStart(handler: DragEvent -> unit) =
        ReactInterop.mkAttr "onDragStart" handler

    static member inline onDragEnter(handler: DragEvent -> unit) =
        ReactInterop.mkAttr "onDragEnter" handler

    static member inline onDragOver(handler: DragEvent -> unit) =
        ReactInterop.mkAttr "onDragOver" handler

    static member inline onDragLeave(handler: DragEvent -> unit) =
        ReactInterop.mkAttr "onDragLeave" handler

    static member inline onDragEnd(handler: DragEvent -> unit) = ReactInterop.mkAttr "onDragEnd" handler
    static member inline onDrop(handler: DragEvent -> unit) = ReactInterop.mkAttr "onDrop" handler

    /// The raw change event.
    static member inline onChange(handler: Event -> unit) = ReactInterop.mkAttr "onChange" handler

    /// The element's `value` as text.
    static member inline onChange(handler: string -> unit) =
        ReactInterop.mkAttr "onChange" (fun (ev: Event) -> handler (!!ev.target?value))

    /// The element's `checked` state.
    static member inline onChange(handler: bool -> unit) =
        ReactInterop.mkAttr "onChange" (fun (ev: Event) -> handler (!!ev.target?``checked``))

    /// The element's `valueAsNumber`, when it is a number.
    static member inline onChange(handler: float -> unit) =
        ReactInterop.mkAttr "onChange" (fun (ev: Event) ->
            let value: float = !!ev.target?valueAsNumber

            if not (isNullOrUndefined value) && not (Double.IsNaN value) then
                handler value)

    /// Every file the element selected, in order.
    static member inline onChange(handler: File list -> unit) =
        ReactInterop.mkAttr "onChange" (fun (ev: Event) ->
            let files: FileList = ev?target?files

            if not (isNullOrUndefined files) then
                handler [ for i in 0 .. files.length - 1 -> files.item i ])

/// A CSS length.
[<Erase>]
type internal length =
    static member inline percent(value: int) : ICssUnit = unbox ((unbox<string> value) + "%")
    static member inline percent(value: float) : ICssUnit = unbox ((unbox<string> value) + "%")

/// The style entries the renderer sets.
[<Erase>]
type internal style =
    static member inline custom(key: string, value: 't) = ReactInterop.mkStyle key value
    static member inline width(value: int) = ReactInterop.mkStyle "width" value
    static member inline width(value: ICssUnit) = ReactInterop.mkStyle "width" value
    static member inline height(value: int) = ReactInterop.mkStyle "height" value
    static member inline height(value: ICssUnit) = ReactInterop.mkStyle "height" value
    static member inline minHeight(value: int) = ReactInterop.mkStyle "minHeight" value
    static member inline minHeight(value: ICssUnit) = ReactInterop.mkStyle "minHeight" value

/// The hooks and the fragment the renderer's function components use.
[<Erase>]
type internal React =

    static member inline Fragment(elements: seq<RenderedElement>) : RenderedElement =
        ReactInterop.createElementWithChildren (ReactInterop.fragmentType, null, elements)

    static member inline KeyedFragment(key: string, elements: seq<RenderedElement>) : RenderedElement =
        ReactInterop.createElementWithChildren (ReactInterop.fragmentType, createObj [ "key" ==> key ], elements)

    static member inline useState<'t>(initializer: unit -> 't) : 't * ('t -> unit) =
        unbox (ReactInterop.useStateNative (box initializer))

    /// `useState` whose setter takes the current value and returns the next.
    static member inline useStateWithUpdater(initial: 't) : 't * (('t -> 't) -> unit) =
        unbox (ReactInterop.useStateNative (box initial))

    static member inline useRef<'t>(initial: 't) : IRefValue<'t> =
        unbox (ReactInterop.useRefNative (box initial))

    /// A ref an element's `prop.ref` fills with that element.
    static member inline useElementRef() : IRefValue<HTMLElement option> = React.useRef (None: HTMLElement option)

    static member inline useEffect(setup: unit -> unit) : unit =
        ReactInterop.useEffectNative (box setup)

    static member inline useEffect(setup: unit -> unit, dependencies: obj[]) : unit =
        ReactInterop.useEffectWithDeps (box setup, dependencies)

    static member inline useEffect(setup: unit -> (unit -> unit)) : unit =
        ReactInterop.useEffectNative (ReactInterop.withCleanup (Func<_, _> setup))

    static member inline useEffect(setup: unit -> (unit -> unit), dependencies: obj[]) : unit =
        ReactInterop.useEffectWithDeps (ReactInterop.withCleanup (Func<_, _> setup), dependencies)

    static member inline useLayoutEffect(setup: unit -> unit) : unit =
        ReactInterop.useLayoutEffectNative (box setup)

    static member inline useLayoutEffect(setup: unit -> unit, dependencies: obj[]) : unit =
        ReactInterop.useLayoutEffectWithDeps (box setup, dependencies)

    static member inline useLayoutEffect(setup: unit -> (unit -> unit)) : unit =
        ReactInterop.useLayoutEffectNative (ReactInterop.withCleanup (Func<_, _> setup))

    static member inline useLayoutEffect(setup: unit -> (unit -> unit), dependencies: obj[]) : unit =
        ReactInterop.useLayoutEffectWithDeps (ReactInterop.withCleanup (Func<_, _> setup), dependencies)

/// `useState` from a plain initial value. An OPTIONAL extension rather than an
/// overload on `React`: F# prefers an intrinsic member, so a call passing a
/// `unit -> 't` resolves to the initializer form above without an annotation,
/// and any other argument falls through to this one.
[<AutoOpen>]
module internal ReactExtensions =
    type React with

        static member inline useState<'t>(initial: 't) : 't * ('t -> unit) =
            unbox (ReactInterop.useStateNative (box initial))
