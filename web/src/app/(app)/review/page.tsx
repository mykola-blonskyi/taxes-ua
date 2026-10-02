import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PaymentCandidates } from "@/features/payments";
import { ReviewScreen } from "@/features/transactions";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("transactions.review");

  return { title: t("title") };
}

export default async function ReviewPage() {
  const t = await getTranslations("transactions.review");
  const tCandidates = await getTranslations("payments.candidates");

  return (
    <section className="flex flex-col gap-6">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <section className="flex flex-col gap-4">
        <h3 className="text-base font-semibold">{t("receiptsTitle")}</h3>
        <ReviewScreen />
      </section>
      <section className="flex flex-col gap-4">
        <h3 className="text-base font-semibold">{tCandidates("title")}</h3>
        <PaymentCandidates />
      </section>
    </section>
  );
}
