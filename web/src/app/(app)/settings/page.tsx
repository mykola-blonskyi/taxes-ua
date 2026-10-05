import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { BackupPanel } from "@/features/backup";
import { SettingsTabs } from "@/features/settings";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("settings");

  return { title: t("title") };
}

export default async function SettingsPage({ searchParams }: { searchParams: Promise<{ tab?: string | string[]; confirmEmail?: string | string[] }> }) {
  const t = await getTranslations("settings");
  const { tab, confirmEmail } = await searchParams;
  const initialTab = typeof tab === "string" ? tab : undefined;
  const confirmEmailToken = typeof confirmEmail === "string" ? confirmEmail : undefined;

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <SettingsTabs initialTab={initialTab} confirmEmailToken={confirmEmailToken} />
      <BackupPanel />
    </section>
  );
}
