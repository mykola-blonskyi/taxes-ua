"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type NotificationChannel = components["schemas"]["NotificationChannelResponse"];
export type DeliveryFailure = NonNullable<NotificationChannel["lastFailure"]>;
export type TelegramConnect = components["schemas"]["TelegramConnectResponse"];

export const notificationChannelsQueryKey = ["notifications", "channels"] as const;

// While the owner is away in Telegram pressing Start, the channel is read every few seconds so the
// tab shows it linked without a reload.
const linkingPollMs = 3_000;

export function useNotificationChannels({ awaitingLink }: { awaitingLink: boolean }) {
  return useQuery({
    queryKey: notificationChannelsQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/notifications/channels");

      return data;
    },
    refetchInterval: (query) => (awaitingLink && !query.state.data?.some((channel) => channel.linked) ? linkingPollMs : false),
  });
}

export function useConnectTelegram() {
  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/notifications/channels/telegram/connect");

      return data;
    },
  });
}

export function useToggleTelegram() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (enabled: boolean) => {
      const { data } = await api.PUT("/api/notifications/channels/telegram", { body: { enabled } });

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationChannelsQueryKey }),
  });
}

export function useTestTelegram() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/notifications/channels/telegram/test");

      return data;
    },
    // A failed delivery is written on the channel, so the outcome is read back either way.
    onSettled: () => queryClient.invalidateQueries({ queryKey: notificationChannelsQueryKey }),
  });
}

export function useDisconnectTelegram() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      await api.DELETE("/api/notifications/channels/telegram");
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationChannelsQueryKey }),
  });
}
