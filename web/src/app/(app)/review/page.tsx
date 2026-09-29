import { getTranslations } from "next-intl/server";
import { ReviewScreen } from "@/features/transactions";

export default async function ReviewPage() {
  const t = await getTranslations("transactions.review");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <ReviewScreen />
    </section>
  );
}
