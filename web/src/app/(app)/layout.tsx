import { AuthGate, SignOutButton } from "@/features/auth";
import { AppHeader } from "@/shared/shell/AppHeader";
import { AppNav } from "@/shared/shell/AppNav";
import { Disclaimer } from "@/shared/shell/Disclaimer";

export default function AppLayout({ children }: LayoutProps<"/">) {
  return (
    <AuthGate>
      <div className="flex min-h-full flex-1 flex-col">
        <AppHeader actions={<SignOutButton />} />
        <div className="flex flex-1 flex-col md:flex-row">
          <AppNav />
          <div className="flex min-w-0 flex-1 flex-col">
            <main className="flex-1 px-4 py-6 md:px-8">{children}</main>
            <Disclaimer />
          </div>
        </div>
      </div>
    </AuthGate>
  );
}
