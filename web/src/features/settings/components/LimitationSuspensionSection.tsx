"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { problemOf } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import { useMe } from "@/data/auth/useMe";
import {
  useLimitationSuspension,
  useSaveLimitationSuspension,
  type LimitationSuspension,
} from "@/data/tax-years/useLimitationSuspension";
import { Button } from "@/shared/ui/button";
import { TextField } from "@/shared/ui/fields";

export function LimitationSuspensionSection() {
  const t = useTranslations("settings");
  const query = useLimitationSuspension();
  const meQuery = useMe();

  if (!query.data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  if (!meQuery.data) {
    return <LoadState query={meQuery} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return <SuspensionForm suspension={query.data} canEdit={meQuery.data.isAdmin} />;
}

function SuspensionForm({ suspension, canEdit }: { suspension: LimitationSuspension; canEdit: boolean }) {
  const t = useTranslations("settings");
  const tSuspension = useTranslations("settings.suspension");
  const apiText = useApiErrorText();
  const save = useSaveLimitationSuspension();
  const [start, setStart] = useState(suspension.start);
  const [end, setEnd] = useState(suspension.end ?? "");
  const [source, setSource] = useState(suspension.source);

  const fieldErrors = apiText.fieldTexts(save.error);
  const hasFieldErrors = Object.keys(problemOf(save.error)?.fieldCodes ?? {}).length > 0;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    save.mutate({ start, end: end === "" ? null : end, source });
  }

  return (
    <section aria-labelledby="limitation-suspension-title" className="flex min-w-0 flex-col gap-3 border-t pt-4">
      <div className="flex flex-col gap-1">
        <h3 id="limitation-suspension-title" className="text-sm font-medium">
          {tSuspension("title")}
        </h3>
        <p className="text-xs text-muted-foreground">{tSuspension("hint")}</p>
      </div>
      <form onSubmit={submit} className="flex max-w-md flex-col gap-3">
        <TextField
          type="date"
          id="suspension-start"
          label={tSuspension("start")}
          value={start}
          onChange={setStart}
          errors={fieldErrors.start}
          disabled={!canEdit}
        />
        <TextField
          type="date"
          id="suspension-end"
          label={tSuspension("end")}
          hint={tSuspension("endHint")}
          value={end}
          onChange={setEnd}
          errors={fieldErrors.end}
          disabled={!canEdit}
        />
        <TextField
          id="suspension-source"
          label={tSuspension("source")}
          value={source}
          onChange={setSource}
          errors={fieldErrors.source}
          disabled={!canEdit}
        />
        {canEdit ? (
          <Button type="submit" size="sm" className="w-fit" disabled={save.isPending}>
            {save.isPending ? t("saving") : t("save")}
          </Button>
        ) : (
          <p className="text-xs text-muted-foreground">{tSuspension("readOnly")}</p>
        )}
        {save.error && !hasFieldErrors ? (
          <p className="text-xs text-destructive">{apiText.withReason(t("saveFailed"), save.error)}</p>
        ) : null}
      </form>
    </section>
  );
}
