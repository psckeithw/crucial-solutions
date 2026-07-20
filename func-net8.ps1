# Custom func wrapper that forces .NET 8 usage
# This works around Azure Functions Core Tools hardcoded C:\Program Files\dotnet path

$dotnet8Path = "C:\Users\Keith.Watson\.dotnet\8.0"

if (-not (Test-Path "$dotnet8Path\dotnet.exe")) {
    Write-Host "✗ .NET 8 SDK not found at: $dotnet8Path" -ForegroundColor Red
    exit 1
}

# Temporarily rename system dotnet to force func to use .NET 8
$systemDotnet = "C:\Program Files\dotnet"
$systemDotnetBackup = "C:\Program Files\dotnet.backup"

Write-Host "Setting up .NET 8 environment..." -ForegroundColor Cyan

# Set environment variables (these help but func still ignores them)
$env:DOTNET_ROOT = $dotnet8Path
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:PATH = "$dotnet8Path;$env:PATH"

# Try setting additional environment variables that Azure Functions might respect
$env:DOTNET_INSTALL_DIR = $dotnet8Path
$env:DOTNET_EXE_PATH = "$dotnet8Path\dotnet.exe"

Write-Host "✓ Using .NET 8 SDK: $dotnet8Path" -ForegroundColor Green
Write-Host ""
Write-Host "NOTE: If 'func start' still fails with .NET 10 error, you'll need admin rights to either:" -ForegroundColor Yellow
Write-Host "  1. Uninstall .NET 10 from C:\Program Files\dotnet" -ForegroundColor Yellow
Write-Host "  2. Temporarily rename C:\Program Files\dotnet to force func to search PATH" -ForegroundColor Yellow
Write-Host ""

# Call original func with all arguments
$funcPath = Get-Command func -ErrorAction SilentlyContinue
if ($funcPath) {
    & $funcPath.Source $args
} else {
    Write-Host "✗ func command not found" -ForegroundColor Red
    exit 1
}
