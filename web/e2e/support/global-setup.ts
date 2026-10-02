import { spawn } from "node:child_process";
import path from "node:path";
import { request, type FullConfig } from "@playwright/test";
import { ownerEmail, signIn } from "./api";
import { startStub } from "./stubs";

const repoRoot = path.resolve(__dirname, "../../..");

function compose(project: string, args: string[], env: Record<string, string>): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn(
      "docker",
      ["compose", "-p", project, "-f", "docker-compose.yml", "-f", "docker-compose.local.yml", ...args],
      { cwd: repoRoot, env: { ...process.env, ...env }, stdio: "inherit" },
    );
    child.on("error", reject);
    child.on("exit", (code) =>
      code === 0 ? resolve() : reject(new Error(`docker compose ${args[0]} exited with ${code}`)),
    );
  });
}

// Brings up a private copy of the stack, signs the owner in through the Development seam and saves the
// session. The returned function is the teardown: it removes the containers and their volumes.
export default async function globalSetup(config: FullConfig) {
  const { baseURL, storageState } = config.projects[0].use;
  const project = `taxesua-e2e-${process.pid}`;
  const telegram = await startStub({ ok: true, result: [] });
  const monobank = await startStub([]);
  const env = {
    WEB_PORT: new URL(baseURL!).port,
    ALLOWED_EMAILS: ownerEmail,
    TELEGRAM_BASE_URL: `http://host.docker.internal:${telegram.port}`,
    MONOBANK_BASE_URL: `http://host.docker.internal:${monobank.port}`,
  };

  const teardown = async () => {
    await compose(project, ["down", "--volumes", "--remove-orphans"], env).catch((error) => console.error(error));
    await Promise.all([telegram.close(), monobank.close()]);
  };

  try {
    // --wait returns once the db, api and web healthchecks pass.
    await compose(project, ["up", "--detach", "--build", "--wait", "--wait-timeout", "300"], env);

    const context = await request.newContext({ baseURL });
    const signedIn = await signIn(context, ownerEmail);
    if (!signedIn.ok() || (await context.get("/api/auth/me")).status() !== 200) {
      throw new Error(`Development sign-in failed: the seam ended at ${signedIn.status()}`);
    }
    await context.storageState({ path: storageState as string });
    await context.dispose();
  } catch (error) {
    // The stack is about to be removed, so this is the only record of why it did not come up.
    await compose(project, ["logs", "--no-color", "api", "web"], env).catch(() => undefined);
    await teardown();
    throw error;
  }

  return teardown;
}
