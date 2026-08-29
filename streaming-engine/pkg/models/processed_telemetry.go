package models

import "time"

// ProcessedTelemetry represents enriched and processed telemetry
type ProcessedTelemetry struct {
	TruckID                  string    `json:"truck_id"`
	MessageID                string    `json:"message_id"`
	EventTimestamp           int64     `json:"event_timestamp"`
	ProcessedAt              int64     `json:"processed_at"`
	Latitude                 float64   `json:"latitude"`
	Longitude                float64   `json:"longitude"`
	EngineTemperatureCelsius float64   `json:"engine_temperature_celsius"`
	SpeedKmh                 float64   `json:"speed_kmh"`
	FuelLevelPercent         float32   `json:"fuel_level_percent"`
	DiagnosticCodes          []string  `json:"diagnostic_codes"`
	Source                   string    `json:"source"`
	
	// Enriched fields from streaming engine
	CountryCode              string  `json:"country_code,omitempty"`
	Region                   string  `json:"region,omitempty"`
	City                     string  `json:"city,omitempty"`
	Geohash                  string  `json:"geohash,omitempty"`
	
	// Anomaly detection
	SpeedViolation           bool    `json:"speed_violation"`
	TempAnomaly              bool    `json:"temp_anomaly"`
	FuelLow                  bool    `json:"fuel_low"`
	GeofenceViolation        bool    `json:"geofence_violation"`
	
	// Aggregated metrics
	DistanceFromLastKm       float64 `json:"distance_from_last_km"`
	TimeSinceLastSec         int64   `json:"time_since_last_sec"`
	AverageSpeedKmh          float64 `json:"average_speed_kmh"`
	MaxSpeedKmh              float64 `json:"max_speed_kmh"`
	IdleTimeSec              int64   `json:"idle_time_sec"`
	
	// Risk scoring
	RiskScore                float64 `json:"risk_score"`
	RiskLevel                string  `json:"risk_level"` // low, medium, high, critical
	
	// Processing metadata
	ProcessingVersion        int     `json:"processing_version"`
	ProcessingDurationMs     int64   `json:"processing_duration_ms"`
}

// TruckState represents the current state of a truck in Redis
type TruckState struct {
	TruckID                  string    `json:"truck_id"`
	LastMessageID            string    `json:"last_message_id"`
	LastUpdateTime           int64     `json:"last_update_time"`
	Latitude                 float64   `json:"latitude"`
	Longitude                float64   `json:"longitude"`
	LastEngineTemp           float64   `json:"last_engine_temp"`
	LastSpeed                float64   `json:"last_speed"`
	LastFuelLevel            float32   `json:"last_fuel_level"`
	
	// Running statistics
	TotalMessagesProcessed   int64     `json:"total_messages_processed"`
	TotalDistanceKm          float64   `json:"total_distance_km"`
	MaxSpeedToday            float64   `json:"max_speed_today"`
	MaxSpeedTodayTime        int64     `json:"max_speed_today_time"`
	ViolationsCount          int       `json:"violations_count"`
	AnomaliesCount           int       `json:"anomalies_count"`
	IdleStartTime            int64     `json:"idle_start_time"`
	TotalIdleTimeSec         int64     `json:"total_idle_time_sec"`
	
	// State metadata
	IsMoving                 bool      `json:"is_moving"`
	IsOnline                 bool      `json:"is_online"`
	LastSeenTime             int64     `json:"last_seen_time"`
	CreatedAt                int64     `json:"created_at"`
	UpdatedAt                time.Time `json:"updated_at"`
}

// RiskLevel constants
const (
	RiskLevelLow      = "low"
	RiskLevelMedium   = "medium"
	RiskLevelHigh     = "high"
	RiskLevelCritical = "critical"
)

// Anomaly types
const (
	AnomalyTypeSpeedViolation = "speed_violation"
	AnomalyTypeTempAnomaly    = "temp_anomaly"
	AnomalyTypeFuelLow        = "fuel_low"
	AnomalyTypeGeofence       = "geofence_violation"
	AnomalyTypeRouteDeviation = "route_deviation"
	AnomalyTypeStale          = "stale_data"
)
