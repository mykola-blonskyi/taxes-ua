import { getTranslations } from "next-intl/server";
import { GoogleSignInButton, PasskeyButton, PasskeyRegisterPrompt } from "@/features/auth";
import { AppHeader } from "@/shared/shell/AppHeader";
import { Disclaimer } from "@/shared/shell/Disclaimer";

export default async function LoginPage() {
  const t = await getTranslations("login");

  return (
    <div className="flex min-h-full flex-1 flex-col">
      <AppHeader />
      <main className="flex flex-1 items-center justify-center px-4 py-8">
        <div className="flex w-full max-w-sm flex-col gap-4">
          <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
          <GoogleSignInButton />
          <PasskeyButton mode="signIn" />
          <p className="text-sm text-muted-foreground">{t("ownerOnly")}</p>
          <PasskeyRegisterPrompt />
        </div>
      </main>
      <Disclaimer />
    </div>
  );
}
