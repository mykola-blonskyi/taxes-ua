"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { Tabs } from "radix-ui";
import { ClientsSection } from "./ClientsSection";
import { FopSettingsForm } from "./FopSettingsForm";
import { InvoicingForm } from "./InvoicingForm";
import { MonobankConnectionSection } from "./MonobankConnectionSection";
import { TaxYearTable } from "./TaxYearTable";

export function SettingsTabs() {
  const t = useTranslations("settings");

  return (
    <Tabs.Root defaultValue="fop" className="flex min-w-0 flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Tabs.List className="flex gap-1 border-b">
          <Tabs.Trigger
            value="fop"
            className="px-3 py-2 text-sm text-muted-foreground data-[state=active]:border-b-2 data-[state=active]:border-foreground data-[state=active]:text-foreground"
          >
            {t("tabs.fop")}
          </Tabs.Trigger>
          <Tabs.Trigger
            value="taxYears"
            className="px-3 py-2 text-sm text-muted-foreground data-[state=active]:border-b-2 data-[state=active]:border-foreground data-[state=active]:text-foreground"
          >
            {t("tabs.taxYears")}
          </Tabs.Trigger>
          <Tabs.Trigger
            value="monobank"
            className="px-3 py-2 text-sm text-muted-foreground data-[state=active]:border-b-2 data-[state=active]:border-foreground data-[state=active]:text-foreground"
          >
            {t("tabs.monobank")}
          </Tabs.Trigger>
          <Tabs.Trigger
            value="clients"
            className="px-3 py-2 text-sm text-muted-foreground data-[state=active]:border-b-2 data-[state=active]:border-foreground data-[state=active]:text-foreground"
          >
            {t("tabs.clients")}
          </Tabs.Trigger>
          <Tabs.Trigger
            value="invoicing"
            className="px-3 py-2 text-sm text-muted-foreground data-[state=active]:border-b-2 data-[state=active]:border-foreground data-[state=active]:text-foreground"
          >
            {t("tabs.invoicing")}
          </Tabs.Trigger>
        </Tabs.List>
        <Link href="/history" className="shrink-0 text-sm text-primary underline-offset-4 hover:underline">
          {t("changeLog")}
        </Link>
      </div>
      <Tabs.Content value="fop">
        <FopSettingsForm />
      </Tabs.Content>
      <Tabs.Content value="taxYears" className="min-w-0">
        <TaxYearTable />
      </Tabs.Content>
      <Tabs.Content value="monobank" className="min-w-0">
        <MonobankConnectionSection />
      </Tabs.Content>
      <Tabs.Content value="clients" className="min-w-0">
        <ClientsSection />
      </Tabs.Content>
      <Tabs.Content value="invoicing" className="min-w-0">
        <InvoicingForm />
      </Tabs.Content>
    </Tabs.Root>
  );
}
