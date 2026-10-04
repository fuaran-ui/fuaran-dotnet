import { defineConfig } from "vite";

// Port from ports.json — the port band map allocates this sample's slot.
export default defineConfig({
  server: { port: 24060, strictPort: true },
  preview: { port: 24060, strictPort: true },
});
