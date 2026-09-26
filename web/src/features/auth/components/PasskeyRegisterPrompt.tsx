"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/data/auth/useMe";
import { PasskeyButton } from "./PasskeyButton";

export function PasskeyRegisterPrompt() {
  const t = useTranslations("login");
  const { data, isPending } = useMe();

  if (isPending || !data) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2 border-t pt-4">
      <p className="text-sm text-muted-foreground">{t("passkeyRegisterPrompt")}</p>
      <PasskeyButton mode="register" />
    </div>
  );
}
