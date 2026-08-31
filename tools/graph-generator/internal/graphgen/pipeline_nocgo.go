//go:build !cgo

package graphgen

import (
	"encoding/json"
	"fmt"
	"go/ast"
	"go/parser"
	"go/token"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

type astNode struct {
	ID    string `json:"id"`
	Label string `json:"label"`
	Type  string `json:"type"`
	File  string `json:"file"`
	Comm  int    `json:"community"`
}

type astEdge struct {
	Source   string `json:"source"`
	Target   string `json:"target"`
	Relation string `json:"relation"`
}

func runIsolatedPipeline(sourcePath, outputDir, projectName string, formats []string) (pipelineResult, error) {
	start := time.Now()

	if err := os.MkdirAll(outputDir, 0o755); err != nil {
		return pipelineResult{}, fmt.Errorf("create output directory: %w", err)
	}

	nodes, edges, err := extractProject(sourcePath)
	if err != nil {
		return pipelineResult{}, err
	}

	communities := clusterByDirectory(nodes)
	for i := range nodes {
		if cid, ok := communities[nodes[i].ID]; ok {
			nodes[i].Comm = cid
		}
	}

	generated, err := writeOutputs(formats, outputDir, projectName, sourcePath, nodes, edges, communities)
	if err != nil {
		return pipelineResult{}, err
	}

	return pipelineResult{
		Nodes:          len(nodes),
		Edges:          len(edges),
		Communities:    distinctCommunities(communities),
		GeneratedFiles: generated,
		Duration:       time.Since(start),
	}, nil
}

func extractProject(root string) ([]astNode, []astEdge, error) {
	goModule := isGoModuleRoot(root)
	var nodes []astNode
	var edges []astEdge
	seen := make(map[string]struct{})

	addNode := func(n astNode) {
		if _, ok := seen[n.ID]; ok {
			return
		}
		seen[n.ID] = struct{}{}
		nodes = append(nodes, n)
	}

	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return nil
		}
		if entry.IsDir() {
			if _, skip := excludedDirs[entry.Name()]; skip {
				return fs.SkipDir
			}
			return nil
		}
		rel, _ := filepath.Rel(root, path)
		ext := strings.ToLower(filepath.Ext(path))
		switch ext {
		case ".go":
			if strings.HasSuffix(strings.ToLower(path), "_test.go") {
				return nil
			}
			n, e := extractGoFile(path, rel, root)
			for _, node := range n {
				addNode(node)
			}
			edges = append(edges, e...)
		case ".cs":
			if goModule {
				return nil
			}
			n, e := extractCSharpFile(path, rel)
			for _, node := range n {
				addNode(node)
			}
			edges = append(edges, e...)
		}
		return nil
	})
	return nodes, edges, err
}

func isGoModuleRoot(root string) bool {
	_, err := os.Stat(filepath.Join(root, "go.mod"))
	return err == nil
}

