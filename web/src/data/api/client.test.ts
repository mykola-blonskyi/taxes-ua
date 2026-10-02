import { describe, expect, it } from "vitest";
import { ApiError, api } from "./client";

// The client's one public behaviour is what it throws, so each case answers a request with a canned
// response and reads the error that comes out.
async function failureOf(response: Response): Promise<ApiError> {
  try {
    await api.GET("/api/periods/{year}", {
      params: { path: { year: 2026 } },
      baseUrl: "http://localhost",
      fetch: async () => response,
    });
  } catch (error) {
    if (error instanceof ApiError) {
      return error;
    }

    throw error;
  }

  throw new Error("The request did not fail.");
}

function problem(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/problem+json", ...headers },
  });
}

describe("ApiError", () => {
  it("defaults its message to the status and carries no code, fields, extensions or wait", () => {
    const error = new ApiError(503);

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe("ApiError");
    expect(error.message).toBe("HTTP 503");
    expect(error.status).toBe(503);
    expect(error.code).toBeNull();
    expect(error.fieldCodes).toEqual({});
    expect(error.extensions).toEqual({});
    expect(error.retryAfterSeconds).toBeNull();
  });

  it("keeps what it is given", () => {
    const error = new ApiError(400, {
      message: "Bad",
      code: "validation_failed",
      fieldCodes: { iban: ["iban_length"] },
      extensions: { missingInvoices: ["2031-002"] },
      retryAfterSeconds: 7,
    });

    expect(error.message).toBe("Bad");
    expect(error.code).toBe("validation_failed");
    expect(error.fieldCodes).toEqual({ iban: ["iban_length"] });
    expect(error.extensions).toEqual({ missingInvoices: ["2031-002"] });
    expect(error.retryAfterSeconds).toBe(7);
  });
});

describe("the api client on a failed response", () => {
  it("returns the data of a successful response without throwing", async () => {
    const { data } = await api.GET("/api/periods/{year}", {
      params: { path: { year: 2026 } },
      baseUrl: "http://localhost",
      fetch: async () => Response.json({ year: 2026 }),
    });

    expect(data).toEqual({ year: 2026 });
  });

  it("throws the problem code, and the title only as the message for logs", async () => {
    const error = await failureOf(problem(409, { title: "The quarter is closed.", code: "quarter_not_ended" }));

    expect(error.status).toBe(409);
    expect(error.code).toBe("quarter_not_ended");
    expect(error.message).toBe("The quarter is closed.");
    expect(error.fieldCodes).toEqual({});
  });

  it("carries the per-field codes of a validation problem and not its English sentences", async () => {
    const error = await failureOf(
      problem(400, {
        title: "One or more validation errors occurred.",
        code: "validation_failed",
        errors: { iban: ["iban checksum is wrong."], name: ["name is required."] },
        errorCodes: { iban: ["iban_checksum"], name: ["required"] },
      }),
    );

    expect(error.status).toBe(400);
    expect(error.code).toBe("validation_failed");
    expect(error.fieldCodes).toEqual({ iban: ["iban_checksum"], name: ["required"] });
    expect(error.extensions).toEqual({});
  });

  it("keeps the data a problem carries beside its code", async () => {
    const error = await failureOf(
      problem(400, { title: "Missing.", code: "backup_missing_invoices", missingInvoices: ["2031-002"] }),
    );

    expect(error.code).toBe("backup_missing_invoices");
    expect(error.extensions).toEqual({ missingInvoices: ["2031-002"] });
  });

  it("reads a response that is not ProblemDetails as a failure with no code", async () => {
    const error = await failureOf(problem(500, { title: "Boom.", code: 7 }));

    expect(error.code).toBeNull();
  });

  it("falls back to the status text when the body is not JSON", async () => {
    const error = await failureOf(new Response("<html>Bad gateway</html>", { status: 502, statusText: "Bad Gateway" }));

    expect(error.status).toBe(502);
    expect(error.message).toBe("Bad Gateway");
    expect(error.code).toBeNull();
  });

  it("falls back to the status when a JSON body is malformed and there is no status text", async () => {
    const error = await failureOf(
      new Response("{not json", { status: 500, headers: { "content-type": "application/json" } }),
    );

    expect(error.status).toBe(500);
    expect(error.message).toBe("HTTP 500");
  });

  it("falls back to the status when the problem has no title", async () => {
    const error = await failureOf(problem(500, {}));

    expect(error.message).toBe("HTTP 500");
  });

  it("accepts a null title, code and error codes", async () => {
    const error = await failureOf(problem(500, { title: null, code: null, errorCodes: null }));

    expect(error.message).toBe("HTTP 500");
    expect(error.code).toBeNull();
    expect(error.fieldCodes).toEqual({});
  });

  describe("Retry-After", () => {
    it.each([
      ["whole seconds", "30", 30],
      ["a single second", "1", 1],
      ["a long wait", "3600", 3600],
      ["seconds with surrounding spaces", " 45 ", 45],
    ])("reads %s", async (_name, header, seconds) => {
      const error = await failureOf(problem(429, { title: "Slow down." }, { "retry-after": header }));

      expect(error.status).toBe(429);
      expect(error.retryAfterSeconds).toBe(seconds);
    });

    it.each([
      ["a missing header", undefined],
      ["zero", "0"],
      ["a negative number", "-5"],
      ["a fraction", "1.5"],
      ["an empty header", ""],
      ["text", "soon"],
      ["an HTTP date", "Wed, 21 Oct 2026 07:28:00 GMT"],
    ])("reads %s as no wait", async (_name, header) => {
      const error = await failureOf(
        problem(429, { title: "Slow down." }, header === undefined ? {} : { "retry-after": header }),
      );

      expect(error.retryAfterSeconds).toBeNull();
    });

    it("is read from a failure that has no JSON body too", async () => {
      const error = await failureOf(new Response("", { status: 429, headers: { "retry-after": "12" } }));

      expect(error.retryAfterSeconds).toBe(12);
    });
  });
});
