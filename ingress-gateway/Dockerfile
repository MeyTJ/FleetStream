# Multi-stage build for FleetStream Ingress Gateway

# Build stage
FROM golang:1.21-alpine AS builder

WORKDIR /app

# Install build dependencies
RUN apk add --no-cache git make

# Copy go.mod and go.sum
COPY go.mod go.sum ./

# Download dependencies
RUN go mod download

# Copy source code
COPY . .

# Build the application
RUN CGO_ENABLED=0 GOOS=linux go build -a -installsuffix cgo -o ingress-gateway ./cmd/server

# Final stage
FROM alpine:3.18

RUN apk --no-cache add ca-certificates curl

WORKDIR /app

# Copy the binary from builder
COPY --from=builder /app/ingress-gateway .

# Create non-root user
RUN addgroup -g 1000 fleetstream && adduser -D -u 1000 -G fleetstream fleetstream
USER fleetstream

# Expose ports
EXPOSE 50051 8080 9090

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health/live || exit 1

# Run the binary
ENTRYPOINT ["./ingress-gateway"]
