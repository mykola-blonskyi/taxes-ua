import { useTranslations } from "next-intl";
import { Download } from "lucide-react";
import { transactionsExportUrl, type ExportFormat } from "@/data/transactions/exportUrl";
import { Button } from "@/shared/ui/button";

const formats: readonly ExportFormat[] = ["xlsx", "csv", "pdf"];

export function ExportButtons({ year }: { year: number }) {
  const t = useTranslations("transactions.export");

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-sm text-muted-foreground">{t("title")}</span>
      {formats.map((format) => (
        <Button key={format} asChild variant="outline" size="sm">
          <a href={transactionsExportUrl(format, year)} download aria-label={t("download", { format: format.toUpperCase(), year })}>
            <Download aria-hidden="true" />
            {format.toUpperCase()}
          </a>
        </Button>
      ))}
    </div>
  );
}
