import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import ru from "../../../messages/ru.json";
import uk from "../../../messages/uk.json";

// public/offline.html is static (the service worker serves it with no app and no api), so its text is a
// copy of the `offline` catalog. This keeps the copy from drifting.
const html = readFileSync(path.join(__dirname, "../../../public/offline.html"), "utf8");

describe("public/offline.html", () => {
  it.each([
    ["uk", uk],
    ["ru", ru],
  ])("carries the %s text of the offline catalog", (_locale, catalog) => {
    expect(html).toContain(catalog.offline.title);
    expect(html).toContain(catalog.offline.body);
    expect(html).toContain(catalog.offline.retry);
  });

  it("works under script-src 'self' and style-src 'self': no inline script, style or handler", () => {
    expect(html).not.toMatch(/<script(?![^>]*\ssrc=)[^>]*>|<style|\sstyle=|\son[a-z]+=/i);
    expect([...html.matchAll(/<(?:script|link)[^>]+(?:src|href)="([^"]+)"/g)].map((m) => m[1])).toEqual([
      "/offline.css",
      "/offline.js",
    ]);
  });

  it("carries no api call", () => {
    expect(html).not.toContain("/api/");
    expect(readFileSync(path.join(__dirname, "../../../public/offline.js"), "utf8")).not.toContain("/api/");
  });

  it("sets the Russian title along with the Russian text", () => {
    const script = readFileSync(path.join(__dirname, "../../../public/offline.js"), "utf8");

    expect(script).toContain(`${ru.offline.title} · ${ru.app.title}`);
    expect(html).toContain(`<title>${uk.offline.title} · ${uk.app.title}</title>`);
  });
});
