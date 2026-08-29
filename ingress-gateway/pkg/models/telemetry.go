package models

import (
	"time"
)

// TelemetryPayload represents incoming telemetry data from a delivery truck.
type TelemetryPayload struct {
	TruckID                   string    `json:"truck_id" validate:"required,uuid"`
	Timestamp                 time.Time `json:"timestamp"`
	Latitude                  float64   `json:"latitude" validate:"gte=-90,lte=90"`
	Longitude                 float64   `json:"longitude" validate:"gte=-180,lte=180"`
	EngineTemperatureCelsius  float64   `json:"engine_temperature_celsius"`
	SpeedKmh                  float64   `json:"speed_kmh" validate:"gte=0"`
	FuelLevelPercent          float32   `json:"fuel_level_percent" validate:"gte=0,lte=100"`
	DiagnosticCodes           []string  `json:"diagnostic_codes"`
	MessageID                 string    `json:"message_id" validate:"required,uuid"`
	Source                    string    `json:"source"` // grpc, websocket, http
}

// NormalizedTelemetry is the enriched internal representation
type NormalizedTelemetry struct {
	TruckID                  string    `json:"truck_id"`
	EventTimestamp           int64     `json:"event_timestamp"`
	IngestedAt               int64     `json:"ingested_at"`
	Latitude                 float64   `json:"latitude"`
	Longitude                float64   `json:"longitude"`
	EngineTemperatureCelsius float64   `json:"engine_temperature_celsius"`
	SpeedKmh                 float64   `json:"speed_kmh"`
	FuelLevelPercent         float32   `json:"fuel_level_percent"`
	DiagnosticCodes          []string  `json:"diagnostic_codes"`
	Source                   string    `json:"source"`
	MessageID                string    `json:"message_id"`
	// Enriched fields
	CountryCode    string  `json:"country_code,omitempty"`
	Region         string  `json:"region,omitempty"`
	SpeedViolation bool    `json:"speed_violation,omitempty"`
	TempAnomaly    bool    `json:"temp_anomaly,omitempty"`
}

// KafkaMessage wraps normalized telemetry for Kafka transport
type KafkaMessage struct {
	Key   string             `json:"key"`   // truck_id for partitioning
	Value NormalizedTelemetry `json:"value"`
	Time  time.Time          `json:"time"`
}

// BatchTelemetry is a collection of telemetry payloads
type BatchTelemetry struct {
	Payloads []TelemetryPayload `json:"payloads"`
}

// IngestResult represents the result of an ingestion attempt
type IngestResult struct {
	Accepted   bool   `json:"accepted"`
	MessageID  string `json:"message_id"`
	Rejected   bool   `json:"rejected,omitempty"`
	Reason     string `json:"reason,omitempty"`
	ProcessedAt int64  `json:"processed_at"`
}

// BatchIngestResult represents the result of a batch ingestion
type BatchIngestResult struct {
	Accepted  int      `json:"accepted"`
	Rejected  int      `json:"rejected"`
	MessageIDs []string `json:"message_ids"`
}
