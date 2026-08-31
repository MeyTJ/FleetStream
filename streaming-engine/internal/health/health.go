package health

import (
	"context"
	"encoding/json"
	"net/http"
)

type Checker struct {
	RedisPing     func(ctx context.Context) error
	ConsumerReady func() bool
	CircuitOK     func() bool
}

func (c *Checker) Live(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]string{"status": "alive"})
}

func (c *Checker) Ready(w http.ResponseWriter, r *http.Request) {
	reasons := make([]string, 0, 3)

	if c.ConsumerReady != nil && !c.ConsumerReady() {
		reasons = append(reasons, "kafka_consumer_not_connected")
	}
	if c.RedisPing != nil {
		if err := c.RedisPing(r.Context()); err != nil {
			reasons = append(reasons, "redis_unavailable")
		}
	}
	if c.CircuitOK != nil && !c.CircuitOK() {
		reasons = append(reasons, "circuit_breaker_open")
	}

	if len(reasons) > 0 {
		writeJSON(w, http.StatusServiceUnavailable, map[string]interface{}{
			"status":  "not_ready",
			"reasons": reasons,
		})
		return
	}

	writeJSON(w, http.StatusOK, map[string]string{"status": "ready"})
}

func writeJSON(w http.ResponseWriter, status int, payload interface{}) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(payload)
}
