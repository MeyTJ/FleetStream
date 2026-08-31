# FleetStream Frontend — Specification Suite

> **Phase 4 deliverable:** Production-grade specifications and phased implementation plan for the FleetStream dashboard.
>
> **Upstream dependency:** [BffApi](../BffApi/docs/README.md) (Phase 3) — REST + SignalR only.

---

## Document index

| # | Document | Scope | Status |
|---|---|---|---|
| 01 | [Implementation phases](01-implementation-phases.md) | Phased delivery from PO, SO, and Team Lead perspectives | 🟡 Draft |
| 02 | [Architecture](02-architecture.md) | App structure, routing, state, rendering strategy | 🔴 TBD |
| 03 | [BFF integration](03-bff-integration.md) | OpenAPI codegen, SignalR client, auth flow | 🔴 TBD |
| 04 | [UI specification](04-ui-specification.md) | Screens, components, interaction patterns | 🔴 TBD |
| 05 | [Testing & quality](05-testing-quality.md) | Unit, integration, E2E, a11y, performance gates | 🔴 TBD |
| 06 | [Deployment](06-deployment.md) | Docker, CDN, env config, CI/CD | 🔴 TBD |

---

## How to read this suite

1. **Product Owner / stakeholders** — start with [01-implementation-phases.md § Product Owner](01-implementation-phases.md#product-owner-po-perspective).
2. **Solution Owner / architect** — [01-implementation-phases.md § Solution Owner](01-implementation-phases.md#solution-owner-so-perspective), then BFF specs `02`, `03`, `05`.
3. **Team Lead / engineers** — [01-implementation-phases.md § Team Lead](01-implementation-phases.md#team-lead-perspective), then planned `02`, `03`, `05`.
4. **Platform / SRE** — planned `06`, plus [docs/platform/](../../docs/platform/) cross-cutting guides.

---

## Status legend

| Badge | Meaning |
|---|---|
| ✅ Final | Reviewed and stable for the current milestone |
| 🟡 Draft | Open issues; track in phase document decision log |
| 🔴 TBD | Not yet drafted |
