"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useInvoice } from "@/data/invoices/useInvoices";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { DraftEditor } from "./DraftEditor";
import { InvoiceDetail } from "./InvoiceDetail";
import { InvoiceList } from "./InvoiceList";

type View = { kind: "list" } | { kind: "new" } | { kind: "open"; id: string };

export function InvoicesScreen() {
  const [view, setView] = useState<View>({ kind: "list" });

  const toList = () => setView({ kind: "list" });
  const open = (id: string) => setView({ kind: "open", id });

  switch (view.kind) {
    case "list":
      return <InvoiceList onOpen={open} onNew={() => setView({ kind: "new" })} />;
    case "new":
      return <DraftEditor key="new" onBack={toList} onOpen={open} />;
    case "open":
      return <OpenInvoice key={view.id} id={view.id} onBack={toList} onOpen={open} />;
  }
}

function OpenInvoice({ id, onBack, onOpen }: { id: string; onBack: () => void; onOpen: (id: string) => void }) {
  const t = useTranslations("invoices");
  const query = useInvoice(id);
  const { data } = query;

  if (query.isLoading || query.isError || !data) {
    // One LoadState for loading and failure, so a retry keeps keyboard focus on its button.
    return (
      <div className="flex flex-col items-start gap-3">
        <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />
        <Button type="button" variant="outline" size="sm" onClick={onBack}>
          {t("editor.back")}
        </Button>
      </div>
    );
  }

  return data.status === "Draft" ? (
    <DraftEditor invoice={data} onBack={onBack} onOpen={onOpen} />
  ) : (
    <InvoiceDetail invoice={data} onBack={onBack} onOpen={onOpen} />
  );
}
