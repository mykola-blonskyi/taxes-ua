import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components, paths } from "@/data/api/schema";

export type RestoreResponse = components["schemas"]["RestoreResponse"];
export type ImportResponse = components["schemas"]["ImportResponse"];

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

async function readJsonText(file: File) {
  if (file.size > maxBackupBytes) {
    throw new TooLargeError();
  }

  const text = await file.text();

  try {
    JSON.parse(text);
  } catch {
    throw new NotJsonError();
  }

  return text;
}

// The file's own text is sent, not a re-serialized parse: JSON.parse would pass every amount through
// a double, and the api reads money from the digits as written.
export function useImportPrototype() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ file, dryRun }: { file: File; dryRun: boolean }) => {
      const text = await readJsonText(file);
      const { data } = await api.POST("/api/import/prototype", {
        params: { query: { dryRun } },
        body: text,
        bodySerializer: (body) => body,
      });

      return data;
    },
    onSuccess: (_data, { dryRun }) => {
      if (!dryRun) {
        queryClient.invalidateQueries();
      }
    },
  });
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
