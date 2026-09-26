#!/usr/bin/env node
// Drives the real WebAuthn ceremony against the real passkey endpoints using Chrome DevTools'
// virtual authenticator. A virtual authenticator is not a device: it proves the attestation and
// assertion paths, the RP ID, and the allowlist gate, and it proves nothing about iOS Safari,
// Android Chrome, or a hardware key. Say which one you ran.
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../../..");

const options = {
  base: "http://localhost:3000",
  email: "owner@example.com",
  rpId: "localhost",
  out: join(repoRoot, ".verify"),
  port: 9223,
  timeout: 20000,
};

for (let index = 2; index < process.argv.length; index += 2) {
  const key = process.argv[index].replace(/^--/, "");
  if (!(key in options)) {
    throw new Error(`unknown option --${key}. Known: ${Object.keys(options).join(", ")}`);
  }
  const value = process.argv[index + 1];
  options[key] = typeof options[key] === "number" ? Number(value) : value;
}

function chromeCommand() {
  const candidates = [
    process.env.CHROME_PATH,
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
  ].filter(Boolean);

  for (const candidate of candidates) {
    try {
      readFileSync(candidate);
      return candidate;
    } catch {
      continue;
    }
  }

  throw new Error(`no Chrome binary found. Tried:\n  ${candidates.join("\n  ")}\nSet CHROME_PATH.`);
}

async function waitFor(describe, predicate, timeout = options.timeout) {
  const deadline = Date.now() + timeout;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await predicate();
      if (last) {
        return last;
      }
    } catch (error) {
      last = error.message;
    }
    await new Promise((done) => setTimeout(done, 150));
  }
  throw new Error(`timed out after ${timeout}ms waiting for ${describe}. Last: ${last}`);
}

async function connect(webSocketDebuggerUrl) {
  const socket = new WebSocket(webSocketDebuggerUrl);
  await new Promise((done, fail) => {
    socket.addEventListener("open", done, { once: true });
    socket.addEventListener("error", () => fail(new Error("CDP socket failed")), { once: true });
  });

  let nextId = 1;
  const pending = new Map();

  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    const settle = pending.get(message.id);
    if (!settle) {
      return;
    }
    pending.delete(message.id);
    if (message.error) {
      settle.fail(new Error(`${message.error.message} (${message.error.code})`));
    } else {
      settle.done(message.result);
    }
  });

  return {
    send(method, params = {}) {
      const id = nextId++;
      socket.send(JSON.stringify({ id, method, params }));
      return new Promise((done, fail) => pending.set(id, { done, fail }));
    },
    close: () => socket.close(),
  };
}