func extractGoFile(absPath, rel, moduleRoot string) ([]astNode, []astEdge) {
	fset := token.NewFileSet()
	file, err := parser.ParseFile(fset, absPath, nil, parser.SkipObjectResolution)
	if err != nil {
		return nil, nil
	}

	pkgPath := goPackagePath(moduleRoot, rel)
	fileID := makeID("file", rel)
	pkgID := makeID("pkg", pkgPath)

	nodes := []astNode{
		{ID: fileID, Label: filepath.Base(rel), Type: "file", File: rel},
		{ID: pkgID, Label: pkgPath, Type: "module", File: rel},
	}
	edges := []astEdge{
		{Source: fileID, Target: pkgID, Relation: "contains"},
	}

	for _, spec := range file.Imports {
		imp := strings.Trim(spec.Path.Value, `"`)
		impID := makeID("import", imp)
		nodes = append(nodes, astNode{ID: impID, Label: imp, Type: "module", File: rel})
		edges = append(edges, astEdge{Source: fileID, Target: impID, Relation: "imports"})
	}

	ast.Inspect(file, func(n ast.Node) bool {
		switch decl := n.(type) {
		case *ast.FuncDecl:
			label := decl.Name.Name + "()"
			kind := "function"
			parent := pkgID
			if decl.Recv != nil && len(decl.Recv.List) > 0 {
				kind = "method"
				recv := recvTypeName(decl.Recv.List[0].Type)
				parent = makeID("type", pkgPath, recv)
				nodes = append(nodes, astNode{ID: parent, Label: recv, Type: "class", File: rel})
			}
			id := makeID(kind, pkgPath, label)
			nodes = append(nodes, astNode{ID: id, Label: label, Type: kind, File: rel})
			edges = append(edges, astEdge{Source: parent, Target: id, Relation: "method"})
			if decl.Body != nil {
				ast.Inspect(decl.Body, func(inner ast.Node) bool {
					call, ok := inner.(*ast.CallExpr)
					if !ok {
						return true
					}
					name := callName(call)
					if name == "" {
						return true
					}
					target := makeID("function", pkgPath, name+"()")
					edges = append(edges, astEdge{Source: id, Target: target, Relation: "calls"})
					return true
				})
			}
		case *ast.TypeSpec:
			id := makeID("type", pkgPath, decl.Name.Name)
			kind := "class"
			if _, ok := decl.Type.(*ast.InterfaceType); ok {
				kind = "interface"
			}
			nodes = append(nodes, astNode{ID: id, Label: decl.Name.Name, Type: kind, File: rel})
			edges = append(edges, astEdge{Source: pkgID, Target: id, Relation: "contains"})
		}
		return true
	})

	return nodes, edges
}

var (
	csClass  = regexp.MustCompile(`(?m)\b(?:class|interface|struct|record)\s+([A-Za-z_][A-Za-z0-9_]*)`)
	csUsing  = regexp.MustCompile(`(?m)^using\s+([A-Za-z0-9_.]+)\s*;`)
	csMethod = regexp.MustCompile(`(?m)\b(?:public|private|protected|internal|static|async|override|virtual|sealed)*\s*(?:[\w.<>,\[\]]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(`)
)

func extractCSharpFile(absPath, rel string) ([]astNode, []astEdge) {
	data, err := os.ReadFile(absPath)
	if err != nil {
		return nil, nil
	}
	src := string(data)
	fileID := makeID("file", rel)
	nodes := []astNode{{ID: fileID, Label: filepath.Base(rel), Type: "file", File: rel}}
	var edges []astEdge

	for _, m := range csUsing.FindAllStringSubmatch(src, -1) {
		id := makeID("import", m[1])
		nodes = append(nodes, astNode{ID: id, Label: m[1], Type: "module", File: rel})
		edges = append(edges, astEdge{Source: fileID, Target: id, Relation: "imports"})
	}
	for _, m := range csClass.FindAllStringSubmatch(src, -1) {
		id := makeID("type", m[1])
		nodes = append(nodes, astNode{ID: id, Label: m[1], Type: "class", File: rel})
		edges = append(edges, astEdge{Source: fileID, Target: id, Relation: "contains"})
	}
	for _, m := range csMethod.FindAllStringSubmatch(src, -1) {
		name := m[1]
		if name == "if" || name == "for" || name == "while" || name == "switch" || name == "catch" || name == "using" {
			continue
		}
		id := makeID("function", rel, name)
		nodes = append(nodes, astNode{ID: id, Label: name + "()", Type: "method", File: rel})
		edges = append(edges, astEdge{Source: fileID, Target: id, Relation: "method"})
	}
	return nodes, edges
}

func clusterByDirectory(nodes []astNode) map[string]int {
	index := make(map[string]int)
	next := 0
	out := make(map[string]int, len(nodes))
	for _, n := range nodes {
		key := filepath.Dir(n.File)
		if key == "" || key == "." {
			key = n.Type
		}
		cid, ok := index[key]
		if !ok {
			cid = next
			index[key] = cid
			next++
		}
		out[n.ID] = cid
	}
	return out
}

