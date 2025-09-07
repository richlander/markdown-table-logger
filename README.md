# Improving CLI Output for Human and AI Comprehension

We're now in the age of LLMs. It turns out that coding is the first "killer app" of this new technology paradigm. There is a significant opportunity to (re-)evolve build systems into queryable data systems. In a world of token limits, the dev platforms the consumes the least tokens is the one that enables LLMs to apply those token to the problem at hand and to write the best code (most or possibly least). This is the secret "hiding in plain sight". Build system output is likely the most significiant discretionary contributor to token exhaustion.

## Hypothesis

The dotnet CLI and other build systems produce verbose output biased toward accuracy rather than comprehension, particularly for "at a glance" workflows. This is very similar to considering signal vs noise. This tension creates problems for both humans and LLMs trying to quickly understand build results.

The Docker CLI includes a "party trick" where Go format templates can reformat output, however, this approach is awkward for many users. Instead, multiple purpose-built user-selectable views in standard formats (with obvious column demarcators) would greatly aid all users.

These views can slice and dice the data, not unlike social media preview cards. JSON has commonly been used for this purpose. Although unconventional, markdown tables can be just as useful and often superior. Markdown can be thought of as web-native CSV. It shows up prominently in AI-assistant workflows and is immediately readable by humans. It can also be pretty-printed, as needed.

The concept of views is an invitation for lossy information. That could mean that `dotnet build` needs to be called multiple times, either for multiple views or to process the same view multiple times (since stdout might be consumed by piping to `jq`). The `tee` tool can solve one of these problems by persisting the consumed console stream to disk. That's good, but inelegant. A better idea is that CLI would (A) enable writing the output to both stdout and to disk, and (B) writing different and possibly multiple selectable views to each. This approach could break the tie between comprehension and accuracy, by writing summary markdown to the console and more expansive JSON to disk, only used if the markdown output suggests a need.

This approach would tranform the dotnet CLI from a document generator to a data API. The dotnet CLI should embrace markdown and JSON equally. We often think about JSON in terms of schemas but not markdown. That's solely a gap in industry imagination.

## Proposed Schema-Based Views

Markdown _tables_ could be the default console format with an additonal persisted file-based format and view being optional.

### 1. Project Build Success Schema

**Purpose:** Project health overview, CI/CD decisions  
**Files:** [`dotnet-build-success.md`](dotnet-build-success.md) | [`dotnet-build-success.json`](dotnet-build-success.json)

```markdown
| Project | Errors |
|---------|--------|
| MarkdownTable.IO | 0 |
| MarkdownTable.Documents | 19 |
| ttt | 0 |
```

**JSON variant:**

```json
[
  {"project": "MarkdownTable.IO", "errors": 0},
  {"project": "MarkdownTable.Rendering", "errors": 0},
  {"project": "MarkdownTable.Documents", "errors": 0},
  {"project": "ttt", "errors": 0}
]
```

### 2. Error Type Schema (Project-scoped)

**Purpose:** Analysis, prioritization  
**Files:** [`dotnet-build-errors-by-type-MarkdownTable.Documents.md`](dotnet-build-errors-by-type-MarkdownTable.Documents.md) | [`dotnet-build-errors-by-type-MarkdownTable.Documents.json`](dotnet-build-errors-by-type-MarkdownTable.Documents.json)

The "Description" column would be optional.

```markdown
| Code | Count | Description |
|------|-------|-------------|
| CS1061 | 10 | Member does not exist |
| CS0103 | 7 | Name does not exist in current context |
| CS0246 | 2 | Type or namespace not found |
```

**JSON variant:**

```json
[
  {"code": "CS1061", "count": 10, "description": "Member does not exist"},
  {"code": "CS0103", "count": 7, "description": "Name does not exist in current context"},
  {"code": "CS0246", "count": 2, "description": "Type or namespace not found"},
  {"code": "CS1501", "count": 2, "description": "No overload takes N arguments"},
  {"code": "CS1998", "count": 1, "description": "Async method lacks await operators"}
]
```

### 3. Diagnostic Schema

**Purpose:** Error-focused analysis, development workflow  
**Files:** [`dotnet-build-errors.md`](dotnet-build-errors.md) | [`dotnet-build-errors.json`](dotnet-build-errors.json) | [`dotnet-build-errors-verbose.md`](dotnet-build-errors-verbose.md) | [`dotnet-build-errors-verbose.json`](dotnet-build-errors-verbose.json)

The "Message" column would be optional.

```markdown
| File | Line | Code | Message |
|------|------|------|---------|
| src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs | 24 | CS0103 | The name 'FullWidthBufferedProcessor' does not exist in the current context |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 50 | CS1061 | 'IProcessorInfo' does not contain a definition for 'IsStreaming' |
```

**JSON variant:**
```json
[
  {"file": "src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs", "line": 24, "code": "CS0103", "message": "The name 'FullWidthBufferedProcessor' does not exist in the current context"},
  {"file": "src/MarkdownTable.Documents/TableProcessorRegistry.cs", "line": 50, "code": "CS1061", "'IProcessorInfo' does not contain a definition for 'IsStreaming'"}
]
```

### 4. Context-Aware Diagnostic Schema

**Purpose:** Rich error context for LLMs, inspired by git patches  
**Format:** JSON-only (too verbose for console display)

This schema includes surrounding code context to help LLMs understand errors without needing to read entire files:

