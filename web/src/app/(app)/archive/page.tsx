import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ArchiveScreen } from "@/features/archive";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("archive");

  return { title: t("title") };
}

export default async function ArchivePage({ searchParams }: { searchParams: Promise<{ year?: string | string[] }> }) {
  const t = await getTranslations("archive");
  const { year } = await searchParams;

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <ArchiveScreen year={typeof year === "string" ? year : undefined} />
    </section>
  );
}
