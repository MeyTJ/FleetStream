"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import {
  clearStoredToken,
  getStoredToken,
  requestDevToken,
  storeToken,
} from "@/lib/api-client";
import { useRouter } from "next/navigation";

// ─── Types ────────────────────────────────────────────────────────

interface AuthState {
  /** The JWT bearer token, or null if not authenticated. */
  token: string | null;
  /** The logged-in subject (username). */
  subject: string | null;
  /** Authenticate via the BFF dev-token endpoint. */
  login: (subject: string, roles?: string[]) => Promise<void>;
  /** Clear token and redirect to /login. */
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

// ─── Lazy initializers ────────────────────────────────────────────

function getInitialToken(): string | null {
  return getStoredToken();
}

function getInitialSubject(): string | null {
  if (typeof window === "undefined") return null;
  return sessionStorage.getItem("fs_subject");
}

// ─── Provider ─────────────────────────────────────────────────────

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(getInitialToken);
  const [subject, setSubject] = useState<string | null>(getInitialSubject);
  const router = useRouter();

  const login = useCallback(
    async (subject: string, roles?: string[]) => {
      const response = await requestDevToken(subject, roles);
      storeToken(response.accessToken, response.expiresAt);
      sessionStorage.setItem("fs_subject", subject);
      setToken(response.accessToken);
      setSubject(subject);
      router.push("/");
    },
    [router],
  );

  const logout = useCallback(() => {
    clearStoredToken();
    sessionStorage.removeItem("fs_subject");
    setToken(null);
    setSubject(null);
    router.push("/login");
  }, [router]);

  const value = useMemo<AuthState>(
    () => ({ token, subject, login, logout }),
    [token, subject, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// ─── Hook ─────────────────────────────────────────────────────────

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an <AuthProvider>");
  }
  return ctx;
}


