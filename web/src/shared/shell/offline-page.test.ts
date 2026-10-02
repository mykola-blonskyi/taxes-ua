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

  it("carries no tax data and loads nothing from the app", () => {
    expect(html).not.toMatch(/<script[^>]+src=|<link[^>]+href=|\/api\//);
  });
});
