import { getTranslations } from "next-intl/server";
import { SettingsTabs } from "@/features/settings";

export default async function SettingsPage() {
  const t = await getTranslations("settings");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <SettingsTabs />
    </section>
  );
}
