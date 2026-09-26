"use client";

import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { Button } from "@/shared/ui/button";
import {
  PasskeyCancelledError,
  type PasskeyMode,
  useIsPasskeySupported,
  usePasskeyCeremony,
} from "../hooks/usePasskeyCeremony";

export function PasskeyButton({ mode }: { mode: PasskeyMode }) {
  const t = useTranslations("login");
  const supported = useIsPasskeySupported();
  const ceremony = usePasskeyCeremony(mode);

  if (!supported) {
    return null;
  }

  const isSignIn = mode === "signIn";
  const label = isSignIn ? t("passkeySignIn") : t("passkeyRegister");

  return (
    <div className="flex flex-col gap-2">
      <Button
        variant="outline"
        size={isSignIn ? "lg" : "default"}
        className={isSignIn ? "w-full" : undefined}
        aria-label={label}
        disabled={ceremony.isPending}
        onClick={() => ceremony.mutate()}
      >
        {label}
      </Button>
      {ceremony.error instanceof PasskeyCancelledError && (
        <p className="text-sm text-muted-foreground">{t("passkeyCancelled")}</p>
      )}
      {ceremony.error instanceof ApiError && (
        <p className="text-sm text-destructive">
          {ceremony.error.status === 403 ? t("passkeyForbidden") : t("passkeyError")}
        </p>
      )}
      {!isSignIn && ceremony.isSuccess && (
        <p className="text-sm text-muted-foreground">{t("passkeyRegisterSuccess")}</p>
      )}
    </div>
  );
}