async function evaluate(page, expression) {
  const { result, exceptionDetails } = await page.send("Runtime.evaluate", {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (exceptionDetails) {
    throw new Error(exceptionDetails.exception?.description ?? exceptionDetails.text);
  }
  return result.value;
}

// Runs in the page, so the cookies, the origin and the WebAuthn implementation are the real ones.
// Returns the status of every call rather than throwing, so a rejection is evidence and not a crash.
const ceremony = `async (optionsPath, submitPath, kind) => {
  const optionsResponse = await fetch(optionsPath, { method: "POST" });
  if (!optionsResponse.ok) {
    return { stage: "options", status: optionsResponse.status };
  }
  const optionsJson = await optionsResponse.json();
  const parsed = kind === "create"
    ? PublicKeyCredential.parseCreationOptionsFromJSON(optionsJson)
    : PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson);
  const credential = kind === "create"
    ? await navigator.credentials.create({ publicKey: parsed })
    : await navigator.credentials.get({ publicKey: parsed });
  const submit = await fetch(submitPath, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ credentialJson: JSON.stringify(credential) }),
  });
  return {
    stage: "submit",
    status: submit.status,
    rpId: parsed.rp ? parsed.rp.id : parsed.rpId,
    credentialId: credential.id,
    detail: submit.ok ? null : (await submit.text()).slice(0, 300),
  };
}`;

const run = (page, optionsPath, submitPath, kind) =>
  evaluate(page, `(${ceremony})(${JSON.stringify(optionsPath)}, ${JSON.stringify(submitPath)}, ${JSON.stringify(kind)})`);

const tamper = (page, optionsPath, submitPath) =>
  evaluate(
    page,
    `(async () => {
      await fetch(${JSON.stringify(optionsPath)}, { method: "POST" });
      const response = await fetch(${JSON.stringify(submitPath)}, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ credentialJson: JSON.stringify({ id: "AAAA", rawId: "AAAA", type: "public-key", response: { clientDataJSON: "AAAA", attestationObject: "AAAA", authenticatorData: "AAAA", signature: "AAAA" }, clientExtensionResults: {} }) }),
      });
      return { status: response.status, detail: (await response.text()).slice(0, 300) };
    })()`,
  );

mkdirSync(options.out, { recursive: true });
const profile = mkdtempSync(join(tmpdir(), "verify-passkey-"));

const chrome = spawn(
  chromeCommand(),
  [
    "--headless=new",
    `--remote-debugging-port=${options.port}`,
    `--user-data-dir=${profile}`,
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-extensions",
    "about:blank",
  ],
  { stdio: "ignore" },
);

const steps = [];
const failures = [];
let page;

const record = (name, expected, actual, extra = {}) => {
  const ok = expected === actual;
  steps.push({ name, expected, actual, ok, ...extra });
  if (!ok) {
    failures.push(`${name}: expected ${expected}, got ${actual}${extra.detail ? ` (${extra.detail})` : ""}`);
  }
};

try {
  const target = await waitFor("Chrome's debugging port", async () => {
    const response = await fetch(`http://127.0.0.1:${options.port}/json/list`);
    return (await response.json()).find((entry) => entry.type === "page");
  });

  page = await connect(target.webSocketDebuggerUrl);
  await page.send("Page.enable");
  await page.send("Runtime.enable");
  await page.send("WebAuthn.enable");

  const { authenticatorId } = await page.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });

  const seam = `${options.base}/api/auth/login/development?email=${encodeURIComponent(options.email)}&returnUrl=%2F`;
  await page.send("Page.navigate", { url: seam });
  await waitFor(
    "the Development sign-in seam to mint a session",
    async () => (await evaluate(page, `fetch("/api/auth/me").then((r) => r.status)`)) === 200,
  );

  const badAttestation = await tamper(page, "/api/auth/passkey/register/options", "/api/auth/passkey/register");
  record("an invalid attestation is rejected", 400, badAttestation.status, { detail: badAttestation.detail });

  const registration = await run(page, "/api/auth/passkey/register/options", "/api/auth/passkey/register", "create");
  record("the virtual authenticator registers a passkey", 204, registration.status, {
    rpId: registration.rpId,
    credentialId: registration.credentialId,
    detail: registration.detail,
  });

  if (registration.rpId !== options.rpId) {
    failures.push(`RP ID: expected ${options.rpId}, the server sent ${registration.rpId}`);
  }

  const credentials = await page.send("WebAuthn.getCredentials", { authenticatorId });
  record("the authenticator holds one discoverable credential", 1, credentials.credentials.length, {
    residentCredential: credentials.credentials[0]?.isResidentCredential,
    credentialRpId: credentials.credentials[0]?.rpId,
  });

  await evaluate(page, `fetch("/api/auth/logout", { method: "POST" }).then((r) => r.status)`);
  await page.send("Network.enable");
  await page.send("Network.clearBrowserCookies");
  record(
    "the session is gone before the passkey sign-in",
    401,
    await evaluate(page, `fetch("/api/auth/me").then((r) => r.status)`),
  );

  const badAssertion = await tamper(page, "/api/auth/passkey/login/options", "/api/auth/passkey/login");
  record("an invalid assertion is rejected", 401, badAssertion.status, { detail: badAssertion.detail });

  const bareAssertion = await evaluate(
    page,
    `fetch("/api/auth/passkey/login", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ credentialJson: "{}" }) }).then((r) => r.status)`,
  );
  record("an assertion with no ceremony underway is rejected", 400, bareAssertion);

  const assertion = await run(page, "/api/auth/passkey/login/options", "/api/auth/passkey/login", "get");
  record("the virtual authenticator signs in with the passkey alone", 204, assertion.status, {
    rpId: assertion.rpId,
    detail: assertion.detail,
  });

  record(
    "the passkey assertion minted a real session",
    200,
    await evaluate(page, `fetch("/api/auth/me").then((r) => r.status)`),
  );

  const me = await evaluate(page, `fetch("/api/auth/me").then((r) => r.ok ? r.json() : null)`);
  record("the session belongs to the allowlisted owner", options.email, me?.email);
} finally {
  page?.close();
  chrome.kill();
}

const report = { base: options.base, email: options.email, authenticator: "CDP virtual authenticator, not a device", steps, failures };
const reportPath = join(options.out, "passkey.json");
writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`);

for (const step of steps) {
  console.log(`${step.ok ? "ok  " : "FAIL"} ${step.name}: expected ${step.expected}, got ${step.actual}`);
}
console.log(`\nreport: ${reportPath}`);

if (failures.length > 0) {
  console.error(`\n${failures.length} failure(s):\n  ${failures.join("\n  ")}`);
  process.exitCode = 1;
}
