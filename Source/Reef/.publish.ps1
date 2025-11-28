# Reef - Publish Script

$repoBase = "D:\Repository\Reef"
$sourceDir = "$repoBase\Source\Reef"
$deploymentDir = "$repoBase\Deployment"

Write-Host "Publishing Reef..." -ForegroundColor Cyan

# Clean deployment directory
if (Test-Path $deploymentDir) {
    Remove-Item "$deploymentDir\*" -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -Path $deploymentDir -ItemType Directory -Force | Out-Null

# Clean build
dotnet clean $sourceDir -c Release
Remove-Item "$sourceDir\obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$sourceDir\bin" -Recurse -Force -ErrorAction SilentlyContinue

# Publish
Write-Host "Building framework-dependent executable..." -ForegroundColor Yellow
dotnet publish $sourceDir -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  --self-contained false `
  -o $deploymentDir

# Create folder structure
New-Item "$deploymentDir\exports" -ItemType Directory -Force | Out-Null
New-Item "$deploymentDir\log" -ItemType Directory -Force | Out-Null
Write-Host "Created folder structure" -ForegroundColor Green

# wwwroot is already included in the publish output, no need to copy manually

# Remove unnecessary files (including Development config)
$filesToRemove = @("*.pdb", "*.xml", "*.deps.json", "web.config", "appsettings.Development.json")
foreach ($pattern in $filesToRemove) {
    Get-ChildItem -Path $deploymentDir -Filter $pattern -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
}
Write-Host "Cleaned up unnecessary files" -ForegroundColor Green

# Create service batch file
$serviceBat = @"
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
"@

Set-Content -Path "$deploymentDir\Reef.bat" -Value $serviceBat -Encoding ASCII
Write-Host "Created service management script" -ForegroundColor Green

# Create README
$readme = @"
REEF SERVICE DEPLOYMENT
============================

PREREQUISITES
-------------
** REQUIRED: .NET 9.0 Runtime **
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
"@

Set-Content -Path "$deploymentDir\README.txt" -Value $readme -Encoding UTF8

# Summary
$exeFile = Get-ChildItem "$deploymentDir\Reef.exe" -ErrorAction SilentlyContinue
if ($exeFile) {
    $sizeInMB = [math]::Round($exeFile.Length / 1MB, 2)
    Write-Host ""
    Write-Host "SUCCESS: Reef published to $deploymentDir" -ForegroundColor Green
    Write-Host "   - Executable Size: $sizeInMB MB" -ForegroundColor Green
    Write-Host "   - Deployment Type: Framework-dependent (requires .NET 9.0 Runtime)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "   1. Ensure .NET 9.0 Runtime is installed on target machine" -ForegroundColor Yellow
    Write-Host "   2. Set REEF_ENCRYPTION_KEY environment variable" -ForegroundColor Yellow
    Write-Host "   3. Run 'Reef.bat test' to test" -ForegroundColor Yellow
    Write-Host "   4. Run 'Reef.bat install' to install as service" -ForegroundColor Yellow
} else {
    Write-Host "ERROR: Publishing failed" -ForegroundColor Red
}