import type { AuditedEntity } from "@/data/audit/useAuditLog";
import { formatDateOnly, formatInstantInKyiv } from "@/shared/lib/dates";
import { formatMinor, formatMoney, formatRate, formatRateE4 } from "@/shared/lib/money";

// The plan's fixed key order per entity; anything the API adds later still renders, appended
// alphabetically, instead of being silently dropped.
const FIELD_ORDER: Record<AuditedEntity, readonly string[]> = {
  Transaction: [
    "valueDate",
    "amountMinor",
    "currency",
    "rateE4",
    "rateDate",
    "rateSource",
    "amountUahKop",
    "kind",
    "nonIncomeReason",
    "clientName",
    "refundsTransactionId",
    "invoiceNumber",
    "description",
    "counterparty",
    "bankTime",
    "externalId",
    "bankAccountId",
    "importBatchId",
    "reviewStatus",
  ],
  BudgetPayment: ["paidOn", "kind", "amountKop", "periodYear", "periodQuarter", "periodMonth", "note", "externalId", "bankAccountId"],
  Settings: [
    "fopRegistrationDate",
    "paymentMode",
    "esvRegistrationMonthPolicy",
    "esvExempt",
    "taxPaymentCountsFromStatutoryDeclarationDate",
    "shiftTaxPaymentFromWeekend",
    "weekendDays",
    "locale",
    "theme",
    "defaultCurrency",
    "group3Since",
    "group3ConfirmedOn",
    "group3ReceiptNumber",
    "dpsFopRegistered",
    "dpsEsvRegistered",
    "dpsAccountsRegistered",
  ],
  InvoicingDetails: [
    "sellerNameUk",
    "sellerNameEn",
    "rnokpp",
    "addressUk",
    "addressEn",
    "acceptanceClauseEn",
    "acceptanceClauseUk",
    "feesClauseEn",
    "feesClauseUk",
    "taxStatusClauseEn",
    "taxStatusClauseUk",
    "currency",
    "iban",
    "beneficiaryBank",
    "swift",
    "intermediaryBank",
    "intermediarySwift",
    "intermediaryAccount",
    "signatureImageBytes",
    "signatureContentType",
    "signatureUpdatedAt",
  ],
  TaxYearConfig: [
    "minWageKop",
    "singleTaxRateBp",
    "militaryLevyRateBp",
    "esvRateBp",
    "excessRateBp",
    "esvMonthlyKop",
    "incomeLimitMinWages",
    "incomeLimitKop",
    "limitWarnThresholdsPct",
    "esvDeadlineDay",
    "declarationDays",
    "taxPaymentDaysAfterDeclaration",
    "advanceRecommendedDay",
    "group3ApplicationDays",
    "holidays",
    "militaryLevyAccountEnd",
    "source",
    "verifiedAt",
  ],
  LimitationSuspension: ["start", "end", "source"],
  Client: ["name", "address", "country", "vatId", "email", "defaultCurrency", "notes"],
  Invoice: [
    "status",
    "numberYear",
    "numberSequence",
    "clientId",
    "issueDate",
    "dueDate",
    "currency",
    "lines",
    "totalMinor",
    "snapshot",
    "signatureImageBytes",
    "signatureContentType",
    "cancelReason",
    "issuedAt",
    "cancelledAt",
  ],
  TreasuryAccount: [
    "kind",
    "manualIban",
    "manualRecipientName",
    "manualRecipientCode",
    "manualUpdatedAt",
    "manualValidUntil",
    "manualEndRemoved",
    "learnedIban",
    "learnedRecipientName",
    "learnedRecipientCode",
    "learnedExternalId",
    "learnedPaidOn",
    "learnedAt",
    "learnedValidUntil",
    "learnedEndRemoved",
    "noticeAt",
  ],
  NotificationChannel: ["kind", "enabled", "linkedAt", "confirmedAt"],
  Backup: ["clients", "transactions", "budgetPayments"],
  DeclarationDetails: ["taxOfficeRegion", "taxOfficeDistrict", "taxOfficeName", "kvedCodes", "address", "fullName", "phone", "reportEmail"],
  DeclarationFiling: ["year", "quarter", "filedOn", "type", "filedIncomeKop"],
};

// Every audited entity, taken from the same record that orders its fields, so none can be left out.
export const auditedEntities = Object.keys(FIELD_ORDER) as AuditedEntity[];

export function auditFieldKeys(entity: AuditedEntity): readonly string[] {
  return FIELD_ORDER[entity];
}

export function orderFields(entity: AuditedEntity, keys: string[]): string[] {
  const order = FIELD_ORDER[entity];
  const known = order.filter((key) => keys.includes(key));
  const unknown = keys.filter((key) => !order.includes(key)).sort((a, b) => a.localeCompare(b));

  return [...known, ...unknown];
}

