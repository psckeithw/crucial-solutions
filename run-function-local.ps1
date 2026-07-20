# Run Azure Function with portable .NET 8 SDK
$dotnetDir = "$env:USERPROFILE\.dotnet\8.0"

if (-not (Test-Path "$dotnetDir\dotnet.exe")) {
    Write-Host "✗ .NET 8 SDK not found at: $dotnetDir" -ForegroundColor Red
    Write-Host "Run: .\setup-dotnet8.ps1 first" -ForegroundColor Yellow
    exit 1
}

Write-Host "Using portable .NET 8 SDK: $dotnetDir" -ForegroundColor Green
& "$dotnetDir\dotnet.exe" --version

# Set environment for this session - must be set BEFORE calling func
$env:DOTNET_ROOT = $dotnetDir
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:PATH = "$dotnetDir;$env:PATH"

Write-Host "DOTNET_ROOT: $env:DOTNET_ROOT" -ForegroundColor Gray
Write-Host "Using dotnet from: $(Get-Command dotnet | Select-Object -ExpandProperty Source)" -ForegroundColor Gray

# Navigate to function directory
Set-Location -Path "$PSScriptRoot\src\Function"

Write-Host "`nStarting Azure Function with 'dotnet run'..." -ForegroundColor Cyan
Write-Host "(This is better for .NET Isolated - builds and starts in one step)" -ForegroundColor Gray
dotnet run
