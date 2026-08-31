package graphgen

import "time"

type pipelineResult struct {
	Nodes          int
	Edges          int
	Communities    int
	GeneratedFiles []string
	Duration       time.Duration
}
