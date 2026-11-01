"use client";

import { useKeepUntil } from "@/data/declarations/useKeepUntil";
import { KeepUntilLine as Line } from "@/shared/ui/keep-until-line";

export function KeepUntilLine({ year }: { year: number }) {
  return <Line year={year} keepUntil={useKeepUntil(year).data?.keepUntil} />;
}
