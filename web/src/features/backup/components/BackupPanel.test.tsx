import { describe, expect, it } from "vitest";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { BackupPanel } from "./BackupPanel";

const restore = "POST /api/restore" as const;
const summary = { clients: 1, transactions: 2, budgetPayments: 3 };

describe("BackupPanel: restore", () => {
  it.each([
    ["uk", "Замінити мої дані", "Канали сповіщень повернулися вимкненими: підтвердьте їх ще раз у Налаштуваннях."],
    ["ru", "Заменить мои данные", "Каналы уведомлений вернулись выключенными: подтвердите их ещё раз в Настройках."],
  ] as const)("tells the owner in %s that notification channels need confirming again", async (locale, confirm, hint) => {
    stubFetch({ [restore]: summary });
    const { user, container } = renderApp(<BackupPanel />, { locale });

    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
    await user.upload(input, new File(["{}"], "backup.json", { type: "application/json" }));
    await user.click(screen.getByRole("button", { name: confirm }));

    expect(await screen.findByText(hint)).toBeVisible();
  });
});
