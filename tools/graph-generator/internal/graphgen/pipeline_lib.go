//go:build cgo

package graphgen

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/sjhorn/graphify/pkg/analyze"
	"github.com/sjhorn/graphify/pkg/cache"
	"github.com/sjhorn/graphify/pkg/cluster"
	"github.com/sjhorn/graphify/pkg/detect"
	"github.com/sjhorn/graphify/pkg/export"
	"github.com/sjhorn/graphify/pkg/extract"
	"github.com/sjhorn/graphify/pkg/graph"
	"github.com/sjhorn/graphify/pkg/report"
)

func runIsolatedPipeline(sourcePath, outputDir, projectName string, formats []string) (pipelineResult, error) {
	start := time.Now()

	if err := os.MkdirAll(outputDir, 0o755); err != nil {
		return pipelineResult{}, fmt.Errorf("create output directory: %w", err)
	}

	detected := detect.CollectFiles(sourcePath)
	files := append([]string{}, detected.Files[detect.FileTypeCode]...)
	files = append(files, detected.Files[detect.FileTypeDocument]...)

	var nodes []extract.Node
	var edges []extract.Edge
	for _, file := range files {
		n, e := extractOne(file, outputDir)
		nodes = append(nodes, n...)
		edges = append(edges, e...)
	}

	g := graph.NewGraph()
	absRoot, err := filepath.Abs(sourcePath)
	if err != nil {
		absRoot = sourcePath
	}

	for _, node := range nodes {
		g.AddNode(node.ID, node.Label, node.Type, relativize(node.File, absRoot))
	}
	for _, edge := range edges {
		g.AddEdge(edge.Source, edge.Target, edge.Relation, edge.Confidence, edge.Weight)
	}

	clustered := cluster.Cluster(g)
	clustered = cluster.SplitLargeCommunities(g, clustered, 100)
	clustered = cluster.MergeTinyCommunities(g, clustered, 3)
	cohesion := cluster.ScoreAll(g, clustered.Communities)
	labels := communityLabels(g, clustered.Communities)

	analysis := analyze.Analyze(g, clustered.Communities, analyze.DetectResultInfo{
		TotalFiles: detected.TotalFiles,
		TotalWords: detected.TotalWords,
		CodeFiles:  len(detected.Files[detect.FileTypeCode]),
		DocFiles:   len(detected.Files[detect.FileTypeDocument]),
	})
	analyze.ExtractDocstrings(analysis.GodNodeDetails, absRoot, g)

	generated, err := exportFormats(formats, g, clustered, cohesion, labels, analysis, detected, outputDir, sourcePath)
	if err != nil {
		return pipelineResult{}, err
	}

	_ = projectName
	return pipelineResult{
		Nodes:          g.NodeCount(),
		Edges:          g.EdgeCount(),
		Communities:    len(clustered.Communities),
		GeneratedFiles: generated,
		Duration:       time.Since(start),
	}, nil
}

func extractOne(file, outputDir string) (nodes []extract.Node, edges []extract.Edge) {
	defer func() {
		if recover() != nil {
			nodes, edges = nil, nil
		}
	}()

	if cached, ok := cache.LoadCached(file, outputDir); ok && cached != nil {
		return cached.Nodes, cached.Edges
	}

	extraction := extract.Extract([]string{file}, "")
	if extraction == nil {
		return nil, nil
	}
	_ = cache.SaveCached(file, extraction, outputDir)
	return extraction.Nodes, extraction.Edges
}

func exportFormats(
	formats []string,
	g *graph.Graph,
	clustered *cluster.ClusterResult,
	cohesion map[int]float64,
	labels map[int]string,
	analysis *analyze.Analysis,
	detected *detect.DetectResult,
	outputDir, sourcePath string,
) ([]string, error) {
	var generated []string

	for _, format := range formats {
		switch format {
		case "json":
			path := filepath.Join(outputDir, "graph.json")
			if err := export.ToJSON(g, clustered, path); err != nil {
				return generated, fmt.Errorf("export json: %w", err)
			}
			generated = append(generated, path)
		case "html":
			path := filepath.Join(outputDir, "graph.html")
			if err := export.ToHTML(g, clustered, path, labels); err != nil {
				return generated, fmt.Errorf("export html: %w", err)
			}
			generated = append(generated, path)
		case "cypher", "neo4j":
			path := filepath.Join(outputDir, "graph.cypher")
			if err := export.ToCypher(g, path); err != nil {
				return generated, fmt.Errorf("export cypher: %w", err)
			}
			generated = append(generated, path)
		case "graphml":
			path := filepath.Join(outputDir, "graph.graphml")
			if err := export.ToGraphML(g, clustered, path); err != nil {
				return generated, fmt.Errorf("export graphml: %w", err)
			}
			generated = append(generated, path)
		case "report":
			content := report.Generate(
				g,
				clustered.Communities,
				cohesion,
				labels,
				analysis,
				report.DetectionResult{
					TotalFiles: detected.TotalFiles,
					TotalWords: detected.TotalWords,
					NeedsGraph: detected.NeedsGraph,
					Warning:    detected.Warning,
				},
				report.TokenInfo{},
				sourcePath,
			)
			path := filepath.Join(outputDir, "GRAPH_REPORT.md")
			if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
				return generated, fmt.Errorf("write report: %w", err)
			}
			generated = append(generated, path)
		}
	}

	return generated, nil
}

func relativize(file, root string) string {
	if file == "" {
		return file
	}
	abs, err := filepath.Abs(file)
	if err != nil {
		return file
	}
	rel, err := filepath.Rel(root, abs)
	if err != nil {
		return file
	}
	return rel
}

func communityLabels(g *graph.Graph, communities map[int][]string) map[int]string {
	labels := make(map[int]string, len(communities))
	for cid, nodeIDs := range communities {
		dirCounts := make(map[string]int)
		bestNode := ""
		bestScore := -999

		for _, nid := range nodeIDs {
			node := g.GetNode(nid)
			if node == nil {
				continue
			}
			if node.File != "" {
				dirCounts[filepath.Base(filepath.Dir(node.File))]++
			}
			score := labelScore(node.Label, node.Type, g.GetNodeDegree(nid))
			if score > bestScore {
				bestScore = score
				bestNode = node.Label
			}
		}

		topDir, topCount := "", 0
		for dir, count := range dirCounts {
			if count > topCount {
				topCount = count
				topDir = dir
			}
		}

		switch {
		case topDir != "" && bestNode != "":
			labels[cid] = topDir + " / " + bestNode
		case topDir != "":
			labels[cid] = topDir
		case bestNode != "":
			labels[cid] = bestNode
		default:
			labels[cid] = fmt.Sprintf("Community %d", cid)
		}
	}
	return labels
}

func labelScore(label, nodeType string, degree int) int {
	if nodeType == "module" {
		return -1
	}
	score := degree
	switch nodeType {
	case "class", "mixin", "extension":
		score += 100
	case "enum":
		score += 90
	case "function":
		score += 50
	case "method":
		score += 30
	case "variable":
		score += 10
	case "file":
		score -= 50
	}
	if label == "main()" {
		score -= 200
	}
	if strings.HasPrefix(label, "_") {
		score -= 20
	}
	return score
}
