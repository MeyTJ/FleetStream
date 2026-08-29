# 04 — Data Model

> **Status:** 🟡 Draft
> **Audience:** Engineers, integrators
> **Goal:** Define the canonical entities, the wire DTOs, the Redis keyspace, and the Kafka topic contracts that the BFF consumes or produces.

---

## 4.1 Ownership map

| Object                    | Owner phase | BFF role                                       |
| ------------------------- | ----------- | ---------------------------------------------- |
| `Truck` (metadata)        | Phase 1     | Read-only cache (in-memory) for fast lookups.  |
| `TruckState` (live view)  | Phase 2     | Authoritative store, BFF reads & broadcasts.   |
| `TruckTelemetry` (sample) | Phase 2     | Read-only history, time-windowed queries.       |
| `Alert`                   | Phase 2     | Read + acknowledge, broadcasts ack.             |
| `Geofence`                | Phase 2     | Not exposed in Phase 3.                         |

The BFF **does not** persist these as the system of record. It is a **read-side projection** of phases 1 and 2, with the broadcast responsibility.

---

## 4.2 Domain entities (Core)

These live in `FleetStream.Core` and have **no** infrastructure dependencies.

```csharp
public sealed class Truck
{
    public string Id { get; init; } = default!;          // primary key, opaque
    public string Name { get; init; } = default!;
    public string LicensePlate { get; init; } = default!;
    public TruckStatus Status { get; init; } = TruckStatus.Active;
    public Instant CreatedAt { get; init; }
    public Instant UpdatedAt { get; init; }
}

public enum TruckStatus { Active, Maintenance, Retired }

public sealed class TruckState
{
    public string TruckId { get; init; } = default!;
    public Instant Timestamp { get; init; }
    public GeoCoordinate Position { get; init; } = default!;
    public double SpeedKmh { get; init; }
    public double EngineTemperatureCelsius { get; init; }
    public float FuelLevelPercent { get; init; }
    public bool IsMoving { get; init; }
    public bool IsOnline { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public double RiskScore { get; init; }
    public double TotalDistanceKm { get; init; }
    public int ViolationsCount { get; init; }
    public int AnomaliesCount { get; init; }
}

public readonly record struct GeoCoordinate(double Latitude, double Longitude)
{
    public static GeoCoordinate Of(double lat, double lon) =>
        new(Math.Clamp(lat, -90, 90), Math.Clamp(lon, -180, 180));
}

public enum RiskLevel { Low, Medium, High, Critical }

public sealed class TruckTelemetry
{
    public string TruckId { get; init; } = default!;
    public Instant EventTimestamp { get; init; }
    public Instant? ProcessedAt { get; init; }
    public GeoCoordinate Position { get; init; } = default!;
    public double SpeedKmh { get; init; }
    public double EngineTemperatureCelsius { get; init; }
    public float FuelLevelPercent { get; init; }
    public string? CountryCode { get; init; }
    public string? Region { get; init; }
    public string? City { get; init; }
    public string? Geohash { get; init; }
    public bool SpeedViolation { get; init; }
    public bool TempAnomaly { get; init; }
    public bool FuelLow { get; init; }
    public bool GeofenceViolation { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public double RiskScore { get; init; }
}

public sealed class Alert
{
    public string Id { get; init; } = default!;
    public string TruckId { get; init; } = default!;
    public AlertType AlertType { get; init; }
    public AlertSeverity Severity { get; init; }
    public string Message { get; init; } = default!;
    public Instant Timestamp { get; init; }
    public bool IsAcknowledged { get; init; }
    public string? AcknowledgedBy { get; init; }
    public Instant? AcknowledgedAt { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } =
        ImmutableDictionary<string, object>.Empty;
}

public enum AlertType
{
    SpeedViolation, TempAnomaly, FuelLow,
    GeofenceViolation, StaleData, RouteDeviation
}

public enum AlertSeverity { Info, Warning, Error, Critical }
```

> Time is represented by `NodaTime.Instant` (added as a dependency of `Core`). It eliminates the `DateTime.Kind` footgun and gives the Phase 4 frontend a clean `string` when serialized.

---

## 4.3 DTOs (Application)

DTOs are the **wire-shape**. They are produced by source-generated `System.Text.Json` contexts (no reflection) and exposed in `FleetStream.Application.Dtos`. They are 1:1 with the JSON schemas in [02-api-contract.md §2.3](02-api-contract.md).

