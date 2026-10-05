import createClient, { type Middleware } from "openapi-fetch";
import type { components, paths } from "./schema";

type Problem = Partial<components["schemas"]["FieldProblemDetails"]> & Record<string, unknown>;

type ApiErrorInit = {
  // The api's English title, for a log or a developer. A screen never shows it: it translates `code`.
  message?: string;
  // The stable snake_case name of the failure (ADR-028), null when the response carried none.
  code?: string | null;
  // The codes of each rejected field of a 400 or 422, keyed by field path.
  fieldCodes?: Readonly<Record<string, string[]>>;
  // The data a problem carries beside its code, such as the invoice numbers a restore would lose.
  extensions?: Readonly<Record<string, unknown>>;
  // Whole seconds from a Retry-After header, for a 429 that says when to come back.
  retryAfterSeconds?: number | null;
};

export class ApiError extends Error {
  readonly code: string | null;
  readonly fieldCodes: Readonly<Record<string, string[]>>;
  readonly extensions: Readonly<Record<string, unknown>>;
  readonly retryAfterSeconds: number | null;

  constructor(
    readonly status: number,
    init: ApiErrorInit = {},
  ) {
    super(init.message ?? `HTTP ${status}`);
    this.name = "ApiError";
    this.code = init.code ?? null;
    this.fieldCodes = init.fieldCodes ?? {};
    this.extensions = init.extensions ?? {};
    this.retryAfterSeconds = init.retryAfterSeconds ?? null;
  }
}

// The api's answer to a failed call, or null when the call never got one: a dropped connection or a
// thrown value that is not an ApiError.
export function problemOf(error: unknown): ApiError | null {
  return error instanceof ApiError ? error : null;
}

const knownMembers = new Set(["type", "title", "status", "detail", "instance", "code", "errors", "errorCodes"]);

// The api answers a failure as ProblemDetails: a machine `code`, an English `title` that is only for logs,
// and, for a rejected body, `errorCodes` with the code of each rejected field. Reading that body in one
// place keeps every caller on one client, including the ones that cannot use it (an image upload, a
// passkey ceremony), which parse their own response with this.
export async function readProblem(response: Response): Promise<ApiError> {
  const problem = response.headers.get("content-type")?.includes("json")
    ? ((await response.clone().json().catch(() => null)) as Problem | null)
    : null;

  return new ApiError(response.status, {
    message: problem?.title || response.statusText || undefined,
    code: typeof problem?.code === "string" ? problem.code : null,
    fieldCodes: problem?.errorCodes ?? {},
    extensions: Object.fromEntries(Object.entries(problem ?? {}).filter(([key]) => !knownMembers.has(key))),
    retryAfterSeconds: retryAfterSeconds(response),
  });
}

const failOnErrorStatus: Middleware = {
  async onResponse({ response }) {
    if (response.ok) {
      return response;
    }

    throw await readProblem(response);
  },
};

function retryAfterSeconds(response: Response) {
  const seconds = Number(response.headers.get("retry-after"));

  return Number.isInteger(seconds) && seconds > 0 ? seconds : null;
}

export const api = createClient<paths>({ baseUrl: "" });

api.use(failOnErrorStatus);
