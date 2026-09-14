#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CI=false
RUNTIME=false
INGRESS_URL="${INGRESS_URL:-http://localhost:8080}"
BFF_URL="${BFF_URL:-http://localhost:8082}"
FRONTEND_URL="${FRONTEND_URL:-http://localhost:3000}"

for arg in "$@"; do
  case "$arg" in
    --ci) CI=true ;;
    --runtime) RUNTIME=true ;;
  esac
done

pass() { echo "PASS: $1"; }
fail() { echo "FAIL: $1" >&2; exit 1; }

json_field() {
  local payload="$1" field="$2"
  python3 -c "import json,sys; print(json.load(sys.stdin)['$field'])" <<<"$payload"
}

echo "=== FleetStream verification ==="

echo "--- ingress-gateway ---"
(cd "$ROOT/ingress-gateway" && go build -o /tmp/ingress-gateway ./cmd/server)
pass "ingress-gateway build"
(cd "$ROOT/ingress-gateway" && go test ./... -count=1)
pass "ingress-gateway unit tests"

echo "--- streaming-engine ---"
(cd "$ROOT/streaming-engine" && go build -o /tmp/streaming-engine ./cmd/processor)
pass "streaming-engine build"
(cd "$ROOT/streaming-engine" && go test ./... -count=1)
pass "streaming-engine unit tests"

echo "--- bff-api ---"
(cd "$ROOT/BffApi" && dotnet build FleetStream.sln -c Release -v q --nologo)
pass "bff-api build"
(cd "$ROOT/BffApi" && dotnet test FleetStream.sln -c Release --no-build -v q --nologo --filter "FullyQualifiedName!~Integration")
pass "bff-api tests"

echo "--- frontend ---"
if command -v npm >/dev/null 2>&1; then
  (cd "$ROOT/frontend" && npm ci --no-audit --no-fund)
  pass "frontend npm ci"
  (cd "$ROOT/frontend" && npm run lint)
  pass "frontend lint"
  (cd "$ROOT/frontend" && npm run typecheck)
  pass "frontend typecheck"
  (cd "$ROOT/frontend" && npm test)
  pass "frontend unit tests"
  (cd "$ROOT/frontend" && npm run build)
  pass "frontend build"
else
  echo "SKIP: npm not on PATH - frontend checks skipped"
fi

