import { describe, expect, it } from "vitest";
import type { CabinetField, DeclarationResponse } from "@/data/declarations/useDeclarations";
import { renderApp, screen, within } from "@/test/harness";
import { FillInCabinet } from "./FillInCabinet";

type Kind = CabinetField["kind"];

function field(
  element: string,
  value: string | null,
  part: CabinetField["part"],
  kind: Kind = "Amount",
  extra: Partial<CabinetField> = {},
): CabinetField {
  return { element, value, part, kind, line: null, row: 0, month: 0, column: 0, ...extra };
}

const line = (element: string, number: string, value: string | null) =>
  field(element, value, "Declaration", "Amount", { line: number });

// What the api sends for a reporting declaration of the first quarter: no excess income, no annex.
const header: CabinetField[] = [
  field("HSTI", "ГУ ДПС у м. Києві", "Header", "Text"),
  field("HNAME", "Тест Тестович", "Header", "Text"),
  field("HLOC", "Київ, вул. Тестова 1", "Header", "Text"),
  field("HTIN", "1234567890", "Header", "Text"),
  field("T1RXXXXG1S", "62.01", "Header", "Text", { row: 1 }),
];
const period: CabinetField[] = [
  field("H1KV", "1", "Period", "Mark"),
  field("HZY", "2026", "Period", "Number"),
];
const lines: CabinetField[] = [
  line("R006G3", "06", "123456.78"),
  line("R007G3", "07", null),
  line("R008G3", "08", "123456.78"),
  line("R009G3", "09", null),
  line("R011G3", "11", "6172.84"),
  line("R012G3", "12", "6172.84"),
  line("R013G3", "13", "0.00"),
  line("R0141G3", "14.1", "6172.84"),
  line("R014G3", "14", "6172.84"),
  line("R021G3", "21", null),
  line("R023G3", "23", "1234.57"),
  line("R024G3", "24", "0.00"),
  line("R025G3", "25", "1234.57"),
];
const annex: CabinetField[] = [
  field("HD1", "1", "Annex", "Mark"),
  field("HKVED", "62.01", "Annex", "Text"),
  field("R08G1D", "01.01.2026", "Annex", "Date", { line: "8" }),
  field("R08G2D", "31.12.2026", "Annex", "Date", { line: "8" }),
  field("R081G1", "6", "Annex", "Number", { line: "8.1" }),
  field("R091G2", "864700.00", "Annex", "Amount", { line: "9", month: 1, column: 2 }),
  field("R091G3", "22.00", "Annex", "Amount", { line: "9", month: 1, column: 3 }),
  field("R091G4", "190234.00", "Annex", "Amount", { line: "9", month: 1, column: 4 }),
  field("R09G2", "864700.00", "Annex", "Amount", { line: "9" }),
  field("R09G4", "190234.00", "Annex", "Amount", { line: "9" }),
];

function declaration(overrides: { cabinet?: CabinetField[]; confirmed?: boolean; fileAvailable?: boolean } = {}) {
  const { cabinet = [...period, ...header, ...lines], confirmed = true, fileAvailable = true } = overrides;

  return {
    year: 2026,
    quarter: 1,
    figures: {},
    limitCrossing: null,
    singleTaxRateBp: 500,
    excessRateBp: 1500,
    militaryLevyRateBp: 100,
    readiness: { group3Confirmed: confirmed },
    fileAvailable,
    fileAvailableFrom: "2026-04-01",
    cabinet: [...cabinet].sort(
      (a, b) => ["Header", "Period", "Declaration", "Annex"].indexOf(a.part) - ["Header", "Period", "Declaration", "Annex"].indexOf(b.part),
    ),
  } as unknown as DeclarationResponse;
}

