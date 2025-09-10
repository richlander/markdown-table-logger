@echo off
REM Build script for dotnet-cli-output
REM Publishes SymbolIndexer and builds MarkdownTableLogger in Release configuration

echo Building dotnet-cli-output components...
echo.

REM Create bin directory if it doesn't exist
if not exist "bin" mkdir bin

REM Build and publish SymbolIndexer
echo Publishing SymbolIndexer to bin\SymbolIndexer...
dotnet publish src\SymbolIndexer -c Release -o bin\SymbolIndexer
if %ERRORLEVEL% neq 0 (
    echo Error building SymbolIndexer
    exit /b %ERRORLEVEL%
)
echo ^> SymbolIndexer published
echo.

REM Build MarkdownTableLogger  
echo Building MarkdownTableLogger to bin\MarkdownTableLogger...
dotnet build src\MarkdownTableLogger -c Release -o bin\MarkdownTableLogger
if %ERRORLEVEL% neq 0 (
    echo Error building MarkdownTableLogger
    exit /b %ERRORLEVEL%
)
echo ^> MarkdownTableLogger built
echo.

echo Build complete!
echo.
echo Usage:
echo   # Start symbol indexer daemon (run from repository root)
echo   .\bin\SymbolIndexer\SymbolIndexer.exe start
echo.
echo   # Use logger with dotnet build
echo   dotnet build --logger:"bin\MarkdownTableLogger\MarkdownTableLogger.dll;mode=prompt" --noconsolelogger