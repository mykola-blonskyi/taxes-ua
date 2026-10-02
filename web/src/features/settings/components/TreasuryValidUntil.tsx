"use client";

import { useState, type FormEvent } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useSetTreasuryValidUntil, type TreasuryAccount } from "@/data/treasury/useTreasuryAccounts";
import { formatDateOnly, todayInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { TextField } from "@/shared/ui/fields";

// The military-levy accounts in use since 2026-07-01 are temporary: Law 4908-IX sets them for 1 July to
// 31 December 2026. It is offered as a one-tap value for that kind, never applied on its own (Rule 16).
const temporaryLevyAccountsEnd = "2026-12-31";

export function TreasuryValidUntil({ account }: { account: TreasuryAccount }) {
  const t = useTranslations("settings.treasury.validUntil");
  const locale = useLocale();
  const save = useSetTreasuryValidUntil();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(account.validUntil ?? "");

  if (account.source === "None") {
    return null;
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate({ kind: account.kind, validUntil: value === "" ? null : value }, { onSuccess: () => setEditing(false) });
  }

  if (!editing) {
    return (
      <div className="flex min-w-0 flex-col gap-2">
        <p className="text-sm">
          <span className="text-muted-foreground">{t("label")}: </span>
          {account.validUntil ? formatDateOnly(account.validUntil, locale) : t("none")}
        </p>
        <div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              setValue(account.validUntil ?? "");
              setEditing(true);
            }}
          >
            {t("change")}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={submit} className="flex min-w-0 flex-col gap-3" noValidate>
      <TextField
        id={`treasury-${account.kind}-valid-until`}
        label={t("label")}
        hint={t("hint")}
        type="date"
        value={value}
        onChange={setValue}
        errors={save.isError ? [t("saveFailed")] : undefined}
      />
      {account.kind === "MilitaryLevy" && account.validUntil === null && todayInKyiv() <= temporaryLevyAccountsEnd ? (
        <div>
          <Button type="button" variant="outline" size="sm" onClick={() => setValue(temporaryLevyAccountsEnd)}>
            {t("temporaryLevy")}
          </Button>
        </div>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="sm" disabled={save.isPending}>
          {t("save")}
        </Button>
        {value !== "" ? (
          <Button type="button" variant="outline" size="sm" onClick={() => setValue("")}>
            {t("clear")}
          </Button>
        ) : null}
        <Button type="button" variant="outline" size="sm" onClick={() => setEditing(false)}>
          {t("cancel")}
        </Button>
      </div>
    </form>
  );
}
