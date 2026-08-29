// Package circuit implements the circuit breaker pattern for resilience.
package circuit

import (
	"errors"
	"sync/atomic"
	"time"

	"github.com/sony/gobreaker"
)

// State represents the circuit breaker state
type State int

const (
	StateClosed State = iota
	StateHalfOpen
	StateOpen
)

func (s State) String() string {
	switch s {
	case StateClosed:
		return "closed"
	case StateHalfOpen:
		return "half-open"
	case StateOpen:
		return "open"
	default:
		return "unknown"
	}
}

// CircuitBreaker implements the circuit breaker pattern
type CircuitBreaker struct {
	cb        *gobreaker.CircuitBreaker
	name      string
	failures  atomic.Int64
	threshold int64
	timeout   time.Duration
}

// NewCircuitBreaker creates a new circuit breaker
func NewCircuitBreaker(name string, threshold int64, timeout time.Duration) *CircuitBreaker {
	cb := gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        name,
		MaxRequests: 3,                  // Requests allowed in half-open state
		Interval:    10 * time.Second,   // Cyclic period for clearing counts
		Timeout:     timeout,            // Time the circuit stays open
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			failureRatio := float64(counts.TotalFailures) / float64(counts.Requests)
			return counts.TotalFailures >= threshold || failureRatio >= 0.6
		},
		OnStateChange: func(name string, from, to gobreaker.State) {
			// State change callback - could log here
		},
	})

	return &CircuitBreaker{
		cb:        cb,
		name:      name,
		threshold: threshold,
		timeout:   timeout,
	}
}

// Execute runs a function through the circuit breaker
func (cb *CircuitBreaker) Execute(fn func() error) error {
	result, err := cb.cb.Execute(func() (interface{}, error) {
		return nil, fn()
	})
	if err != nil {
		return err
	}
	return result.(error)
}

// IsAvailable returns true if the circuit is allowing requests
func (cb *CircuitBreaker) IsAvailable() bool {
	return cb.cb.State() != gobreaker.StateOpen
}

// GetState returns the current circuit state
func (cb *CircuitBreaker) GetState() State {
	switch cb.cb.State() {
	case gobreaker.StateClosed:
		return StateClosed
	case gobreaker.StateHalfOpen:
		return StateHalfOpen
	case gobreaker.StateOpen:
		return StateOpen
	default:
		return StateClosed
	}
}

// GetFailureCount returns the number of consecutive failures
func (cb *CircuitBreaker) GetFailureCount() int64 {
	return cb.failures.Load()
}

// MultiCircuitBreaker manages multiple circuit breakers
type MultiCircuitBreaker struct {
	breakers map[string]*CircuitBreaker
}

// NewMultiCircuitBreaker creates a manager for multiple circuit breakers
func NewMultiCircuitBreaker() *MultiCircuitBreaker {
	return &MultiCircuitBreaker{
		breakers: make(map[string]*CircuitBreaker),
	}
}

// Register registers a new circuit breaker
func (m *MultiCircuitBreaker) Register(name string, threshold int64, timeout time.Duration) *CircuitBreaker {
	cb := NewCircuitBreaker(name, threshold, timeout)
	m.breakers[name] = cb
	return cb
}

// Get retrieves a circuit breaker by name
func (m *MultiCircuitBreaker) Get(name string) (*CircuitBreaker, bool) {
	cb, ok := m.breakers[name]
	return cb, ok
}

// ExecuteOn executes a function on a specific circuit breaker
func (m *MultiCircuitBreaker) ExecuteOn(name string, fn func() error) error {
	cb, ok := m.breakers[name]
	if !ok {
		return fn() // Execute without circuit breaker if not found
	}
	return cb.Execute(fn)
}

// ErrCircuitOpen is returned when circuit is open
var ErrCircuitOpen = errors.New("circuit breaker is open")
