// Package cmd provides the main entry point for the Ingress Gateway service.
package main

import (
	"context"
	"fmt"
	"net/http"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"github.com/fleetstream/ingress-gateway/internal/handlers"
	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/config"
	"github.com/prometheus/client_golang/prometheus/promhttp"
	"github.com/sirupsen/logrus"
)

func main() {
	logger := logrus.New()
	logger.SetFormatter(&logrus.JSONFormatter{TimestampFormat: time.RFC3339Nano})
	logger.SetOutput(os.Stdout)
	logger.SetLevel(logrus.InfoLevel)

	cfg := &config.Config{
		Server: config.ServerConfig{
			GRPCPort:      50051,
			WebsocketPort: 8080,
			ReadTimeout:   5 * time.Second,
			WriteTimeout:  10 * time.Second,
		},
		Kafka: config.KafkaConfig{
			Brokers:     []string{"localhost:9092"},
			Topic:       "fleet.telemetry.raw",
			Compression: "snappy",
		},
		WorkerPool: config.WorkerPoolConfig{
			Shards:          8,
			WorkersPerShard: 4,
			QueueSize:       10000,
		},
		Backpressure: config.BackpressureConfig{
			Enabled:        true,
			DropOnFull:     true,
			MaxQueueDepth:  100000,
			AlertThreshold: 0.8,
		},
		Metrics: config.MetricsConfig{
			Enabled: true,
			Port:   9090,
		},
		Shutdown: config.ShutdownConfig{
			Timeout:        30 * time.Second,
			WaitForPending: true,
		},
	}

	logger.Info("starting FleetStream Ingress Gateway")

	// Initialize components
	metrics := processors.NewMetrics("fleetstream_ingress")
	
	pool, err := processors.NewShardedPool(processors.PoolConfig{
		NumShards:       cfg.WorkerPool.Shards,
		WorkersPerShard: cfg.WorkerPool.WorkersPerShard,
		QueueSize:      cfg.WorkerPool.QueueSize,
		Logger:          logger,
		Metrics:         metrics,
	})
	if err != nil {
		logger.WithError(err).Fatal("failed to create worker pool")
	}

	ctx, cancel := context.WithCancel(context.Background())
	pool.Start(ctx)

	// Initialize handlers
	grpcHandler := handlers.NewGRPCHandler(pool, metrics, logger)
	wsHandler := handlers.NewWebSocketHandler(pool, metrics, logger)
	httpHandler := handlers.NewHTTPHandler(pool, metrics, logger)

	// Start servers
	go startGRPCServer(cfg.Server.GRPCPort, grpcHandler, logger)
	go startHTTPServer(cfg.Server.WebsocketPort, wsHandler, httpHandler, logger)
	go startMetricsServer(cfg.Metrics.Port, logger)

	logger.Info("all servers started, waiting for shutdown signal")

	// Wait for shutdown
	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
	<-sigChan

	logger.Info("shutdown initiated")
	cancel()
	pool.Shutdown(ctx)
	logger.Info("shutdown complete")
}

func startGRPCServer(port int, handler *handlers.GRPCHandler, logger *logrus.Logger) {
	// In production, this would start a gRPC server
	// For now, just log that it would start
	logger.WithField("port", port).Info("gRPC server would start here")
}

func startHTTPServer(port int, wsHandler *handlers.WebSocketHandler, httpHandler *handlers.HTTPHandler, logger *logrus.Logger) {
	http.HandleFunc("/ws", wsHandler.HandleWebSocket)
	http.HandleFunc("/ingest", httpHandler.ServeHTTP)
	http.HandleFunc("/health", healthHandler)

	server := &http.Server{
		Addr: fmt.Sprintf(":%d", port),
	}
	
	logger.WithField("port", port).Info("HTTP/WebSocket server starting")
	if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		logger.WithError(err).Error("HTTP server error")
	}
}

func startMetricsServer(port int, logger *logrus.Logger) {
	http.Handle("/metrics", promhttp.Handler())
	logger.WithField("port", port).Info("metrics server starting")
	http.ListenAndServe(fmt.Sprintf(":%d", port), nil)
}

func healthHandler(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	w.Write([]byte(`{"status":"healthy"}`))
}
