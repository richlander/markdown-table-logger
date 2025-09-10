#!/bin/bash

# Build script for dotnet-cli-output
# Publishes SymbolIndexer and builds MarkdownTableLogger in Release configuration

set -e  # Exit on any error

echo "Building dotnet-cli-output components..."
echo

# Create bin directory if it doesn't exist
mkdir -p bin

# Build and publish SymbolIndexer
echo "Publishing SymbolIndexer to bin/SymbolIndexer..."
dotnet publish src/SymbolIndexer -c Release -o bin/SymbolIndexer
echo "✓ SymbolIndexer published"
echo

# Build MarkdownTableLogger  
echo "Building MarkdownTableLogger to bin/MarkdownTableLogger..."
dotnet build src/MarkdownTableLogger -c Release -o bin/MarkdownTableLogger
echo "✓ MarkdownTableLogger built"
echo

echo "Build complete!"
echo
echo "Usage:"
echo "  # Start symbol indexer daemon (run from repository root)"
echo "  ./bin/SymbolIndexer/SymbolIndexer start &"
echo
echo "  # Use logger with dotnet build"
echo "  dotnet build --logger:\"bin/MarkdownTableLogger/MarkdownTableLogger.dll;mode=prompt\" --noconsolelogger"