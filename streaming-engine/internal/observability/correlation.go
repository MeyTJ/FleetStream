package observability

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"net/http"

	"github.com/IBM/sarama"
	"github.com/sirupsen/logrus"
)

const (
	HeaderCorrelationID = "X-Correlation-Id"
	HeaderTraceparent   = "traceparent"
)

type ctxKey int

const (
	correlationIDKey ctxKey = iota
	traceparentKey
)

func CorrelationID(ctx context.Context) string {
	if ctx == nil {
		return ""
	}
	v, _ := ctx.Value(correlationIDKey).(string)
	return v
}

func Traceparent(ctx context.Context) string {
	if ctx == nil {
		return ""
	}
	v, _ := ctx.Value(traceparentKey).(string)
	return v
}

func WithIDs(ctx context.Context, correlationID, traceparent string) context.Context {
	if ctx == nil {
		ctx = context.Background()
	}
	if correlationID != "" {
		ctx = context.WithValue(ctx, correlationIDKey, correlationID)
	}
	if traceparent != "" {
		ctx = context.WithValue(ctx, traceparentKey, traceparent)
	}
	return ctx
}

func NewCorrelationID() string {
	b := make([]byte, 4)
	if _, err := rand.Read(b); err != nil {
		return "c-unknown"
	}
	return "c-" + hex.EncodeToString(b)
}

func Ensure(ctx context.Context, correlationID, traceparent string) (context.Context, string, string) {
	if correlationID == "" {
		correlationID = CorrelationID(ctx)
	}
	if correlationID == "" {
		correlationID = NewCorrelationID()
	}
	if traceparent == "" {
		traceparent = Traceparent(ctx)
	}
	return WithIDs(ctx, correlationID, traceparent), correlationID, traceparent
}

func FromKafkaHeaders(headers []*sarama.RecordHeader) (string, string) {
	var correlationID, traceparent string
	for _, h := range headers {
		switch string(h.Key) {
		case HeaderCorrelationID:
			correlationID = string(h.Value)
		case HeaderTraceparent:
			traceparent = string(h.Value)
		}
	}
	return correlationID, traceparent
}

func KafkaHeaders(ctx context.Context) []sarama.RecordHeader {
	var headers []sarama.RecordHeader
	if id := CorrelationID(ctx); id != "" {
		headers = append(headers, sarama.RecordHeader{
			Key:   []byte(HeaderCorrelationID),
			Value: []byte(id),
		})
	}
	if tp := Traceparent(ctx); tp != "" {
		headers = append(headers, sarama.RecordHeader{
			Key:   []byte(HeaderTraceparent),
			Value: []byte(tp),
		})
	}
	return headers
}

type ContextHook struct{}

func (ContextHook) Levels() []logrus.Level { return logrus.AllLevels }

func (ContextHook) Fire(e *logrus.Entry) error {
	if e.Context == nil {
		return nil
	}
	if id := CorrelationID(e.Context); id != "" {
		e.Data["correlationId"] = id
	}
	if tp := Traceparent(e.Context); tp != "" {
		e.Data["traceparent"] = tp
	}
	return nil
}

func Middleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		ctx, id, _ := Ensure(r.Context(), r.Header.Get(HeaderCorrelationID), r.Header.Get(HeaderTraceparent))
		w.Header().Set(HeaderCorrelationID, id)
		next.ServeHTTP(w, r.WithContext(ctx))
	})
}
