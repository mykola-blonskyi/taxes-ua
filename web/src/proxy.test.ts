import { NextRequest } from "next/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import { proxy } from "./proxy";
import { contentSecurityPolicy } from "@/shared/security/csp";

const SIGNED_IN = { cookie: "__Host-taxesua.auth=ticket" };

function page(path: string, headers: Record<string, string> = {}) {
  return new NextRequest(`https://taxes.example${path}`, { headers });
}

function scriptSrc(policy: string): string {
  return policy.split("; ").find((directive) => directive.startsWith("script-src"))!;
}

describe("contentSecurityPolicy", () => {
  it("allows scripts only by nonce, with no unsafe-inline", () => {
    const directive = scriptSrc(contentSecurityPolicy("abc"));

    expect(directive).toContain("'nonce-abc'");
    expect(directive).not.toContain("'unsafe-inline'");
  });

  it("keeps the framing, form, base and object lockdowns", () => {
    const policy = contentSecurityPolicy("abc");

    for (const directive of ["frame-ancestors 'none'", "object-src 'none'", "base-uri 'self'", "form-action 'self'"]) {
      expect(policy).toContain(directive);
    }
  });
});

describe("proxy", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("sends a signed-out visitor to the login page", () => {
    const response = proxy(page("/transactions"));

    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe("https://taxes.example/login");
  });

  it("lets a signed-out visitor see the login page itself", () => {
    vi.stubEnv("NODE_ENV", "production");

    const response = proxy(page("/login"));

    expect(response.status).toBe(200);
    expect(response.headers.get("content-security-policy")).toContain("script-src");
  });

  it("gives each page request its own nonce, in the response and in the request Next reads it from", () => {
    vi.stubEnv("NODE_ENV", "production");

    const first = proxy(page("/transactions", SIGNED_IN));
    const second = proxy(page("/transactions", SIGNED_IN));
    const nonceOf = (response: Response) => /'nonce-([^']+)'/.exec(response.headers.get("content-security-policy")!)![1];

    expect(nonceOf(first)).not.toBe(nonceOf(second));
    expect(first.headers.get("x-middleware-request-x-nonce")).toBe(nonceOf(first));
    expect(first.headers.get("x-middleware-request-content-security-policy")).toBe(
      first.headers.get("content-security-policy"),
    );
  });

  it("sets no policy under next dev, whose fast refresh evaluates code", () => {
    vi.stubEnv("NODE_ENV", "development");

    const response = proxy(page("/transactions", SIGNED_IN));

    expect(response.headers.get("content-security-policy")).toBeNull();
  });
});
