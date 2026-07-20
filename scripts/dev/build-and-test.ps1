# Complete testing workflow without local function runtime
# No admin rights required - uses portable .NET 8.0 for build/test

$portableDotnet = "C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0"

Write-Host "=== ServiceNow → ADO Testing (No Admin) ===" -ForegroundColor Cyan
Write-Host ""

# Set environment for portable .NET
$env:DOTNET_ROLL_FORWARD = "Major"
$env:PATH = "$portableDotnet;$env:PATH"

Write-Host "✓ Using portable .NET 8.0" -ForegroundColor Green
Write-Host ""

# Run tests
Write-Host "Step 1: Running unit tests..." -ForegroundColor Yellow
dotnet test tests\ServiceNowToAdo.Tests.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "✗ Tests failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ All tests passed" -ForegroundColor Green
Write-Host ""

# Build function
Write-Host "Step 2: Building function..." -ForegroundColor Yellow
dotnet build src\Function\ServiceNowToAdo.csproj -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "✗ Build failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ Build successful" -ForegroundColor Green
Write-Host ""

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Testing complete! Next steps:" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "OPTION A: Deploy to Azure and test there" -ForegroundColor Green
Write-Host "  1. See docs/DEPLOYMENT.md for deployment instructions" -ForegroundColor Gray
Write-Host "  2. Use pwsh scripts\dev\send.ps1 to test deployed endpoint" -ForegroundColor Gray
Write-Host "  3. Use samples/service-now-mockup.html for UI testing" -ForegroundColor Gray
Write-Host ""

Write-Host "OPTION B: Test with mockup against deployed function" -ForegroundColor Green
Write-Host "  1. Edit samples/service-now-mockup.html (line ~620)" -ForegroundColor Gray
Write-Host "  2. Change API_ENDPOINT to deployed URL" -ForegroundColor Gray
Write-Host "  3. Open in browser and test" -ForegroundColor Gray
Write-Host ""

Write-Host "OPTION C: Request admin access to install .NET 8.0 globally" -ForegroundColor Green
Write-Host "  Then: pwsh scripts\dev\demo.ps1 will work for local testing" -ForegroundColor Gray
Write-Host ""

Write-Host "Build artifacts:" -ForegroundColor Yellow
Write-Host "  src\Function\bin\Release\net8.0\ServiceNowToAdo.dll" -ForegroundColor Gray
Write-Host ""
