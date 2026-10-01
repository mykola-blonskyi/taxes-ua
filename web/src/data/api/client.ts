import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./schema";

type Problem = { title?: string | null; errors?: Record<string, string[]> };

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message?: string,
    readonly errors: Readonly<Record<string, string[]>> = {},
    // Whole seconds from a Retry-After header, for a 429 that says when to come back.
    readonly retryAfterSeconds: number | null = null,
  ) {
    super(message ?? `HTTP ${status}`);
    this.name = "ApiError";
  }
}

const failOnErrorStatus: Middleware = {
  // The api answers a failure as ProblemDetails, whose title is a sentence about this failure, and a
  // 400 from Results.ValidationProblem adds one message per rejected field. Reading that body here is
  // what keeps every caller on one client. A caller that needs those messages would otherwise need a
  // second client that returns errors instead of throwing.
  async onResponse({ response }) {
    if (response.ok) {
      return response;
    }

    const problem = response.headers.get("content-type")?.includes("json")
      ? ((await response.clone().json().catch(() => null)) as Problem | null)
      : null;

    throw new ApiError(
      response.status,
      problem?.title ?? response.statusText ?? undefined,
      problem?.errors ?? {},
      retryAfterSeconds(response),
    );
  },
};

function retryAfterSeconds(response: Response) {
  const seconds = Number(response.headers.get("retry-after"));

  return Number.isInteger(seconds) && seconds > 0 ? seconds : null;
}

export const api = createClient<paths>({ baseUrl: "" });

api.use(failOnErrorStatus);
