import { describe, expect, it } from "vitest";
import { ApiError } from "@/data/api/client";
import { renderApp, screen } from "@/test/harness";
import { UploadFailure } from "./UploadFailure";

const words = { notJson: "Це не JSON.", tooLarge: "Завеликий.", failed: "Не вдалося відновити." };

describe("UploadFailure", () => {
  it("lists the invoice numbers a restore would lose, read from the problem's data", () => {
    const error = new ApiError(400, {
      message: "The backup lacks invoices.",
      code: "backup_missing_invoices",
      extensions: { missingInvoices: ["2031-002", "2031-003"] },
    });

    renderApp(<UploadFailure error={error} {...words} />);

    expect(screen.getByRole("alert")).toHaveTextContent("2031-002, 2031-003");
    expect(screen.queryByText(/The backup lacks/)).not.toBeInTheDocument();
  });

  it("words the problem and each rejected field by code, keeping the field's path", () => {
    const error = new ApiError(400, {
      message: "The backup file breaks the rules below.",
      code: "backup_invalid",
      fieldCodes: { "clients[2].name": ["duplicate_value"], "transactions[0].clientId": ["unknown_reference"] },
    });

    renderApp(<UploadFailure error={error} {...words} />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Не вдалося відновити.");
    expect(alert).toHaveTextContent("У файлі резервної копії є помилки.");
    expect(alert).toHaveTextContent("clients[2].name: Таке значення вже є.");
    expect(alert).toHaveTextContent("transactions[0].clientId: Посилання на запис, якого не існує.");
    expect(alert).not.toHaveTextContent("breaks the rules");
  });

  it("shows the generic sentence for a code it has no words for, never the English", () => {
    const error = new ApiError(400, { message: "Something English.", code: "a_code_from_the_future" });

    renderApp(<UploadFailure error={error} {...words} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Не вдалося виконати дію. Спробуйте ще раз.");
    expect(screen.queryByText(/Something English/)).not.toBeInTheDocument();
  });

  it("says only that the file is too large for a 413", () => {
    renderApp(<UploadFailure error={new ApiError(413, { code: "payload_too_large" })} {...words} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Завеликий.");
    expect(screen.queryByText("Файл завеликий.")).not.toBeInTheDocument();
  });
});
