import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query";
import { ApiError } from "@/data/api/client";
import { meQueryKey } from "@/data/auth/useMe";

const loginPath = "/login";

export function createQueryClient(navigate: (path: string) => void) {
  let leaving = false;

  function onFailure(error: Error) {
    if (!(error instanceof ApiError) || error.status !== 401) {
      return;
    }
    if (leaving || window.location.pathname.startsWith(loginPath)) {
      return;
    }

    leaving = true;
    client.clear();
    navigate(loginPath);
  }

  const client = new QueryClient({
    defaultOptions: { queries: { retry: 1, staleTime: 60_000 } },
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (query.queryKey[0] !== meQueryKey[0]) {
          onFailure(error);
        }
      },
      onSuccess: (_data, query) => {
        if (query.queryKey[0] === meQueryKey[0]) {
          leaving = false;
        }
      },
    }),
    mutationCache: new MutationCache({ onError: onFailure }),
  });

  return client;
}
