import { getTranslations } from "next-intl/server";

export async function Disclaimer() {
  const t = await getTranslations();

  return (
    <p className="border-t px-4 py-3 text-xs text-muted-foreground md:px-8">{t("disclaimer")}</p>
  );
}
