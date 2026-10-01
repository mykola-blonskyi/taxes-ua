export type ErrorKey = "iban" | "recipientName" | "recipientCode" | "tooLong" | "controlChar";

// The api answers in English; the messages it can send for a field are a closed set, so each maps to a
// translated one and anything unforeseen is shown as it came.
export function errorKey(field: string, message: string): ErrorKey | null {
  if (message.includes("exceed")) {
    return "tooLong";
  }

  if (message.includes("control character")) {
    return "controlChar";
  }

  return field === "iban" || field === "recipientName" || field === "recipientCode" ? field : null;
}
