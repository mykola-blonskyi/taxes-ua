"use client";

import { LogOut } from "lucide-react";
import { useTranslations } from "next-intl";
import { useSignOut } from "@/data/auth/useSignOut";
import { Button } from "@/shared/ui/button";

export function SignOutButton() {
  const t = useTranslations("account");
  const signOut = useSignOut();

  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={t("signOut")}
      disabled={signOut.isPending}
      onClick={() => signOut.mutate()}
    >
      <LogOut />
    </Button>
  );
}
