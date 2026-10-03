"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useClients,
  useCreateClient,
  useDeleteClient,
  useUpdateClient,
  type ClientRequest,
  type ClientResponse,
} from "@/data/clients/useClients";
import { currencies } from "@/data/fx/useFxRate";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { SelectField, TextAreaField, TextField, FieldForm } from "@/shared/ui/fields";

type FormState = {
  name: string;
  address: string;
  country: string;
  vatId: string;
  email: string;
  defaultCurrency: string;
  notes: string;
};

const emptyForm: FormState = {
  name: "",
  address: "",
  country: "",
  vatId: "",
  email: "",
  defaultCurrency: "",
  notes: "",
};

function toFormState(client: ClientResponse): FormState {
  return {
    name: client.name,
    address: client.address ?? "",
    country: client.country ?? "",
    vatId: client.vatId ?? "",
    email: client.email ?? "",
    defaultCurrency: client.defaultCurrency ?? "",
    notes: client.notes ?? "",
  };
}

function toRequest(form: FormState): ClientRequest {
  const optional = (value: string) => (value.trim() === "" ? null : value);

  return {
    name: form.name,
    address: optional(form.address),
    country: optional(form.country),
    vatId: optional(form.vatId),
    email: optional(form.email),
    defaultCurrency: form.defaultCurrency === "" ? null : (form.defaultCurrency as ClientRequest["defaultCurrency"]),
    notes: optional(form.notes),
  };
}

