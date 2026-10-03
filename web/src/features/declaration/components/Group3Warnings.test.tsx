import { describe, expect, it } from "vitest";
import type { DeclarationReadiness, DeclarationResponse } from "@/data/declarations/useDeclarations";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { FilingMark } from "./FilingMark";
import { Readiness } from "./Readiness";
import { XmlFile } from "./XmlFile";

const period = { year: 2026, quarter: 3 };
const today = "2026-10-02";

const readiness: DeclarationReadiness = {
  receiptsToReview: 0,
  pendingPaymentCandidates: 0,
  taxYearVerified: true,
  registrationDateSet: true,
  missingDetails: [],
  outsideGroup3: false,
  beforeGroup3: false,
  group3Confirmed: false,
  fullNameSet: true,
  unpaid: { singleTaxKop: 0, militaryLevyKop: 0, esvKop: 0 },
  ready: true,
};

function declaration(overrides: Partial<DeclarationReadiness>) {
  return {
    year: 2026,
    quarter: 3,
    figures: { esvKop: null },
    readiness: { ...readiness, ...overrides },
    filed: null,
    files: [],
    fileAvailable: true,
    fileAvailableFrom: "2026-10-01",
  } as unknown as DeclarationResponse;
}

describe("the declaration while group 3 is unconfirmed", () => {
  it("warns beside the filed mark and the file, and leaves both actions open", () => {
    stubFetch({});
    renderApp(
      <>
        <FilingMark filed={null} period={period} today={today} group3Confirmed={false} />
        <XmlFile declaration={declaration({})} period={period} />
      </>,
    );

    expect(
      screen.getByText(/^Групу 3 ще не підтверджено в ДПС, тож цифри орієнтовні\. Позначити декларацію поданою можна й зараз\./),
    ).toBeVisible();
    expect(
      screen.getByText(/^Групу 3 ще не підтверджено в ДПС, тож цифри у файлі орієнтовні\. Підготувати файл можна й зараз\./),
    ).toBeVisible();
    for (const link of screen.getAllByRole("link", { name: "Перевірити статус" })) {
      expect(link).toHaveAttribute("href", "/settings?tab=dps");
    }
    expect(screen.getByRole("button", { name: "Позначити поданою" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Завантажити XML" })).toBeEnabled();
  });

  it("says nothing once group 3 is confirmed", () => {
    stubFetch({});
    renderApp(
      <>
        <FilingMark filed={null} period={period} today={today} group3Confirmed />
        <XmlFile declaration={declaration({ group3Confirmed: true })} period={period} />
      </>,
    );

    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("warns in Russian", () => {
    stubFetch({});
    renderApp(
      <>
        <FilingMark filed={null} period={period} today={today} group3Confirmed={false} />
        <XmlFile declaration={declaration({})} period={period} />
      </>,
      { locale: "ru" },
    );

    expect(screen.getByText(/^Группа 3 ещё не подтверждена в ДПС, поэтому цифры ориентировочные\./)).toBeVisible();
    expect(screen.getByText(/^Группа 3 ещё не подтверждена в ДПС, поэтому цифры в файле ориентировочные\./)).toBeVisible();
    expect(screen.getAllByRole("link", { name: "Проверить статус" })).toHaveLength(2);
  });
});

describe("Readiness of a quarter before group 3", () => {
  it("says the quarter is filed under the general system, not that the limit was crossed", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={{ ...readiness, beforeGroup3: true, outsideGroup3: true, ready: false }} />);

    expect(
      screen.getByText(
        "Квартал до початку 3 групи: за нього звітують за загальною системою, а не декларацією платника єдиного податку",
      ),
    ).toBeVisible();
    expect(screen.getByRole("link", { name: "Статус у ДПС" })).toHaveAttribute("href", "/settings?tab=dps");
    expect(screen.queryByText(/після завершення 3 групи/)).not.toBeInTheDocument();
    expect(screen.queryByText("Квартал у межах 3 групи")).not.toBeInTheDocument();
  });

  it("keeps the limit wording for a quarter after a limit crossing", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={{ ...readiness, outsideGroup3: true, ready: false }} />, { locale: "ru" });

    expect(
      screen.getByText("Квартал после окончания 3 группы: декларацию подают по новой системе налогообложения"),
    ).toBeVisible();
    expect(screen.queryByText(/до начала 3 группы/)).not.toBeInTheDocument();
  });

  it("says it in Russian", () => {
    stubFetch({});
    renderApp(<Readiness year={2026} readiness={{ ...readiness, beforeGroup3: true, ready: false }} />, { locale: "ru" });

    expect(
      screen.getByText("Квартал до начала 3 группы: за него отчитываются по общей системе, а не декларацией плательщика единого налога"),
    ).toBeVisible();
  });
});
