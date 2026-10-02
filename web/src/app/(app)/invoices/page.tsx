import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { InvoicesScreen } from "@/features/invoices";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("invoices");

  return { title: t("title") };
}

export default async function InvoicesPage() {
  const t = await getTranslations("invoices");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <InvoicesScreen />
    </section>
  );
}
