# MarkdownTableLogger Prototype

A proof-of-concept MSBuild logger that transforms dotnet CLI output into structured formats (markdown tables + JSON) optimized for both human and LLM consumption.

## Quick Start

```bash
# Build the logger
dotnet build src/MarkdownTableLogger/

# Use with any dotnet build (for clean table-only output)
dotnet build --logger:"src/MarkdownTableLogger/bin/Debug/net8.0/MarkdownTableLogger.dll" --noconsolelogger

# Or run the test script
./test-prototype.sh
```

### Clean Output Tips

**Clean Table-Only Output:**
```bash
# Complete suppression of MSBuild output - only structured tables
dotnet build --logger:"path/to/MarkdownTableLogger.dll" --noconsolelogger

# Alternative: Redirect MSBuild output to file
dotnet build --logger:"path/to/MarkdownTableLogger.dll" --filelogger --fileloggerparameters:logfile=build.log
```

**Perfect!** The `--noconsolelogger` flag gives you pure structured output with zero noise.

## What It Does

**Console Output**: Shows clean markdown tables for immediate comprehension
```text
📊 Build Summary
| Project | Errors | Warnings | Status |
|---------|--------|----------|--------|
| MyProject | 4 | 1 | ❌ |

🔍 4 Errors Found  
| File | Line | Code | Message |
|------|------|------|---------|
| Program.cs | 11 | CS0103 | The name 'undefinedVariable' does not exist in ... |
| Program.cs | 15 | CS1061 | 'string' does not contain a definition for 'Non... |
```

**Generated Files**: Creates structured data for programmatic analysis
- `dotnet-build-results.{md,json}` - Project success overview
- `dotnet-build-errors.{md,json}` - Individual error diagnostics  
- `dotnet-build-error-types.{md,json}` - Error aggregation by type

## Key Features

### ✅ Implemented Schemas

1. **Project Results Schema** - Overview of all projects
2. **Error Diagnostics Schema** - File/line/code/message for each error
3. **Error Type Summary Schema** - Aggregated error counts by CS code

### ✅ Smart Output Selection

- **≤6 errors**: Shows individual diagnostics table
- **>6 errors**: Shows error type summary table  
- **Console**: Human-optimized markdown tables
- **Files**: Both markdown + JSON for different use cases

### ✅ Path Normalization

- Converts absolute paths to workspace-relative paths
- `src/MyProject/Program.cs` instead of `/full/path/to/src/MyProject/Program.cs`

### ✅ Token Efficiency

Based on actual testing, this approach achieves:
- **75-89% token reduction** vs raw dotnet CLI output
- **Structured data** that enables programmatic analysis with jq/grep
- **Visual hierarchy** for rapid pattern recognition

## Usage Examples

### Basic Error Analysis

```bash
# Build with structured logger
dotnet build --logger:"path/to/MarkdownTableLogger.dll"

# Query JSON with jq - find all CS1061 errors
jq '.[] | select(.code == "CS1061")' dotnet-build-errors.json

# Query markdown with grep
grep "CS1061" dotnet-build-errors.md
```

### Multi-Project Builds

```bash
# See which projects failed
jq '.[] | select(.errors > 0)' dotnet-build-results.json

# Get error counts by project  
jq 'group_by(.project) | map({project: .[0].project, total_errors: map(.errors) | add})' dotnet-build-results.json
```

## Architecture

```text
MSBuild Events → StructuredBuildLogger → Schema Models → Output Generators
                      ↓                      ↓              ↓
               Error/Warning/Project    ProjectResult    Markdown + JSON
                    Events             ErrorDiagnostic   
                                      ErrorTypeSummary
```

**Core Components:**
- `StructuredBuildLogger` - MSBuild ILogger implementation
- `Models/SchemaModels.cs` - Data structures for all schemas
- `Output/OutputGenerator.cs` - Markdown/JSON formatters

## Comparison: Before vs After

**Raw Terminal Logger Output (586 words)**:
```text
  MarkdownTable.Documents failed with 19 error(s) and 1 warning(s) (0.1s)
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs(24,16): error CS0103: The name 'FullWidthBufferedProcessor' does not exist in the current context
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/IdentityRendererConfiguration.cs(23,16): error CS0103: The name 'IdentityStreamingProcessor' does not exist in the current context
    ...17 more similar lines...
```

**Structured Output (148 words, 75% reduction)**:
```text
📊 Build Summary
| Project | Errors | Warnings | Status |
|---------|--------|----------|--------|
| MarkdownTable.Documents | 19 | 1 | ❌ |

🔍 Error Type Summary  
| Code | Count | Description |
|------|-------|-------------|
| CS1061 | 10 | Member does not exist |
| CS0103 | 7 | Name does not exist in current context |
| CS0246 | 2 | Type or namespace not found |
```

## Testing

The prototype includes a comprehensive test that demonstrates:

1. **Success Case**: Clean project builds → project results table
2. **Error Case**: Multiple error types → appropriate view selection
3. **File Generation**: Both .md and .json outputs
4. **Console Experience**: Immediate visual feedback

Run `./test-prototype.sh` to see it in action.

## Next Steps

This ~300-line prototype validates the core hypothesis. Potential enhancements:

- **Context-aware diagnostics** with surrounding code lines
- **Symbol resolution hints** (missing using statements, typos)
- **Integration** as dotnet global tool
- **Configuration** for context line counts and output preferences
- **Streaming** for large builds (JSONL format)

The prototype proves that structured CLI output can achieve significant token efficiency while improving human comprehension.