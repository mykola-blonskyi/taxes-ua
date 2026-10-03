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
  unknownKvedCodes: [],
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

describe.each([
  {
    locale: "uk" as const,
    text: "КВЕД 12.34 немає в класифікаторі КВЕД ДК 009:2010 — виправте його в налаштуваннях.",
    second: "КВЕД 99.99 немає в класифікаторі КВЕД ДК 009:2010 — виправте його в налаштуваннях.",
    fix: "Виправити",
  },
  {
    locale: "ru" as const,
    text: "КВЭД 12.34 нет в классификаторе КВЭД ДК 009:2010 — исправьте его в настройках.",
    second: "КВЭД 99.99 нет в классификаторе КВЭД ДК 009:2010 — исправьте его в настройках.",
    fix: "Исправить",
  },
])("Readiness and a stored KVED code the classifier does not know in $locale", ({ locale, text, second, fix }) => {
  it("lists each code as its own item that links to the settings", () => {
    stubFetch({});
    renderApp(
      <Readiness year={2026} readiness={{ ...readiness, unknownKvedCodes: ["12.34", "99.99"], ready: false }} />,
      { locale },
    );

    expect(screen.getByText(text)).toBeVisible();
    expect(screen.getByText(second)).toBeVisible();
    const links = screen.getAllByRole("link", { name: fix });
    expect(links).toHaveLength(2);
    expect(links[0]).toHaveAttribute("href", "/settings?tab=declaration");
  });

  it("adds no item when every stored code is known", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={readiness} />, { locale });

    expect(screen.queryByText(/12\.34/)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: fix })).not.toBeInTheDocument();
  });
});
