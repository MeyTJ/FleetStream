package processors

import (
	"context"
	"errors"
	"hash/fnv"
	"sync"
	"time"

	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/google/uuid"
	"github.com/sirupsen/logrus"
)

// ErrPoolFull is returned when the worker pool's queue is full
var ErrPoolFull = errors.New("worker pool queue is full, dropping event")

// ErrShuttingDown is returned when the pool is being shut down
var ErrShuttingDown = errors.New("worker pool is shutting down")

// Job represents a unit of work to be processed by a worker
type Job struct {
	Payload  models.TelemetryPayload
	Source   string
	Enqueued time.Time
}

// Shard is a single shard within the worker pool
type Shard struct {
	id       int
	jobs     chan Job
	workers  []*Worker
	wg       sync.WaitGroup
	closed   atomic.Bool
}

// Worker processes jobs from its shard's channel
type Worker struct {
	id      int
	shard   *Shard
	jobChan chan Job
}

// ShardedPool is a sharded worker pool for high-throughput processing.
// The pool distributes work across multiple shards to reduce contention.
type ShardedPool struct {
	shards     []*Shard
	numShards  int
	queueSize  int
	logger     *logrus.Logger
	closed     atomic.Bool
	
	// Stats
	enqueued   atomic.Uint64
	processed  atomic.Uint64
	dropped    atomic.Uint64
}

// PoolConfig configures the sharded worker pool
type PoolConfig struct {
	NumShards    int
	WorkersPerShard int
	QueueSize    int
	Logger       *logrus.Logger
}

// NewShardedPool creates a new sharded worker pool
func NewShardedPool(cfg PoolConfig) (*ShardedPool, error) {
	if cfg.NumShards <= 0 {
		return nil, errors.New("num_shards must be positive")
	}
	if cfg.WorkersPerShard <= 0 {
		return nil, errors.New("workers_per_shard must be positive")
	}
	if cfg.QueueSize <= 0 {
		return nil, errors.New("queue_size must be positive")
	}
	if cfg.Logger == nil {
		return nil, errors.New("logger is required")
	}

	pool := &ShardedPool{
		numShards: cfg.NumShards,
		queueSize: cfg.QueueSize,
		logger:    cfg.Logger,
	}

	pool.shards = make([]*Shard, cfg.NumShards)
	for i := 0; i < cfg.NumShards; i++ {
		shard := &Shard{
			id:        i,
			jobs:      make(chan Job, cfg.QueueSize),
		}
		shard.workers = make([]*Worker, cfg.WorkersPerShard)
		for j := 0; j < cfg.WorkersPerShard; j++ {
			shard.workers[j] = &Worker{
				id:      j,
				shard:   shard,
				jobChan: shard.jobs,
			}
		}
		pool.shards[i] = shard
	}

	return pool, nil
}

// Start starts all workers in the pool
func (p *ShardedPool) Start(ctx context.Context) {
	for _, shard := range p.shards {
		for _, worker := range shard.workers {
			shard.wg.Add(1)
			go worker.run(ctx)
		}
	}
	p.logger.WithField("shards", p.numShards).
		WithField("workers_per_shard", len(p.shards[0].workers)).
		Info("sharded worker pool started")
}

// Submit submits a job to the pool, selecting the appropriate shard based on payload.
// Returns ErrPoolFull if the pool is full and drop_on_full is enabled.
func (p *ShardedPool) Submit(ctx context.Context, payload models.TelemetryPayload, source string) error {
	if p.closed.Load() {
		return ErrShuttingDown
	}

	if payload.MessageID == "" {
		payload.MessageID = uuid.New().String()
	}

	// Select shard based on truck_id for consistent ordering
	shard := p.selectShard(payload.TruckID)
	
	job := Job{
		Payload:  payload,
		Source:   source,
		Enqueued: time.Now(),
	}

	// Non-blocking enqueue with backpressure
	select {
	case shard.jobs <- job:
		p.enqueued.Add(1)
		return nil
	default:
		// Pool is full - apply backpressure
		p.dropped.Add(1)
		return ErrPoolFull
	}
}

// SubmitBatch submits multiple jobs to the pool
func (p *ShardedPool) SubmitBatch(ctx context.Context, payloads []models.TelemetryPayload, source string) error {
	for _, payload := range payloads {
		if err := p.Submit(ctx, payload, source); err != nil {
			return err
		}
	}
	return nil
}

// selectShard selects a shard based on the truck_id for consistent partitioning
func (p *ShardedPool) selectShard(key string) *Shard {
	hash := fnv.New32a()
	hash.Write([]byte(key))
	idx := hash.Sum32() % uint32(p.numShards)
	return p.shards[idx]
}

// Shutdown gracefully shuts down the worker pool
func (p *ShardedPool) Shutdown(ctx context.Context) error {
	if !p.closed.CompareAndSwap(false, true) {
		return nil
	}

	p.logger.Info("shutting down sharded worker pool")

	// Close all shard job channels
	for _, shard := range p.shards {
		shard.closed.Store(true)
		close(shard.jobs)
	}

	// Wait for all workers to complete with timeout
	done := make(chan struct{})
	go func() {
		for _, shard := range p.shards {
			shard.wg.Wait()
		}
		close(done)
	}()

	select {
	case <-done:
		p.logger.Info("sharded worker pool shutdown complete")
		return nil
	case <-ctx.Done():
		p.logger.Warn("sharded worker pool shutdown timeout")
		return ctx.Err()
	}
}

// Stats returns current pool statistics
func (p *ShardedPool) Stats() PoolStats {
	queueDepth := 0
	for _, shard := range p.shards {
		queueDepth += len(shard.jobs)
	}

	return PoolStats{
		Enqueued:  p.enqueued.Load(),
		Processed: p.processed.Load(),
		Dropped:   p.dropped.Load(),
		QueueDepth: queueDepth,
		QueueCapacity: p.numShards * p.queueSize,
		Shards:    p.numShards,
	}
}

// PoolStats represents worker pool statistics
type PoolStats struct {
	Enqueued  uint64
	Processed uint64
	Dropped   uint64
	QueueDepth int
	QueueCapacity int
	Shards    int
}

// run is the worker's main loop
func (w *Worker) run(ctx context.Context) {
	defer w.shard.wg.Done()

	for {
		select {
		case <-ctx.Done():
			return
		case job, ok := <-w.jobChan:
			if !ok {
				return
			}
			w.processJob(ctx, job)
		}
	}
}

// processJob processes a single job
func (w *Worker) processJob(ctx context.Context, job Job) {
	// This would be where processing happens in the real implementation
	// For now, we just increment the processed counter
	w.shard.workers[0].shard.processed.Add(1)
}