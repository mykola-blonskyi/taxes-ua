"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import {
  jarErrorKind,
  useChooseReserveJar,
  useClearReserveJar,
  useLoadJars,
  useRefreshReserveJar,
  useReserveJar,
} from "@/data/monobank/useReserveJar";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField } from "@/shared/ui/fields";

// Which monobank jar holds the tax reserve. The jar's name and balance appear here and on the home screen
// only, to the owner; the list of jars is read from the bank when the owner asks for it.
export function ReserveJarSection({ canRead }: { canRead: boolean }) {
  const t = useTranslations("reserveJar");
  const locale = useLocale();
  const stored = useReserveJar();
  const load = useLoadJars();
  const choose = useChooseReserveJar();
  const refresh = useRefreshReserveJar();
  const clear = useClearReserveJar();
  const [picked, setPicked] = useState("");
  const jar = stored.data ?? null;
  const choices = load.data?.jars;
  const failure = [load, choose, refresh, clear].find((mutation) => mutation.isError)?.error;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    choose.mutate(picked, {
      onSuccess: () => {
        setPicked("");
        load.reset();
      },
    });
  }

  // A stored jar outlives a disconnect or a rejected token, so it can always be seen and removed; asking the
  // bank needs a working token.
  if (!jar && !canRead) {
    return null;
  }

  return (
    <section aria-labelledby="reserve-jar-title" className="flex flex-col gap-2 border-t pt-4">
      <h3 id="reserve-jar-title" className="text-sm font-medium">
        {t("title")}
      </h3>
      <p className="text-xs text-muted-foreground">{t("hint")}</p>

      {jar ? (
        <div className="flex min-w-0 flex-col gap-0.5">
          <p className="min-w-0 break-words text-sm">{t("balance", { title: jar.title, amount: formatMoney(Number(jar.balanceKop), locale) })}</p>
          <p className={jar.stale ? "text-xs text-destructive" : "text-xs text-muted-foreground"}>
            {jar.stale
              ? t("stale", { time: formatInstantInKyiv(jar.fetchedAt, locale) })
              : t("asOf", { time: formatInstantInKyiv(jar.fetchedAt, locale) })}
          </p>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">{t("none")}</p>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {jar ? (
          <>
            {canRead ? (
              <Button type="button" variant="outline" size="sm" disabled={refresh.isPending} onClick={() => refresh.mutate()}>
                {refresh.isPending ? t("refreshing") : t("refresh")}
              </Button>
            ) : null}
            <Button type="button" variant="outline" size="sm" disabled={clear.isPending} onClick={() => clear.mutate()}>
              {t("remove")}
            </Button>
          </>
        ) : null}
        {canRead && !choices ? (
          <Button type="button" variant="outline" size="sm" disabled={load.isPending} onClick={() => load.mutate()}>
            {load.isPending ? t("loading") : jar ? t("change") : t("load")}
          </Button>
        ) : null}
      </div>

      {canRead && choices && choices.length === 0 ? <p className="text-sm text-muted-foreground">{t("noUahJars")}</p> : null}

      {canRead && choices && choices.length > 0 ? (
        <form className="flex flex-col gap-2" onSubmit={submit}>
          <SelectField
            id="reserve-jar-pick"
            label={t("pick")}
            value={picked}
            onChange={setPicked}
            placeholder={t("pickPlaceholder")}
            options={choices.map((choice) => ({
              value: choice.id,
              label: `${choice.title} · ${formatMoney(Number(choice.balanceKop), locale)}`,
            }))}
          />
          <div>
            <Button type="submit" size="sm" disabled={choose.isPending || picked === ""}>
              {choose.isPending ? t("choosing") : t("choose")}
            </Button>
          </div>
        </form>
      ) : null}

      {failure ? (
        <p role="alert" className="min-w-0 break-words text-xs text-destructive">
          {t(`errors.${jarErrorKind(failure)}`)}
        </p>
      ) : null}
    </section>
  );
}
