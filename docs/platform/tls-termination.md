# FleetStream — TLS Termination

> **Scope:** External TLS at the edge; Kafka TLS for broker connections  
> **Last updated:** 2026-08-31

---

## Principles

1. **Terminate TLS at the edge** — Load balancer or Kubernetes Ingress handles HTTPS/WSS for clients; pod-to-pod traffic stays on the cluster network.
2. **Kafka uses TLS + SASL** — All three applications connect to brokers with `KAFKA_TLS_ENABLED=true` and SASL credentials in production.
3. **No mTLS between services** — Service-to-service trust is network-segmentation based; mTLS is a future enhancement.

---

## External TLS (client-facing)

### Kubernetes Ingress (recommended)

The [ops/k8s/ingress.yaml](../../ops/k8s/ingress.yaml) manifest defines:

| Host | Backend | Purpose |
|---|---|---|
| `api.fleetstream.example.com` | bff-api:8080 | REST API + SignalR WebSocket |
| `ingest.fleetstream.example.com` | ingress-gateway:8080 | Telemetry HTTP ingest |

**TLS certificate:** Store in Kubernetes Secret `fleetstream-tls`:

```bash
kubectl create secret tls fleetstream-tls \
  --cert=fullchain.pem \
  --key=privkey.pem \
  -n fleetstream
```

**Cert-manager (automated):**

```yaml
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: fleetstream-tls
  namespace: fleetstream
spec:
  secretName: fleetstream-tls
  issuerRef:
    name: letsencrypt-prod
    kind: ClusterIssuer
  dnsNames:
    - api.fleetstream.example.com
    - ingest.fleetstream.example.com
```

### Cloud load balancers

| Platform | Component | Notes |
|---|---|---|
| AWS | ALB + ACM certificate | Target groups → EKS pod IPs or NodePort |
| Azure | Application Gateway + Key Vault cert | Backend pool → AKS services |
| GCP | HTTPS Load Balancer + managed cert | Backend services → GKE |

### SignalR WebSocket upgrade

Ensure the ingress/proxy allows WebSocket passthrough:

- `nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"` (set in ingress.yaml)
- Sticky sessions optional; Redis backplane not required at current scale

---

## Kafka TLS (broker connections)

Configured via environment variables (all three apps):

| Variable | Value (production) |
|---|---|
| `KAFKA_TLS_ENABLED` | `true` |
| `KAFKA_CA_CERT_PATH` | `/etc/fleetstream/certs/kafka-ca.pem` |
| `KAFKA_SASL_MECHANISM` | `SCRAM-SHA-512` |
| `KAFKA_SASL_USERNAME` | From secret store |
| `KAFKA_SASL_PASSWORD` | From secret store |

Mount the CA bundle as a Kubernetes Secret volume — see [secret.yaml.example](../../ops/k8s/secret.yaml.example).

Implementation:

- Go services: [ingress-gateway/pkg/kafkasecurity](../../ingress-gateway/pkg/kafkasecurity/security.go)
- BFF: [KafkaClientConfig.cs](../../BffApi/src/Infrastructure/Messaging/KafkaClientConfig.cs)

---

## Redis TLS (optional)

StackExchange.Redis supports TLS via connection string:

```
redis.example.com:6380,password=***,ssl=true,abortConnect=false
```

Enable when your managed Redis provider requires TLS. Update `ConnectionStrings__Redis` in K8s secrets.

---

## Verification checklist

| Check | Command / method |
|---|---|
| HTTPS redirect | `curl -I http://api.fleetstream.example.com` → 301/308 |
| Valid cert | `curl -v https://api.fleetstream.example.com/api/v1/health/live` |
| Ingest over TLS | `curl -X POST https://ingest.fleetstream.example.com/ingest ...` |
| Kafka TLS handshake | App logs on startup; readiness probe passes |
| Dev endpoints blocked | `POST /api/v1/auth/dev-token` → 404 in Production |

---

## Related

- [Secrets management](secrets-management.md)
- [HA topology](ha-topology.md)
- [Kubernetes deployment](../../ops/k8s/README.md)
