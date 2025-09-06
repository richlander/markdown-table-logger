# Improving CLI Output for Human and AI Comprehension

We're now in the age of LLMs. It turns out that coding is the first "killer app" of this new technology paradigm. There is a significant opportunity to (re-)evolve build systems into queryable data systems. In a world of token limits, the dev platforms the consumes the least tokens is the one that enables LLMs to apply those token to the problem at hand and to write the best code (most or possibly least). This is the secret "hiding in plain sight". Build system output is likely the most significiant discretionary contributor to token exhaustion.

## Hypothesis

The dotnet CLI and other build systems produce verbose output biased toward accuracy rather than comprehension, particularly for "at a glance" workflows. This creates problems for both humans and LLMs trying to quickly understand build results.

The Docker CLI includes a "party trick" where Go format templates can reformat output, however, this approach is awkward for many users. Instead, multiple purpose-built user-selectable views in standard formats (with obvious column demarcators) would greatly aid all users.

These views can slice and dice the data, not unlike social media preview cards. JSON has commonly been used for this purpose. Although unconventional, markdown tables can be just as useful and often superior. Markdown can be thought of as web-native CSV. It shows up prominently in AI-assistant workflows and is immediately readable by humans. It can also be pretty-printed, as needed.

The concept of views is an invitation for lossy information. That could mean that `dotnet build` needs to be called multiple times, either for multiple views or to process the same view multiple times (since stdout might be consumed by piping to `jq`). The `tee` tool can solve one of these problems by persisting the consumed console stream to disk. That's good, but inelegant. A better idea is that CLI would (A) enable writing the output to both stdout and to disk, and (B) writing different selectable views to each. This approach could break the tie between comprehension and accuracy, by writing summary markdown to the console and more expansive JSON to disk, only used if the markdown output suggests a need.

The dotnet CLI should embrace markdown and JSON equally. We often think about JSON in terms of schemas but not markdown. That's solely a gap in industry imagination.

## Current State vs. Structured Output

### Success Case: Clean but Limited

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

### Failure Case: Comprehension Breakdown

**Source:** [`dotnet-build-errors.txt`](dotnet-build-errors.txt)

```text
  MarkdownTable.Documents failed with 19 error(s) and 1 warning(s) (0.1s)
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs(24,16): error CS0103: The name 'FullWidthBufferedProcessor' does not exist in the current context
    /Users/rich/git/smooth-markdown-table/src/MarkdownTable.Documents/IdentityRendererConfiguration.cs(23,16): error CS0103: The name 'IdentityStreamingProcessor' does not exist in the current context
    ...18 more similar lines...
```

**Problems:**
- Much the same as the success case
- Verbose file paths (most LLMs are live in a workspace-relative world)
- Error codes buried in text
- Non-standard demarcators (for example, ':' is used more as "for more information" than a column)
- Row/Column information require indexing on '(' and ')' characters.

### The dual personality logger problem

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

## Proposed Schema-Based Views

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

### 2. Error Type Schema (Project-scoped)
**Purpose:** Pattern analysis, prioritization  
**Files:** [`dotnet-build-errors-by-type-MarkdownTable.Documents.md`](dotnet-build-errors-by-type-MarkdownTable.Documents.md) | [`dotnet-build-errors-by-type-MarkdownTable.Documents.json`](dotnet-build-errors-by-type-MarkdownTable.Documents.json)

The "Description" column would be optional.

```markdown
| Code | Count | Description |
|------|-------|-------------|
| CS1061 | 10 | Member does not exist |
| CS0103 | 7 | Name does not exist in current context |
| CS0246 | 2 | Type or namespace not found |
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

**JSON equivalent:**
```json
[
  {"file": "src/MarkdownTable.Documents/FullWidthRendererConfiguration.cs", "line": 24, "code": "CS0103", "message": "The name 'FullWidthBufferedProcessor' does not exist in the current context"},
  {"file": "src/MarkdownTable.Documents/TableProcessorRegistry.cs", "line": 50, "code": "CS1061", "'IProcessorInfo' does not contain a definition for 'IsStreaming'"}
]
```

## Efficiency Analysis

### Token Count Comparison

| Format | Word Count | Token Efficiency |
|--------|------------|-----------------|
| Raw output (terminal logger OFF) | 1187 words | Baseline (worst) |
| Raw output (terminal logger ON) | 586 words | **51% reduction** |
| Diagnostic schema (MD) | 148 words | **87% reduction** |
| Diagnostic schema (JSON) | 122 words | **90% reduction** |
| Error type schema (MD) | 64 words | **95% reduction** |

**Verification commands:**
```bash
$ wc -w dotnet-build-errors-tl-off.txt
1187 dotnet-build-errors-tl-off.txt

$ wc -w dotnet-build-errors.txt
586 dotnet-build-errors.txt

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

$ grep "TableProcessorRegistry" dotnet-build-errors.md
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

**Key Insight**: Markdown enables quick pattern-matching without JSON parsing overhead, while JSON enables complex aggregations and transformations.

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

The goal isn't to replace existing output, but to provide purpose-built views that optimize for comprehension alongside accuracy.