```csharp
[JsonSerializable(typeof(FleetSummaryDto))]
[JsonSerializable(typeof(TruckDto))]
[JsonSerializable(typeof(TruckStateDto))]
[JsonSerializable(typeof(TelemetrySampleDto))]
[JsonSerializable(typeof(AlertDto))]
[JsonSerializable(typeof(Page<TruckStateDto>))]
[JsonSerializable(typeof(ProblemDetails))]
public partial class FleetStreamJsonContext : JsonSerializerContext { }
```

DTOs use `Instant`-style strings on the wire; they are never deserialized back into `Instant` on the client.

---

## 4.4 Redis keyspace

The BFF treats Redis as a **read-side cache** that mirrors the Streaming Engine's authoritative state. Keys are namespaced with prefixes and use snake_case for easy ops inspection.

| Key pattern                    | Type   | TTL                | Purpose                                |
| ------------------------------ | ------ | ------------------ | -------------------------------------- |
| `fleet:summary`                | string | 5 s                | Cached `FleetSummary` JSON.            |
| `truck:state:{truckId}`        | string | 24 h               | Latest `TruckState` JSON.              |
| `truck:state:{truckId}:ver`    | int    | 24 h               | Monotonic state version, used for ETag. |
| `trucks:online`                | set    | 24 h (sliding)     | `truckId` of every online truck.       |
| `trucks:moving`                | set    | 24 h (sliding)     | `truckId` of every moving truck.       |
| `trucks:at_risk`               | zset   | 24 h (sliding)     | `score = riskScore, member = truckId`. |
| `truck:telemetry:idx:{truckId}`| list   | 1 h, max 1,000     | Time-ordered `TelemetrySample` ids.    |
| `alert:active`                 | zset   | 7 d (rolling)      | `score = timestamp, member = alertId`. |
| `signalr:presence:{truckId}`   | int64  | 5 min              | `lastSeenUnixMs`.                      |
| `idempotency:{key}`            | string | 24 h               | Idempotency-Key cache.                 |

**Eviction / TTL discipline:**
- `truck:state:*` keys ride a 24 h TTL so dormant trucks are eventually evicted.
- `trucks:online` and `trucks:moving` are repopulated lazily on first read after TTL expiry.
- `OnlinePresenceSweeper` (hosted service, runs every 60 s) ZRANGEs trucks whose `lastSeenUnixMs < now - 5min` and ZREMs them from the online set, then broadcasts `OnPresenceChange(truckId, false)`.

**Cluster key tags (for Redis Cluster):**
- All `truck:*` keys use `{truckId}` hash tag to co-locate per-truck keys on one slot.

**Lua / pipelining hot paths:**
- `UpdateStateAsync` uses a single batch (no Lua) of 4 commands: `SET` state, `SET` version, `ZADD` at_risk (NX), `SADD` online.
- `GetOnlineCountAsync` uses `SCARD` for O(1) lookup.

---

## 4.5 Kafka topics

The BFF **consumes** two topics and **produces none** in Phase 3.

### `fleet.telemetry.processed` (consume)

