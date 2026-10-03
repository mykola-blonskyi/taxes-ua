"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { Plus, Trash2 } from "lucide-react";
import { ApiError } from "@/data/api/client";
import { LoadState } from "@/data/api/LoadState";
import { useClients } from "@/data/clients/useClients";
import { currencies, type Currency } from "@/data/fx/useFxRate";
import {
  invoicePdfUrl,
  invoiceUnits,
  maxInvoiceLines,
  useCreateInvoice,
  useDeleteInvoice,
  useDuplicateInvoice,
  useIssueInvoice,
  useUpdateInvoice,
  type InvoiceRequest,
  type InvoiceResponse,
  type InvoiceUnit,
} from "@/data/invoices/useInvoices";
import { todayInKyiv } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { FieldErrors as FieldErrorList, QuietFieldErrorsScope, SelectField, TextAreaField, TextField } from "@/shared/ui/fields";
import { useErrorMessages } from "../errorMessages";
import {
  addDays,
  buildRequest,
  computeTotals,
  emptyForm,
  formFromInvoice,
  newLine,
  type FieldErrors,
  type InvoiceForm,
  type LineForm,
} from "../form";

type Confirming = "issue" | "delete" | null;

export function DraftEditor({
  invoice,
  onBack,
  onOpen,
}: {
  invoice?: InvoiceResponse;
  onBack: () => void;
  onOpen: (id: string) => void;
}) {
  const t = useTranslations("invoices.editor");
  const tErrors = useTranslations("invoices.errors");
  const tCurrencies = useTranslations("transactions.currencies");
  const tUnits = useTranslations("invoices.units");
  const locale = useLocale();
  const clients = useClients();
  const createInvoice = useCreateInvoice();
  const updateInvoice = useUpdateInvoice();
  const deleteInvoice = useDeleteInvoice();
  const duplicateInvoice = useDuplicateInvoice();
  const issueInvoice = useIssueInvoice();

  const [id, setId] = useState<string | null>(invoice?.id ?? null);
  const [form, setForm] = useState<InvoiceForm>(() => (invoice ? formFromInvoice(invoice) : emptyForm(todayInKyiv())));
  const [baseline, setBaseline] = useState(() => JSON.stringify(stripKeys(form)));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [confirming, setConfirming] = useState<Confirming>(null);
  const [busy, setBusy] = useState(false);

  const messages = useErrorMessages(errors);
  const totals = computeTotals(form.lines);
  const dirty = JSON.stringify(stripKeys(form)) !== baseline;
  const problems = messages.issueProblems();
  const hasErrors = Object.keys(errors).length > 0;

  function edit(change: (current: InvoiceForm) => InvoiceForm) {
    setForm(change);
    setSaved(false);
  }

  function editLine(key: number, patch: Partial<LineForm>) {
    edit((current) => ({
      ...current,
      lines: current.lines.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    }));
  }

  function changeClient(clientId: string) {
    const defaultCurrency = clients.data?.find((client) => client.id === clientId)?.defaultCurrency;

    edit((current) => ({
      ...current,
      clientId,
      currency: id === null && defaultCurrency ? defaultCurrency : current.currency,
    }));
  }

  function changeIssueDate(issueDate: string) {
    edit((current) => ({
      ...current,
      issueDate,
      dueDate:
        current.issueDate !== "" && current.dueDate === addDays(current.issueDate, 14) && issueDate !== ""
          ? addDays(issueDate, 14)
          : current.dueDate,
    }));
  }

  function fail(error: unknown) {
    if (error instanceof ApiError && Object.keys(error.fieldCodes).length > 0) {
      setErrors(error.fieldCodes);
    } else if (error instanceof ApiError && error.status === 409) {
      setFailure(tErrors("conflict"));
    } else {
      setFailure(tErrors("generic"));
    }
  }

  async function save(): Promise<InvoiceResponse | null> {
    setErrors({});
    setFailure(null);
    setSaved(false);

    const built = buildRequest(form);

    if (!built.request) {
      setErrors(built.errors);

      return null;
    }

    const body: InvoiceRequest = built.request;

    try {
      const result = id ? await updateInvoice.mutateAsync({ id, body }) : await createInvoice.mutateAsync(body);

      if (!result) {
        setFailure(tErrors("generic"));

        return null;
      }

      // The server's figures replace the typed ones, so what is on screen is what the PDF will say.
      const next = formFromInvoice(result);
      setId(result.id);
      setForm(next);
      setBaseline(JSON.stringify(stripKeys(next)));
      setSaved(true);

      return result;
    } catch (error) {
      fail(error);

      return null;
    }
  }

  async function saveIfNeeded(): Promise<string | null> {
    if (id !== null && !dirty) {
      return id;
    }

    return (await save())?.id ?? null;
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);

    try {
      await action();
    } finally {
      setBusy(false);
    }
  }

  function preview() {
    // Opened synchronously so the click still counts as a user gesture once the save has finished.
    const tab = window.open("", "_blank");

    void run(async () => {
      const savedId = await saveIfNeeded();

      if (savedId === null) {
        tab?.close();

        return;
      }

      const url = invoicePdfUrl(savedId);

      if (tab) {
        tab.location.href = url;
      } else {
        window.open(url, "_blank");
      }
    });
  }

  function issue() {
    setConfirming(null);

    void run(async () => {
      const savedId = await saveIfNeeded();

      if (savedId === null) {
        return;
      }

      try {
        await issueInvoice.mutateAsync(savedId);
        onOpen(savedId);
      } catch (error) {
        fail(error);
      }
    });
  }

  function duplicate() {
    void run(async () => {
      const savedId = await saveIfNeeded();

      if (savedId === null) {
        return;
      }

      try {
        const copy = await duplicateInvoice.mutateAsync(savedId);

        if (copy) {
          onOpen(copy.id);
        }
      } catch (error) {
        fail(error);
      }
    });
  }

  function remove() {
    if (id === null) {
      return;
    }

    setConfirming(null);

    void run(async () => {
      try {
        await deleteInvoice.mutateAsync(id);
        onBack();
      } catch (error) {
        fail(error);
      }
    });
  }

  const clientOptions = (clients.data ?? []).map((client) => ({ value: client.id, label: client.name }));

  return (
    <QuietFieldErrorsScope quiet={hasErrors}>
    <div className="flex min-w-0 max-w-2xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button type="button" variant="outline" size="sm" onClick={onBack}>
          {t("back")}
        </Button>
        <h3 className="text-base font-semibold">{id ? t("draftTitle") : t("newTitle")}</h3>
      </div>

      {hasErrors ? (
        <div role="alert" className="flex flex-col gap-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          <p>{tErrors("validation")}</p>
          {problems.length > 0 ? (
            <ul className="flex flex-col gap-1">
              {problems.map((problem) => (
                <li key={problem.key} className="break-words">
                  {problem.text}
                  {problem.href ? (
                    <>
                      {" "}
                      <Link href={problem.href} className="font-medium underline underline-offset-4">
                        {tErrors("openSettings")}
                      </Link>
                    </>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
      {failure ? <p className="text-sm text-destructive">{failure}</p> : null}

      <SelectField
        id="invoice-client"
        label={t("client")}
        placeholder={t("clientPlaceholder")}
        value={form.clientId}
        onChange={changeClient}
        options={clientOptions}
        errors={messages.forField("clientId")}
      />
      <LoadState quiet query={clients} failed={t("clientsFailed")} />
      {clients.data && clients.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {t("noClients")}{" "}
          <Link href="/settings" className="font-medium text-primary underline-offset-4 hover:underline">
            {tErrors("openSettings")}
          </Link>
        </p>
      ) : null}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <TextField
          id="invoice-issue-date"
          label={t("issueDate")}
          type="date"
          value={form.issueDate}
          onChange={changeIssueDate}
          errors={messages.forField("issueDate")}
        />
        <TextField
          id="invoice-due-date"
          label={t("dueDate")}
          type="date"
          value={form.dueDate}
          onChange={(dueDate) => edit((current) => ({ ...current, dueDate }))}
          errors={messages.forField("dueDate")}
        />
        <SelectField
          id="invoice-currency"
          label={t("currency")}
          value={form.currency}
          onChange={(currency) => edit((current) => ({ ...current, currency: currency as Currency }))}
          options={currencies.map((currency) => ({ value: currency, label: tCurrencies(currency) }))}
          errors={messages.forField("currency")}
        />
      </div>

      <section className="flex flex-col gap-3">
        <h4 className="text-sm font-semibold">{t("lines")}</h4>
        <FieldErrorList id="invoice-lines-error" errors={messages.forField("lines")} />

        {form.lines.map((line, index) => (
          <div key={line.key} className="flex min-w-0 flex-col gap-3 rounded-lg border p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm font-medium">{t("lineTitle", { number: index + 1 })}</span>
              {form.lines.length > 1 ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() =>
                    edit((current) => ({ ...current, lines: current.lines.filter((item) => item.key !== line.key) }))
                  }
                >
                  <Trash2 aria-hidden="true" />
                  {t("removeLine")}
                </Button>
              ) : null}
            </div>

            <TextAreaField
              id={`invoice-line-${line.key}-en`}
              label={t("descriptionEn")}
              rows={2}
              value={line.descriptionEn}
              onChange={(descriptionEn) => editLine(line.key, { descriptionEn })}
              errors={messages.forField(`lines[${index}].descriptionEn`)}
            />
            <TextAreaField
              id={`invoice-line-${line.key}-uk`}
              label={t("descriptionUk")}
              rows={2}
              value={line.descriptionUk}
              onChange={(descriptionUk) => editLine(line.key, { descriptionUk })}
              errors={messages.forField(`lines[${index}].descriptionUk`)}
            />

            <div className="grid grid-cols-2 gap-3">
              <div className="col-span-2 sm:col-span-1">
                <SelectField
                  id={`invoice-line-${line.key}-unit`}
                  label={t("unit")}
                  value={line.unit}
                  onChange={(unit) => editLine(line.key, { unit: unit as InvoiceUnit })}
                  options={invoiceUnits.map((unit) => ({ value: unit, label: tUnits(unit) }))}
                  errors={messages.forField(`lines[${index}].unit`)}
                />
              </div>
              <TextField
                id={`invoice-line-${line.key}-quantity`}
                label={t("quantity")}
                inputMode="decimal"
                autoComplete="off"
                value={line.quantity}
                onChange={(quantity) => editLine(line.key, { quantity })}
                errors={messages.forField(`lines[${index}].quantityThousandths`)}
              />
              <TextField
                id={`invoice-line-${line.key}-rate`}
                label={t("rate")}
                inputMode="decimal"
                autoComplete="off"
                value={line.rate}
                onChange={(rate) => editLine(line.key, { rate })}
                errors={messages.forField(`lines[${index}].rateMinor`)}
              />
            </div>

            <p className="text-right text-sm">
              <span className="text-muted-foreground">{t("amount")}: </span>
              <span className="font-medium tabular-nums">
                {totals.amounts[index] == null ? "—" : formatAmount(totals.amounts[index], form.currency, locale)}
              </span>
            </p>
          </div>
        ))}

        <div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={form.lines.length >= maxInvoiceLines}
            onClick={() => edit((current) => ({ ...current, lines: [...current.lines, newLine()] }))}
          >
            <Plus aria-hidden="true" />
            {t("addLine")}
          </Button>
        </div>
      </section>

      <p className="flex flex-wrap items-baseline justify-between gap-2 border-t pt-3">
        <span className="text-sm font-medium">{t("total")}</span>
        <span className="text-lg font-semibold tabular-nums">{formatAmount(totals.total, form.currency, locale)}</span>
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <Button type="button" disabled={busy || confirming !== null} onClick={() => void run(async () => void (await save()))}>
          {busy ? t("saving") : t("save")}
        </Button>
        <Button type="button" variant="outline" disabled={busy} onClick={preview}>
          {t("preview")}
        </Button>
        <Button type="button" variant="outline" disabled={busy} onClick={() => setConfirming("issue")}>
          {t("issue")}
        </Button>
        {id ? (
          <Button type="button" variant="outline" disabled={busy} onClick={duplicate}>
            {t("duplicate")}
          </Button>
        ) : null}
        {id ? (
          <Button type="button" variant="destructive" disabled={busy} onClick={() => setConfirming("delete")}>
            {t("delete")}
          </Button>
        ) : null}
        {saved && !dirty ? <span className="text-sm text-muted-foreground">{t("saved")}</span> : null}
      </div>

      {confirming ? (
        <div className="flex flex-col gap-2 rounded-lg border p-3">
          <p className="text-sm">{confirming === "issue" ? t("confirmIssue") : t("confirmDelete")}</p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              size="sm"
              variant={confirming === "issue" ? "default" : "destructive"}
              disabled={busy}
              onClick={confirming === "issue" ? issue : remove}
            >
              {confirming === "issue" ? t("issueConfirm") : t("deleteConfirm")}
            </Button>
            <Button type="button" size="sm" variant="outline" onClick={() => setConfirming(null)}>
              {t("cancel")}
            </Button>
          </div>
        </div>
      ) : null}
    </div>
    </QuietFieldErrorsScope>
  );
}

// Line keys only identify rows on screen; they must not make an untouched form look edited.
function stripKeys(form: InvoiceForm) {
  return {
    ...form,
    lines: form.lines.map((line) => [line.descriptionEn, line.descriptionUk, line.unit, line.quantity, line.rate]),
  };
}
