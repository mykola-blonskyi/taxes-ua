import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const featureInternals = {
  group: ["@/features/*/*"],
  message: "Import a feature through its index: @/features/<name>.",
};
const appInternals = {
  group: ["@/app/*"],
  message: "Routes are leaves. Nothing imports from @/app.",
};

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    files: ["src/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-imports": ["error", { patterns: [featureInternals, appInternals] }],
    },
  },
  {
    files: ["src/features/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-imports": [
        "error",
        { patterns: [featureInternals, appInternals, { group: ["@/features/*"], message: "Features do not import each other. Lift shared code to @/shared or @/data." }] },
      ],
    },
  },
  {
    files: ["src/data/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-imports": [
        "error",
        { patterns: [appInternals, { group: ["@/features/*"], message: "@/data knows nothing about features." }] },
      ],
    },
  },
  {
    files: ["src/shared/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-imports": [
        "error",
        { patterns: [appInternals, { group: ["@/features/*", "@/data/*"], message: "@/shared is the bottom layer. It imports nothing from features or data." }] },
      ],
    },
  },
  globalIgnores([
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    "service-worker/**",
    // Bundled by `serwist build`, not hand-written; same category as .next/.
    "public/sw.js",
  ]),
]);

export default eslintConfig;
