"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useInvoices, type InvoiceStatus } from "@/data/invoices/useInvoices";
import { currentYearInKyiv, formatDateOnly } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField } from "@/shared/ui/fields";
import { InvoiceStatusBadge } from "./InvoiceStatusBadge";

const statuses: readonly InvoiceStatus[] = ["Draft", "Issued", "Cancelled"];

export function InvoiceList({ onOpen, onNew }: { onOpen: (id: string) => void; onNew: () => void }) {
  const t = useTranslations("invoices");
  const locale = useLocale();
  const [status, setStatus] = useState("");
  const [year, setYear] = useState("");
  const { data, isLoading, isError } = useInvoices({
    status: status === "" ? undefined : (status as InvoiceStatus),
    year: year === "" ? undefined : Number(year),
  });

  const thisYear = currentYearInKyiv();
  const years = Array.from({ length: 7 }, (_, index) => thisYear + 1 - index);

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Button type="button" onClick={onNew}>
          {t("new")}
        </Button>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:max-w-md">
        <SelectField
          id="invoice-filter-status"
          label={t("filters.status")}
          value={status}
          onChange={setStatus}
          options={[
            { value: "", label: t("filters.allStatuses") },
            ...statuses.map((value) => ({ value, label: t(`status.${value}`) })),
          ]}
        />
        <SelectField
          id="invoice-filter-year"
          label={t("filters.year")}
          value={year}
          onChange={setYear}
          options={[
            { value: "", label: t("filters.allYears") },
            ...years.map((value) => ({ value: String(value), label: String(value) })),
          ]}
        />
      </div>

      {isLoading ? <p className="text-sm text-muted-foreground">{t("loading")}</p> : null}
      {isError ? <p className="text-sm text-destructive">{t("loadFailed")}</p> : null}

      {data && data.length === 0 ? <p className="text-sm text-muted-foreground">{t("empty")}</p> : null}

      {data && data.length > 0 ? (
        <ul className="flex flex-col gap-2">
          {data.map((invoice) => (
            <li key={invoice.id}>
              <button
                type="button"
                onClick={() => onOpen(invoice.id)}
                className="flex w-full min-w-0 flex-col gap-1 rounded-lg border p-3 text-left hover:bg-muted focus-visible:border-ring focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
              >
                <span className="flex min-w-0 flex-wrap items-center justify-between gap-2">
                  <span className="break-words font-medium">{invoice.number ?? t("list.draftNumber")}</span>
                  <InvoiceStatusBadge standing={invoice.standing} />
                </span>
                <span className="break-words text-sm">{invoice.clientName}</span>
                <span className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                  <span className="text-xs text-muted-foreground">
                    {t("list.dates", {
                      issueDate: formatDateOnly(invoice.issueDate, locale),
                      dueDate: formatDateOnly(invoice.dueDate, locale),
                    })}
                  </span>
                  <span className="font-semibold tabular-nums">
                    {formatAmount(Number(invoice.totalMinor), invoice.currency, locale)}
                  </span>
                </span>
                {invoice.dueMinor !== null && invoice.standing !== "Paid" ? (
                  <span className="text-xs text-muted-foreground">
                    {t("list.due", { amount: formatAmount(Number(invoice.dueMinor), invoice.currency, locale) })}
                  </span>
                ) : null}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
