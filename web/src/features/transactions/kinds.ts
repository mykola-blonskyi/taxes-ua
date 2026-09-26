import type { TransactionKind } from "@/data/transactions/useTransactions";

export const incomeKinds: readonly TransactionKind[] = ["Income", "RefundToClient"];

export const kindOptions: readonly TransactionKind[] = [
  "Income",
  "RefundToClient",
  "OwnTransfer",
  "FxSale",
  "OwnDeposit",
  "ErroneousReturn",
  "OtherNonIncome",
];

export function isNonIncomeKind(kind: TransactionKind): boolean {
  return !incomeKinds.includes(kind);
}
