import { serwist } from "@serwist/next/config";

// `serwist()` resolves asynchronously (it reads Next's build config), and the
// `serwist` CLI reads this module's default export directly without awaiting
// it itself. Top-level `await` is required so `import()` hands the CLI the
// resolved BuildOptions object, not a pending Promise.
export default await serwist({
  swSrc: "service-worker/sw.ts",
  swDest: "public/sw.js",
});
