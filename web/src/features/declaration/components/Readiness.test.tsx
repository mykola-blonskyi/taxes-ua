import { describe, expect, it } from "vitest";
import type { DeclarationReadiness } from "@/data/declarations/useDeclarations";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { Readiness } from "./Readiness";

const readiness: DeclarationReadiness = {
  receiptsToReview: 0,
  pendingPaymentCandidates: 0,
  taxYearVerified: true,
  registrationDateSet: true,
  missingDetails: [],
  outsideGroup3: false,
  beforeGroup3: false,
  group3Confirmed: true,
  fullNameSet: false,
  unpaid: { singleTaxKop: 0, militaryLevyKop: 0, esvKop: 0 },
  ready: true,
};

describe("Readiness and the full name", () => {
  it("warns without blocking that the declaration may go without the patronymic, and links to the field", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={readiness} />);

    expect(screen.getByText("Усе готово до подання")).toBeVisible();
    expect(screen.getByText(/можливо без по батькові/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Вказати повне ім’я" })).toHaveAttribute("href", "/settings?tab=declaration");
  });

  it("says it in Russian", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={readiness} />, { locale: "ru" });

    expect(screen.getByText(/возможно без отчества/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Указать полное имя" })).toBeVisible();
  });

  it("says nothing once the full name is set", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={{ ...readiness, fullNameSet: true }} />);

    expect(screen.queryByText(/без по батькові/)).not.toBeInTheDocument();
  });

  it("leaves a missing name to the blocking item", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={{ ...readiness, missingDetails: ["Name"], ready: false }} />);

    expect(screen.queryByText(/без по батькові/)).not.toBeInTheDocument();
  });
});
