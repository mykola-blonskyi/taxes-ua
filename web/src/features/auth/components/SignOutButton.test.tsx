import { describe, expect, it } from "vitest";
import { dashboardQueryKey } from "@/data/dashboard/useDashboard";
import { renderApp, router, screen, stubFetch, waitFor } from "@/test/harness";
import { SignOutButton } from "./SignOutButton";

describe("SignOutButton", () => {
  it("leaves nothing of the owner's data in the cache and sends them to the sign-in page", async () => {
    const api = stubFetch({ "POST /api/auth/logout": {} });
    const { queryClient, user } = renderApp(<SignOutButton />);
    queryClient.setQueryData(["me"], { id: "u1" });
    queryClient.setQueryData(dashboardQueryKey, { year: 2026 });

    await user.click(screen.getByRole("button", { name: "Вийти" }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/login"));
    expect(api.requestsTo("POST /api/auth/logout")).toHaveLength(1);
    expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
    expect(queryClient.getQueryData(dashboardQueryKey)).toBeUndefined();
  });
});
