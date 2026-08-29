# 09 — Testing

> **Status:** 🟡 Draft
> **Audience:** Engineers, reviewers
> **Goal:** Define the test pyramid, tools, and acceptance criteria that prove the BFF API is production-ready.

---

## 9.1 Test pyramid

```
                ┌──────────────────────┐
                │   Load / Soak (k6)   │   < 1 % of suite
                └──────────┬───────────┘
                           │
                ┌──────────▼───────────┐
                │  E2E / Contract      │   ≈ 5 % of suite
                │  (WebApplicationFactory) │
                └──────────┬───────────┘
                           │
                ┌──────────▼───────────┐
                │  Integration         │   ≈ 15 % of suite
                │  (Testcontainers)    │
                └──────────┬───────────┘
                           │
                ┌──────────▼───────────┐
                │  Unit (xUnit + NSub) │   ≈ 80 % of suite
                │  Core + Application  │
                └──────────────────────┘
```

| Suite                                  | Speed target | Infra required |
| -------------------------------------- | ------------ | -------------- |
| `FleetStream.UnitTests`               | < 30 s total | none           |
| `FleetStream.InfrastructureTests`     | < 5 min total | Docker (Testcontainers) |
| `FleetStream.ApiTests`                | < 3 min total | Docker (Testcontainers) |
| `FleetStream.LoadTests`               | n/a (manual) | Docker         |

---

## 9.2 Unit tests (Core + Application)

- **Scope:** pure logic — entities, value objects, use-cases, validators, mappers.
- **Frameworks:** xUnit + FluentAssertions + NSubstitute + `Verify` for snapshot.
- **Time:** always via the injected `TimeProvider` — never `DateTime.UtcNow`.
- **Coverage target:** **≥ 85 %** line coverage on `Core` and `Application`.

Examples (representative, not exhaustive):

| Test                                                       | Validates                                                  |
| ---------------------------------------------------------- | ---------------------------------------------------------- |
| `Truck_Constructor_RejectsEmptyId`                         | Entity invariants.                                         |
| `FleetSummary_ComputesCorrectIdleCount`                    | Idle = online − moving.                                    |
| `GetTruckStateQuery_WhenTruckIdIsEmpty_Throws`             | Argument validation.                                       |
| `AcknowledgeAlertValidator_RejectsEmptyAcknowledgedBy`     | FluentValidation.                                          |
| `TruckStateDto_RoundTripsViaJson`                          | `JsonSerializerContext` produces identical bytes.          |
| `ValidationBehavior_ShortCircuitsOnError`                  | MediatR pipeline correctness.                              |
| `CachingBehavior_CachesPositiveResultForTtl`               | Cache hit on second call.                                  |

---

## 9.3 Integration tests (Infrastructure)

- **Scope:** `RedisTruckStateStore`, `RedisCacheService`, `KafkaTelemetryConsumer`, `KafkaAlertConsumer`.
- **Framework:** xUnit + FluentAssertions + **Testcontainers.Redis** + **Testcontainers.Kafka**.
- **Lifecycle:** one container per test class (`IAsyncLifetime`); per-test cleanup via key prefix flush.
- **Network:** containers run on the host network; tests get a `IConnectionMultiplexer` pointed at the container.

Examples:

| Test                                                            | Validates                                                     |
| --------------------------------------------------------------- | ------------------------------------------------------------- |
| `RedisTruckStateStore_SetAndGet_RoundTrips`                     | `SetStateAsync` → `GetStateAsync`.                            |
| `RedisTruckStateStore_GetOnlineCount_ReflectsAddedTrucks`       | Set cardinality.                                              |
| `RedisCacheService_GetAsync_OnMissingKey_ReturnsNull`          | Cache miss path.                                              |
| `RedisCacheService_GetAsync_OnRedisDown_ReturnsNull`            | Graceful degradation.                                         |
| `KafkaTelemetryConsumer_ConsumesMessageAndUpdatesState`         | End-to-end: produce → consume → Redis update.                 |
| `KafkaTelemetryConsumer_CommitsOffsetsInBatches`                | Offset commit semantics.                                      |
| `KafkaTelemetryConsumer_OnPoisonMessage_Dlqs`                   | DLQ contract.                                                 |
| `KafkaTelemetryConsumer_OnIncompatibleVersion_Dlqs`            | Version-mismatch handling.                                   |

---

## 9.4 API tests (WebApplicationFactory)

- **Scope:** the full HTTP/WS surface from a black-box perspective.
- **Framework:** `Microsoft.AspNetCore.Mvc.Testing` + `Verify` (for OpenAPI snapshot).
- **Auth:** tests issue real JWTs signed with a test-only key (`Jwt:Algorithm=HS256` in test config).

