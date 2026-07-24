using System.Runtime.CompilerServices;

// The analyzer's test project asserts the VB vocabulary (internal) equals the
// translator's runtime vocabulary — the single-source-of-truth pin (Phase 315).
[assembly: InternalsVisibleTo("Fuaran.UI.Analyzers.VisualBasic.Tests")]
