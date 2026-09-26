import { TaxYearVerificationWarning } from "@/features/settings";
import { ScreenPlaceholder } from "@/shared/shell/ScreenPlaceholder";

export default function DashboardPage() {
  return (
    <div className="flex flex-col gap-4">
      <TaxYearVerificationWarning />
      <ScreenPlaceholder screen="dashboard" />
    </div>
  );
}
