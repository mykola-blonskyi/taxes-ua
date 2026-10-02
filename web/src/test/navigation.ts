import { vi } from "vitest";

/** The router the components under test push to. Read it to assert where the owner was sent. */
export const router = {
  push: vi.fn(),
  replace: vi.fn(),
  back: vi.fn(),
  forward: vi.fn(),
  refresh: vi.fn(),
  prefetch: vi.fn(),
};

let pathname = "/";
let searchParams = new URLSearchParams();

/** Sets the page the components think they are on. Reset after each test. */
export function setLocation(path: string) {
  const url = new URL(path, "http://localhost");
  pathname = url.pathname;
  searchParams = url.searchParams;
}

export function resetNavigation() {
  Object.values(router).forEach((fn) => fn.mockReset());
  setLocation("/");
}

/** Stands in for "next/navigation"; vitest.setup.ts registers it. */
export const navigationModule = {
  useRouter: () => router,
  usePathname: () => pathname,
  useSearchParams: () => searchParams,
  useParams: () => ({}),
  redirect: vi.fn(),
  notFound: vi.fn(),
};
