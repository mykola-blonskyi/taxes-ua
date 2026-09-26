"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useSyncExternalStore } from "react";
import { ApiError } from "@/data/api/client";
import { meQueryKey } from "@/data/auth/useMe";

export class PasskeyUnsupportedError extends Error {
  constructor() {
    super("WebAuthn is not supported in this browser");
    this.name = "PasskeyUnsupportedError";
  }
}

export class PasskeyCancelledError extends Error {
  constructor() {
    super("The passkey ceremony was cancelled");
    this.name = "PasskeyCancelledError";
  }
}

export function isPasskeySupported(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof window.PublicKeyCredential !== "undefined" &&
    typeof window.PublicKeyCredential.parseCreationOptionsFromJSON === "function" &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === "function" &&
    typeof navigator.credentials !== "undefined"
  );
}

function subscribeToNothing() {
  return () => {};
}

function getUnsupportedSnapshot() {
  return false;
}

// Support never changes after mount, so this only needs to resolve the browser-vs-server
// mismatch once: useSyncExternalStore reports the server-safe `false` for the first (SSR-matching)
// client render, then re-renders with the real capability, with no effect or setState involved.
export function useIsPasskeySupported(): boolean {
  return useSyncExternalStore(subscribeToNothing, isPasskeySupported, getUnsupportedSnapshot);
}

// Each row fuses its parse step and its navigator.credentials call into one closure rather than
// exposing them as two fields. Indexing a union-keyed table by a non-literal mode makes TypeScript
// demand an argument assignable to both rows' parameter types at once, which nothing satisfies, so
// separate fields would force an unsound cast in the driver below.
const passkeyCeremonies = {
  register: {
    optionsPath: "/api/auth/passkey/register/options",
    submitPath: "/api/auth/passkey/register",
    getCredential: (rawOptions: unknown) => {
      const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(
        rawOptions as PublicKeyCredentialCreationOptionsJSON,
      );

      return navigator.credentials.create({ publicKey });
    },
  },
  signIn: {
    optionsPath: "/api/auth/passkey/login/options",
    submitPath: "/api/auth/passkey/login",
    getCredential: (rawOptions: unknown) => {
      const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(
        rawOptions as PublicKeyCredentialRequestOptionsJSON,
      );

      return navigator.credentials.get({ publicKey });
    },
  },
} as const;

export type PasskeyMode = keyof typeof passkeyCeremonies;

async function postForJson(path: string): Promise<unknown> {
  const response = await fetch(path, { method: "POST", credentials: "same-origin" });
  if (!response.ok) {
    throw new ApiError(response.status, response.statusText || undefined);
  }

  return response.json();
}

async function postCredential(path: string, credential: Credential): Promise<void> {
  const response = await fetch(path, {
    method: "POST",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json" },
    // toJSON() ships in the same WebAuthn Level 3 rollout as parse*OptionsFromJSON, so the
    // isPasskeySupported() gate in the mutation below already covers this call being well-formed.
    body: JSON.stringify({ credentialJson: JSON.stringify(credential) }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, response.statusText || undefined);
  }
}

async function runCeremony(mode: PasskeyMode): Promise<void> {
  const ceremony = passkeyCeremonies[mode];
  const rawOptions = await postForJson(ceremony.optionsPath);

  let credential: Credential | null;
  try {
    credential = await ceremony.getCredential(rawOptions);
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === "NotAllowedError") {
      throw new PasskeyCancelledError();
    }
    throw cause;
  }

  if (!credential) {
    throw new PasskeyCancelledError();
  }

  await postCredential(ceremony.submitPath, credential);
}

export function usePasskeyCeremony(mode: PasskeyMode) {
  const router = useRouter();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      if (!isPasskeySupported()) {
        throw new PasskeyUnsupportedError();
      }

      await runCeremony(mode);
    },
    onSuccess: () => {
      if (mode === "signIn") {
        queryClient.removeQueries({ queryKey: meQueryKey });
        router.replace("/");
      }
    },
  });
}
