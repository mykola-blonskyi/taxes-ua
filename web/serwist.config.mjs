import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { serwist } from "@serwist/next/config";

// The Next preset treats every .html it finds as a prerendered page and strips the extension, which
// turned public/offline.html into the entry `/public/offline` (a 404 at install, so the worker never
// activated). The offline page is therefore ignored by the glob and added back under its real URL.
const offlineRevision = createHash("md5").update(readFileSync("public/offline.html")).digest("hex");

// `serwist()` resolves asynchronously (it reads Next's build config), and the
// `serwist` CLI reads this module's default export directly without awaiting
// it itself. Top-level `await` is required so `import()` hands the CLI the
// resolved BuildOptions object, not a pending Promise.
export default await serwist({
  swSrc: "service-worker/sw.ts",
  swDest: "public/sw.js",
  globIgnores: ["public/offline.html"],
  // Precache static assets only. A prerendered page in the precache would be served from it ahead of the
  // worker's NetworkOnly navigation rule, so a future static route would show stale HTML until the next worker.
  precachePrerendered: false,
  additionalPrecacheEntries: [{ url: "/offline.html", revision: offlineRevision }],
});
