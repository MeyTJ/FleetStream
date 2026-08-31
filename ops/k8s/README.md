# FleetStream — Kubernetes Deployment

Production manifests for the three FleetStream applications. Shared infrastructure (Redis, Kafka) is expected to be **managed services** — see [HA topology](../docs/platform/ha-topology.md).

## Prerequisites

- Kubernetes 1.28+
- Ingress controller with TLS (nginx, AWS ALB, etc.)
- Container images built and pushed to your registry
- Secrets created from [secret.yaml.example](./secret.yaml.example)

## Quick start

```bash
# 1. Create namespace and config
kubectl apply -f namespace.yaml
kubectl apply -f configmap.yaml

# 2. Create secrets (edit example first)
kubectl apply -f secret.yaml   # from secret.yaml.example

# 3. Deploy applications
kubectl apply -f ingress-gateway/
kubectl apply -f streaming-engine/
kubectl apply -f bff-api/

# 4. Expose via Ingress (TLS termination at edge)
kubectl apply -f ingress.yaml
```

## Image tags

Replace `fleetstream/<service>:latest` in each Deployment with your registry path, e.g.:

```yaml
image: ghcr.io/myorg/fleetstream-ingress-gateway:v1.0.0
```

CI builds locally; publish strategy is documented in [secrets-management](../docs/platform/secrets-management.md).

## Ports

| Service | Container ports | Purpose |
|---|---|---|
| ingress-gateway | 8080, 50051, 9090 | HTTP ingest, gRPC, metrics |
| streaming-engine | 8081, 9091 | Health/admin, metrics |
| bff-api | 8080 | REST + SignalR + metrics |

## Health probes

All Deployments use liveness and readiness probes aligned with docker-compose healthchecks. Readiness fails when Kafka or Redis dependencies are unreachable.

## TLS

External TLS is terminated at the Ingress resource — see [tls-termination.md](../docs/platform/tls-termination.md). Internal service-to-service traffic uses cluster networking; enable Kafka TLS/SASL via secrets for broker connections.

## Scaling

| Deployment | Min replicas | HPA metric |
|---|---|---|
| ingress-gateway | 2 | CPU 70%, custom ingest RPS |
| streaming-engine | 2 | Consumer lag, CPU 70% |
| bff-api | 2 | CPU 70%, SignalR connections |

HPA manifests are not included; configure per cluster capacity.

## Monitoring

Import Grafana dashboards from [`../grafana/`](../grafana/) and Prometheus rules from [`../prometheus/`](../prometheus/).
