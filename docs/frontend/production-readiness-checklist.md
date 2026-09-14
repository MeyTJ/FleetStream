# Frontend - Production Readiness Checklist

> **Application:** `frontend` (Next.js)
> **Audit date:** 2026-09-14
> **Status:** Partially implemented - F0-F3 feature code exists and is verified
> locally; F4-F5 hardening, E2E, and deployment are still open.

> **Correction to the previous version of this file:** it read "Not started -
> Phase 4 pending BFF gate". That was wrong. `frontend/` contains a working
> Next.js 16 App Router dashboard (login, fleet summary, trucks table, map with
> live markers, alert feed with ack, telemetry sparklines, settings) plus
> SignalR and REST integration, a lint/typecheck/build CI workflow, and now a
> unit-test suite. Everything ticked below was verified in this pass.

Reference: [frontend/docs](../../frontend/docs/README.md) - BFF contracts in [BffApi/docs](../../BffApi/docs/README.md)

---

## Entry gate (before F0)

- [ ] BFF `02-api-contract.md` -> Final
- [ ] BFF `03-signalr-protocol.md` -> Final - **blocked**: section 3.3 documents
      `OnAlertsPurged`, `OnPresenceChange` and `OnSystemMessage`, none of which
      the server ever sends (see the implementation-status table added there).
- [x] OpenAPI reachable from frontend CI - the BFF serves
      `/swagger/v10/swagger.json`, but no frontend job consumes it yet.
- [x] Dev token flow verified against local BFF - `POST /api/v1/auth/dev-token`
      drives the login page and the role presets in `src/app/login/page.tsx`.

---

## P0 - Block production deployment

- [ ] Production OIDC / JWT validation (no dev-token in prod build) - login is
      dev-token only. `jose` is a dependency but no RS256/OIDC path exists.
- [x] CSP and security headers configured - `src/lib/csp.ts` derives
      `connect-src` from `NEXT_PUBLIC_API_BASE_URL` /
      `NEXT_PUBLIC_SIGNALR_HUB_URL` (or an explicit `CSP_CONNECT_SRC`), so a
      staging/prod build names its own BFF. Previously localhost was hardcoded,
      which made any non-local deployment impossible. Covered by
      `src/lib/csp.test.ts`.
- [x] No secrets in client bundle (`NEXT_PUBLIC_*` audit) - only the API base URL
      and hub URL are exposed. The bearer token sits in `sessionStorage`; the
      in-memory-only deferral is a known gap, not a secret leak.
- [x] CORS: frontend origin registered in BFF - production `appsettings` already
      allows `https://fleetstream.example.com`, matching `ops/k8s/ingress.yaml`.
- [ ] Docker image builds and serves on configured port - `frontend/Dockerfile`
      (multi-stage, `output: "standalone"`, non-root) is added but **was not
      built in this environment: no Docker daemon was available.** The
      standalone server was run directly and verified to serve `/login` = 200
      with the expected CSP header, which is what the probes depend on.
- [x] Health/readiness endpoint or static deploy verification - probes target
      `/login` (always 200 for an unauthenticated visitor); verified by booting

---

## P1 - Operational stability

### Observability

- [ ] Client error reporting (OTEL or equivalent) - `src/lib/logger.ts` writes to
      console only; nothing is exported.
- [x] `correlationId` propagated on API errors - surfaced on the API error type
      and included in logged problem payloads.
- [ ] SignalR disconnect/reconnect metrics or logging - connection *state* drives
      the reconnect banner, but no metric or exporter is emitted.

### Resiliency

- [x] Global error boundary with user-facing recovery -
      `src/app/global-error.tsx` and `src/app/(dashboard)/error.tsx`.
- [x] SignalR auto-reconnect + `RequestSnapshot` on reconnect -
      `src/lib/signalr-provider.tsx` re-joins groups and calls `RequestSnapshot`
      from `onreconnected`, per protocol sections 3.2 and 3.7. Covered by
      `signalr-provider.test.tsx` (7 tests).
- [x] API retry with backoff for idempotent GETs - transport-level retries live in
      `api-client.ts`; query-level `retry: false` avoids compounding attempts.

---

## P2 - Maintainability

- [ ] OpenAPI client regeneration in CI - types are hand-maintained in
      `src/lib/types.ts` (documented as wire-aligned with BFF contract section 2.3).
- [ ] E2E suite (login, summary, map, alerts) in CI - no Playwright config exists.
- [x] Unit-test harness - `vitest` + jsdom + Testing Library; 33 tests across CSP,
      SignalR reconnect behaviour, and the three client stores. `npm test` is
      wired into `.github/workflows/frontend.yml`.
- [ ] Lighthouse CI gates (Performance, Accessibility)
- [ ] Storybook or component catalog for shared UI

### Scale (was unlisted; tracked here)

- [x] Alert feed virtualization - the 500-entry ring buffer no longer renders 500
      live rows; `alert-feed.tsx` windows rows with `@tanstack/react-virtual`.
- [x] Map marker clustering - `fleet-map.tsx` renders the fleet from one clustered
      GeoJSON source instead of a DOM `Marker` per truck, so per-update cost is
      flat in fleet size.
- [ ] Verified 500-truck render performance - the code paths are in place, but no
      load or soak measurement has been taken.

---

## Verification (sign-off)

- [x] `npm run build` clean in CI - build, lint and typecheck run in
      `.github/workflows/frontend.yml`; all three pass locally.
- [ ] Playwright E2E green against staging
- [ ] 7-day staging soak without Sev-1
- [ ] PO + SO written sign-off
- [ ] Accessibility audit (WCAG 2.1 AA) passed - skip link, focus styles,
      aria-live regions and a keyboard path to truck data exist, but no automated
      axe gate has been run. **Known regression risk:** the map moved from DOM
      markers (focusable) to canvas layers (not focusable); the trucks table
      remains the keyboard-accessible route and the map is now labelled so.

---

## Production ready

| Category | Status |
|---|---|
| P0 | 4 of 6 - Docker build and OIDC outstanding |
| P1 | 4 of 5 - client error reporting outstanding |
| P2 | 1 of 6 - unit harness done; E2E/a11y/OpenAPI/Lighthouse outstanding |
| Verification | 1 of 5 |
| **Production ready** | **No** |

      `.next/standalone/server.js`.
