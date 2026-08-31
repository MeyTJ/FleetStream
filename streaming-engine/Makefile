.PHONY: build test integration

build:
	go build -o streaming-engine ./cmd/processor

test:
	go test ./...

integration:
	go test -tags=integration -count=1 ./test/integration/...
