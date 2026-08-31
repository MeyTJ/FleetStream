package observability

import (
	"context"
	"net/http"

	"github.com/google/uuid"
	"github.com/sirupsen/logrus"
	"google.golang.org/grpc"
	"google.golang.org/grpc/metadata"
)

const (
	HeaderCorrelationID = "X-Correlation-Id"
	HeaderTraceparent   = "traceparent"
	mdCorrelationID     = "x-correlation-id"
	mdTraceparent       = "traceparent"
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
	return "c-" + uuid.New().String()[:8]
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

func WithCtx(logger *logrus.Logger, ctx context.Context) *logrus.Entry {
	entry := logger.WithContext(ctx)
	if id := CorrelationID(ctx); id != "" {
		entry = entry.WithField("correlationId", id)
	}
	if tp := Traceparent(ctx); tp != "" {
		entry = entry.WithField("traceparent", tp)
	}
	return entry
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

func UnaryServerInterceptor() grpc.UnaryServerInterceptor {
	return func(ctx context.Context, req interface{}, _ *grpc.UnaryServerInfo, handler grpc.UnaryHandler) (interface{}, error) {
		return handler(fromGRPC(ctx), req)
	}
}

func StreamServerInterceptor() grpc.StreamServerInterceptor {
	return func(srv interface{}, ss grpc.ServerStream, _ *grpc.StreamServerInfo, handler grpc.StreamHandler) error {
		return handler(srv, &wrappedStream{ServerStream: ss, ctx: fromGRPC(ss.Context())})
	}
}

type wrappedStream struct {
	grpc.ServerStream
	ctx context.Context
}

func (w *wrappedStream) Context() context.Context { return w.ctx }

func fromGRPC(ctx context.Context) context.Context {
	var id, tp string
	if md, ok := metadata.FromIncomingContext(ctx); ok {
		if v := md.Get(mdCorrelationID); len(v) > 0 {
			id = v[0]
		}
		if v := md.Get(mdTraceparent); len(v) > 0 {
			tp = v[0]
		}
	}
	ctx, _, _ = Ensure(ctx, id, tp)
	return ctx
}
