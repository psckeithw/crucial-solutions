# Open ServiceNow Mockup Demo Page
# Use this to test against a deployed Azure Function endpoint

$htmlPath = Join-Path $PSScriptRoot "samples\service-now-mockup.html"

if (-not (Test-Path $htmlPath)) {
    Write-Host "ERROR: Demo page not found: $htmlPath" -ForegroundColor Red
    exit 1
}

Write-Host "=== ServiceNow Mockup Demo ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Opening demo page: $htmlPath" -ForegroundColor Green
Write-Host ""
Write-Host "CONFIGURATION:" -ForegroundColor Yellow
Write-Host ""
Write-Host "By default, the mockup sends requests to: http://localhost:7071/api/servicenow/userstory"
Write-Host ""
Write-Host "To test against a deployed Azure function:" -ForegroundColor Cyan
Write-Host "  1. Edit: $htmlPath" -ForegroundColor Gray
Write-Host "  2. Find line ~620: const API_ENDPOINT = ..." -ForegroundColor Gray
Write-Host "  3. Change to your Azure function URL:" -ForegroundColor Gray
Write-Host "     https://your-function-app.azurewebsites.net/api/servicenow/userstory" -ForegroundColor Gray
Write-Host ""
Write-Host "To run function locally (requires global .NET 8.0):" -ForegroundColor Cyan
Write-Host "  1. Install .NET 8.0 SDK globally (needs admin)" -ForegroundColor Gray
Write-Host "  2. Run: pwsh scripts\dev\demo.ps1" -ForegroundColor Gray
Write-Host ""

Start-Process $htmlPath

Write-Host "✓ Demo page opened in browser" -ForegroundColor Green
