# 02 — API Contract

> **Status:** 🟡 Draft
> **Audience:** Frontend, integrators, codegen
> **Goal:** Define the BFF's HTTP surface as an authoritative contract that Phase 4 codegen can target.

---

## 2.1 Conventions

- **Base URL (dev):** `http://localhost:8080`
- **Base URL (prod):** `https://bff.fleetstream.example.com`
- **Versioning:** URL segment, **major only** for breaking changes: `/api/v1/…`.
- **Content type:** `application/json; charset=utf-8` on writes; `application/problem+json` (RFC 7807) on errors.
- **Charset / encoding:** UTF-8, no BOM.
- **Timestamps:** ISO 8601 UTC strings with millisecond precision, e.g. `"2026-08-29T12:34:56.789Z"`.
- **IDs:** opaque strings, max 64 chars, URL-safe.
- **Pagination:** cursor-based (see §2.6). Page size cap: 200 for `trucks`, 500 for `alerts`.
- **Field naming:** `camelCase` on the wire; `null` means "not present", not "empty".
- **Empty arrays** are `[]`, never `null`.
- **Status codes:** 2xx success, 4xx client error, 5xx server error. 401 always wins over 403.
- **Idempotency:** all `GET` and `HEAD` are idempotent. `POST` (where present) require an `Idempotency-Key` header.
- **Compression:** `gzip` for bodies > 1 KiB; `br` if client sends `Accept-Encoding: br`.

---

## 2.2 Surface (v1)

| Method | Path                                          | Auth         | Description                                  |
| ------ | --------------------------------------------- | ------------ | -------------------------------------------- |
| GET    | `/api/v1/fleet/summary`                       | `fleet:reader` | Fleet-wide KPIs.                            |
| GET    | `/api/v1/fleet/trucks`                        | `fleet:reader` | Paginated truck list.                       |
| GET    | `/api/v1/fleet/trucks/{truckId}`              | `fleet:reader` | Truck metadata (name, plate, status).       |
| GET    | `/api/v1/fleet/trucks/{truckId}/state`        | `fleet:reader` | Latest processed `TruckState`.             |
| GET    | `/api/v1/fleet/trucks/{truckId}/telemetry`     | `fleet:reader` | Recent telemetry history.                  |
| GET    | `/api/v1/fleet/alerts`                        | `fleet:reader` | Active alerts.                             |
| POST   | `/api/v1/fleet/alerts/{id}/acknowledge`       | `alerts:ack`   | Acknowledge a single alert.               |
| POST   | `/api/v1/auth/dev-token`                      | none (dev only) | Issue a JWT for local dev.                |
| GET    | `/api/v1/health/live`                         | none         | Liveness.                                    |
| GET    | `/api/v1/health/ready`                        | none         | Readiness (Redis + Kafka).                  |
| WS     | `/hubs/v1/fleet`                              | JWT          | SignalR hub (see 03).                       |
| GET    | `/api/ingress/{**catch-all}`                  | inherits     | YARP → Ingress Gateway.                     |
| GET    | `/api/streaming/{**catch-all}`                | inherits     | YARP → Streaming Engine.                    |
| GET    | `/metrics`                                    | none         | Prometheus scrape.                          |
| GET    | `/swagger/v1/swagger.json`                    | none         | OpenAPI 3.1 document.                       |
| GET    | `/swagger`                                    | none         | Swagger UI.                                 |

**Routes that are reverse-proxied (YARP)** preserve the original path. The dashboard in Phase 4 may hit `/api/streaming/healthz` and it will be forwarded verbatim.

---

## 2.3 Schemas (OpenAPI / JSON Schema 2020-12)

