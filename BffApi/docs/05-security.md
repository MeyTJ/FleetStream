# 05 — Security

> **Status:** 🟡 Draft
> **Audience:** Security, SRE, engineers
> **Goal:** Define the authentication, authorization, transport, input-handling, and threat-model posture of the BFF API.

---

## 5.1 Threat model (STRIDE summary)

| Threat                                | Mitigation in this spec                                                |
| ------------------------------------- | --------------------------------------------------------------------- |
| **S**poofing of identity              | JWT bearer with RS256 in prod, validated against a fixed issuer.      |
| **T**ampering of requests             | HTTPS only; bodies JSON-validated with FluentValidation.              |
| **R**epudiation                       | Every request gets a `correlationId` and a `traceId` in the logs.     |
| **I**nformation disclosure            | Standard `application/problem+json` envelopes — no internal stack traces. |
| **D**enial of service                 | Rate limiting (1000 rpm/IP), payload size cap, Kafka isolation.       |
| **E**levation of privilege            | Role claims checked on every controller; SignalR group joins check JWT. |

---

## 5.2 Authentication

- **Scheme:** `Microsoft.AspNetCore.Authentication.JwtBearer` 10.x.
- **Algorithm:**
  - Development: HS256 with a 256-bit secret from `Jwt:SigningKey` (env-var override).
  - Production: RS256 with the public key in `Jwt:SigningKey:PublicPem` (PEM) or JWKS at `Jwt:JwksUri`.
- **Issuer / Audience:**
  - `iss = "https://auth.fleetstream.example.com"` (configurable).
  - `aud = "fleetstream-bff"` (configurable).
- **Lifetime:** access tokens ≤ 60 min, refresh tokens handled by the auth provider (out of scope).
- **Clock skew:** 30 s allowed (`TokenValidationParameters.ClockSkew`).
- **Required claims:** see [02-api-contract.md §2.5](02-api-contract.md).
- **Anonymous endpoints:** `/api/v1/health/*`, `/metrics`, `/swagger*`, `/hubs/v1/fleet/negotiate`.

### Dev token endpoint

- `POST /api/v1/auth/dev-token` issues an HS256 token signed with the dev key.
- **Disabled in any non-Development environment** (`IHostEnvironment.IsDevelopment() == false` ⇒ 404).
- The endpoint exists so the Phase 4 dashboard can integrate without standing up an IdP.

---

## 5.3 Authorization

