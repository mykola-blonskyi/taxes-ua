import { createServer } from "node:http";
import type { AddressInfo } from "node:net";

export type Stub = { port: number; requests: string[]; close: () => Promise<void> };

// A stand-in for an external service the api would otherwise call. It answers every request with the
// given JSON and records "METHOD path", so a test can assert what the api asked for.
export async function startStub(body: unknown): Promise<Stub> {
  const requests: string[] = [];
  const server = createServer((request, response) => {
    requests.push(`${request.method} ${request.url}`);
    response.setHeader("content-type", "application/json");
    response.end(JSON.stringify(body));
  });
  // The api runs in a container and reaches the host through host.docker.internal, so loopback is not enough.
  await new Promise<void>((resolve) => server.listen(0, "0.0.0.0", resolve));

  return {
    port: (server.address() as AddressInfo).port,
    requests,
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}
