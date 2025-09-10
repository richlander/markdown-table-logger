# MarkdownTableLogger Tutorial

A dotnet build logger that transforms verbose MSBuild output into clean, token-efficient markdown tables optimized for LLMs and humans.

## Quick Start

```bash
# Build the logger and publish symbol indexer (one-time setup)
git clone https://github.com/your-repo/dotnet-cli-output
cd dotnet-cli-output
dotnet build src/MarkdownTableLogger
dotnet publish src/SymbolIndexer -o bin/SymbolIndexer

# Start the symbol indexer daemon (MUST run from repository root for full indexing)
./bin/SymbolIndexer/SymbolIndexer start &

# Discover all available modes and options
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;help" --noconsolelogger

# Use with any dotnet project (LLM-optimized prompt mode)
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;mode=prompt" --noconsolelogger
```

## Mode Discovery

**Always start with help** to see current options:
```bash
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;help" --noconsolelogger
```

Shows all modes: `projects` (default), `errors`, `types`, `minimal`, `prompt`, `prompt-verbose`  
And column options: `status`, `description`, `message`, `all`

## Primary Use Case: LLM-Optimized Prompt Mode

**Best for AI-assisted debugging** with complete context and symbol references:

```bash
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;mode=prompt" --noconsolelogger
```
```markdown
# dotnet-cli-output build log

Command: dotnet build
Time: 2025-09-09T11:31:39
Duration: 0.6s

## Projects

| Project | Errors | Warnings |
|---------|--------|----------|
| MyApp   | 2      | 0        |

## Build Errors

| File | Line | Col | Code | Anchor | Lines |
|------|------|-----|------|--------|-------|
| Program.cs | 42 | 15 | CS0103 | #programcs4215 | 27-42 |
| Utils.cs   | 15 | 9  | CS1061 | #utilscs159 | 44-58 |

### Program.cs:42:15

- File: Program.cs
- Lines: 38-46
- Error: CS0103
- Message: The name 'undefinedVar' does not exist in the current context

```csharp
    public void ProcessData() {
        var data = GetData();
        Console.WriteLine(undefinedVar); // ← CS0103
        return result;
    }
```

**Referenced symbols:**
- `Console` - .NET Libraries (System.Console)
- `WriteLine` - .NET Libraries
- `undefinedVar` - undefined symbol
- `data` - Program.cs:39,13
```

## Common Workflows

```bash
# Quick status check
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;mode=minimal" --noconsolelogger

# LLM debugging (recommended) - requires daemon running from repo root
./bin/SymbolIndexer/SymbolIndexer start &
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;mode=prompt" --noconsolelogger

# Human-readable version
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;mode=prompt-verbose" --noconsolelogger

# Discover all options
dotnet build --logger:"/path/to/MarkdownTableLogger.dll;help" --noconsolelogger
```

## Symbol Indexer Integration

The logger integrates with a persistent SymbolIndexer daemon to provide enhanced **Referenced symbols** sections in prompt mode output:

### **Symbol Classification**
- **User-defined symbols**: `myVariable` - MyClass.cs:42,13 (exact file location)
- **Framework types**: `Console` - .NET Libraries (System.Console) (assembly info)  
- **External packages**: `JsonConvert` - external package (Newtonsoft.Json)
- **Undefined symbols**: `undefinedVar` - undefined symbol (compilation errors)

### **Daemon Management**

**⚠️ IMPORTANT**: The SymbolIndexer **must be started from the repository root** to index all projects. It recursively indexes all `.cs` files from its launch directory.

```bash
# Start daemon (MUST run from repository root for complete symbol indexing)
cd /path/to/your-repo-root
./bin/SymbolIndexer/SymbolIndexer start &

# Query symbols manually (optional)
./bin/SymbolIndexer/SymbolIndexer query --file Program.cs --line 42

# Shutdown daemon
./bin/SymbolIndexer/SymbolIndexer shutdown

# Daemon auto-discovers via PID files following .NET build-server patterns
# Logger gracefully degrades if daemon unavailable
```

**Directory Impact on Symbol Indexing:**
- **From repo root**: Indexes all projects → Full cross-project symbol references
- **From single project**: Only indexes that project → Limited symbol context  
- **Best practice**: Always start from repository root for maximum utility

### **Benefits for LLMs**
✅ **Symbol context** - Know where types are defined vs. undefined  
✅ **Assembly information** - Framework vs. user code distinction  
✅ **Fix guidance** - Undefined symbols clearly identified  
✅ **Token efficient** - Precise location references  

## Generated Files

The logger creates both console output AND organized persistent files in `_logs/YYYY-MM-DDTHH-MM-SS/`:
- `dotnet-build-results.{md,json}` - Project overview
- `dotnet-build-diagnostics.{md,json}` - Error/warning details (with column info)
- `dotnet-build-diagnostic-types.{md,json}` - Error summaries
- `dotnet-build-prompt.md` - LLM-optimized document (if using prompt mode)
- `dotnet-build-prompt-verbose.md` - Human-readable document (if using prompt-verbose mode)

Use `jq` for advanced JSON analysis:
```bash
# Navigate to latest logs
cd _logs/$(ls -1 _logs | tail -1)

# Find all CS1061 errors with column info
jq '.[] | select(.code == "CS1061") | {file, line, column, code}' dotnet-build-diagnostics.json

# Count errors by file
jq 'group_by(.file) | map({file: .[0].file, count: length})' dotnet-build-diagnostics.json

# Analyze error patterns by column position
jq 'group_by(.code) | map({code: .[0].code, count: length, avgCol: (map(.column) | add / length)})' dotnet-build-diagnostics.json
```

## Enhanced Table Navigation

The prompt mode now includes **Anchor** and **Lines** columns for precise document navigation:

- **Anchor**: GitHub-compatible anchor link (e.g., `#programcs4215`)
- **Lines**: Exact line range in the prompt document (e.g., `27-42`)

**LLM Usage Example:**
```bash
# Extract specific error context using the Lines column
sed -n '27,42p' dotnet-build-prompt.md

# Result: Complete error section with metadata and code
- File: Program.cs
- Lines: 38-46  
- Error: CS0103
- Message: The name 'undefinedVar' does not exist in the current context
```

**Enhanced JSON Output:**
Prompt mode also generates `*-enhanced.json` files with anchor and line references:
```json
{
  "file": "Program.cs",
  "line": 42,
  "code": "CS0103", 
  "anchor": "#programcs4215",
  "lines": "27-42"
}
```

## Key Benefits

✅ **75-89% token reduction** vs raw dotnet output  
✅ **LLM-first design** with token-optimized prompt mode  
✅ **Direct line access** - `sed -n '27,42p'` for precise extraction  
✅ **Structured metadata** - Clean bullet points for key error info  
✅ **Column precision** - distinguish errors at same line  
✅ **Git patch-style context** - 4 lines before/after for fix scope  
✅ **Enhanced symbol references** - User code vs. framework vs. undefined  
✅ **Assembly information** - Know exactly which .NET library types come from  
✅ **Persistent daemon** - Real-time symbol indexing with file watching  
✅ **Clean organized logs** - timestamped `_logs/` directory  
✅ **Web-native tables** - consistent markdown table structures  
✅ **Both audiences** - concise for LLMs, verbose for humans  

## Pro Tips

1. **Start with `help` mode** to discover current options
2. **Always use `--noconsolelogger`** to avoid MSBuild noise
3. **Use `prompt` mode for LLMs** - provides complete context with symbol references
4. **Daemon enhances prompt mode** - start SymbolIndexer for better symbol classification
5. **Logs persist** in organized `_logs/` directory for later analysis