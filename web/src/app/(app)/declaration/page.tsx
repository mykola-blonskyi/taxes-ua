import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { DeclarationScreen } from "@/features/declaration";

function single(value: string | string[] | undefined): string | undefined {
  return typeof value === "string" ? value : undefined;
}

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("declaration");

  return { title: t("pageTitle") };
}

export default async function DeclarationPage({
  searchParams,
}: {
  searchParams: Promise<{ year?: string | string[]; quarter?: string | string[] }>;
}) {
  const params = await searchParams;

  return <DeclarationScreen year={single(params.year)} quarter={single(params.quarter)} />;
}
