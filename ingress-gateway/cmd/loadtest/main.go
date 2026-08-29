// Package cmd provides the load testing tool for the Ingress Gateway.
package main

import (
	"flag"
	"fmt"
	"math/rand"
	"net/http"
	"sync"
	"sync/atomic"
	"time"

	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/google/uuid"
	"github.com/sirupsen/logrus"
)

var (
	numTrucks    = flag.Int("trucks", 10000, "Number of simulated trucks")
	rate         = flag.Int("rate", 100, "Messages per second per truck")
	duration     = flag.Duration("duration", 1*time.Minute, "Test duration")
	targetURL    = flag.String("url", "http://localhost:8080/ingest", "Target ingestion URL")
	useWebSocket = flag.Bool("ws", false, "Use WebSocket instead of HTTP")
)

type loadTestStats struct {
	sent     atomic.Uint64
	accepted atomic.Uint64
	rejected atomic.Uint64
	errors   atomic.Uint64
	latencies []float64
	mu        sync.Mutex
}

func main() {
	flag.Parse()

	logger := logrus.New()
	logger.SetFormatter(&logrus.TextFormatter{
		TimestampFormat: time.RFC3339,
		FullTimestamp:   true,
	})

	stats := &loadTestStats{
		latencies: make([]float64, 0, *numTrucks),
	}

	logger.WithFields(logrus.Fields{
		"trucks":     *numTrucks,
		"rate":       *rate,
		"duration":   *duration,
		"target_url": *targetURL,
	}).Info("starting load test")

	// Create HTTP client
	client := &http.Client{
		Timeout: 10 * time.Second,
	}

	// Start truck simulators
	var wg sync.WaitGroup
	startTime := time.Now()

	for i := 0; i < *numTrucks; i++ {
		wg.Add(1)
		go func(truckID int) {
			defer wg.Done()
			simulateTruck(client, truckID, stats, logger)
		}(i)
		
		// Stagger truck connections
		time.Sleep(100 * time.Microsecond)
	}

	// Report stats periodically
	ticker := time.NewTicker(5 * time.Second)
	go func() {
		for {
			select {
			case <-ticker.C:
				elapsed := time.Since(startTime)
				sent := stats.sent.Load()
				accepted := stats.accepted.Load()
				rejected := stats.rejected.Load()
				
				logger.WithFields(logrus.Fields{
					"elapsed":    elapsed.Round(time.Second),
					"sent":       sent,
					"accepted":   accepted,
					"rejected":   rejected,
					"throughput": float64(sent) / elapsed.Seconds(),
				}).Info("load test progress")
			}
		}
	}()

	// Wait for test duration or completion
	time.Sleep(*duration)
	
	// Final stats
	elapsed := time.Since(startTime)
	logger.WithFields(logrus.Fields{
		"elapsed":    elapsed.Round(time.Second),
		"total_sent":  stats.sent.Load(),
		"accepted":   stats.accepted.Load(),
		"rejected":   stats.rejected.Load(),
		"errors":      stats.errors.Load(),
		"avg_rate":    float64(stats.sent.Load()) / elapsed.Seconds(),
	}).Info("load test complete")
}

func simulateTruck(client *http.Client, truckID int, stats *loadTestStats, logger *logrus.Logger) {
	truckUUID := uuid.New().String()
	
	// Calculate interval between messages
	interval := time.Second / time.Duration(*rate)
	ticker := time.NewTicker(interval)
	defer ticker.Stop()

	for {
		select {
		case <-ticker.C:
			payload := generateTelemetry(truckUUID)
			sendTelemetry(client, payload, stats, logger)
		}
	}
}

func generateTelemetry(truckID string) models.TelemetryPayload {
	return models.TelemetryPayload{
		TruckID:                  truckID,
		Timestamp:                time.Now(),
		Latitude:                40.7128 + (rand.Float64()-0.5)*0.1,
		Longitude:               -74.0060 + (rand.Float64()-0.5)*0.1,
		EngineTemperatureCelsius: 85 + rand.Float64()*10,
		SpeedKmh:                50 + rand.Float64()*30,
		FuelLevelPercent:         50 + rand.Float32()*50,
		DiagnosticCodes:          nil,
		MessageID:               uuid.New().String(),
	}
}

func sendTelemetry(client *http.Client, payload models.TelemetryPayload, stats *loadTestStats, logger *logrus.Logger) {
	start := time.Now()
	
	body, err := json.Marshal(payload)
	if err != nil {
		stats.errors.Add(1)
		return
	}

	resp, err := client.Post(*targetURL, "application/json", bytes.NewReader(body))
	if err != nil {
		stats.errors.Add(1)
		stats.rejected.Add(1)
		logger.WithError(err).Warn("failed to send telemetry")
		return
	}
	defer resp.Body.Close()

	latency := time.Since(start).Seconds()
	stats.mu.Lock()
	stats.latencies = append(stats.latencies, latency)
	stats.mu.Unlock()

	stats.sent.Add(1)

	if resp.StatusCode == http.StatusAccepted {
		stats.accepted.Add(1)
	} else {
		stats.rejected.Add(1)
	}
}
