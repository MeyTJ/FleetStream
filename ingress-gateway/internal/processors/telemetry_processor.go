package processors

import (
	"context"
	"time"

	"github.com/fleetstream/ingress-gateway/internal/observability"
	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/sirupsen/logrus"
)

// TelemetryProcessor processes individual telemetry payloads
type TelemetryProcessor struct {
	kafkaProducer *KafkaProducer
	logger        *logrus.Logger
	metrics       *Metrics
}

// NewTelemetryProcessor creates a new telemetry processor
func NewTelemetryProcessor(producer *KafkaProducer, logger *logrus.Logger, metrics *Metrics) *TelemetryProcessor {
	return &TelemetryProcessor{
		kafkaProducer: producer,
		logger:        logger,
		metrics:       metrics,
	}
}

// Process processes a single telemetry payload
func (p *TelemetryProcessor) Process(ctx context.Context, payload models.TelemetryPayload, source string) (*models.NormalizedTelemetry, error) {
	// Normalize the payload
	normalized := p.normalize(payload, source)

	// Enrich with additional metadata
	p.enrich(normalized)

	// Publish to Kafka
	if err := p.kafkaProducer.Publish(ctx, *normalized); err != nil {
		observability.WithCtx(p.logger, ctx).WithError(err).
			WithField("message_id", payload.MessageID).
			Error("failed to publish telemetry to Kafka")
		return nil, err
	}

	// Update metrics
	if p.metrics != nil {
		p.metrics.JobsProcessed.WithLabelValues(source).Inc()
		p.metrics.KafkaMessagesPublished.Inc()
	}

	return normalized, nil
}

// normalize converts the raw payload to a normalized internal representation
func (p *TelemetryProcessor) normalize(payload models.TelemetryPayload, source string) *models.NormalizedTelemetry {
	return &models.NormalizedTelemetry{
		TruckID:                  payload.TruckID,
		EventTimestamp:           payload.Timestamp.UnixMilli(),
		IngestedAt:               time.Now().UnixMilli(),
		Latitude:                 payload.Latitude,
		Longitude:                payload.Longitude,
		EngineTemperatureCelsius: payload.EngineTemperatureCelsius,
		SpeedKmh:                 payload.SpeedKmh,
		FuelLevelPercent:         payload.FuelLevelPercent,
		DiagnosticCodes:          payload.DiagnosticCodes,
		Source:                   source,
		MessageID:                payload.MessageID,
	}
}

// enrich adds additional metadata to the normalized telemetry
func (p *TelemetryProcessor) enrich(telemetry *models.NormalizedTelemetry) {
	if telemetry.SpeedKmh > 100 {
		telemetry.SpeedViolation = true
	}
	if telemetry.EngineTemperatureCelsius > 110 || telemetry.EngineTemperatureCelsius < -20 {
		telemetry.TempAnomaly = true
	}
}
