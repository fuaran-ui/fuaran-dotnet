import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const here = fileURLToPath(new URL(".", import.meta.url));

// Vite bundles the Fable-transpiled output/*.js (Fable does F# -> JS; Vite does
// the bundling). The `watch.ignored` keeps Vite from reloading on raw .fs edits
// — the Fable watcher (`dotnet fable --watch`) handles those, emitting fresh JS.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      react: resolve(here, "node_modules/react"),
      "react-dom": resolve(here, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: {
    port: 24001,
    strictPort: false,
    watch: { ignored: ["**/*.fs"] },
  },
  preview: { port: 14001, strictPort: false },
});
