import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./schema";

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message?: string,
  ) {
    super(message ?? `HTTP ${status}`);
    this.name = "ApiError";
  }
}

const failOnErrorStatus: Middleware = {
  onResponse({ response }) {
    if (!response.ok) {
      throw new ApiError(response.status, response.statusText || undefined);
    }

    return response;
  },
};

export const api = createClient<paths>({ baseUrl: "" });

api.use(failOnErrorStatus);
