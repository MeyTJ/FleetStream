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
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/sirupsen/logrus"
)

// MessageMetadata contains metadata about a failed message
type MessageMetadata struct {
	OriginalTopic     string            `json:"original_topic"`
	OriginalPartition int32             `json:"original_partition"`
	OriginalOffset    int64             `json:"original_offset"`
	Timestamp        time.Time         `json:"timestamp"`
	Error            string            `json:"error"`
	RetryCount       int               `json:"retry_count"`
	Headers          map[string]string `json:"headers,omitempty"`
}

// DLQHandler handles messages that fail processing
type DLQHandler struct {
	producer sarama.AsyncProducer
	topic    string
	cfg      *config.DLQConfig
	logger   *logrus.Logger
	metrics  *metrics.Metrics
	
	// Retry tracking
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

	producer, err := sarama.NewAsyncProducer(cfg.Brokers, saramaConfig)
	if err != nil {
		return nil, err
	}

	h := &DLQHandler{
		producer:  producer,
		topic:    cfg.Topic,
		cfg:      cfg,
		logger:   logger,
		metrics:  m,
		retryCount: make(map[string]int),
		retryTimes: make(map[string]time.Time),
	}

	// Start error handler
	go h.handleErrors()

	// Start retry processor
	go h.processRetries()

	return h, nil
}

// SendToDLQ sends a failed message to the dead letter queue
func (h *DLQHandler) SendToDLQ(ctx context.Context, msg *sarama.ConsumerMessage, err error) error {
	if !h.cfg.Enabled {
		h.logger.WithError(err).Warn("DLQ disabled, message dropped")
		return nil
	}

	metadata := MessageMetadata{
		OriginalTopic:     msg.Topic,
		OriginalPartition: msg.Partition,
		OriginalOffset:    msg.Offset,
		Timestamp:        time.Now(),
		Error:           err.Error(),
		RetryCount:       h.getRetryCount(msg.Key),
		Headers:          make(map[string]string),
	}

	// Add original headers
	for _, header := range msg.Headers {
		metadata.Headers[string(header.Key)] = string(header.Value)
	}

	// Serialize metadata
	metadataBytes, _ := json.Marshal(metadata)

	// Create DLQ message
	dlqMsg := &sarama.ProducerMessage{
		Topic: h.topic,
		Key:   msg.Key,
		Value: sarama.ByteEncoder(msg.Value),
		Headers: []sarama.RecordHeader{
			{Key: []byte("error"), Value: []byte(err.Error())},
			{Key: []byte("original_topic"), Value: []byte(msg.Topic)},
			{Key: []byte("original_offset"), Value: []byte(string(rune(msg.Offset)))},
			{Key: []byte("metadata"), Value: metadataBytes},
		},
	}

	select {
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

// ShouldRetry determines if a message should be retried
func (h *DLQHandler) ShouldRetry(key []byte) bool {
	count := h.getRetryCount(key)
	return count < h.cfg.RetryAttempts
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

// processRetries periodically checks for messages to retry
func (h *DLQHandler) processRetries() {
	ticker := time.NewTicker(h.cfg.RetryBackoff)
	defer ticker.Stop()

	for {
		select {
		case <-ticker.C:
			h.retryMu.Lock()
			now := time.Now()
			for key, lastTime := range h.retryTimes {
				if now.Sub(lastTime) >= h.cfg.RetryBackoff {
					count := h.retryCount[key]
					if count < h.cfg.RetryAttempts {
						h.retryCount[key] = count + 1
						h.logger.WithField("key", key).Info("message would be retried")
					}
					delete(h.retryTimes, key)
				}
			}
			h.retryMu.Unlock()
		}
	}
}

// Close closes the DLQ handler
func (h *DLQHandler) Close() error {
	if !h.closed.CompareAndSwap(false, true) {
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
