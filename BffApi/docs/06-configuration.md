# 06 — Configuration

> **Status:** 🟡 Draft
> **Audience:** Engineers, SRE
> **Goal:** Define every configuration key, its environment-variable override, and its default. This document is the source of truth for `appsettings.json`.

---

## 6.1 Source precedence (low → high)

1. Defaults baked into the code (lowest).
2. `appsettings.json` (committed).
3. `appsettings.{Environment}.json` (committed, environment-specific overrides).
4. Environment variables (e.g. `Redis__Configuration=...`).
5. Command-line args (`--Redis:Configuration=...`).
6. User secrets (Development only).
7. Azure App Configuration / HashiCorp Consul (optional, opt-in via `Features:RemoteConfig=true`).

---

## 6.2 Schema

```jsonc
{
  "Logging": {
    "LogLevel": {
      "Default":                  "Information",
      "Microsoft.AspNetCore":     "Warning",
      "FleetStream":              "Information",
      "FleetStream.Audit":        "Information"
    }
  },

  "AllowedHosts": "*",

  "Kestrel": {
    "Endpoints": {
      "Http":  { "Url": "http://0.0.0.0:8080" },
      "Https": { "Url": "https://0.0.0.0:8443" }
    }
  },

  "ConnectionStrings": {
    "Redis": "localhost:6379,abortConnect=false,connectTimeout=5000",
    "Kafka": "localhost:9092"
  },

  "Redis": {
    "Configuration": "localhost:6379,abortConnect=false,connectTimeout=5000",
    "TruckStateTtl": "24:00:00",
    "OnlineThreshold": "00:05:00",
    "CacheTtl":       "00:00:05",
    "KeyPrefix":      "fleetstream"
  },

  "Kafka": {
    "Brokers":          "localhost:9092",
    "ConsumerGroup":    "fleetstream-bff",
    "TelemetryTopic":   "fleet.telemetry.processed",
    "AlertTopic":       "fleet.alerts",
    "DlqTopic":         "fleet.bff.dlq",
    "CommitBatchSize":  100,
    "CommitIntervalMs": 5000
  },

  "SignalR": {
    "MaximumReceiveMessageSize":  32768,
    "KeepAliveInterval":          "00:00:15",
    "ClientTimeoutInterval":      "00:01:00",
    "BackpressureWindow":         "00:01:00",
    "StreamBufferCapacity":       1000,
    "Backplane": {
      "ChannelPrefix":            "FleetStream"
    }
  },

  "Jwt": {
    "Issuer":        "https://auth.fleetstream.example.com",
    "Audience":      "fleetstream-bff",
    "ClockSkewSec":  30,
    "Algorithm":     "RS256",
    "SigningKey":    "",          // dev only; prod uses JwksUri
    "JwksUri":       ""           // e.g. https://auth.../.well-known/jwks.json
  },

  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "https://fleetstream.example.com"
    ]
  },

  "RateLimiting": {
    "Global":   { "PermitLimit": 1000, "Window": "00:01:00" },
    "PerRoute": {
      "/api/v1/fleet/summary":   { "PermitLimit": 10000, "Window": "00:01:00" },
      "/api/v1/auth/dev-token":   { "PermitLimit": 10,    "Window": "00:01:00" }
    }
  },

  "Yarp": {
    "Routes": {
      "ingress-route":   { "ClusterId": "ingress-cluster",   "Match": { "Path": "/api/ingress/{**catch-all}"   } },
      "streaming-route": { "ClusterId": "streaming-cluster", "Match": { "Path": "/api/streaming/{**catch-all}" } }
    },
    "Clusters": {
      "ingress-cluster":   { "Destinations": { "default": { "Address": "http://ingress-gateway:50051"   } } },
      "streaming-cluster": { "Destinations": { "default": { "Address": "http://streaming-engine:8080"  } } }
    }
  },

  "OpenTelemetry": {
    "ServiceName":    "fleetstream-bff",
    "ServiceVersion": "1.0.0",
    "OtlpEndpoint":   "http://otel-collector:4317",
    "Prometheus":     { "Enabled": true, "Path": "/metrics" },
    "SamplingRatio":  1.0
  },

  "Features": {
    "DevToken":     true,    // disabled in Production
    "RemoteConfig": false
  }
}
```

---

## 6.3 Environment-variable overrides

Use the `__` (double underscore) separator to map JSON sections to env vars.

