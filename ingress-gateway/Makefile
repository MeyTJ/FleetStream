.PHONY: build test integration loadtest-gate

build:
	go build -o ingress-gateway ./cmd/server

test:
	go test ./...

integration:
	go test -tags=integration -count=1 ./test/integration/...

loadtest-gate:
	go run ./cmd/loadtest --mode=connections --connections=10000 --duration=30s --gate --url=http://localhost:8080/ingest
