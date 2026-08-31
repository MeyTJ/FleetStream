package handlers

import (
	"errors"

	"github.com/fleetstream/ingress-gateway/pkg/models"
)

func validatePayload(p models.TelemetryPayload) error {
	if p.TruckID == "" {
		return errors.New("truck_id is required")
	}
	if p.Latitude < -90 || p.Latitude > 90 {
		return errors.New("latitude out of range")
	}
	if p.Longitude < -180 || p.Longitude > 180 {
		return errors.New("longitude out of range")
	}
	if p.SpeedKmh < 0 {
		return errors.New("speed_kmh must be >= 0")
	}
	if p.FuelLevelPercent < 0 || p.FuelLevelPercent > 100 {
		return errors.New("fuel_level_percent out of range")
	}
	return nil
}
