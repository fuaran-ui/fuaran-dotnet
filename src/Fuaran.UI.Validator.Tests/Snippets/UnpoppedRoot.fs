module Snippets.UnpoppedRoot

open Fuaran.UI
open Fuaran.UI.Types

/// A partially-applied root: the walker sees a one-argument `Fuaran.dashboard`
/// call. It must not stay open as the tree of every call after it.
let partialRoot: DashboardSpec<unit> -> Node<unit> = Fuaran.dashboard "first"

/// Two loose helpers sharing an id — loose components, in no tree, so no
/// per-tree duplicate.
let helperA () : Node<unit> = Fuaran.metric "loose" Defaults.metric

let helperB () : Node<unit> = Fuaran.metric "loose" Defaults.metric