const weekdayReferenceIndex: Record<string, number> = {
  Sunday: 0,
  Monday: 1,
  Tuesday: 2,
  Wednesday: 3,
  Thursday: 4,
  Friday: 5,
  Saturday: 6,
};

// 2023-01-01 was a Sunday; used only as a UTC anchor for naming weekdays, never as a real date.
function weekdayName(day: string, locale: string): string {
  const index = weekdayReferenceIndex[day];

  if (index === undefined) {
    return day;
  }

  return new Intl.DateTimeFormat(locale, { weekday: "long", timeZone: "UTC" }).format(Date.UTC(2023, 0, 1 + index));
}

const isoDateOnlyPattern = /^\d{4}-\d{2}-\d{2}$/;

export type FieldFormatContext = {
  key: string;
  value: unknown;
  snapshot: Record<string, unknown>;
  locale: string;
  none: string;
  yes: string;
  no: string;
  enumLabel: (key: string, value: string) => string | null;
};

type FieldFormatter = (context: FieldFormatContext) => string;

const enumValue: FieldFormatter = ({ key, value, enumLabel }) => enumLabel(key, String(value)) ?? String(value);

// Formatters keyed by the exact field name. Checked before the suffix table below.
const exactFormatters: Record<string, FieldFormatter> = {
  amountMinor: ({ value, snapshot, locale }) => {
    const currency = typeof snapshot.currency === "string" ? snapshot.currency : "UAH";

    return formatMinor(Number(value), currency, locale);
  },
  totalMinor: ({ value, snapshot, locale }) => {
    const currency = typeof snapshot.currency === "string" ? snapshot.currency : "UAH";

    return formatMinor(Number(value), currency, locale);
  },
  lines: ({ value, snapshot, locale }) => {
    if (!Array.isArray(value)) {
      return String(value);
    }

    const currency = typeof snapshot.currency === "string" ? snapshot.currency : "UAH";

    return value
      .map((line: Record<string, unknown>) => {
        const quantity = new Intl.NumberFormat(locale, { maximumFractionDigits: 3 }).format(
          Number(line.quantityThousandths) / 1000,
        );

        return `${String(line.descriptionEn)}: ${quantity} × ${formatMinor(Number(line.rateMinor), currency, locale)}`;
      })
      .join("; ");
  },
  // The frozen seller, buyer and payment details are an object; the log says only that they were captured.
  snapshot: ({ yes }) => yes,
  clientId: ({ value }) => String(value).slice(0, 8),
  issuedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  cancelledAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  status: enumValue,
  rateE4: ({ value, locale }) => formatRateE4(Number(value), locale),
  verifiedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  signatureUpdatedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  manualUpdatedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  learnedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  linkedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  confirmedAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  noticeAt: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  militaryLevyAccountEnd: ({ value, locale }) => formatDateOnly(String(value), locale),
  holidays: ({ value, locale }) =>
    Array.isArray(value) ? value.map((entry) => formatDateOnly(String(entry), locale)).join(", ") : String(value),
  weekendDays: ({ value, locale }) =>
    Array.isArray(value) ? value.map((entry) => weekdayName(String(entry), locale)).join(", ") : String(value),
  kvedCodes: ({ value }) => (Array.isArray(value) ? value.join(", ") : String(value)),
  refundsTransactionId: ({ value }) => String(value).slice(0, 8),
  bankAccountId: ({ value }) => String(value).slice(0, 8),
  importBatchId: ({ value }) => String(value).slice(0, 8),
  bankTime: ({ value, locale }) => formatInstantInKyiv(String(value), locale),
  reviewStatus: enumValue,
  kind: enumValue,
  paymentMode: enumValue,
  esvRegistrationMonthPolicy: enumValue,
  rateSource: enumValue,
  type: enumValue,
};

// Formatters keyed by field-name suffix, checked when no exact match applies.
const suffixFormatters: readonly (readonly [string, FieldFormatter])[] = [
  ["Kop", ({ value, locale }) => formatMoney(Number(value), locale)],
  ["Bp", ({ value, locale }) => formatRate(Number(value), locale)],
];

export function formatFieldValue(context: FieldFormatContext): string {
  const { key, value, none, yes, no, locale } = context;

  if (value === null || value === undefined) {
    return none;
  }

  const exact = exactFormatters[key];

  if (exact) {
    return exact(context);
  }

  const suffix = suffixFormatters.find(([candidate]) => key.endsWith(candidate));

  if (suffix) {
    return suffix[1](context);
  }

  if (typeof value === "boolean") {
    return value ? yes : no;
  }

  if (typeof value === "string" && isoDateOnlyPattern.test(value)) {
    return formatDateOnly(value, locale);
  }

  return String(value);
}
