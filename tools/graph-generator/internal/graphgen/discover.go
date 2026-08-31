package graphgen

import (
	"context"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"strings"

	"github.com/fleetstream/graph-generator/internal/graph"
)

var excludedDirs = map[string]struct{}{
	".git": {}, "node_modules": {}, "vendor": {}, "bin": {}, "obj": {},
	"dist": {}, "build": {}, "target": {}, "outputs": {}, "graphify-out": {},
	".vs": {}, ".idea": {}, ".vscode": {}, "__pycache__": {}, "proto": {},
}

type discoverer struct{}

func NewDiscoverer() *discoverer {
	return &discoverer{}
}

func (d *discoverer) Discover(ctx context.Context, root string) ([]graph.Project, error) {
	if strings.TrimSpace(root) == "" {
		return nil, fmt.Errorf("repository root is required")
	}

	abs, err := filepath.Abs(root)
	if err != nil {
		return nil, fmt.Errorf("resolve repository root: %w", err)
	}

	info, err := os.Stat(abs)
	if err != nil {
		return nil, fmt.Errorf("repository root: %w", err)
	}
	if !info.IsDir() {
		return nil, fmt.Errorf("repository root is not a directory: %s", abs)
	}

	byPath := make(map[string]graph.Project)

	err = filepath.WalkDir(abs, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return nil
		}
		select {
		case <-ctx.Done():
			return ctx.Err()
		default:
		}

		if entry.IsDir() {
			if _, skip := excludedDirs[entry.Name()]; skip {
				return fs.SkipDir
			}
			return nil
		}

		project, ok := detectProjectFile(path)
		if !ok {
			return nil
		}
		if _, exists := byPath[project.Path]; !exists {
			byPath[project.Path] = project
		}
		return nil
	})
	if err != nil {
		return nil, err
	}

	projects := make([]graph.Project, 0, len(byPath))
	for _, p := range byPath {
		projects = append(projects, p)
	}
	return projects, nil
}

func detectProjectFile(path string) (graph.Project, bool) {
	name := filepath.Base(path)
	dir := filepath.Dir(path)
	ext := strings.ToLower(filepath.Ext(name))

	switch {
	case name == "go.mod":
		moduleName := readGoModuleName(path)
		displayName := filepath.Base(dir)
		if moduleName != "" {
			if base := filepath.Base(moduleName); base != "" && base != "." {
				displayName = base
			}
		}
		return graph.Project{Name: displayName, Path: dir, Type: graph.ProjectGo, Language: "Go", BuildFile: name}, true
	case ext == ".csproj" || ext == ".fsproj" || ext == ".vbproj":
		return graph.Project{Name: strings.TrimSuffix(name, ext), Path: dir, Type: graph.ProjectDotNet, Language: "C#", BuildFile: name}, true
	case name == "Cargo.toml":
		return graph.Project{Name: filepath.Base(dir), Path: dir, Type: graph.ProjectRust, Language: "Rust", BuildFile: name}, true
	case name == "pom.xml" || name == "build.gradle" || name == "build.gradle.kts":
		return graph.Project{Name: filepath.Base(dir), Path: dir, Type: graph.ProjectJava, Language: "Java", BuildFile: name}, true
	case name == "package.json":
		return graph.Project{Name: filepath.Base(dir), Path: dir, Type: graph.ProjectTypeScript, Language: "TypeScript", BuildFile: name}, true
	case name == "pyproject.toml" || name == "setup.py" || name == "Pipfile":
		return graph.Project{Name: filepath.Base(dir), Path: dir, Type: graph.ProjectPython, Language: "Python", BuildFile: name}, true
	default:
		return graph.Project{}, false
	}
}

func readGoModuleName(goModPath string) string {
	data, err := os.ReadFile(goModPath)
	if err != nil {
		return ""
	}
	for _, line := range strings.Split(string(data), "\n") {
		line = strings.TrimSpace(line)
		if !strings.HasPrefix(line, "module ") {
			continue
		}
		return strings.TrimSpace(strings.TrimPrefix(line, "module "))
	}
	return ""
}
