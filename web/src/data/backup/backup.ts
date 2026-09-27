import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components, paths } from "@/data/api/schema";

export type RestoreResponse = components["schemas"]["RestoreResponse"];

type BackupPath = Extract<keyof paths, "/api/backup">;

// Typed against the generated schema, so a renamed or removed backup endpoint fails the build.
export const backupUrl: BackupPath = "/api/backup";

// The api's own limit (BackupEndpoints.MaxRestoreBytes), checked here so a huge file is refused before
// the browser reads it into memory.
const maxBackupBytes = 8 * 1024 * 1024;

export class TooLargeError extends Error {
  constructor() {
    super("Backup file too large");
    this.name = "TooLargeError";
  }
}

export class NotJsonError extends Error {
  constructor() {
    super("Not a JSON file");
    this.name = "NotJsonError";
  }
}

export function useRestoreBackup() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (file: File) => {
      if (file.size > maxBackupBytes) {
        throw new TooLargeError();
      }

      const text = await file.text();
      let body;

      try {
        // The server is the validator: JSON.parse's `any` return is passed through as-is.
        body = JSON.parse(text);
      } catch {
        throw new NotJsonError();
      }

      const { data } = await api.POST("/api/restore", { body });

      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries();
    },
  });
}
