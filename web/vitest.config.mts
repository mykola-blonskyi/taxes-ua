import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  resolve: { tsconfigPaths: true },
  test: {
    environment: "jsdom",
    restoreMocks: true,
    include: ["src/**/*.test.{ts,tsx}", "e2e/support/**/*.test.ts"],
    setupFiles: ["./vitest.setup.ts"],
  },
});
