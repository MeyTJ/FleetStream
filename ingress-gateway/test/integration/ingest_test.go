//go:build integration

package integration_test

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/ingress-gateway/internal/handlers"
	"github.com/fleetstream/ingress-gateway/internal/observability"
	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/google/uuid"
	"github.com/sirupsen/logrus"
)

func TestHTTPIngestPublishesToKafka(t *testing.T) {
	brokers := brokerList()
	topic := envOr("KAFKA_TOPIC", "fleet.telemetry.raw")

	logger := logrus.New()
	logger.SetLevel(logrus.WarnLevel)

	producer, err := processors.NewKafkaProducer(processors.KafkaProducerConfig{
		Brokers:        brokers,
		Topic:          topic,
		ClientID:       "ingress-gateway-integration-test",
		Compression:    "none",
		DropOnFull:     true,
		MaxQueueDepth:  100000,
		AlertThreshold: 0.8,
		Logger:         logger,
	})
	if err != nil {
		t.Fatalf("create kafka producer: %v", err)
	}
	t.Cleanup(func() { _ = producer.Close() })

	metrics := processors.NewMetrics("fleetstream_ingress_test")
	telemetryProcessor := processors.NewTelemetryProcessor(producer, logger, metrics)

	pool, err := processors.NewShardedPool(processors.PoolConfig{
		NumShards:       2,
		WorkersPerShard: 2,
		QueueSize:       1000,
		Logger:          logger,
		Metrics:         metrics,
		Processor:       telemetryProcessor,
	})
	if err != nil {
		t.Fatalf("create worker pool: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	t.Cleanup(cancel)
	pool.Start(ctx)
	t.Cleanup(func() {
		shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer shutdownCancel()
		_ = pool.Shutdown(shutdownCtx)
	})

	httpHandler := handlers.NewHTTPHandler(pool, metrics, logger)
	mux := http.NewServeMux()
	mux.HandleFunc("/ingest", httpHandler.ServeHTTP)
	server := httptest.NewServer(observability.Middleware(mux))
	t.Cleanup(server.Close)

	messageID := uuid.New().String()
	truckID := uuid.New().String()
	payload := models.TelemetryPayload{
		TruckID:                  truckID,
		Timestamp:                time.Now().UTC(),
		Latitude:                 40.7128,
		Longitude:                -74.0060,
		EngineTemperatureCelsius: 90,
		SpeedKmh:                 55,
		FuelLevelPercent:         75,
		MessageID:                messageID,
	}

	body, err := json.Marshal(payload)
	if err != nil {
		t.Fatalf("marshal payload: %v", err)
	}

	startOffsets, err := kafkaNewestOffsets(brokers, topic)
	if err != nil {
		t.Fatalf("read kafka offsets: %v", err)
	}

	resp, err := http.Post(server.URL+"/ingest", "application/json", bytes.NewReader(body))
	if err != nil {
		t.Fatalf("post ingest: %v", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusAccepted {
		t.Fatalf("expected 202, got %d", resp.StatusCode)
	}

	if !waitForKafkaMessage(t, brokers, topic, messageID, startOffsets, 30*time.Second) {
		t.Fatalf("message %s not found on topic %s within timeout", messageID, topic)
	}
}

func kafkaNewestOffsets(brokers []string, topic string) (map[int32]int64, error) {
	clientCfg := sarama.NewConfig()
	client, err := sarama.NewClient(brokers, clientCfg)
	if err != nil {
		return nil, err
	}
	defer client.Close()

	partitions, err := client.Partitions(topic)
	if err != nil {
		return nil, err
	}

	startOffsets := make(map[int32]int64, len(partitions))
	for _, partition := range partitions {
		offset, err := client.GetOffset(topic, partition, sarama.OffsetNewest)
		if err != nil {
			return nil, err
		}
		startOffsets[partition] = offset
	}
	return startOffsets, nil
}

func waitForKafkaMessage(t *testing.T, brokers []string, topic, messageID string, startOffsets map[int32]int64, timeout time.Duration) bool {
	t.Helper()

	deadline := time.Now().Add(timeout)
	consumerCfg := sarama.NewConfig()
	consumer, err := sarama.NewConsumer(brokers, consumerCfg)
	if err != nil {
		t.Fatalf("create kafka consumer: %v", err)
	}
	defer consumer.Close()

	for time.Now().Before(deadline) {
		for partition, offset := range startOffsets {
			pc, err := consumer.ConsumePartition(topic, partition, offset)
			if err != nil {
				t.Fatalf("consume partition %d: %v", partition, err)
			}

			select {
			case msg, ok := <-pc.Messages():
				if !ok {
					pc.Close()
					continue
				}
				startOffsets[partition] = msg.Offset + 1
				var normalized models.NormalizedTelemetry
				if err := json.Unmarshal(msg.Value, &normalized); err != nil {
					pc.Close()
					continue
				}
				if normalized.MessageID == messageID {
					pc.Close()
					return true
				}
			case <-time.After(500 * time.Millisecond):
			}
			pc.Close()
		}
	}
	return false
}

func brokerList() []string {
	if v := os.Getenv("KAFKA_BROKERS"); v != "" {
		return splitCSV(v)
	}
	return []string{"localhost:9092"}
}

func envOr(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func splitCSV(v string) []string {
	parts := strings.Split(v, ",")
	out := make([]string, 0, len(parts))
	for _, p := range parts {
		p = strings.TrimSpace(p)
		if p != "" {
			out = append(out, p)
		}
	}
	return out
}
