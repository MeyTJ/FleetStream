package engine

import (
	"context"
	"strings"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/observability"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/kafkasecurity"
)

type SyncPublisher struct {
	producer sarama.SyncProducer
}

func NewSyncPublisher(cfg *config.ProducerConfig) (*SyncPublisher, error) {
	saramaConfig := sarama.NewConfig()
	saramaConfig.Producer.Return.Successes = true
	saramaConfig.Producer.Return.Errors = true
	saramaConfig.Producer.RequiredAcks = sarama.WaitForAll
	saramaConfig.Producer.Retry.Max = cfg.Retries
	saramaConfig.Producer.Idempotent = cfg.Idempotent
	if cfg.Idempotent {
		saramaConfig.Net.MaxOpenRequests = 1
	}
	saramaConfig.Version = sarama.V2_8_0_0
	if err := kafkasecurity.Apply(saramaConfig, kafkasecurity.FromEnv()); err != nil {
		return nil, err
	}

	switch strings.ToLower(cfg.Compression) {
	case "gzip":
		saramaConfig.Producer.Compression = sarama.CompressionGZIP
	case "lz4":
		saramaConfig.Producer.Compression = sarama.CompressionLZ4
	case "zstd":
		saramaConfig.Producer.Compression = sarama.CompressionZSTD
	default:
		saramaConfig.Producer.Compression = sarama.CompressionSnappy
	}

	producer, err := sarama.NewSyncProducer(cfg.Brokers, saramaConfig)
	if err != nil {
		return nil, err
	}
	return &SyncPublisher{producer: producer}, nil
}

func (p *SyncPublisher) Publish(ctx context.Context, topic, key string, value []byte) error {
	msg := &sarama.ProducerMessage{
		Topic:   topic,
		Key:     sarama.StringEncoder(key),
		Value:   sarama.ByteEncoder(value),
		Headers: observability.KafkaHeaders(ctx),
	}
	_, _, err := p.producer.SendMessage(msg)
	return err
}

func (p *SyncPublisher) Close() error {
	return p.producer.Close()
}
