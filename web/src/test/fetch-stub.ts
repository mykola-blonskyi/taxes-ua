import type { paths } from "@/data/api/schema";

type Method = "get" | "post" | "put" | "patch" | "delete";

type OperationOf<P extends keyof paths, M extends Method> = M extends keyof paths[P]
  ? Exclude<paths[P][M], undefined>
  : never;

/** "GET /api/periods/{year}": every method and path the generated OpenAPI types declare. */
export type RouteKey = {
  [P in keyof paths & string]: {
    [M in Method]: [OperationOf<P, M>] extends [never] ? never : `${Uppercase<M>} ${P}`;
  }[Method];
}[keyof paths & string];

type Split<K extends RouteKey> = K extends `${infer M} ${infer P}`
  ? [Lowercase<M> & Method, P & keyof paths]
  : never;

type DeepPartial<T> = T extends readonly (infer U)[]
  ? readonly DeepPartial<U>[]
  : T extends object
    ? { [K in keyof T]?: DeepPartial<T[K]> }
    : T;

type SuccessBody<O> = O extends { responses: infer R }
  ? {
      [S in keyof R & (200 | 201 | 202)]: R[S] extends { content: { "application/json": infer B } } ? B : never;
    }[keyof R & (200 | 201 | 202)]
  : never;

/** The JSON body of the success response the generated types declare for this route. */
export type ResponseBody<K extends RouteKey> = SuccessBody<OperationOf<Split<K>[1], Split<K>[0]>>;

export type RecordedRequest = {
  method: string;
  /** The concrete path, such as "/api/periods/2026". */
  path: string;
  query: Record<string, string>;
  /** The parsed JSON body, or undefined when the request had none. */
  body: unknown;
};

/** A response that is not the declared success body: a failure status, a header, or an empty 204. */
export type Reply = { status: number; body?: unknown; headers?: Record<string, string> };

export function reply(status: number, body?: unknown, headers?: Record<string, string>): Reply {
  return { status, body, headers };
}

type Answer<K extends RouteKey> = DeepPartial<ResponseBody<K>> | Reply;

/**
 * A fixture is a partial body: a field the test does not set is absent, but a field the schema does not
 * have, or one of the wrong type, fails the typecheck. Pass a function to read the request or to answer
 * after a delay.
 */
export type Handler<K extends RouteKey> = Answer<K> | ((request: RecordedRequest) => Answer<K> | Promise<Answer<K>>);

export type Routes = { [K in RouteKey]?: Handler<K> };

type Route = { method: string; pattern: RegExp; params: number; handler: Handler<RouteKey> };

let routes: Route[] = [];
let recorded: RecordedRequest[] = [];
let unmatched: string[] = [];

function compile(key: string, handler: Handler<RouteKey>): Route {
  const [method, path] = key.split(" ");
  const params = path.match(/\{[^}]+\}/g)?.length ?? 0;
  const source = path.replace(/[.*+?^$()|[\]\\]/g, "\\$&").replace(/\{[^}]+\}/g, "([^/]+)");

  return { method, pattern: new RegExp(`^${source}$`), params, handler };
}

function isReply(answer: unknown): answer is Reply {
  return typeof answer === "object" && answer !== null && "status" in answer && typeof answer.status === "number";
}

function json(status: number, body: unknown, headers: Record<string, string> = {}) {
  if (body === undefined || status === 204) {
    return new Response(null, { status, headers });
  }

  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", ...headers } });
}

async function describeRequest(request: Request): Promise<RecordedRequest> {
  const url = new URL(request.url);
  const text = request.method === "GET" || request.method === "HEAD" ? "" : await request.clone().text();

  return {
    method: request.method,
    path: url.pathname,
    query: Object.fromEntries(url.searchParams),
    body: text ? JSON.parse(text) : undefined,
  };
}

/** The `fetch` the page sees. vitest.setup.ts installs it before the api client captures `fetch`. */
export async function delegatingFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const request = await describeRequest(new Request(input, init));
  // A literal route wins over one with parameters: "/payments/candidates" over "/payments/{id}".
  const route = [...routes]
    .sort((a, b) => a.params - b.params)
    .find((candidate) => candidate.method === request.method && candidate.pattern.test(request.path));

  if (!route) {
    unmatched.push(`${request.method} ${request.path}`);

    return json(501, { title: `No fetch stub for ${request.method} ${request.path}` });
  }

  recorded.push(request);
  const answer = typeof route.handler === "function" ? await route.handler(request) : route.handler;

  return isReply(answer) ? json(answer.status, answer.body, answer.headers) : json(200, answer);
}

export type FetchStub = {
  /** Every request a stubbed route answered, in order. */
  readonly requests: readonly RecordedRequest[];
  /** The requests one route answered. */
  requestsTo(key: RouteKey): RecordedRequest[];
};

/** Answers the page's requests from `answers` until the test ends. A request no route covers fails the test. */
export function stubFetch(answers: Routes): FetchStub {
  routes = Object.entries(answers).map(([key, handler]) => compile(key, handler as Handler<RouteKey>));
  recorded = [];
  unmatched = [];

  return {
    get requests() {
      return recorded;
    },
    requestsTo(key) {
      const { method, pattern } = compile(key, {});

      return recorded.filter((request) => request.method === method && pattern.test(request.path));
    },
  };
}

/** Runs after each test: clears the routes and fails the test that sent a request nothing answered. */
export function resetFetchStub() {
  const missed = unmatched;
  routes = [];
  recorded = [];
  unmatched = [];

  if (missed.length > 0) {
    throw new Error(`Requests with no fetch stub: ${missed.join(", ")}`);
  }
}
