"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { paths } from "@/data/api/schema";

type DeadlinesPath = Extract<keyof paths, "/api/calendar/deadlines.ics">;

// Typed against the generated schema, so a renamed or removed download endpoint fails the build.
export const calendarDownloadUrl: DeadlinesPath = "/api/calendar/deadlines.ics";

export const calendarFeedQueryKey = ["calendar", "feed"] as const;

export function useCalendarFeed() {
  return useQuery({
    queryKey: calendarFeedQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/calendar/feed");

      return data;
    },
  });
}

export function useRotateCalendarFeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/calendar/feed/rotate");

      return data;
    },
    onSuccess: (data) => queryClient.setQueryData(calendarFeedQueryKey, data),
  });
}