describe("FillInCabinet", () => {
  it("lists the lines in the form's order with their number and label, and copies the plain value", async () => {
    const { user } = renderApp(<FillInCabinet declaration={declaration()} />);

    const form = screen.getByRole("region", { name: "Заповніть в Електронному кабінеті" });
    const labels = within(form)
      .getAllByText(/^\d+(\.\d)?\. /)
      .map((node) => /^(\d+(?:\.\d)?)\. /.exec(node.textContent ?? "")?.[1]);
    expect(labels).toEqual(["06", "08", "11", "12", "13", "14.1", "14", "23", "24", "25"]);
    expect(within(form).getByText("06. Обсяг доходу, що оподатковується за ставкою 5%")).toBeVisible();

    await user.click(within(form).getByRole("button", { name: "Копіювати: Рядок 06" }));
    expect(await navigator.clipboard.readText()).toBe("123456.78");
    await user.click(within(form).getByRole("button", { name: "Копіювати: Рядок 13" }));
    expect(await navigator.clipboard.readText()).toBe("0.00");
    await user.click(within(form).getByRole("button", { name: "Копіювати: Рядок 14.1" }));
    expect(await navigator.clipboard.readText()).toBe("6172.84");
  });

  it("copies the header values and the year, and names the period to pick", async () => {
    const { user } = renderApp(<FillInCabinet declaration={declaration()} />);

    await user.click(screen.getByRole("button", { name: "Копіювати: РНОКПП" }));
    expect(await navigator.clipboard.readText()).toBe("1234567890");
    await user.click(screen.getByRole("button", { name: "Копіювати: Звітний рік" }));
    expect(await navigator.clipboard.readText()).toBe("2026");
    expect(screen.getByText("Позначте: період «I квартал»")).toBeVisible();
  });

  it("tells the owner to leave the empty lines empty and gives them no button", () => {
    renderApp(<FillInCabinet declaration={declaration()} />);

    expect(screen.getByText("Рядки 07, 09, 21 залишіть порожніми.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Копіювати: Рядок 07" })).not.toBeInTheDocument();
  });

  it("makes manual entry the way in and never claims the Cabinet imports the file", () => {
    renderApp(<FillInCabinet declaration={declaration()} />);

    expect(screen.getByText(/«Введення звітності» → «Створити»/)).toBeVisible();
    expect(screen.getByText(/Електронний кабінет не імпортує XML/)).toBeVisible();
    expect(screen.queryByText(/Імпортувати XML з пристрою/)).not.toBeInTheDocument();
  });

  it("hides the annex when the quarter has none", () => {
    renderApp(<FillInCabinet declaration={declaration()} />);

    expect(screen.queryByRole("heading", { name: "Додаток 1: єдиний внесок" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Додаток 1 \(єдиний внесок\) подається/)).not.toBeInTheDocument();
  });

  it("shows the annex lines with their months and copies them", async () => {
    const { user } = renderApp(<FillInCabinet declaration={declaration({ cabinet: [...period, ...header, ...lines, ...annex] })} />);

    expect(screen.getByRole("heading", { name: "Додаток 1: єдиний внесок" })).toBeVisible();
    expect(screen.getByText(/Додаток 1 \(єдиний внесок\) подається/)).toBeVisible();
    expect(screen.getByText("Позначте: Додаток 1 (єдиний внесок)")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Копіювати: Січень: ставка, %" }));
    expect(await navigator.clipboard.readText()).toBe("22.00");
    await user.click(screen.getByRole("button", { name: "Копіювати: Пункт 8: період по" }));
    expect(await navigator.clipboard.readText()).toBe("31.12.2026");
    await user.click(screen.getByRole("button", { name: "Копіювати: Разом: сума єдиного внеску" }));
    expect(await navigator.clipboard.readText()).toBe("190234.00");
  });

  it("asks for the move to other taxes when the annex marks it", () => {
    const leaving = [...annex, field("H03", "1", "Annex", "Mark")];
    renderApp(<FillInCabinet declaration={declaration({ cabinet: [...period, ...header, ...lines, ...leaving] })} />);

    expect(screen.getByText("Позначте: перехід на сплату інших податків і зборів")).toBeVisible();
  });

  it("keeps the provisional note until group 3 is confirmed", () => {
    const { unmount } = renderApp(<FillInCabinet declaration={declaration({ confirmed: false })} />);

    expect(screen.getByRole("note")).toHaveTextContent(
      "Групу 3 ще не підтверджено в ДПС, тож цифри орієнтовні. Вводити їх у кабінеті можна й зараз.",
    );
    expect(screen.getByRole("link", { name: "Перевірити статус" })).toHaveAttribute("href", "/settings?tab=dps");
    unmount();

    renderApp(<FillInCabinet declaration={declaration({ confirmed: true })} />);
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("says the figures are a preview while the quarter runs", () => {
    renderApp(<FillInCabinet declaration={declaration({ fileAvailable: false })} />);

    expect(screen.getByText(/Квартал ще не закінчився, тож цифри попередні\./)).toBeVisible();
  });

  it("points at the readiness block while the header is missing", () => {
    renderApp(<FillInCabinet declaration={declaration({ cabinet: [...period, ...lines] })} />);

    expect(screen.getByText(/Шапка з’явиться/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "Копіювати: РНОКПП" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Копіювати: Рядок 06" })).toBeEnabled();
  });

  describe("in Russian", () => {
    it("words the guide and the buttons in Russian, keeps the form's line names and copies the same values", async () => {
      const { user } = renderApp(<FillInCabinet declaration={declaration({ cabinet: [...period, ...header, ...lines, ...annex] })} />, {
        locale: "ru",
      });

      expect(screen.getByRole("region", { name: "Заполните в Электронном кабинете" })).toBeVisible();
      expect(screen.getByText(/Электронный кабинет не импортирует XML/)).toBeVisible();
      expect(screen.getByText(/^06\. Обсяг доходу, що оподатковується за ставкою 5\s%$/)).toBeVisible();
      expect(screen.getByText("Строки 07, 09, 21 оставьте пустыми.")).toBeVisible();
      expect(screen.getByText("Отметьте: период «I квартал»")).toBeVisible();

      await user.click(screen.getByRole("button", { name: "Копировать: Строка 11" }));
      expect(await navigator.clipboard.readText()).toBe("6172.84");
      expect(screen.getAllByRole("status").some((node) => node.textContent === "Скопировано")).toBe(true);
      await user.click(screen.getByRole("button", { name: "Копировать: Январь: ставка, %" }));
      expect(await navigator.clipboard.readText()).toBe("22.00");
      await user.click(screen.getByRole("button", { name: "Копировать: Пункт 8: период с" }));
      expect(await navigator.clipboard.readText()).toBe("01.01.2026");
    });
  });
});