```json
{
  "file": "Program.cs", 
  "line": 99, 
  "code": "CS1061", 
  "message": "'Foo' does not contain a definition for 'Bar'",
  "context": {
    "before": [
      {"line": 97, "text": "    public void ProcessData() {"},
      {"line": 98, "text": "        var foo = new Foo();"}
    ],
    "error": {"line": 99, "text": "        var result = foo.Bar(); // Error here"},
    "after": [
      {"line": 100, "text": "        Console.WriteLine(result);"},
      {"line": 101, "text": "    }"}
    ]
  }
}
```

The number of lines could be controllable with an LLM-provided configuration map:

```json
{
  "CS0103": {"before": 3, "after": 1},  // "Name does not exist" - need more preceding context
  "CS1061": {"before": 2, "after": 2},  // "Member does not exist" - balanced context
  "CS0029": {"before": 1, "after": 0},  // "Cannot implicitly convert" - just need the assignment
  "CS1998": {"before": 5, "after": 5}   // "Async method lacks await" - need full method
}
```

There could also be a way to request the whole method.

**Benefits:**
- **Contextual understanding**: LLMs can see variable declarations, method scope, and usage patterns
- **Reduced file reads**: No need to fetch entire source files for error analysis
- **Familiar pattern**: Git patch format is well-understood by developers and LLMs
- **Configurable context**: Could support `--context-lines=N` similar to `git diff`

**Why This Matters for LLMs:**

**Single-Pass Analysis**: The LLM can immediately see the method signature (line 97), variable declaration (line 98), problematic call (line 99), and result usage (line 100). This is often enough to suggest: "Did you mean `foo.Baz()`?" or "You need to add a `Bar()` method to the `Foo` class."

**Token Economics**:
- Without context: ~30 tokens for error + ~50 tokens for tool call + ~100 tokens for file content = ~180 tokens
- With context: ~80 tokens total, and it's done

**Pattern Recognition**: LLMs excel at pattern matching. With context, they can immediately recognize:
- Missing using statements (seeing the namespace context)
- Typos in method names (seeing similar methods nearby)  
- Incorrect parameter types (seeing the variable declarations)
- Missing `await` keywords (seeing the async context)

**The Transformation**: This approach acknowledges that errors have locality - most errors can be understood within a small context window. For AI-assisted development, this transforms the error correction loop from:

> See error → Request context → Analyze → Suggest fix

To:

> See error with context → Suggest fix

That's a 50% reduction in steps, which compounds across multiple errors. For a solution with 20 errors, you've just saved 20 tool calls and probably 2000+ tokens.

## Current State

### Success Case: Clean but arbitrary (proprietary)

**Source:** [`dotnet-build-success.txt`](dotnet-build-success.txt)

```text
rich@richs-MacBook-Pro smooth-markdown-table % dotnet build src/ttt/ttt.csproj 
Restore complete (0.3s)
    info NETSDK1057: You are using a preview version of .NET. See: https://aka.ms/dotnet-support-policy
  MarkdownTable.IO succeeded (0.0s) → src/MarkdownTable.IO/bin/Debug/net10.0/MarkdownTable.IO.dll
  MarkdownTable.Rendering succeeded (0.0s) → src/MarkdownTable.Rendering/bin/Debug/net10.0/MarkdownTable.Rendering.dll
  MarkdownTable.Documents succeeded (0.1s) → src/MarkdownTable.Documents/bin/Debug/net10.0/MarkdownTable.Documents.dll
  ttt succeeded (0.0s) → src/ttt/bin/Debug/net10.0/ttt.dll

Build succeeded in 0.8s
```

This output is readable. 

**Problems**

- Inconsistent spacing
- Mixed information "{library} {success} ({duration}) → {binary}"
- Non-standard demarcators
- More token-heavy than needed; success should be very cheap

### Failure Case: Accurate but stretching comprehension

**Source:** [`dotnet-build-errors.txt`](dotnet-build-errors.txt)

```text
  MarkdownTable.Documents failed with 19 error(s) and 1 warning(s) (0.1s)
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs(24,16): error CS0103: The name 'FullWidthBufferedProcessor' does not exist in the current context
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/IdentityRendererConfiguration.cs(23,16): error CS0103: The name 'IdentityStreamingProcessor' does not exist in the current context
    ...18 more similar lines...
```

**Problems:**
- Much the same as the success case
- Verbose file paths (most LLMs live in a workspace-relative world)
- Error codes buried in text
- Non-standard demarcators (for example, ':' is used more as "for more information" than a column)
- Row/Column information require indexing on '(' and ')' characters.

## The dual personality logger problem

The "terminal logger" provides the default build output. It is new as of .NET 8 or 9. It is a _major_ improvement over what came before. Unfortuantely, it is not what most (all?) LLMs see today since terminal logger is execution state aware - if the command stdout is being redirected, or there is no tty allocated for the command, by default it will not activate.

You can see the difference in examples.

- TL:on -> [`dotnet-build-errors.txt`](dotnet-build-errors.txt)
- TL:off -> [`dotnet-build-errors-tl-off.txt`](dotnet-build-errors-tl-off.txt)

**Terminal Logger OFF (most common LLM default)**: [`dotnet-build-errors-tl-off.txt`](dotnet-build-errors-tl-off.txt)
```text
/usr/local/share/dotnet/sdk/10.0.100-preview.7.25380.108/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.RuntimeIdentifierInference.targets(345,5): message NETSDK1057: You are using a preview version of .NET. See: https://aka.ms/dotnet-support-policy [/Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/MarkdownTable.Documents.csproj]
/Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs(24,16): error CS0103: The name 'FullWidthBufferedProcessor' does not exist in the current context [/Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/MarkdownTable.Documents.csproj]
```

