import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

// Port allocation: this sample's slot is declared in ./ports.json — Vite dev
// 24090–24099, preview 14090–14099 (a client-only Fable app, no SSR backend).
export default defineConfig({
  plugins: [react()],
  resolve: {
    // Pin react / react-dom through this project's node_modules and dedupe so
    // the Feliz renderer and Fuaran.MVU.React see a single React instance (hooks
    // break on dupes).
    alias: {
      react: resolve(__dirname, "node_modules/react"),
      "react-dom": resolve(__dirname, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: {
    port: 24090,
    strictPort: true,
    watch: {
      // Fable (run in a sibling terminal) emits the .fs.js Vite watches;
      // ignoring .fs avoids the double-watch during edit storms.
      ignored: ["**/*.fs"],
    },
  },
  preview: {
    port: 14090,
    strictPort: true,
  },
});
