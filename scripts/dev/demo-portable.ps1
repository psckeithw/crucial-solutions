# Demo launcher using portable .NET 8.0 installation
# Works without admin rights - uses local .NET 8.0

$portableDotnet = "C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0"

if (-not (Test-Path "$portableDotnet\dotnet.exe")) {
    Write-Host "ERROR: Portable .NET 8.0 not found at: $portableDotnet" -ForegroundColor Red
    Write-Host "Run: pwsh scripts\bootstrap\dotnet\install-dotnet8-portable.ps1" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== ServiceNow Mockup Demo (Portable Mode) ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "⚠ Using portable .NET 8.0 from: $portableDotnet" -ForegroundColor Yellow
Write-Host ""

# Set environment to use portable .NET
$env:DOTNET_ROOT = $portableDotnet
$env:PATH = "$portableDotnet;$env:PATH"

Write-Host "Step 1: Building and starting function..." -ForegroundColor Green
Write-Host ""

# Navigate to function directory
Push-Location src/Function

try {
    # Build first
    Write-Host "Building..." -ForegroundColor Yellow
    & "$portableDotnet\dotnet.exe" build -c Debug
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✓ Build complete" -ForegroundColor Green
    Write-Host ""
    
    # Start function (this will fail with portable - func requires global install)
    Write-Host "Attempting to start function..." -ForegroundColor Yellow
    Write-Host "Note: This may fail because Azure Functions Core Tools requires global .NET installation" -ForegroundColor Gray
    Write-Host ""
    
    $funcJob = Start-Job -ScriptBlock {
        param($dotnetRoot, $dotnetPath)
        $env:DOTNET_ROOT = $dotnetRoot
        $env:PATH = "$dotnetPath;$env:PATH"
        Set-Location $using:PWD
        func start
    } -ArgumentList $portableDotnet, $portableDotnet
    
    Write-Host "Waiting for function startup (15 seconds)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 15
    
    # Check function status
    $funcOutput = Receive-Job $funcJob.Id
    
    if ($funcOutput -match "Failed to start language worker") {
        Write-Host ""
        Write-Host "✗ Function startup failed (expected with portable installation)" -ForegroundColor Red
        Write-Host ""
        Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Yellow
        Write-Host "Azure Functions Core Tools requires global .NET installation" -ForegroundColor Yellow
        Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "OPTIONS:" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "1. Install .NET 8.0 globally (REQUIRES ADMIN):" -ForegroundColor Green
        Write-Host "   • Download: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Gray
        Write-Host "   • Run the installer as administrator" -ForegroundColor Gray
        Write-Host "   • Then run: pwsh scripts\dev\demo.ps1" -ForegroundColor Gray
        Write-Host ""
        Write-Host "2. Open mockup page manually (NO FUNCTION NEEDED):" -ForegroundColor Green
        Write-Host "   • Use mockup with deployed Azure function endpoint" -ForegroundColor Gray
        Write-Host "   • Edit samples/service-now-mockup.html line ~620" -ForegroundColor Gray
        Write-Host "   • Change API_ENDPOINT to your Azure function URL" -ForegroundColor Gray
        Write-Host "   • Then: Start-Process samples/service-now-mockup.html" -ForegroundColor Gray
        Write-Host ""
        Write-Host "3. Test with scripts\dev\send.ps1 (if function is deployed):" -ForegroundColor Green
        Write-Host "   `$env:API_KEY='your-key'; pwsh scripts\dev\send.ps1 samples/valid-payload.json https://your-function.azurewebsites.net" -ForegroundColor Gray
        Write-Host ""
        
        Stop-Job $funcJob.Id
        Remove-Job $funcJob.Id
        exit 1
    }
    
    # If we get here, function might be running
    Write-Host "✓ Function appears to be running!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Step 2: Opening demo page..." -ForegroundColor Green
    
    Start-Sleep -Seconds 2
    
    $htmlPath = Join-Path $PSScriptRoot "samples\service-now-mockup.html"
    Start-Process $htmlPath
    
    Write-Host ""
    Write-Host "=== Demo Running ===" -ForegroundColor Cyan
    Write-Host "Function endpoint: http://localhost:7071/api/servicenow/userstory" -ForegroundColor Gray
    Write-Host "Demo page: $htmlPath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "To stop:" -ForegroundColor Yellow
    Write-Host "  Stop-Job $($funcJob.Id); Remove-Job $($funcJob.Id)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Showing function output (Ctrl+C to exit):" -ForegroundColor Yellow
    Write-Host ""
    
    while ($true) {
        Start-Sleep -Seconds 5
        Receive-Job $funcJob.Id
    }
    
} finally {
    Pop-Location
}