- **Role-based** with two roles in Phase 3: `fleet:reader`, `fleet:admin` (implies reader), `alerts:ack` (admin implies this).
- Controller actions are decorated with `[Authorize(Roles = "...")]` or a custom `FleetStreamAuthorizeAttribute` that consults the `roles` claim.
- **Group joins:** the `JoinTruckGroup(truckId)` hub method validates `truckId` against a regex and against the authenticated principal's allowed `truckIds` claim (if present). Future per-truck ACLs plug in here.

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/fleet")]
[FleetStreamAuthorize(Roles = "fleet:reader")]
public sealed class FleetController : ControllerBase { … }
```

---

## 5.4 Transport security

- **HTTPS only** in non-dev environments (`app.UseHsts()` if not Development).
- **HTTP/2** required; **HTTP/3** enabled when the listener is bound to a UDP socket.
- **HSTS:** `max-age=31536000; includeSubDomains; preload`.
- **HSTS preload eligible** — no mixed content, all subdomains on HTTPS.
- **TLS:** minimum TLS 1.2, prefer 1.3. Ciphers restricted to the Mozilla "intermediate" list.
- **CORS:** see [06-configuration.md](06-configuration.md); production allowed origins is a finite allow-list from `Cors:AllowedOrigins`.

---

## 5.5 Input validation

- All request bodies are deserialized via source-generated `System.Text.Json` and then validated by a `IValidator<T>` resolved by FluentValidation.
- A MediatR `ValidationBehavior<TRequest, TResponse>` runs validators and short-circuits with a 422 + `ProblemDetails` envelope when any rule fails.
- **Truck IDs** match `^[A-Za-z0-9\-_:.]+$` and are ≤ 64 chars (rejected with 400 otherwise).
- **Strings** have explicit `MaxLength` on the DTOs, which is enforced before the validator runs.
- **Numbers** have explicit range attributes (`Range`, `MinLength`/`MaxLength`).
- **Path traversal:** `truckId` route values are checked against the regex above; any other path-segment values are URL-decoded once and matched against an allow-list per route.
- **SQL injection:** not applicable (no SQL).
- **Deserialization bombs:** `System.Text.Json` has a default depth limit of 64; the BFF tightens it to 16 via `JsonSerializerOptions.MaxDepth = 16`.

---

## 5.6 Output hardening

- `Server: Kestrel` header is suppressed; `X-Powered-By` is not added.
- Default `Cache-Control: no-store` is set on `/api/v1/auth/*` and `/api/v1/fleet/alerts/*` (alerts may contain PII in `metadata`).
- The OpenAPI document does not leak internal types (e.g., EF Core entity types are not exposed as schemas).
- `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` are added by an `SecurityHeadersMiddleware`.

---

## 5.7 Rate limiting

- **Algorithm:** fixed window per IP, **1000 requests / minute** by default.
- **Per-endpoint overrides** in `appsettings.json`:

```json
"RateLimiting": {
  "Global":   { "PermitLimit": 1000, "Window": "00:01:00" },
  "PerRoute": {
    "/api/v1/fleet/summary":          { "PermitLimit": 10000, "Window": "00:01:00" },
    "/api/v1/auth/dev-token":          { "PermitLimit": 10,    "Window": "00:01:00" }
  }
}
```

- **Rejection:** 429 + `application/problem+json` with `Retry-After: <seconds>`.
- **Bypass:** the SignalR `/negotiate` endpoint and the health endpoints are exempt.

---

## 5.8 Secrets

- **No secrets in `appsettings.json`**. The file contains only the **key name**; values come from environment variables or a secrets manager.
- Production: secrets are mounted from the cluster's secret store (Kubernetes `Secret`, AWS Secrets Manager, etc.) into env vars with the `__` separator.
- The dev `Jwt:SigningKey` is read from `JWT__SIGNINGKEY` (env) or `dotnet user-secrets`.
- The container's filesystem is read-only at runtime; secrets are mounted via tmpfs.
- The container does **not** run as root (UID 10001 in the Dockerfile).
- No `kubectl exec`, no debug port exposed.

---

## 5.9 Audit logging

Every authenticated request logs (at `Information`):

```json
{
  "timestamp": "2026-08-29T12:34:56.789Z",
  "level":     "Information",
  "category":  "FleetStream.Audit",
  "message":   "Request",
  "method":    "POST",
  "path":      "/api/v1/fleet/alerts/alert-7b3a/acknowledge",
  "status":    200,
  "durationMs":42,
  "subject":   "user-42",
  "roles":     ["alerts:ack"],
  "correlationId":"c-1f3a",
  "traceId":   "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

The audit log is **append-only**, shipped to the same log sink as the operational logs but tagged `audit=true` so it can be filtered into a long-term store.

---

## 5.10 Incident response checklist

1. **Suspected compromised JWT.** Rotate the signing key (`Jwt:SigningKey`) in the secret store. Restart all BFF pods. All clients must re-authenticate.
2. **Redis breach.** Rotate the Redis `requirepass` and ACL; restart BFF; `redis-cli FLUSHDB` only after consulting the IR runbook.
3. **Kafka breach.** Revoke the BFF's client certificate; rotate `Kafka:Sasl:Password`; restart BFF.
4. **DoS.** Drop the offending IP in the WAF; the rate limiter will also kick in within 60 s.

---

## 5.11 Acceptance criteria for this document

- [ ] Anonymous endpoints return 401 with the correct `WWW-Authenticate: Bearer` header when probed without a token.
- [ ] Tokens signed with the **wrong** key are rejected with 401.
- [ ] Tokens expired by > 5 min are rejected with 401.
- [ ] A request whose body fails FluentValidation returns 422 + the `errors` array.
- [ ] The `dev-token` endpoint returns 404 in any non-Development environment.
- [ ] A test sending 1001 requests in 60 s to a single IP gets a 429 on request 1001.
