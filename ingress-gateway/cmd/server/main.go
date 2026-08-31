package main

import (
	"context"
	"fmt"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/fleetstream/ingress-gateway/internal/handlers"
	"github.com/fleetstream/ingress-gateway/internal/observability"
	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/config"
	telemetry "github.com/fleetstream/ingress-gateway/proto/gen/v1"
	"github.com/prometheus/client_golang/prometheus/promhttp"
	"github.com/sirupsen/logrus"
	"google.golang.org/grpc"
)

func main() {
	cfg, err := config.Load()
	if err != nil {
		logrus.WithError(err).Fatal("failed to load config")
	}

	logger, err := observability.NewLogger(cfg.Logging)
	if err != nil {
		logrus.WithError(err).Fatal("failed to configure logger")
	}

	logger.WithFields(logrus.Fields{
		"grpc_port":     cfg.Server.GRPCPort,
		"http_port":     cfg.Server.WebsocketPort,
		"metrics_port":  cfg.Metrics.Port,
		"kafka_brokers": cfg.Kafka.Brokers,
		"kafka_topic":   cfg.Kafka.Topic,
	}).Info("starting FleetStream Ingress Gateway")

	metrics := processors.NewMetrics("fleetstream_ingress")

	producer, err := processors.NewKafkaProducer(processors.KafkaProducerConfig{
		Brokers:        cfg.Kafka.Brokers,
		Topic:          cfg.Kafka.Topic,
		ClientID:       cfg.Kafka.ClientID,
		Compression:    cfg.Kafka.Compression,
		DropOnFull:     cfg.Backpressure.DropOnFull,
		MaxQueueDepth:  cfg.Backpressure.MaxQueueDepth,
		AlertThreshold: cfg.Backpressure.AlertThreshold,
		Logger:         logger,
	})
	if err != nil {
		logger.WithError(err).Fatal("failed to create kafka producer")
	}

	telemetryProcessor := processors.NewTelemetryProcessor(producer, logger, metrics)

	pool, err := processors.NewShardedPool(processors.PoolConfig{
		NumShards:       cfg.WorkerPool.Shards,
		WorkersPerShard: cfg.WorkerPool.WorkersPerShard,
		QueueSize:       cfg.WorkerPool.QueueSize,
		Logger:          logger,
		Metrics:         metrics,
		Processor:       telemetryProcessor,
	})
	if err != nil {
		logger.WithError(err).Fatal("failed to create worker pool")
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	pool.Start(ctx)

	grpcHandler := handlers.NewGRPCHandler(pool, metrics, logger)
	wsHandler := handlers.NewWebSocketHandler(pool, metrics, logger)
	httpHandler := handlers.NewHTTPHandler(pool, metrics, logger)
	healthHandler := handlers.NewHealthHandler(producer, pool, cfg.Backpressure)

	grpcServer, err := newGRPCServer(cfg.Server.GRPCPort, grpcHandler, logger)
	if err != nil {
		logger.WithError(err).Fatal("failed to listen for gRPC")
	}
	httpServer := newHTTPServer(cfg, wsHandler, httpHandler, healthHandler)
	var metricsServer *http.Server
	if cfg.Metrics.Enabled {
		metricsServer = newMetricsServer(cfg)
	}

	go serveGRPC(grpcServer.srv, grpcServer.lis, logger)
	go serveHTTP(httpServer, "HTTP/WebSocket", logger)
	if metricsServer != nil {
		go serveHTTP(metricsServer, "metrics", logger)
	}

	logger.Info("all servers started, waiting for shutdown signal")

	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
	<-sigChan

	logger.Info("shutdown initiated")
	gracefulShutdown(cfg, httpServer, metricsServer, grpcServer.srv, pool, producer, logger)
	logger.Info("shutdown complete")
}

type grpcListener struct {
	srv *grpc.Server
	lis net.Listener
}

func newGRPCServer(port int, handler *handlers.GRPCHandler, logger *logrus.Logger) (*grpcListener, error) {
	lis, err := net.Listen("tcp", fmt.Sprintf(":%d", port))
	if err != nil {
		return nil, err
	}
	srv := grpc.NewServer(
		grpc.ChainUnaryInterceptor(observability.UnaryServerInterceptor()),
		grpc.ChainStreamInterceptor(observability.StreamServerInterceptor()),
	)
	telemetry.RegisterTelemetryIngestionServer(srv, handler)
	logger.WithField("port", port).Info("gRPC server starting")
	return &grpcListener{srv: srv, lis: lis}, nil
}

func newHTTPServer(
	cfg *config.Config,
	wsHandler *handlers.WebSocketHandler,
	httpHandler *handlers.HTTPHandler,
	healthHandler *handlers.HealthHandler,
) *http.Server {
	mux := http.NewServeMux()
	mux.HandleFunc("/ws", wsHandler.HandleWebSocket)
	mux.HandleFunc("/ingest", httpHandler.ServeHTTP)
	mux.HandleFunc("/health/live", healthHandler.Live)
	mux.HandleFunc("/health/ready", healthHandler.Ready)
	mux.HandleFunc("/health", healthHandler.Live)

	return &http.Server{
		Addr:         fmt.Sprintf(":%d", cfg.Server.WebsocketPort),
		Handler:      observability.Middleware(mux),
		ReadTimeout:  cfg.Server.ReadTimeout,
		WriteTimeout: cfg.Server.WriteTimeout,
		IdleTimeout:  cfg.Server.IdleTimeout,
	}
}

func newMetricsServer(cfg *config.Config) *http.Server {
	path := cfg.Metrics.Path
	if path == "" {
		path = "/metrics"
	}
	mux := http.NewServeMux()
	mux.Handle(path, promhttp.Handler())
	return &http.Server{
		Addr:         fmt.Sprintf(":%d", cfg.Metrics.Port),
		Handler:      mux,
		ReadTimeout:  cfg.Server.ReadTimeout,
		WriteTimeout: cfg.Server.WriteTimeout,
		IdleTimeout:  cfg.Server.IdleTimeout,
	}
}

func serveGRPC(srv *grpc.Server, lis net.Listener, logger *logrus.Logger) {
	if err := srv.Serve(lis); err != nil && err != grpc.ErrServerStopped {
		logger.WithError(err).Error("gRPC server error")
	}
}

func serveHTTP(server *http.Server, name string, logger *logrus.Logger) {
	logger.WithField("addr", server.Addr).Infof("%s server starting", name)
	if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		logger.WithError(err).Errorf("%s server error", name)
	}
}

