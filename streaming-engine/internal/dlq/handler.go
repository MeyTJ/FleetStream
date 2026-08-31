// Package dlq implements the Dead Letter Queue handler for failed messages.
package dlq

import (
	"context"
	"encoding/json"
	"sync"
	"sync/atomic"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/observability"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/kafkasecurity"
	"github.com/sirupsen/logrus"
)

// MessageMetadata contains metadata about a failed message
type MessageMetadata struct {
	OriginalTopic     string            `json:"original_topic"`
	OriginalPartition int32             `json:"original_partition"`
	OriginalOffset    int64             `json:"original_offset"`
	Timestamp         time.Time         `json:"timestamp"`
	Error             string            `json:"error"`
	RetryCount        int               `json:"retry_count"`
	Headers           map[string]string `json:"headers,omitempty"`
}

// DLQHandler handles messages that fail processing
type DLQHandler struct {
	producer sarama.AsyncProducer
	topic    string
	cfg      *config.DLQConfig
	logger   *logrus.Logger
	metrics  *metrics.Metrics

	retryMu    sync.Mutex
	retryCount map[string]int
	retryTimes map[string]time.Time
	closed     atomic.Bool
}

// NewDLQHandler creates a new DLQ handler
func NewDLQHandler(cfg *config.DLQConfig, logger *logrus.Logger, m *metrics.Metrics) (*DLQHandler, error) {
	saramaConfig := sarama.NewConfig()
	saramaConfig.Producer.Return.Successes = false
	saramaConfig.Producer.Return.Errors = true
	saramaConfig.Producer.Compression = sarama.CompressionSnappy
	if err := kafkasecurity.Apply(saramaConfig, kafkasecurity.FromEnv()); err != nil {
		return nil, err
	}

	h := &DLQHandler{
		topic:      cfg.Topic,
		cfg:        cfg,
		logger:     logger,
		metrics:    m,
		retryCount: make(map[string]int),
		retryTimes: make(map[string]time.Time),
	}

	if !cfg.Enabled {
		return h, nil
	}

	producer, err := sarama.NewAsyncProducer(cfg.Brokers, saramaConfig)
	if err != nil {
		return nil, err
	}
	h.producer = producer

	go h.handleErrors()

	return h, nil
}

// SendToDLQ sends a failed message to the dead letter queue
func (h *DLQHandler) SendToDLQ(ctx context.Context, msg *sarama.ConsumerMessage, err error) error {
	if !h.cfg.Enabled || h.producer == nil {
		h.logger.WithError(err).Warn("DLQ disabled, message dropped")
		return nil
	}

	metadata := MessageMetadata{
		OriginalTopic:     msg.Topic,
		OriginalPartition: msg.Partition,
		OriginalOffset:    msg.Offset,
		Timestamp:         time.Now(),
		Error:             err.Error(),
		RetryCount:        h.getRetryCount(msg.Key),
		Headers:           make(map[string]string),
	}

	for _, header := range msg.Headers {
		metadata.Headers[string(header.Key)] = string(header.Value)
	}

	metadataBytes, _ := json.Marshal(metadata)

	dlqMsg := &sarama.ProducerMessage{
		Topic: h.topic,
		Key:   sarama.ByteEncoder(msg.Key),
		Value: sarama.ByteEncoder(msg.Value),
		Headers: append(observability.KafkaHeaders(ctx), []sarama.RecordHeader{
			{Key: []byte("error"), Value: []byte(err.Error())},
			{Key: []byte("original_topic"), Value: []byte(msg.Topic)},
			{Key: []byte("original_offset"), Value: []byte(string(rune(msg.Offset)))},
			{Key: []byte("metadata"), Value: metadataBytes},
		}...),
	}

	select {
	case <-ctx.Done():
		h.metrics.DLQErrors.Inc()
		return ctx.Err()
	case h.producer.Input() <- dlqMsg:
		h.metrics.DLQMessages.WithLabelValues("sent").Inc()
		h.logger.WithField("topic", msg.Topic).
			WithField("offset", msg.Offset).
			WithField("error", err).
			Info("message sent to DLQ")
		return nil
	default:
		h.metrics.DLQErrors.Inc()
		h.logger.WithError(err).Error("failed to send to DLQ: channel full")
		return err
	}
}

// RecordFailure increments the retry counter and reports whether another attempt is allowed.
func (h *DLQHandler) RecordFailure(key []byte) bool {
	if !h.cfg.Enabled || h.cfg.RetryAttempts <= 0 || h.cfg.RetryBackoff <= 0 {
		return false
	}

	h.retryMu.Lock()
	defer h.retryMu.Unlock()

	keyStr := string(key)
	count := h.retryCount[keyStr] + 1
	h.retryCount[keyStr] = count
	h.retryTimes[keyStr] = time.Now()
	return count < h.cfg.RetryAttempts
}

// BackoffRemaining returns how long to wait before the next retry attempt.
func (h *DLQHandler) BackoffRemaining(key []byte) time.Duration {
	h.retryMu.Lock()
	defer h.retryMu.Unlock()

	keyStr := string(key)
	last, ok := h.retryTimes[keyStr]
	if !ok {
		return 0
	}
	elapsed := time.Since(last)
	if elapsed >= h.cfg.RetryBackoff {
		return 0
	}
	return h.cfg.RetryBackoff - elapsed
}

// ResetRetries clears retry state after successful processing.
func (h *DLQHandler) ResetRetries(key []byte) {
	h.retryMu.Lock()
	defer h.retryMu.Unlock()
	keyStr := string(key)
	delete(h.retryCount, keyStr)
	delete(h.retryTimes, keyStr)
}

// getRetryCount gets the retry count for a message
func (h *DLQHandler) getRetryCount(key []byte) int {
	h.retryMu.Lock()
	defer h.retryMu.Unlock()
	return h.retryCount[string(key)]
}

// handleErrors processes producer errors
func (h *DLQHandler) handleErrors() {
	for err := range h.producer.Errors() {
		h.metrics.DLQErrors.Inc()
		h.logger.WithError(err.Err).Error("DLQ producer error")
	}
}

// Close closes the DLQ handler
func (h *DLQHandler) Close() error {
	if !h.closed.CompareAndSwap(false, true) {
		return nil
	}
	if h.producer == nil {
		return nil
	}
	return h.producer.Close()
}

// GetStats returns DLQ statistics
func (h *DLQHandler) GetStats() DLQStats {
	h.retryMu.Lock()
	defer h.retryMu.Unlock()
	return DLQStats{
		PendingRetries: len(h.retryTimes),
	}
}

// DLQStats holds DLQ statistics
type DLQStats struct {
	PendingRetries int
}
