import { afterEach, describe, expect, it } from "vitest";
import { reply, renderApp, router, screen, stubFetch, waitFor } from "@/test/harness";
import { useDashboard } from "./dashboard/useDashboard";
import { usePeriods } from "./periods/usePeriods";

function Screen() {
  const dashboard = useDashboard();
  const periods = usePeriods(2026);

  return <p>{dashboard.isError || periods.isError ? "помилка" : "..."}</p>;
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 50));

afterEach(() => {
  window.history.pushState({}, "", "/");
});

describe("an expired session", () => {
  it("sends the owner to sign-in once and drops what was cached, however many requests fail", async () => {
    stubFetch({ "GET /api/dashboard": reply(401), "GET /api/periods/{year}": reply(401) });
    const { queryClient } = renderApp(<Screen />);
    queryClient.setQueryData(["transactions"], [{ id: "t1" }]);

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/login"));
    await settle();

    expect(router.replace).toHaveBeenCalledTimes(1);
    expect(queryClient.getQueryData(["transactions"])).toBeUndefined();
  });

  it("does not send the owner anywhere from the sign-in page", async () => {
    window.history.pushState({}, "", "/login");
    stubFetch({ "GET /api/dashboard": reply(401), "GET /api/periods/{year}": reply(401) });
    renderApp(<Screen />);

    await screen.findByText("помилка");

    expect(router.replace).not.toHaveBeenCalled();
  });
});
