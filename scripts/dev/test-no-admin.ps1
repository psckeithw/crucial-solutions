# Test ServiceNow mockup without local function
# For when you don't have .NET 8.0 globally installed

Write-Host "=== ServiceNow Mockup Demo (No Admin Required) ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Since Azure Functions requires global .NET 8.0 installation," -ForegroundColor Yellow
Write-Host "this script opens the mockup for testing against a deployed function." -ForegroundColor Yellow
Write-Host ""

$htmlPath = Join-Path $PSScriptRoot "samples\service-now-mockup.html"

if (-not (Test-Path $htmlPath)) {
    Write-Host "ERROR: Mockup not found: $htmlPath" -ForegroundColor Red
    exit 1
}

Write-Host "OPTIONS FOR TESTING:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Test against deployed Azure function" -ForegroundColor Green
Write-Host "   • Edit samples/service-now-mockup.html line ~620" -ForegroundColor Gray
Write-Host "   • Change API_ENDPOINT to your Azure function URL" -ForegroundColor Gray
Write-Host "   • Example: https://your-app.azurewebsites.net/api/servicenow/userstory" -ForegroundColor Gray
Write-Host ""

Write-Host "2. Deploy function to Azure first" -ForegroundColor Green
Write-Host "   • Requires Azure subscription and deployment" -ForegroundColor Gray
Write-Host "   • See docs/DEPLOYMENT.md for instructions" -ForegroundColor Gray
Write-Host ""

Write-Host "3. Test with scripts\dev\send.ps1 (command-line testing)" -ForegroundColor Green
Write-Host "   • No UI, direct API calls" -ForegroundColor Gray
Write-Host "   • Example: `$env:API_KEY='key'; pwsh scripts\dev\send.ps1 samples/valid-payload.json https://your-function.azurewebsites.net" -ForegroundColor Gray
Write-Host ""

$choice = Read-Host "Open mockup page now? (Y/n)"

if ($choice -eq '' -or $choice -eq 'y' -or $choice -eq 'Y') {
    Write-Host ""
    Write-Host "Opening mockup page..." -ForegroundColor Green
    Write-Host ""
    Write-Host "REMEMBER: Edit the API_ENDPOINT in the HTML file to point to your deployed function!" -ForegroundColor Yellow
    Write-Host ""
    Start-Process $htmlPath
    Write-Host "✓ Mockup opened in browser" -ForegroundColor Green
} else {
    Write-Host "Cancelled." -ForegroundColor Gray
}
