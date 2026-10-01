export type IbanProblem =
  | { key: "ibanLength"; count: number }
  | { key: "ibanPrefix" | "ibanChars" | "ibanBank" | "ibanChecksum" };

// The api words an IBAN failure from a closed set (InvoicingEndpoints.IbanProblem); each one maps to a
// translated message, and the length keeps the number of characters the owner entered.
export function parseIbanProblem(message: string): IbanProblem | null {
  const length = /^iban has (\d+) characters/.exec(message);
  if (length) {
    return { key: "ibanLength", count: Number(length[1]) };
  }

  if (message.includes("must start with UA")) {
    return { key: "ibanPrefix" };
  }

  if (message.includes("only digits and capital letters")) {
    return { key: "ibanChars" };
  }

  if (message.includes("bank id must be")) {
    return { key: "ibanBank" };
  }

  return message.includes("checksum") ? { key: "ibanChecksum" } : null;
}
