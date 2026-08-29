package handlers

import (
	"encoding/json"
	"net/http"
	"sync"
	"time"

	"github.com/gorilla/websocket"
	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/sirupsen/logrus"
)

// WebSocketHandler handles WebSocket telemetry ingestion
type WebSocketHandler struct {
	pool    *processors.ShardedPool
	metrics *processors.Metrics
	logger  *logrus.Logger
}

// NewWebSocketHandler creates a new WebSocket handler
func NewWebSocketHandler(pool *processors.ShardedPool, metrics *processors.Metrics, logger *logrus.Logger) *WebSocketHandler {
	return &WebSocketHandler{
		pool:    pool,
		metrics: metrics,
		logger:  logger,
	}
}

// HandleWebSocket handles a WebSocket connection for telemetry ingestion
func (h *WebSocketHandler) HandleWebSocket(w http.ResponseWriter, r *http.Request) {
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		h.logger.WithError(err).Error("websocket upgrade failed")
		return
	}
	defer conn.Close()

	// Track connection
	h.metrics.ActiveWebsocketConnections.Inc()
	defer h.metrics.ActiveWebsocketConnections.Dec()

	conn.SetReadLimit(65536)
	conn.SetReadDeadline(time.Now().Add(60 * time.Second))
	conn.SetPongHandler(func(string) error {
		conn.SetReadDeadline(time.Now().Add(60 * time.Second))
		return nil
	})

	var wg sync.WaitGroup
	wg.Add(2)

	// Read goroutine
	go func() {
		defer wg.Done()
		for {
			_, message, err := conn.ReadMessage()
			if err != nil {
				if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
					h.logger.WithError(err).Warn("websocket read error")
				}
				break
			}

			// Parse and submit telemetry
			var payload models.TelemetryPayload
			if err := json.Unmarshal(message, &payload); err != nil {
				h.logger.WithError(err).Warn("failed to parse telemetry payload")
				continue
			}

			if err := h.pool.Submit(r.Context(), payload, "websocket"); err != nil {
				h.metrics.JobsDropped.WithLabelValues("websocket", err.Error()).Inc()
			}
		}
	}()

	// Write goroutine for heartbeats
	go func() {
		defer wg.Done()
		ticker := time.NewTicker(30 * time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-ticker.C:
				if err := conn.WriteMessage(websocket.PingMessage, nil); err != nil {
					return
				}
			}
		}
	}()

	wg.Wait()
}

// HTTPHandler handles HTTP REST telemetry ingestion
type HTTPHandler struct {
	pool    *processors.ShardedPool
	metrics *processors.Metrics
	logger  *logrus.Logger
}

// NewHTTPHandler creates a new HTTP handler
func NewHTTPHandler(pool *processors.ShardedPool, metrics *processors.Metrics, logger *logrus.Logger) *HTTPHandler {
	return &HTTPHandler{
		pool:    pool,
		metrics: metrics,
		logger:  logger,
	}
}

// ServeHTTP handles HTTP requests
func (h *HTTPHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	start := time.Now()

	if r.Method != http.MethodPost {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	var payload models.TelemetryPayload
	if err := json.NewDecoder(r.Body).Decode(&payload); err != nil {
		h.logger.WithError(err).Warn("failed to parse HTTP payload")
		http.Error(w, "Invalid JSON", http.StatusBadRequest)
		return
	}
	defer r.Body.Close()

	if err := h.pool.Submit(r.Context(), payload, "http"); err != nil {
		h.metrics.JobsDropped.WithLabelValues("http", err.Error()).Inc()
		w.WriteHeader(http.StatusServiceUnavailable)
		json.NewEncoder(w).Encode(map[string]interface{}{
			"error": err.Error(),
		})
		return
	}

	w.WriteHeader(http.StatusAccepted)
	json.NewEncoder(w).Encode(map[string]interface{}{
		"accepted":    true,
		"message_id":  payload.MessageID,
		"processed_at": time.Now().UnixMilli(),
	})

	h.metrics.ProcessingDuration.WithLabelValues("http").Observe(time.Since(start).Seconds())
}
