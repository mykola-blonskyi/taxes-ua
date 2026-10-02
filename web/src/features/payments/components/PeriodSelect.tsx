"use client";

import { useLocale, useTranslations } from "next-intl";
import type { PaymentKind } from "@/data/payments/usePayments";
import { useIsOutsideGroup3 } from "@/data/periods/usePeriods";
import { FieldWrapper, inputClasses } from "@/shared/ui/fields";
import { fromPeriodValue, monthName, type PeriodValue } from "../period";

const quarters = [1, 2, 3, 4];
const months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

export function PeriodSelect({
  id,
  year,
  kind,
  value,
  onChange,
  hint,
  errors,
}: {
  id: string;
  year: number;
  kind: PaymentKind | null;
  value: PeriodValue;
  onChange: (value: PeriodValue) => void;
  hint?: string;
  errors?: string[];
}) {
  const t = useTranslations("payments");
  const tForm = useTranslations("payments.form");
  const locale = useLocale();
  const { periodQuarter, periodMonth } = fromPeriodValue(value);
  const outsideGroup3 = useIsOutsideGroup3(year, periodQuarter ?? Math.ceil(periodMonth! / 3), kind);

  return (
    <FieldWrapper label={tForm("period")} htmlFor={id} hint={hint} errors={errors}>
      {(control) => (
        <>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value as PeriodValue)}
        className={inputClasses}
        {...control}
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
      {outsideGroup3 ? (
        <p className="text-xs text-amber-700 dark:text-amber-400">{tForm("outsideGroup3")}</p>
      ) : null}
        </>
      )}
    </FieldWrapper>
  );
}
