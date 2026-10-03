import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import type { AuditedEntity } from "@/data/audit/useAuditLog";
import { HistoryPanel } from "@/features/audit";

const validEntities: readonly AuditedEntity[] = [
  "Transaction",
  "BudgetPayment",
  "Settings",
  "InvoicingDetails",
  "TaxYearConfig",
  "Client",
  "Invoice",
  "DeclarationDetails",
  "DeclarationFiling",
];

function parseEntity(value: string | string[] | undefined): AuditedEntity | undefined {
  return typeof value === "string" && (validEntities as readonly string[]).includes(value)
    ? (value as AuditedEntity)
    : undefined;
}

function parseId(value: string | string[] | undefined): string | undefined {
  return typeof value === "string" ? value : undefined;
}

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("audit");

  return { title: t("title") };
}

export default async function HistoryPage({
  searchParams,
}: {
  searchParams: Promise<{ entity?: string | string[]; id?: string | string[] }>;
}) {
  const t = await getTranslations("audit");
  const params = await searchParams;
  const entity = parseEntity(params.entity);
  const id = parseId(params.id);

  return (
    <section className="flex flex-col gap-4">
      <div className="flex min-w-0 flex-col gap-1">
        <h2 className="text-lg font-semibold md:text-xl">{entity ? t("historyTitle") : t("title")}</h2>
        {entity ? (
          <p className="min-w-0 break-words text-sm text-muted-foreground">
            {t("subtitle", { entity: t(`entities.${entity}`) })}
          </p>
        ) : null}
      </div>

      {entity ? (
        <Link href="/history" className="text-sm text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">
          {t("viewFullLog")}
        </Link>
      ) : null}

      <HistoryPanel entity={entity} id={id} />
    </section>
  );
}
