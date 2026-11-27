module FableRender.Dom

// ============================================================================
//  The mounted-tree helpers the cases share: a container in the document, a
//  React root over it, and a synchronous commit (`flushSync`), so a case reads
//  the DOM a render produced with no page, timer or wait between them.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core
open Feliz

[<Import("createRoot", "react-dom/client")>]
let createRoot (container: obj) : obj = jsNative

[<Import("flushSync", "react-dom")>]
let flushSync (work: unit -> unit) : unit = jsNative

[<Emit("$0.render($1)")>]
let rootRender (root: obj) (element: ReactElement) : unit = jsNative

[<Emit("$0.unmount()")>]
let rootUnmount (root: obj) : unit = jsNative

[<Emit("(() => { const d = document.createElement('div'); document.body.appendChild(d); return d; })()")>]
let newContainer () : obj = jsNative

[<Emit("$0.remove()")>]
let removeContainer (container: obj) : unit = jsNative
#endif
