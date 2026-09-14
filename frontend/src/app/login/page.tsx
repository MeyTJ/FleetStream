"use client";

import { useState, type FormEvent } from "react";
import { useAuth } from "@/lib/auth-context";
import { ApiError } from "@/lib/api-client";
import { Truck, ShieldCheck, Eye } from "lucide-react";

/**
 * Dev-token role presets matching the BFF authorization policies
 * (Program.cs): FleetReader, AlertsAck, FleetAdmin.
 */
const ROLE_PRESETS = [
  {
    id: "operator",
    label: "Operator",
    description: "Fleet reader + alert ack",
    roles: ["fleet:reader", "alerts:ack"],
  },
  {
    id: "viewer",
    label: "Viewer",
    description: "Read-only (no ack)",
    roles: ["fleet:reader"],
  },
  {
    id: "admin",
    label: "Admin",
    description: "Full access (implies ack)",
    roles: ["fleet:admin"],
  },
] as const;

export default function LoginPage() {
  const { login } = useAuth();
  const [subject, setSubject] = useState("operator-01");
  const [presetId, setPresetId] = useState<string>("operator");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const preset = ROLE_PRESETS.find((p) => p.id === presetId) ?? ROLE_PRESETS[0];

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      await login(subject, [...preset.roles]);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.problem.detail ?? err.problem.title);
      } else {
        setError("An unexpected error occurred. Is the BFF running?");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 px-4 dark:bg-zinc-950">
      <div className="w-full max-w-sm">
        {/* Branding */}
        <div className="mb-8 flex flex-col items-center gap-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-blue-600 text-white">
            <Truck className="h-6 w-6" />
          </div>
          <h1 className="text-2xl font-bold tracking-tight">FleetStream</h1>
          <p className="text-sm text-muted-foreground">
            Fleet Operations Dashboard
          </p>
        </div>

        {/* Login form */}
        <form
          onSubmit={handleSubmit}
          className="rounded-lg border bg-white p-6 shadow-sm dark:bg-zinc-900"
        >
          <h2 className="mb-4 text-lg font-semibold">Sign in</h2>

          <div className="mb-4">
            <label
              htmlFor="subject"
              className="mb-1.5 block text-sm font-medium"
            >
              Operator ID
            </label>
            <input
              id="subject"
              type="text"
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder="e.g. operator-01"
              required
              minLength={1}
              maxLength={64}
              className="w-full rounded-md border bg-transparent px-3 py-2 text-sm outline-none transition-colors placeholder:text-muted-foreground focus:ring-2 focus:ring-ring"
            />
          </div>

          {/* Role preset (dev only) */}
          <fieldset className="mb-4">
            <legend className="mb-1.5 block text-sm font-medium">
              Role
            </legend>
            <div className="space-y-2">
              {ROLE_PRESETS.map((p) => (
                <label
                  key={p.id}
                  className={
                    "flex cursor-pointer items-start gap-2.5 rounded-md border px-3 py-2 transition-colors " +
                    (presetId === p.id
                      ? "border-blue-400 bg-blue-50 dark:border-blue-700 dark:bg-blue-950"
                      : "hover:bg-zinc-50 dark:hover:bg-zinc-800")
                  }
                >
                  <input
                    type="radio"
                    name="role-preset"
                    value={p.id}
                    checked={presetId === p.id}
                    onChange={() => setPresetId(p.id)}
                    className="mt-0.5"
                  />
                  <span className="flex-1">
                    <span className="flex items-center gap-1.5 text-sm font-medium">
                      {p.id === "admin" && (
                        <ShieldCheck className="h-3.5 w-3.5 text-blue-600 dark:text-blue-400" />
                      )}
                      {p.id === "viewer" && (
                        <Eye className="h-3.5 w-3.5 text-muted-foreground" />
                      )}
                      {p.label}
                    </span>
                    <span className="block text-xs text-muted-foreground">
                      {p.description} — {p.roles.join(", ")}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>

          {error && (
            <div className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={isSubmitting}
            className="flex w-full items-center justify-center rounded-md bg-blue-600 px-4 py-2.5 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isSubmitting ? (
              <div className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
            ) : (
              "Sign in with dev token"
            )}
          </button>

          <p className="mt-4 text-center text-xs text-muted-foreground">
            Development mode — uses BFF dev-token endpoint.
          </p>
        </form>
      </div>
    </div>
  );
}
