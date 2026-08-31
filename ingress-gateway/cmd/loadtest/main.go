// Package cmd provides the load testing tool for the Ingress Gateway.
package main

import (
	"bytes"
	"encoding/json"
	"flag"
	"fmt"
	"math/rand"
	"net/http"
	"net/url"
	"os"
	"sync"
	"sync/atomic"
	"time"

	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/google/uuid"
	"github.com/gorilla/websocket"
	"github.com/sirupsen/logrus"
)

var (
	mode           = flag.String("mode", "throughput", "Test mode: throughput or connections")
	numTrucks      = flag.Int("trucks", 10000, "Number of simulated trucks (throughput mode)")
	connections    = flag.Int("connections", 10000, "Number of concurrent connections (connections mode)")
	rate           = flag.Int("rate", 100, "Messages per second per truck")
	duration       = flag.Duration("duration", 1*time.Minute, "Test duration")
	targetURL      = flag.String("url", "http://localhost:8080/ingest", "Target ingestion URL")
	useWebSocket   = flag.Bool("ws", false, "Use WebSocket instead of HTTP")
	gate           = flag.Bool("gate", false, "Exit non-zero when success criteria are not met")
	minSuccessRate = flag.Float64("min-success-rate", 0.99, "Minimum success rate for gate mode")
)

type loadTestStats struct {
	sent      atomic.Uint64
	accepted  atomic.Uint64
	rejected  atomic.Uint64
	errors    atomic.Uint64
	latencies []float64
	mu        sync.Mutex
}

type connectionStats struct {
	attempted atomic.Uint64
	connected atomic.Uint64
	failed    atomic.Uint64
}

func main() {
	flag.Parse()

	logger := logrus.New()
	logger.SetFormatter(&logrus.TextFormatter{
		TimestampFormat: time.RFC3339,
		FullTimestamp:   true,
	})

	switch *mode {
	case "connections":
		os.Exit(runConnectionsGate(logger))
	case "throughput":
		os.Exit(runThroughput(logger))
	default:
		logger.Fatalf("unknown mode %q (use throughput or connections)", *mode)
	}
}

func runThroughput(logger *logrus.Logger) int {
	stats := &loadTestStats{
		latencies: make([]float64, 0, *numTrucks),
	}

	logger.WithFields(logrus.Fields{
		"mode":       "throughput",
		"trucks":     *numTrucks,
		"rate":       *rate,
		"duration":   *duration,
		"target_url": *targetURL,
	}).Info("starting load test")

	client := &http.Client{
		Timeout: 10 * time.Second,
	}

	var wg sync.WaitGroup
	startTime := time.Now()

	for i := 0; i < *numTrucks; i++ {
		wg.Add(1)
		go func(truckID int) {
			defer wg.Done()
			simulateTruck(client, truckID, stats, logger)
		}(i)
		time.Sleep(100 * time.Microsecond)
	}

	ticker := time.NewTicker(5 * time.Second)
	done := make(chan struct{})
	go func() {
		defer close(done)
		for {
			select {
			case <-ticker.C:
				elapsed := time.Since(startTime)
				logger.WithFields(logrus.Fields{
					"elapsed":    elapsed.Round(time.Second),
					"sent":       stats.sent.Load(),
					"accepted":   stats.accepted.Load(),
					"rejected":   stats.rejected.Load(),
					"throughput": float64(stats.sent.Load()) / elapsed.Seconds(),
				}).Info("load test progress")
			}
		}
	}()

	time.Sleep(*duration)
	ticker.Stop()
	<-done

	elapsed := time.Since(startTime)
	sent := stats.sent.Load()
	accepted := stats.accepted.Load()
	rejected := stats.rejected.Load()
	errors := stats.errors.Load()

	logger.WithFields(logrus.Fields{
		"elapsed":    elapsed.Round(time.Second),
		"total_sent": sent,
		"accepted":   accepted,
		"rejected":   rejected,
		"errors":     errors,
		"avg_rate":   float64(sent) / elapsed.Seconds(),
	}).Info("load test complete")

	if !*gate {
		return 0
	}
	if sent == 0 {
		logger.Error("gate failed: no messages sent")
		return 1
	}
	successRate := float64(accepted) / float64(sent)
	if successRate < *minSuccessRate {
		logger.WithField("success_rate", successRate).Error("gate failed: success rate below threshold")
		return 1
	}
	return 0
}

