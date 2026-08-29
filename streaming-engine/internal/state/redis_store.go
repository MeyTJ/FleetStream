// Package state provides Redis-based state management for truck telemetry.
package state

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/redis/go-redis/v9"
	"github.com/sirupsen/logrus"
)

// Key prefixes for Redis
const (
	TruckStatePrefix = "truck:state:"
	DedupPrefix      = "dedup:"
	CheckpointPrefix = "checkpoint:"
)

// RedisStateStore manages truck state in Redis
type RedisStateStore struct {
	client  *redis.Client
	cfg     *config.RedisConfig
	logger  *logrus.Logger
	metrics *metrics.Metrics
}

// NewRedisStateStore creates a new Redis state store
func NewRedisStateStore(cfg *config.RedisConfig, logger *logrus.Logger, m *metrics.Metrics) (*RedisStateStore, error) {
	client := redis.NewClusterClient(&redis.ClusterOptions{
		Addrs:    cfg.Addresses,
		Password: cfg.Password,
		DB:       cfg.DB,
		PoolSize: cfg.PoolSize,
	})

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := client.Ping(ctx).Err(); err != nil {
		return nil, fmt.Errorf("failed to connect to Redis: %w", err)
	}

	return &RedisStateStore{
		client:  client,
		cfg:     cfg,
		logger:  logger,
		metrics: m,
	}, nil
}

// GetTruckState retrieves the current state of a truck
func (s *RedisStateStore) GetTruckState(ctx context.Context, truckID string) (*models.TruckState, error) {
	key := TruckStatePrefix + truckID
	val, err := s.client.Get(ctx, key).Bytes()
	if err == redis.Nil {
		s.metrics.RedisOperations.WithLabelValues("get", "miss").Inc()
		return nil, nil
	}
	if err != nil {
		s.metrics.RedisErrors.WithLabelValues("get").Inc()
		return nil, err
	}

	var state models.TruckState
	if err := json.Unmarshal(val, &state); err != nil {
		return nil, err
	}

	s.metrics.RedisOperations.WithLabelValues("get", "hit").Inc()
	return &state, nil
}

// SetTruckState saves the current state of a truck
func (s *RedisStateStore) SetTruckState(ctx context.Context, state *models.TruckState) error {
	key := TruckStatePrefix + state.TruckID
	data, err := json.Marshal(state)
	if err != nil {
		return err
	}

	if err := s.client.Set(ctx, key, data, s.cfg.StateTTL).Err(); err != nil {
		s.metrics.RedisErrors.WithLabelValues("set").Inc()
		return err
	}

	return nil
}

// CheckDuplicate checks if a message has already been processed using SETNX
func (s *RedisStateStore) CheckDuplicate(ctx context.Context, messageID string) (bool, error) {
	key := DedupPrefix + messageID

	added, err := s.client.SetNX(ctx, key, "1", s.cfg.DedupTTL).Result()
	if err != nil {
		s.metrics.RedisErrors.WithLabelValues("dedup_check").Inc()
		return false, err
	}

	isDuplicate := !added
	if isDuplicate {
		s.metrics.DuplicatesDropped.WithLabelValues("redis").Inc()
	}

	return isDuplicate, nil
}

// SaveCheckpoint saves the current consumer offset
func (s *RedisStateStore) SaveCheckpoint(ctx context.Context, topic string, partition int32, offset int64) error {
	key := fmt.Sprintf("%s%s:%d", CheckpointPrefix, topic, partition)
	return s.client.Set(ctx, key, offset, 0).Err()
}

// GetCheckpoint retrieves the last committed offset
func (s *RedisStateStore) GetCheckpoint(ctx context.Context, topic string, partition int32) (int64, error) {
	key := fmt.Sprintf("%s%s:%d", CheckpointPrefix, topic, partition)
	offset, err := s.client.Get(ctx, key).Int64()
	if err == redis.Nil {
		return -1, nil
	}
	return offset, err
}

// Close closes the Redis connection
func (s *RedisStateStore) Close() error {
	return s.client.Close()
}

// TruckStateUpdate represents an update to truck state
type TruckStateUpdate struct {
	TruckID    string
	Timestamp  int64
	MessageID  string
	Latitude   float64
	Longitude  float64
	Speed      float64
	EngineTemp float64
	FuelLevel  float32
}
