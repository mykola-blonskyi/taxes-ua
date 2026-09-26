import { CalendarRange, House, Landmark, Receipt, Settings, type LucideIcon } from "lucide-react";

export type NavKey = "dashboard" | "transactions" | "periods" | "payments" | "settings";

export type NavItem = { readonly href: string; readonly key: NavKey; readonly icon: LucideIcon };

export const navItems: readonly NavItem[] = [
  { href: "/", key: "dashboard", icon: House },
  { href: "/transactions", key: "transactions", icon: Receipt },
  { href: "/periods", key: "periods", icon: CalendarRange },
  { href: "/payments", key: "payments", icon: Landmark },
  { href: "/settings", key: "settings", icon: Settings },
];
