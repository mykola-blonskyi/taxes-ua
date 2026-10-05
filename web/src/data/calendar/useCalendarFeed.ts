"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components, paths } from "@/data/api/schema";

type DeadlinesPath = Extract<keyof paths, "/api/calendar/deadlines.ics">;

export type CalendarFeed = components["schemas"]["CalendarFeedResponse"];

// Typed against the generated schema, so a renamed or removed download endpoint fails the build.
export const calendarDownloadUrl: DeadlinesPath = "/api/calendar/deadlines.ics";

export const calendarFeedQueryKey = ["calendar", "feed"] as const;

// Says only whether a link exists and when it was made: the server keeps a hash, never the link.
export function useCalendarFeed() {
  return useQuery({
    queryKey: calendarFeedQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/calendar/feed");

      return data;
    },
  });
}

// The only answer that carries the link. It stays in the mutation, never in the query cache, so it is gone
// once the screen is left.
export function useRotateCalendarFeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/calendar/feed/rotate");

      return data;
    },
    onSuccess: (data) => {
      if (data) {
        queryClient.setQueryData<CalendarFeed>(calendarFeedQueryKey, { createdAt: data.createdAt });
      }
    },
  });
}
