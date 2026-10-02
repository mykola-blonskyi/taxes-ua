"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { recordedPeriodOf, type KindDebt } from "@/data/dashboard/useDashboard";
import { isPeriodNotComputed, usePaymentDetails } from "@/data/payments/usePaymentDetails";
import { Button } from "@/shared/ui/button";
import { PayPanel } from "@/shared/ui/pay-panel";
import { DebtPeriod } from "./DebtParts";

// `onAmountChange` reports the amount on the panel, so the screen that holds this button can start
// "mark paid" from it.
export function PayDebtButton({
  debt,
  className,
  onAmountChange,
}: {
  debt: KindDebt;
  className?: string;
  onAmountChange?: (amountKop: number | null) => void;
}) {
  const t = useTranslations("pay");
  const tKinds = useTranslations("payments.kinds");
  const [open, setOpen] = useState(false);
  const startKop = Number(debt.amountKop);
  const [amountKop, setAmountKop] = useState<number | null>(startKop);
  const details = usePaymentDetails({ kind: debt.kind, ...recordedPeriodOf(debt) }, amountKop, open);
  const notComputed = isPeriodNotComputed(details.error);

  return (
    <>
      <Button
        type="button"
        variant="outline"
        size="sm"
        className={className}
        onClick={() => {
          setAmountKop(startKop);
          onAmountChange?.(startKop);
          setOpen(true);
        }}
      >
        {t("button")}
      </Button>
      <PayPanel
        open={open}
        onOpenChange={setOpen}
        title={
          <>
            {tKinds(debt.kind)}, <DebtPeriod debt={debt} />
          </>
        }
        initialAmountKop={startKop}
        onAmountChange={(next) => {
          setAmountKop(next);
          onAmountChange?.(next);
        }}
        details={details.data}
        loading={details.isFetching}
        failed={details.isError && !notComputed}
        notComputed={notComputed}
      />
    </>
  );
}
