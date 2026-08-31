// Package processor implements the stream processing logic with enrichment and anomaly detection.
package processor

import (
	"context"
	"math"
	"time"

	"github.com/fleetstream/streaming-engine/internal/enrichment"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/sirupsen/logrus"
)

// StreamProcessor processes telemetry streams with enrichment and anomaly detection
type StreamProcessor struct {
	cfg         *config.ProcessingConfig
	logger      *logrus.Logger
	metrics     *metrics.Metrics
	enricher    *enrichment.GeoEnricher
	riskScorer  *RiskScorer
}

// NewStreamProcessor creates a new stream processor
func NewStreamProcessor(cfg *config.ProcessingConfig, logger *logrus.Logger, m *metrics.Metrics) *StreamProcessor {
	return &StreamProcessor{
		cfg:        cfg,
		logger:     logger,
		metrics:    m,
		enricher:   enrichment.NewGeoEnricher(),
		riskScorer: NewRiskScorer(&cfg.RiskScoring),
	}
}

// ProcessResult holds the result of processing a telemetry message
type ProcessResult struct {
	Processed   *models.ProcessedTelemetry
	IsDuplicate bool
	Error      error
}

// Process processes a single telemetry message
func (p *StreamProcessor) Process(ctx context.Context, telemetry models.TelemetryPayload, prevState *models.TruckState) *ProcessResult {
	start := time.Now()
	result := &models.ProcessedTelemetry{
		TruckID:                  telemetry.TruckID,
		MessageID:                telemetry.MessageID,
		EventTimestamp:           telemetry.Timestamp.UnixMilli(),
		ProcessedAt:              time.Now().UnixMilli(),
		Latitude:                 telemetry.Latitude,
		Longitude:                telemetry.Longitude,
		EngineTemperatureCelsius: telemetry.EngineTemperatureCelsius,
		SpeedKmh:                 telemetry.SpeedKmh,
		FuelLevelPercent:         telemetry.FuelLevelPercent,
		DiagnosticCodes:          telemetry.DiagnosticCodes,
		Source:                  telemetry.Source,
	}

	// Enrich with geographic data
	p.enrich(result)
	// Detect anomalies
	p.detectAnomalies(result, prevState)
	// Calculate aggregated metrics
	p.calculateAggregations(result, prevState)
	// Calculate risk score
	p.riskScorer.Calculate(result)

	result.ProcessingDurationMs = time.Since(start).Milliseconds()
	result.ProcessingVersion = 1
	return &ProcessResult{Processed: result}
}

// enrich adds geographic enrichment to the telemetry
func (p *StreamProcessor) enrich(result *models.ProcessedTelemetry) {
	result.Geohash = p.enricher.CalculateGeohash(result.Latitude, result.Longitude, 6)
	location := p.enricher.ReverseGeocode(result.Latitude, result.Longitude)
	result.CountryCode = location.CountryCode
	result.Region = location.Region
	result.City = location.City
}

// detectAnomalies identifies anomalies in the telemetry data
func (p *StreamProcessor) detectAnomalies(result *models.ProcessedTelemetry, prevState *models.TruckState) {
	t := p.cfg.AnomalyThreshold
	r := p.cfg.RiskScoring

	if result.SpeedKmh > t.SpeedViolationThresholdKmh || result.SpeedKmh > t.MaxSpeedKmh {
		result.SpeedViolation = true
		result.RiskScore += r.SpeedWeight
		p.metrics.SpeedViolations.Inc()
		p.metrics.AnomaliesDetected.WithLabelValues("speed_violation").Inc()
	}
	if result.EngineTemperatureCelsius > t.MaxEngineTempCelsius || result.EngineTemperatureCelsius < t.MinEngineTempCelsius {
		result.TempAnomaly = true
		result.RiskScore += r.TempWeight
		p.metrics.TempAnomalies.Inc()
		p.metrics.AnomaliesDetected.WithLabelValues("temp_anomaly").Inc()
	}
	if result.FuelLevelPercent < t.MinFuelLevelPercent {
		result.FuelLow = true
		result.RiskScore += r.FuelWeight
		p.metrics.FuelLowEvents.Inc()
		p.metrics.AnomaliesDetected.WithLabelValues("fuel_low").Inc()
	}
}

// calculateAggregations computes aggregated metrics
func (p *StreamProcessor) calculateAggregations(result *models.ProcessedTelemetry, prevState *models.TruckState) {
	if prevState == nil {
		result.DistanceFromLastKm = 0
		result.TimeSinceLastSec = 0
		result.AverageSpeedKmh = result.SpeedKmh
		result.MaxSpeedKmh = result.SpeedKmh
		result.IdleTimeSec = 0
		return
	}
	result.DistanceFromLastKm = haversineDistance(prevState.Latitude, prevState.Longitude, result.Latitude, result.Longitude)
	result.TimeSinceLastSec = (result.EventTimestamp - prevState.LastUpdateTime) / 1000
	if result.TimeSinceLastSec > 0 {
		result.AverageSpeedKmh = (prevState.LastSpeed + result.SpeedKmh) / 2
	}
	result.MaxSpeedKmh = math.Max(prevState.MaxSpeedToday, result.SpeedKmh)
	if result.SpeedKmh < 5 {
		result.IdleTimeSec = prevState.TotalIdleTimeSec + result.TimeSinceLastSec
	}
}

// RiskScorer calculates risk scores for telemetry data
type RiskScorer struct {
	cfg *config.RiskConfig
}

// NewRiskScorer creates a new risk scorer
func NewRiskScorer(cfg *config.RiskConfig) *RiskScorer {
	return &RiskScorer{cfg: cfg}
}

// Calculate computes the risk score and level
func (r *RiskScorer) Calculate(result *models.ProcessedTelemetry) {
	score := math.Min(1.0, result.RiskScore)
	result.RiskScore = score
	switch {
	case score >= r.cfg.HighRiskThreshold:
		result.RiskLevel = models.RiskLevelCritical
	case score >= r.cfg.MediumRiskThreshold:
		result.RiskLevel = models.RiskLevelHigh
	case score >= r.cfg.MediumRiskThreshold/2:
		result.RiskLevel = models.RiskLevelMedium
	default:
		result.RiskLevel = models.RiskLevelLow
	}
}

// haversineDistance calculates distance between two points in km
func haversineDistance(lat1, lon1, lat2, lon2 float64) float64 {
	const earthRadiusKm = 6371.0
	dLat := degreesToRadians(lat2 - lat1)
	dLon := degreesToRadians(lon2 - lon1)
	lat1Rad := degreesToRadians(lat1)
	lat2Rad := degreesToRadians(lat2)
	a := math.Sin(dLat/2)*math.Sin(dLat/2) + math.Sin(dLon/2)*math.Sin(dLon/2)*math.Cos(lat1Rad)*math.Cos(lat2Rad)
	return earthRadiusKm * 2 * math.Atan2(math.Sqrt(a), math.Sqrt(1-a))
}

func degreesToRadians(deg float64) float64 {
	return deg * math.Pi / 180
}
