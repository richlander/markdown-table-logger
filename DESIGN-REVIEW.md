# Design Review Notes – Semantic Model Branch

## Critical Findings

### 1. Compilation Missing Framework References (High)
- Location: `src/SymbolIndexer/RoslynSymbolIndexer.cs:380-417`
- Issue: `LoadMetadataReferences()` populates references using `Assembly.Location`. With `<PublishAot>true>` in the project, those calls return empty strings, so the Roslyn compilation has no metadata references. Consequently `QuerySymbolsAsync` (and `dotnet-logs symbols query`) always return "No symbols found." 
- Recommendation: Populate references via a reliable mechanism (`AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")`, `DependencyContext`, or an explicit reference-pack path), and fail fast or log prominently when the reference list is empty.

### 2. Symbol Index Cleared After Rebuild (High)
- Location: `src/SymbolIndexer/RoslynSymbolIndexer.cs:94-112`
- Issue: `RebuildIndexAsync` iterates through `UpdateFileAsync` (which repopulates `_symbolIndex`), but immediately afterwards calls `_symbolIndex.Clear()`, erasing the freshly built cache.
- Recommendation: Clear `_symbolIndex` (and `_syntaxTrees`) before the rebuild loop or drop the clear entirely so the index remains populated.

## Additional Notes
- `SymbolIndexerService` now hooks rename/delete events via `RemoveFileAsync`; once references are fixed this path should be validated to ensure compilation and cache stay consistent.
- The new `IndexedSymbol` record (`src/SymbolIndexer/SymbolQuery.cs`) captures namespace, kind, containing type, and signature—once the semantic model resolves symbols we can surface that richer context in CLI output.

---
Addressing these blockers will restore symbol queries and unblock downstream consumers like MarkdownTableLogger.
