import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { describe, expect, it } from "vitest";

// ADR-028: the api's title, detail and field sentences are English for logs, and a screen translates the
// code instead. These guards fail the build when a source starts reading that English again, either by
// touching `message`, `title` or `detail` as data or by carrying a sentence the api sends.
const web = process.cwd();
const sourceRoot = join(web, "src");

function files(directory: string, extension: RegExp): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name);

    return statSync(path).isDirectory() ? files(path, extension) : extension.test(name) ? [path] : [];
  });
}

const sources = files(sourceRoot, /\.(ts|tsx)$/)
  .filter((path) => !/\.test\.tsx?$/.test(path) && !path.endsWith("schema.d.ts") && !path.includes(`${join("src", "test")}`))
  .map((path) => ({ path: relative(web, path), text: readFileSync(path, "utf8") }));

// The code with comments and the text of string literals blanked, so only identifiers and operators remain.
function codeOnly(text: string): string {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, " ")
    .replace(/(^|[^:])\/\/.*$/gm, "$1")
    .replace(/"(?:[^"\\\n]|\\.)*"/g, '""')
    .replace(/'(?:[^'\\\n]|\\.)*'/g, "''")
    .replace(/`(?:[^`\\]|\\[\s\S])*`/g, (literal) => `\`${[...literal.matchAll(/\$\{([^}]*)\}/g)].map((match) => match[1]).join(" ")}\``);
}

describe("no web source reads the api's English", () => {
  it("finds the sources it guards", () => {
    expect(sources.length).toBeGreaterThan(100);
  });

  it("never reads an error's message, title or detail as text to show or match", () => {
    const reads = /\.(message|detail)\b(?!\s*:)|\b(error|failure|problem)\w*\.title\b/i;
    const matching = /\b(message|title|detail)\w*\.(includes|startsWith|endsWith|match|test|indexOf)\(|\b(message|title|detail)\w*\s*(===|!==)\s*["'`]/;
    const offenders = sources
      // The client is the one place that keeps the title, as the Error's message for a log.
      .filter(({ path }) => path !== join("src", "data", "api", "client.ts"))
      .flatMap(({ path, text }) =>
        codeOnly(text)
          .split("\n")
          .flatMap((line, index) => (reads.test(line) || matching.test(line) ? [`${path}:${index + 1}: ${line.trim()}`] : [])),
      );

    expect(offenders).toEqual([]);
  });

  it("carries none of the English sentences the api sends", () => {
    const apiSources = files(resolve(web, "..", "api", "src", "TaxesUa.Api"), /\.cs$/).filter(
      (path) => !path.includes(`${join("", "obj")}`) && !path.includes(`${join("", "bin")}`),
    );
    // A sentence is a literal of three or more words that ends in a full stop or a question mark; an
    // interpolated one contributes its fixed parts.
    const sentences = new Set(
      apiSources.flatMap((path) =>
        [...readFileSync(path, "utf8").matchAll(/\$?"((?:[^"\\\n]|\\.)*)"/g)]
          .map((match) => match[1]!)
          .flatMap((literal) => literal.split(/\{[^}]*\}/))
          .map((part) => part.trim())
          .filter((part) => part.length >= 24 && part.split(/\s+/).length >= 4 && /[A-Za-z]{3}/.test(part) && /[.?]$/.test(part)),
      ),
    );

    expect(sentences.size).toBeGreaterThan(100);
    const offenders = sources.flatMap(({ path, text }) =>
      [...sentences].filter((sentence) => text.includes(sentence)).map((sentence) => `${path}: ${sentence}`),
    );

    expect(offenders).toEqual([]);
  });
});
