import { useSyncExternalStore } from "react";
import { useMutation, useQueryClient, type QueryClient } from "@tanstack/react-query";
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

let replacements = 0;
const replacementListeners = new Set<() => void>();

function subscribeToReplacements(listener: () => void) {
  replacementListeners.add(listener);

  return () => {
    replacementListeners.delete(listener);
  };
}

// Counts the restores and imports that replaced the owner's data. A form that copies server data into its
// own state keys itself by it, so it re-seeds after a replacement and keeps an edit through any other refetch.
export function useDataReplacements() {
  return useSyncExternalStore(
    subscribeToReplacements,
    () => replacements,
    () => 0,
  );
}

async function refreshAfterReplacement(queryClient: QueryClient) {
  await queryClient.invalidateQueries();
  replacements += 1;
  replacementListeners.forEach((listener) => listener());
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
    onSuccess: async (_data, { dryRun }) => {
      if (!dryRun) {
        await refreshAfterReplacement(queryClient);
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
    onSuccess: () => refreshAfterReplacement(queryClient),
  });
}
