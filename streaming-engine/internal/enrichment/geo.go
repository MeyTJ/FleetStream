// Package enrichment provides geographic enrichment for telemetry data.
package enrichment

import (
	"math"
	"strings"
)

// GeoEnricher provides geographic enrichment functionality
type GeoEnricher struct{}

// NewGeoEnricher creates a new GeoEnricher
func NewGeoEnricher() *GeoEnricher {
	return &GeoEnricher{}
}

// Location represents a geographic location
type Location struct {
	CountryCode string
	Region     string
	City       string
}

// Base32 characters for geohash
const base32 = "0123456789bcdefghjkmnpqrstuvwxyz"

// CalculateGeohash calculates a geohash from coordinates
func (g *GeoEnricher) CalculateGeohash(lat, lng float64, precision int) string {
	var geohash strings.Builder
	minLat, maxLat := -90.0, 90.0
	minLng, maxLng := -180.0, 180.0
	even := true
	bit := 0
	ch := 0

	for geohash.Len() < precision {
		if even {
			mid := (minLng + maxLng) / 2
			if lng >= mid {
				ch |= 1 << (4 - bit)
				minLng = mid
			} else {
				maxLng = mid
			}
		} else {
			mid := (minLat + maxLat) / 2
			if lat >= mid {
				ch |= 1 << (4 - bit)
				minLat = mid
			} else {
				maxLat = mid
			}
		}
		even = !even
		bit++
		if bit == 5 {
			geohash.WriteByte(base32[ch])
			bit = 0
			ch = 0
		}
	}
	return geohash.String()
}

// ReverseGeocode performs reverse geocoding (simplified for demo)
func (g *GeoEnricher) ReverseGeocode(lat, lng float64) Location {
	// Simplified reverse geocoding based on coordinates
	// In production, this would call a geocoding service
	loc := Location{}
	
	// Determine country based on longitude (simplified)
	switch {
	case lng >= -180 && lng < -30:
		loc.CountryCode = "US"
		loc.Region = "North America"
	case lng >= -30 && lng < 60:
		loc.CountryCode = "EU"
		loc.Region = "Europe"
	case lng >= 60 && lng < 180:
		loc.CountryCode = "AS"
		loc.Region = "Asia"
	}
	
	// Determine city based on rough coordinates
	switch {
	case lat >= 35 && lat <= 45 && lng >= -120 && lng <= -74:
		loc.City = "Major US City"
	case lat >= 48 && lat <= 55 && lng >= 5 && lng <= 15:
		loc.City = "European City"
	default:
		loc.City = "Unknown"
	}
	
	return loc
}

// IsOutsideGeofence checks if a truck has moved outside a geofence
func (g *GeoEnricher) IsOutsideGeofence(lat1, lng1, lat2, lng2 float64, thresholdKm float64) bool {
	distance := g.calculateDistance(lat1, lng1, lat2, lng2)
	return distance > thresholdKm
}

// calculateDistance calculates distance between two points using Haversine
func (g *GeoEnricher) calculateDistance(lat1, lng1, lat2, lng2 float64) float64 {
	const R = 6371 // Earth's radius in km
	dLat := degreesToRadians(lat2 - lat1)
	dLng := degreesToRadians(lng2 - lng1)
	a := math.Sin(dLat/2)*math.Sin(dLat/2) + math.Cos(degreesToRadians(lat1))*math.Cos(degreesToRadians(lat2))*math.Sin(dLng/2)*math.Sin(dLng/2)
	c := 2 * math.Atan2(math.Sqrt(a), math.Sqrt(1-a))
	return R * c
}

func degreesToRadians(deg float64) float64 {
	return deg * math.Pi / 180
}

// DistanceToHq calculates distance from headquarters (simplified)
func (g *GeoEnricher) DistanceToHq(lat, lng, hqLat, hqLng float64) float64 {
	return g.calculateDistance(lat, lng, hqLat, hqLng)
}
