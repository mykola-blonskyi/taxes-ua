"use client";

import { useLocale, useTranslations } from "next-intl";
import { FieldWrapper } from "@/shared/ui/fields";
import { monthName, type PeriodValue } from "../period";

const quarters = [1, 2, 3, 4];
const months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

export function PeriodSelect({
  id,
  value,
  onChange,
  hint,
  errors,
}: {
  id: string;
  value: PeriodValue;
  onChange: (value: PeriodValue) => void;
  hint?: string;
  errors?: string[];
}) {
  const t = useTranslations("payments");
  const tForm = useTranslations("payments.form");
  const locale = useLocale();

  return (
    <FieldWrapper label={tForm("period")} htmlFor={id} hint={hint} errors={errors}>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value as PeriodValue)}
        className="w-full min-w-24 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        <optgroup label={tForm("quarters")}>
          {quarters.map((quarter) => (
            <option key={quarter} value={`q${quarter}`}>
              {t("quarter", { quarter })}
            </option>
          ))}
        </optgroup>
        <optgroup label={tForm("months")}>
          {months.map((month) => (
            <option key={month} value={`m${month}`}>
              {monthName(month, locale)}
            </option>
          ))}
        </optgroup>
      </select>
    </FieldWrapper>
  );
}
