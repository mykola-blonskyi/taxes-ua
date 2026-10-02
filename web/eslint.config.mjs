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

// src/test is the component-test harness. A rule block replaces the one before it, so each layer lists the
// harness pattern itself, and the blocks for test files leave it out.
const testHarness = {
  group: ["@/test", "@/test/*", "**/test", "**/test/*"],
  message: "src/test is the test harness. Only *.test.ts(x) files import it.",
};
const testFiles = "**/*.test.{ts,tsx}";

const layers = [
  {
    glob: "src/**/*.{ts,tsx}",
    patterns: [featureInternals, appInternals],
  },
  {
    glob: "src/features/**/*.{ts,tsx}",
    patterns: [
      featureInternals,
      appInternals,
      { group: ["@/features/*"], message: "Features do not import each other. Lift shared code to @/shared or @/data." },
    ],
  },
  {
    glob: "src/data/**/*.{ts,tsx}",
    patterns: [appInternals, { group: ["@/features/*"], message: "@/data knows nothing about features." }],
  },
  {
    glob: "src/shared/**/*.{ts,tsx}",
    patterns: [
      appInternals,
      { group: ["@/features/*", "@/data/*"], message: "@/shared is the bottom layer. It imports nothing from features or data." },
    ],
  },
];

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  ...layers.flatMap(({ glob, patterns }) => [
    {
      files: [glob],
      ignores: [testFiles],
      rules: { "no-restricted-imports": ["error", { patterns: [...patterns, testHarness] }] },
    },
    {
      files: [glob.replace(/\*\.\{ts,tsx\}$/, "*.test.{ts,tsx}")],
      rules: { "no-restricted-imports": ["error", { patterns }] },
    },
  ]),
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
