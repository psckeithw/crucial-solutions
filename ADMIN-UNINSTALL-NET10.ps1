# Administrative script to remove .NET 10 
# Run this in an elevated PowerShell session (right-click -> Run as Administrator)

Write-Host "Checking for .NET 10..." -ForegroundColor Cyan

$systemDotnet = "C:\Program Files\dotnet"
if (-not (Test-Path $systemDotnet)) {
    Write-Host "✓ No system .NET installation found" -ForegroundColor Green
    exit 0
}

$installedSdks = & "$systemDotnet\dotnet.exe" --list-sdks
$installedRuntimes = & "$systemDotnet\dotnet.exe" --list-runtimes

Write-Host "`nInstalled SDKs:" -ForegroundColor Yellow
$installedSdks

Write-Host "`nInstalled Runtimes:" -ForegroundColor Yellow
$installedRuntimes

$net10Count = ($installedSdks + $installedRuntimes) | Where-Object { $_ -like "10.*" } | Measure-Object | Select-Object -ExpandProperty Count

if ($net10Count -eq 0) {
    Write-Host "`n✓ No .NET 10 installations found" -ForegroundColor Green
    exit 0
}

Write-Host "`n⚠ Found .NET 10 installation(s)" -ForegroundColor Yellow
Write-Host "This is blocking Azure Functions from using .NET 8`n" -ForegroundColor Yellow

$confirm = Read-Host "Do you want to rename C:\Program Files\dotnet to disable it? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "Cancelled" -ForegroundColor Yellow
    exit 0
}

try {
    Write-Host "`nRenaming C:\Program Files\dotnet -> dotnet.disabled..." -ForegroundColor Cyan
    Rename-Item -Path $systemDotnet -NewName "dotnet.disabled" -Force -ErrorAction Stop
    Write-Host "✓ Successfully disabled system .NET installation" -ForegroundColor Green
    Write-Host "`nTo re-enable later, run:" -ForegroundColor Gray
    Write-Host "  Rename-Item 'C:\Program Files\dotnet.disabled' -NewName 'dotnet'" -ForegroundColor Gray
} catch {
    Write-Host "✗ Failed to rename: $_" -ForegroundColor Red
    Write-Host "`nTry uninstalling .NET 10 from Windows Settings:" -ForegroundColor Yellow
    Write-Host "  Settings -> Apps -> Installed apps -> Search 'Microsoft .NET SDK'" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n✓ Done! Now restart your terminal and try 'func start' again" -ForegroundColor Green
