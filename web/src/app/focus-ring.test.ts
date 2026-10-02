import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// WCAG 1.4.11: the focus indicator needs 3:1 against the colours next to it. The ring is drawn solid in
// --ring, next to the page (--background) or a filled button (--primary). Every neutral here is an oklch grey, whose relative luminance is L cubed.
const css = readFileSync(path.resolve(__dirname, "globals.css"), "utf8");

function greyLuminance(block: string, name: string) {
  const match = new RegExp(`${name}:\\s*oklch\\(([\\d.]+) 0 0\\)`).exec(block);
  if (!match) throw new Error(`${name} is not an oklch grey in the block`);
  return Number(match[1]) ** 3;
}

const contrast = (a: number, b: number) => (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);

describe("the focus ring", () => {
  const light = css.slice(css.indexOf(":root {"), css.indexOf(".dark {"));
  const dark = css.slice(css.indexOf(".dark {"), css.indexOf("@layer base"));

  it.each([
    ["light", light],
    ["dark", dark],
  ])("has at least 3:1 against the page and against a filled button in the %s theme", (_theme, block) => {
    const ring = greyLuminance(block, "--ring");
    expect(contrast(ring, greyLuminance(block, "--background"))).toBeGreaterThanOrEqual(3);
    expect(contrast(ring, greyLuminance(block, "--primary"))).toBeGreaterThanOrEqual(3);
  });

  it("is not drawn translucent", () => {
    expect(css).not.toMatch(/ring-ring\/\d+/);
  });
});
