// Package processors implements the Kafka producer with backpressure handling.
package processors

import (
	"context"
	"encoding/json"
	"errors"
	"sync/atomic"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/ingress-gateway/internal/observability"
	"github.com/fleetstream/ingress-gateway/pkg/kafkasecurity"
	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/sirupsen/logrus"
)

// KafkaProducer handles publishing telemetry to Kafka with backpressure support
type KafkaProducer struct {
	producer sarama.AsyncProducer
	brokers  []string
	topic    string
	logger   *logrus.Logger

	// Backpressure configuration
	dropOnFull     bool
	maxQueueDepth  int
	alertThreshold float64

	// Backpressure state
	backpressureState atomic.Int32
	queueDepth        atomic.Int64
	dropCount         atomic.Uint64
	errorCount        atomic.Uint64
}

// KafkaProducerConfig holds configuration for the Kafka producer
type KafkaProducerConfig struct {
	Brokers        []string
	Topic          string
	ClientID       string
	Compression    string
	DropOnFull     bool
	MaxQueueDepth  int
	AlertThreshold float64
	Logger         *logrus.Logger
}

// NewKafkaProducer creates a new Kafka producer with backpressure handling
func NewKafkaProducer(cfg KafkaProducerConfig) (*KafkaProducer, error) {
	saramaConfig := sarama.NewConfig()
	if cfg.ClientID != "" {
		saramaConfig.ClientID = cfg.ClientID
	}
	saramaConfig.Producer.Return.Successes = false
	saramaConfig.Producer.Return.Errors = true
	saramaConfig.Producer.Compression = getCompressionCodec(cfg.Compression)
	saramaConfig.Producer.Flush.Frequency = 10 * time.Millisecond
	saramaConfig.Producer.Flush.Messages = 100
	saramaConfig.Producer.Retry.Max = 3
	saramaConfig.Net.MaxOpenRequests = 5
	if err := kafkasecurity.Apply(saramaConfig, kafkasecurity.FromEnv()); err != nil {
		return nil, err
	}

	producer, err := sarama.NewAsyncProducer(cfg.Brokers, saramaConfig)
	if err != nil {
		return nil, err
	}

	p := &KafkaProducer{
		producer:       producer,
		brokers:        cfg.Brokers,
		topic:          cfg.Topic,
		logger:         cfg.Logger,
		dropOnFull:     cfg.DropOnFull,
		maxQueueDepth:  cfg.MaxQueueDepth,
		alertThreshold: cfg.AlertThreshold,
	}

	// Start error handler
	go p.handleErrors()

	return p, nil
}

// Publish publishes a normalized telemetry message to Kafka
func (p *KafkaProducer) Publish(ctx context.Context, telemetry models.NormalizedTelemetry) error {
	data, err := json.Marshal(telemetry)
	if err != nil {
		p.errorCount.Add(1)
		return err
	}

	headers := []sarama.RecordHeader{
		{Key: []byte("source"), Value: []byte(telemetry.Source)},
		{Key: []byte("timestamp"), Value: []byte(time.Now().Format(time.RFC3339))},
	}
	if id := observability.CorrelationID(ctx); id != "" {
		headers = append(headers, sarama.RecordHeader{Key: []byte("X-Correlation-Id"), Value: []byte(id)})
	}
	if tp := observability.Traceparent(ctx); tp != "" {
		headers = append(headers, sarama.RecordHeader{Key: []byte("traceparent"), Value: []byte(tp)})
	}

	msg := &sarama.ProducerMessage{
		Topic:   p.topic,
		Key:     sarama.StringEncoder(telemetry.TruckID),
		Value:   sarama.ByteEncoder(data),
		Headers: headers,
	}

	// Check backpressure before publishing
	if p.shouldDrop() {
		p.dropCount.Add(1)
		return errors.New("dropping message due to backpressure")
	}

	// Non-blocking send with channel
	select {
	case p.producer.Input() <- msg:
		p.queueDepth.Add(1)
		return nil
	default:
		if p.dropOnFull {
			p.dropCount.Add(1)
			return errors.New("dropping message: producer channel full")
		}
		p.producer.Input() <- msg
		return nil
	}
}

// shouldDrop determines if we should drop messages based on backpressure
func (p *KafkaProducer) shouldDrop() bool {
	depth := p.queueDepth.Load()
	maxDepth := int64(p.maxQueueDepth)
	if depth >= maxDepth {
		p.backpressureState.Store(2) // Critical
		return true
	}
	ratio := float64(depth) / float64(maxDepth)
	if ratio >= p.alertThreshold {
		p.backpressureState.Store(1) // Warning
	}
	return false
}

// handleErrors processes producer errors
func (p *KafkaProducer) handleErrors() {
	for err := range p.producer.Errors() {
		p.errorCount.Add(1)
		p.queueDepth.Add(-1)
		p.logger.WithError(err.Err).Error("kafka producer error")
	}
}

// GetStats returns current producer statistics
func (p *KafkaProducer) GetStats() ProducerStats {
	return ProducerStats{
		QueueDepth: p.queueDepth.Load(),
		DropCount:  p.dropCount.Load(),
		ErrorCount: p.errorCount.Load(),
	}
}

// ProducerStats holds producer statistics
type ProducerStats struct {
	QueueDepth int64
	DropCount  uint64
	ErrorCount uint64
}

func (p *KafkaProducer) Ready(ctx context.Context) error {
	cfg := sarama.NewConfig()
	cfg.Net.DialTimeout = 2 * time.Second
	cfg.Net.ReadTimeout = 2 * time.Second
	cfg.Net.WriteTimeout = 2 * time.Second
	cfg.Metadata.Timeout = 2 * time.Second
	if err := kafkasecurity.Apply(cfg, kafkasecurity.FromEnv()); err != nil {
		return err
	}

	errCh := make(chan error, 1)
	go func() {
		client, err := sarama.NewClient(p.brokers, cfg)
		if err != nil {
			errCh <- err
			return
		}
		defer client.Close()
		if err := client.RefreshMetadata(); err != nil {
			errCh <- err
			return
		}
		if len(client.Brokers()) == 0 {
			errCh <- errors.New("no kafka brokers reachable")
			return
		}
		errCh <- nil
	}()

	select {
	case <-ctx.Done():
		return ctx.Err()
	case err := <-errCh:
		return err
	}
}

func (p *KafkaProducer) Close() error {
	return p.producer.Close()
}

// getCompressionCodec converts compression string to Sarama codec
func getCompressionCodec(compression string) sarama.CompressionCodec {
	switch compression {
	case "snappy":
		return sarama.CompressionSnappy
	case "gzip":
		return sarama.CompressionGZIP
	case "lz4":
		return sarama.CompressionLZ4
	default:
		return sarama.CompressionNone
	}
}
