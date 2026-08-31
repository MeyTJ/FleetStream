# FleetStream — Secrets Management

> **Scope:** All production applications and shared infrastructure  
> **Last updated:** 2026-08-31

---

## Principles

1. **Never commit secrets** — only `.env.example` (placeholders) is tracked; `.env.production` and `.env.*` are gitignored.
2. **Inject at runtime** — secrets arrive via environment variables or a platform secret store (Kubernetes Secrets, AWS Secrets Manager, Azure Key Vault, HashiCorp Vault).
3. **Fail fast in production** — `docker-compose.production.yml` requires `REDIS_PASSWORD` and JWT JWKS vars; missing values abort compose startup.
4. **Dev vs production** — local dev uses plaintext Kafka and optional Redis password; production enables Redis auth and Kafka TLS/SASL.

---

## Secret inventory

| Secret | Used by | Dev source | Production source |
|---|---|---|---|
| `REDIS_PASSWORD` | Redis, streaming-engine, BFF | Optional (production overlay) | K8s Secret / vault |
| `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_JWKS_URI` | BFF (production profile) | Defaults in compose | Identity provider config |
| `JWT_SIGNING_KEY` | BFF (dev profile only) | `.env` / compose default | **Not used in production** |
| `KAFKA_TLS_ENABLED`, `KAFKA_CA_CERT_PATH` | ingress-gateway, streaming-engine, BFF | `false` (plaintext) | `true` + mounted CA cert |
| `KAFKA_SASL_*` | All Kafka clients | Empty (no SASL) | Managed Kafka credentials |

---

## Local development

```bash
cp .env.example .env.production   # edit values

# Dev stack (symmetric JWT, optional Redis password):
export REDIS_PASSWORD=fleetstream-redis-dev-secret
docker compose -f docker-compose.yml -f docker-compose.production.yml --profile dev up -d

# Production profile (JWKS, required secrets):
docker compose -f docker-compose.yml -f docker-compose.production.yml --profile production up -d
```

---

## Kubernetes (recommended production)

Mount secrets as environment variables using the `__` separator for .NET config:

```yaml
env:
  - name: ConnectionStrings__Redis
    valueFrom:
      secretKeyRef: { name: fleetstream-secrets, key: redis-connection }
  - name: Jwt__JwksUri
    valueFrom:
      secretKeyRef: { name: fleetstream-secrets, key: jwt-jwks-uri }
  - name: REDIS_PASSWORD
    valueFrom:
      secretKeyRef: { name: fleetstream-secrets, key: redis-password }
  - name: KAFKA_TLS_ENABLED
    value: "true"
  - name: KAFKA_CA_CERT_PATH
    value: /etc/fleetstream/certs/kafka-ca.pem
  - name: KAFKA_SASL_MECHANISM
    valueFrom:
      secretKeyRef: { name: fleetstream-secrets, key: kafka-sasl-mechanism }
  - name: KAFKA_SASL_USERNAME
    valueFrom:
      secretKeyRef: { name: fleetstream-secrets, key: kafka-sasl-username }
  - name: KAFKA_SASL_PASSWORD
    valueFrom:
      secretKeyRef: { name: fleetstream-secrets, key: kafka-sasl-password }
volumeMounts:
  - name: kafka-ca
    mountPath: /etc/fleetstream/certs
    readOnly: true
volumes:
  - name: kafka-ca
    secret:
      secretName: fleetstream-kafka-ca
```

See also [BffApi deployment docs](../BffApi/docs/08-deployment.md) and [security docs](../BffApi/docs/05-security.md).

---

## Environment variable reference

| Variable | Service(s) | Description |
|---|---|---|
| `REDIS_PASSWORD` | Redis, streaming-engine, BFF | Redis AUTH password |
| `JWT_ISSUER` | BFF | JWT issuer claim validation |
| `JWT_AUDIENCE` | BFF | JWT audience claim validation |
| `JWT_JWKS_URI` | BFF | JWKS endpoint for RS256 validation |
| `JWT_SIGNING_KEY` | BFF (dev only) | Symmetric signing key ≥ 32 chars |
| `KAFKA_TLS_ENABLED` | ingress-gateway, streaming-engine, BFF | Enable TLS to Kafka brokers |
| `KAFKA_CA_CERT_PATH` | All Kafka clients | PEM CA bundle for broker verification |
| `KAFKA_TLS_INSECURE_SKIP_VERIFY` | All Kafka clients | Skip TLS verify (non-prod only) |
| `KAFKA_SASL_MECHANISM` | All Kafka clients | `PLAIN`, `SCRAM-SHA-256`, or `SCRAM-SHA-512` |
| `KAFKA_SASL_USERNAME` | All Kafka clients | SASL username |
| `KAFKA_SASL_PASSWORD` | All Kafka clients | SASL password |

Template: [`.env.example`](../.env.example)  
Production overlay: [`docker-compose.production.yml`](../docker-compose.production.yml)

---

## Rotation

| Secret | Rotation cadence | Notes |
|---|---|---|
| Redis password | 90 days | Update K8s Secret; rolling restart Redis clients |
| JWT signing keys | Per IdP policy | JWKS auto-refresh on BFF; no redeploy needed |
| Kafka SASL credentials | Per broker policy | Rolling restart of all three apps |

---

## Verification

Run the automated P0 verification script (build + optional runtime):

```bash
bash scripts/verify-production-readiness.sh          # build + unit tests
bash scripts/verify-production-readiness.sh --runtime  # + docker stack probes
```

CI: [`.github/workflows/verify-production-readiness.yml`](../.github/workflows/verify-production-readiness.yml)
