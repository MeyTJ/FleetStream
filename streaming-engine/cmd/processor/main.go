// Package cmd provides the main entry point for the Streaming Engine service.
package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/circuit"
	"github.com/fleetstream/streaming-engine/internal/consumer"
	"github.com/fleetstream/streaming-engine/internal/dlq"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/processor"
	"github.com/fleetstream/streaming-engine/internal/state"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/prometheus/client_golang/prometheus/promhttp"
	"github.com/sirupsen/logrus"
)

func main() {
	logger := logrus.New()
	logger.SetFormatter(&logrus.JSONFormatter{TimestampFormat: time.RFC3339Nano})
	logger.SetOutput(os.Stdout)
	logger.SetLevel(logrus.InfoLevel)

	cfg := config.DefaultConfig()
	logger.Info("starting FleetStream Streaming Engine")

	m := metrics.NewMetrics("fleetstream_streaming")

	stateStore, err := state.NewRedisStateStore(&cfg.Redis, logger, m)
	if err != nil {
		logger.WithError(err).Fatal("failed to connect to Redis")
	}

	dlqHandler, _ := dlq.NewDLQHandler(&cfg.DLQ, logger, m)

	saramaConfig := sarama.NewConfig()
	saramaConfig.Producer.Return.Errors = true
	saramaConfig.Producer.Compression = sarama.CompressionSnappy
	saramaConfig.Producer.RequiredAcks = sarama.WaitForAll
	saramaConfig.Producer.Idempotent = cfg.Producer.Idempotent

	producer, err := sarama.NewAsyncProducer(cfg.Producer.Brokers, saramaConfig)
	if err != nil {
		logger.WithError(err).Fatal("failed to create Kafka producer")
	}

	streamProcessor := processor.NewStreamProcessor(&cfg.Processing, logger, m)
	circuitBreaker := circuit.NewMultiCircuitBreaker()
	circuitBreaker.Register("kafka", 10, 30*time.Second)
	circuitBreaker.Register("redis", 5, 10*time.Second)

	engine := &streamingEngine{
		cfg: cfg, logger: logger, metrics: m, state: stateStore,
		producer: producer, processor: streamProcessor,
		dlq: dlqHandler, circuit: circuitBreaker,
	}

	messageHandler := engine.createMessageHandler()
	kafkaConsumer, err := consumer.NewExactlyOnceConsumer(
		cfg.Consumer.Brokers, cfg.Consumer.Topic, cfg.Consumer.GroupID,
		&cfg.Consumer, logger, m, messageHandler,
	)
	if err != nil {
		logger.WithError(err).Fatal("failed to create Kafka consumer")
	}
	engine.consumer = kafkaConsumer

	ctx, cancel := context.WithCancel(context.Background())
	kafkaConsumer.Start(ctx)

	go engine.handleProducerErrors()
	go engine.startMetricsServer()
	go engine.startHealthServer()

	logger.Info("streaming engine started")

	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
	<-sigChan

	cancel()
	engine.shutdown()
}

type streamingEngine struct {
	cfg       *config.Config
	logger    *logrus.Logger
	metrics   *metrics.Metrics
	state     *state.RedisStateStore
	consumer  *consumer.ExactlyOnceConsumer
	producer  sarama.AsyncProducer
	processor *processor.StreamProcessor
	dlq       *dlq.DLQHandler
	circuit   *circuit.MultiCircuitBreaker
}

func (e *streamingEngine) createMessageHandler() consumer.MessageHandler {
	return func(ctx context.Context, msg *sarama.ConsumerMessage) error {
		start := time.Now()
		var telemetry models.TelemetryPayload
		if err := json.Unmarshal(msg.Value, &telemetry); err != nil {
			return err
		}

		// Idempotency check
		if e.cfg.Idempotency.Enabled {
			isDup, _ := e.state.CheckDuplicate(ctx, telemetry.MessageID)
			if isDup && e.cfg.Idempotency.DropDuplicates {
				e.metrics.MessagesDropped.WithLabelValues("duplicate").Inc()
				return nil
			}
		}

		prevState, _ := e.state.GetTruckState(ctx, telemetry.TruckID)
		result := e.processor.Process(ctx, telemetry, prevState)
		if result.Error != nil {
			return result.Error
		}

		// Update state
		newState := &models.TruckState{
			TruckID: telemetry.TruckID, LastMessageID: telemetry.MessageID,
			LastUpdateTime: telemetry.Timestamp.UnixMilli(),
			Latitude: telemetry.Latitude, Longitude: telemetry.Longitude,
			LastSpeed: telemetry.SpeedKmh, LastEngineTemp: telemetry.EngineTemperatureCelsius,
			LastFuelLevel: telemetry.FuelLevelPercent, LastSeenTime: time.Now().UnixMilli(),
		}
		if prevState != nil {
			newState.TotalMessagesProcessed = prevState.TotalMessagesProcessed + 1
			newState.MaxSpeedToday = result.Processed.MaxSpeedKmh
		}
		e.state.SetTruckState(ctx, newState)

		// Publish
		processedData, _ := json.Marshal(result.Processed)
		e.producer.Input() <- &sarama.ProducerMessage{
			Topic: e.cfg.Producer.Topic, Key: sarama.StringEncoder(telemetry.TruckID),
			Value: sarama.ByteEncoder(processedData),
		}
		e.metrics.MessagesPublished.WithLabelValues(e.cfg.Producer.Topic).Inc()
		e.metrics.ProcessingDuration.WithLabelValues("total").Observe(time.Since(start).Seconds())
		return nil
	}
}

func (e *streamingEngine) handleProducerErrors() {
	for err := range e.producer.Errors() {
		e.metrics.PublishErrors.WithLabelValues("kafka").Inc()
		e.logger.WithError(err.Err).Error("producer error")
	}
}

func (e *streamingEngine) startMetricsServer() {
	http.Handle(e.cfg.Metrics.Path, promhttp.Handler())
	http.ListenAndServe(fmt.Sprintf(":%d", e.cfg.Metrics.Port), nil)
}

func (e *streamingEngine) startHealthServer() {
	http.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		json.NewEncoder(w).Encode(map[string]interface{}{
			"status": "healthy",
			"circuit_kafka": e.circuit.Get("kafka").GetState().String(),
		})
	})
	http.ListenAndServe(":9092", nil)
}

func (e *streamingEngine) shutdown() {
	e.logger.Info("starting graceful shutdown")
	if e.consumer != nil {
		e.consumer.Close()
		e.consumer.WaitForShutdown()
	}
	e.producer.AsyncClose()
	if e.state != nil {
		e.state.Close()
	}
	e.logger.Info("streaming engine shutdown complete")
}
