# FleetStream — Documentation

Cross-cutting and application-specific production readiness documentation derived from the 2026-08-31 codebase audit.

## Production readiness

| Document | Description |
|---|---|
| **[Production Readiness Report](production-readiness-report.md)** | Consolidated status for all applications and platform gaps |
| [Secrets management](platform/secrets-management.md) | Production secrets strategy, env vars, K8s mounting |
| [HA topology](platform/ha-topology.md) | Production Redis/Kafka HA patterns and scaling guidance |
| [TLS termination](platform/tls-termination.md) | Edge TLS, Kafka TLS, cert-manager setup |
| [Kubernetes manifests](../ops/k8s/README.md) | Production Deployment, Service, Ingress templates |
| [BFF API checklist](bff-api/production-readiness-checklist.md) | Per-app P0/P1/P2 + verification |
| [Ingress Gateway checklist](ingress-gateway/production-readiness-checklist.md) | Per-app P0/P1/P2 + verification |
| [Streaming Engine checklist](streaming-engine/production-readiness-checklist.md) | Per-app P0/P1/P2 + verification |
| [Frontend checklist](frontend/production-readiness-checklist.md) | Phase 4 dashboard — not started |

### Current status (2026-08-31)

All three backend applications — **Ingress Gateway**, **Streaming Engine**, and **BFF API** — have P0/P1/P2 remediation complete. **Frontend (Phase 4)** is planned; see [frontend/docs/01-implementation-phases.md](../frontend/docs/01-implementation-phases.md). **P0 and P1 platform gaps (CI, secrets, E2E, K8s, observability, TLS) are resolved.** Per-app runtime verification sign-off remains before production cutover. See the [consolidated report](production-readiness-report.md) for details.

## Related documentation

- [BffApi architecture & specs](../BffApi/docs/README.md)
- [DEVELOPMENT_PHASES.md](../DEVELOPMENT_PHASES.md)
- [Frontend README](../frontend/README.md) · [Implementation phases](../frontend/docs/01-implementation-phases.md)
- [Ingress Gateway README](../ingress-gateway/README.md)
- [Streaming Engine README](../streaming-engine/README.md)

## Priority legend

| Priority | Meaning |
|---|---|
| **P0** | Blocks production deployment |
| **P1** | Required for operational stability |
| **P2** | Important for maintainability and spec compliance |
