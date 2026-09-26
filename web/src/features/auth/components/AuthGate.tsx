"use client";

import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useMe } from "@/data/auth/useMe";
import { Button } from "@/shared/ui/button";

export function AuthGate({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { data, error, isPending, isFetching, refetch } = useMe();
  const t = useTranslations("authGate");
  const unauthenticated = error instanceof ApiError && error.status === 401;

  useEffect(() => {
    if (unauthenticated) {
      router.replace("/login");
    }
  }, [router, unauthenticated]);

  if (isPending || unauthenticated) {
    return null;
  }

  if (!data) {
    console.error("AuthGate: /api/auth/me did not return a session", error);

    return (
      <div className="flex min-h-full flex-1 flex-col items-center justify-center gap-4 px-4 py-8 text-center">
        <p className="text-sm text-muted-foreground">{t("error")}</p>
        <Button variant="outline" disabled={isFetching} onClick={() => void refetch()}>
          {t("retry")}
        </Button>
      </div>
    );
  }

  return children;
}
