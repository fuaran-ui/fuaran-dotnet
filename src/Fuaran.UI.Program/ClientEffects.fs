namespace Fuaran.UI.Program

// ============================================================================
//  The UI tier's client effects, placed against the core's egress seam.
//
//  Part of the `Fuaran.UI.Program` adapter package (Phase 1896 wrote it, Phase
//  1897 made it a package). The destination policy's
//  mechanism is the core's (`Fuaran.Program.Runtime.EgressPolicy`); what is
//  here is what only the UI tier knows — its renderer URL floor, and which of
//  its eight effect arms points where.
// ============================================================================

open Fuaran.UI.ServerDriven
open Fuaran.Program.Runtime

module EgressPolicy =

    /// Resolve a URL to the destination a policy reasons about, with the UI
    /// tier's renderer URL floor (`Sanitize.sanitizeUrl`) as the scheme floor —
    /// the floor runs FIRST, and there is nothing to say about where an unsafe
    /// URL points.
    let classify (url: string) : EffectDestination =
        Fuaran.Program.Runtime.EgressPolicy.classifyWith Fuaran.UI.Renderer.Sanitize.sanitizeUrl url

module ClientEffectDestination =

    /// The destination an effect reaches, by arm.
    ///
    /// `ReadFileBody` is `Local` rather than `Absent`, and the distinction is the
    /// honest one rather than a convenience: the arm carries a node id, not a URL,
    /// so there is no origin to allowlist — but the body it reads travels back to
    /// the host that is driving the loop, which is the local origin by
    /// construction. A policy denying local egress therefore denies it, and one
    /// permitting local egress leaves it to the discriminator gate, which is
    /// exactly where the decision belongs. What this seam does NOT claim is any
    /// bound on what the host does with the body once it has it.
    let destinationOf (effect: ClientEffect) : EffectDestination =
        match effect with
        // The route is what a destination policy judges; the browsing-context
        // target is not a destination and changes none of this. `Blank` opens
        // the SAME URL in another context, so a policy that permits the origin
        // permits it and one that refuses the origin refuses it.
        | ClientEffect.Navigate(route, _)
        | ClientEffect.PushState route -> EgressPolicy.classify route
        | ClientEffect.Download(url, _) -> EgressPolicy.classify url
        | ClientEffect.ReadFileBody _ -> EffectDestination.Local
        // `Print` is payload-free: no URL to classify, no node to read from, and
        // nothing that travels anywhere. `Confirm` asks the reader a question
        // and sends nothing anywhere: the answer returns through the ordinary
        // event channel, and the continuation it may unlock meets its OWN
        // destination check when it is dispatched. The discriminator gate still
        // governs whether any of these runs at all.
        | ClientEffect.Print
        | ClientEffect.WriteToClipboard _
        | ClientEffect.Confirm _
        | ClientEffect.Focus _ -> EffectDestination.Absent