func gracefulShutdown(
	cfg *config.Config,
	httpServer *http.Server,
	metricsServer *http.Server,
	grpcServer *grpc.Server,
	pool *processors.ShardedPool,
	producer *processors.KafkaProducer,
	logger *logrus.Logger,
) {
	drainCtx, drainCancel := context.WithTimeout(context.Background(), cfg.Shutdown.DrainTimeout)
	defer drainCancel()

	if err := httpServer.Shutdown(drainCtx); err != nil {
		logger.WithError(err).Warn("http server shutdown error")
	}
	if metricsServer != nil {
		if err := metricsServer.Shutdown(drainCtx); err != nil {
			logger.WithError(err).Warn("metrics server shutdown error")
		}
	}
	grpcStopped := make(chan struct{})
	go func() {
		grpcServer.GracefulStop()
		close(grpcStopped)
	}()
	select {
	case <-grpcStopped:
	case <-drainCtx.Done():
		grpcServer.Stop()
	}

	shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), cfg.Shutdown.Timeout)
	defer shutdownCancel()
	if err := pool.Shutdown(shutdownCtx); err != nil {
		logger.WithError(err).Warn("worker pool shutdown error")
	}
	if err := producer.Close(); err != nil {
		logger.WithError(err).Warn("kafka producer close error")
	}
}
