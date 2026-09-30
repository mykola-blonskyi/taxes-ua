import type { Currency } from "@/data/fx/useFxRate";
import type {
  InvoiceLineRequest,
  InvoiceRequest,
  InvoiceResponse,
  InvoiceUnit,
} from "@/data/invoices/useInvoices";
import { parseHryvnia } from "@/shared/lib/money";

export const maxQuantityThousandths = 1_000_000;

export type LineForm = {
  key: number;
  descriptionEn: string;
  descriptionUk: string;
  unit: InvoiceUnit;
  quantity: string;
  rate: string;
};

export type InvoiceForm = {
  clientId: string;
  issueDate: string;
  dueDate: string;
  currency: Currency;
  lines: LineForm[];
};

// Like parseHryvnia: thousandths are assembled from the digit strings, never from a float.
const quantityPattern = /^\s*(\d{1,7})(?:[.,](\d{1,3}))?\s*$/;

export function parseQuantity(input: string): number | null {
  const match = quantityPattern.exec(input);

  if (!match) {
    return null;
  }

  const thousandths = Number(match[1]) * 1000 + Number((match[2] ?? "").padEnd(3, "0"));

  return thousandths === 0 || thousandths > maxQuantityThousandths ? null : thousandths;
}

export function quantityToInput(thousandths: number): string {
  const whole = Math.floor(thousandths / 1000);
  const fraction = String(thousandths % 1000)
    .padStart(3, "0")
    .replace(/0+$/, "");

  return fraction === "" ? String(whole) : `${whole},${fraction}`;
}

export function rateToInput(rateMinor: number): string {
  const whole = Math.floor(rateMinor / 100);
  const fraction = String(rateMinor % 100).padStart(2, "0");

  return fraction === "00" ? String(whole) : `${whole},${fraction}`;
}

// Same arithmetic as the server: (quantity * rate + 500) / 1000, truncated. BigInt keeps it exact where
// the product would pass 2^53.
export function lineAmountMinor(quantityThousandths: number, rateMinor: number): number {
  return Number((BigInt(quantityThousandths) * BigInt(rateMinor) + BigInt(500)) / BigInt(1000));
}

export type LineTotals = { amounts: (number | null)[]; total: number };

export function computeTotals(lines: LineForm[]): LineTotals {
  let total = BigInt(0);

  const amounts = lines.map((line) => {
    const quantity = parseQuantity(line.quantity);
    const rate = parseHryvnia(line.rate);

    if (quantity === null || rate === null) {
      return null;
    }

    const amount = lineAmountMinor(quantity, rate);
    total += BigInt(amount);

    return amount;
  });

  return { amounts, total: Number(total) };
}

export function addDays(isoDate: string, days: number): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  const shifted = new Date(Date.UTC(year, month - 1, day + days));

  return shifted.toISOString().slice(0, 10);
}

let nextLineKey = 0;

export function newLine(): LineForm {
  nextLineKey += 1;

  return { key: nextLineKey, descriptionEn: "", descriptionUk: "", unit: "Service", quantity: "1", rate: "" };
}

export function emptyForm(today: string): InvoiceForm {
  return {
    clientId: "",
    issueDate: today,
    dueDate: addDays(today, 14),
    currency: "UAH",
    lines: [newLine()],
  };
}

export function formFromInvoice(invoice: InvoiceResponse): InvoiceForm {
  return {
    clientId: invoice.clientId,
    issueDate: invoice.issueDate,
    dueDate: invoice.dueDate,
    currency: invoice.currency,
    lines: invoice.lines.map((line) => ({
      ...newLine(),
      descriptionEn: line.descriptionEn,
      descriptionUk: line.descriptionUk,
      unit: line.unit,
      quantity: quantityToInput(Number(line.quantityThousandths)),
      rate: rateToInput(Number(line.rateMinor)),
    })),
  };
}

// Keys are the api's own (`lines[2].rateMinor`), so a local rejection and a server one render alike.
export type FieldErrors = Record<string, string[]>;

export type BuiltRequest = { request: InvoiceRequest; errors: null } | { request: null; errors: FieldErrors };

export function buildRequest(form: InvoiceForm, messages: { quantity: string; rate: string }): BuiltRequest {
  const errors: FieldErrors = {};
  const lines: InvoiceLineRequest[] = [];

  form.lines.forEach((line, index) => {
    const quantityThousandths = parseQuantity(line.quantity);
    const rateMinor = parseHryvnia(line.rate);

    if (quantityThousandths === null) {
      errors[`lines[${index}].quantityThousandths`] = [messages.quantity];
    }

    if (rateMinor === null) {
      errors[`lines[${index}].rateMinor`] = [messages.rate];
    }

    if (quantityThousandths !== null && rateMinor !== null) {
      lines.push({
        descriptionEn: line.descriptionEn,
        descriptionUk: line.descriptionUk,
        unit: line.unit,
        quantityThousandths,
        rateMinor,
      });
    }
  });

  if (Object.keys(errors).length > 0) {
    return { request: null, errors };
  }

  return {
    request: {
      clientId: form.clientId,
      issueDate: form.issueDate,
      dueDate: form.dueDate,
      currency: form.currency,
      lines,
    },
    errors: null,
  };
}
