package handlers

import (
	"context"
	"io"
	"time"

	"github.com/fleetstream/ingress-gateway/internal/observability"
	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/models"
	telemetry "github.com/fleetstream/ingress-gateway/proto/gen/v1"
	"github.com/sirupsen/logrus"
)

type GRPCHandler struct {
	telemetry.UnimplementedTelemetryIngestionServer
	pool    *processors.ShardedPool
	metrics *processors.Metrics
	logger  *logrus.Logger
}

var _ telemetry.TelemetryIngestionServer = (*GRPCHandler)(nil)

func NewGRPCHandler(pool *processors.ShardedPool, metrics *processors.Metrics, logger *logrus.Logger) *GRPCHandler {
	return &GRPCHandler{
		pool:    pool,
		metrics: metrics,
		logger:  logger,
	}
}

func (h *GRPCHandler) Ingest(ctx context.Context, req *telemetry.IngestRequest) (*telemetry.IngestResponse, error) {
	start := time.Now()
	payload := protoToPayload(req.GetPayload())

	if err := validatePayload(payload); err != nil {
		observability.WithCtx(h.logger, ctx).WithError(err).Warn("invalid payload received")
		return &telemetry.IngestResponse{
			Accepted:    false,
			MessageId:   payload.MessageID,
			ProcessedAt: time.Now().UnixMilli(),
		}, nil
	}

	err := h.pool.Submit(ctx, payload, "grpc")
	resp := &telemetry.IngestResponse{
		MessageId:   payload.MessageID,
		ProcessedAt: time.Now().UnixMilli(),
	}

	if err != nil {
		resp.Accepted = false
		if h.metrics != nil {
			h.metrics.JobsDropped.WithLabelValues("grpc", err.Error()).Inc()
		}
		return resp, nil
	}

	resp.Accepted = true
	observability.WithCtx(h.logger, ctx).WithField("message_id", payload.MessageID).Info("telemetry ingested")
	if h.metrics != nil {
		h.metrics.ProcessingDuration.WithLabelValues("grpc").Observe(time.Since(start).Seconds())
	}
	return resp, nil
}

func (h *GRPCHandler) BatchIngest(ctx context.Context, req *telemetry.BatchIngestRequest) (*telemetry.BatchIngestResponse, error) {
	resp := &telemetry.BatchIngestResponse{}
	for _, p := range req.GetPayloads() {
		item, err := h.Ingest(ctx, &telemetry.IngestRequest{Payload: p})
		if err != nil {
			resp.Rejected++
			continue
		}
		if item.Accepted {
			resp.Accepted++
			resp.MessageIds = append(resp.MessageIds, item.MessageId)
		} else {
			resp.Rejected++
		}
	}
	return resp, nil
}

func (h *GRPCHandler) StreamIngest(stream telemetry.TelemetryIngestion_StreamIngestServer) error {
	for {
		req, err := stream.Recv()
		if err == io.EOF {
			return nil
		}
		if err != nil {
			return err
		}
		resp, err := h.Ingest(stream.Context(), req)
		if err != nil {
			return err
		}
		if err := stream.Send(resp); err != nil {
			return err
		}
	}
}

func protoToPayload(p *telemetry.TelemetryPayload) models.TelemetryPayload {
	if p == nil {
		return models.TelemetryPayload{}
	}
	ts := time.Time{}
	if p.TimestampUnixMs > 0 {
		ts = time.UnixMilli(p.TimestampUnixMs)
	}
	return models.TelemetryPayload{
		TruckID:                  p.TruckId,
		Timestamp:                ts,
		Latitude:                 p.Latitude,
		Longitude:                p.Longitude,
		EngineTemperatureCelsius: p.EngineTemperatureCelsius,
		SpeedKmh:                 p.SpeedKmh,
		FuelLevelPercent:         p.FuelLevelPercent,
		DiagnosticCodes:          p.DiagnosticCodes,
		MessageID:                p.MessageId,
		Source:                   "grpc",
	}
}
