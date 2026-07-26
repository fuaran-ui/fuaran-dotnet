import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

// Port allocation: the Fuaran workspace CLAUDE.md "Port allocation" table
// reserves 24010–24019 for fuaran-dotnet/samples/catalog (Vite dev) and 14010–
// 14019 for the SSR-style preview server. Website-class band, sibling-
// disjoint from fuaran-dotnet/samples/demo's 24000/14000 reservation.
export default defineConfig({
  // Phase 169 — static production build. `base: "./"` makes every emitted
  // asset reference relative, so the built `dist/` hosts from any static file
  // server *and* any sub-path (e.g. GitHub Pages' `/<repo>/`) without a
  // rebuild. Deep-linking rides the query string (?kind/?theme/?locale/?a11y),
  // so no SPA 404-rewrite config is needed — index.html serves the directory
  // and the client routes.
  base: "./",
  plugins: [react()],
  resolve: {
    // Mirrors samples/demo: pin react / react-dom paths through this
    // project's node_modules so Rollup resolves them consistently across
    // the workspace and dedupes the React instance so hooks behave
    // (Fable.Elmish.React breaks on React duplicates).
    alias: {
      react: resolve(__dirname, "node_modules/react"),
      "react-dom": resolve(__dirname, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: {
    port: 24010,
    strictPort: true,
    watch: {
      // F# source changes trigger Fable (run in a sibling terminal),
      // which emits the .fs.js Vite actually watches. Ignoring .fs
      // avoids the double-watch + spurious "file changed" pings during
      // edit storms.
      ignored: ["**/*.fs"],
    },
  },
  preview: {
    port: 14010,
    strictPort: true,
  },
});