- **Partitions:** 12 (matches the Streaming Engine's default).
- **Key:** `truckId` (string). Guarantees per-truck ordering.
- **Compression:** `zstd`.
- **Acks:** `all` (set by the producer in Phase 2; BFF is a consumer).
- **Consumer group:** `fleetstream-bff-telemetry`.
- **Schema (JSON, matches `ProcessedTelemetry` in Phase 2):**

```jsonc
{
  "truck_id": "TAC-00112",
  "message_id": "01JBMZ4K5N8…",
  "event_timestamp": 1756467296789,
  "processed_at": 1756467296900,
  "latitude": 37.7749,
  "longitude": -122.4194,
  "engine_temperature_celsius": 88.1,
  "speed_kmh": 52.4,
  "fuel_level_percent": 64.8,
  "diagnostic_codes": [],
  "source": "ingest-grpc",
  "country_code": "US",
  "region": "California",
  "city": "San Francisco",
  "geohash": "9q8yyz8xk6",
  "speed_violation": false,
  "temp_anomaly": false,
  "fuel_low": false,
  "geofence_violation": false,
  "distance_from_last_km": 0.42,
  "time_since_last_sec": 7,
  "average_speed_kmh": 47.3,
  "max_speed_kmh": 64.0,
  "idle_time_sec": 0,
  "risk_score": 12.5,
  "risk_level": "low",
  "processing_version": 2,
  "processing_duration_ms": 3
}
```

### `fleet.alerts` (consume)

- **Partitions:** 6.
- **Key:** `truckId`.
- **Consumer group:** `fleetstream-bff-alerts`.
- **Schema (JSON):**

```jsonc
{
  "id": "alert-7b3a-…",
  "truck_id": "TAC-00112",
  "alert_type": "SpeedViolation",
  "severity": "Error",
  "message": "Truck TAC-00112 reported speed 142 km/h (limit 100).",
  "timestamp": 1756467301103,
  "is_acknowledged": false,
  "metadata": { "speed_kmh": 142, "limit_kmh": 100, "geohash": "9q8yyz" }
}
```

### Offsets, commits, idempotency

- The BFF uses `EnableAutoCommit = false` and commits offsets in batches of 100 messages or every 5 s, whichever comes first.
- The consumer is wrapped in a `try / catch` that:
  1. Logs at `Warn` with `traceId`.
  2. Increments a `kafka_consumer_errors_total{topic}` counter.
  3. **Does not** re-raise — the message is sent to the **DLQ** topic `fleet.bff.dlq` with the original payload + failure metadata.
- The BFF is **at-least-once** with respect to Kafka; the per-truck state write is **idempotent** because the `UpdateStateAsync` is a `SET` overwrite keyed on `truckId` (latest-wins).

---

## 4.6 Migration & version notes

- `RiskLevel` is a 4-value enum on the wire (`low|medium|high|critical`); older payloads that used `Low|Medium|High|Critical` are normalized in a deserializer shim and the swagger document advertises the lowercase form.
- `TruckState.IsOnline` is computed by the BFF as `now - lastSeen < 5 min` if the streaming engine has not explicitly set it.
- The `processing_version` field in `fleet.telemetry.processed` is used to gate migration. A v2 payload MUST NOT be deserialized by a v1 BFF — the consumer returns `IncompatibleVersion` and DLQs.

---

## 4.7 Validation rules (FluentValidation)

Per request, the corresponding validator lives in `Application/Validation/`:

| DTO                   | Rule                                                                 |
| --------------------- | -------------------------------------------------------------------- |
| `TruckIdDto`          | `Id` not empty, ≤ 64 chars, matches `^[A-Za-z0-9\-_:.]+$`.           |
| `AcknowledgeAlertDto` | `AcknowledgedBy` not empty, ≤ 64 chars.                              |
| `PageRequestDto`      | `PageSize` 1..200 (trucks) / 1..500 (alerts); `Cursor` if present is base64url. |
| `TelemetryWindowDto`  | `From < To`; window ≤ 24 h.                                          |

**Failure response:** `422 Unprocessable Entity` with the standard `application/problem+json` envelope and an `errors` array per §2.7.

---

## 4.8 Cardinality & growth assumptions

| Series              | Items      | Growth / day | Retention                |
| ------------------- | ---------- | ------------ | ------------------------ |
| `truck:state:*`     | ≤ 12,000   | churn < 1 %  | 24 h TTL in Redis.       |
| `trucks:online`     | ≤ 12,000   | continuous   | 24 h sliding TTL.        |
| `trucks:at_risk`    | ≤ 500      | spiky        | 24 h sliding TTL.        |
| `alert:active`      | ≤ 50,000   | ~ 5 k / day  | 7 d rolling.             |
| `fleet.telemetry.processed` | 100 K / sec | 100 K / sec | Kafka retention 1 h.     |
| `fleet.alerts`      | ≤ 500 / sec | ≤ 500 / sec | Kafka retention 7 d.     |

---

## 4.9 Acceptance criteria for this document

- [ ] Every domain entity in `Core` is `sealed` and uses `NodaTime.Instant` for time.
- [ ] Every DTO in `Application/Dtos` is annotated for the source-generated `JsonSerializerContext`.
- [ ] Every Redis key used in `Infrastructure` appears in the §4.4 table.
- [ ] Kafka consumer is configured with `EnableAutoCommit = false` and commits in batches.
- [ ] A migration test in `FleetStream.InfrastructureTests` proves that a v2 `processing_version=2` payload is rejected by a v1 BFF (configurable via DI) and DLQ'd.
