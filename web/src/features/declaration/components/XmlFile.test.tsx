import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DeclarationFile, DeclarationResponse } from "@/data/declarations/useDeclarations";
import { reply, renderApp, screen, stubFetch, useFakeTimers, within } from "@/test/harness";
import { XmlFile } from "./XmlFile";

const period = { year: 2026, quarter: 2 };

const withAnnex: DeclarationFile = {
  type: "Reporting",
  fileName: "F0103309_2026_2.xml",
  annexFileName: "F0103309_2026_2_D1.xml",
  generatedAt: "2026-07-05T09:30:00Z",
};
const withoutAnnex: DeclarationFile = { ...withAnnex, annexFileName: null };

function declaration(
  overrides: { ready?: boolean; esvKop?: number | null; files?: DeclarationFile[]; fileAvailable?: boolean } = {},
) {
  const { ready = true, esvKop = 190_234, files = [], fileAvailable = true } = overrides;

  return {
    year: 2026,
    quarter: 2,
    figures: { esvKop },
    readiness: { ready },
    filed: null,
    files,
    fileAvailable,
    fileAvailableFrom: "2026-07-01",
  } as unknown as DeclarationResponse;
}

type Download = { href: string; download: string; at: number };
let downloads: Download[];
let timers: ReturnType<typeof useFakeTimers>;

