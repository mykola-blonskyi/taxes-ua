import { LanguageToggle } from "./LanguageToggle";
import { ThemeToggle } from "./ThemeToggle";

// The header's language menu and theme toggle: each writes the browser and, signed in, the server too.
export function AppearanceMenus() {
  return (
    <>
      <LanguageToggle />
      <ThemeToggle />
    </>
  );
}
