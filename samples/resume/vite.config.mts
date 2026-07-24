import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "path";
import { fileURLToPath } from "url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

// Port allocation (workspace CLAUDE.md): sample band server 14050–14059 / Vite
// 24050–24059. The hydration sample squats 24050/14050; resume takes the next.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      react: resolve(__dirname, "node_modules/react"),
      "react-dom": resolve(__dirname, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom"],
  },
  server: { port: 24051, strictPort: true, watch: { ignored: ["**/*.fs"] } },
  preview: { port: 14051, strictPort: true },
});
