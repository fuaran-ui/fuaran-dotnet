import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

// Port allocation: the Fuaran workspace CLAUDE.md "Port allocation" table's
// "next new sample" band — Vite dev 24020–24029 (server 14020–14029 unused;
// this is a client-only Fable app, no SSR backend).
export default defineConfig({
  plugins: [react()],
  resolve: {
    // Pin react / react-dom through this project's node_modules and dedupe so
    // Fable.Elmish.React sees a single React instance (hooks break on dupes).
    alias: {
      react: resolve(__dirname, "node_modules/react"),
      "react-dom": resolve(__dirname, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: {
    port: 24020,
    strictPort: true,
    watch: {
      // Fable (run in a sibling terminal) emits the .fs.js Vite watches;
      // ignoring .fs avoids the double-watch during edit storms.
      ignored: ["**/*.fs"],
    },
  },
  preview: {
    port: 14020,
    strictPort: true,
  },
});