func writeOutputs(formats []string, outputDir, projectName, sourcePath string, nodes []astNode, edges []astEdge, communities map[string]int) ([]string, error) {
	var generated []string
	for _, format := range formats {
		switch format {
		case "json":
			path := filepath.Join(outputDir, "graph.json")
			payload := map[string]any{"nodes": nodes, "links": edges}
			data, err := json.MarshalIndent(payload, "", "  ")
			if err != nil {
				return generated, fmt.Errorf("marshal json: %w", err)
			}
			if err := os.WriteFile(path, data, 0o644); err != nil {
				return generated, fmt.Errorf("write json: %w", err)
			}
			generated = append(generated, path)
		case "html":
			path := filepath.Join(outputDir, "graph.html")
			if err := os.WriteFile(path, []byte(renderHTML(projectName, nodes, edges)), 0o644); err != nil {
				return generated, fmt.Errorf("write html: %w", err)
			}
			generated = append(generated, path)
		case "report":
			path := filepath.Join(outputDir, "GRAPH_REPORT.md")
			if err := os.WriteFile(path, []byte(renderReport(projectName, sourcePath, nodes, edges, communities)), 0o644); err != nil {
				return generated, fmt.Errorf("write report: %w", err)
			}
			generated = append(generated, path)
		}
	}
	return generated, nil
}

func renderHTML(title string, nodes []astNode, edges []astEdge) string {
	type htmlNode struct {
		ID    string `json:"id"`
		Label string `json:"label"`
		Group int    `json:"group"`
	}
	type htmlEdge struct {
		From string `json:"from"`
		To   string `json:"to"`
	}
	hn := make([]htmlNode, 0, len(nodes))
	for _, n := range nodes {
		hn = append(hn, htmlNode{ID: n.ID, Label: n.Label, Group: n.Comm})
	}
	he := make([]htmlEdge, 0, len(edges))
	for _, e := range edges {
		he = append(he, htmlEdge{From: e.Source, To: e.Target})
	}
	nj, _ := json.Marshal(hn)
	ej, _ := json.Marshal(he)
	return fmt.Sprintf(`<!DOCTYPE html>
<html><head><meta charset="utf-8"><title>%s</title>
<script src="https://unpkg.com/vis-network/standalone/umd/vis-network.min.js"></script>
<style>html,body,#g{margin:0;height:100%%;background:#0f0f1a}</style></head>
<body><div id="g"></div>
<script>
new vis.Network(document.getElementById('g'),
  {nodes:new vis.DataSet(%s),edges:new vis.DataSet(%s)},
  {nodes:{shape:'dot',size:14,font:{color:'#fff'}},physics:{barnesHut:{gravitationalConstant:-2500}}});
</script></body></html>`, htmlEscape(title), nj, ej)
}

func renderReport(name, root string, nodes []astNode, edges []astEdge, communities map[string]int) string {
	extractor := "stdlib go/ast"
	if isGoModuleRoot(root) {
		extractor = "Go module extractor (go/ast)"
	}
	return fmt.Sprintf("# Graph Report - %s\n\n- Source: `%s`\n- %d nodes · %d edges · %d communities\n- Extractor: %s\n",
		name, root, len(nodes), len(edges), distinctCommunities(communities), extractor)
}

func goPackagePath(moduleRoot, rel string) string {
	dir := filepath.ToSlash(filepath.Dir(rel))
	if dir == "." {
		return filepath.Base(moduleRoot)
	}
	return filepath.ToSlash(filepath.Join(filepath.Base(moduleRoot), dir))
}

func recvTypeName(expr ast.Expr) string {
	switch t := expr.(type) {
	case *ast.StarExpr:
		return recvTypeName(t.X)
	case *ast.Ident:
		return t.Name
	case *ast.IndexExpr:
		return recvTypeName(t.X)
	default:
		return "unknown"
	}
}

func callName(call *ast.CallExpr) string {
	switch f := call.Fun.(type) {
	case *ast.Ident:
		return f.Name
	case *ast.SelectorExpr:
		return f.Sel.Name
	default:
		return ""
	}
}

func makeID(parts ...string) string {
	return strings.ToLower(strings.Join(parts, "_"))
}

func distinctCommunities(communities map[string]int) int {
	seen := make(map[int]struct{}, len(communities))
	for _, cid := range communities {
		seen[cid] = struct{}{}
	}
	return len(seen)
}

func htmlEscape(s string) string {
	r := strings.NewReplacer("&", "&amp;", "<", "&lt;", ">", "&gt;", `"`, "&quot;")
	return r.Replace(s)
}
