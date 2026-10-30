import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Bygget legges i backendens wwwroot, slik at backend serverer ferdig frontend ved normal bruk.
export default defineConfig({
  plugins: [react()],
  build: { outDir: "../src/UuDok.Web/wwwroot", emptyOutDir: true, sourcemap: false },
  server: {
    host: "127.0.0.1",
    port: 5173,
    proxy: { "/api": { target: "http://127.0.0.1:5317", changeOrigin: true } },
  },
  test: { environment: "node" },
});
