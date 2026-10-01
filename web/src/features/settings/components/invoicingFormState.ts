import type {
  InvoicingDetailsRequest,
  InvoicingDetailsResponse,
  PaymentDetailsInput,
} from "@/data/invoicing/useInvoicing";

export type Currency = PaymentDetailsInput["currency"];

export const currencies = ["UAH", "USD", "EUR"] as const satisfies readonly Currency[];

export type PaymentForm = Omit<PaymentDetailsInput, "currency">;

export const emptyPayment: PaymentForm = {
  iban: "",
  beneficiaryBank: "",
  swift: "",
  intermediaryBank: "",
  intermediarySwift: "",
  intermediaryAccount: "",
};

export type FormState = Omit<InvoicingDetailsRequest, "paymentDetails"> & {
  payments: Record<Currency, PaymentForm>;
};

export function toFormState(details: InvoicingDetailsResponse): FormState {
  const payments = { UAH: emptyPayment, USD: emptyPayment, EUR: emptyPayment };
  for (const { currency, ...rest } of details.paymentDetails) {
    payments[currency] = rest;
  }

  return {
    sellerNameUk: details.sellerNameUk,
    sellerNameEn: details.sellerNameEn,
    rnokpp: details.rnokpp,
    addressUk: details.addressUk,
    addressEn: details.addressEn,
    acceptanceClauseEn: details.acceptanceClauseEn,
    acceptanceClauseUk: details.acceptanceClauseUk,
    feesClauseEn: details.feesClauseEn,
    feesClauseUk: details.feesClauseUk,
    taxStatusClauseEn: details.taxStatusClauseEn,
    taxStatusClauseUk: details.taxStatusClauseUk,
    payments,
  };
}

export const isBlank = (payment: PaymentForm) => Object.values(payment).every((value) => value.trim() === "");

// A currency with nothing typed is not sent, so the index an error names is the index in this list.
export function sentCurrencies(form: FormState): Currency[] {
  return currencies.filter((currency) => !isBlank(form.payments[currency]));
}

export function toRequest(form: FormState): InvoicingDetailsRequest {
  const { payments, ...rest } = form;

  return {
    ...rest,
    paymentDetails: sentCurrencies(form).map((currency) => ({ currency, ...payments[currency] })),
  };
}

export type ErrorKey =
  | "rnokpp"
  | "iban"
  | "swift"
  | "beneficiaryBank"
  | "required"
  | "tooLong"
  | "controlChar"
  | "duplicateCurrency";

// The api answers in English; the messages it can send for a field are a closed set, so each maps to a
// translated one and anything unforeseen is shown as it came.
export function errorKey(field: string, message: string): ErrorKey | null {
  if (message.includes("exceed")) {
    return "tooLong";
  }

  if (message.includes("control character")) {
    return "controlChar";
  }

  if (message.includes("must not repeat")) {
    return "duplicateCurrency";
  }

  if (field === "rnokpp" || field === "iban" || field === "swift") {
    return field;
  }

  return message.includes("required") ? (field === "beneficiaryBank" ? "beneficiaryBank" : "required") : null;
}
