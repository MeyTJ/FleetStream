package engine

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/processor"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/sirupsen/logrus"
)

type StateStore interface {
	CheckDuplicate(ctx context.Context, messageID string) (bool, error)
	MarkProcessed(ctx context.Context, messageID string) error
	GetTruckState(ctx context.Context, truckID string) (*models.TruckState, error)
	SetTruckState(ctx context.Context, state *models.TruckState) error
}

type Publisher interface {
	Publish(ctx context.Context, topic, key string, value []byte) error
}

type DeadLetter interface {
	SendToDLQ(ctx context.Context, msg *sarama.ConsumerMessage, err error) error
}

type RetryTracker interface {
	RecordFailure(key []byte) bool
	BackoffRemaining(key []byte) time.Duration
	ResetRetries(key []byte)
}

type Handler struct {
	cfg       *config.Config
	logger    *logrus.Logger
	metrics   *metrics.Metrics
	state     StateStore
	publisher Publisher
	dlq       DeadLetter
	processor *processor.StreamProcessor
}

func NewHandler(
	cfg *config.Config,
	logger *logrus.Logger,
	m *metrics.Metrics,
	state StateStore,
	publisher Publisher,
	dlq DeadLetter,
	proc *processor.StreamProcessor,
) *Handler {
	return &Handler{
		cfg:       cfg,
		logger:    logger,
		metrics:   m,
		state:     state,
		publisher: publisher,
		dlq:       dlq,
		processor: proc,
	}
}

func (h *Handler) Handle(ctx context.Context, msg *sarama.ConsumerMessage) error {
	start := time.Now()
	for {
		err := h.process(ctx, msg, start)
		if err == nil {
			if rt, ok := h.dlq.(RetryTracker); ok {
				rt.ResetRetries(msg.Key)
			}
			return nil
		}

		if rt, ok := h.dlq.(RetryTracker); ok && rt.RecordFailure(msg.Key) {
			if d := rt.BackoffRemaining(msg.Key); d > 0 {
				select {
				case <-ctx.Done():
					return ctx.Err()
				case <-time.After(d):
				}
			}
			h.logger.WithError(err).
				WithField("topic", msg.Topic).
				WithField("partition", msg.Partition).
				WithField("offset", msg.Offset).
				Warn("retrying failed message")
			continue
		}

		h.sendDLQ(ctx, msg, err)
		return err
	}
}

func (h *Handler) process(ctx context.Context, msg *sarama.ConsumerMessage, start time.Time) error {
	var telemetry models.TelemetryPayload
	if err := json.Unmarshal(msg.Value, &telemetry); err != nil {
		h.metrics.MessagesDropped.WithLabelValues("unmarshal").Inc()
		return fmt.Errorf("unmarshal telemetry: %w", err)
	}

	if h.cfg.Idempotency.Enabled {
		isDup, err := h.state.CheckDuplicate(ctx, telemetry.MessageID)
		if err != nil {
			return fmt.Errorf("idempotency check: %w", err)
		}
		if isDup && h.cfg.Idempotency.DropDuplicates {
			h.metrics.MessagesDropped.WithLabelValues("duplicate").Inc()
			return nil
		}
	}

	prevState, err := h.state.GetTruckState(ctx, telemetry.TruckID)
	if err != nil {
		return fmt.Errorf("get truck state: %w", err)
	}

	result := h.processor.Process(ctx, telemetry, prevState)
	if result.Error != nil {
		return result.Error
	}

	newState := &models.TruckState{
		TruckID:        telemetry.TruckID,
		LastMessageID:  telemetry.MessageID,
		LastUpdateTime: telemetry.Timestamp.UnixMilli(),
		Latitude:       telemetry.Latitude,
		Longitude:      telemetry.Longitude,
		LastSpeed:      telemetry.SpeedKmh,
		LastEngineTemp: telemetry.EngineTemperatureCelsius,
		LastFuelLevel:  telemetry.FuelLevelPercent,
		LastSeenTime:   time.Now().UnixMilli(),
	}
	if prevState != nil {
		newState.TotalMessagesProcessed = prevState.TotalMessagesProcessed + 1
		newState.MaxSpeedToday = result.Processed.MaxSpeedKmh
	}
	if err := h.state.SetTruckState(ctx, newState); err != nil {
		return fmt.Errorf("set truck state: %w", err)
	}

	processedData, err := json.Marshal(result.Processed)
	if err != nil {
		return fmt.Errorf("marshal processed: %w", err)
	}
	if err := h.publisher.Publish(ctx, h.cfg.Producer.Topic, telemetry.TruckID, processedData); err != nil {
		return fmt.Errorf("publish: %w", err)
	}

	if h.cfg.Idempotency.Enabled {
		if err := h.state.MarkProcessed(ctx, telemetry.MessageID); err != nil {
			return fmt.Errorf("mark processed: %w", err)
		}
	}

	h.metrics.MessagesPublished.WithLabelValues(h.cfg.Producer.Topic).Inc()
	h.metrics.ProcessingDuration.WithLabelValues("total").Observe(time.Since(start).Seconds())
	return nil
}

func (h *Handler) sendDLQ(ctx context.Context, msg *sarama.ConsumerMessage, err error) {
	if h.dlq == nil {
		return
	}
	if dlqErr := h.dlq.SendToDLQ(ctx, msg, err); dlqErr != nil {
		h.logger.WithError(dlqErr).Error("failed to send message to DLQ")
	}
}
