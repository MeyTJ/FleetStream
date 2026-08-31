package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"github.com/fleetstream/graph-generator/internal/graph"
	"github.com/fleetstream/graph-generator/internal/graphgen"
)

func main() {
	args := os.Args[1:]
	if len(args) > 0 && args[0] == "--" {
		args = args[1:]
	}
	os.Exit(run(args))
}

func run(args []string) int {
	fs := flag.NewFlagSet("graph-generator", flag.ContinueOnError)
	fs.SetOutput(os.Stderr)

	output := fs.String("output", filepath.Join("outputs", "graphs"), "output root directory")
	fs.StringVar(output, "o", filepath.Join("outputs", "graphs"), "output root directory (shorthand)")
	filter := fs.String("filter", "", "comma-separated project name filter")
	fs.StringVar(filter, "f", "", "comma-separated project name filter (shorthand)")
	format := fs.String("format", "json,html,report", "comma-separated export formats")
	concurrency := fs.Int("concurrency", 0, "max concurrent project graphs (default: GOMAXPROCS)")
	failFast := fs.Bool("fail-fast", false, "stop on first project failure")
	discoverOnly := fs.Bool("discover", false, "list discovered projects and exit")
	projectPath := fs.String("project-path", "", "generate a single project by directory path")
	projectName := fs.String("project-name", "", "project name (required with --project-path)")
	jsonOut := fs.Bool("json", false, "emit single-project result as JSON")
	timeout := fs.Duration("timeout", 10*time.Minute, "per-project generation timeout")
	help := fs.Bool("help", false, "show help")
	fs.BoolVar(help, "h", false, "show help (shorthand)")

	if err := fs.Parse(args); err != nil {
		return 2
	}
	if *help {
		printUsage(fs)
		return 0
	}

	root := fs.Arg(0)
	if root == "" {
		root = findRepoRoot()
	}

	log := slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelInfo}))
	gen := graphgen.NewGraphGenerator(graph.Config{
		OutputDirectory:   *output,
		ExportFormats:     splitCSV(*format),
		Concurrency:       *concurrency,
		FailFast:          *failFast,
		PerProjectTimeout: *timeout,
	}, log)

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	if *discoverOnly {
		projects, err := gen.Discover(ctx, root)
		if err != nil {
			fmt.Fprintf(os.Stderr, "FAILED: %v\n", err)
			return 1
		}
		for _, p := range projects {
			fmt.Printf("%s\t%s\t%s\n", p.Name, p.Type, p.Path)
		}
		fmt.Printf("Total: %d\n", len(projects))
		return 0
	}

	if strings.TrimSpace(*projectPath) != "" {
		name := strings.TrimSpace(*projectName)
		if name == "" {
			name = filepath.Base(*projectPath)
		}

		res, err := gen.GenerateProject(ctx, graph.GenerateProjectRequest{
			ProjectPath:     *projectPath,
			ProjectName:     name,
			OutputDirectory: *output,
			ExportFormats:   splitCSV(*format),
		})
		if err != nil {
			if *jsonOut {
				_ = json.NewEncoder(os.Stdout).Encode(graph.ProjectResult{
					ProjectName: name,
					ProjectPath: *projectPath,
					Success:     false,
					Error:       err.Error(),
				})
			} else {
				fmt.Fprintf(os.Stderr, "FAILED: %v\n", err)
			}
			return 1
		}

		if *jsonOut {
			if err := json.NewEncoder(os.Stdout).Encode(res); err != nil {
				fmt.Fprintf(os.Stderr, "FAILED: %v\n", err)
				return 1
			}
			return 0
		}

		fmt.Printf("%s: nodes=%d edges=%d communities=%d output=%s\n",
			res.ProjectName, res.Nodes, res.Edges, res.Communities, res.OutputPath)
		return 0
	}

	batch, err := gen.GenerateAll(ctx, graph.GenerateAllRequest{
		RepositoryRoot:  root,
		OutputDirectory: *output,
		ExportFormats:   splitCSV(*format),
		ProjectFilter:   splitCSV(*filter),
		FailFast:        *failFast,
	})
	if err != nil && batch.TotalProjects == 0 {
		fmt.Fprintf(os.Stderr, "FAILED: %v\n", err)
		return 1
	}

	for _, item := range batch.Results {
		if item.Success {
			fmt.Printf("%s: nodes=%d edges=%d communities=%d output=%s\n",
				item.ProjectName, item.Nodes, item.Edges, item.Communities, item.OutputPath)
			continue
		}
		fmt.Fprintf(os.Stderr, "ERROR %s: %s\n", item.ProjectName, item.Error)
	}

	fmt.Printf("Done: %d/%d succeeded in %.1fs\n",
		batch.SuccessfulProjects, batch.TotalProjects, batch.Duration.Seconds())

	if !batch.Success {
		return 1
	}
	return 0
}

func findRepoRoot() string {
	dir, err := os.Getwd()
	if err != nil {
		return "."
	}
	for {
		if _, err := os.Stat(filepath.Join(dir, ".git")); err == nil {
			return dir
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			cwd, _ := os.Getwd()
			return cwd
		}
		dir = parent
	}
}

func splitCSV(value string) []string {
	if strings.TrimSpace(value) == "" {
		return nil
	}
	parts := strings.Split(value, ",")
	out := make([]string, 0, len(parts))
	for _, p := range parts {
		p = strings.TrimSpace(p)
		if p != "" {
			out = append(out, p)
		}
	}
	return out
}

func printUsage(fs *flag.FlagSet) {
	fmt.Fprintln(os.Stderr, `FleetStream Graph Generator — isolated per-project Graphify graphs

Usage:
  go run ./cmd/graph-generator -- [repo-root] [options]
  go run ./cmd/graph-generator -- --project-path <dir> --project-name <name> [options]

Options:`)
	fs.PrintDefaults()
}
