# Semantic Model Upgrade Plan

## Overview

We will replace the current heuristic-based symbol classification with a Roslyn semantic-model pipeline. This enables accurate tagging of .NET framework symbols, NuGet package symbols, user-defined code, and unresolved references without special-casing identifiers.

## Work Breakdown

1. **Compilation Infrastructure**
   - Load all workspace `.cs` files plus project metadata.
   - Create a `CSharpCompilation` with standard framework metadata references.
   - Implement incremental updates (reuse syntax trees, update changed files via `WithChangedText`).
   - Cache `SemanticModel` instances for hot queries; invalidate on compilation updates.

2. **Indexing Pipeline**
   - During rebuild, walk semantic model to gather declared symbols and their locations.
   - Persist symbol metadata (symbol kind, containing assembly, locations) for fast lookup.
   - Handle file deletes/renames by removing entries tied to missing paths.

3. **Query Flow**
   - Map cursor position → syntax node; call `SemanticModel.GetSymbolInfo` / `GetDeclaredSymbol`.
   - Walk symbol hierarchy (namespace → type → member) to surface meaningful results.
   - Return `SymbolResult` objects populated with symbol kind, assembly info, and definition locations.

4. **Classification Rules (Before → After)**
   - **.NET Libraries**: Previously heuristic; now any symbol whose `ContainingAssembly` matches framework references (`System.*`, `Microsoft.*`) is classified as Framework with precise namespace/type info.
   - **Package**: Previously mostly unknown; now symbols whose containing assembly maps to project references (non-framework) are marked External, using actual assembly/package names.
   - **User Code**: Previously matched by identifier strings; now `GetDeclaredSymbol` provides exact source locations, eliminating stale matches.
   - **Unknown**: Previously catch-all; now only when Roslyn returns `null` (e.g., unresolved identifier). Candidate symbols enable richer guidance later.

5. **CLI & Daemon Integration**
   - Keep PID/idle/discovery logic as-is.
   - Update CLI `symbols query` output to include symbol kind/assembly from semantic data.
   - Optionally add `dotnet-logs symbols inspect` for semantic debugging.

6. **Validation**
   - Add unit tests covering each category: framework namespace, NuGet type, user-defined class, unresolved identifier.
   - Run MarkdownTableLogger prompt mode end-to-end; confirm referenced symbol sections reflect improved data.
   - Compare `_logs` output to capture before/after behavior for the four primary categories.

## Delivery Notes

- This refactor can ship in stages (compilation infrastructure first, package mapping next), but the full value arrives once Roslyn drives classification. We'll start implementing immediately.

