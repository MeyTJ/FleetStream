# Frontend — Production Readiness Checklist

> **Application:** `frontend` (Next.js)  
> **Audit date:** —  
> **Status:** Not started — Phase 4 pending BFF gate

Reference: [frontend/docs](../../frontend/docs/README.md) · BFF contracts in [BffApi/docs](../../BffApi/docs/README.md)

---

## Entry gate (before F0)

- [ ] BFF `02-api-contract.md` → ✅ Final
- [ ] BFF `03-signalr-protocol.md` → ✅ Final
- [ ] OpenAPI reachable from frontend CI
- [ ] Dev token flow verified against local BFF

---

## P0 — Block production deployment

- [ ] Production OIDC / JWT validation (no dev-token in prod build)
- [ ] CSP and security headers configured
- [ ] No secrets in client bundle (`NEXT_PUBLIC_*` audit)
- [ ] CORS: frontend origin registered in BFF
- [ ] Docker image builds and serves on configured port
- [ ] Health/readiness endpoint or static deploy verification

---

## P1 — Operational stability

### Observability

- [ ] Client error reporting (OTEL or equivalent)
- [ ] `correlationId` propagated on API errors
- [ ] SignalR disconnect/reconnect metrics or logging

### Resiliency

- [ ] Global error boundary with user-facing recovery
- [ ] SignalR auto-reconnect + `RequestSnapshot` on reconnect
- [ ] API retry with backoff for idempotent GETs

---

## P2 — Maintainability

- [ ] OpenAPI client regeneration in CI
- [ ] E2E suite (login, summary, map, alerts) in CI
- [ ] Lighthouse CI gates (Performance, Accessibility)
- [ ] Storybook or component catalog for shared UI

---

## Verification (sign-off)

- [ ] `npm run build` clean in CI
- [ ] Playwright E2E green against staging
- [ ] 7-day staging soak without Sev-1
- [ ] PO + SO written sign-off
- [ ] Accessibility audit (WCAG 2.1 AA) passed

---

## Production ready

| Category | Status |
|---|---|
| P0 | ⬜ |
| P1 | ⬜ |
| P2 | ⬜ |
| Verification | ⬜ |
| **Production ready** | **No** |
