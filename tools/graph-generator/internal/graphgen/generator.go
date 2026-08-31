package graphgen

import (
	"context"
	"fmt"
	"log/slog"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"time"
	"unicode"

	"github.com/fleetstream/graph-generator/internal/graph"
	"golang.org/x/sync/errgroup"
)

type projectDiscoverer interface {
	Discover(ctx context.Context, root string) ([]graph.Project, error)
}

type Generator struct {
	cfg        graph.Config
	log        *slog.Logger
	discoverer projectDiscoverer
}

func NewGraphGenerator(cfg graph.Config, log *slog.Logger) *Generator {
	if log == nil {
		log = slog.Default()
	}
	cfg = withDefaults(cfg)
	return &Generator{
		cfg:        cfg,
		log:        log,
		discoverer: NewDiscoverer(),
	}
}

func withDefaults(cfg graph.Config) graph.Config {
	if strings.TrimSpace(cfg.OutputDirectory) == "" {
		cfg.OutputDirectory = filepath.Join("outputs", "graphs")
	}
	if len(cfg.ExportFormats) == 0 {
		cfg.ExportFormats = []string{"json", "html", "report"}
	}
	if cfg.Concurrency <= 0 {
		cfg.Concurrency = runtime.GOMAXPROCS(0)
	}
	if cfg.PerProjectTimeout <= 0 {
		cfg.PerProjectTimeout = 10 * time.Minute
	}
	return cfg
}

func (g *Generator) Discover(ctx context.Context, repositoryRoot string) ([]graph.Project, error) {
	return g.discoverer.Discover(ctx, repositoryRoot)
}

func (g *Generator) GenerateProject(ctx context.Context, req graph.GenerateProjectRequest) (graph.ProjectResult, error) {
	if strings.TrimSpace(req.ProjectPath) == "" {
		return graph.ProjectResult{}, fmt.Errorf("project path is required")
	}
	if strings.TrimSpace(req.ProjectName) == "" {
		return graph.ProjectResult{}, fmt.Errorf("project name is required")
	}

	info, err := os.Stat(req.ProjectPath)
	if err != nil {
		return graph.ProjectResult{}, fmt.Errorf("project directory: %w", err)
	}
	if !info.IsDir() {
		return graph.ProjectResult{}, fmt.Errorf("project path is not a directory: %s", req.ProjectPath)
	}

	outputRoot := req.OutputDirectory
	if strings.TrimSpace(outputRoot) == "" {
		outputRoot = g.cfg.OutputDirectory
	}
	outputRoot, err = filepath.Abs(outputRoot)
	if err != nil {
		return graph.ProjectResult{}, fmt.Errorf("resolve output directory: %w", err)
	}

	projectOut := filepath.Join(outputRoot, sanitizeFileName(req.ProjectName))
	if err := os.MkdirAll(outputRoot, 0o755); err != nil {
		return graph.ProjectResult{}, fmt.Errorf("create output root: %w", err)
	}
	if err := os.RemoveAll(projectOut); err != nil {
		return graph.ProjectResult{}, fmt.Errorf("reset project output: %w", err)
	}

	formats := req.ExportFormats
	if len(formats) == 0 {
		formats = g.cfg.ExportFormats
	}
	formats = normalizeFormats(formats)

	ctx, cancel := context.WithTimeout(ctx, g.cfg.PerProjectTimeout)
	defer cancel()

	g.log.Info("generating isolated graph", "project", req.ProjectName, "path", req.ProjectPath)

	type outcome struct {
		result pipelineResult
		err    error
	}
	done := make(chan outcome, 1)
	go func() {
		res, runErr := runIsolatedPipeline(req.ProjectPath, projectOut, req.ProjectName, formats)
		done <- outcome{result: res, err: runErr}
	}()

	select {
	case <-ctx.Done():
		return graph.ProjectResult{}, fmt.Errorf("graph generation timed out for %s: %w", req.ProjectName, ctx.Err())
	case out := <-done:
		if out.err != nil {
			return graph.ProjectResult{}, fmt.Errorf("generate %s: %w", req.ProjectName, out.err)
		}
		files := make([]string, 0, len(out.result.GeneratedFiles))
		for _, f := range out.result.GeneratedFiles {
			files = append(files, filepath.Base(f))
		}
		return graph.ProjectResult{
			ProjectName:    req.ProjectName,
			ProjectPath:    req.ProjectPath,
			OutputPath:     projectOut,
			Success:        true,
			Nodes:          out.result.Nodes,
			Edges:          out.result.Edges,
			Communities:    out.result.Communities,
			GeneratedFiles: files,
			Duration:       out.result.Duration,
		}, nil
	}
}