| Setting                                | Env var                                      |
| -------------------------------------- | -------------------------------------------- |
| `ConnectionStrings:Redis`              | `ConnectionStrings__Redis`                   |
| `ConnectionStrings:Kafka`              | `ConnectionStrings__Kafka`                   |
| `Redis:Configuration`                  | `Redis__Configuration`                       |
| `Redis:TruckStateTtl`                  | `Redis__TruckStateTtl`                       |
| `Kafka:Brokers`                        | `Kafka__Brokers`                             |
| `Kafka:ConsumerGroup`                  | `Kafka__ConsumerGroup`                       |
| `SignalR:ClientTimeoutInterval`        | `SignalR__ClientTimeoutInterval`             |
| `Jwt:SigningKey`                       | `Jwt__SigningKey`                            |
| `Jwt:JwksUri`                          | `Jwt__JwksUri`                               |
| `Cors:AllowedOrigins:0`                | `Cors__AllowedOrigins__0`                    |
| `OpenTelemetry:OtlpEndpoint`           | `OpenTelemetry__OtlpEndpoint`                |
| `OpenTelemetry:SamplingRatio`          | `OpenTelemetry__SamplingRatio`               |
| `Features:DevToken`                    | `Features__DevToken`                         |
| `Logging:LogLevel:Default`             | `Logging__LogLevel__Default`                 |
| `ASPNETCORE_ENVIRONMENT`               | (built-in)                                   |
| `ASPNETCORE_URLS`                      | (built-in)                                   |
| `DOTNET_EnableDiagnostics`             | (built-in)                                   |

---

## 6.4 Strongly typed options

Every config section is bound to an `IOptions<T>` POCO registered in `Program.cs`:

```csharp
public sealed class RedisOptions
{
    public string Configuration    { get; set; } = "localhost:6379";
    public TimeSpan TruckStateTtl  { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan OnlineThreshold{ get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CacheTtl       { get; set; } = TimeSpan.FromSeconds(5);
    public string  KeyPrefix       { get; set; } = "fleetstream";
}

public sealed class KafkaOptions
{
    public string Brokers         { get; set; } = "localhost:9092";
    public string ConsumerGroup   { get; set; } = "fleetstream-bff";
    public string TelemetryTopic  { get; set; } = "fleet.telemetry.processed";
    public string AlertTopic      { get; set; } = "fleet.alerts";
    public string DlqTopic        { get; set; } = "fleet.bff.dlq";
    public int    CommitBatchSize { get; set; } = 100;
    public int    CommitIntervalMs{ get; set; } = 5000;
}

public sealed class JwtOptions
{
    public string Issuer       { get; set; } = "";
    public string Audience     { get; set; } = "fleetstream-bff";
    public int    ClockSkewSec { get; set; } = 30;
    public string Algorithm    { get; set; } = "RS256";
    public string SigningKey   { get; set; } = "";
    public string JwksUri      { get; set; } = "";
}
```

All options classes:
- Use `public` setters and are validated at startup with `ValidateDataAnnotations().ValidateOnStart()`.
- Have a corresponding `IValidateOptions<T>` when validation needs cross-field logic (e.g., `Algorithm=HS256 ⇒ SigningKey required; Algorithm=RS256 ⇒ JwksUri required`).

---

## 6.5 Feature flags

A single `Features` section in Phase 3:

| Flag             | Default | Effect                                                                 |
| ---------------- | ------- | ---------------------------------------------------------------------- |
| `DevToken`       | true (dev) / false (prod) | When true, `POST /api/v1/auth/dev-token` is wired up. |
| `RemoteConfig`   | false   | When true, reads overrides from Azure App Configuration or Consul.     |
| `VerboseTracing` | false   | When true, every Kafka message produces a trace span.                  |

Flags are bound to `IFeatureManager` (Microsoft.FeatureManagement 4.x) and are evaluated **per-request** for `VerboseTracing` and **once at startup** for `DevToken`/`RemoteConfig`.

---

## 6.6 Local development

```bash
# appsettings.Development.json overrides the dev defaults.
cp src/Presentation/appsettings.json src/Presentation/appsettings.Development.json

# Required env-var to sign dev tokens.
export JWT__SIGNINGKEY=$(openssl rand -base64 32)
export ASPNETCORE_ENVIRONMENT=Development

# Start the stack.
docker compose -f docker/docker-compose.yml up -d
dotnet run --project src/Presentation
```

The dev signing key is **never** committed; it lives in `dotnet user-secrets` or an env var.

---

## 6.7 Production baseline

| Setting                                  | Value                                                |
| ---------------------------------------- | ---------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`                 | `Production`                                         |
| `Jwt:Algorithm`                          | `RS256`                                              |
| `Jwt:JwksUri`                            | Auth provider's JWKS endpoint                        |
| `Features:DevToken`                      | `false` (also hard-disabled in code as a safety net) |
| `Cors:AllowedOrigins`                    | Single production origin (no wildcards)              |
| `OpenTelemetry:SamplingRatio`            | `0.10` (10 % of traces)                              |
| `OpenTelemetry:OtlpEndpoint`             | Cluster OTLP collector                               |
| `RateLimiting:Global:PermitLimit`        | 1000 / minute / IP                                   |
| `Kestrel:Endpoints:Https:Url`            | `https://+:8443`                                     |

---

## 6.8 Acceptance criteria for this document

- [ ] `IOptions<T>` is registered for every section in §6.2.
- [ ] `ValidateOnStart()` is enabled for every `IOptions<T>` so misconfiguration fails at boot, not at first request.
- [ ] A unit test loads `appsettings.Test.json` and asserts the expected defaults.
- [ ] The dev `appsettings.json` contains no production secrets.
- [ ] The container logs the effective configuration (redacted) on startup so SRE can confirm what shipped.