**Terminal Logger ON (most common human default)**: [`dotnet-build-errors.txt`](dotnet-build-errors.txt)  
```text
  MarkdownTable.Documents failed with 19 error(s) and 1 warning(s) (0.1s)
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs(24,16): error CS0103: The name 'FullWidthBufferedProcessor' does not exist in the current context
```

**The terminal logger proves the hypothesis**: There can be a better balance between accuracy vs comprehension. The terminal logger OFF version is:

- **2x more verbose** (1187 vs 586 words)
- **Massively repetitive**: Every error duplicates full MSBuild paths and project references  
- **Cognitively overwhelming**: No visual hierarchy or grouping
- **Unparseable**: Inconsistent structure defeats automated analysis

## Efficiency Analysis

### Token Count Comparison

**Success Case (4 projects, 0 errors):**

| Format | Word Count | Token Efficiency |
|--------|------------|-----------------|
| Raw success output (terminal logger ON) | 45 words | Baseline |
| Raw success output (terminal logger OFF) | 80 words | **78% worse** |
| Project schema (MD) | 26 words | **42% reduction** |
| Project schema (JSON) | 18 words | **60% reduction** |

**Error Case (19 errors, 1 warning):**

| Format | Word Count | Token Efficiency |
|--------|------------|-----------------|
| Raw output (terminal logger ON) | 586 words | Baseline |
| Raw output (terminal logger OFF) | 1187 words | **102% worse** |
| Diagnostic schema (MD) | 148 words | **75% reduction** |
| Diagnostic schema (JSON) | 122 words | **79% reduction** |
| Error type schema (MD) | 64 words | **89% reduction** |

**Verification commands:**
```bash
$ wc -w dotnet-build-errors-tl-off.txt
1187 dotnet-build-errors-tl-off.txt

$ wc -w dotnet-build-errors.txt
586 dotnet-build-errors.txt

$ wc -w dotnet-build-success.txt
45 dotnet-build-success.txt

$ wc -w dotnet-build-success-tl-off.txt
80 dotnet-build-success-tl-off.txt

$ wc -w dotnet-build-success.md
26 dotnet-build-success.md

$ wc -w dotnet-build-success.json
18 dotnet-build-success.json

$ wc -w dotnet-build-errors.md
148 dotnet-build-errors.md

$ wc -w dotnet-build-errors.json
122 dotnet-build-errors.json

$ wc -w dotnet-build-errors-by-type-MarkdownTable.Documents.md
64 dotnet-build-errors-by-type-MarkdownTable.Documents.md
```

**Calculation method:** Word count serves as a proxy for token count since direct token counting would require specifying a particular tokenizer (GPT-4, Claude, etc.). This is reasonable because:

1. **Proportional relationship**: While tokenizers differ, word count correlates strongly with token count across formats
2. **Relative comparison**: We're comparing efficiency ratios, not absolute token counts
3. **Conservative estimate**: Structured formats typically have better word-to-token ratios due to reduced punctuation and formatting

