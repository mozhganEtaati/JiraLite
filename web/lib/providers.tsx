"use client";

import {
  QueryClient,
  QueryClientProvider,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { ThemeProvider } from "next-themes";
import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  useSyncExternalStore,
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
  /*
   * The server has no localStorage, so it reports null — "not known yet" —
   * and the first client render after hydration swaps in the real answer.
   * tokens.set/clear announce themselves, so signing in or out re-renders
   * from here without anyone assigning state.
   */
  const hasToken = useSyncExternalStore(
    tokens.subscribe,
    tokens.has,
    () => null,
  );

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
    /*
     * The theme defaults to whatever the machine is set to, so someone who runs
     * their desktop dark never gets a white flash on the way in. next-themes
     * writes the class before paint, which is why <html> carries
     * suppressHydrationWarning in the root layout.
     */
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      <QueryClientProvider client={client}>
        <SessionProvider>{children}</SessionProvider>
      </QueryClientProvider>
    </ThemeProvider>
  );
}