func runConnectionsGate(logger *logrus.Logger) int {
	wsURL, err := websocketURL(*targetURL)
	if err != nil {
		logger.WithError(err).Fatal("invalid target url")
	}

	stats := &connectionStats{}
	logger.WithFields(logrus.Fields{
		"mode":        "connections",
		"connections": *connections,
		"duration":    *duration,
		"ws_url":      wsURL,
	}).Info("starting connection load test")

	ctx, cancel := contextWithTimeout(*duration)
	defer cancel()

	var wg sync.WaitGroup
	for i := 0; i < *connections; i++ {
		wg.Add(1)
		stats.attempted.Add(1)
		go func() {
			defer wg.Done()
			holdWebSocket(ctx, wsURL, stats)
		}()
		if i%500 == 0 {
			time.Sleep(time.Millisecond)
		}
	}

	<-ctx
	wg.Wait()

	attempted := stats.attempted.Load()
	connected := stats.connected.Load()
	failed := stats.failed.Load()
	successRate := 0.0
	if attempted > 0 {
		successRate = float64(connected) / float64(attempted)
	}

	logger.WithFields(logrus.Fields{
		"attempted":    attempted,
		"connected":    connected,
		"failed":       failed,
		"success_rate": successRate,
	}).Info("connection load test complete")

	if !*gate {
		return 0
	}
	if successRate < *minSuccessRate {
		logger.WithField("min_success_rate", *minSuccessRate).Error("gate failed: connection success rate below threshold")
		return 1
	}
	return 0
}

func holdWebSocket(done <-chan struct{}, wsURL string, stats *connectionStats) {
	dialer := websocket.Dialer{
		HandshakeTimeout: 10 * time.Second,
	}
	conn, _, err := dialer.Dial(wsURL, nil)
	if err != nil {
		stats.failed.Add(1)
		return
	}
	stats.connected.Add(1)
	defer conn.Close()

	_ = conn.SetReadDeadline(time.Now().Add(90 * time.Second))
	for {
		select {
		case <-done:
			return
		default:
			if _, _, err := conn.ReadMessage(); err != nil {
				return
			}
		}
	}
}

func websocketURL(target string) (string, error) {
	u, err := url.Parse(target)
	if err != nil {
		return "", err
	}
	switch u.Scheme {
	case "http":
		u.Scheme = "ws"
	case "https":
		u.Scheme = "wss"
	default:
		return "", fmt.Errorf("unsupported scheme %q", u.Scheme)
	}
	u.Path = "/ws"
	u.RawQuery = ""
	u.Fragment = ""
	return u.String(), nil
}

func contextWithTimeout(d time.Duration) (<-chan struct{}, func()) {
	done := make(chan struct{})
	timer := time.NewTimer(d)
	go func() {
		<-timer.C
		close(done)
	}()
	return done, func() { timer.Stop() }
}

func simulateTruck(client *http.Client, truckID int, stats *loadTestStats, logger *logrus.Logger) {
	truckUUID := uuid.New().String()
	interval := time.Second / time.Duration(*rate)
	ticker := time.NewTicker(interval)
	defer ticker.Stop()

	for range ticker.C {
		payload := generateTelemetry(truckUUID)
		sendTelemetry(client, payload, stats, logger)
	}
}

func generateTelemetry(truckID string) models.TelemetryPayload {
	return models.TelemetryPayload{
		TruckID:                  truckID,
		Timestamp:                time.Now(),
		Latitude:                 40.7128 + (rand.Float64()-0.5)*0.1,
		Longitude:                -74.0060 + (rand.Float64()-0.5)*0.1,
		EngineTemperatureCelsius: 85 + rand.Float64()*10,
		SpeedKmh:                 50 + rand.Float64()*30,
		FuelLevelPercent:         50 + rand.Float32()*50,
		DiagnosticCodes:          nil,
		MessageID:                uuid.New().String(),
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
