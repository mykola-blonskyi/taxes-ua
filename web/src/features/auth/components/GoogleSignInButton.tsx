import { getTranslations } from "next-intl/server";
import { Button } from "@/shared/ui/button";

export async function GoogleSignInButton() {
  const t = await getTranslations("login");

  return (
    <Button asChild size="lg" className="w-full">
      <a href="/api/auth/login/google?returnUrl=/">{t("google")}</a>
    </Button>
  );
}