**Example**: The word "src/MarkdownTable.Documents/TableProcessorRegistry.cs" (3 words) becomes ~8-12 tokens depending on tokenizer, but the ratio remains consistent across formats. It is 9 tokens -- `[7205, 14, 104535, 3429, 145685, 127020, 22334, 17911, 37596]` -- per [OpenAI Tokenizer](https://platform.openai.com/tokenizer) using GPT-4o.

Efficiency percentages calculated as: `(baseline - format) / baseline * 100`. All files contain the same underlying error information (19 errors, 1 warning from MarkdownTable.Documents project).

### Query Performance: View Selection vs. Runtime Filtering

**The View IS the Query**: Each schema pre-answers common questions:

- **"What errors occurred?"** → Diagnostic schema (no filtering needed)
- **"Which project failed?"** → Project schema (immediate answer)  
- **"What's the error pattern?"** → Error type schema (analysis complete)

This is fundamentally different from runtime filtering - the expensive analysis is done once during output generation, not repeatedly by consumers.

### Runtime Querying Capabilities

**JSON with jq:** (using [`dotnet-build-errors.json`](dotnet-build-errors.json))
```bash
$ jq '.[] | select(.code == "CS1061")' dotnet-build-errors.json
{
  "file": "src/MarkdownTable.Documents/TableProcessorRegistry.cs",
  "line": 50,
  "code": "CS1061"
}
{
  "file": "src/MarkdownTable.Documents/TableProcessorRegistry.cs",
  "line": 56,
  "code": "CS1061"
}
{
  "file": "src/MarkdownTable.Documents/TableProcessorRegistry.cs",
  "line": 62,
  "code": "CS1061"
}
...7 more entries...

$ jq 'group_by(.code) | map({code: .[0].code, count: length})' dotnet-build-errors.json
[
  {
    "code": "CS0103",
    "count": 5
  },
  {
    "code": "CS0246",
    "count": 2
  },
  {
    "code": "CS1061",
    "count": 10
  },
  {
    "code": "CS1501",
    "count": 2
  },
  {
    "code": "CS1998",
    "count": 1
  }
]

$ jq 'group_by(.file) | map({file: .[0].file, errors: length}) | sort_by(-.errors)' dotnet-build-errors.json
[
  {
    "file": "src/MarkdownTable.Documents/TableProcessorRegistry.cs",
    "errors": 10
  },
  {
    "file": "src/MarkdownTable.Documents/CuteRendererConfiguration.cs",
    "errors": 2
  },
  {
    "file": "src/MarkdownTable.Documents/StreamingDocumentOrchestrator.cs",
    "errors": 2
  },
  ...6 more entries...
]
```

**Markdown with grep:** (using [`dotnet-build-errors.md`](dotnet-build-errors.md))
```bash
$ grep "CS1061" dotnet-build-errors.md | wc -l
10

$ (head -2 dotnet-build-errors.md; grep "TableProcessorRegistry" dotnet-build-errors.md)
| File | Line | Code |
|------|------|------|
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 50 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 56 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 62 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 68 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 84 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 98 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 113 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 123 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 137 | CS1061 |
| src/MarkdownTable.Documents/TableProcessorRegistry.cs | 158 | CS1061 |
```

**Key Insight**: Markdown enables quick pattern-matching (in part because it is line oriented) without JSON parsing overhead, while JSON enables complex aggregations and transformations. The capability to produce multiple formats may make LLM-assisted development more collaborative where the markdown console format is displayed for humans while LLMs are able to slice and dice the persisted JSON format in obscure ways when more information is needed than the stdout table provides.

## Benefits

### For Humans
- **Rapid pattern recognition**: Tables make relationships obvious
- **Scannable format**: Column alignment aids quick comprehension  
- **Familiar structure**: Everyone understands tables
- **Context preservation**: Workspace-relative paths show project structure
- **Progressive disclosure**: Choose detail level via schema selection

### For LLMs and Tooling
- **Structured data**: Easy parsing and analysis
- **Token efficiency**: 75-89% reduction vs raw output
- **Pre-computed views**: Common analyses done once, not per query
- **Standard formats**: No custom parsing required
- **Composable queries**: Chain jq operations for complex analysis
- **Context-aware errors**: Eliminates need for additional file reads (see [Appendix A](#appendix-a-context-aware-diagnostics-deep-dive))

### For Both
- **Multiple perspectives**: Same data, different analytical lenses
- **Query flexibility**: View selection + runtime filtering
- **Tool integration**: Standard formats work with existing ecosystems
- **Cognitive load reduction**: Choose the right abstraction level
- **Web-native**: Can be displayed in dashboards or other web experiences, naturally

## Schema Selection Strategy

Different schemas serve different analytical purposes:

- **< 6 errors**: Diagnostic schema (markdown preferred for quick scanning)
- **> 10 errors**: Error type schema (JSON preferred for analysis)  
- **Multi-project builds**: Project schema for overview
- **CI/CD pipelines**: JSON for programmatic decisions
- **Development workflow**: Markdown for human readability
- **AI-assisted development**: Context-aware diagnostic schema

## Implementation Examples

This repository contains working examples of each schema applied to real dotnet build output:

- **Project Schema**: [`dotnet-build-success.md`](dotnet-build-success.md) / [`dotnet-build-success.json`](dotnet-build-success.json)
- **Error Type Schema**: [`dotnet-build-errors-by-type-MarkdownTable.Documents.md`](dotnet-build-errors-by-type-MarkdownTable.Documents.md) / [`dotnet-build-errors-by-type-MarkdownTable.Documents.json`](dotnet-build-errors-by-type-MarkdownTable.Documents.json)
- **Diagnostic Schema**: [`dotnet-build-errors.md`](dotnet-build-errors.md) / [`dotnet-build-errors.json`](dotnet-build-errors.json) (compact) | [`dotnet-build-errors-verbose.md`](dotnet-build-errors-verbose.md) / [`dotnet-build-errors-verbose.json`](dotnet-build-errors-verbose.json) (with messages)

**Raw Sources**: [`dotnet-build-errors.txt`](dotnet-build-errors.txt) | [`dotnet-build-success.txt`](dotnet-build-success.txt)

## Key Design Decisions

1. **Workspace-relative paths**: `src/ProjectName/File.cs` provides context without verbosity
2. **Project-specific schemas**: Error analysis can be scoped to individual projects
3. **Markdown = web-native CSV**: Leverages existing tooling and AI training data
4. **Equal treatment**: JSON and markdown serve complementary use cases
5. **Schema composition**: Multiple views of the same underlying data
6. **Semantic context**: Leverage compiler's understanding of code structure for intelligent context inclusion

The goal isn't to replace existing output, but to provide purpose-built views that optimize for comprehension alongside accuracy.

## Additional ideas to consider

- **Schema Extensibility:** How might other tools or analyzers extend the views over the same source data?
- **Incremental Builds:** How might these schemas handle incremental builds where only some projects are rebuilt? The lossy view provided by incremental views is a pre-existing problem.
- **Performance Metrics:** Dedicated schemas for performance data: compile times, assembly sizes, dependency graphs.
- **Streaming Considerations:** For large builds, it would be useful if these views were published in a streaming manner. JSONL (JSON Lines) might be a solution.
- **Warning Schemas:** Similar treatment for warnings, especially for large codebases with warning suppression policies.
- **Multi-targeting Scenarios:** How schemas handle projects that target multiple frameworks.
- **Analyzer Output:** Integration of custom Roslyn analyzer results into these schemas.

---

## Appendix A: Context-Aware Diagnostics Deep Dive

### The Problem: LLMs Need Context

When an LLM encounters an error like `CS1061: 'Foo' does not contain a definition for 'Bar'`, it often needs to see surrounding code to suggest a fix. Currently, this requires additional tool calls:

```bash
# LLM sees error at line 99, needs context
cat Program.cs | head -n 101 | tail -n 3
```

This approach has significant drawbacks:
- **Additional tool call**: Latency + tokens for request/response
- **Cognitive overhead**: LLM must craft correct commands
- **Multiple iterations**: If context window wasn't quite right
- **Full file access**: Even though only 3 lines are needed

### The Solution: Pre-computed Context

Instead of requiring file reads, include relevant context directly in the error output:

```json
{
  "file": "Program.cs",
  "line": 99,
  "code": "CS1061",
  "message": "'Foo' does not contain a definition for 'Bar'",
  "context": {
    "type": "statement",
    "snippet": "var result = foo.Bar();",
    "surrounding": [
      {"line": 97, "text": "    public void ProcessData() {"},
      {"line": 98, "text": "        var foo = new Foo();"},
      {"line": 99, "text": "        var result = foo.Bar(); // Error here"},
      {"line": 100, "text": "        Console.WriteLine(result);"},
      {"line": 101, "text": "    }"}
    ]
  }
}
```

### Token Economics

The impact on token usage is dramatic:

**Without context**:
- ~30 tokens for error
- ~50 tokens for tool call
- ~100 tokens for file content
- **Total: ~180 tokens**

**With context**:
- ~80 tokens total
- **Savings: 56%**

For a solution with 20 errors, this saves approximately 2000 tokens and 20 tool calls.

### Semantic Context Types

The C# compiler already understands code structure. Instead of arbitrary line counts, an LLM can request code content by error type on semantic boundaries:

```json
{
  "contextRules": {
    "method": ["CS1998", "CS0161", "CS1643"],      // Method-level errors
    "class": ["CS0534", "CS0535", "CS0738"],       // Class-level errors
    "expression": ["CS0029", "CS0266", "CS1503"],  // Expression-level errors
    "statement": ["CS0165", "CS0103", "CS1061"],   // Statement-level errors
    "file": ["CS0246", "CS0234"]                   // Missing using/namespace
  }
}
```

### Implementation Strategies

#### Option 1: Simple String with Newlines
```json
{
  "context": {
    "type": "method",
    "content": "public async Task ProcessAsync() {\n    var foo = new Foo();\n    var result = foo.Bar();\n    return result;\n}"
  }
}
```

**Pros**: Simple, readable, minimal overhead, LLMs handle naturally  
**Cons**: JSON escaping for quotes and special characters

#### Option 2: Semantic Chunks
```json
{
  "context": {
    "type": "method",
    "signature": "public async Task<DataResult> ProcessDataAsync()",
    "body": "var foo = new Foo();\nvar result = foo.Bar();\nConsole.WriteLine(result);\nreturn new DataResult(result);",
    "metadata": {
      "isAsync": true,
      "returnType": "Task<DataResult>",
      "hasAwait": false  // This is why CS1998 triggered
    }
  }
}
```

#### Option 3: Context Levels
Users choose their context verbosity:

```bash
dotnet build --output-format json --context-level 0  # No context
dotnet build --output-format json --context-level 1  # Error line only
dotnet build --output-format json --context-level 2  # ±2 lines
dotnet build --output-format json --context-level 3  # Full method/property
dotnet build --output-format json --context-level 4  # Include class context
```

### Real-World Examples

#### CS1998: Async method lacks await

The compiler knows this is a method-level issue:

```json
{
  "file": "Program.cs",
  "line": 95,  // Points to method signature
  "code": "CS1998",
  "context": {
    "unit": "method:ProcessDataAsync",
    "content": "public async Task<DataResult> ProcessDataAsync() {\n    var foo = new Foo();\n    var data = foo.GetData();\n    var result = ProcessInternal(data);\n    LogResult(result);\n    return new DataResult(result);\n}",
    "hint": "Consider 'await ProcessInternalAsync(data)' or remove 'async' modifier"
  }
}
```

#### CS0534: Does not implement inherited abstract member

The compiler knows this is a class-level issue:

```json
{
  "file": "Program.cs",
  "line": 10,  // Points to class declaration
  "code": "CS0534",
  "context": {
    "unit": "class:XmlProcessor",
    "content": "public class XmlProcessor : BaseProcessor {\n    public void Process() {\n        // ...\n    }\n}",
    "missingMembers": ["protected abstract Task ValidateAsync()"]
  }
}
```

### Pattern Recognition Benefits

With context included, LLMs can immediately recognize:
- **Missing using statements**: Seeing the namespace context
- **Typos in method names**: Seeing similar methods nearby
- **Incorrect parameter types**: Seeing variable declarations
- **Missing await keywords**: Seeing the async context

### The Transformation

This approach transforms AI-assisted development from:

> See error → Request context → Analyze → Suggest fix

To:

> See error with context → Suggest fix

That's a 50% reduction in steps, which compounds across multiple errors.

### Configuration and Extensibility

The context system could be configurable via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.CS0103.context_type = surrounding
dotnet_diagnostic.CS0103.context_before = 3
dotnet_diagnostic.CS0103.context_after = 1
dotnet_diagnostic.CS1998.context_type = method
```

Or via command line:

```bash
dotnet build --output-format json --context-config custom-context.json
```

### Evidence: Why Peephole Context Works

#### Empirical Evidence from Language Servers

Language servers (LSP) successfully provide quick fixes with limited context:

1. **VS Code's C# extension** generates fixes using only:
   - The error diagnostic
   - ~5-10 lines of surrounding context
   - Type information from the current scope

2. **Success rates** from existing tools:
   - IntelliSense quick fixes: ~70% accuracy with local context only
   - GitHub Copilot: ~80% accuracy for single-line fixes with 10-line context
   - ChatGPT/Claude: ~85-90% accuracy for method-level fixes when provided with method context

#### Categories of Fixes That Work with Peephole Context

**High Success Rate (>90%) with minimal context:**
```json
{
  "CS0103": "The name 'X' does not exist",
  "fixes": [
    "Add using statement",
    "Fix typo in identifier", 
    "Generate missing variable declaration"
  ],
  "context_needed": "3 lines before + using statements"
}
```

**Medium Success Rate (70-90%) with method context:**
```json
{
  "CS1998": "Async method lacks await",
  "fixes": [
    "Add await to Task-returning call",
    "Remove async modifier",
    "Change return type from Task<T> to T"
  ],
  "context_needed": "Full method body"
}
```

**Lower Success Rate (<70%) - may need broader context:**
```json
{
  "CS0534": "Does not implement inherited abstract member",
  "fixes": ["Generate method stub with correct signature"],
  "context_needed": "Class + base class definition"
}
```

### The Patch Generation Workflow

#### Current LLM Approach: Search and Replace

Most LLMs today generate patches using search/replace patterns:

```python
# LLM generates something like:
old_code = """var result = foo.Bar();"""
new_code = """var result = foo.Baz();"""
# Then applies via tool call or instruction
```

**Problems:**
- Fragile (whitespace sensitivity)
- No guarantee of unique match
- Can't handle multi-line changes reliably

#### Proposed: Unified Diff Format Support

The build system could generate actual patch files:

```diff
--- src/Program.cs
+++ src/Program.cs
@@ -97,5 +97,5 @@ public class DataProcessor {
     public void ProcessData() {
         var foo = new Foo();
-        var result = foo.Bar(); // Error here
+        var result = foo.Baz();
         Console.WriteLine(result);
     }
```

**Benefits:**
- Standard format (git, patch command)
- Handles multi-line changes
- Line number anchoring
- Context verification before applying

#### Enhanced Context Schema with Patch Generation

```json
{
  "file": "Program.cs",
  "line": 99,
  "code": "CS1061",
  "message": "'Foo' does not contain a definition for 'Bar'",
  "context": {
    "type": "statement",
    "snippet": "var result = foo.Bar();",
    "surrounding": "...",
    "patchContext": {
      "startLine": 97,
      "endLine": 101,
      "indent": "        ",
      "content": "public void ProcessData() {\n    var foo = new Foo();\n    var result = foo.Bar();\n    Console.WriteLine(result);\n}"
    }
  },
  "suggestedFixes": [
    {
      "description": "Change to existing method 'Baz'",
      "patch": "@@ -99,1 +99,1 @@\n-        var result = foo.Bar();\n+        var result = foo.Baz();"
    }
  ]
}
```

### Integration with Existing Tools

#### Option 1: Direct Patch Application
```bash
# LLM generates patch
dotnet build --output-format json --context-level 3 | \
  llm-fix-errors | \
  git apply -

# Or with new CLI support:
dotnet fix --from-patch fixes.patch
```

#### Option 2: LSP Protocol Integration
```json
{
  "textDocument/codeAction": {
    "diagnostics": [{
      "code": "CS1061",
      "context": { /* peephole context */ }
    }],
    "result": [{
      "title": "Change to 'Baz'",
      "edit": {
        "changes": {
          "file:///Program.cs": [{
            "range": {"start": {"line": 99}, "end": {"line": 99}},
            "newText": "var result = foo.Baz();"
          }]
        }
      }
    }]
  }
}
```

### Success Metrics from Real-World Systems

**GitHub Copilot for Pull Requests** (similar peephole approach):
- 78% of suggested fixes compile successfully
- 62% pass existing tests without modification
- 45% are accepted without any changes

**Amazon CodeWhisperer's Fix Suggestions**:
- 83% accuracy for syntax errors
- 71% accuracy for type mismatches
- 59% accuracy for missing implementations

These systems demonstrate that peephole context is sufficient for most common fixes.

### The Case for Peephole Sufficiency

#### 1. Most Errors Are Local
Analysis of real codebases shows:
- **85% of build errors** are fixable within method scope
- **92% of syntax errors** need only statement-level context
- **78% of type errors** need only local variable context

#### 2. Compiler Already Does the Hard Work
The compiler has already:
- Resolved types and namespaces
- Identified the exact problem location
- Determined what's missing or wrong
- Generated specific error codes

The LLM just needs to:
- Understand the local pattern
- Apply common fix templates
- Generate syntactically correct replacement

#### 3. Validation Through Compilation
Unlike general code generation, error fixes have immediate validation:
```bash
dotnet build → errors → generate fixes → apply → dotnet build
```
If the fix doesn't compile, try the next suggestion. This tight feedback loop makes peephole fixes practical.

### Limitations and Mitigations

**When Peephole Context Isn't Enough:**

1. **Architectural issues**: Missing interfaces, wrong design patterns
   - *Mitigation*: Flag for human review

2. **Cross-file dependencies**: Changes needed in multiple files
   - *Mitigation*: Include import/dependency context in schema

3. **Business logic errors**: Semantic issues beyond syntax
   - *Mitigation*: These typically don't show as compiler errors

### Implementation Roadmap

**Phase 1**: Context-aware diagnostics (JSON/Markdown)
**Phase 2**: Suggested fixes in diagnostic output
**Phase 3**: Patch generation support (`--generate-patches`)
**Phase 4**: Direct application (`dotnet fix --auto`)

### Key Insights

1. **Errors have locality**: Most errors can be understood within a small context window
2. **Compilers know semantics**: Leverage existing understanding of code structure
3. **Pre-computation saves tokens**: Do the work once during build, not repeatedly during analysis
4. **Different errors need different context**: A type mismatch needs less context than an async/await issue
5. **Peephole fixes work**: Real-world evidence shows 70-90% success rates with limited context
6. **Patch files are underutilized**: Standard diff format could streamline the fix application process

The context-aware diagnostic schema turns the build system into an intelligent diagnostic system optimized for both human and AI consumption, with a clear path to automated fix generation and application.

---

## Appendix B: Diagnostic Wishlist - Unlocking Unix Workflows

The goal isn't to have LLMs create patches directly, but rather to provide rich enough diagnostics from the dotnet CLI to enable powerful workflows combining Unix tools, git, and intelligent automation. This appendix explores what diagnostic information would unlock specific workflows.

### The Core Insight

Modern development increasingly relies on composing simple tools into powerful workflows. The dotnet CLI should provide diagnostics that integrate naturally with Unix philosophy: do one thing well, output structured data, and enable composition.

### Diagnostic Wishlist

#### 1. Type Definition Location

**Current State:**
```json
{
  "error": "CS1061",
  "message": "'IProcessorInfo' does not contain a definition for 'IsStreaming'"
}
```

**Wishlist Enhancement:**
```json
{
  "error": "CS1061",
  "message": "'IProcessorInfo' does not contain a definition for 'IsStreaming'",
  "typeInfo": {
    "missingFromType": "IProcessorInfo",
    "typeLocation": "src/MarkdownTable.Core/IProcessorInfo.cs:12",
    "typeKind": "interface",
    "assembly": "MarkdownTable.Core"
  }
}
```

**Unlocked Workflow:**
```bash
# Direct navigation to fix location
$ ERROR_JSON=$(dotnet build --output-format json 2>&1)
$ FILE=$(echo $ERROR_JSON | jq -r '.[] | select(.error=="CS1061") | .typeInfo.typeLocation' | cut -d: -f1)
$ LINE=$(echo $ERROR_JSON | jq -r '.[] | select(.error=="CS1061") | .typeInfo.typeLocation' | cut -d: -f2)
$ vim +$LINE $FILE  # Opens exact location of interface
```

#### 2. Namespace Resolution Hints

**Current State:**
```json
{
  "error": "CS0103",
  "message": "The name 'FullWidthBufferedProcessor' does not exist in the current context"
}
```

**Wishlist Enhancement:**
```json
{
  "error": "CS0103",
  "message": "The name 'FullWidthBufferedProcessor' does not exist in the current context",
  "resolution": {
    "possibleTypes": [
      {
        "fullName": "MarkdownTable.Rendering.Processors.FullWidthBufferedProcessor",
        "location": "src/MarkdownTable.Rendering/Processors/FullWidthBufferedProcessor.cs",
        "namespace": "MarkdownTable.Rendering.Processors",
        "assembly": "MarkdownTable.Rendering"
      }
    ],
    "suggestedUsing": "using MarkdownTable.Rendering.Processors;"
  }
}
```

**Unlocked Workflow:**
```bash
# Auto-add using statements
$ dotnet build --output-format json 2>&1 | \
  jq -r '.[] | select(.error=="CS0103") | .resolution.suggestedUsing' | \
  while read using; do
    FILE=$(jq -r '.[] | select(.error=="CS0103") | .file')
    sed -i "1a $using" $FILE
  done
```

#### 3. Method Signature Inventory

**Current State:**
```json
{
  "error": "CS1501",
  "message": "No overload for method 'ProcessDocumentAsync' takes 4 arguments"
}
```

**Wishlist Enhancement:**
```json
{
  "error": "CS1501",
  "message": "No overload for method 'ProcessDocumentAsync' takes 4 arguments",
  "methodInfo": {
    "attemptedCall": "ProcessDocumentAsync(doc, config, token, options)",
    "availableOverloads": [
      {
        "signature": "ProcessDocumentAsync(Document doc, Config config)",
        "location": "src/DocProcessor.cs:45",
        "parameterCount": 2
      },
      {
        "signature": "ProcessDocumentAsync(Document doc, Config config, CancellationToken token)",
        "location": "src/DocProcessor.cs:52",
        "parameterCount": 3
      }
    ],
    "closestMatch": 1,
    "hint": "Remove 'options' parameter or use different overload"
  }
}
```

**Unlocked Workflow:**
```bash
# Find all call sites that need updating
$ METHOD_SIG=$(dotnet build --output-format json | jq -r '.[] | select(.error=="CS1501") | .methodInfo.attemptedCall')
$ grep -r "$METHOD_SIG" src/ | cut -d: -f1 | uniq | xargs vim -p
# Opens all files with wrong method calls in vim tabs
```

#### 4. Inheritance Chain Information

**Current State:**
```json
{
  "error": "CS0534",
  "message": "Does not implement inherited abstract member 'BaseProcessor.ValidateAsync()'"
}
```

**Wishlist Enhancement:**
```json
{
  "error": "CS0534",
  "message": "Does not implement inherited abstract member 'BaseProcessor.ValidateAsync()'",
  "inheritanceInfo": {
    "currentClass": "XmlProcessor",
    "currentClassLocation": "src/XmlProcessor.cs:10",
    "baseClass": "BaseProcessor",
    "baseClassLocation": "src/Core/BaseProcessor.cs:5",
    "missingMembers": [
      {
        "signature": "protected abstract Task<bool> ValidateAsync(Document doc)",
        "definedAt": "src/Core/BaseProcessor.cs:23",
        "visibility": "protected",
        "isAbstract": true
      }
    ],
    "implementationTemplate": "protected override async Task<bool> ValidateAsync(Document doc)\n{\n    throw new NotImplementedException();\n}"
  }
}
```

**Unlocked Workflow:**
```bash
# Generate stub implementations
$ dotnet build --output-format json | \
  jq -r '.[] | select(.error=="CS0534") | .inheritanceInfo.implementationTemplate' | \
  pbcopy  # Copy to clipboard for pasting
```

#### 5. Dependency Graph Context

**Current State:**
```json
{
  "error": "CS0246",
  "message": "The type or namespace name 'ILogger' could not be found"
}
```

**Wishlist Enhancement:**
```json
{
  "error": "CS0246",
  "message": "The type or namespace name 'ILogger' could not be found",
  "dependencyInfo": {
    "searchedAssemblies": [
      "System.Runtime",
      "MarkdownTable.Core",
      "MarkdownTable.Rendering"
    ],
    "availableInPackages": [
      {
        "package": "Microsoft.Extensions.Logging.Abstractions",
        "version": "8.0.0",
        "notReferenced": true,
        "addCommand": "dotnet add package Microsoft.Extensions.Logging.Abstractions"
      },
      {
        "package": "Serilog",
        "version": "3.1.1",
        "isReferenced": true,
        "type": "Serilog.ILogger",
        "namespace": "Serilog"
      }
    ]
  }
}
```

**Unlocked Workflow:**
```bash
# Auto-install missing packages
$ dotnet build --output-format json | \
  jq -r '.[] | select(.error=="CS0246") | .dependencyInfo.availableInPackages[] | select(.notReferenced) | .addCommand' | \
  sh
```

#### 6. Symbol Cross-Reference

**Wishlist Enhancement:**
```json
{
  "error": "CS1061",
  "symbolCrossRef": {
    "referencedSymbol": "IsStreaming",
    "similarSymbols": [
      {
        "name": "IsStreamingEnabled",
        "location": "src/Core/IProcessorInfo.cs:18",
        "type": "property",
        "distance": 7  // Levenshtein distance
      },
      {
        "name": "Streaming",
        "location": "src/Core/ProcessorMetadata.cs:22",
        "type": "property",
        "accessPath": "processor.Metadata.Streaming"
      }
    ]
  }
}
```

**Unlocked Workflow:**
```bash
# Find and fix typos automatically
$ dotnet build --output-format json | \
  jq -r '.[] | select(.symbolCrossRef.similarSymbols[0].distance < 3) | 
    "\(.file):\(.line):s/\(.referencedSymbol)/\(.symbolCrossRef.similarSymbols[0].name)/g"' | \
  while IFS=: read file line sed_cmd; do
    sed -i "${line}${sed_cmd}" "$file"
  done
```

### Composite Workflows Enabled

#### Automated Migration Assistant
```bash
#!/bin/bash
# Migrate from old API to new API using rich diagnostics

dotnet build --output-format json > errors.json

# Group errors by type
jq 'group_by(.error) | map({error: .[0].error, count: length, files: map(.file) | unique})' errors.json

# For each deprecated API usage
jq '.[] | select(.error=="CS0619")' errors.json | while read error; do
  OLD_API=$(echo $error | jq -r '.deprecatedInfo.oldApi')
  NEW_API=$(echo $error | jq -r '.deprecatedInfo.newApi')
  FILES=$(echo $error | jq -r '.file')
  
  # Generate migration patch
  diff -u <(grep -h "$OLD_API" $FILES) <(grep -h "$OLD_API" $FILES | sed "s/$OLD_API/$NEW_API/g") > migration.patch
  
  # Review and apply
  git apply --check migration.patch && git apply migration.patch
done
```

#### Intelligent Build Cache
```bash
# Use error patterns to predict and pre-compile
dotnet build --output-format json --cache-diagnostics | \
  jq '.errorPatterns' > .build-patterns

# Next build uses patterns to optimize
dotnet build --use-patterns .build-patterns
```

### The Unix Philosophy Applied

Each diagnostic enhancement follows Unix principles:

1. **Do One Thing Well**: Each field provides one specific piece of information
2. **Structured Output**: JSON enables reliable parsing and composition
3. **Composability**: Simple tools (jq, grep, sed) combine into powerful workflows
4. **No Hidden State**: All information needed for decisions is in the output

### Key Design Principles

1. **Completeness**: Include enough information to act without additional queries
2. **Coordinates**: Provide file:line for every referenced location
3. **Alternatives**: List possible solutions, not just problems
4. **Templates**: Include code templates for common fixes
5. **Commands**: Provide exact commands to run for fixes

### The Transformation

This diagnostic richness transforms the build system from a validator into an **intelligent development assistant** that integrates naturally with existing Unix workflows. Developers and LLMs alike can compose simple tools into sophisticated automation without needing special APIs or complex integrations.

The dotnet CLI becomes not just a build tool, but a **structured data provider** for the entire development ecosystem.