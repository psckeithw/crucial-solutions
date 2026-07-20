# Quick test launcher for ServiceNow mockup demo
# Requires: .NET 8.0 SDK installed (currently blocked - see below)

Write-Host "=== ServiceNow Mockup Demo Launcher ===" -ForegroundColor Cyan
Write-Host ""

# Check if .NET 8.0 is available
$dotnetVersion = dotnet --list-runtimes | Select-String "Microsoft.NETCore.App 8.0"
if (-not $dotnetVersion) {
    Write-Host "ERROR: .NET 8.0 runtime not found" -ForegroundColor Red
    Write-Host ""
    Write-Host "This function requires .NET 8.0 to run locally." -ForegroundColor Yellow
    Write-Host "You currently have .NET 10.0.9 installed." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To fix:" -ForegroundColor Cyan
    Write-Host "  1. Download .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host "  2. Or run: winget install Microsoft.DotNet.SDK.8"
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

Write-Host "Step 1: Starting Azure Function..." -ForegroundColor Green

# Start function in background
$funcJob = Start-Job -ScriptBlock {
    Set-Location $using:PWD
    cd src/Function
    func start
}

Write-Host "Waiting for function to start (10 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Check if function is responding
try {
    $health = Invoke-WebRequest -Uri "http://localhost:7071/api/health" -UseBasicParsing -TimeoutSec 5
    Write-Host "✓ Function is running!" -ForegroundColor Green
} catch {
    Write-Host "⚠ Function may not be ready yet" -ForegroundColor Yellow
    Write-Host "Check the background job output with: Receive-Job $($funcJob.Id)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Step 2: Opening demo page..." -ForegroundColor Green

# Get the full path to the HTML file
$htmlPath = Join-Path $PSScriptRoot "samples\service-now-mockup.html"

# Open in default browser
Start-Process $htmlPath

Write-Host ""
Write-Host "=== Demo Running ===" -ForegroundColor Cyan
Write-Host "Function endpoint: http://localhost:7071/api/servicenow/userstory" -ForegroundColor Gray
Write-Host "Demo page: $htmlPath" -ForegroundColor Gray
Write-Host ""
Write-Host "To stop the function:" -ForegroundColor Yellow
Write-Host "  Stop-Job $($funcJob.Id); Remove-Job $($funcJob.Id)"
Write-Host ""
Write-Host "Press Ctrl+C or close this window when done."

# Keep script running and show function output
while ($true) {
    Start-Sleep -Seconds 5
    Receive-Job $funcJob.Id
}
