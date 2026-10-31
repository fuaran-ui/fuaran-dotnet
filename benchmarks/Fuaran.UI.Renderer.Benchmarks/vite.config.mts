import { defineConfig } from "vite";
import { resolve } from "path";
import { fileURLToPath } from "url";

const here = fileURLToPath(new URL(".", import.meta.url));

// React's production PROFILING build: production timing, plus the Profiler's
// render-phase durations. Vite's default production mode keeps every
// development check out of the bundle.
export default defineConfig({
  base: "./",
  build: { minify: false, chunkSizeWarningLimit: 100000 },
  resolve: {
    alias: [
      { find: /^react-dom\/client$/, replacement: resolve(here, "node_modules/react-dom/profiling") },
      { find: /^react-dom$/, replacement: resolve(here, "node_modules/react-dom") },
      { find: /^react$/, replacement: resolve(here, "node_modules/react") },
    ],
    dedupe: ["react", "react-dom"],
  },
});
