# SymbolIndexer Local PID Directory Implementation Plan

## Problem Statement

The current SymbolIndexer uses a global PID directory (`/Users/rich/.dotnet/pids/build/`) which creates a significant architectural limitation:

- **Single instance per machine** - Only one SymbolIndexer can run at a time
- **Workspace mismatch** - SymbolIndexer for Project A cannot provide symbols for Project B
- **Developer friction** - Must manually stop/start SymbolIndexer when switching projects

## Solution: Local `.dotnet` Directory

### Core Change
Replace global PID directory with **project-local** PID directory:

```bash
# Before (Global)
/Users/rich/.dotnet/pids/build/sym-xxxxx.pid

# After (Local)
/Users/rich/git/project-a/.dotnet/pids/build/sym-xxxxx.pid
/Users/rich/git/project-b/.dotnet/pids/build/sym-yyyyy.pid
```

## Implementation Details

### File Changes Required

**1. `SymbolPidFileManager.cs`**
```csharp
// Current implementation
private string GetPidDirectory()
{
    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(userProfile, ".dotnet", "pids", "build");
}

// New implementation
private string GetPidDirectory()
{
    var currentDir = Directory.GetCurrentDirectory();
    return Path.Combine(currentDir, ".dotnet", "pids", "build");
}
```

**2. Directory Creation**
Ensure the local `.dotnet/pids/build/` directory is created when needed:
```csharp
var pidDir = GetPidDirectory();
Directory.CreateDirectory(pidDir); // Creates full path including .dotnet
```

**3. Gitignore Update**
Add to project `.gitignore`:
```gitignore
# SymbolIndexer local state
.dotnet/
```

## Workflow After Implementation

### Multi-Project Concurrent Usage
```bash
# Terminal 1 - Project A
cd /Users/rich/git/dotnet-cli-output
bin/SymbolIndexer/SymbolIndexer start    # Creates ./.dotnet/pids/build/sym-12345.pid
dotnet build --logger:"MarkdownTableLogger.dll"  # ✅ Rich symbol info

# Terminal 2 - Project B (simultaneously)
cd /Users/rich/git/smooth-markdown-table
../dotnet-cli-output/bin/SymbolIndexer/SymbolIndexer start  # Creates ./.dotnet/pids/build/sym-67890.pid
dotnet build --logger:"MarkdownTableLogger.dll"  # ✅ Rich symbol info
```

### Developer Experience Improvements
1. **No manual coordination** - Each project manages its own SymbolIndexer
2. **Natural cleanup** - PID files stay with project, easy to clean
3. **Workspace isolation** - No cross-project interference
4. **Intuitive behavior** - Local state stays local

## Benefits Analysis

### Technical Benefits
- ✅ **True multi-project support** - Multiple SymbolIndexers can run simultaneously
- ✅ **Workspace-specific indexing** - Each SymbolIndexer indexes correct project symbols
- ✅ **Improved reliability** - No cross-project symbol resolution failures
- ✅ **Better resource isolation** - Each project controls its own indexing

### Developer Benefits
- ✅ **Seamless project switching** - No need to stop/start SymbolIndexer manually
- ✅ **Parallel development** - Work on multiple projects simultaneously
- ✅ **Reduced friction** - MarkdownTableLogger "just works" in any project
- ✅ **Cleaner state management** - Local state is easier to reason about

### Operational Benefits
- ✅ **Easier cleanup** - Remove `.dotnet` directory to clean all local state
- ✅ **Better debugging** - PID files next to project make troubleshooting easier
- ✅ **Version control friendly** - Local state clearly separated from code

## Cons Analysis (Minimal Impact)

### Breaking Changes
- **Impact**: Existing global SymbolIndexer instances will need to be restarted
- **Mitigation**: Simple restart process, one-time migration

### Directory Creation
- **Impact**: Creates `.dotnet` directories in project roots
- **Mitigation**: Standard pattern (similar to `node_modules`, `.vs`, etc.)

### Gitignore Updates
- **Impact**: Projects need `.dotnet/` in `.gitignore`
- **Mitigation**: Standard practice, can provide template

## Implementation Priority

### Phase 1: Core Implementation
1. Update `SymbolPidFileManager.cs` to use local directory
2. Ensure directory creation logic
3. Test with single project

### Phase 2: Multi-Project Testing
1. Test concurrent SymbolIndexer instances
2. Verify MarkdownTableLogger works with local PID files
3. Test project switching scenarios

### Phase 3: Documentation & Polish
1. Update help text and examples
2. Add `.gitignore` recommendations
3. Document migration path for existing users

## Expected Outcome

After implementation:
- **Framework types** (Stream, Console, etc.) will be properly identified in all projects
- **Cross-project references** will work correctly
- **Multi-project development** will be seamless
- **Symbol resolution failures** will be eliminated

This change transforms the SymbolIndexer from a single-instance global service into a proper project-local development tool, enabling the full vision of the MarkdownTableLogger ↔ SymbolIndexer integration across all project types and workflows.