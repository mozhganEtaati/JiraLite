/**
 * Single entry point to the JiraLite API.
 *
 * Everything the backend returns on failure is RFC7807 ProblemDetails
 * (spec/19-api-guidelines.md), so we normalise it once here and forms can
 * read `problem.errors[field]` without re-parsing anything.
 *
 * Access tokens are short-lived. On a 401 we refresh once and replay the
 * request; concurrent 401s queue behind a single refresh so we never fire
 * two refreshes with the same rotated token.
 */

/**
 * Empty by default: requests go to this app's own origin and next.config.ts
 * rewrites /api and /files through to the backend, so the browser never has
 * to cross an origin and the API needs no CORS configuration.
 */
export const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? "";

const ACCESS_KEY = "jl.access";
const REFRESH_KEY = "jl.refresh";

export const tokens = {
  access: () =>
    typeof window === "undefined" ? null : localStorage.getItem(ACCESS_KEY),
  refresh: () =>
    typeof window === "undefined" ? null : localStorage.getItem(REFRESH_KEY),
  set(access: string, refresh?: string | null) {
    localStorage.setItem(ACCESS_KEY, access);
    if (refresh) localStorage.setItem(REFRESH_KEY, refresh);
  },
  clear() {
    localStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
  },
};

export class ApiError extends Error {
  status: number;
  title: string;
  detail?: string;
  /** field name -> messages, straight from ProblemDetails `errors` */
  errors?: Record<string, string[]>;

  constructor(status: number, body: unknown) {
    const p = (body ?? {}) as Record<string, unknown>;
    const title =
      (typeof p.title === "string" && p.title) ||
      (typeof p.detail === "string" && p.detail) ||
      fallbackTitle(status);
    super(title);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.detail = typeof p.detail === "string" ? p.detail : undefined;
    this.errors = (p.errors as Record<string, string[]>) ?? undefined;
  }

  /** First message for a field, matched case-insensitively. */
  field(name: string): string | undefined {
    if (!this.errors) return undefined;
    const hit = Object.keys(this.errors).find(
      (k) => k.toLowerCase() === name.toLowerCase(),
    );
    return hit ? this.errors[hit]?.[0] : undefined;
  }
}

function fallbackTitle(status: number) {
  if (status === 401) return "Your session has ended. Sign in again.";
  if (status === 403) return "You do not have access to this.";
  if (status === 404) return "Not found.";
  if (status === 409) return "That conflicts with the current state.";
  if (status === 429) return "Too many requests. Wait a moment.";
  if (status >= 500) return "The server could not complete that.";
  return "That request could not be completed.";
}

let refreshing: Promise<string | null> | null = null;

async function runRefresh(): Promise<string | null> {
  const refreshToken = tokens.refresh();
  if (!refreshToken) return null;
  const res = await fetch(`${API_BASE}/api/auth/refresh`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });
  if (!res.ok) {
    tokens.clear();
    return null;
  }
  const data = (await res.json()) as {
    accessToken: string;
    refreshToken?: string;
  };
  tokens.set(data.accessToken, data.refreshToken);
  return data.accessToken;
}

export type Query = Record<
  string,
  string | number | boolean | null | undefined
>;

function withQuery(path: string, query?: Query) {
  if (!query) return path;
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v === undefined || v === null || v === "") continue;
    qs.set(k, String(v));
  }
  const s = qs.toString();
  return s ? `${path}?${s}` : path;
}

type RequestOptions = {
  method?: string;
  body?: unknown;
  query?: Query;
  /** skip the auth header + refresh dance (login, register, refresh) */
  anonymous?: boolean;
  signal?: AbortSignal;
};

async function raw(path: string, opts: RequestOptions, token: string | null) {
  const isForm = opts.body instanceof FormData;
  const headers: Record<string, string> = {};
  if (!isForm && opts.body !== undefined)
    headers["Content-Type"] = "application/json";
  if (token) headers["Authorization"] = `Bearer ${token}`;

  return fetch(`${API_BASE}${withQuery(path, opts.query)}`, {
    method: opts.method ?? "GET",
    headers,
    body: isForm
      ? (opts.body as FormData)
      : opts.body !== undefined
        ? JSON.stringify(opts.body)
        : undefined,
    signal: opts.signal,
  });
}

export async function request<T>(
  path: string,
  opts: RequestOptions = {},
): Promise<T> {
  let res = await raw(path, opts, opts.anonymous ? null : tokens.access());

  if (res.status === 401 && !opts.anonymous && tokens.refresh()) {
    refreshing ??= runRefresh().finally(() => {
      refreshing = null;
    });
    const fresh = await refreshing;
    if (fresh) res = await raw(path, opts, fresh);
  }

  if (res.status === 204 || res.headers.get("content-length") === "0") {
    if (res.ok) return undefined as T;
  }

  const text = await res.text();
  const data = text ? safeJson(text) : undefined;

  if (!res.ok) throw new ApiError(res.status, data ?? { title: text });
  return data as T;
}

function safeJson(text: string) {
  try {
    return JSON.parse(text);
  } catch {
    return { title: text };
  }
}

export const api = {
  get: <T>(path: string, query?: Query) => request<T>(path, { query }),
  post: <T>(path: string, body?: unknown, query?: Query) =>
    request<T>(path, { method: "POST", body, query }),
  patch: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PATCH", body }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body }),
  del: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "DELETE", body }),
  anon: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body, anonymous: true }),
};

/** Cursor-paged list shape used by every paged endpoint (CursorPagination.PageInfo). */
export type Paged<T> = {
  items: T[];
  pageInfo: { hasNextPage: boolean; nextCursor: string | null };
};

/** Endpoints that return the whole collection at once. */
export type Listed<T> = { items: T[] };

/** Files are served behind auth, so fetch as a blob and hand back an object URL. */
export async function fetchBlobUrl(path: string): Promise<string> {
  const res = await raw(path, {}, tokens.access());
  if (!res.ok) throw new ApiError(res.status, await res.json().catch(() => ({})));
  return URL.createObjectURL(await res.blob());
}