```jsonc
// FleetSummary
{
  "type": "object",
  "required": ["totalTrucks","onlineTrucks","movingTrucks","idleTrucks","atRiskTrucks","averageSpeed","averageFuelLevel","generatedAt"],
  "properties": {
    "totalTrucks":      { "type": "integer", "minimum": 0 },
    "onlineTrucks":     { "type": "integer", "minimum": 0 },
    "movingTrucks":     { "type": "integer", "minimum": 0 },
    "idleTrucks":       { "type": "integer", "minimum": 0 },
    "atRiskTrucks":     { "type": "integer", "minimum": 0 },
    "averageSpeed":     { "type": "number",  "minimum": 0, "description": "km/h, fleet-wide mean" },
    "averageFuelLevel": { "type": "number",  "minimum": 0, "maximum": 100, "description": "percent" },
    "generatedAt":      { "type": "string",  "format": "date-time" }
  }
}

// Truck (metadata only; no live state)
{
  "type": "object",
  "required": ["id","name","licensePlate","status","createdAt","updatedAt"],
  "properties": {
    "id":           { "type": "string", "maxLength": 64 },
    "name":         { "type": "string", "maxLength": 128 },
    "licensePlate": { "type": "string", "maxLength": 32 },
    "status":       { "type": "string", "enum": ["Active","Maintenance","Retired"] },
    "createdAt":    { "type": "string", "format": "date-time" },
    "updatedAt":    { "type": "string", "format": "date-time" }
  }
}

// TruckState (the live "order book" view)
{
  "type": "object",
  "required": ["truckId","timestamp","latitude","longitude","speedKmh","engineTemperatureCelsius","fuelLevelPercent","isMoving","isOnline","riskLevel","riskScore"],
  "properties": {
    "truckId":                 { "type": "string" },
    "timestamp":               { "type": "string", "format": "date-time" },
    "latitude":                { "type": "number", "minimum": -90, "maximum": 90 },
    "longitude":               { "type": "number", "minimum": -180, "maximum": 180 },
    "speedKmh":                { "type": "number", "minimum": 0 },
    "engineTemperatureCelsius":{ "type": "number" },
    "fuelLevelPercent":        { "type": "number", "minimum": 0, "maximum": 100 },
    "isMoving":                { "type": "boolean" },
    "isOnline":                { "type": "boolean" },
    "riskLevel":               { "type": "string", "enum": ["Low","Medium","High","Critical"] },
    "riskScore":               { "type": "number", "minimum": 0, "maximum": 100 },
    "totalDistanceKm":         { "type": "number", "minimum": 0 },
    "violationsCount":         { "type": "integer", "minimum": 0 },
    "anomaliesCount":          { "type": "integer", "minimum": 0 }
  }
}

// TelemetrySample (point-in-time)
{
  "type": "object",
  "required": ["truckId","eventTimestamp","latitude","longitude","speedKmh","engineTemperatureCelsius","fuelLevelPercent"],
  "properties": {
    "truckId":                 { "type": "string" },
    "eventTimestamp":          { "type": "string", "format": "date-time" },
    "processedAt":             { "type": "string", "format": "date-time" },
    "latitude":                { "type": "number" },
    "longitude":               { "type": "number" },
    "speedKmh":                { "type": "number" },
    "engineTemperatureCelsius":{ "type": "number" },
    "fuelLevelPercent":        { "type": "number" },
    "countryCode":             { "type": "string", "nullable": true },
    "region":                  { "type": "string", "nullable": true },
    "city":                    { "type": "string", "nullable": true },
    "geohash":                 { "type": "string", "nullable": true },
    "speedViolation":          { "type": "boolean" },
    "tempAnomaly":             { "type": "boolean" },
    "fuelLow":                 { "type": "boolean" },
    "geofenceViolation":       { "type": "boolean" },
    "riskLevel":               { "type": "string", "enum": ["Low","Medium","High","Critical"] },
    "riskScore":               { "type": "number" }
  }
}

// Alert
{
  "type": "object",
  "required": ["id","truckId","alertType","severity","message","timestamp","isAcknowledged"],
  "properties": {
    "id":             { "type": "string" },
    "truckId":        { "type": "string" },
    "alertType":      { "type": "string", "enum": ["SpeedViolation","TempAnomaly","FuelLow","GeofenceViolation","StaleData","RouteDeviation"] },
    "severity":       { "type": "string", "enum": ["Info","Warning","Error","Critical"] },
    "message":        { "type": "string", "maxLength": 512 },
    "timestamp":      { "type": "string", "format": "date-time" },
    "isAcknowledged": { "type": "boolean" },
    "acknowledgedBy": { "type": "string", "nullable": true },
    "acknowledgedAt": { "type": "string", "format": "date-time", "nullable": true },
    "metadata":       { "type": "object", "additionalProperties": true }
  }
}

// Page<T>
{
  "type": "object",
  "required": ["items","pageSize","hasMore"],
  "properties": {
    "items":     { "type": "array" },
    "nextCursor":{ "type": "string", "nullable": true },
    "pageSize":  { "type": "integer", "minimum": 1, "maximum": 500 },
    "hasMore":   { "type": "boolean" }
  }
}
```

