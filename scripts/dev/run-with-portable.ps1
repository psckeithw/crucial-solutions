# Force Azure Functions to use portable .NET 8.0
# Uses environment variables to override default location

$portableDotnet = "C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0"

if (-not (Test-Path "$portableDotnet\dotnet.exe")) {
    Write-Host "ERROR: Portable .NET 8.0 not found" -ForegroundColor Red
    exit 1
}

Write-Host "=== Azure Function with Portable .NET 8.0 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Using portable .NET 8.0 from:" -ForegroundColor Yellow
Write-Host "  $portableDotnet" -ForegroundColor Gray
Write-Host ""

# Verify version
Write-Host "Verifying .NET installation..." -ForegroundColor Yellow
& "$portableDotnet\dotnet.exe" --version
Write-Host ""

# Set environment variables to force func to use portable .NET
$env:DOTNET_ROOT = $portableDotnet
$env:PATH = "$portableDotnet;$env:PATH"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"  # Disable global .NET lookup

Write-Host "Step 1: Building function..." -ForegroundColor Green
Push-Location src/Function

try {
    # Build using portable .NET
    & "$portableDotnet\dotnet.exe" build -c Debug
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✓ Build complete" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "Step 2: Starting function..." -ForegroundColor Green
    Write-Host ""
    Write-Host "Environment:" -ForegroundColor Gray
    Write-Host "  DOTNET_ROOT: $env:DOTNET_ROOT" -ForegroundColor Gray
    Write-Host "  DOTNET_MULTILEVEL_LOOKUP: $env:DOTNET_MULTILEVEL_LOOKUP" -ForegroundColor Gray
    Write-Host ""
    
    # Try to start function with portable .NET
    func start
    
} finally {
    Pop-Location
}