Examples:

| Test                                                                    | Validates                                          |
| ----------------------------------------------------------------------- | -------------------------------------------------- |
| `GetSummary_Anonymous_Returns401`                                       | Auth pipeline.                                     |
| `GetSummary_Authorized_Returns200AndSumsOnlineAndMoving`                | Happy path.                                        |
| `GetSummary_WhenRedisDown_Returns200WithSentinels`                      | Degraded mode.                                     |
| `GetTruckState_UnknownId_Returns404`                                    | Not-found.                                         |
| `AcknowledgeAlert_NonAdmin_Returns403`                                  | AuthZ.                                             |
| `AcknowledgeAlert_ValidRequest_Returns200AndBroadcastsViaHub`           | REST + SignalR end-to-end.                         |
| `SwaggerJson_IsValidOpenApi31Document`                                  | Schema validity (Verify snapshot).                 |
| `FleetHub_ConnectSubscribeReceiveSnapshot_Within250ms`                  | Real-time contract.                                |
| `RateLimiter_ExceedingPermit_Returns429`                                | DoS posture.                                       |
| `Cors_DisallowedOrigin_Returns403WithoutCorsHeader`                    | CORS contract.                                     |

---

## 9.5 Contract tests (cross-service)

- **Scope:** the BFF's expectations of upstream Kafka messages and the Ingress Gateway's gRPC contract.
- **Mechanism:** `PactNet` consumer-driven contracts.
- **Pacts checked-in:** `tests/pacts/bff-ingress.json`, `tests/pacts/bff-streaming-engine.json`.
- **Verified in CI** by a nightly job that spins up the producer side in a Testcontainer and replays the recorded interactions.

---

## 9.6 Load tests (k6)

- **Tool:** Grafana k6.
- **Scenarios (in `tests/load/scenarios/`):**

| Scenario                              | Profile                                          |
| ------------------------------------- | ------------------------------------------------ |
| `summary-cold.js`                     | 5,000 RPS, 5 min, 10 VUs, **no warm-up**.        |
| `summary-warm.js`                     | 5,000 RPS, 30 min, 50 VUs, 5 s warm-up.          |
| `trucks-paginated.js`                 | 500 RPS, 30 min, 25 VUs, scrolling cursor.       |
| `signalr-10k-clients.js`              | 10,000 WS clients, 100 broadcasts/sec sustained. |
| `kafka-restart-resilience.js`         | Kill Kafka mid-test, verify reconnect.           |

- **Pass criteria:** p99 < 300 ms, error rate < 0.1 %, no memory leak (heap stable after 5 min warmup).

---

## 9.7 CI pipeline

```
PR opened
  │
  ├─ build:   dotnet build -c Release
  ├─ format:  dotnet format --verify-no-changes
  ├─ analyze: dotnet build /p:TreatWarningsAsErrors=true
  ├─ unit:    dotnet test tests/FleetStream.UnitTests
  ├─ api:     dotnet test tests/FleetStream.ApiTests  (Testcontainers)
  ├─ policy:  threshold(coverage, ≥ 85 % on Core+Application)
  └─ publish: docker build -t ghcr.io/fleetstream/bff:pr-<n> .

Merge to main
  │
  ├─ build + unit + api (same as PR)
  ├─ integration: dotnet test tests/FleetStream.InfrastructureTests
  ├─ contract:  dotnet test tests/FleetStream.ContractTests
  ├─ image:    docker push ghcr.io/fleetstream/bff:<sha>
  └─ deploy:   argo-rollouts to staging (auto-promote)
```

---

## 9.8 Coverage thresholds

| Project                                  | Minimum line coverage |
| ---------------------------------------- | --------------------- |
| `FleetStream.Core`                       | 90 %                  |
| `FleetStream.Application`                | 85 %                  |
| `FleetStream.Infrastructure`             | 70 %                  |
| `FleetStream.Presentation`               | 60 %                  |
| **Overall**                              | **75 %**              |

Branches with `pragma: exclude` need a comment justifying the exclusion; CI rejects new excludes without one.

---

## 9.9 Acceptance criteria for this document

- [ ] `dotnet test` runs the full unit + api suite in < 5 min on a 4-core laptop.
- [ ] The CI pipeline fails on coverage < thresholds.
- [ ] A k6 run of `summary-warm.js` produces a report that satisfies §9.6.
- [ ] A `Verify` snapshot of the OpenAPI document is committed; intentional changes require `--verify` and a code review.
- [ ] No `Thread.Sleep` in any test (use `Task.Delay`, polling, or WireMock).
