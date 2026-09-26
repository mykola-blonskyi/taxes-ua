"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useTaxYears } from "@/data/tax-years/useTaxYears";

export function TaxYearVerificationWarning() {
  const t = useTranslations("settings.verificationWarning");
  const { data } = useTaxYears();

  if (!data) {
    return null;
  }

  const currentYear = new Date().getFullYear();
  const yearsNeedingVerification = new Set(
    data.filter((taxYear) => taxYear.verifiedAt === null).map((taxYear) => Number(taxYear.year)),
  );

  if (!data.some((taxYear) => Number(taxYear.year) === currentYear)) {
    yearsNeedingVerification.add(currentYear);
  }

  const sortedYears = [...yearsNeedingVerification].sort((a, b) => a - b);

  if (sortedYears.length === 0) {
    return null;
  }

  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
      <h3 className="text-sm font-semibold text-destructive">{t("title")}</h3>
      <p className="text-sm text-muted-foreground">{t("message", { years: sortedYears.join(", ") })}</p>
      <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
        {t("cta")}
      </Link>
    </section>
  );
}
