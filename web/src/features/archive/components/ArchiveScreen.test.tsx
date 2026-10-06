import { describe, expect, it } from "vitest";
import type { ArchiveResponse } from "@/data/archive/useArchive";
import { renderApp, screen, stubFetch, within } from "@/test/harness";
import { ArchiveScreen } from "./ArchiveScreen";

const full: ArchiveResponse = {
  year: 2025,
  invoices: [
    {
      id: "invoice:a1",
      name: "2025-001_Acme.pdf",
      url: "/api/invoices/a1/pdf",
      number: "2025-001",
      status: "Issued",
      issueDate: "2025-02-10",
      client: "Acme GmbH",
      currency: "UAH",
      totalMinor: 150_000,
    },
    {
      id: "invoice:a2",
      name: "2025-002_Acme.pdf",
      url: "/api/invoices/a2/pdf",
      number: "2025-002",
      status: "Cancelled",
      issueDate: "2025-03-01",
      client: "Acme GmbH",
      currency: "UAH",
      totalMinor: 90_000,
    },
  ],
  quarters: [
    {
      quarter: 1,
      filed: { filedOn: "2025-04-20", type: "Reporting" },
      files: [
        {
          id: "declaration:2025-1-Reporting",
          type: "Reporting",
          annex: false,
          name: "F0103309_2025_1.xml",
          url: "/api/declarations/2025/1/files/Reporting",
          generatedAt: "2025-04-18T09:00:00Z",
        },
      ],
    },
    {
      quarter: 4,
      filed: null,
      files: [
        {
          id: "declaration:2025-4-Reporting",
          type: "Reporting",
          annex: false,
          name: "F0103309_2025_4.xml",
          url: "/api/declarations/2025/4/files/Reporting",
          generatedAt: "2026-01-20T09:00:00Z",
        },
        {
          id: "annex:2025-4-Reporting",
          type: "Reporting",
          annex: true,
          name: "F0133109_2025_4.xml",
          url: "/api/declarations/2025/4/files/Reporting/annex",
          generatedAt: "2026-01-20T09:00:00Z",
        },
      ],
    },
  ],
  statements: ["csv", "xlsx", "pdf"].map((format) => ({
    id: `statement:2025:${format}`,
    name: `transactions-2025.${format}`,
    url: `/api/export/transactions.${format}?year=2025`,
  })),
  payments: [{ id: "payments:2025:csv", name: "payments-2025.csv", url: "/api/payments/register.csv?year=2025" }],
};

const empty: ArchiveResponse = { year: 2025, invoices: [], quarters: [], statements: [], payments: [] };

function stub(archive: ArchiveResponse) {
  return stubFetch({
    "GET /api/tax-years": [{ year: 2025 }, { year: 2024 }],
    "GET /api/archive/{year}": (request) => ({ ...archive, year: Number(request.path.split("/").pop()) }),
  });
}

function hrefOf(name: string) {
  return screen.getByRole("link", { name }).getAttribute("href");
}

describe("ArchiveScreen", () => {
  it("lists the year's documents, each with a download of its own", async () => {
    stub(full);
    renderApp(<ArchiveScreen year="2025" />);

    expect(await screen.findByText("2025-001 · Acme GmbH")).toBeVisible();
    expect(screen.getByText("2025-002 · Acme GmbH")).toBeVisible();
    expect(screen.getByText("Скасовано")).toBeVisible();
    expect(hrefOf("Завантажити 2025-001_Acme.pdf")).toBe("/api/invoices/a1/pdf");
    expect(hrefOf("Завантажити F0103309_2025_1.xml")).toBe("/api/declarations/2025/1/files/Reporting");
    expect(hrefOf("Завантажити F0133109_2025_4.xml")).toBe("/api/declarations/2025/4/files/Reporting/annex");
    expect(hrefOf("Завантажити transactions-2025.xlsx")).toBe("/api/export/transactions.xlsx?year=2025");
    expect(hrefOf("Завантажити payments-2025.csv")).toBe("/api/payments/register.csv?year=2025");
    expect(screen.getByRole("link", { name: "Завантажити payments-2025.csv" })).toHaveAttribute("download", "payments-2025.csv");
  });

  it("shows the filing mark of a quarter, or says it has none", async () => {
    stub(full);
    renderApp(<ArchiveScreen year="2025" />);

    const declarations = within(await screen.findByRole("region", { name: "Декларації" }));

    expect(declarations.getByText(/Подано .* \(Звітна\)/)).toBeVisible();
    expect(declarations.getByText("Не позначено як подану")).toBeVisible();
    expect(declarations.getByText("Додаток 1 (ЄСВ) · Звітна")).toBeVisible();
  });

  it("says so in each section that holds nothing", async () => {
    stub(empty);
    renderApp(<ArchiveScreen year="2025" />);

    expect(await screen.findByText("За цей рік немає виставлених чи скасованих інвойсів.")).toBeVisible();
    expect(screen.getByText("За цей рік немає ні файлів декларацій, ні позначок про подання.")).toBeVisible();
    expect(screen.getByText("За цей рік немає надходжень.")).toBeVisible();
    expect(screen.getByText("За цей рік немає платежів до бюджету.")).toBeVisible();
    expect(screen.queryByRole("link", { name: /Завантажити/ })).toBeNull();
  });

  it("loads another year when the owner picks one", async () => {
    const api = stub(empty);
    const { user } = renderApp(<ArchiveScreen year="2025" />);
    await screen.findByText("За цей рік немає надходжень.");

    await user.selectOptions(screen.getByLabelText("Рік"), "2024");

    await screen.findByText("За цей рік немає надходжень.");
    expect(api.requestsTo("GET /api/archive/{year}").map((request) => request.path)).toEqual([
      "/api/archive/2025",
      "/api/archive/2024",
    ]);
  });

  it("reads in Russian", async () => {
    stub(full);
    renderApp(<ArchiveScreen year="2025" />, { locale: "ru" });

    expect(await screen.findByRole("region", { name: "Декларации" })).toBeVisible();
    expect(screen.getByRole("region", { name: "Реестр платежей в бюджет" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Скачать payments-2025.csv" })).toBeVisible();
    expect(screen.getByText("Отменён", { exact: false })).toBeVisible();
  });
});
