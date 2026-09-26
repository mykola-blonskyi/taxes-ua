"use client";

import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { ApiError } from "@/data/api/client";
import { useMe } from "@/data/auth/useMe";

export function AuthGate({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { data, error } = useMe();
  const unauthenticated = error instanceof ApiError && error.status === 401;

  useEffect(() => {
    if (unauthenticated) {
      router.replace("/login");
    }
  }, [router, unauthenticated]);

  return data ? children : null;
}
