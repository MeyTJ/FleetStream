//go:build integration

package integration_test

import (
	"context"
	"encoding/json"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/consumer"
	"github.com/fleetstream/streaming-engine/internal/dlq"
	"github.com/fleetstream/streaming-engine/internal/engine"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/processor"
	"github.com/fleetstream/streaming-engine/internal/state"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/hashicorp/go-uuid"
	"github.com/sirupsen/logrus"
)

func TestRawToProcessedPipeline(t *testing.T) {
	cfg := config.DefaultConfig()
	cfg.Consumer.Brokers = brokerList()
	cfg.Producer.Brokers = cfg.Consumer.Brokers
	cfg.DLQ.Brokers = cfg.Consumer.Brokers
	cfg.Redis.Addresses = redisAddresses()
	cfg.Consumer.Topic = envOr("CONSUMER_TOPIC", "fleet.telemetry.raw")
	cfg.Producer.Topic = envOr("PRODUCER_TOPIC", "fleet.telemetry.processed")
	groupID, err := uuid.GenerateUUID()
	if err != nil {
		t.Fatalf("generate group id: %v", err)
	}
	cfg.Consumer.GroupID = "streaming-engine-integration-" + groupID
	cfg.Consumer.StartOffset = "newest"
	cfg.Idempotency.Enabled = true

	logger := logrus.New()
	logger.SetLevel(logrus.WarnLevel)
	m := metrics.NewMetrics("fleetstream_streaming_integration_" + strings.ReplaceAll(t.Name(), "/", "_"))

	stateStore, err := state.NewRedisStateStore(&cfg.Redis, logger, m)
	if err != nil {
		t.Fatalf("connect redis: %v", err)
	}
	t.Cleanup(func() { _ = stateStore.Close() })

	dlqHandler, err := dlq.NewDLQHandler(&cfg.DLQ, logger, m)
	if err != nil {
		t.Fatalf("create dlq handler: %v", err)
	}
	t.Cleanup(func() { _ = dlqHandler.Close() })

	publisher, err := engine.NewSyncPublisher(&cfg.Producer)
	if err != nil {
		t.Fatalf("create publisher: %v", err)
	}
	t.Cleanup(func() { _ = publisher.Close() })

	streamProcessor := processor.NewStreamProcessor(&cfg.Processing, logger, m)
	msgHandler := engine.NewHandler(cfg, logger, m, stateStore, publisher, dlqHandler, streamProcessor)

	kafkaConsumer, err := consumer.NewExactlyOnceConsumer(
		cfg.Consumer.Brokers,
		cfg.Consumer.Topic,
		cfg.Consumer.GroupID,
		&cfg.Consumer,
		logger,
		m,
		msgHandler.Handle,
	)
	if err != nil {
		t.Fatalf("create consumer: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	t.Cleanup(cancel)
	if err := kafkaConsumer.Start(ctx); err != nil {
		t.Fatalf("start consumer: %v", err)
	}
	t.Cleanup(func() {
		_ = kafkaConsumer.Close()
		kafkaConsumer.WaitForShutdown()
	})

	messageID, err := uuid.GenerateUUID()
	if err != nil {
		t.Fatalf("generate message id: %v", err)
	}
	truckID := "integration-" + messageID
	payload := models.TelemetryPayload{
		TruckID:                  truckID,
		MessageID:                messageID,
		Timestamp:                time.Now().UTC(),
		Latitude:                 40.7128,
		Longitude:                -74.0060,
		EngineTemperatureCelsius: 90,
		SpeedKmh:                 55,
		FuelLevelPercent:         75,
		Source:                   "integration-test",
	}
	body, err := json.Marshal(payload)
	if err != nil {
		t.Fatalf("marshal payload: %v", err)
	}

	startOffsets, err := kafkaNewestOffsets(cfg.Producer.Brokers, cfg.Producer.Topic)
	if err != nil {
		t.Fatalf("read processed offsets: %v", err)
	}

	producerCfg := sarama.NewConfig()
	producerCfg.Producer.Return.Successes = true
	producerCfg.Version = sarama.V2_8_0_0
	rawProducer, err := sarama.NewSyncProducer(cfg.Consumer.Brokers, producerCfg)
	if err != nil {
		t.Fatalf("create raw producer: %v", err)
	}
	t.Cleanup(func() { _ = rawProducer.Close() })

	_, _, err = rawProducer.SendMessage(&sarama.ProducerMessage{
		Topic: cfg.Consumer.Topic,
		Key:   sarama.StringEncoder(truckID),
		Value: sarama.ByteEncoder(body),
	})
	if err != nil {
		t.Fatalf("publish raw message: %v", err)
	}

	if !waitForProcessedMessage(t, cfg.Producer.Brokers, cfg.Producer.Topic, messageID, startOffsets, 45*time.Second) {
		t.Fatalf("processed message %s not found on topic %s", messageID, cfg.Producer.Topic)
	}
}

func kafkaNewestOffsets(brokers []string, topic string) (map[int32]int64, error) {
	clientCfg := sarama.NewConfig()
	clientCfg.Version = sarama.V2_8_0_0
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

func waitForProcessedMessage(t *testing.T, brokers []string, topic, messageID string, startOffsets map[int32]int64, timeout time.Duration) bool {
	t.Helper()

	deadline := time.Now().Add(timeout)
	consumerCfg := sarama.NewConfig()
	consumerCfg.Version = sarama.V2_8_0_0
	kafkaConsumer, err := sarama.NewConsumer(brokers, consumerCfg)
	if err != nil {
		t.Fatalf("create kafka consumer: %v", err)
	}
	defer kafkaConsumer.Close()

	for time.Now().Before(deadline) {
		for partition, offset := range startOffsets {
			pc, err := kafkaConsumer.ConsumePartition(topic, partition, offset)
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
				var processed models.ProcessedTelemetry
				if err := json.Unmarshal(msg.Value, &processed); err != nil {
					pc.Close()
					continue
				}
				if processed.MessageID == messageID {
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

func redisAddresses() []string {
	if v := os.Getenv("REDIS_ADDRESSES"); v != "" {
		return splitCSV(v)
	}
	if v := os.Getenv("REDIS_ADDR"); v != "" {
		return splitCSV(v)
	}
	return []string{"localhost:6379"}
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