---

## 2.4 Endpoint details

### `GET /api/v1/fleet/summary`

- **Caching:** server-side, 5 s TTL, key `fleet:summary`. Cache invalidated by every processed-telemetry Kafka message and every alert ack.
- **Headers out:** `Cache-Control: public, max-age=2, stale-while-revalidate=10`, `ETag: W/"<sha1>"`.
- **Example response:**

```json
{
  "totalTrucks": 10234,
  "onlineTrucks": 9871,
  "movingTrucks": 7220,
  "idleTrucks":   2651,
  "atRiskTrucks": 17,
  "averageSpeed": 47.3,
  "averageFuelLevel": 64.8,
  "generatedAt": "2026-08-29T12:34:56.789Z"
}
```

- **Errors:** `401`, `503` (Redis down ⇒ degraded summary with `atRiskTrucks: -1` sentinel — see §2.7).

### `GET /api/v1/fleet/trucks`

- **Query params:**
  - `cursor` (opaque, base64url of last item key)
  - `pageSize` (default 50, max 200)
  - `status` filter (`Active`, `Maintenance`, `Retired`)
- **Ordering:** by `id ASC`. Stable.
- **Example response:** `{ "items": [ {Truck}, … ], "nextCursor": "eyJpZCI6IlRBQy0wMDAzNCJ9", "pageSize": 50, "hasMore": true }`.

### `GET /api/v1/fleet/trucks/{truckId}`

- **Errors:** `404` when unknown; `400` when `truckId` is not URL-safe or > 64 chars.

### `GET /api/v1/fleet/trucks/{truckId}/state`

- **Caching:** per-truck, 10 s TTL, key `truck:state:{truckId}`.
- **Errors:** `404` if no state has ever been recorded.

### `GET /api/v1/fleet/trucks/{truckId}/telemetry`

- **Query params:**
  - `from` (ISO 8601, inclusive, default: now − 1 h)
  - `to` (ISO 8601, exclusive, default: now)
  - `limit` (default 200, max 1000)
- **Errors:** `400` if `from >= to` or window > 24 h.

### `GET /api/v1/fleet/alerts`

- **Query params:** `cursor`, `pageSize` (default 100, max 500), `severity` (CSV), `truckId`, `onlyActive` (default `true`).
- **Order:** timestamp DESC.

### `POST /api/v1/fleet/alerts/{id}/acknowledge`

- **Body:** `{ "acknowledgedBy": "user-42" }` (required, non-empty).
- **Idempotency:** header `Idempotency-Key` (UUID) — replays return the original 200 within 24 h.
- **Effects:** updates `Alert` in the alert store (Phase 2), broadcasts `IFleetHubClient.OnAlert(alert)` to the `alerts` group.
- **Errors:** `404` unknown id, `409` already acknowledged, `422` validation.

### `POST /api/v1/auth/dev-token`  (dev only)

- **Disabled** when `ASPNETCORE_ENVIRONMENT=Production`.
- **Body:** `{ "subject": "alice", "roles": ["fleet:reader"] }`.
- **Response:** `{ "accessToken": "<jwt>", "expiresAt": "2026-08-29T13:34:56Z" }`.

---

## 2.5 AuthN/AuthZ headers

- All non-public endpoints require `Authorization: Bearer <jwt>`.
- JWT MUST validate against the configured issuer/signing key (see [05-security.md](05-security.md)).
- Required claims:
  - `sub` (string, non-empty)
  - `aud = "fleetstream-bff"`
  - `iss = "<configured issuer>"`
  - `exp` (now + ≤ 1 h)
  - `nbf` (now − 30 s, clock skew allowed)
  - `roles` (array of strings; one of `fleet:reader`, `fleet:admin`, `alerts:ack`)
- `fleet:admin` implies `fleet:reader` and `alerts:ack`.

---

## 2.6 Pagination

