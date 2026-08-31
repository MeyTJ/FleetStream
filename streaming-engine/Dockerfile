# Multi-stage build for FleetStream Streaming Engine

FROM golang:1.21-alpine AS builder

WORKDIR /app

RUN apk add --no-cache git

COPY go.mod go.sum ./
RUN go mod download

COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -a -installsuffix cgo -o streaming-engine ./cmd/processor

FROM alpine:3.18

RUN apk --no-cache add ca-certificates

WORKDIR /app
COPY --from=builder /app/streaming-engine .

RUN addgroup -g 1000 fleetstream && adduser -D -u 1000 -G fleetstream fleetstream
USER fleetstream

EXPOSE 9091 8081

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8081/health/live || exit 1

ENTRYPOINT ["./streaming-engine"]