export function ClientsSection() {
  const t = useTranslations("settings.clients");
  const query = useClients();
  const { data } = query;
  const [editing, setEditing] = useState<string | "new" | null>(null);

  if (query.isError || !data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return (
    <div className="flex max-w-xl flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("hint")}</p>

      {editing === "new" ? (
        <ClientEditor key="new" onClose={() => setEditing(null)} />
      ) : (
        <div>
          <Button type="button" size="sm" onClick={() => setEditing("new")}>
            {t("add")}
          </Button>
        </div>
      )}

      {data.length === 0 && editing !== "new" ? (
        <p className="text-sm text-muted-foreground">{t("empty")}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {data.map((client) => (
            <li key={client.id} className="min-w-0 rounded-lg border p-3">
              {editing === client.id ? (
                <ClientEditor client={client} onClose={() => setEditing(null)} />
              ) : (
                <ClientSummary client={client} onEdit={() => setEditing(client.id)} />
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ClientSummary({ client, onEdit }: { client: ClientResponse; onEdit: () => void }) {
  const t = useTranslations("settings.clients");
  const details = [client.country, client.vatId, client.email].filter(Boolean).join(" · ");

  return (
    <div className="flex min-w-0 items-start justify-between gap-2">
      <div className="flex min-w-0 flex-col gap-0.5">
        <span className="break-words font-medium">{client.name}</span>
        {details ? <span className="break-words text-xs text-muted-foreground">{details}</span> : null}
        <span className="text-xs text-muted-foreground">{t("receiptCount", { count: client.receiptCount })}</span>
      </div>
      <Button type="button" variant="outline" size="sm" onClick={onEdit}>
        {t("edit")}
      </Button>
    </div>
  );
}

function ClientEditor({ client, onClose }: { client?: ClientResponse; onClose: () => void }) {
  const t = useTranslations("settings.clients");
  const apiText = useApiErrorText();
  const tCurrencies = useTranslations("transactions.currencies");
  const locale = useLocale();
  const createClient = useCreateClient();
  const updateClient = useUpdateClient();
  const deleteClient = useDeleteClient();
  const [form, setForm] = useState<FormState>(() => (client ? toFormState(client) : emptyForm));
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const saving = createClient.isPending || updateClient.isPending;
  const saveError = createClient.error ?? updateClient.error;
  const saveFailure = saveError instanceof ApiError ? saveError : undefined;
  const deleteFailure = deleteClient.error instanceof ApiError ? deleteClient.error : null;
  const fieldErrors = apiText.fieldTexts(saveFailure);
  const rejectedFields = Object.keys(fieldErrors).length > 0;
  const countryName = countryDisplayName(form.country, locale);
  const canDelete = client !== undefined && client.receiptCount === 0;

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const body = toRequest(form);
    if (client) {
      updateClient.mutate({ id: client.id, body }, { onSuccess: onClose });
    } else {
      createClient.mutate(body, { onSuccess: onClose });
    }
  }

  return (
    <FieldForm quietErrors={Boolean(rejectedFields)} className="flex min-w-0 flex-col gap-3" onSubmit={submit}>
      {rejectedFields ? (
        <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : saveError ? (
        <p className="text-sm text-destructive">
          {saveFailure ? apiText.withReason(t("saveFailed"), saveFailure) : t("saveFailedGeneric")}
        </p>
      ) : null}

      <TextField
        id="client-name"
        label={t("name")}
        autoComplete="off"
        value={form.name}
        onChange={(value) => update("name", value)}
        errors={fieldErrors.name}
      />
      <TextAreaField
        rows={3}
        id="client-address"
        label={t("address")}
        autoComplete="off"
        value={form.address}
        onChange={(value) => update("address", value)}
        errors={fieldErrors.address}
      />
      <TextField
        id="client-country"
        label={t("country")}
        hint={countryName ?? t("countryHint")}
        autoComplete="off"
        maxLength={2}
        value={form.country}
        onChange={(value) => update("country", value.toUpperCase())}
        errors={fieldErrors.country}
      />
      <TextField
        id="client-vat-id"
        label={t("vatId")}
        autoComplete="off"
        value={form.vatId}
        onChange={(value) => update("vatId", value)}
        errors={fieldErrors.vatId}
      />
      <TextField
        id="client-email"
        label={t("email")}
        type="email"
        autoComplete="off"
        value={form.email}
        onChange={(value) => update("email", value)}
        errors={fieldErrors.email}
      />
      <SelectField
        id="client-default-currency"
        label={t("defaultCurrency")}
        value={form.defaultCurrency}
        onChange={(value) => update("defaultCurrency", value)}
        options={[
          { value: "", label: t("noDefaultCurrency") },
          ...currencies.map((currency) => ({ value: currency, label: tCurrencies(currency) })),
        ]}
        errors={fieldErrors.defaultCurrency}
      />
      <TextAreaField
        rows={3}
        id="client-notes"
        label={t("notes")}
        value={form.notes}
        onChange={(value) => update("notes", value)}
        errors={fieldErrors.notes}
      />

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" disabled={saving || form.name.trim() === ""}>
          {saving ? t("saving") : t("save")}
        </Button>
        <Button type="button" variant="outline" onClick={onClose} disabled={saving}>
          {t("cancel")}
        </Button>
        {client ? (
          <Link
            href={`/history?entity=Client&id=${client.id}`}
            className="text-sm text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center"
          >
            {t("history")}
          </Link>
        ) : null}
      </div>

      {client ? (
        <div className="flex flex-col gap-2 border-t pt-3">
          {confirmingDelete ? (
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm">{t("confirmDelete")}</span>
              <Button
                type="button"
                variant="destructive"
                size="sm"
                disabled={deleteClient.isPending}
                onClick={() => deleteClient.mutate(client.id, { onSuccess: onClose })}
              >
                {t("deleteConfirm")}
              </Button>
              <Button type="button" variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
                {t("cancel")}
              </Button>
            </div>
          ) : (
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                variant="destructive"
                size="sm"
                disabled={!canDelete}
                onClick={() => setConfirmingDelete(true)}
              >
                {t("delete")}
              </Button>
              {!canDelete ? <span className="text-xs text-muted-foreground">{t("deleteBlocked")}</span> : null}
            </div>
          )}
          {deleteClient.error ? (
            <p className="text-sm text-destructive">
              {deleteFailure === null
                ? t("deleteFailedGeneric")
                : deleteFailure.status === 409
                  ? t("deleteBlocked")
                  : apiText.withReason(t("deleteFailed"), deleteFailure)}
            </p>
          ) : null}
        </div>
      ) : null}
    </FieldForm>
  );
}

// Intl answers the code itself for a code it does not know, which is no help to the owner.
function countryDisplayName(code: string, locale: string): string | null {
  if (!/^[A-Z]{2}$/.test(code)) {
    return null;
  }

  try {
    const name = new Intl.DisplayNames([locale], { type: "region" }).of(code);

    return name && name !== code ? name : null;
  } catch {
    return null;
  }
}
