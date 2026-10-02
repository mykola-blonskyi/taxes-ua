import type { PrecacheEntry, SerwistGlobalConfig } from "serwist";
import { NetworkOnly, Serwist } from "serwist";

declare global {
  interface WorkerGlobalScope extends SerwistGlobalConfig {
    __SW_MANIFEST: (PrecacheEntry | string)[] | undefined;
  }
}

declare const self: ServiceWorkerGlobalScope;

// Only the build's static files and the offline page are cached. Pages and /api responses always go to
// the network: a cached page or amount could show stale tax data. A new worker waits until the owner
// accepts the update prompt (the page sends SKIP_WAITING), so open tabs never run chunks from two builds.
const serwist = new Serwist({
  precacheEntries: self.__SW_MANIFEST,
  skipWaiting: false,
  clientsClaim: true,
  runtimeCaching: [{ matcher: ({ request }) => request.mode === "navigate", handler: new NetworkOnly() }],
  fallbacks: {
    entries: [{ url: "/offline.html", matcher: ({ request }) => request.destination === "document" }],
  },
});

serwist.addEventListeners();
