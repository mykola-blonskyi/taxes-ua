"use client";

import { useLocale, useTranslations } from "next-intl";
import { useAuditLog, type AuditedEntity, type AuditEntryResponse } from "@/data/audit/useAuditLog";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { formatFieldValue, orderFields } from "../fields";

// Field and kind names come from the API's snapshot data, not a fixed literal set known at compile
// time, so next-intl's strict message-key typing cannot check them. This narrow cast is the seam:
// callers still go through `has` before rendering an unknown key.
type DynamicTranslator = { (key: string): string; has(key: string): boolean };

function asDynamic(t: object): DynamicTranslator {
  return t as unknown as DynamicTranslator;
}

export function HistoryPanel({ entity, id }: { entity?: AuditedEntity; id?: string }) {
  const t = useTranslations("audit");
  const { data, isLoading, isError } = useAuditLog({ entity, id });

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (isError) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  if (!data || data.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("empty")}</p>;
  }

  return (
    <ul className="flex flex-col gap-2">
      {data.map((entry) => (
        <HistoryEntryCard key={entry.id} entry={entry} />
      ))}
    </ul>
  );
}

function HistoryEntryCard({ entry }: { entry: AuditEntryResponse }) {
  const t = useTranslations("audit");
  const tTransactionKinds = useTranslations("transactions.kinds");
  const tPaymentKinds = useTranslations("payments.kinds");
  const tFop = useTranslations("settings.fop");
  const tDeclarationTypes = useTranslations("declaration.types");
  const locale = useLocale();

  const dynamicTransactionKinds = asDynamic(tTransactionKinds);
  const dynamicPaymentKinds = asDynamic(tPaymentKinds);
  const dynamicFop = asDynamic(tFop);
  const dynamicDeclarationTypes = asDynamic(tDeclarationTypes);
  const dynamicT = asDynamic(t);

  function enumLabel(key: string, value: string): string | null {
    const [translator, messageKey] =
      key === "kind"
        ? [entry.entity === "Transaction" ? dynamicTransactionKinds : dynamicPaymentKinds, value]
        : key === "rateSource" || key === "reviewStatus" || key === "status"
          ? [dynamicT, `values.${key}.${value}`]
          : key === "type"
            ? [dynamicDeclarationTypes, value]
            : [dynamicFop, `${key}${value}`];

    return translator.has(messageKey) ? translator(messageKey) : null;
  }

  function fieldLabel(key: string): string {
    const messageKey = `fields.${key}`;

    return dynamicT.has(messageKey) ? dynamicT(messageKey) : key;
  }

  function format(key: string, value: unknown, snapshot: Record<string, unknown>): string {
    return formatFieldValue({
      key,
      value,
      snapshot,
      locale,
      none: t("values.none"),
      yes: t("values.yes"),
      no: t("values.no"),
      enumLabel,
    });
  }

  const actionLabel = t(`actions.${entry.action}`);
  const entityLabel = t(`entities.${entry.entity}`);
  const time = formatInstantInKyiv(entry.at, locale);

  return (
    <li className="flex min-w-0 flex-col gap-1.5 rounded-lg border p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 flex-wrap items-baseline gap-2">
          <span className="text-sm font-medium">{actionLabel}</span>
          <span className="text-sm text-muted-foreground">{entityLabel}</span>
        </div>
        <span className="shrink-0 text-xs text-muted-foreground">{time}</span>
      </div>

      {(entry.action === "Create" || entry.action === "Restore") && entry.after ? (
        <SnapshotFields snapshot={entry.after} fieldLabel={fieldLabel} format={format} entity={entry.entity} />
      ) : null}

      {entry.action === "Delete" && entry.before ? (
        <SnapshotFields snapshot={entry.before} fieldLabel={fieldLabel} format={format} entity={entry.entity} />
      ) : null}

      {entry.action === "Update" && entry.before && entry.after ? (
        <UpdateFields
          before={entry.before}
          after={entry.after}
          fieldLabel={fieldLabel}
          format={format}
          entity={entry.entity}
        />
      ) : null}
    </li>
  );
}

function SnapshotFields({
  snapshot,
  entity,
  fieldLabel,
  format,
}: {
  snapshot: Record<string, unknown>;
  entity: AuditedEntity;
  fieldLabel: (key: string) => string;
  format: (key: string, value: unknown, snapshot: Record<string, unknown>) => string;
}) {
  const keys = orderFields(
    entity,
    Object.keys(snapshot).filter((key) => snapshot[key] !== null),
  );

  if (keys.length === 0) {
    return null;
  }

  return (
    <dl className="flex flex-col gap-1">
      {keys.map((key) => (
        <div key={key} className="flex min-w-0 flex-wrap gap-1 text-xs">
          <dt className="min-w-0 shrink-0 break-words text-muted-foreground">{fieldLabel(key)}:</dt>
          <dd className="min-w-0 break-words">{format(key, snapshot[key], snapshot)}</dd>
        </div>
      ))}
    </dl>
  );
}

function UpdateFields({
  before,
  after,
  entity,
  fieldLabel,
  format,
}: {
  before: Record<string, unknown>;
  after: Record<string, unknown>;
  entity: AuditedEntity;
  fieldLabel: (key: string) => string;
  format: (key: string, value: unknown, snapshot: Record<string, unknown>) => string;
}) {
  const allKeys = new Set([...Object.keys(before), ...Object.keys(after)]);
  const changed = [...allKeys].filter((key) => JSON.stringify(before[key]) !== JSON.stringify(after[key]));
  const keys = orderFields(entity, changed);

  if (keys.length === 0) {
    return null;
  }

  return (
    <dl className="flex flex-col gap-1">
      {keys.map((key) => (
        <div key={key} className="flex min-w-0 flex-wrap items-baseline gap-1 text-xs">
          <dt className="min-w-0 shrink-0 break-words text-muted-foreground">{fieldLabel(key)}:</dt>
          <dd className="min-w-0 break-words">
            <span className="text-muted-foreground line-through">{format(key, before[key], before)}</span>
            {" → "}
            <span>{format(key, after[key], after)}</span>
          </dd>
        </div>
      ))}
    </dl>
  );
}
