#!/bin/bash

# Test script for the MarkdownTableLogger prototype
# This demonstrates the structured output generation

echo "🧪 Testing MarkdownTableLogger Prototype"
echo "===================================="
echo ""

# Check if we have a test project to build
if [ ! -d "src" ]; then
    echo "❌ No src directory found. This should be run from the repository root."
    exit 1
fi

# Build the logger first
echo "📦 Building MarkdownTableLogger..."
cd src/MarkdownTableLogger
dotnet build --verbosity quiet
if [ $? -ne 0 ]; then
    echo "❌ Failed to build MarkdownTableLogger"
    exit 1
fi
echo "✅ MarkdownTableLogger built successfully"

# Go back to root
cd ../..

# Get the logger path
LOGGER_PATH=$(find src/MarkdownTableLogger/bin -name "MarkdownTableLogger.dll" | head -1)
if [ -z "$LOGGER_PATH" ]; then
    echo "❌ Could not find MarkdownTableLogger.dll"
    exit 1
fi

echo "🔍 Logger found at: $LOGGER_PATH"
echo ""

# Find a project to test with
TEST_PROJECT=""
if [ -f "*.sln" ]; then
    echo "🎯 Testing with solution..."
    TEST_PROJECT=""
elif [ -d "src" ]; then
    # Look for any csproj file
    TEST_PROJECT=$(find src -name "*.csproj" | head -1)
fi

if [ -z "$TEST_PROJECT" ]; then
    echo "🎯 Testing with current directory (no specific project)"
    TEST_ARGS=""
else
    echo "🎯 Testing with project: $TEST_PROJECT"
    TEST_ARGS="$TEST_PROJECT"
fi

echo ""
echo "🚀 Running dotnet build with MarkdownTableLogger..."
echo "Command: dotnet build $TEST_ARGS --logger:\"$LOGGER_PATH\""
echo ""

# Run the build with our custom logger and quiet verbosity to reduce noise
dotnet build $TEST_ARGS --logger:"$LOGGER_PATH" --verbosity quiet

echo ""
echo "📄 Generated files:"
ls -la dotnet-build-*.{md,json} 2>/dev/null || echo "No files generated (build may have succeeded without errors)"

echo ""
echo "🔍 Preview of generated content:"
for file in dotnet-build-*.md; do
    if [ -f "$file" ]; then
        echo ""
        echo "--- $file ---"
        head -10 "$file"
        if [ $(wc -l < "$file") -gt 10 ]; then
            echo "... (truncated)"
        fi
    fi
done

echo ""
echo "✨ Test complete! Check the generated .md and .json files for structured output."