func (g *Generator) GenerateAll(ctx context.Context, req graph.GenerateAllRequest) (graph.BatchResult, error) {
	start := time.Now()

	if _, err := os.Stat(req.RepositoryRoot); err != nil {
		return graph.BatchResult{}, fmt.Errorf("repository root: %w", err)
	}

	projects, err := g.Discover(ctx, req.RepositoryRoot)
	if err != nil {
		return graph.BatchResult{}, err
	}
	if len(projects) == 0 {
		return graph.BatchResult{}, fmt.Errorf("no analyzable projects found in %s", req.RepositoryRoot)
	}

	projects = filterProjects(projects, req.ProjectFilter)
	if len(projects) == 0 {
		return graph.BatchResult{}, fmt.Errorf("project filter matched no projects")
	}

	outputDir := req.OutputDirectory
	if strings.TrimSpace(outputDir) == "" {
		outputDir = g.cfg.OutputDirectory
	}
	formats := req.ExportFormats
	if len(formats) == 0 {
		formats = g.cfg.ExportFormats
	}
	failFast := req.FailFast || g.cfg.FailFast

	g.log.Info("starting batch graph generation",
		"root", req.RepositoryRoot,
		"projects", len(projects),
		"concurrency", g.cfg.Concurrency)

	var (
		mu      sync.Mutex
		results []graph.ProjectResult
	)

	eg, ctx := errgroup.WithContext(ctx)
	eg.SetLimit(g.cfg.Concurrency)

	for _, project := range projects {
		project := project
		eg.Go(func() error {
			res, genErr := g.GenerateProject(ctx, graph.GenerateProjectRequest{
				ProjectPath:     project.Path,
				ProjectName:     project.Name,
				OutputDirectory: outputDir,
				ExportFormats:   formats,
			})

			mu.Lock()
			defer mu.Unlock()

			if genErr != nil {
				g.log.Warn("project graph failed", "project", project.Name, "err", genErr)
				results = append(results, graph.ProjectResult{
					ProjectName: project.Name,
					ProjectPath: project.Path,
					Success:     false,
					Error:       genErr.Error(),
				})
				if failFast {
					return genErr
				}
				return nil
			}

			results = append(results, res)
			return nil
		})
	}

	waitErr := eg.Wait()

	successful := 0
	for _, r := range results {
		if r.Success {
			successful++
		}
	}

	batch := graph.BatchResult{
		Success:            waitErr == nil && successful == len(projects),
		Results:            results,
		TotalProjects:      len(projects),
		SuccessfulProjects: successful,
		FailedProjects:     len(projects) - successful,
		Duration:           time.Since(start),
	}
	if waitErr != nil && failFast {
		return batch, waitErr
	}
	return batch, nil
}

func filterProjects(projects []graph.Project, filter []string) []graph.Project {
	if len(filter) == 0 {
		return projects
	}
	wanted := make(map[string]struct{}, len(filter))
	for _, name := range filter {
		wanted[strings.ToLower(strings.TrimSpace(name))] = struct{}{}
	}
	filtered := make([]graph.Project, 0, len(projects))
	for _, p := range projects {
		if _, ok := wanted[strings.ToLower(p.Name)]; ok {
			filtered = append(filtered, p)
		}
	}
	return filtered
}

func normalizeFormats(formats []string) []string {
	seen := make(map[string]struct{}, len(formats))
	out := make([]string, 0, len(formats))
	for _, f := range formats {
		f = strings.ToLower(strings.TrimSpace(f))
		if f == "" {
			continue
		}
		if _, ok := seen[f]; ok {
			continue
		}
		seen[f] = struct{}{}
		out = append(out, f)
	}
	if len(out) == 0 {
		return []string{"json", "html", "report"}
	}
	return out
}

func sanitizeFileName(name string) string {
	var b strings.Builder
	b.Grow(len(name))
	for _, r := range name {
		switch {
		case unicode.IsLetter(r), unicode.IsDigit(r), r == '-', r == '_', r == '.':
			b.WriteRune(r)
		default:
			b.WriteByte('_')
		}
	}
	cleaned := strings.Trim(b.String(), "._")
	if cleaned == "" {
		return "project"
	}
	return cleaned
}

var _ graph.GraphGenerator = (*Generator)(nil)
