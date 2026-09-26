"use client";

import { useTranslations } from "next-intl";
import { Tabs } from "radix-ui";
import { FopSettingsForm } from "./FopSettingsForm";
import { TaxYearTable } from "./TaxYearTable";

export function SettingsTabs() {
  const t = useTranslations("settings");

  return (
    <Tabs.Root defaultValue="fop" className="flex min-w-0 flex-col gap-4">
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
      </Tabs.List>
      <Tabs.Content value="fop">
        <FopSettingsForm />
      </Tabs.Content>
      <Tabs.Content value="taxYears" className="min-w-0">
        <TaxYearTable />
      </Tabs.Content>
    </Tabs.Root>
  );
}
