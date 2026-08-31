package main

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"os"
	"os/signal"
	"sync"
	"syscall"

	"github.com/fleetstream/streaming-engine/internal/circuit"
	"github.com/fleetstream/streaming-engine/internal/consumer"
	"github.com/fleetstream/streaming-engine/internal/dlq"
	"github.com/fleetstream/streaming-engine/internal/engine"
	"github.com/fleetstream/streaming-engine/internal/health"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/observability"
	"github.com/fleetstream/streaming-engine/internal/processor"
	"github.com/fleetstream/streaming-engine/internal/state"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/prometheus/client_golang/prometheus/promhttp"
	"github.com/sirupsen/logrus"
)

func main() {
	logger := logrus.New()
	logger.SetFormatter(&logrus.JSONFormatter{})
	logger.SetOutput(os.Stdout)
	logger.AddHook(observability.ContextHook{})

	cfg, err := config.Load()
	if err != nil {
		logger.WithError(err).Fatal("failed to load config")
	}
	logger.SetLevel(config.ParseLogLevel(cfg.Logging.Level))

	logger.WithFields(logrus.Fields{
		"consumer_brokers": cfg.Consumer.Brokers,
		"consumer_topic":   cfg.Consumer.Topic,
		"producer_topic":   cfg.Producer.Topic,
		"redis":            cfg.Redis.Addresses,
		"redis_cluster":    cfg.Redis.Cluster || len(cfg.Redis.Addresses) > 1,
		"dlq_topic":        cfg.DLQ.Topic,
		"metrics_port":     cfg.Metrics.Port,
		"admin_port":       cfg.Admin.Port,
	}).Info("starting FleetStream Streaming Engine")

	m := metrics.NewMetrics("fleetstream_streaming")

	stateStore, err := state.NewRedisStateStore(&cfg.Redis, logger, m)
	if err != nil {
		logger.WithError(err).Fatal("failed to connect to Redis")
	}

	dlqHandler, err := dlq.NewDLQHandler(&cfg.DLQ, logger, m)
	if err != nil {
		logger.WithError(err).Fatal("failed to create DLQ handler")
	}

	publisher, err := engine.NewSyncPublisher(&cfg.Producer)
	if err != nil {
		logger.WithError(err).Fatal("failed to create Kafka producer")
	}

	streamProcessor := processor.NewStreamProcessor(&cfg.Processing, logger, m)
	circuitBreaker := circuit.NewMultiCircuitBreaker()
	circuitBreaker.Register("kafka", 10, cfg.Shutdown.Timeout)
	circuitBreaker.Register("redis", 5, cfg.Shutdown.DrainTimeout)

	msgHandler := engine.NewHandler(cfg, logger, m, stateStore, publisher, dlqHandler, streamProcessor)

	eng := &streamingEngine{
		cfg: cfg, logger: logger, metrics: m, state: stateStore,
		publisher: publisher, dlq: dlqHandler, circuit: circuitBreaker,
	}

	kafkaConsumer, err := consumer.NewExactlyOnceConsumer(
		cfg.Consumer.Brokers, cfg.Consumer.Topic, cfg.Consumer.GroupID,
		&cfg.Consumer, logger, m, msgHandler.Handle,
	)
	if err != nil {
		logger.WithError(err).Fatal("failed to create Kafka consumer")
	}
	eng.consumer = kafkaConsumer

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	kafkaConsumer.Start(ctx)

	if cfg.Metrics.Enabled {
		eng.startMetricsServer()
	}
	eng.startAdminServer()

	logger.Info("streaming engine started")

	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
	<-sigChan

	cancel()
	eng.shutdown()
}

type streamingEngine struct {
	cfg           *config.Config
	logger        *logrus.Logger
	metrics       *metrics.Metrics
	state         *state.RedisStateStore
	consumer      *consumer.ExactlyOnceConsumer
	publisher     *engine.SyncPublisher
	dlq           *dlq.DLQHandler
	circuit       *circuit.MultiCircuitBreaker
	metricsServer *http.Server
	adminServer   *http.Server
}

func (e *streamingEngine) startMetricsServer() {
	mux := http.NewServeMux()
	mux.Handle(e.cfg.Metrics.Path, promhttp.Handler())

	e.metricsServer = &http.Server{
		Addr:    fmt.Sprintf(":%d", e.cfg.Metrics.Port),
		Handler: observability.Middleware(mux),
	}

	go func() {
		if err := e.metricsServer.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			e.logger.WithError(err).Error("metrics server error")
		}
	}()
}

func (e *streamingEngine) startAdminServer() {
	checker := &health.Checker{
		RedisPing:     e.state.Ping,
		ConsumerReady: e.consumer.IsReady,
		CircuitOK:     e.circuitsOK,
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/health/live", checker.Live)
	mux.HandleFunc("/health/ready", checker.Ready)

	e.adminServer = &http.Server{
		Addr:    fmt.Sprintf(":%d", e.cfg.Admin.Port),
		Handler: observability.Middleware(mux),
	}

	go func() {
		if err := e.adminServer.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			e.logger.WithError(err).Error("admin server error")
		}
	}()
}

func (e *streamingEngine) circuitsOK() bool {
	for _, name := range []string{"kafka", "redis"} {
		cb, ok := e.circuit.Get(name)
		if ok && !cb.IsAvailable() {
			return false
		}
	}
	return true
}

func (e *streamingEngine) shutdown() {
	e.logger.Info("starting graceful shutdown")

	ctx, cancel := context.WithTimeout(context.Background(), e.cfg.Shutdown.Timeout)
	defer cancel()

	var httpWG sync.WaitGroup
	if e.metricsServer != nil {
		httpWG.Add(1)
		go func() {
			defer httpWG.Done()
			_ = e.metricsServer.Shutdown(ctx)
		}()
	}
	if e.adminServer != nil {
		httpWG.Add(1)
		go func() {
			defer httpWG.Done()
			_ = e.adminServer.Shutdown(ctx)
		}()
	}

	if e.consumer != nil {
		_ = e.consumer.Close()
		if e.cfg.Shutdown.WaitForPending {
			e.consumer.WaitForInflight(e.cfg.Shutdown.DrainTimeout)
		}

		done := make(chan struct{})
		go func() {
			e.consumer.WaitForShutdown()
			close(done)
		}()

		select {
		case <-done:
		case <-ctx.Done():
			e.logger.Warn("consumer shutdown timed out")
		}
	}

	if e.publisher != nil {
		_ = e.publisher.Close()
	}
	if e.dlq != nil {
		_ = e.dlq.Close()
	}
	if e.state != nil {
		_ = e.state.Close()
	}

	httpWG.Wait()
	e.logger.Info("streaming engine shutdown complete")
}