if [[ "$RUNTIME" == true ]]; then
  echo "--- docker images ---"
  docker build -q -t fleetstream/ingress-gateway:verify "$ROOT/ingress-gateway"
  pass "ingress-gateway docker build"
  docker build -q -t fleetstream/streaming-engine:verify "$ROOT/streaming-engine"
  pass "streaming-engine docker build"
  docker build -q -t fleetstream/bff-api:verify -f "$ROOT/BffApi/docker/Dockerfile" "$ROOT/BffApi"
  pass "bff-api docker build"
  # Build with a non-local origin: this is what the CSP connect-src must reflect,
  # otherwise the deployed dashboard cannot reach its own BFF.
  docker build -q -t fleetstream/frontend:verify \
    --build-arg NEXT_PUBLIC_API_BASE_URL="$BFF_URL" \
    --build-arg NEXT_PUBLIC_SIGNALR_HUB_URL="$BFF_URL/hubs/v1/fleet" \
    "$ROOT/frontend"
  pass "frontend docker build"

  echo "--- stack runtime ---"
  cd "$ROOT"
  export REDIS_PASSWORD="${REDIS_PASSWORD:-fleetstream-redis-dev-secret}"
  export JWT_SIGNING_KEY="${JWT_SIGNING_KEY:-this-is-a-dev-signing-key-at-least-32-chars!!}"

  docker compose -f docker-compose.yml -f docker-compose.production.yml --profile dev up -d --build \
    redis zookeeper kafka ingress-gateway streaming-engine bff-api-dev frontend

  wait_for() {
    local url="$1" name="$2" max="${3:-60}"
    for _ in $(seq 1 "$max"); do
      if curl -sf "$url" >/dev/null 2>&1; then
        pass "$name"
        return 0
      fi
      sleep 2
    done
    fail "$name (timeout)"
  }

  wait_for "$INGRESS_URL/health/live" "ingress-gateway liveness"
  wait_for "$INGRESS_URL/health/ready" "ingress-gateway readiness"
  wait_for "http://localhost:8081/health/live" "streaming-engine liveness"
  wait_for "http://localhost:8081/health/ready" "streaming-engine readiness"
  wait_for "$BFF_URL/api/v1/health/live" "bff-api liveness"
  wait_for "$BFF_URL/api/v1/health/ready" "bff-api readiness"
  wait_for "$FRONTEND_URL/login" "frontend serving login page"

  # The browser reaches the BFF directly, so its CSP must allow the exact origin
  # the image was built with. A localhost-only CSP here means an undeployable UI.
  frontend_csp="$(curl -s -D - -o /dev/null "$FRONTEND_URL/login" \
    | grep -i '^content-security-policy:' || true)"
  [[ -n "$frontend_csp" ]] && pass "frontend returns a Content-Security-Policy header"
  echo "$frontend_csp" | grep -qi 'localhost:8082' \
    && pass "frontend CSP allows the dev BFF origin" \
    || fail "frontend CSP does not allow the BFF origin: $frontend_csp"

  curl -sf "http://localhost:9090/metrics" | grep -q "fleetstream_ingress" && pass "ingress-gateway metrics"
  curl -sf "http://localhost:9091/metrics" | grep -q "fleetstream_streaming" && pass "streaming-engine metrics"
  curl -sf "$BFF_URL/metrics" | grep -q "fleetstream" && pass "bff-api metrics"

  code="$(curl -s -o /dev/null -w '%{http_code}' "$BFF_URL/api/v1/fleet/summary")"
  [[ "$code" == "401" ]] && pass "bff-api auth enforcement (401 without token)"

  code="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BFF_URL/api/v1/auth/dev-token" \
    -H "Content-Type: application/json" -d '{"subject":"verify"}')"
  [[ "$code" == "200" ]] && pass "bff-api dev-token endpoint reachable in dev profile"

  echo "--- full-stack E2E ---"
  token_response="$(curl -sf -X POST "$BFF_URL/api/v1/auth/dev-token" \
    -H "Content-Type: application/json" \
    -d '{"subject":"e2e-verify","roles":["admin"]}')"
  token="$(json_field "$token_response" accessToken)"
  [[ -n "$token" ]] && pass "dev-token issued"

  truck_id="TAC-00001"
  message_id="$(uuidgen 2>/dev/null || cat /proc/sys/kernel/random/uuid)"
  curl -sf -X POST "$INGRESS_URL/ingest" \
    -H "Content-Type: application/json" \
    -H "X-Correlation-Id: verify-$message_id" \
    -d "{\"truckId\":\"$truck_id\",\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"latitude\":40.7128,\"longitude\":-74.006,\"engineTemperatureCelsius\":90,\"speedKmh\":55,\"fuelLevelPercent\":75,\"messageId\":\"$message_id\"}" \
    >/dev/null
  pass "telemetry ingest accepted (truck=$truck_id)"

  e2e_ok=false
  for _ in $(seq 1 30); do
    state_code="$(curl -s -o /tmp/fleetstream-e2e-state.json -w '%{http_code}' \
      -H "Authorization: Bearer $token" \
      "$BFF_URL/api/v1/fleet/trucks/$truck_id/state")"
    if [[ "$state_code" == "200" ]]; then
      speed="$(json_field "$(cat /tmp/fleetstream-e2e-state.json)" speedKmh)"
      if python3 -c "import sys; sys.exit(0 if float('$speed') > 0 else 1)"; then
        e2e_ok=true
        break
      fi
    fi
    sleep 2
  done
  rm -f /tmp/fleetstream-e2e-state.json
  [[ "$e2e_ok" == true ]] && pass "full-stack E2E: ingest → process → BFF state read"
  [[ "$e2e_ok" == true ]] || fail "full-stack E2E: truck state not available after ingest"

  docker compose -f docker-compose.yml -f docker-compose.production.yml --profile dev down -v
  pass "stack teardown"
fi

echo "=== verification complete ==="