Cursor-based only. `offset`/`limit` pagination is **forbidden** because the `trucks` collection mutates with every Kafka message.

**Cursor format:** base64url(JSON) of `{"id": "<truckId-or-alertId>", "ts": "<iso8601>"}` for time-ordered sets, or `{"id": "<truckId>"}` for `trucks`.

**Rules:**
- First page omits `cursor`.
- `nextCursor` is `null` when `hasMore` is `false`.
- Clients MUST treat `nextCursor` as opaque; they MUST NOT parse it.
- The server MAY rotate the cursor format on a major version bump; minor changes preserve compatibility.

---

## 2.7 Error model (RFC 7807)

Every non-2xx response (except 401 from the auth pipeline itself) carries a `application/problem+json` body:

```json
{
  "type":       "https://fleetstream.example.com/errors/truck-not-found",
  "title":      "Truck not found",
  "status":     404,
  "detail":     "No truck with id 'TAC-9999' is registered.",
  "instance":   "/api/v1/fleet/trucks/TAC-9999",
  "traceId":    "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "correlationId": "c-1f3a",
  "errors": [
    { "pointer": "/truckId", "code": "NotFound", "message": "Unknown truck" }
  ]
}
```

**Standard error `type` URIs:**

| Status | `type`                                                  | When                                  |
| ------ | ------------------------------------------------------- | ------------------------------------- |
| 400    | `/errors/validation`                                    | Request body / params failed.         |
| 401    | `/errors/unauthorized`                                  | Missing / invalid / expired JWT.      |
| 403    | `/errors/forbidden`                                     | Token valid but role missing.         |
| 404    | `/errors/not-found`                                     | Resource unknown.                     |
| 409    | `/errors/conflict`                                      | ETag mismatch, duplicate ack.         |
| 422    | `/errors/business-rule`                                 | FluentValidation rule violation.      |
| 429    | `/errors/rate-limited`                                  | Fixed window exceeded.                |
| 503    | `/errors/dependency-unavailable`                        | Redis / Kafka unhealthy.              |

**Degraded responses:** when a downstream is unhealthy, the endpoint MAY return a `200` with sentinel values (see `summary` example) or a `503` with a `Retry-After: 5` header. The choice is documented per-endpoint in §2.4.

---

## 2.8 Versioning policy

- **Major** (URL segment) — breaking: field removal, type change, status code change, new required field.
- **Minor** (header `X-FleetStream-Api-Minor: 1`) — additive: new optional fields, new endpoints.
- **Patch** — never a contract change.
- A `Sunset` header announces deprecation at least **90 days** before a major bump.
- During deprecation the response is served on both `/api/v1` and `/api/v2`; the older one returns `Deprecation: true` and `Link: <…v2…>; rel="successor-version"`.

---

## 2.9 OpenAPI / Swagger

- Source of truth: the controller attributes + `Swashbuckle.AspNetCore` filters.
- Document URL: `GET /swagger/v1/swagger.json` (OpenAPI 3.1).
- UI: `GET /swagger` (Swagger UI 5.x, themed).
- The document includes a `Bearer` security scheme that targets the same JWT issuer as the API.
- Every operation has `OperationId` set so Orval/NSwag client generators are stable.

```jsonc
// openapi.json (excerpt)
{
  "openapi": "3.1.0",
  "info": {
    "title": "FleetStream BFF API",
    "version": "1.0.0",
    "contact": { "name": "Platform Team", "url": "https://fleetstream.example.com" }
  },
  "components": {
    "securitySchemes": {
      "bearer": { "type": "http", "scheme": "bearer", "bearerFormat": "JWT" }
    }
  },
  "security": [{ "bearer": [] }]
}
```

---

## 2.10 Acceptance criteria for this document

- [ ] `dotnet build` produces no warnings.
- [ ] `GET /swagger/v1/swagger.json` returns 200 and validates against the OpenAPI 3.1 meta-schema.
- [ ] A `Verify` snapshot test in `FleetStream.ApiTests` locks the OpenAPI document to a known-good shape.
- [ ] All 16 endpoints respond on a fresh `docker compose up` with the correct status codes per §2.4 and §2.7.
- [ ] A Postman/Orval-generated client compiles against `@phase4/dashboard` without manual fixups.