// The owner's browser starts a download when the page clicks a temporary link, so those clicks are what
// the test watches. The fake clock is what "about 500 ms later" is measured on.
beforeEach(() => {
  downloads = [];
  timers = useFakeTimers();
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    downloads.push({ href: new URL(this.href).pathname, download: this.download, at: Date.now() });
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

// The request and its response settle on promises, not on the fake clock, so the clock moves a
// millisecond at a time until the condition holds.
async function until(condition: () => boolean) {
  for (let step = 0; step < 200 && !condition(); step++) {
    await timers.advance(1);
  }
  expect(condition()).toBe(true);
}

function renderXml(xml: DeclarationResponse, locale: "uk" | "ru" = "uk") {
  return renderApp(<XmlFile declaration={xml} period={period} />, { locale }, timers.userOptions);
}

describe("XmlFile", () => {
  describe("one click on a declaration with an annex", () => {
    it("asks for the file, downloads the declaration at once and the annex 500 ms later", async () => {
      const api = stubFetch({ "POST /api/declarations/{year}/{quarter}/files": withAnnex });
      const { user } = renderXml(declaration());

      await user.click(screen.getByRole("button", { name: "Завантажити XML" }));
      await until(() => downloads.length === 1);

      expect(api.requests).toEqual([
        { method: "POST", path: "/api/declarations/2026/2/files", query: {}, body: { type: "Reporting" } },
      ]);
      expect(downloads[0]).toMatchObject({
        href: "/api/declarations/2026/2/files/Reporting",
        download: "F0103309_2026_2.xml",
      });

      // `until` may have stepped a few milliseconds past the first download, so the delay is measured from it.
      await timers.advance(downloads[0].at + 499 - Date.now());
      expect(downloads).toHaveLength(1);

      await timers.advance(1);
      expect(downloads).toHaveLength(2);
      expect(downloads[1]).toMatchObject({
        href: "/api/declarations/2026/2/files/Reporting/annex",
        download: "F0103309_2026_2_D1.xml",
      });
      expect(downloads[1].at - downloads[0].at).toBe(500);
    });

    it("does the same in Russian", async () => {
      stubFetch({ "POST /api/declarations/{year}/{quarter}/files": withAnnex });
      const { user } = renderXml(declaration(), "ru");

      expect(screen.getByRole("heading", { name: "XML для Электронного кабинета" })).toBeVisible();
      await user.click(screen.getByRole("button", { name: "Скачать XML" }));
      await until(() => downloads.length === 1);
      await timers.advance(500);

      expect(downloads.map((download) => download.download)).toEqual(["F0103309_2026_2.xml", "F0103309_2026_2_D1.xml"]);
    });

    it("shows the preparing label until the file is ready", async () => {
      let answer: ((file: DeclarationFile) => void) | null = null;
      stubFetch({
        "POST /api/declarations/{year}/{quarter}/files": () => new Promise((resolve) => (answer = resolve)),
      });
      const { user } = renderXml(declaration());

      await user.click(screen.getByRole("button", { name: "Завантажити XML" }));

      await until(() => answer !== null);
      expect(screen.getByRole("button", { name: "Підготовка…" })).toBeDisabled();

      answer!(withAnnex);
      await until(() => downloads.length === 1);

      expect(screen.getByRole("button", { name: "Завантажити XML" })).toBeEnabled();
    });
  });

  it("downloads one file and waits for no annex when the declaration has none", async () => {
    stubFetch({ "POST /api/declarations/{year}/{quarter}/files": withoutAnnex });
    const { user } = renderXml(declaration({ esvKop: null }));

    expect(screen.queryByText(/додаток 1 з єдиним внеском/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Завантажити XML" }));
    await until(() => downloads.length === 1);
    await timers.advance(2_000);

    expect(downloads).toHaveLength(1);
  });

  it("sends the declaration type the owner chose", async () => {
    const api = stubFetch({ "POST /api/declarations/{year}/{quarter}/files": { ...withoutAnnex, type: "Clarifying" } });
    const { user } = renderXml(declaration());

    await user.selectOptions(screen.getByRole("combobox", { name: "Тип декларації" }), "Уточнююча");
    await user.click(screen.getByRole("button", { name: "Завантажити XML" }));
    await until(() => downloads.length === 1);

    expect(api.requests[0].body).toEqual({ type: "Clarifying" });
    expect(downloads[0].href).toBe("/api/declarations/2026/2/files/Clarifying");
  });

  describe("when the file cannot be prepared", () => {
    it("explains a declaration that is not ready and sends nothing", async () => {
      const api = stubFetch({});
      renderXml(declaration({ ready: false }));

      expect(screen.getByRole("button", { name: "Завантажити XML" })).toBeDisabled();
      expect(screen.getByText("Файл можна підготувати, коли все готово до подання.")).toBeVisible();
      expect(api.requests).toHaveLength(0);
    });

    it("says so in Russian", () => {
      renderXml(declaration({ ready: false }), "ru");

      expect(screen.getByRole("button", { name: "Скачать XML" })).toBeDisabled();
      expect(screen.getByText("Файл можно подготовить, когда всё готово к подаче.")).toBeVisible();
    });

    describe("a quarter that has not ended", () => {
      it("disables the download and says from which date it opens, even when everything else is ready", () => {
        const api = stubFetch({});
        renderXml(declaration({ fileAvailable: false }));

        expect(screen.getByRole("button", { name: "Завантажити XML" })).toBeDisabled();
        expect(screen.getByText(/^Файл можна підготувати з 1 лип\. 2026 р\., коли квартал закінчиться/)).toBeVisible();
        expect(screen.queryByText("Файл можна підготувати, коли все готово до подання.")).not.toBeInTheDocument();
        expect(api.requests).toHaveLength(0);
      });

      it("says so in Russian", () => {
        renderXml(declaration({ fileAvailable: false }), "ru");

        expect(screen.getByRole("button", { name: "Скачать XML" })).toBeDisabled();
        expect(screen.getByText(/^Файл можно подготовить с 1 июл\. 2026 г\., когда квартал закончится/)).toBeVisible();
      });
    });

    it("reports a conflict and downloads nothing", async () => {
      stubFetch({ "POST /api/declarations/{year}/{quarter}/files": reply(409, { title: "Not ready" }) });
      const { user } = renderXml(declaration());

      await user.click(screen.getByRole("button", { name: "Завантажити XML" }));
      await until(() => screen.queryByRole("alert") !== null);

      expect(screen.getByRole("alert")).toHaveTextContent("Декларація ще не готова до подання, тож файл не підготовлено.");
      expect(downloads).toHaveLength(0);
    });

    it("lists what the schema check rejected", async () => {
      stubFetch({
        "POST /api/declarations/{year}/{quarter}/files": reply(422, {
          title: "Invalid",
          errors: { file: ["Element 'KVED' is not valid."] },
        }),
      });
      const { user } = renderXml(declaration());

      await user.click(screen.getByRole("button", { name: "Завантажити XML" }));
      await until(() => screen.queryByRole("alert") !== null);

      const alert = screen.getByRole("alert");
      expect(alert).toHaveTextContent("З цих даних не вийде файл, що пройде перевірку схеми.");
      expect(within(alert).getByRole("listitem")).toHaveTextContent("Element 'KVED' is not valid.");
    });

    it("reports any other failure", async () => {
      stubFetch({ "POST /api/declarations/{year}/{quarter}/files": reply(500) });
      const { user } = renderXml(declaration(), "ru");

      await user.click(screen.getByRole("button", { name: "Скачать XML" }));
      await until(() => screen.queryByRole("alert") !== null);

      expect(screen.getByRole("alert")).toHaveTextContent("Не удалось подготовить файл.");
    });
  });

  describe("the prepared files", () => {
    it("links the declaration and its annex for another download", () => {
      renderXml(declaration({ files: [withAnnex] }));

      expect(screen.getByRole("link", { name: "Завантажити декларацію" })).toHaveAttribute(
        "href",
        "/api/declarations/2026/2/files/Reporting",
      );
      expect(screen.getByRole("link", { name: "Завантажити додаток 1 (ЄСВ)" })).toHaveAttribute(
        "href",
        "/api/declarations/2026/2/files/Reporting/annex",
      );
      expect(screen.getByText("F0103309_2026_2_D1.xml")).toBeVisible();
    });

    it("links only the declaration when there is no annex", () => {
      renderXml(declaration({ files: [withoutAnnex] }), "ru");

      expect(screen.getByRole("link", { name: "Скачать ещё раз" })).toHaveAttribute(
        "href",
        "/api/declarations/2026/2/files/Reporting",
      );
      expect(screen.queryByRole("link", { name: /приложение 1/ })).not.toBeInTheDocument();
    });

    it("says there are none yet", () => {
      renderXml(declaration());

      expect(screen.getByText("Файлів ще немає.")).toBeVisible();
    });
  });
});
