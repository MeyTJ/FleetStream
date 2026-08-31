package processor

import (
	"context"
	"io"
	"testing"

	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/sirupsen/logrus"
)

func TestDetectAnomalies_UsesConfigThresholds(t *testing.T) {
	cfg := config.DefaultConfig()
	cfg.Processing.AnomalyThreshold = config.AnomalyConfig{
		MaxSpeedKmh:                80,
		MaxEngineTempCelsius:       100,
		MinEngineTempCelsius:       0,
		MinFuelLevelPercent:        20,
		SpeedViolationThresholdKmh: 60,
	}
	cfg.Processing.RiskScoring = config.RiskConfig{
		SpeedWeight: 0.11,
		TempWeight:  0.22,
		FuelWeight:  0.33,
	}

	logger := logrus.New()
	logger.SetOutput(io.Discard)
	m := metrics.NewMetrics("fleetstream_streaming_processor_test_" + t.Name())
	p := NewStreamProcessor(&cfg.Processing, logger, m)

	result := p.Process(context.Background(), models.TelemetryPayload{
		TruckID:                  "truck-1",
		MessageID:                "msg-1",
		SpeedKmh:                 70,
		EngineTemperatureCelsius: 110,
		FuelLevelPercent:         10,
	}, nil).Processed

	if !result.SpeedViolation {
		t.Fatal("expected speed violation from config threshold")
	}
	if !result.TempAnomaly {
		t.Fatal("expected temp anomaly from config threshold")
	}
	if !result.FuelLow {
		t.Fatal("expected fuel low from config threshold")
	}
	if result.RiskScore != 0.66 {
		t.Fatalf("risk score = %v, want 0.66 from config weights", result.RiskScore)
	}
}

func TestDetectAnomalies_NoViolationBelowThresholds(t *testing.T) {
	cfg := config.DefaultConfig()
	cfg.Processing.AnomalyThreshold.SpeedViolationThresholdKmh = 120
	cfg.Processing.AnomalyThreshold.MaxSpeedKmh = 150
	cfg.Processing.AnomalyThreshold.MaxEngineTempCelsius = 110
	cfg.Processing.AnomalyThreshold.MinEngineTempCelsius = -20
	cfg.Processing.AnomalyThreshold.MinFuelLevelPercent = 15

	logger := logrus.New()
	logger.SetOutput(io.Discard)
	m := metrics.NewMetrics("fleetstream_streaming_processor_test_clean_" + t.Name())
	p := NewStreamProcessor(&cfg.Processing, logger, m)

	result := p.Process(context.Background(), models.TelemetryPayload{
		TruckID:                  "truck-1",
		MessageID:                "msg-2",
		SpeedKmh:                 80,
		EngineTemperatureCelsius: 90,
		FuelLevelPercent:         50,
	}, nil).Processed

	if result.SpeedViolation || result.TempAnomaly || result.FuelLow {
		t.Fatalf("unexpected anomalies: speed=%v temp=%v fuel=%v", result.SpeedViolation, result.TempAnomaly, result.FuelLow)
	}
}
