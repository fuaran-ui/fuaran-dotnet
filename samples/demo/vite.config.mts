import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

// Port allocation: the Fuaran workspace CLAUDE.md "Port allocation" table
// reserves 24000–24009 for fuaran-dotnet/samples/demo (Vite dev) and 14000–14009
// for the SSR-style preview server. This is a renderer demo — no SSR
// backend exists yet, only the Vite dev server is wired.
export default defineConfig({
  plugins: [react()],
  resolve: {
    // Same trick the sibling samples use — pin react / react-dom paths
    // through this project's node_modules so Rollup resolves them
    // consistently across the workspace and dedupes the React instance
    // so hooks behave (Fable.Elmish.React breaks on React duplicates).
    alias: {
      react: resolve(__dirname, "node_modules/react"),
      "react-dom": resolve(__dirname, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: {
    port: 24000,
    strictPort: true,
    watch: {
      // F# source changes trigger Fable (run in a sibling terminal),
      // which emits the .fs.js Vite actually watches. Ignoring .fs
      // avoids the double-watch + spurious "file changed" pings during
      // edit storms.
      ignored: ["**/*.fs"],
    },
  },
  // The preview server (vite preview) is the placeholder for a future
  // SSR-style serve path — port 14000 from the workspace's website-class
  // allocation.
  preview: {
    port: 14000,
    strictPort: true,
  },
});
