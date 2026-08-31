package models

import "time"

// TelemetryPayload is the raw telemetry consumed from fleet.telemetry.raw.
type TelemetryPayload struct {
	TruckID                  string    `json:"truck_id"`
	Timestamp                time.Time `json:"timestamp"`
	Latitude                 float64   `json:"latitude"`
	Longitude                float64   `json:"longitude"`
	EngineTemperatureCelsius float64   `json:"engine_temperature_celsius"`
	SpeedKmh                 float64   `json:"speed_kmh"`
	FuelLevelPercent         float32   `json:"fuel_level_percent"`
	DiagnosticCodes          []string  `json:"diagnostic_codes"`
	MessageID                string    `json:"message_id"`
	Source                   string    `json:"source"`
}
