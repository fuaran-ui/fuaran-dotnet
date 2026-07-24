namespace Fuaran.UI.StyleObserver

// ─── StyleObservation — one resolved-style snapshot per NodeId ──
//
// The per-node payload surfaced by `IStyleObserver` — keyed by
// NodeId (the same identity the renderer uses for DOM-id resolution),
// carrying the resolved colour evidence + the derived flag list.
// Twin of `LayoutObservation`: stays small on the wire because every
// AI tool result that includes a `Style` block ships one of these
// per observed node; a 50-node snapshot serialises in well under
// ~5 KB (same budget discipline as `LayoutObservation`).
//
// **Raw evidence is colour + font + tone.** `Foreground` is the
// composited colour the text actually paints with (the declared
// foreground over the effective background, so an alpha-blended
// foreground reports its rendered value, not its declared value).
// `EffectiveBackground` is the opaque colour behind the text after
// the ancestor-compositing walk. `FontRole` is the coarse
// classification; `EmittedTone` is the `--fuaran-tone-*` token the
// element declared (`None` when the element opted into no tone);
// `ContrastRatio` is the WCAG foreground/background ratio the flags
// keyed off. If a future phase needs more evidence, the record
// extends additively.
//
// **JSON shape:** camelCase per the existing
// `_platform.ui.inspect_active_module` envelope convention. `flags`
// is a JSON array of tagged-object flags per `StyleFlag.encode`;
// `emittedTone` is `null` when `None`.

/// One resolved-style snapshot for a single addressable node,
/// derived from the browser's `getComputedStyle` + the
/// effective-background composite walk (browser path) or from a
/// hand-authored fixture (in-memory path). The flag list is the
/// AI-facing interpretation; the colour/font/tone fields are the
/// supporting evidence.
type StyleObservation =
    { NodeId: string
      Foreground: Rgba
      EffectiveBackground: Rgba
      FontRole: FontRole
      EmittedTone: string option
      ContrastRatio: float
      Flags: StyleFlag list }

module StyleObservation =
    /// Construct an observation with an empty flag set — the
    /// observer's flag-derivation logic populates `Flags` post-hoc.
    let withoutFlags
        (nodeId: string)
        (foreground: Rgba)
        (effectiveBackground: Rgba)
        (fontRole: FontRole)
        (emittedTone: string option)
        (contrastRatio: float)
        : StyleObservation =
        { NodeId = nodeId
          Foreground = foreground
          EffectiveBackground = effectiveBackground
          FontRole = fontRole
          EmittedTone = emittedTone
          ContrastRatio = contrastRatio
          Flags = [] }

    /// Encode as JSON. Invariant-culture floats; tagged-object flags;
    /// `emittedTone` is `null` when `None`. Field order matches the
    /// record declaration so the shape stays stable across F# minor
    /// versions (same discipline as `LayoutObservation.encode`).
    let encode (obs: StyleObservation) : string =
        let inv = System.Globalization.CultureInfo.InvariantCulture

        let escape (s: string) =
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")

        let nodeId = escape obs.NodeId
        let fg = Rgba.encode obs.Foreground
        let bg = Rgba.encode obs.EffectiveBackground
        let role = FontRole.kind obs.FontRole

        let tone =
            match obs.EmittedTone with
            | Some t -> $"\"{escape t}\""
            | None -> "null"

        let contrast = obs.ContrastRatio.ToString("F2", inv)
        let flagsJson = obs.Flags |> List.map StyleFlag.encode |> String.concat ","

        $"""{{"nodeId":"{nodeId}","foreground":{fg},"effectiveBackground":{bg},"fontRole":"{role}","emittedTone":{tone},"contrastRatio":{contrast},"flags":[{flagsJson}]}}"""
