"use client";

import {
  QueryClient,
  QueryClientProvider,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import { ApiError, api, tokens } from "./api";
import type { AuthTokens, Me } from "./types";

function makeClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: false,
        retry: (count, error) =>
          error instanceof ApiError && error.status < 500 ? false : count < 2,
      },
    },
  });
}

/* ── session ──────────────────────────────────────────────── */

type Session = {
  me: Me | undefined;
  status: "loading" | "signed-in" | "signed-out";
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
};

const SessionContext = createContext<Session | null>(null);

export function useSession() {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error("useSession must be used inside <Providers>");
  return ctx;
}

function SessionProvider({ children }: { children: React.ReactNode }) {
  const qc = useQueryClient();
  const router = useRouter();
  const [hasToken, setHasToken] = useState<boolean | null>(null);

  useEffect(() => {
    setHasToken(Boolean(tokens.access()));
  }, []);

  const { data: me, isLoading } = useQuery({
    queryKey: ["me"],
    queryFn: () => api.get<Me>("/api/users/me"),
    enabled: hasToken === true,
    staleTime: 5 * 60_000,
  });

  const signIn = useCallback(
    async (email: string, password: string) => {
      const auth = await api.anon<AuthTokens>("/api/auth/login", {
        email,
        password,
      });
      tokens.set(auth.accessToken, auth.refreshToken);
      setHasToken(true);
      await qc.invalidateQueries();
    },
    [qc],
  );

  const signOut = useCallback(async () => {
    const refreshToken = tokens.refresh();
    try {
      if (refreshToken) await api.post("/api/auth/logout", { refreshToken });
    } catch {
      /* the token is going away either way */
    }
    tokens.clear();
    setHasToken(false);
    qc.clear();
    router.replace("/login");
  }, [qc, router]);

  const value = useMemo<Session>(
    () => ({
      me,
      status:
        hasToken === null || (hasToken && isLoading)
          ? "loading"
          : hasToken && me
            ? "signed-in"
            : "signed-out",
      signIn,
      signOut,
    }),
    [me, hasToken, isLoading, signIn, signOut],
  );

  return (
    <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
  );
}

export function Providers({ children }: { children: React.ReactNode }) {
  const [client] = useState(makeClient);
  return (
    <QueryClientProvider client={client}>
      <SessionProvider>{children}</SessionProvider>
    </QueryClientProvider>
  );
}
