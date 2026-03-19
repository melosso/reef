#!/usr/bin/env bash
set -euo pipefail

# Reef - Publish Script

# Check required tools
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet is not installed or not in PATH" >&2
    exit 1
fi

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
REPO_BASE=$(cd "$SCRIPT_DIR/../.." && pwd)
SOURCE_DIR="$SCRIPT_DIR"
DEPLOYMENT_DIR="$REPO_BASE/Deployment"

echo "Publishing Reef..."

# Clean deployment directory
find "$DEPLOYMENT_DIR" -mindepth 1 -delete 2>/dev/null || true
mkdir -p "$DEPLOYMENT_DIR"

# Clean build
dotnet clean "$SOURCE_DIR" -c Release
rm -rf "$SOURCE_DIR/obj" || true
rm -rf "$SOURCE_DIR/bin" || true

# Publish
echo "Building framework-dependent executable..."
dotnet publish "$SOURCE_DIR" -c Release -r win-x64 \
  -p:PublishSingleFile=true \
  --self-contained false \
  -o "$DEPLOYMENT_DIR"

# Create folder structure
mkdir -p "$DEPLOYMENT_DIR/exports"
mkdir -p "$DEPLOYMENT_DIR/log"
echo "Created folder structure"

# wwwroot is already included in the publish output, no need to copy manually

# Remove unnecessary files (including Development config)
for pattern in "*.pdb" "*.xml" "*.deps.json" "web.config" "appsettings.Development.json"; do
    find "$DEPLOYMENT_DIR" -name "$pattern" -delete 2>/dev/null || true
done
echo "Cleaned up unnecessary files"

# Create service batch file
touch "$DEPLOYMENT_DIR/Reef.bat"
cat > "$DEPLOYMENT_DIR/Reef.bat" << 'BATEOF'
@echo off
setlocal

set SERVICE_NAME=ReefExportService
set DISPLAY_NAME=Reef Export Service
set EXE_PATH=%~dp0Reef.exe

if "%1"=="" (
    echo Usage: %0 {install^|start^|stop^|status^|uninstall^|test}
    goto :eof
)

if /i "%1"=="install" (
    sc create "%SERVICE_NAME%" binPath= "\"%EXE_PATH%\"" start= auto displayname= "%DISPLAY_NAME%"
    sc description "%SERVICE_NAME%" "A lightweight data export and automation service"
    echo Service installed. Run '%0 start' to start the service.
    goto :eof
)

if /i "%1"=="start" (
    sc start "%SERVICE_NAME%"
    goto :eof
)

if /i "%1"=="stop" (
    sc stop "%SERVICE_NAME%"
    goto :eof
)

if /i "%1"=="status" (
    sc query "%SERVICE_NAME%"
    goto :eof
)

if /i "%1"=="uninstall" (
    sc stop "%SERVICE_NAME%"
    timeout /t 2
    sc delete "%SERVICE_NAME%"
    echo Service uninstalled.
    goto :eof
)

if /i "%1"=="test" (
    echo Testing Reef in console mode...
    "%EXE_PATH%"
    goto :eof
)
BATEOF
echo "Created service management script"

# Create README
touch "$DEPLOYMENT_DIR/README.txt"
cat > "$DEPLOYMENT_DIR/README.txt" << 'READMEEOF'
REEF SERVICE DEPLOYMENT
============================

PREREQUISITES
-------------
** REQUIRED: .NET 10.0 Runtime **
Download: https://dotnet.microsoft.com/download/dotnet/9.0
Install "ASP.NET Core Runtime 9.0.x" for Windows x64

TESTING
-------
1. Run 'Reef.bat test' to test in console mode
2. Press Ctrl+C to stop
3. Check logs in 'log' folder

SERVICE INSTALLATION
--------------------
1. Run 'Reef.bat install' as Administrator
2. Run 'Reef.bat start' to start the service
3. Open browser: http://localhost:8085

SERVICE MANAGEMENT
------------------
- Start:     Reef.bat start
- Stop:      Reef.bat stop
- Status:    Reef.bat status
- Uninstall: Reef.bat uninstall

CONFIGURATION
-------------
1. Set REEF_ENCRYPTION_KEY environment variable
2. Edit appsettings.json for custom settings

TROUBLESHOOTING
---------------
- Check logs in 'log' folder
- Run 'Reef.bat status' for service status
- Test in console mode first: 'Reef.bat test'
READMEEOF

# Summary
EXE_FILE="$DEPLOYMENT_DIR/Reef.exe"
if [ -f "$EXE_FILE" ]; then
    SIZE_BYTES=$(stat -c%s "$EXE_FILE")
    SIZE_MB=$(awk "BEGIN { printf \"%.2f\", $SIZE_BYTES / 1048576 }")
    echo ""
    echo "SUCCESS: Reef published to $DEPLOYMENT_DIR"
    echo "   - Executable Size: ${SIZE_MB} MB"
    echo "   - Deployment Type: Framework-dependent (requires .NET 10.0 Runtime)"
    echo ""
    echo "Next steps:"
    echo "   1. Ensure .NET 10.0 Runtime is installed on target machine"
    echo "   2. Set REEF_ENCRYPTION_KEY environment variable"
    echo "   3. Run 'Reef.bat test' to test"
    echo "   4. Run 'Reef.bat install' to install as service"
else
    echo "ERROR: Publishing failed" >&2
    exit 1
fi
