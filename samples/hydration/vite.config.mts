import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

// Port allocation (workspace CLAUDE.md): next new sample = Vite 24050 / preview 14050.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      react: resolve(__dirname, "node_modules/react"),
      "react-dom": resolve(__dirname, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: { port: 24050, strictPort: true, watch: { ignored: ["**/*.fs"] } },
  preview: { port: 14050, strictPort: true },
});
