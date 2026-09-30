"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Tabs } from "radix-ui";
import { useTreasuryAccounts } from "@/data/treasury/useTreasuryAccounts";
import { ClientsSection } from "./ClientsSection";
import { DeclarationDetailsForm } from "./DeclarationDetailsForm";
import { FopSettingsForm } from "./FopSettingsForm";
import { InvoicingForm } from "./InvoicingForm";
import { MonobankConnectionSection } from "./MonobankConnectionSection";
import { NotificationsSection } from "./NotificationsSection";
import { TaxYearTable } from "./TaxYearTable";
import { TreasuryAccountsSection } from "./TreasuryAccountsSection";

const tabs = ["fop", "taxYears", "monobank", "clients", "invoicing", "declaration", "treasury", "notifications"] as const;
type Tab = (typeof tabs)[number];

function isTab(value: string | undefined): value is Tab {
  return (tabs as readonly (string | undefined)[]).includes(value);
}

export function SettingsTabs({ initialTab }: { initialTab?: string }) {
  const t = useTranslations("settings");
  const [tab, setTab] = useState<Tab>(isTab(initialTab) ? initialTab : "fop");
  const listRef = useRef<HTMLDivElement>(null);
  const treasuryNotice = useTreasuryAccounts().data?.some((account) => account.notice) ?? false;

  useEffect(() => {
    listRef.current?.querySelector('[data-state="active"]')?.scrollIntoView({ inline: "nearest", block: "nearest" });
  }, [tab]);

  return (
    <Tabs.Root value={tab} onValueChange={(value) => setTab(value as Tab)} className="flex min-w-0 flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Tabs.List ref={listRef} className="flex min-w-0 max-w-full gap-1 overflow-x-auto border-b">
          {tabs.map((name) => (
            <Tabs.Trigger
              key={name}
              value={name}
              className="shrink-0 whitespace-nowrap px-3 py-2 text-sm text-muted-foreground data-[state=active]:border-b-2 data-[state=active]:border-foreground data-[state=active]:text-foreground"
            >
              {t(`tabs.${name}`)}
              {name === "treasury" && treasuryNotice ? (
                <span role="img" aria-label={t("treasury.noticeDot")} className="ml-1.5 inline-block size-2 rounded-full bg-amber-500" />
              ) : null}
            </Tabs.Trigger>
          ))}
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
      <Tabs.Content value="declaration" className="min-w-0">
        <DeclarationDetailsForm />
      </Tabs.Content>
      <Tabs.Content value="treasury" className="min-w-0">
        <TreasuryAccountsSection />
      </Tabs.Content>
      <Tabs.Content value="notifications" className="min-w-0">
        <NotificationsSection />
      </Tabs.Content>
    </Tabs.Root>
  );
}
