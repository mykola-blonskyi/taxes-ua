import { expect, type APIRequestContext, type APIResponse } from "@playwright/test";

export const ownerEmail = "owner@example.com";

// The Development seam signs the email in through the same callback and allowlist as Google, then
// redirects. The context follows the redirects and keeps the cookie; the answer is where they end.
export async function signIn(context: APIRequestContext, email: string) {
  return context.get(`/api/auth/login/development?email=${encodeURIComponent(email)}&returnUrl=%2F`);
}

async function json<T>(response: APIResponse): Promise<T> {
  expect(response.ok(), `${response.url()} answered ${response.status()}`).toBe(true);
  return (await response.json()) as T;
}

type Dashboard = { today: string; burden: { incomeKop: number } | null };
type Quarter = { quarter: number; incomeKop: number };

export function dashboard(owner: APIRequestContext) {
  return owner.get("/api/dashboard").then((response) => json<Dashboard>(response));
}

export async function quarterIncome(owner: APIRequestContext, year: number, quarter: number) {
  const periods = await json<{ quarters: Quarter[] }>(await owner.get(`/api/periods/${year}`));
  return periods.quarters.find((candidate) => candidate.quarter === quarter)?.incomeKop ?? 0;
}

// Makes the owner a registered FOP with a tax year for the current year, whatever state earlier tests
// left. It only ever raises the state to that floor, so any test can call it first.
export async function seedRegisteredOwner(owner: APIRequestContext) {
  const { today } = await dashboard(owner);
  const year = Number(today.slice(0, 4));

  const taxYears = await json<{ year: number }[]>(await owner.get("/api/tax-years"));
  if (!taxYears.some((taxYear) => taxYear.year === year)) {
    const source = Math.max(...taxYears.map((taxYear) => taxYear.year));
    expect((await owner.post(`/api/tax-years/${source}/clone-to/${year}`)).ok()).toBe(true);
  }

  const settings = await json<Record<string, unknown>>(await owner.get("/api/settings"));
  const registered = settings.fopRegistrationDate as string | null;
  if (registered === null || registered > `${year}-01-01`) {
    const updated = await owner.put("/api/settings", { data: { ...settings, fopRegistrationDate: `${year}-01-01` } });
    expect(updated.ok()).toBe(true);
  }

  return { year, quarter: Math.ceil(Number(today.slice(5, 7)) / 3) };
}
