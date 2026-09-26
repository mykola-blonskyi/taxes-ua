"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { api } from "@/data/api/client";
import { meQueryKey } from "./useMe";

export function useSignOut() {
  const router = useRouter();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => api.POST("/api/auth/logout"),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: meQueryKey });
      router.replace("/login");
    },
  });
}
