"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { createQueryClient } from "./queryClient";

let browserQueryClient: QueryClient | undefined;
let navigateBrowser: (path: string) => void = () => {};

function getQueryClient() {
  if (typeof window === "undefined") {
    return createQueryClient(() => {});
  }

  browserQueryClient ??= createQueryClient((path) => navigateBrowser(path));

  return browserQueryClient;
}

export function QueryProvider({ children }: { children: ReactNode }) {
  const router = useRouter();

  useEffect(() => {
    navigateBrowser = (path) => router.replace(path);
  }, [router]);

  return <QueryClientProvider client={getQueryClient()}>{children}</QueryClientProvider>;
}
