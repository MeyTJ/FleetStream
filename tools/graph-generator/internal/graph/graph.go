package graph

import (
	"context"
	"time"
)

type ProjectType string

const (
	ProjectUnknown    ProjectType = "unknown"
	ProjectGo         ProjectType = "go"
	ProjectDotNet     ProjectType = "dotnet"
	ProjectPython     ProjectType = "python"
	ProjectTypeScript ProjectType = "typescript"
	ProjectJava       ProjectType = "java"
	ProjectRust       ProjectType = "rust"
)

type Project struct {
	Name      string
	Path      string
	Type      ProjectType
	Language  string
	BuildFile string
}

type Config struct {
	OutputDirectory   string
	ExportFormats     []string
	Concurrency       int
	FailFast          bool
	PerProjectTimeout time.Duration
}

type GenerateProjectRequest struct {
	ProjectPath     string
	ProjectName     string
	OutputDirectory string
	ExportFormats   []string
}

type GenerateAllRequest struct {
	RepositoryRoot  string
	OutputDirectory string
	ExportFormats   []string
	ProjectFilter   []string
	FailFast        bool
}

type ProjectResult struct {
	ProjectName    string
	ProjectPath    string
	OutputPath     string
	Success        bool
	Nodes          int
	Edges          int
	Communities    int
	GeneratedFiles []string
	Duration       time.Duration
	Error          string
}

type BatchResult struct {
	Success            bool
	Results            []ProjectResult
	TotalProjects      int
	SuccessfulProjects int
	FailedProjects     int
	Duration           time.Duration
}

type GraphGenerator interface {
	Discover(ctx context.Context, repositoryRoot string) ([]Project, error)
	GenerateProject(ctx context.Context, req GenerateProjectRequest) (ProjectResult, error)
	GenerateAll(ctx context.Context, req GenerateAllRequest) (BatchResult, error)
}
