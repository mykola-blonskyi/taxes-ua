import { createServer } from "node:http";
import type { AddressInfo } from "node:net";

export type Stub = { port: number; requests: string[]; close: () => Promise<void> };

// A stand-in for an external service the api would otherwise call. It answers every request with the
// given JSON and records "METHOD path", so a test can assert what the api asked for.
export async function startStub(body: unknown | ((url: URL) => unknown)): Promise<Stub> {
  const requests: string[] = [];
  const server = createServer((request, response) => {
    requests.push(`${request.method} ${request.url}`);
    response.setHeader("content-type", "application/json");
    const answer = typeof body === "function" ? body(new URL(request.url ?? "/", "http://stub")) : body;
    response.end(JSON.stringify(answer));
  });
  // The api runs in a container and reaches the host through host.docker.internal, so loopback is not enough.
  await new Promise<void>((resolve) => server.listen(0, "0.0.0.0", resolve));

  return {
    port: (server.address() as AddressInfo).port,
    requests,
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}

// The NBU exchange endpoint the api asks for a rate, answered for whatever currency and day it names with
// a fixed rate. The suite's own data is in hryvnias, so this only makes the real NBU unreachable.
export function startNbuStub() {
  return startStub((url) => {
    const day = url.searchParams.get("date") ?? "";
    return [
      {
        r030: 840,
        txt: "stub",
        rate: 40,
        cc: url.searchParams.get("valcode"),
        exchangedate: `${day.slice(6, 8)}.${day.slice(4, 6)}.${day.slice(0, 4)}`,
      },
    ];
  });
}
