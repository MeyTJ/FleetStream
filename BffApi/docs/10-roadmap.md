# 10 — Roadmap

> **Status:** 🟡 Draft
> **Audience:** Engineering manager, reviewers
> **Goal:** Sequence the Phase 3 work into milestones with concrete exit criteria, risks, and a decision log.

---

## 10.1 Milestones

| ID    | Name                       | Exit criterion (verifiable)                                                                    | Duration |
| ----- | -------------------------- | ---------------------------------------------------------------------------------------------- | -------- |
| **M0** | Baseline                  | All four `.csproj` target `net10.0`; `dotnet build` is clean; `docker compose up` is healthy.    | 1 d      |
| **M1** | Core + Application        | `dotnet test FleetStream.UnitTests` green, coverage ≥ 85 % on Application, ≥ 90 % on Core.     | 3 d      |
| **M2** | Infrastructure adapters   | `dotnet test FleetStream.InfrastructureTests` green; Testcontainers Redis + Kafka pass.         | 3 d      |
| **M3** | Presentation: REST + SignalR | `dotnet test FleetStream.ApiTests` green; OpenAPI snapshot locked.                            | 4 d      |
| **M4** | Auth + Rate limiting + CORS | Security tests in §5.11 all pass.                                                              | 2 d      |
| **M5** | Persistence (EF Core)     | `ITruckRepository` swapped to EF Core; migration test passes.                                  | 2 d      |
| **M6** | Observability             | All metrics in §7.3 exposed; Grafana dashboard JSON committed.                                | 2 d      |
| **M7** | Load testing + perf tune  | k6 scenarios in §9.6 pass; image size < 250 MB.                                               | 3 d      |
| **M8** | Docs & hand-off           | All `docs/*.md` reach ✅ Final; `CHANGELOG.md` published; demo video recorded.                  | 1 d      |

**Total Phase 3 estimate:** ~21 working days (~ 4 weeks).

---

## 10.2 Phase 3 → Phase 4 contract

The dashboard (Phase 4) starts when the following are ✅ Final:

- `02-api-contract.md` is ✅ Final.
- `03-signalr-protocol.md` is ✅ Final.
- A tagged image `v1.0.0` of the BFF is pushed to `ghcr.io/fleetstream/bff`.
- The OpenAPI document at `https://bff.staging.fleetstream.example.com/swagger/v1/swagger.json` is reachable from the Phase 4 CI.

---

## 10.3 Risks

| Risk                                                                                          | Probability | Impact | Mitigation                                                              |
| --------------------------------------------------------------------------------------------- | ----------- | ------ | ----------------------------------------------------------------------- |
| Confluent.Kafka 2.6.x is not API-compatible with librdkafka 2.4.x on Alpine                    | Low         | High   | Pin in CI; test image build before merging M2.                          |
| Redis StackExchange.Redis 2.8.x has a known ACL bug with `CLIENT NO-EVICT` on Cluster 7.2     | Low         | Medium | Use Cluster 7.4; pin in compose.                                        |
| FluentValidation 11.10.x lacks a `RuleFor(...).Cascade(CascadeMode.Stop).NotEmpty()` for records | Low         | Low    | Use a custom validator; covered by a unit test.                         |
| A future `.NET 11` major lands mid-Phase 3                                                  | Low         | Medium | Plan the upgrade in M5 alongside EF Core; isolation is small.            |
| YARP 2.2.x has a regression in HTTP/3 when the upstream is HTTP/1.1                            | Medium      | Low    | Set `HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower`.      |
| SignalR backplane on Redis Cluster experiences re-sharding storms                              | Medium      | High   | Pre-warm the cluster; add a circuit breaker around the broadcaster.     |
| The `dev-token` endpoint accidentally ships to Production                                     | Low         | High   | Hard-disable in code; covered by a test in §5.11.                       |
| Phase 4 starts before the OpenAPI is locked                                                   | Medium      | Medium | OpenAPI snapshot in `Verify` is a CI gate; cannot be skipped.            |

---

## 10.4 Decision log

| #   | Date         | Decision                                                                                  | Status   | Rationale                                                                              |
| --- | ------------ | ----------------------------------------------------------------------------------------- | -------- | -------------------------------------------------------------------------------------- |
| D1  | 2026-08-29   | Phase 3 targets .NET 10 (current major).                                                  | Accepted | Active toolchain, current framework defaults, ecosystem alignment with the existing skeleton. |
| D2  | 2026-08-29   | JWT auth in Phase 3; cookie auth deferred.                                                | Accepted | Aligns with the dashboard's need to call the API from a non-cookied origin.            |
| D3  | 2026-08-29   | Use `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (10.x) as the SignalR backplane.     | Accepted | First-class ASP.NET Core support, no extra cluster to operate.                          |
| D4  | 2026-08-29   | Use `MediatR` 12.4.x for in-process use-cases.                                            | Accepted | Industry-standard; last free v12.                                                       |
| D5  | 2026-08-29   | Use `NSubstitute` over `Moq` in tests.                                                    | Accepted | License clarity; similar API.                                                          |
| D6  | 2026-08-29   | Use `NodaTime` for domain time.                                                            | Accepted | Eliminates `DateTime.Kind` ambiguity; `Instant` serializes cleanly.                     |
| D7  | 2026-08-29   | Defer EF Core adoption to M5.                                                             | Accepted | The Phase 3 system of record is Redis + Kafka; EF Core not on the hot path.            |
| D8  | 2026-08-29   | Use `Microsoft.FeatureManagement` 4.x for feature flags.                                  | Accepted | First-party; integrates with `IOptions`.                                               |
| D9  | 2026-08-29   | YARP stays in Phase 3; remove-or-keep review at end of M3.                                 | Open     | If Phase 4 uses YARP, keep; otherwise drop.                                            |
| D10 | 2026-08-29   | Cache TTLs: `fleet:summary` = 5 s, `truck:state:*` = 24 h, `trucks:online` set TTL = 24 h. | Accepted | Aligns with the Streaming Engine's state-TTL; review after load test.                  |

---

## 10.5 Definition of done (Phase 3)

- [ ] Every milestone in §10.1 has its exit criterion verified.
- [ ] All risks in §10.3 are at "Low" or "Medium-with-mitigation".
- [ ] All decision-log items are either "Accepted" or "Open and tracked".
- [ ] The BFF is running in `staging` and serving the dashboard's e2e tests for 7 consecutive days without a Sev-1.
- [ ] A blog post / case study is drafted describing the architecture, the build, and one non-trivial bug encountered along the way.

---

## 10.6 Acceptance criteria for this document

- [ ] Every milestone has a date assigned once M0 starts.
- [ ] Every Open question in [01-architecture.md §1.9](01-architecture.md) has a row in the decision log.
- [ ] Phase 3 exit criteria are reviewed at the end of each milestone and signed off in the PR description.
